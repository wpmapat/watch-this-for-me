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

    // How many consecutive failed attempts this run represents, including itself if it fails.
    // 1 means this is the first attempt since the last success.
    public required int AttemptNumber { get; set; }
}
