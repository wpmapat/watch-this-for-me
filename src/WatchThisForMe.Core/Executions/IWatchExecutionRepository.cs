namespace WatchThisForMe.Core.Executions;

public interface IWatchExecutionRepository
{
    Task<WatchExecution> CreateAsync(WatchExecution execution, CancellationToken cancellationToken = default);

    Task<WatchExecution> UpdateAsync(WatchExecution execution, CancellationToken cancellationToken = default);

    // Most recent first.
    Task<IReadOnlyList<WatchExecution>> ListAsync(string watchId, int? maxCount = null, CancellationToken cancellationToken = default);
}
