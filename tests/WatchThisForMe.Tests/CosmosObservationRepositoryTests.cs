using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Infrastructure.Cosmos;
using Xunit;

namespace WatchThisForMe.Tests;

// Requires the Cosmos DB Emulator to be running locally (https://localhost:8081).
public class CosmosObservationRepositoryTests
{
    private static readonly CosmosOptions TestOptions = new()
    {
        ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
        DatabaseName = "WatchThisForMe",
        ObservationsContainerName = "Observations",
        IsEmulator = true
    };

    [Fact]
    public async Task ListAsync_WithMaxCount_ReturnsOnlyTheMostRecent()
    {
        using var client = CosmosClientFactory.Create(TestOptions);
        var options = Options.Create(TestOptions);

        await new CosmosDbInitializer(client, options).InitializeAsync();

        var repository = new CosmosObservationRepository(client, options);
        var watchId = Guid.NewGuid().ToString();
        var createdIds = new List<string>();

        try
        {
            for (var i = 0; i < 5; i++)
            {
                var observation = new Observation
                {
                    Id = Guid.NewGuid().ToString(),
                    WatchId = watchId,
                    CheckedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                    ContentHash = $"hash-{i}",
                    NormalizedContent = $"content-{i}",
                    Changed = true
                };

                await repository.CreateAsync(observation);
                createdIds.Add(observation.Id);
            }

            var recent = await repository.ListAsync(watchId, maxCount: 2);

            Assert.Equal(2, recent.Count);
            Assert.Equal("hash-4", recent[0].ContentHash);
            Assert.Equal("hash-3", recent[1].ContentHash);
        }
        finally
        {
            foreach (var id in createdIds)
            {
                await repository.DeleteAsync(id, watchId);
            }
        }
    }
}
