namespace AudioParsing;

/// <summary>Produces a summarizer response (summary text + trailing "Keywords: ..." line) from a transcript.</summary>
public interface ISummaryGenerator
{
    /// <summary>Short label used in progress messages (e.g. the model name or "GigaChat3.1-10B-A1.8B (local)").</summary>
    public string Name { get; }

    /// <summary>Summarizes a raw transcript. Never returns an empty string; throws on failure.</summary>
    public Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default);
}
