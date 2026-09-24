namespace AudioParsing;

/// <summary>Wraps RouterAI luna summarization behind <see cref="ISummaryGenerator"/>.</summary>
public sealed class RouterAiSummaryGenerator : ISummaryGenerator
{
    private readonly RouterAiClient _client;
    private readonly string _model;

    /// <summary>
    /// Creates an external summarizer over <paramref name="client"/> using the luna
    /// <paramref name="model"/>.
    /// </summary>
    public RouterAiSummaryGenerator(RouterAiClient client, string model)
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
    public Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default) =>
        _client.GenerateSummaryAsync(transcript, _model, cancellationToken);
}
