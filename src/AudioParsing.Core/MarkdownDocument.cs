using System.Globalization;
using System.Text;

namespace AudioParsing;

/// <summary>
/// Builds the output Markdown document from luna's summary and whisper's transcript.
/// </summary>
public static class MarkdownDocument
{
    private const string KeywordsMarker = "Keywords:";

    /// <summary>
    /// Splits luna's raw response into the summary text and the keywords
    /// from the trailing "Keywords: ..." marker line.
    /// </summary>
    public static (string Summary, IReadOnlyList<string> Keywords) SplitSummaryAndKeywords(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new ArgumentException("Response must not be empty.", nameof(response));
        }

        string[] lines = response.Split('\n');
        int markerIndex = -1;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Trim().Length == 0)
            {
                continue;
            }

            if (lines[i].Trim().StartsWith(KeywordsMarker, StringComparison.Ordinal))
            {
                markerIndex = i;
            }

            break;
        }

        if (markerIndex < 0)
        {
            return (response.Trim(), []);
        }

        List<string> keywords = lines[markerIndex]
            .Trim()
            .Substring(KeywordsMarker.Length)
            .Split(',')
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .ToList();

        string summary = string.Join('\n', lines, 0, markerIndex).Trim();
        return (summary, keywords);
    }

    /// <summary>
    /// Assembles the final Markdown document with YAML frontmatter,
    /// the summary section and the verbatim transcript section.
    /// </summary>
    public static string Build(
        string title,
        DateOnly date,
        IReadOnlyList<string> keywords,
        string summary,
        string transcript)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title must not be empty.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Summary must not be empty.", nameof(summary));
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new ArgumentException("Transcript must not be empty.", nameof(transcript));
        }

        StringBuilder builder = new();
        builder.AppendLine("---");
        builder.Append("title: \"").Append(EscapeYaml(title)).AppendLine("\"");
        builder.Append("date: ").AppendLine(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (keywords is not null && keywords.Count > 0)
        {
            builder.AppendLine("keywords:");
            foreach (string keyword in keywords)
            {
                builder.Append("  - ").AppendLine(keyword);
            }
        }

        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("## Краткое содержание");
        builder.AppendLine();
        builder.AppendLine(summary.Trim());
        builder.AppendLine();
        builder.AppendLine("## Полный транскрипт");
        builder.AppendLine();
        builder.AppendLine(transcript.Trim());
        return builder.ToString();
    }

    private static string EscapeYaml(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
