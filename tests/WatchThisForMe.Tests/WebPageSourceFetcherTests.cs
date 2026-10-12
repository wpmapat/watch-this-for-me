using WatchThisForMe.Infrastructure.Sources;
using Xunit;

namespace WatchThisForMe.Tests;

// Requires internet access.
public class WebPageSourceFetcherTests
{
    [Fact]
    public async Task FetchAsync_ReturnsNonEmptyNormalizedContentAndHash()
    {
        using var httpClient = new HttpClient();
        var fetcher = new WebPageSourceFetcher(httpClient);

        var snapshot = await fetcher.FetchAsync("https://example.com");

        Assert.False(string.IsNullOrWhiteSpace(snapshot.NormalizedContent));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ContentHash));
    }

    [Fact]
    public async Task FetchAsync_ProducesSameHashForUnchangedContent()
    {
        using var httpClient = new HttpClient();
        var fetcher = new WebPageSourceFetcher(httpClient);

        var first = await fetcher.FetchAsync("https://example.com");
        var second = await fetcher.FetchAsync("https://example.com");

        Assert.Equal(first.ContentHash, second.ContentHash);
    }
}
