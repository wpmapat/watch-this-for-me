namespace WatchThisForMe.Core.Observations;

public interface IObservationRepository
{
    Task<Observation> CreateAsync(Observation observation, CancellationToken cancellationToken = default);

    Task<Observation?> GetLatestAsync(string watchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Observation>> ListAsync(string watchId, int? maxCount = null, CancellationToken cancellationToken = default);

    Task<Observation> UpdateAsync(Observation observation, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, string watchId, CancellationToken cancellationToken = default);

    // Removes every snapshot of a watch - used when the watch itself is deleted.
    Task DeleteAllAsync(string watchId, CancellationToken cancellationToken = default);
}
