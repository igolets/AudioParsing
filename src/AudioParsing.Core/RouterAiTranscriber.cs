namespace AudioParsing;

/// <summary>Wraps the existing RouterAI Whisper transcription behind <see cref="IAudioTranscriber"/>.</summary>
public sealed class RouterAiTranscriber : IAudioTranscriber
{
    private readonly RouterAiClient _client;
    private readonly string _model;

    /// <summary>
    /// Creates an external transcriber over <paramref name="client"/> using the Whisper
    /// <paramref name="model"/>.
    /// </summary>
    public RouterAiTranscriber(RouterAiClient client, string model)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must not be empty.", nameof(model));
        }

        _model = model;
    }

    /// <inheritdoc />
    public string Name => _model;

    /// <inheritdoc />
    public Task<string> TranscribeAsync(
        string audioFilePath,
        string? language,
        CancellationToken cancellationToken = default) =>
        _client.TranscribeAsync(audioFilePath, _model, language, cancellationToken);
}
