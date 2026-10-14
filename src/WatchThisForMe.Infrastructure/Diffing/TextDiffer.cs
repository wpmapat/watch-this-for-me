using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace WatchThisForMe.Infrastructure.Diffing;

public static class TextDiffer
{
    private const int MaxDiffLines = 50;
    private const int MaxCharacters = 4000;

    public static string Summarize(string previous, string current)
    {
        var diff = InlineDiffBuilder.Diff(previous, current);
        var changedLines = diff.Lines
            .Where(line => line.Type is ChangeType.Inserted or ChangeType.Deleted)
            .ToList();

        if (changedLines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        for (var i = 0; i < changedLines.Count; i++)
        {
            if (i >= MaxDiffLines)
            {
                builder.AppendLine($"... ({changedLines.Count - i} more changed lines omitted)");
                break;
            }

            var prefix = changedLines[i].Type == ChangeType.Inserted ? "+" : "-";
            builder.AppendLine($"{prefix} {changedLines[i].Text}");
        }

        var result = builder.ToString().TrimEnd();
        return result.Length > MaxCharacters ? result[..MaxCharacters] + "... (truncated)" : result;
    }
}
