using WatchThisForMe.Infrastructure.Sources;
using Xunit;

namespace WatchThisForMe.Tests;

public class VolatileContentNormalizerTests
{
    [Fact]
    public void Normalize_ReplacesRelativeTimestamps()
    {
        var result = VolatileContentNormalizer.Normalize("Posted 2 hours ago by Alice");

        Assert.Equal("Posted [TIMESTAMP] by Alice", result);
    }

    [Fact]
    public void Normalize_ReplacesAbsoluteDates()
    {
        var result = VolatileContentNormalizer.Normalize("Last updated: 2026-10-12");

        Assert.Equal("Last updated: [DATE]", result);
    }

    [Fact]
    public void Normalize_ReplacesCountsButKeepsTheMetricName()
    {
        var result = VolatileContentNormalizer.Normalize("1,234 views and 56 stars");

        Assert.Equal("[COUNT] views and [COUNT] stars", result);
    }

    [Fact]
    public void Normalize_ReplacesHackerNewsStylePointCounts()
    {
        var result = VolatileContentNormalizer.Normalize("730 points by someuser 2 hours ago");

        Assert.Equal("[COUNT] points by someuser [TIMESTAMP]", result);
    }

    [Fact]
    public void Normalize_ProducesIdenticalOutputWhenOnlyNoiseDiffers()
    {
        var pageYesterday = "Release notes. Posted 3 hours ago. 102 stars.";
        var pageToday = "Release notes. Posted 5 hours ago. 108 stars.";

        Assert.Equal(VolatileContentNormalizer.Normalize(pageYesterday), VolatileContentNormalizer.Normalize(pageToday));
    }

    [Fact]
    public void Normalize_PreservesRealContentChanges()
    {
        var before = "The API supports authentication using X.";
        var after = "The API supports authentication using X and Y.";

        Assert.NotEqual(VolatileContentNormalizer.Normalize(before), VolatileContentNormalizer.Normalize(after));
    }

    [Theory]
    [InlineData("This is the Trace Id: 504cbad79399f4f50a17dd1c0b188409", "This is the Trace Id: [ID]")] // Microsoft IR page
    [InlineData("Request 3f2504e0-4f89-11d3-9a0c-0305e82c3301 failed", "Request [ID] failed")]               // GUID
    [InlineData("session=9F86D081884C7D659A2FEAA0C55AD015", "session=[ID]")]
    public void Normalize_MasksRandomIds(string input, string expected) =>
        Assert.Equal(expected, VolatileContentNormalizer.Normalize(input));

    [Theory]
    [InlineData("Order number 1234567890123456")]   // long number, no letters
    [InlineData("Version 10.0.404 and v11.0.100-rc.3")]
    [InlineData("commit abc1234 fixed the build")] // short hash - too short to be a random id
    [InlineData("Breaking change in the deadbeef module")]
    public void Normalize_LeavesOrdinaryTextAlone(string input) =>
        Assert.Equal(input, VolatileContentNormalizer.Normalize(input));
}
