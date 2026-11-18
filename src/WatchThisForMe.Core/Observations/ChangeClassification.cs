namespace WatchThisForMe.Core.Observations;

public static class ChangeCategories
{
    // The new content isn't the real page any more: "not found", error, maintenance, sign-in
    // wall... Never meaningful; flags the watch so the user knows it isn't really watching.
    public const string PageUnavailable = "page_unavailable";
}

public record ChangeClassification(
    bool Meaningful,
    string Category,
    double Confidence,
    string Summary);
