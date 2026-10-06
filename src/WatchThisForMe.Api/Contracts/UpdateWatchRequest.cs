using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Api.Contracts;

public class UpdateWatchRequest
{
    public required string Name { get; set; }

    public required string Source { get; set; }

    public required SourceType SourceType { get; set; }

    public required TimeSpan CheckFrequency { get; set; }

    public required string MeaningfulChangeCriteria { get; set; }

    public string? InvestigationInstructions { get; set; }

    public required NotificationPolicy NotificationPolicy { get; set; }

    public required NotificationChannel NotificationChannel { get; set; }

    public required WatchStatus Status { get; set; }
}
