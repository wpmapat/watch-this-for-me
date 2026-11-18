using WatchThisForMe.Infrastructure.Diffing;
using WatchThisForMe.Infrastructure.Sources;
using Xunit;

namespace WatchThisForMe.Tests;

// Real HTML -> the same cleanup the fetcher does -> diff. This is exactly the path that was
// broken in Nov 2026: the cleanup flattened pages to one line, so the diff was "whole old page
// vs whole new page" (truncated), and the AI never saw the new release. The unit tests for the
// differ alone didn't catch it because they fed it hand-made multi-line text.
public class PageDiffEndToEndTests
{
    private static string ReleasesPage(params string[] releases) => $"""
        <html>
        <head><title>.NET Releases</title><style>li {"{"} color: blue; {"}"}</style></head>
        <body>
          <nav><a href="/">Home</a> | <a href="/docs">Docs</a></nav>
          <h1>Releases</h1>
          <p>Last updated 3 hours ago. 1,204 stars.</p>
          <ul>
            {string.Concat(releases.Select(r => $"<li><a href=\"/r\">{r}</a> - <em>preview</em></li>\n"))}
          </ul>
          <footer>&copy; Example Corp</footer>
          <script>trackPageView();</script>
        </body>
        </html>
        """;

    // Mirrors WebPageSourceFetcher.
    private static string Clean(string html) => VolatileContentNormalizer.Normalize(HtmlNormalizer.Normalize(html));

    [Fact]
    public void NewRelease_DiffContainsOnlyTheNewLine()
    {
        var before = Clean(ReleasesPage(".NET 11.0 RC 2", ".NET 11.0 RC 1", ".NET 11.0 Preview 7"));
        var after = Clean(ReleasesPage(".NET 11.0 RC 3", ".NET 11.0 RC 2", ".NET 11.0 RC 1", ".NET 11.0 Preview 7"));

        var diff = TextDiffer.Summarize(before, after);

        Assert.Equal("+ .NET 11.0 RC 3 - preview", diff);
    }

    [Fact]
    public void EditedLine_ShowsOldAndNewVersionsOfThatLineOnly()
    {
        var before = Clean(ReleasesPage(".NET 11.0 RC 2", ".NET 11.0 RC 1"));
        var after = Clean(ReleasesPage(".NET 11.0 RC 2 (withdrawn)", ".NET 11.0 RC 1"));

        var diff = TextDiffer.Summarize(before, after);

        Assert.Equal("- .NET 11.0 RC 2 - preview\n+ .NET 11.0 RC 2 (withdrawn) - preview", diff.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void NoiseOnlyChange_ProducesNoDiff()
    {
        // Relative time and star count change, the releases don't.
        var before = Clean(ReleasesPage(".NET 11.0 RC 2"));
        var after = Clean(ReleasesPage(".NET 11.0 RC 2").Replace("3 hours ago", "5 hours ago").Replace("1,204", "1,311"));

        Assert.Equal(string.Empty, TextDiffer.Summarize(before, after));
    }

    [Fact]
    public void RandomTraceIdOnEveryVisit_IsNotAChange()
    {
        // Microsoft's investor page prints a new trace id on every request; before masking, that
        // made the page "change" every hour and cost an AI call each time.
        var before = Clean(ReleasesPage(".NET 11.0 RC 2").Replace("</footer>", "</footer><p>This is the Trace Id: 504cbad79399f4f50a17dd1c0b188409</p>"));
        var after = Clean(ReleasesPage(".NET 11.0 RC 2").Replace("</footer>", "</footer><p>This is the Trace Id: 62019e64c139185ee176bba1b0aab2e5</p>"));

        Assert.Equal(before, after);
    }

    [Fact]
    public void ScriptsStylesAndTags_NeverReachTheDiff()
    {
        var cleaned = Clean(ReleasesPage(".NET 11.0 RC 2"));

        Assert.DoesNotContain("trackPageView", cleaned);
        Assert.DoesNotContain("color: blue", cleaned);
        Assert.DoesNotContain("<", cleaned);
        Assert.Contains("\n", cleaned);
    }
}
