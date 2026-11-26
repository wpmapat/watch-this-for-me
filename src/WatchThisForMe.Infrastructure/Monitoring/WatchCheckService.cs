using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Preferences;
using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Diffing;
using WatchThisForMe.Infrastructure.Embeddings;
using WatchThisForMe.Infrastructure.Notifications;
using WatchThisForMe.Infrastructure.Observability;
using WatchThisForMe.Infrastructure.Sources;

namespace WatchThisForMe.Infrastructure.Monitoring;

public class WatchCheckService(
    IWatchRepository watchRepository,
    IObservationRepository observationRepository,
    ISourceFetcher sourceFetcher,
    IChangeClassifier changeClassifier,
    IEmbeddingGenerator embeddingGenerator,
    IInterestProfileRepository interestProfileRepository,
    IWatchExecutionRepository executionRepository,
    NotificationService notificationService,
    ILogger<WatchCheckService> logger)
{
    private const int MaxObservationsPerWatch = 10;
    private const int MaxSimilarPastMatches = 3;
    private const int RecentExecutionsToInspect = 5;

    // Consecutive failed runs before a watch is dead-lettered (Status -> DeadLettered) and
    // stops being scheduled automatically - a permanently broken source (dead URL, revoked
    // token) shouldn't be silently retried forever.
    private const int DeadLetterThreshold = 3;

    // Per-watch lock so a retried or overlapping call (e.g. a client retry after a timeout
    // racing the original still-running request, or a manual check overlapping the scheduler's
    // own check of the same watch) can't both see "unchanged" and create two observations for
    // the same real-world change. Only guards a single process - a multi-instance deployment
    // would need a distributed lock instead, but this app only ever runs as one instance today.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _watchLocks = new();

    public async Task<Observation?> CheckAsync(string watchId, CancellationToken cancellationToken = default)
    {
        var watchLock = _watchLocks.GetOrAdd(watchId, _ => new SemaphoreSlim(1, 1));
        await watchLock.WaitAsync(cancellationToken);
        try
        {
            return await CheckInternalAsync(watchId, cancellationToken);
        }
        finally
        {
            watchLock.Release();
        }
    }

    private async Task<Observation?> CheckInternalAsync(string watchId, CancellationToken cancellationToken)
    {
        var watch = await watchRepository.GetAsync(watchId, cancellationToken);
        if (watch is null)
        {
            return null;
        }

        var recentExecutions = await executionRepository.ListAsync(watchId, RecentExecutionsToInspect, cancellationToken);
        var consecutiveFailures = CountConsecutiveFailures(recentExecutions, watch.ResumedAt);

        var execution = new WatchExecution
        {
            Id = Guid.NewGuid().ToString(),
            WatchId = watchId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = consecutiveFailures > 0 ? WatchExecutionStatus.Retrying : WatchExecutionStatus.Started,
            AttemptNumber = consecutiveFailures + 1
        };
        await executionRepository.CreateAsync(execution, cancellationToken);

        // Record the attempt before anything can fail, so the scheduler waits a full
        // CheckFrequency before retrying instead of retrying on every tick.
        watch.LastAttemptAt = execution.StartedAt;
        await watchRepository.UpdateAsync(watch, cancellationToken);

        // Root span for the whole run - every step below (fetch/classify/embed/save) becomes
        // a child span nested under this one, so a single trace shows the full pipeline.
        using var activity = Telemetry.ActivitySource.StartActivity("WatchCheck");
        activity?.SetTag("watch.id", watchId);
        activity?.SetTag("execution.id", execution.Id);
        activity?.SetTag("execution.attempt_number", execution.AttemptNumber);

        try
        {
            var result = await RunCheckAsync(watch, watchId, execution.Steps, cancellationToken);

            execution.Status = WatchExecutionStatus.Succeeded;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            await executionRepository.UpdateAsync(execution, cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            execution.Status = WatchExecutionStatus.Failed;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.ErrorMessage = ex.Message;
            execution.AiUnavailable = ex is AiUnavailableException;
            await executionRepository.UpdateAsync(execution, cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            Telemetry.CheckFailures.Add(1, new KeyValuePair<string, object?>("watch.id", watchId));

            // Only website failures stop a watch: an AI outage says nothing about the page.
            if (!execution.AiUnavailable && execution.AttemptNumber >= DeadLetterThreshold)
            {
                watch.Status = WatchStatus.DeadLettered;
                await watchRepository.UpdateAsync(watch, cancellationToken);
                logger.LogWarning(
                    "Watch {WatchId} dead-lettered after {AttemptNumber} consecutive failed runs. Last error: {Error}",
                    watchId, execution.AttemptNumber, ex.Message);
            }

            throw;
        }
    }

    // Failures from before the user last resumed the watch don't count - resuming means
    // "give it a fresh set of attempts", not "stop it again on the next hiccup". An AI failure
    // ends the streak too: on that run the website DID answer.
    private static int CountConsecutiveFailures(IReadOnlyList<WatchExecution> recentExecutionsMostRecentFirst, DateTimeOffset? resumedAt)
    {
        var count = 0;
        foreach (var execution in recentExecutionsMostRecentFirst)
        {
            if (execution.Status != WatchExecutionStatus.Failed || execution.AiUnavailable || execution.StartedAt < resumedAt)
            {
                break;
            }

            count++;
        }

        return count;
    }

    // Each step a person would care about is added to `steps`, which the history page shows.
    private async Task<Observation?> RunCheckAsync(Watch watch, string watchId, List<ExecutionStep> steps, CancellationToken cancellationToken)
    {
        var snapshot = await RecordStepAsync(steps, "Page fetch",
            () => TracedStepAsync("fetch", () => sourceFetcher.FetchAsync(watch.Source, cancellationToken)),
            DescribeFetch);

        var compareStartedAt = DateTimeOffset.UtcNow;
        var previous = await observationRepository.GetLatestAsync(watchId, cancellationToken);
        var changed = previous is null || previous.ContentHash != snapshot.ContentHash;

        watch.LastCheckedAt = DateTimeOffset.UtcNow;
        watch.RedirectedTo = snapshot.RedirectedTo;
        await watchRepository.UpdateAsync(watch, cancellationToken);

        if (!changed)
        {
            // Same content as last time, so the AI's verdict on it (incl. "page unavailable") stands.
            // A notice that failed last time is retried even though nothing new happened.
            steps.Add(ExecutionStep.EndingNow("Comparison", compareStartedAt, StepOutcome.Ok, "No change since the last check."));
            await notificationService.NotifyPendingAsync(watch, steps, cancellationToken);
            return previous;
        }

        var compareWith = await FindContentToCompareWithAsync(watch, previous, snapshot, cancellationToken);

        var observation = new Observation
        {
            Id = Guid.NewGuid().ToString(),
            WatchId = watchId,
            CheckedAt = DateTimeOffset.UtcNow,
            ContentHash = snapshot.ContentHash,
            NormalizedContent = snapshot.NormalizedContent,
            ContentFormatVersion = CurrentContentFormatVersion,
            Changed = true,
            DiffSummary = compareWith is null
                ? null
                : TracedStep("diff", () => TextDiffer.Summarize(compareWith, snapshot.NormalizedContent))
        };
        steps.Add(ExecutionStep.EndingNow("Comparison", compareStartedAt, StepOutcome.Ok, DescribeComparison(previous, observation.DiffSummary)));

        if (!string.IsNullOrEmpty(observation.DiffSummary))
        {
            // One step on the history page for all three AI calls - the separate timings are in the traces.
            await RecordStepAsync(steps, "AI analysis", async () =>
            {
                // Query embedding is of the raw diff, not a classification summary - we don't have
                // this change's own summary yet, that's the whole point of asking "have I seen
                // something like this before" ahead of classifying it.
                var queryEmbedding = await TracedStepAsync("embed_diff", () => AiStepAsync(() => embeddingGenerator.GenerateAsync(observation.DiffSummary, cancellationToken), cancellationToken));
                var history = await observationRepository.ListAsync(watchId, MaxObservationsPerWatch, cancellationToken);
                var similarPast = EmbeddingSimilarity.FindTopMatches(queryEmbedding, history, MaxSimilarPastMatches);
                var interestProfile = await interestProfileRepository.GetAsync(cancellationToken);

                var classification = await TracedStepAsync("classify", () => AiStepAsync(() => changeClassifier.ClassifyAsync(
                    observation.DiffSummary, watch.MeaningfulChangeCriteria, similarPast, interestProfile.Interests, cancellationToken), cancellationToken));
                observation.Category = classification.Category;
                // An error/"not found" page is never news in itself, whatever the model says.
                observation.Meaningful = classification.Meaningful && classification.Category != ChangeCategories.PageUnavailable;
                observation.Confidence = classification.Confidence;
                observation.ClassificationSummary = classification.Summary;

                // Embed the summary, not the raw diff - it's the compact, semantic statement of
                // what happened, which is what the similarity search matches against.
                observation.Embedding = await TracedStepAsync("embed_summary", () => AiStepAsync(() => embeddingGenerator.GenerateAsync(classification.Summary, cancellationToken), cancellationToken));
                return observation;
            }, DescribeVerdict);
        }

        // Decided now and stored with the change, so a failure later can't make us forget it.
        observation.NotificationPending = NotificationService.ShouldNotify(watch, observation);
        if (observation.NotificationPending != true && observation.Meaningful is not null && observation.Category != ChangeCategories.PageUnavailable)
        {
            steps.Add(ExecutionStep.EndingNow("Notice", DateTimeOffset.UtcNow, StepOutcome.Ok,
                "No notice: this change doesn't match what you asked to hear about."));
        }

        var created = await TracedStepAsync("save", () => observationRepository.CreateAsync(observation, cancellationToken));
        await PruneOldObservationsAsync(watchId, cancellationToken);

        // The content changed, so the previous "page unavailable" verdict no longer applies -
        // replace it with this one's (null unless the AI says this content is broken too).
        var unavailableReason = observation.Category == ChangeCategories.PageUnavailable ? observation.ClassificationSummary : null;
        if (watch.PageUnavailableReason != unavailableReason)
        {
            watch.PageUnavailableReason = unavailableReason;
            await watchRepository.UpdateAsync(watch, cancellationToken);
        }

        // This change's notice, plus any earlier one that failed and is still owed.
        await TracedStepAsync("notify", async () => { await notificationService.NotifyPendingAsync(watch, steps, cancellationToken); return 0; });

        return created;
    }

    // Runs one step of the run's story and adds it to the story - with the error if it fails,
    // so the history shows exactly where a failed run stopped.
    private static async Task<T> RecordStepAsync<T>(
        List<ExecutionStep> steps, string name, Func<Task<T>> step, Func<T, (StepOutcome Outcome, string Detail)> describe)
    {
        var startedAt = DateTimeOffset.UtcNow;
        T result;
        try
        {
            result = await step();
        }
        catch (Exception ex)
        {
            steps.Add(ExecutionStep.EndingNow(name, startedAt, StepOutcome.Failed, ex.Message));
            throw;
        }

        var (outcome, detail) = describe(result);
        steps.Add(ExecutionStep.EndingNow(name, startedAt, outcome, detail));
        return result;
    }

    private static (StepOutcome, string) DescribeFetch(SourceSnapshot snapshot) => (StepOutcome.Ok,
        $"Read {snapshot.NormalizedContent.Length:N0} characters of text."
        + (snapshot.RedirectedTo is null ? "" : $" The address now redirects to {snapshot.RedirectedTo}."));

    private static string DescribeComparison(Observation? previous, string? diffSummary)
    {
        if (previous is null)
        {
            return "First look at this page - saved as the starting point for later checks.";
        }

        if (diffSummary is null)
        {
            return "Saved as a new starting point (the address changed, or the old copy can't be compared).";
        }

        var lines = diffSummary.Split('\n');
        var added = lines.Count(l => l.StartsWith("+ ", StringComparison.Ordinal));
        var removed = lines.Count(l => l.StartsWith("- ", StringComparison.Ordinal));
        if (added + removed == 0)
        {
            return "The page's text differs, but no line changed in a way that counts - nothing to analyse.";
        }

        var more = diffSummary.Contains("more changed lines omitted", StringComparison.Ordinal) ? ", and more" : "";
        return $"The page changed: {Lines(added)} added, {Lines(removed)} removed{more}.";

        static string Lines(int n) => n == 1 ? "1 line" : $"{n} lines";
    }

    private static (StepOutcome, string) DescribeVerdict(Observation judged)
    {
        var category = judged.Category?.Replace('_', ' ') ?? "uncategorised";
        var sure = judged.Confidence is { } confidence ? $", {Math.Round(confidence * 100)}% sure" : "";

        if (judged.Category == ChangeCategories.PageUnavailable)
        {
            return (StepOutcome.Warning, $"Looks like an error or unavailable page, not a real change. {judged.ClassificationSummary}");
        }

        return judged.Meaningful == true
            ? (StepOutcome.Highlight, $"Meaningful - {category}{sure}. {judged.ClassificationSummary}")
            : (StepOutcome.Ok, $"Not important - {category}{sure}. {judged.ClassificationSummary}");
    }

    // The earlier page text the new content should be diffed against, or null to save it as a
    // fresh baseline without asking the AI anything.
    private async Task<string?> FindContentToCompareWithAsync(
        Watch watch, Observation? previous, SourceSnapshot snapshot, CancellationToken cancellationToken)
    {
        // previous.NormalizedContent can be null for an old/corrupted observation row even
        // though the model declares it non-nullable - `required` only checked at write time.
        if (previous is null || string.IsNullOrEmpty(previous.NormalizedContent))
        {
            return null;
        }

        // The user pointed the watch at a new address: a different page, not a change to this one.
        if (watch.SourceChangedAt is { } addressChangedAt && previous.CheckedAt < addressChangedAt)
        {
            return null;
        }

        var compareWith = previous;

        // Last time we saw an error/"not found"/maintenance page. Compare the new content with
        // the last REAL page instead - otherwise, when the page comes back, everything on it
        // (old releases included) would look new.
        if (previous.Category == ChangeCategories.PageUnavailable)
        {
            var history = await observationRepository.ListAsync(watch.Id, MaxObservationsPerWatch, cancellationToken);
            compareWith = history
                .Where(o => o.Category != ChangeCategories.PageUnavailable && !string.IsNullOrEmpty(o.NormalizedContent))
                .Where(o => watch.SourceChangedAt is null || o.CheckedAt >= watch.SourceChangedAt)
                .MaxBy(o => o.CheckedAt) ?? previous;
        }

        return ContentInCurrentFormat(compareWith, snapshot);
    }

    // Bump when a change to the page cleanup (HtmlNormalizer, VolatileContentNormalizer) would
    // make old snapshots look different from new ones even though the page itself didn't change -
    // and teach ContentInCurrentFormat how to bring the previous version up to date.
    private const int CurrentContentFormatVersion = 3;

    // A stored snapshot's text as the current cleanup would have produced it, so the diff shows
    // only real page changes; null when that's impossible and a fresh baseline is needed.
    private static string? ContentInCurrentFormat(Observation stored, SourceSnapshot current) => stored.ContentFormatVersion switch
    {
        CurrentContentFormatVersion => stored.NormalizedContent,

        // v3 (Nov 18 2026) only added masking of random ids, which can be re-applied to v2 text.
        2 => VolatileContentNormalizer.Normalize(stored.NormalizedContent),

        // v1 (no version, before Nov 18 2026) flattened the whole page onto one line, which can't
        // be compared line by line with the new format. A page that is still a single line is
        // unaffected, so it's compared as usual.
        _ when !current.NormalizedContent.Contains('\n') => stored.NormalizedContent,
        _ => null
    };

    private async Task PruneOldObservationsAsync(string watchId, CancellationToken cancellationToken)
    {
        var all = await observationRepository.ListAsync(watchId, cancellationToken: cancellationToken);

        foreach (var stale in all.Skip(MaxObservationsPerWatch))
        {
            await observationRepository.DeleteAsync(stale.Id, watchId, cancellationToken);
        }
    }

    // Any failure inside an AI call is the AI's, whatever its type (network error, timeout, a
    // reply we couldn't parse) - except our own cancellation (shutdown), which isn't a failure.
    private static async Task<T> AiStepAsync<T>(Func<Task<T>> step, CancellationToken cancellationToken)
    {
        try
        {
            return await step();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw AiUnavailableException.From(ex);
        }
    }

    // Wraps one pipeline step in both a trace span (for inspecting a single run) and a
    // duration metric (for aggregate latency across many runs) - the two serve different
    // observability questions, so both are recorded rather than picking just one.
    private static async Task<T> TracedStepAsync<T>(string stepName, Func<Task<T>> step)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(stepName);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await step();
        }
        finally
        {
            Telemetry.StepDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("step", stepName));
        }
    }

    private static T TracedStep<T>(string stepName, Func<T> step)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(stepName);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return step();
        }
        finally
        {
            Telemetry.StepDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("step", stepName));
        }
    }
}
