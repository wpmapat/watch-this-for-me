namespace WatchThisForMe.Core.Observations;

public class Observation
{
    public required string Id { get; set; }

    public required string WatchId { get; set; }

    public required DateTimeOffset CheckedAt { get; set; }

    public required string ContentHash { get; set; }

    public required bool Changed { get; set; }
}
