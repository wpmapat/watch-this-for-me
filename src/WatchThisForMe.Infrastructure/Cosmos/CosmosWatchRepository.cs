using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosWatchRepository : IWatchRepository
{
    private readonly Container _container;

    public CosmosWatchRepository(CosmosClient client, IOptions<CosmosOptions> options)
    {
        var settings = options.Value;
        _container = client.GetContainer(settings.DatabaseName, settings.WatchesContainerName);
    }

    public async Task<Watch> CreateAsync(Watch watch, CancellationToken cancellationToken = default)
    {
        var response = await _container.CreateItemAsync(watch, new PartitionKey(watch.Id), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<Watch?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<Watch>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Watch>> ListAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Watch>();
        using var iterator = _container.GetItemLinqQueryable<Watch>().ToFeedIterator();
        while (iterator.HasMoreResults)
        {
            foreach (var watch in await iterator.ReadNextAsync(cancellationToken))
            {
                results.Add(watch);
            }
        }

        return results;
    }

    public async Task<Watch> UpdateAsync(Watch watch, CancellationToken cancellationToken = default)
    {
        var response = await _container.ReplaceItemAsync(watch, watch.Id, new PartitionKey(watch.Id), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _container.DeleteItemAsync<Watch>(id, new PartitionKey(id), cancellationToken: cancellationToken);
    }
}
