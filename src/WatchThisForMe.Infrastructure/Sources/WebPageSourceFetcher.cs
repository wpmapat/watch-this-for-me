using System.Net;
using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Sources;

public class WebPageSourceFetcher(HttpClient httpClient) : ISourceFetcher
{
    public SourceType SourceType => SourceType.WebPage;

    public async Task<SourceSnapshot> FetchAsync(string source, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(source, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // These messages end up on the dashboard, so say what it means and what to do.
            throw new HttpRequestException(DescribeFailure(response), null, response.StatusCode);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var normalized = HtmlNormalizer.Normalize(html);
        var stable = VolatileContentNormalizer.Normalize(normalized);
        var hash = ContentHasher.Compute(stable);

        // HttpClient follows redirects silently; RequestUri is where we actually ended up.
        var finalAddress = response.RequestMessage?.RequestUri;
        var redirectedTo = finalAddress is not null && !IsSamePage(source, finalAddress) ? finalAddress.ToString() : null;

        return new SourceSnapshot(stable, hash, DateTimeOffset.UtcNow, redirectedTo);
    }

    private async Task<HttpResponseMessage> SendAsync(string source, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.GetAsync(source, cancellationToken);
        }
        catch (HttpRequestException ex) when (SourceServiceCollectionExtensions.IsBlocked(ex))
        {
            throw new HttpRequestException(BlockedAddressException.FriendlyMessage, ex);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.NameResolutionError)
        {
            throw new HttpRequestException(
                "The site's address couldn't be found (no such domain). Check the address for typos, or the site may no longer exist.",
                ex);
        }
    }

    private static string DescribeFailure(HttpResponseMessage response)
    {
        var code = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
        return response.StatusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Gone =>
                $"The page wasn't found ({code}). It may have moved or been removed - edit the watch's address.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"The site refused access ({code}). The page may need a sign-in, or the site may block automated checks.",
            HttpStatusCode.TooManyRequests =>
                $"The site is limiting how often we can check ({code}). Usually temporary.",
            >= HttpStatusCode.InternalServerError =>
                $"The site is having problems ({code}). Usually temporary.",
            _ => $"The site returned an error ({code})."
        };
    }

    // Same host and path = same page. Ignores http->https upgrades, "www.", a trailing slash,
    // host capitalisation and a folder's default page (/investor -> /investor/default), which
    // sites redirect to all the time without the page moving.
    internal static bool IsSamePage(string source, Uri final) =>
        Uri.TryCreate(source, UriKind.Absolute, out var requested)
        && string.Equals(WithoutWww(requested.Host), WithoutWww(final.Host), StringComparison.OrdinalIgnoreCase)
        && string.Equals(PagePath(requested), PagePath(final), StringComparison.Ordinal);

    private static readonly string[] DefaultPageNames = ["default", "default.aspx", "default.htm", "default.html", "index", "index.html", "index.htm", "index.php"];

    private static string PagePath(Uri address)
    {
        var path = address.AbsolutePath.TrimEnd('/');
        var lastSegment = path[(path.LastIndexOf('/') + 1)..];
        return DefaultPageNames.Contains(lastSegment, StringComparer.OrdinalIgnoreCase)
            ? path[..path.LastIndexOf('/')]
            : path;
    }

    private static string WithoutWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}
