namespace WatchThisForMe.Infrastructure.GitHub;

public record GitHubRelease(string TagName, string Name, string Body, DateTimeOffset PublishedAt, string HtmlUrl);
