namespace WatchThisForMe.Infrastructure.GitHub;

public record GitHubIssue(int Number, string Title, string State, DateTimeOffset CreatedAt, string HtmlUrl);
