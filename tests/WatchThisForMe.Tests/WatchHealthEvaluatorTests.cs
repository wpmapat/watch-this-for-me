using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Watches;
using Xunit;

namespace WatchThisForMe.Tests;

public class WatchHealthEvaluatorTests
{
    private static WatchExecution Execution(WatchExecutionStatus status) => new()
    {
        Id = "e1",
        WatchId = "w1",
        StartedAt = DateTimeOffset.UtcNow,
        Status = status,
        AttemptNumber = 1
    };

    [Fact]
    public void ActiveWatchThatNeverRan_IsPending() =>
        Assert.Equal(WatchHealth.Pending, WatchHealthEvaluator.Evaluate(WatchStatus.Active, null));

    [Theory]
    [InlineData(WatchExecutionStatus.Succeeded, WatchHealth.Healthy)]
    [InlineData(WatchExecutionStatus.Started, WatchHealth.Healthy)]
    [InlineData(WatchExecutionStatus.Failed, WatchHealth.NeedsAttention)]
    [InlineData(WatchExecutionStatus.Retrying, WatchHealth.NeedsAttention)]
    public void ActiveWatch_HealthFollowsLatestRun(WatchExecutionStatus runStatus, WatchHealth expected) =>
        Assert.Equal(expected, WatchHealthEvaluator.Evaluate(WatchStatus.Active, Execution(runStatus)));

    [Fact]
    public void DeadLetteredWatch_IsStopped_RegardlessOfRun() =>
        Assert.Equal(WatchHealth.Stopped, WatchHealthEvaluator.Evaluate(WatchStatus.DeadLettered, Execution(WatchExecutionStatus.Failed)));

    [Fact]
    public void PausedWatch_IsPaused_EvenAfterAFailure() =>
        Assert.Equal(WatchHealth.Paused, WatchHealthEvaluator.Evaluate(WatchStatus.Paused, Execution(WatchExecutionStatus.Failed)));

    // The silent-failure case from Scenario 8: every check "succeeds", but on a "not found"
    // page or the site's homepage.
    [Theory]
    [InlineData(WatchExecutionStatus.Succeeded)]
    [InlineData(WatchExecutionStatus.Started)]
    public void SuccessfulRunsOnTheWrongPage_NeedAttention(WatchExecutionStatus runStatus) =>
        Assert.Equal(WatchHealth.NeedsAttention, WatchHealthEvaluator.Evaluate(WatchStatus.Active, Execution(runStatus), sourceLooksWrong: true));
}
