using System.ComponentModel;
using ModelContextProtocol.Server;
using WatchThisForMe.Infrastructure.GitHub;

namespace WatchThisForMe.McpServer.Tools;

[McpServerToolType]
public sealed class GitHubTools(GitHubClient gitHubClient)
{
    [McpServerTool(Name = "get_github_release", ReadOnly = true, Destructive = false)]
    [Description("Gets the latest release of a GitHub repository, including its tag, release notes, and publish date.")]
    public async Task<string> GetGitHubRelease(
        [Description("The repository owner, e.g. 'dotnet'.")] string owner,
        [Description("The repository name, e.g. 'runtime'.")] string repo)
    {
        var release = await gitHubClient.GetLatestReleaseAsync(owner, repo);
        if (release is null)
        {
            return $"No releases found for {owner}/{repo}.";
        }

        return $"""
            Tag: {release.TagName}
            Name: {release.Name}
            Published: {release.PublishedAt:u}
            URL: {release.HtmlUrl}
            Notes:
            {release.Body}
            """;
    }

    [McpServerTool(Name = "get_github_issues", ReadOnly = true, Destructive = false)]
    [Description("Lists issues for a GitHub repository (excludes pull requests).")]
    public async Task<string> GetGitHubIssues(
        [Description("The repository owner, e.g. 'dotnet'.")] string owner,
        [Description("The repository name, e.g. 'runtime'.")] string repo,
        [Description("Issue state to filter by: open, closed, or all. Defaults to open.")] string state = "open")
    {
        var issues = await gitHubClient.GetIssuesAsync(owner, repo, state);
        if (issues.Count == 0)
        {
            return $"No {state} issues found for {owner}/{repo}.";
        }

        return string.Join("\n", issues.Select(i => $"#{i.Number} [{i.State}] {i.Title} ({i.HtmlUrl})"));
    }
}
