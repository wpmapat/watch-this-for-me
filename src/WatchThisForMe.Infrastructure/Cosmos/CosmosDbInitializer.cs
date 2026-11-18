using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosDbInitializer(CosmosClient client, IOptions<CosmosOptions> options)
{
    private readonly CosmosOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Identity-based (Entra) access to Cosmos only covers data - reading and writing items. Creating
        // databases and containers is a management operation it doesn't permit, so in Azure they're
        // provisioned by the deployment (Bicep) instead, and this only runs against the emulator.
        if (_options.UsesIdentity)
        {
            return;
        }

        var database = await client.CreateDatabaseIfNotExistsAsync(_options.DatabaseName, cancellationToken: cancellationToken);

        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_options.WatchesContainerName, partitionKeyPath: "/id"),
            cancellationToken: cancellationToken);

        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_options.ObservationsContainerName, partitionKeyPath: "/watchId"),
            cancellationToken: cancellationToken);

        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_options.InterestProfileContainerName, partitionKeyPath: "/id"),
            cancellationToken: cancellationToken);

        var retentionSeconds = (int)TimeSpan.FromDays(_options.ExecutionRetentionDays).TotalSeconds;
        var executions = await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_options.ExecutionsContainerName, partitionKeyPath: "/watchId") { DefaultTimeToLive = retentionSeconds },
            cancellationToken: cancellationToken);

        // CreateIfNotExists leaves an existing container as it is, so containers created before
        // retention existed (or with a different setting) are updated here.
        if (executions.Resource.DefaultTimeToLive != retentionSeconds)
        {
            executions.Resource.DefaultTimeToLive = retentionSeconds;
            await executions.Container.ReplaceContainerAsync(executions.Resource, cancellationToken: cancellationToken);
        }
    }
}
