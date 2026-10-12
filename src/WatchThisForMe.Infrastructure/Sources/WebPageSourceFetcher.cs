using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Sources;

public class WebPageSourceFetcher(HttpClient httpClient) : ISourceFetcher
{
    public SourceType SourceType => SourceType.WebPage;

    public async Task<SourceSnapshot> FetchAsync(string source, CancellationToken cancellationToken = default)
    {
        var html = await httpClient.GetStringAsync(source, cancellationToken);
        var normalized = HtmlNormalizer.Normalize(html);
        var stable = VolatileContentNormalizer.Normalize(normalized);
        var hash = ContentHasher.Compute(stable);

        return new SourceSnapshot(stable, hash, DateTimeOffset.UtcNow);
    }
}
