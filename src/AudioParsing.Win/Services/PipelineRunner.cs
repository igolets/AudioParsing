using AudioParsing.LocalStt;
using AudioParsing.LocalSummary;

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
        TranscriptionBackend backend = AppSettings.ParseTranscriptionBackend(settings.TranscriptionBackend);
        SummaryBackend summary = AppSettings.ParseSummaryBackend(settings.SummaryBackend);
        bool needsKey = backend == TranscriptionBackend.External || summary == SummaryBackend.External;
        string? key = _apiKey.GetApiKey();
        if (needsKey && string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"Не задан ключ API. Задайте переменную среды {EnvApiKeyProvider.EnvVarName}.");
        }

        string? language = string.IsNullOrWhiteSpace(settings.Language)
            || settings.Language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : settings.Language;

        using RouterAiClient? client = needsKey ? _clientFactory(key!) : null;
        using GigaAmTranscriber? localTranscriber = backend == TranscriptionBackend.Local
            ? new GigaAmTranscriber(settings.GigaAm)
            : null;
        using GigaChatSummaryGenerator? localSummary = summary == SummaryBackend.Local
            ? new GigaChatSummaryGenerator(settings.GigaChat)
            : null;
        IAudioTranscriber transcriber = localTranscriber is not null
            ? localTranscriber
            : new RouterAiTranscriber(client!, settings.TranscriptionModel);
        ISummaryGenerator summarizer = localSummary is not null
            ? new ChunkingSummaryGenerator(localSummary, settings.GigaChat.ContextSize, reservedOutputTokens: 2048)
            : new RouterAiSummaryGenerator(client!, settings.SummaryModel);
        AudioPipeline pipeline = new(transcriber, backend, summarizer, summary, settings.FfmpegPath, language);
        return await pipeline
            .ProcessFilesAsync(files, force: false, progress, cancellationToken)
            .ConfigureAwait(false);
    }
}
