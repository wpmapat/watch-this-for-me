namespace WatchThisForMe.Core.Sources;

// RedirectedTo: the final address when the site redirected the fetch somewhere other than the
// requested source (null when it didn't).
public record SourceSnapshot(string NormalizedContent, string ContentHash, DateTimeOffset FetchedAt, string? RedirectedTo = null);
