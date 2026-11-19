namespace WatchThisForMe.Core.Notifications;

public interface INotificationRepository
{
    // Saves the notice unless one with the same Id already exists; returns false in that case.
    Task<bool> TryCreateAsync(WatchNotification notification, CancellationToken cancellationToken = default);

    Task<WatchNotification?> GetAsync(string id, string watchId, CancellationToken cancellationToken = default);

    // Newest first, across all watches.
    Task<IReadOnlyList<WatchNotification>> ListRecentAsync(int maxCount, CancellationToken cancellationToken = default);

    // Removes every notice of a watch - used when the watch itself is deleted.
    Task DeleteAllAsync(string watchId, CancellationToken cancellationToken = default);
}
