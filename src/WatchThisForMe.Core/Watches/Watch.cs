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

    public DateTimeOffset? LastCheckedAt { get; set; }
}
