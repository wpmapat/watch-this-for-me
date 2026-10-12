using System.Net;
using System.Text.RegularExpressions;

namespace WatchThisForMe.Infrastructure.Sources;

public static partial class HtmlNormalizer
{
    public static string Normalize(string html)
    {
        var withoutScripts = ScriptBlockRegex().Replace(html, string.Empty);
        var withoutStyles = StyleBlockRegex().Replace(withoutScripts, string.Empty);
        var withoutTags = TagRegex().Replace(withoutStyles, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = WhitespaceRegex().Replace(decoded, " ");

        return collapsed.Trim();
    }

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptBlockRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlockRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
