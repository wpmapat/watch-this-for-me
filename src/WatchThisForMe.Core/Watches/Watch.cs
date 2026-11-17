namespace WatchThisForMe.Core.Watches;

public class Watch
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string UserRequest { get; set; }

    public required string Source { get; set; }

    public required SourceType SourceType { get; set; }

    public required TimeSpan CheckFrequency { get; set; }

    public required string MeaningfulChangeCriteria { get; set; }

    public string? InvestigationInstructions { get; set; }

    public required NotificationPolicy NotificationPolicy { get; set; }

    public required NotificationChannel NotificationChannel { get; set; }

    public WatchStatus Status { get; set; } = WatchStatus.Active;

    public required DateTimeOffset CreatedAt { get; set; }

    // Last time a check fetched the source successfully.
    public DateTimeOffset? LastCheckedAt { get; set; }

    // Last time a check was attempted, whether or not it succeeded. Scheduling is based on this,
    // so a failing watch is retried on its normal cadence rather than on every scheduler tick.
    public DateTimeOffset? LastAttemptAt { get; set; }

    // When the user last resumed this watch (from Paused or Stopped). Failures before this
    // don't count toward dead-lettering, so a resumed watch gets a fresh set of attempts
    // instead of being stopped again by its very next failure.
    public DateTimeOffset? ResumedAt { get; set; }
}
