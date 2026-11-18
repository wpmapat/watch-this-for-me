using System.Net;
using System.Text.RegularExpressions;

namespace WatchThisForMe.Infrastructure.Sources;

// Turns a page's HTML into plain text with ONE LINE PER BLOCK (heading, paragraph, list item,
// table row...). The line structure matters: TextDiffer compares line by line, so a page
// flattened onto a single line turns every change into "whole old page vs whole new page".
public static partial class HtmlNormalizer
{
    public static string Normalize(string html)
    {
        var withoutScripts = ScriptBlockRegex().Replace(html, string.Empty);
        var withoutStyles = StyleBlockRegex().Replace(withoutScripts, string.Empty);

        // Line breaks in the HTML source are just formatting - collapse them first, so lines in
        // the output come only from the page's block structure, not from how its HTML is indented.
        var singleSpaced = WhitespaceRegex().Replace(withoutStyles, " ");
        var withBlockBreaks = BlockTagRegex().Replace(singleSpaced, "\n");
        var withoutTags = TagRegex().Replace(withBlockBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);

        var lines = decoded
            .Split('\n')
            .Select(line => WhitespaceRegex().Replace(line, " ").Trim())
            .Where(line => line.Length > 0);

        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptBlockRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlockRegex();

    // Opening or closing tag of an element that starts a new line on screen.
    [GeneratedRegex(
        @"<\s*/?\s*(?:p|div|br|hr|h[1-6]|li|ul|ol|dl|dt|dd|tr|table|thead|tbody|tfoot|caption|section|article|header|footer|nav|main|aside|blockquote|pre|figure|figcaption|title|form|fieldset|details|summary|address)\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
