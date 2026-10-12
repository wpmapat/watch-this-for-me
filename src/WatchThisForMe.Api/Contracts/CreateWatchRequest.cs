using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Api.Contracts;

public class CreateWatchRequest
{
    public required string Name { get; set; }

    public required string UserRequest { get; set; }

    public required string Source { get; set; }

    public required SourceType SourceType { get; set; }

    public required TimeSpan CheckFrequency { get; set; }

    public required string MeaningfulChangeCriteria { get; set; }

    public string? InvestigationInstructions { get; set; }

    public NotificationPolicy NotificationPolicy { get; set; } = NotificationPolicy.OnlyMeaningfulChanges;

    public NotificationChannel NotificationChannel { get; set; } = NotificationChannel.Email;
}
