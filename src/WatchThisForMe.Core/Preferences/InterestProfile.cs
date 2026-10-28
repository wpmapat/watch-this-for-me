namespace WatchThisForMe.Core.Preferences;

// Singleton for now - there's no multi-user/auth concept yet, so there's exactly
// one interest profile for the whole system, always stored under DefaultId.
public class InterestProfile
{
    public const string DefaultId = "default";

    public string Id { get; set; } = DefaultId;

    public string Interests { get; set; } = string.Empty;
}
