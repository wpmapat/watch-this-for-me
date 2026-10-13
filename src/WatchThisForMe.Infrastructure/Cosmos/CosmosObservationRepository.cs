using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Observations;

namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosObservationRepository : IObservationRepository
{
    private readonly Container _container;

    public CosmosObservationRepository(CosmosClient client, IOptions<CosmosOptions> options)
    {
        var settings = options.Value;
        _container = client.GetContainer(settings.DatabaseName, settings.ObservationsContainerName);
    }

    public async Task<Observation> CreateAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        var response = await _container.CreateItemAsync(observation, new PartitionKey(observation.WatchId), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<Observation?> GetLatestAsync(string watchId, CancellationToken cancellationToken = default)
    {
        var queryOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(watchId) };
        using var iterator = _container.GetItemLinqQueryable<Observation>(requestOptions: queryOptions)
            .Where(o => o.WatchId == watchId)
            .OrderByDescending(o => o.CheckedAt)
            .Take(1)
            .ToFeedIterator();

        if (!iterator.HasMoreResults)
        {
            return null;
        }

        var page = await iterator.ReadNextAsync(cancellationToken);
        return page.FirstOrDefault();
    }

    public async Task<IReadOnlyList<Observation>> ListAsync(string watchId, int? maxCount = null, CancellationToken cancellationToken = default)
    {
        var queryOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(watchId) };
        var queryable = _container.GetItemLinqQueryable<Observation>(requestOptions: queryOptions)
            .Where(o => o.WatchId == watchId)
            .OrderByDescending(o => o.CheckedAt);

        var limited = maxCount is { } limit ? queryable.Take(limit) : queryable;

        var results = new List<Observation>();
        using var iterator = limited.ToFeedIterator();

        while (iterator.HasMoreResults)
        {
            foreach (var observation in await iterator.ReadNextAsync(cancellationToken))
            {
                results.Add(observation);
            }
        }

        return results;
    }

    public async Task DeleteAsync(string id, string watchId, CancellationToken cancellationToken = default)
    {
        await _container.DeleteItemAsync<Observation>(id, new PartitionKey(watchId), cancellationToken: cancellationToken);
    }
}
