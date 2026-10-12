using System.Text.RegularExpressions;

namespace WatchThisForMe.Infrastructure.Sources;

public static partial class VolatileContentNormalizer
{
    public static string Normalize(string content)
    {
        var withoutRelativeTimestamps = RelativeTimestampRegex().Replace(content, "[TIMESTAMP]");
        var withoutDates = DateRegex().Replace(withoutRelativeTimestamps, "[DATE]");
        var withoutCounts = CountRegex().Replace(withoutDates, "[COUNT] $1");

        return withoutCounts;
    }

    [GeneratedRegex(@"\b\d+\s+(second|minute|hour|day|week|month|year)s?\s+ago\b", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeTimestampRegex();

    [GeneratedRegex(
        @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}\b|\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}/\d{1,2}/\d{2,4}\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b[\d,]+\s*(views?|likes?|stars?|comments?|forks?|watchers?|followers?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CountRegex();
}
