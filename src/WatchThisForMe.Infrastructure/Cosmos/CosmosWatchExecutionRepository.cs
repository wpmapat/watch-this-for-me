using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Executions;

namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosWatchExecutionRepository : IWatchExecutionRepository
{
    private readonly Container _container;

    public CosmosWatchExecutionRepository(CosmosClient client, IOptions<CosmosOptions> options)
    {
        var settings = options.Value;
        _container = client.GetContainer(settings.DatabaseName, settings.ExecutionsContainerName);
    }

    public async Task<WatchExecution> CreateAsync(WatchExecution execution, CancellationToken cancellationToken = default)
    {
        var response = await _container.CreateItemAsync(execution, new PartitionKey(execution.WatchId), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<WatchExecution> UpdateAsync(WatchExecution execution, CancellationToken cancellationToken = default)
    {
        var response = await _container.ReplaceItemAsync(execution, execution.Id, new PartitionKey(execution.WatchId), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<IReadOnlyList<WatchExecution>> ListAsync(string watchId, int? maxCount = null, CancellationToken cancellationToken = default)
    {
        var queryOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(watchId) };
        var queryable = _container.GetItemLinqQueryable<WatchExecution>(requestOptions: queryOptions)
            .Where(e => e.WatchId == watchId)
            .OrderByDescending(e => e.StartedAt);

        var limited = maxCount is { } limit ? queryable.Take(limit) : queryable;

        var results = new List<WatchExecution>();
        using var iterator = limited.ToFeedIterator();

        while (iterator.HasMoreResults)
        {
            foreach (var execution in await iterator.ReadNextAsync(cancellationToken))
            {
                results.Add(execution);
            }
        }

        return results;
    }
}
