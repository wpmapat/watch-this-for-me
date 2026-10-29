namespace WatchThisForMe.Core.Executions;

public enum WatchExecutionStatus
{
    // First attempt since the watch's last successful run.
    Started,

    // An attempt following one or more recent failures, not yet dead-lettered.
    Retrying,

    Succeeded,
    Failed
}
