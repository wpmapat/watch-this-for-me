using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Diffing;

namespace WatchThisForMe.Infrastructure.Monitoring;

public class WatchCheckService(
    IWatchRepository watchRepository,
    IObservationRepository observationRepository,
    ISourceFetcher sourceFetcher,
    IChangeClassifier changeClassifier)
{
    private const int MaxObservationsPerWatch = 10;

    public async Task<Observation?> CheckAsync(string watchId, CancellationToken cancellationToken = default)
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
            var classification = await changeClassifier.ClassifyAsync(observation.DiffSummary, watch.MeaningfulChangeCriteria, cancellationToken);
            observation.Meaningful = classification.Meaningful;
            observation.Category = classification.Category;
            observation.Confidence = classification.Confidence;
            observation.ClassificationSummary = classification.Summary;
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
