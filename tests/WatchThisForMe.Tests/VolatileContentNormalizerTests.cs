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
}
