using WatchThisForMe.Infrastructure.Sources;
using Xunit;

namespace WatchThisForMe.Tests;

public class HtmlNormalizerTests
{
    [Fact]
    public void Normalize_StripsScriptsStylesAndTags()
    {
        const string html = """
            <html>
            <head><style>body { color: red; }</style></head>
            <body>
                <script>console.log('hi');</script>
                <h1>Hello &amp; welcome</h1>
                <p>Some   text   here.</p>
            </body>
            </html>
            """;

        var result = HtmlNormalizer.Normalize(html);

        Assert.DoesNotContain("console.log", result);
        Assert.DoesNotContain("color: red", result);
        Assert.DoesNotContain("<h1>", result);
        Assert.Contains("Hello & welcome", result);
        Assert.Contains("Some text here.", result);
    }

    [Fact]
    public void Normalize_IsStableAcrossWhitespaceDifferences()
    {
        const string htmlA = "<p>Hello   world</p>";
        const string htmlB = "<p>Hello\n\nworld</p>";

        Assert.Equal(HtmlNormalizer.Normalize(htmlA), HtmlNormalizer.Normalize(htmlB));
    }

    [Fact]
    public void Normalize_PutsEachBlockOnItsOwnLine()
    {
        const string html = "<h1>Releases</h1><ul><li>v2.0</li><li>v1.0</li></ul><p>Footer</p>";

        Assert.Equal("Releases\nv2.0\nv1.0\nFooter", HtmlNormalizer.Normalize(html));
    }

    [Fact]
    public void Normalize_KeepsInlineTagsOnTheSameLine()
    {
        const string html = "<p>Version <b>2.0</b> is <a href='/x'>out</a>.<br>Next line</p>";

        Assert.Equal("Version 2.0 is out .\nNext line", HtmlNormalizer.Normalize(html));
    }

    [Fact]
    public void Normalize_IgnoresSourceIndentation_AndDropsEmptyLines()
    {
        const string html = """
            <div>
                <div></div>
                <p>
                    One
                    sentence.
                </p>
            </div>
            """;

        Assert.Equal("One sentence.", HtmlNormalizer.Normalize(html));
    }
}
