using System.Net;
using WatchThisForMe.Core.Sources;
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

// No network: a stand-in HTTP handler plays the website, so status codes and redirects are exact.
public class WebPageSourceFetcherOfflineTests
{
    private const string Page = "https://example.com/releases";

    // Answers every request with the given status/body. finalAddress simulates HttpClient having
    // followed redirects: it records the address it ended up at on the response's request.
    private sealed class FakeSite(HttpStatusCode status, string body = "<p>v1.0</p>", string? finalAddress = null, Exception? fail = null)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (fail is not null)
            {
                throw fail;
            }

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalAddress ?? request.RequestUri!.ToString())
            });
        }
    }

    private static Task<SourceSnapshot> Fetch(FakeSite site, string source = Page) =>
        new WebPageSourceFetcher(new HttpClient(site)).FetchAsync(source);

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "The page wasn't found (404 Not Found). It may have moved or been removed - edit the watch's address.")]
    [InlineData(HttpStatusCode.Gone, "The page wasn't found (410 Gone). It may have moved or been removed - edit the watch's address.")]
    [InlineData(HttpStatusCode.Forbidden, "The site refused access (403 Forbidden). The page may need a sign-in, or the site may block automated checks.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "The site is having problems (503 Service Unavailable). Usually temporary.")]
    public async Task ErrorStatus_GivesAFriendlyExplanation(HttpStatusCode status, string expected)
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Fetch(new FakeSite(status)));

        Assert.Equal(expected, ex.Message);
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task UnknownDomain_SaysToCheckTheAddress()
    {
        var dnsFailure = new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Fetch(new FakeSite(HttpStatusCode.OK, fail: dnsFailure)));

        Assert.StartsWith("The site's address couldn't be found", ex.Message);
    }

    [Fact]
    public async Task NoRedirect_HasNoRedirectedTo()
    {
        var snapshot = await Fetch(new FakeSite(HttpStatusCode.OK));

        Assert.Null(snapshot.RedirectedTo);
        Assert.Equal("v1.0", snapshot.NormalizedContent);
    }

    [Theory]
    [InlineData("https://example.com/")]                 // sent to the homepage
    [InlineData("https://example.com/releases/v2")]      // page moved
    [InlineData("https://other-site.com/releases")]      // different site
    public async Task RedirectToADifferentPage_IsReported(string finalAddress)
    {
        var snapshot = await Fetch(new FakeSite(HttpStatusCode.OK, finalAddress: finalAddress));

        Assert.Equal(finalAddress, snapshot.RedirectedTo);
    }

    [Theory]
    [InlineData("http://example.com/releases", "https://example.com/releases")]   // http -> https
    [InlineData("https://example.com/releases", "https://www.example.com/releases")]
    [InlineData("https://example.com/releases", "https://example.com/releases/")]
    [InlineData("https://example.com/releases", "https://EXAMPLE.com/releases")]
    public async Task HarmlessRedirect_IsNotReported(string source, string finalAddress)
    {
        var snapshot = await Fetch(new FakeSite(HttpStatusCode.OK, finalAddress: finalAddress), source);

        Assert.Null(snapshot.RedirectedTo);
    }
}
