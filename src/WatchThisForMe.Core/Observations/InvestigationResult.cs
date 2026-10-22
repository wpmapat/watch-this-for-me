namespace WatchThisForMe.Core.Observations;

public record InvestigationResult(
    ChangeClassification Classification,
    Notification? Notification);
