using WatchThisForMe.Core.Watches;
using Xunit;

namespace WatchThisForMe.Tests;

public class WatchScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static Watch HourlyWatch(DateTimeOffset? lastCheckedAt, DateTimeOffset? lastAttemptAt) => new()
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
        CreatedAt = Now.AddDays(-1),
        LastCheckedAt = lastCheckedAt,
        LastAttemptAt = lastAttemptAt
    };

    [Fact]
    public void NeverRun_IsDue() =>
        Assert.True(WatchSchedule.IsDue(HourlyWatch(null, null), Now));

    // The regression this guards: the last success was long ago, but a failed attempt just
    // happened - the watch must wait a full interval, not retry on the next scheduler tick.
    [Fact]
    public void RecentFailedAttempt_IsNotDue_EvenIfLastSuccessWasLongAgo() =>
        Assert.False(WatchSchedule.IsDue(HourlyWatch(Now.AddDays(-2), Now.AddSeconds(-20)), Now));

    [Fact]
    public void FullIntervalSinceLastAttempt_IsDue() =>
        Assert.True(WatchSchedule.IsDue(HourlyWatch(Now.AddDays(-2), Now.AddHours(-1)), Now));

    [Fact]
    public void WatchSavedBeforeLastAttemptAtExisted_FallsBackToLastCheckedAt()
    {
        Assert.False(WatchSchedule.IsDue(HourlyWatch(Now.AddMinutes(-10), null), Now));
        Assert.True(WatchSchedule.IsDue(HourlyWatch(Now.AddHours(-2), null), Now));
    }
}
