using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Monitoring;

public class WatchCheckService(
    IWatchRepository watchRepository,
    IObservationRepository observationRepository,
    ISourceFetcher sourceFetcher)
{
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
            Changed = true
        };

        return await observationRepository.CreateAsync(observation, cancellationToken);
    }
}
