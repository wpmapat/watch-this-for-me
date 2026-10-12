namespace WatchThisForMe.Core.Watches;

public interface IWatchRepository
{
    Task<Watch> CreateAsync(Watch watch, CancellationToken cancellationToken = default);

    Task<Watch?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Watch>> ListAsync(CancellationToken cancellationToken = default);

    Task<Watch> UpdateAsync(Watch watch, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
