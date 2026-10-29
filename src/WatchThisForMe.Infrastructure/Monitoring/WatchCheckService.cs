using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Preferences;
using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Diffing;
using WatchThisForMe.Infrastructure.Embeddings;

namespace WatchThisForMe.Infrastructure.Monitoring;

public class WatchCheckService(
    IWatchRepository watchRepository,
    IObservationRepository observationRepository,
    ISourceFetcher sourceFetcher,
    IChangeClassifier changeClassifier,
    IEmbeddingGenerator embeddingGenerator,
    IInterestProfileRepository interestProfileRepository)
{
    private const int MaxObservationsPerWatch = 10;
    private const int MaxSimilarPastMatches = 3;

    // Per-watch lock so a retried or overlapping call (e.g. a client retry after a timeout
    // racing the original still-running request, or a manual check overlapping the scheduler's
    // own check of the same watch) can't both see "unchanged" and create two observations for
    // the same real-world change. Only guards a single process - a multi-instance deployment
    // would need a distributed lock instead, but this app only ever runs as one instance today.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _watchLocks = new();

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

        var snapshot = await sourceFetcher.FetchAsync(watch.Source, cancellationToken);
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
                : TextDiffer.Summarize(previous.NormalizedContent, snapshot.NormalizedContent)
        };

        if (!string.IsNullOrEmpty(observation.DiffSummary))
        {
            // Query embedding is of the raw diff, not a classification summary - we don't have
            // this change's own summary yet, that's the whole point of asking "have I seen
            // something like this before" ahead of classifying it.
            var queryEmbedding = await embeddingGenerator.GenerateAsync(observation.DiffSummary, cancellationToken);
            var history = await observationRepository.ListAsync(watchId, MaxObservationsPerWatch, cancellationToken);
            var similarPast = EmbeddingSimilarity.FindTopMatches(queryEmbedding, history, MaxSimilarPastMatches);
            var interestProfile = await interestProfileRepository.GetAsync(cancellationToken);

            var classification = await changeClassifier.ClassifyAsync(
                observation.DiffSummary, watch.MeaningfulChangeCriteria, similarPast, interestProfile.Interests, cancellationToken);
            observation.Meaningful = classification.Meaningful;
            observation.Category = classification.Category;
            observation.Confidence = classification.Confidence;
            observation.ClassificationSummary = classification.Summary;

            // Embed the summary, not the raw diff - it's the compact, semantic statement of
            // what happened, which is what tomorrow's similarity search should match against.
            observation.Embedding = await embeddingGenerator.GenerateAsync(classification.Summary, cancellationToken);
        }

        var created = await observationRepository.CreateAsync(observation, cancellationToken);
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
}
