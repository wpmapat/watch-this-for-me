namespace WatchThisForMe.Core.Watches;

public record WatchDraft(
    string Name,
    string Source,
    SourceType SourceType,
    TimeSpan CheckFrequency,
    string MeaningfulChangeCriteria,
    string? InvestigationInstructions,
    NotificationPolicy NotificationPolicy,
    NotificationChannel NotificationChannel);
