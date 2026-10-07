namespace WatchThisForMe.Core.Sources;

public record SourceSnapshot(string NormalizedContent, string ContentHash, DateTimeOffset FetchedAt);
