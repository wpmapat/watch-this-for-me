using WatchThisForMe.Core.Executions;

namespace WatchThisForMe.Core.Watches;

// A single at-a-glance answer to "is this watch doing its job?" for the dashboard.
// Deliberately separate from "did something important change" - a watch can be perfectly
// healthy and have nothing to report, or be broken while its last finding still stands.
public enum WatchHealth
{
    // Active, but no run has been recorded yet.
    Pending,

    // Active and the most recent run succeeded.
    Healthy,

    // Active, but the most recent run failed or is being retried - not dead-lettered yet -
    // or runs succeed but the page looks wrong (redirected elsewhere, "not found" page...).
    NeedsAttention,

    // Dead-lettered after repeated failures; the scheduler no longer runs it.
    Stopped,

    // Paused by the user.
    Paused
}

public static class WatchHealthEvaluator
{
    // latestExecution is the most recent run record for this watch, or null if it has never run.
    // sourceLooksWrong: checks succeed, but on the wrong content (see Watch.RedirectedTo and
    // Watch.PageUnavailableReason) - the dangerous case where a watch would otherwise look
    // healthy while silently missing every real change.
    public static WatchHealth Evaluate(WatchStatus status, WatchExecution? latestExecution, bool sourceLooksWrong = false) => status switch
    {
        WatchStatus.Paused => WatchHealth.Paused,
        WatchStatus.DeadLettered => WatchHealth.Stopped,
        _ => latestExecution?.Status switch
        {
            null => WatchHealth.Pending,
            WatchExecutionStatus.Succeeded when sourceLooksWrong => WatchHealth.NeedsAttention,
            WatchExecutionStatus.Succeeded => WatchHealth.Healthy,
            WatchExecutionStatus.Failed => WatchHealth.NeedsAttention,
            // A Retrying record is created at the start of an attempt that follows a failure,
            // so until it succeeds the watch is still in a recovering state.
            WatchExecutionStatus.Retrying => WatchHealth.NeedsAttention,
            // Started = first attempt since the last success, currently in flight. Nothing has
            // gone wrong, so don't alarm the user mid-run.
            WatchExecutionStatus.Started when sourceLooksWrong => WatchHealth.NeedsAttention,
            WatchExecutionStatus.Started => WatchHealth.Healthy,
            _ => WatchHealth.Pending
        }
    };
}
