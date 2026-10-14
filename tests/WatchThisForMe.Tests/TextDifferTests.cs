using WatchThisForMe.Infrastructure.Diffing;
using Xunit;

namespace WatchThisForMe.Tests;

public class TextDifferTests
{
    [Fact]
    public void Summarize_ReturnsEmpty_WhenTextIsIdentical()
    {
        var result = TextDiffer.Summarize("The API supports authentication using X.", "The API supports authentication using X.");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Summarize_ShowsOnlyTheChangedLine()
    {
        var before = "Line one.\nLine two.\nLine three.";
        var after = "Line one.\nLine two updated.\nLine three.";

        var result = TextDiffer.Summarize(before, after);

        Assert.Contains("- Line two.", result);
        Assert.Contains("+ Line two updated.", result);
        Assert.DoesNotContain("Line one.", result);
        Assert.DoesNotContain("Line three.", result);
    }

    [Fact]
    public void Summarize_TruncatesVeryLargeDiffs()
    {
        var before = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"line {i}"));
        var after = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"line {i} changed"));

        var result = TextDiffer.Summarize(before, after);

        Assert.Contains("more changed lines omitted", result);
    }
}
