namespace WatchThisForMe.Infrastructure.GitHub;

public class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string BaseUrl { get; set; } = "https://api.github.com";

    public string? Token { get; set; }
}
