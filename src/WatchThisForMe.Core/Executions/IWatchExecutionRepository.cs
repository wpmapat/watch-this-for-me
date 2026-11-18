namespace WatchThisForMe.Core.Executions;

public interface IWatchExecutionRepository
{
    Task<WatchExecution> CreateAsync(WatchExecution execution, CancellationToken cancellationToken = default);

    Task<WatchExecution> UpdateAsync(WatchExecution execution, CancellationToken cancellationToken = default);

    // Most recent first.
    Task<IReadOnlyList<WatchExecution>> ListAsync(string watchId, int? maxCount = null, CancellationToken cancellationToken = default);

    // Removes every run record of a watch - used when the watch itself is deleted.
    Task DeleteAllAsync(string watchId, CancellationToken cancellationToken = default);
}
