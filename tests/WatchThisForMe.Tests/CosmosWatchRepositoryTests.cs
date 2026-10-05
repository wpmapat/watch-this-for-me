using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Cosmos;
using Xunit;

namespace WatchThisForMe.Tests;

// Requires the Cosmos DB Emulator to be running locally (https://localhost:8081).
public class CosmosWatchRepositoryTests
{
    private static readonly CosmosOptions TestOptions = new()
    {
        ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
        DatabaseName = "WatchThisForMe",
        WatchesContainerName = "Watches",
        IsEmulator = true
    };

    [Fact]
    public async Task CreateAndGet_RoundTripsAWatch()
    {
        using var client = CosmosClientFactory.Create(TestOptions);
        var options = Options.Create(TestOptions);

        await new CosmosDbInitializer(client, options).InitializeAsync();

        var repository = new CosmosWatchRepository(client, options);
        var watch = new Watch
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Integration test watch",
            UserRequest = "Watch this for me",
            Source = "https://example.com",
            SourceType = SourceType.WebPage,
            CheckFrequency = TimeSpan.FromHours(1),
            MeaningfulChangeCriteria = "Any change",
            NotificationPolicy = NotificationPolicy.Always,
            NotificationChannel = NotificationChannel.Email,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await repository.CreateAsync(watch);
            var fetched = await repository.GetAsync(watch.Id);

            Assert.NotNull(fetched);
            Assert.Equal(watch.Name, fetched!.Name);
            Assert.Equal(watch.SourceType, fetched.SourceType);
        }
        finally
        {
            await repository.DeleteAsync(watch.Id);
        }
    }
}
