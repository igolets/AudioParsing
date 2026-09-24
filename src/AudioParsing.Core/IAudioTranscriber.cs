namespace AudioParsing;

/// <summary>Produces raw transcript text from a prepared audio input.</summary>
public interface IAudioTranscriber
{
    /// <summary>Short label used in progress messages (e.g. model name or "GigaAM-v3 (local)").</summary>
    public string Name { get; }

    /// <summary>
    /// Transcribes <paramref name="audioFilePath"/> (already preprocessed for this backend).
    /// <paramref name="language"/> may be null ("auto"); the local backend ignores it.
    /// </summary>
    public Task<string> TranscribeAsync(
        string audioFilePath,
        string? language,
        CancellationToken cancellationToken = default);
}
