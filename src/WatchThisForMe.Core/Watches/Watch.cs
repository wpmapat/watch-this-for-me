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

    // When the user last changed Source. The next check starts a fresh baseline instead of
    // diffing the new address's page against the old address's page.
    public DateTimeOffset? SourceChangedAt { get; set; }

    // Where the page actually ended up on the last successful fetch, when the site redirected
    // somewhere other than Source (moved page, or sent to the homepage). Null = no redirect.
    public string? RedirectedTo { get; set; }

    // The AI's explanation when the latest content looks like a "not found", error, maintenance
    // or sign-in page instead of the real page. Null = the page looks normal.
    public string? PageUnavailableReason { get; set; }
}
