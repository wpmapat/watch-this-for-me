using System.Text.RegularExpressions;

namespace WatchThisForMe.Infrastructure.Sources;

public static partial class VolatileContentNormalizer
{
    public static string Normalize(string content)
    {
        // Random per-request identifiers (trace/request ids, session tokens) differ on every
        // visit, so without this a page carrying one "changes" on every check - e.g. Microsoft's
        // investor page prints "Trace Id: <32 hex chars>", which cost an AI call every hour.
        var withoutGuids = GuidRegex().Replace(content, "[ID]");
        var withoutIds = HexIdRegex().Replace(withoutGuids, "[ID]");
        var withoutRelativeTimestamps = RelativeTimestampRegex().Replace(withoutIds, "[TIMESTAMP]");
        var withoutDates = DateRegex().Replace(withoutRelativeTimestamps, "[DATE]");
        var withoutCounts = CountRegex().Replace(withoutDates, "[COUNT] $1");

        return withoutCounts;
    }

    [GeneratedRegex(@"\b\d+\s+(second|minute|hour|day|week|month|year)s?\s+ago\b", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeTimestampRegex();

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex GuidRegex();

    // 16+ hex characters mixing letters AND digits: trace ids, hashes, tokens. Requiring both
    // leaves long plain numbers alone. Side effect: full commit SHAs are masked too - fine for
    // release pages, where the release name changes alongside the SHA.
    [GeneratedRegex(@"\b(?=[0-9a-f]*[a-f])(?=[0-9a-f]*[0-9])[0-9a-f]{16,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex HexIdRegex();

    [GeneratedRegex(
        @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}\b|\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}/\d{1,2}/\d{2,4}\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b[\d,]+\s*(views?|likes?|stars?|comments?|forks?|watchers?|followers?|points?|upvotes?|votes?|shares?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CountRegex();
}
