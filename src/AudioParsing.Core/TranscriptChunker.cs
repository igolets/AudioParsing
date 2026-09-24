namespace AudioParsing;

/// <summary>
/// Pure helper: splits a transcript into segments that fit a token budget.
/// Token counts use the <c>chars/3</c> heuristic (see <see cref="EstimateTokens"/>);
/// deliberately approximate, deterministic, and dependency-free.
/// </summary>
public static class TranscriptChunker
{
    /// <summary>
    /// Heuristic token estimate (ceiling of <c>chars/3</c>). Deliberately approximate;
    /// only used to keep local-model input inside <c>ContextSize</c>.
    /// </summary>
    public static int EstimateTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return 0;
        }

        return Math.Max(1, (text.Length + 2) / 3);
    }

    /// <summary>
    /// Splits <paramref name="transcript"/> into segments of at most
    /// <paramref name="maxTokensPerSegment"/> estimated tokens. Splits on blank lines
    /// first, then on sentence boundaries, then on a hard character count, so no
    /// segment exceeds the budget. Deterministic: the same input always yields the
    /// same segments. Never returns an empty list.
    /// </summary>
    public static IReadOnlyList<string> Split(string transcript, int maxTokensPerSegment)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        if (maxTokensPerSegment <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokensPerSegment), "Token budget must be positive.");
        }

        if (transcript.Length == 0)
        {
            return [transcript];
        }

        int maxChars = maxTokensPerSegment * 3;

        List<string> units = new();
        foreach (string paragraph in SplitParagraphs(transcript))
        {
            if (paragraph.Length <= maxChars)
            {
                units.Add(paragraph);
            }
            else
            {
                foreach (string sentence in SplitSentences(paragraph))
                {
                    if (sentence.Length <= maxChars)
                    {
                        units.Add(sentence);
                    }
                    else
                    {
                        units.AddRange(SplitHard(sentence, maxChars));
                    }
                }
            }
        }

        if (units.Count == 0)
        {
            return [transcript];
        }

        List<string> segments = new();
        string? current = null;
        foreach (string unit in units)
        {
            if (current is null)
            {
                current = unit;
            }
            else if (current.Length + 2 + unit.Length <= maxChars)
            {
                current = current + "\n\n" + unit;
            }
            else
            {
                segments.Add(current);
                current = unit;
            }
        }

        if (current is not null)
        {
            segments.Add(current);
        }

        return segments;
    }

    private static string[] SplitParagraphs(string transcript)
    {
        string normalized = transcript
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return normalized.Split(
            "\n\n",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> SplitSentences(string paragraph)
    {
        int start = 0;
        for (int i = 0; i < paragraph.Length; i++)
        {
            char c = paragraph[i];
            bool isTerminator = c is '.' or '!' or '?' or '…';
            bool atNewline = c == '\n';
            bool boundary = atNewline
                || (isTerminator && (i + 1 == paragraph.Length || char.IsWhiteSpace(paragraph[i + 1])));
            if (boundary)
            {
                string piece = paragraph.Substring(start, i - start + 1).Trim();
                if (piece.Length > 0)
                {
                    yield return piece;
                }

                start = i + 1;
            }
        }

        if (start < paragraph.Length)
        {
            string tail = paragraph.Substring(start).Trim();
            if (tail.Length > 0)
            {
                yield return tail;
            }
        }
    }

    private static IEnumerable<string> SplitHard(string text, int maxChars)
    {
        for (int i = 0; i < text.Length; i += maxChars)
        {
            int take = Math.Min(maxChars, text.Length - i);
            string part = text.Substring(i, take).Trim();
            if (part.Length > 0)
            {
                yield return part;
            }
        }
    }
}
