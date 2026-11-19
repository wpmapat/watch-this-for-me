using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Monitoring;
using WatchThisForMe.Tests.Fakes;
using Xunit;

namespace WatchThisForMe.Tests;

public class WatchSchedulerTests
{
    // Wraps the in-memory store but makes the first N "list all watches" calls fail, like a
    // database that's briefly unavailable.
    private sealed class FlakyListWatchRepository(IWatchRepository inner, int failuresBeforeRecovery) : IWatchRepository
    {
        private int _remainingFailures = failuresBeforeRecovery;

        public Task<IReadOnlyList<Watch>> ListAsync(CancellationToken cancellationToken = default) =>
            Interlocked.Decrement(ref _remainingFailures) >= 0
                ? throw new HttpRequestException("Database briefly unavailable")
                : inner.ListAsync(cancellationToken);

        public Task<Watch> CreateAsync(Watch watch, CancellationToken cancellationToken = default) => inner.CreateAsync(watch, cancellationToken);
        public Task<Watch?> GetAsync(string id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<Watch> UpdateAsync(Watch watch, CancellationToken cancellationToken = default) => inner.UpdateAsync(watch, cancellationToken);
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
    }

    [Fact]
    public async Task DatabaseErrorWhileListingWatches_DoesNotKillTheScheduler()
    {
        var store = new InMemoryWatchRepository();
        await store.CreateAsync(new Watch
        {
            Id = "w1",
            Name = "Test",
            UserRequest = "test",
            Source = "https://example.com",
            SourceType = SourceType.WebPage,
            CheckFrequency = TimeSpan.FromHours(1),
            MeaningfulChangeCriteria = "anything",
            NotificationPolicy = NotificationPolicy.OnlyMeaningfulChanges,
            NotificationChannel = NotificationChannel.Email,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var watches = new FlakyListWatchRepository(store, failuresBeforeRecovery: 2);
        var executions = new InMemoryWatchExecutionRepository();
        var observations = new InMemoryObservationRepository();
        var checkService = new WatchCheckService(
            watches, observations, new ScriptedSourceFetcher().ThenReturns("v1.0"),
            new FakeChangeClassifier(), new FakeEmbeddingGenerator(), new FakeInterestProfileRepository(),
            executions, TestNotifier.Create(observations), NullLogger<WatchCheckService>.Instance);

        var scheduler = new WatchScheduler(
            watches, checkService,
            Options.Create(new WatchSchedulerOptions { PollingInterval = TimeSpan.FromMilliseconds(20) }),
            NullLogger<WatchScheduler>.Instance);

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            // Two failed rounds, then the database "recovers" - the watch must still get checked.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!executions.All.Any(e => e.Status == WatchExecutionStatus.Succeeded) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.False(scheduler.ExecuteTask!.IsFaulted, "Scheduler loop crashed instead of retrying.");
            var run = Assert.Single(executions.All);
            Assert.Equal(WatchExecutionStatus.Succeeded, run.Status);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }
}
