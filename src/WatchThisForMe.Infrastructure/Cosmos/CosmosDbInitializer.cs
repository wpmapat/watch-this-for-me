using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosDbInitializer(CosmosClient client, IOptions<CosmosOptions> options)
{
    private readonly CosmosOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var database = await client.CreateDatabaseIfNotExistsAsync(_options.DatabaseName, cancellationToken: cancellationToken);

        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_options.WatchesContainerName, partitionKeyPath: "/id"),
            cancellationToken: cancellationToken);

        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_options.ObservationsContainerName, partitionKeyPath: "/watchId"),
            cancellationToken: cancellationToken);
    }
}
