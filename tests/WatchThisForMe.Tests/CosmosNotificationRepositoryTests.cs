using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Notifications;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Infrastructure.Cosmos;
using Xunit;

namespace WatchThisForMe.Tests;

// Requires the Cosmos DB Emulator to be running locally (https://localhost:8081).
// The "never twice" promise rests on the real database refusing a second notice with the same Id -
// the in-memory fake only imitates that, so it's proven here against Cosmos itself.
public class CosmosNotificationRepositoryTests
{
    private static readonly CosmosOptions TestOptions = new()
    {
        ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
        DatabaseName = "WatchThisForMe",
        IsEmulator = true
    };

    private static WatchNotification Notice(string id, string watchId) => new()
    {
        Id = id,
        WatchId = watchId,
        WatchName = "Test",
        Source = "https://example.com",
        CreatedAt = DateTimeOffset.UtcNow,
        ChangeDetectedAt = DateTimeOffset.UtcNow,
        WhatChanged = "v2.0 released",
        WhyItMatters = "You asked about releases",
        Evidence = [new EvidenceItem("diff", "+ v2.0")]
    };

    [Fact]
    public async Task SecondNoticeForTheSameChange_IsRefused_AndDeleteAllRemovesThem()
    {
        using var client = CosmosClientFactory.Create(TestOptions);
        var options = Options.Create(TestOptions);
        await new CosmosDbInitializer(client, options).InitializeAsync();
        var repository = new CosmosNotificationRepository(client, options);
        var watchId = Guid.NewGuid().ToString();
        var changeId = Guid.NewGuid().ToString();

        Assert.True(await repository.TryCreateAsync(Notice(changeId, watchId)));
        Assert.False(await repository.TryCreateAsync(Notice(changeId, watchId)));

        var saved = await repository.GetAsync(changeId, watchId);
        Assert.Equal("+ v2.0", Assert.Single(saved!.Evidence).Detail);
        Assert.Contains(await repository.ListRecentAsync(200), n => n.Id == changeId);

        await repository.DeleteAllAsync(watchId);
        Assert.Null(await repository.GetAsync(changeId, watchId));
    }

    [Fact]
    public async Task PendingMarkOnAChange_SurvivesAnUpdate()
    {
        using var client = CosmosClientFactory.Create(TestOptions);
        var options = Options.Create(TestOptions);
        await new CosmosDbInitializer(client, options).InitializeAsync();
        var observations = new CosmosObservationRepository(client, options);
        var watchId = Guid.NewGuid().ToString();

        var change = await observations.CreateAsync(new Observation
        {
            Id = Guid.NewGuid().ToString(),
            WatchId = watchId,
            CheckedAt = DateTimeOffset.UtcNow,
            ContentHash = "h",
            NormalizedContent = "v2.0",
            Changed = true,
            DiffSummary = "+ v2.0",
            NotificationPending = true
        });

        change.NotificationPending = false;
        change.NotificationOutcome = "Sent";
        await observations.UpdateAsync(change);

        var reloaded = await observations.GetLatestAsync(watchId);
        Assert.False(reloaded!.NotificationPending);
        Assert.Equal("Sent", reloaded.NotificationOutcome);

        await observations.DeleteAllAsync(watchId);
    }
}
