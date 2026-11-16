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
        var consecutiveFailures = CountConsecutiveFailures(recentExecutions);

        var execution = new WatchExecution
        {
            Id = Guid.NewGuid().ToString(),
            WatchId = watchId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = consecutiveFailures > 0 ? WatchExecutionStatus.Retrying : WatchExecutionStatus.Started,
            AttemptNumber = consecutiveFailures + 1
        };
        await executionRepository.CreateAsync(execution, cancellationToken);

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

    private static int CountConsecutiveFailures(IReadOnlyList<WatchExecution> recentExecutionsMostRecentFirst)
    {
        var count = 0;
        foreach (var execution in recentExecutionsMostRecentFirst)
        {
            if (execution.Status != WatchExecutionStatus.Failed)
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
        await watchRepository.UpdateAsync(watch, cancellationToken);

        if (!changed)
        {
            return previous;
        }

        var observation = new Observation
        {
            Id = Guid.NewGuid().ToString(),
            WatchId = watchId,
            CheckedAt = DateTimeOffset.UtcNow,
            ContentHash = snapshot.ContentHash,
            NormalizedContent = snapshot.NormalizedContent,
            Changed = true,
            // previous.NormalizedContent can be null for an old/corrupted observation row even
            // though the model declares it non-nullable - `required` only checked at write time.
            DiffSummary = string.IsNullOrEmpty(previous?.NormalizedContent)
                ? null
                : TracedStep("diff", () => TextDiffer.Summarize(previous.NormalizedContent, snapshot.NormalizedContent))
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
            observation.Meaningful = classification.Meaningful;
            observation.Category = classification.Category;
            observation.Confidence = classification.Confidence;
            observation.ClassificationSummary = classification.Summary;

            // Embed the summary, not the raw diff - it's the compact, semantic statement of
            // what happened, which is what the similarity search matches against.
            observation.Embedding = await TracedStepAsync("embed_summary", () => embeddingGenerator.GenerateAsync(classification.Summary, cancellationToken));
        }

        var created = await TracedStepAsync("save", () => observationRepository.CreateAsync(observation, cancellationToken));
        await PruneOldObservationsAsync(watchId, cancellationToken);

        return created;
    }

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
