using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Notifications;

namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosNotificationRepository : INotificationRepository
{
    private readonly Container _container;

    public CosmosNotificationRepository(CosmosClient client, IOptions<CosmosOptions> options)
    {
        var settings = options.Value;
        _container = client.GetContainer(settings.DatabaseName, settings.NotificationsContainerName);
    }

    public async Task<bool> TryCreateAsync(WatchNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            // Create (not upsert): the database itself refuses a second notice with the same Id.
            await _container.CreateItemAsync(notification, new PartitionKey(notification.WatchId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<WatchNotification?> GetAsync(string id, string watchId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<WatchNotification>(id, new PartitionKey(watchId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<WatchNotification>> ListRecentAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        // Across all watches (partitions) - fine for one user's handful of notices.
        using var iterator = _container.GetItemLinqQueryable<WatchNotification>()
            .OrderByDescending(n => n.CreatedAt)
            .Take(maxCount)
            .ToFeedIterator();

        var results = new List<WatchNotification>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return results;
    }

    public Task DeleteAllAsync(string watchId, CancellationToken cancellationToken = default) =>
        CosmosPartition.DeleteAllAsync(_container, watchId, cancellationToken);
}
