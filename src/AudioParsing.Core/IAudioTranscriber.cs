namespace AudioParsing;

/// <summary>Produces raw transcript text from a prepared audio input.</summary>
public interface IAudioTranscriber
{
    /// <summary>Short label used in progress messages (e.g. model name or "GigaAM-v3 (local)").</summary>
    public string Name { get; }

    /// <summary>
    /// Transcribes <paramref name="audioFilePath"/> (already preprocessed for this backend).
    /// <paramref name="language"/> may be null ("auto"); the local backend ignores it.
    /// Implementations that process audio in chunks (e.g. the local GigaAM backend)
    /// report fine-grained progress via <paramref name="progress"/>; single-shot
    /// backends may ignore it.
    /// </summary>
    public Task<string> TranscribeAsync(
        string audioFilePath,
        string? language,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
