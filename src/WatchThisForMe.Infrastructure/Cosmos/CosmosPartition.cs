using Microsoft.Azure.Cosmos;

namespace WatchThisForMe.Infrastructure.Cosmos;

internal static class CosmosPartition
{
    // Deletes every item in one partition (all of a watch's snapshots or run records). Reads only
    // the ids, then deletes one by one - a watch has at most a few hundred items thanks to pruning
    // and TTL, and this avoids Cosmos's delete-by-partition feature, which needs enabling per account.
    public static async Task DeleteAllAsync(Container container, string partitionKey, CancellationToken cancellationToken)
    {
        var key = new PartitionKey(partitionKey);
        using var iterator = container.GetItemQueryIterator<IdOnly>(
            "SELECT c.id FROM c", requestOptions: new QueryRequestOptions { PartitionKey = key });

        while (iterator.HasMoreResults)
        {
            foreach (var item in await iterator.ReadNextAsync(cancellationToken))
            {
                await container.DeleteItemStreamAsync(item.id, key, cancellationToken: cancellationToken);
            }
        }
    }

    // Cosmos's serializer here is Newtonsoft with the default (as-written) property names.
    private sealed record IdOnly(string id);
}
