namespace AudioParsing;

/// <summary>
/// Map-reduce decorator over an <see cref="ISummaryGenerator"/> for long transcripts.
/// If the transcript fits the context budget, forwards to the inner generator once.
/// Otherwise summarizes each chunk, joins the partial summaries, and summarizes the
/// join once more. Uses the same inner generator (hence the same system prompt) for
/// every pass, so the final response still ends with a <c>Keywords:</c> line and
/// <see cref="MarkdownDocument.SplitSummaryAndKeywords"/> needs no change.
/// Only applied to the local backend; the external path handles long input server-side.
/// </summary>
public sealed class ChunkingSummaryGenerator : ISummaryGenerator
{
    private readonly ISummaryGenerator _inner;
    private readonly int _contextSize;
    private readonly int _reservedOutputTokens;

    /// <summary>
    /// Creates a chunking decorator. <paramref name="contextSize"/> is the model context
    /// window in tokens; <paramref name="reservedOutputTokens"/> is kept free for the
    /// summary plus the system prompt overhead.
    /// </summary>
    public ChunkingSummaryGenerator(ISummaryGenerator inner, int contextSize, int reservedOutputTokens)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (contextSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextSize), "Context size must be positive.");
        }

        if (reservedOutputTokens < 0 || reservedOutputTokens >= contextSize)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedOutputTokens), "Reserved output tokens must be non-negative and smaller than the context size.");
        }

        _contextSize = contextSize;
        _reservedOutputTokens = reservedOutputTokens;
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public async Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        int maxInputTokens = _contextSize - _reservedOutputTokens - TranscriptChunker.EstimateTokens(RouterAiClient.SummarySystemPrompt);
        if (maxInputTokens <= 0)
        {
            throw new InvalidOperationException(
                $"Context size {_contextSize} leaves no input budget for the transcript " +
                $"after reserving {_reservedOutputTokens} output tokens.");
        }

        if (TranscriptChunker.EstimateTokens(transcript) <= maxInputTokens)
        {
            return await _inner.GenerateSummaryAsync(transcript, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<string> chunks = TranscriptChunker.Split(transcript, maxInputTokens);
        List<string> partials = new(chunks.Count);
        foreach (string chunk in chunks)
        {
            string partial = await _inner.GenerateSummaryAsync(chunk, cancellationToken).ConfigureAwait(false);
            partials.Add(partial);
        }

        string joined = string.Join("\n\n", partials);
        return await _inner.GenerateSummaryAsync(joined, cancellationToken).ConfigureAwait(false);
    }
}
