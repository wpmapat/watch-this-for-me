using System.Net;
using System.Text.Json;

namespace WatchThisForMe.Infrastructure.GitHub;

public class GitHubClient(HttpClient httpClient)
{
    public async Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"repos/{owner}/{repo}/releases/latest", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;

        return new GitHubRelease(
            TagName: root.GetProperty("tag_name").GetString()!,
            Name: root.GetProperty("name").GetString() ?? root.GetProperty("tag_name").GetString()!,
            Body: root.GetProperty("body").GetString() ?? string.Empty,
            PublishedAt: root.GetProperty("published_at").GetDateTimeOffset(),
            HtmlUrl: root.GetProperty("html_url").GetString()!);
    }

    public async Task<IReadOnlyList<GitHubIssue>> GetIssuesAsync(
        string owner, string repo, string state = "open", int limit = 10, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"repos/{owner}/{repo}/issues?state={state}&per_page={limit}", cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var issues = new List<GitHubIssue>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            // GitHub's issues endpoint also returns pull requests; skip those.
            if (item.TryGetProperty("pull_request", out _))
            {
                continue;
            }

            issues.Add(new GitHubIssue(
                Number: item.GetProperty("number").GetInt32(),
                Title: item.GetProperty("title").GetString()!,
                State: item.GetProperty("state").GetString()!,
                CreatedAt: item.GetProperty("created_at").GetDateTimeOffset(),
                HtmlUrl: item.GetProperty("html_url").GetString()!));
        }

        return issues;
    }
}
