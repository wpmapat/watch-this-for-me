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
}
