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
            var result = await RunCheckAsync(watch, watchId, cancellationToken);

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
            await executionRepository.UpdateAsync(execution, cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            Telemetry.CheckFailures.Add(1, new KeyValuePair<string, object?>("watch.id", watchId));

            if (execution.AttemptNumber >= DeadLetterThreshold)
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
    // "give it a fresh set of attempts", not "stop it again on the next hiccup".
    private static int CountConsecutiveFailures(IReadOnlyList<WatchExecution> recentExecutionsMostRecentFirst, DateTimeOffset? resumedAt)
    {
        var count = 0;
        foreach (var execution in recentExecutionsMostRecentFirst)
        {
            if (execution.Status != WatchExecutionStatus.Failed || execution.StartedAt < resumedAt)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private async Task<Observation?> RunCheckAsync(Watch watch, string watchId, CancellationToken cancellationToken)
    {
        var snapshot = await TracedStepAsync("fetch", () => sourceFetcher.FetchAsync(watch.Source, cancellationToken));
        var previous = await observationRepository.GetLatestAsync(watchId, cancellationToken);
        var changed = previous is null || previous.ContentHash != snapshot.ContentHash;

        watch.LastCheckedAt = DateTimeOffset.UtcNow;
        watch.RedirectedTo = snapshot.RedirectedTo;
        await watchRepository.UpdateAsync(watch, cancellationToken);

        if (!changed)
        {
            // Same content as last time, so the AI's verdict on it (incl. "page unavailable") stands.
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

        if (!string.IsNullOrEmpty(observation.DiffSummary))
        {
            // Query embedding is of the raw diff, not a classification summary - we don't have
            // this change's own summary yet, that's the whole point of asking "have I seen
            // something like this before" ahead of classifying it.
            var queryEmbedding = await TracedStepAsync("embed_diff", () => embeddingGenerator.GenerateAsync(observation.DiffSummary, cancellationToken));
            var history = await observationRepository.ListAsync(watchId, MaxObservationsPerWatch, cancellationToken);
            var similarPast = EmbeddingSimilarity.FindTopMatches(queryEmbedding, history, MaxSimilarPastMatches);
            var interestProfile = await interestProfileRepository.GetAsync(cancellationToken);

            var classification = await TracedStepAsync("classify", () => changeClassifier.ClassifyAsync(
                observation.DiffSummary, watch.MeaningfulChangeCriteria, similarPast, interestProfile.Interests, cancellationToken));
            observation.Category = classification.Category;
            // An error/"not found" page is never news in itself, whatever the model says.
            observation.Meaningful = classification.Meaningful && classification.Category != ChangeCategories.PageUnavailable;
            observation.Confidence = classification.Confidence;
            observation.ClassificationSummary = classification.Summary;

            // Embed the summary, not the raw diff - it's the compact, semantic statement of
            // what happened, which is what the similarity search matches against.
            observation.Embedding = await TracedStepAsync("embed_summary", () => embeddingGenerator.GenerateAsync(classification.Summary, cancellationToken));
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

        return created;
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
