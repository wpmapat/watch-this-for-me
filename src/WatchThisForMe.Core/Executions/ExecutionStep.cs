namespace WatchThisForMe.Core.Executions;

// One line of a run's story, in plain words, for the watch's history page: "Fetched the page",
// "AI analysis", "Sent you a notice"... These are the steps a person cares about - the finer
// internal timings (each AI call, each database write) live in the telemetry traces instead.
public class ExecutionStep
{
    public required string Name { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    public double DurationMs { get; set; }

    public required StepOutcome Outcome { get; set; }

    public string? Detail { get; set; }

    // A step that began at startedAt and has just ended.
    public static ExecutionStep EndingNow(string name, DateTimeOffset startedAt, StepOutcome outcome, string? detail) => new()
    {
        Name = name,
        StartedAt = startedAt,
        DurationMs = Math.Max(0, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
        Outcome = outcome,
        Detail = detail
    };
}

public enum StepOutcome
{
    // An ordinary step that went fine.
    Ok,

    // Worth noticing: a meaningful change, a notice sent.
    Highlight,

    // The run carried on, but something is off: an error page, a notice that will be retried.
    Warning,

    // This step stopped the run.
    Failed
}
