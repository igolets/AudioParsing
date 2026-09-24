namespace AudioParsing.Win.Services;

/// <summary>
/// Builds a <see cref="RouterAiClient"/> plus <see cref="AudioPipeline"/> from the
/// latest saved settings on every run, so settings-dialog edits take effect on the
/// next drop without restarting the app.
/// </summary>
public sealed class PipelineRunner : IPipelineRunner
{
    private readonly ISettingsStore _settings;
    private readonly IApiKeyProvider _apiKey;
    private readonly Func<string, RouterAiClient> _clientFactory;

    public PipelineRunner(ISettingsStore settings, IApiKeyProvider apiKey)
        : this(settings, apiKey, static key => new RouterAiClient(key))
    {
    }

    /// <summary>
    /// Test seam allowing a <see cref="RouterAiClient"/> over a stubbed HTTP handler.
    /// </summary>
    public PipelineRunner(ISettingsStore settings, IApiKeyProvider apiKey, Func<string, RouterAiClient> clientFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async Task<IReadOnlyList<AudioFileResult>> ProcessAsync(
        IReadOnlyList<string> files,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        AppSettingsModel settings = _settings.Load();
        string? key = _apiKey.GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"Не задан ключ API. Задайте переменную среды {EnvApiKeyProvider.EnvVarName}.");
        }

        using RouterAiClient client = _clientFactory(key);
        AudioPipeline pipeline = new(
            client,
            settings.FfmpegPath,
            settings.TranscriptionModel,
            AudioPipeline.DefaultLanguage,
            settings.SummaryModel);
        return await pipeline
            .ProcessFilesAsync(files, force: false, progress, cancellationToken)
            .ConfigureAwait(false);
    }
}
