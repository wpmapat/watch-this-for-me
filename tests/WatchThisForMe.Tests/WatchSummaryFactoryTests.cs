using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Watches;
using Xunit;

namespace WatchThisForMe.Tests;

public class WatchSummaryFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static Watch MakeWatch(WatchStatus status = WatchStatus.Active) => new()
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
        CreatedAt = Now.AddDays(-7),
        Status = status
    };

    private static WatchExecution Run(WatchExecutionStatus status, string? error = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        WatchId = "w1",
        StartedAt = Now,
        Status = status,
        ErrorMessage = error,
        AttemptNumber = 1
    };

    private static Observation Obs(int hoursAgo, string? diff, bool? meaningful = null, string? category = null, string? summary = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        WatchId = "w1",
        CheckedAt = Now.AddHours(-hoursAgo),
        ContentHash = Guid.NewGuid().ToString(),
        NormalizedContent = "content",
        Changed = true,
        DiffSummary = diff,
        Meaningful = meaningful,
        Category = category,
        ClassificationSummary = summary
    };

    [Fact]
    public void RetryInFlight_ShowsTheErrorFromThePreviousFailure()
    {
        // Most recent first: a retry has started but not finished; the failure before it holds the reason.
        var runs = new[] { Run(WatchExecutionStatus.Retrying), Run(WatchExecutionStatus.Failed, "No such host is known.") };

        var summary = WatchSummaryFactory.Create(MakeWatch(), runs, []);

        Assert.Equal(WatchHealth.NeedsAttention, summary.Health);
        Assert.Equal("No such host is known.", summary.LastError);
    }

    [Fact]
    public void FailedRun_ShowsItsOwnError()
    {
        var summary = WatchSummaryFactory.Create(MakeWatch(), [Run(WatchExecutionStatus.Failed, "Timeout")], []);

        Assert.Equal(WatchHealth.NeedsAttention, summary.Health);
        Assert.Equal("Timeout", summary.LastError);
    }

    [Fact]
    public void StoppedWatch_ShowsTheErrorThatStoppedIt()
    {
        var summary = WatchSummaryFactory.Create(MakeWatch(WatchStatus.DeadLettered), [Run(WatchExecutionStatus.Failed, "404")], []);

        Assert.Equal(WatchHealth.Stopped, summary.Health);
        Assert.Equal("404", summary.LastError);
    }

    [Fact]
    public void HealthyWatch_ShowsNoError_EvenIfAnOlderRunFailed()
    {
        var runs = new[] { Run(WatchExecutionStatus.Succeeded), Run(WatchExecutionStatus.Failed, "old error") };

        var summary = WatchSummaryFactory.Create(MakeWatch(), runs, []);

        Assert.Equal(WatchHealth.Healthy, summary.Health);
        Assert.Null(summary.LastError);
    }

    [Fact]
    public void LastImportantChange_IsTheNewestMeaningfulOne_NotTheNewestChange()
    {
        var observations = new[]
        {
            Obs(1, "footer tweak", meaningful: false, category: "cosmetic", summary: "Footer reworded."),
            Obs(5, "v2.0 added", meaningful: true, category: "new_release", summary: "v2.0 released."),
            Obs(30, "v1.0 added", meaningful: true, category: "new_release", summary: "v1.0 released."),
            Obs(48, diff: null) // baseline
        };

        var summary = WatchSummaryFactory.Create(MakeWatch(), [Run(WatchExecutionStatus.Succeeded)], observations);

        Assert.Equal(Now.AddHours(-1), summary.LastChangeAt);
        Assert.Equal(Now.AddHours(-5), summary.LastImportantChangeAt);
        Assert.Equal("v2.0 released.", summary.LastImportantChangeSummary);
        Assert.Equal("new_release", summary.LastImportantChangeCategory);
    }

    [Fact]
    public void BaselineOnly_CountsAsNoChangesYet()
    {
        var summary = WatchSummaryFactory.Create(MakeWatch(), [Run(WatchExecutionStatus.Succeeded)], [Obs(1, diff: null)]);

        Assert.Null(summary.LastChangeAt);
        Assert.Null(summary.LastImportantChangeAt);
    }

    [Fact]
    public void OnlyTrivialChanges_HasALastChange_ButNoImportantOne()
    {
        var summary = WatchSummaryFactory.Create(MakeWatch(), [Run(WatchExecutionStatus.Succeeded)],
            [Obs(2, "footer tweak", meaningful: false), Obs(10, diff: null)]);

        Assert.Equal(Now.AddHours(-2), summary.LastChangeAt);
        Assert.Null(summary.LastImportantChangeAt);
    }

    [Fact]
    public void RedirectedPage_NeedsAttention_AndSaysWhereItGoesNow()
    {
        var watch = MakeWatch();
        watch.RedirectedTo = "https://example.com/";

        var summary = WatchSummaryFactory.Create(watch, [Run(WatchExecutionStatus.Succeeded)], []);

        Assert.Equal(WatchHealth.NeedsAttention, summary.Health);
        Assert.Equal("This page now redirects to https://example.com/. If it has moved, edit the watch's address.", summary.LastError);
    }

    [Fact]
    public void UnavailablePage_NeedsAttention_WithTheAIsReason()
    {
        var watch = MakeWatch();
        watch.PageUnavailableReason = "The page now shows a 'Page not found' message.";

        var summary = WatchSummaryFactory.Create(watch, [Run(WatchExecutionStatus.Succeeded)], []);

        Assert.Equal(WatchHealth.NeedsAttention, summary.Health);
        Assert.Equal("The page doesn't look like the real page any more: The page now shows a 'Page not found' message.", summary.LastError);
    }

    [Fact]
    public void SourceProblem_AfterAnOldFailure_ShowsTheCurrentProblemNotTheOldError()
    {
        var watch = MakeWatch();
        watch.RedirectedTo = "https://example.com/";
        var runs = new[] { Run(WatchExecutionStatus.Succeeded), Run(WatchExecutionStatus.Failed, "Old timeout") };

        var summary = WatchSummaryFactory.Create(watch, runs, []);

        Assert.StartsWith("This page now redirects", summary.LastError);
    }

    [Fact]
    public void NeverRun_IsPendingWithNothingToShow()
    {
        var summary = WatchSummaryFactory.Create(MakeWatch(), [], []);

        Assert.Equal(WatchHealth.Pending, summary.Health);
        Assert.Null(summary.LastError);
        Assert.Null(summary.LastChangeAt);
    }
}
