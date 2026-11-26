namespace WatchThisForMe.Core.Executions;

// One record per attempted watch run (scheduled or manual) - makes failures a durable,
// visible fact instead of a log line that scrolls away.
public class WatchExecution
{
    public required string Id { get; set; }

    public required string WatchId { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public required WatchExecutionStatus Status { get; set; }

    public string? ErrorMessage { get; set; }

    // Failed because the AI couldn't analyse a change, not because the website failed. Such a
    // run never counts toward stopping the watch. (Older records lack the field: false, which is
    // right - before this existed every failure was treated as a website failure.)
    public bool AiUnavailable { get; set; }

    // How many consecutive failed attempts this run represents, including itself if it fails.
    // 1 means this is the first attempt since the last success.
    public required int AttemptNumber { get; set; }

    // What the run did, in order - saved when the run ends. Empty for runs recorded before
    // Nov 25 2026, and for a run that is still going (or crashed before it could be saved).
    public List<ExecutionStep> Steps { get; set; } = [];
}
