using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Monitoring;

public class WatchCheckService(
    IWatchRepository watchRepository,
    IObservationRepository observationRepository,
    ISourceFetcher sourceFetcher)
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
            Changed = true
        };

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
