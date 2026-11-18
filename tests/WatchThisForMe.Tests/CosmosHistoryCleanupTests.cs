using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Infrastructure.Cosmos;
using Xunit;

namespace WatchThisForMe.Tests;

// Requires the Cosmos DB Emulator to be running locally (https://localhost:8081).
// Keeps storage from growing forever: run records expire, and deleting a watch removes its history.
public class CosmosHistoryCleanupTests
{
    private static readonly CosmosOptions TestOptions = new()
    {
        ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
        DatabaseName = "WatchThisForMe",
        IsEmulator = true
    };

    [Fact]
    public async Task RunRecordsContainer_ExpiresItemsAfterTheRetentionPeriod()
    {
        using var client = CosmosClientFactory.Create(TestOptions);
        await new CosmosDbInitializer(client, Options.Create(TestOptions)).InitializeAsync();

        var properties = await client.GetContainer(TestOptions.DatabaseName, TestOptions.ExecutionsContainerName).ReadContainerAsync();

        Assert.Equal(TestOptions.ExecutionRetentionDays * 24 * 60 * 60, properties.Resource.DefaultTimeToLive);
    }

    [Fact]
    public async Task DeleteAll_RemovesEveryRunRecordAndSnapshotOfThatWatch_Only()
    {
        using var client = CosmosClientFactory.Create(TestOptions);
        var options = Options.Create(TestOptions);
        await new CosmosDbInitializer(client, options).InitializeAsync();
        var executions = new CosmosWatchExecutionRepository(client, options);
        var observations = new CosmosObservationRepository(client, options);

        var doomed = Guid.NewGuid().ToString();
        var survivor = Guid.NewGuid().ToString();

        try
        {
            foreach (var watchId in new[] { doomed, doomed, doomed, survivor })
            {
                await executions.CreateAsync(new WatchExecution
                {
                    Id = Guid.NewGuid().ToString(),
                    WatchId = watchId,
                    StartedAt = DateTimeOffset.UtcNow,
                    Status = WatchExecutionStatus.Succeeded,
                    AttemptNumber = 1
                });
                await observations.CreateAsync(new Observation
                {
                    Id = Guid.NewGuid().ToString(),
                    WatchId = watchId,
                    CheckedAt = DateTimeOffset.UtcNow,
                    ContentHash = "h",
                    NormalizedContent = "c",
                    Changed = true
                });
            }

            await executions.DeleteAllAsync(doomed);
            await observations.DeleteAllAsync(doomed);

            Assert.Empty(await executions.ListAsync(doomed));
            Assert.Empty(await observations.ListAsync(doomed));
            Assert.Single(await executions.ListAsync(survivor));
            Assert.Single(await observations.ListAsync(survivor));
        }
        finally
        {
            await executions.DeleteAllAsync(survivor);
            await observations.DeleteAllAsync(survivor);
        }
    }
}
