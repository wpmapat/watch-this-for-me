using System.ComponentModel;
using ModelContextProtocol.Server;
using WatchThisForMe.Core.Sources;

namespace WatchThisForMe.McpServer.Tools;

[McpServerToolType]
public sealed class SourceTools(ISourceFetcher sourceFetcher)
{
    [McpServerTool(Name = "get_web_page", ReadOnly = true, Destructive = false)]
    [Description("Fetches a web page and returns its normalized text content (HTML stripped, noisy bits like timestamps and counters replaced with placeholders).")]
    public async Task<string> GetWebPage(
        [Description("The URL of the web page to fetch.")] string url)
    {
        var snapshot = await sourceFetcher.FetchAsync(url);
        return snapshot.NormalizedContent;
    }
}
