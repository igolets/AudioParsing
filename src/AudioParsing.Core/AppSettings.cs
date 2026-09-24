namespace AudioParsing;

/// <summary>
/// Reads application settings from appsettings.json.
/// Delegates parsing to <see cref="AppSettingsFile"/> so the console and the
/// Win shell share a single implementation; this type stays as the read-only facade.
/// </summary>
public static class AppSettings
{
    public const string DefaultFfmpegPath = @"C:\Program Files (x86)\ffmpeg\ffmpeg.exe";

    /// <summary>
    /// Returns the configured ffmpeg path, falling back to <see cref="DefaultFfmpegPath"/>
    /// when the settings file or the property is missing.
    /// </summary>
    public static string GetFfmpegPath(string? settingsFilePath = null) =>
        AppSettingsFile.Load(settingsFilePath).FfmpegPath;

    /// <summary>
    /// Returns the configured transcription model, falling back to
    /// <see cref="AudioPipeline.DefaultModel"/> when the settings file or the property is missing.
    /// </summary>
    public static string GetTranscriptionModel(string? settingsFilePath = null) =>
        AppSettingsFile.Load(settingsFilePath).TranscriptionModel;

    /// <summary>
    /// Returns the configured summary model, falling back to
    /// <see cref="AudioPipeline.DefaultSummaryModel"/> when the settings file or the property is missing.
    /// </summary>
    public static string GetSummaryModel(string? settingsFilePath = null) =>
        AppSettingsFile.Load(settingsFilePath).SummaryModel;

    /// <summary>
    /// Returns the configured transcription backend, falling back to
    /// <see cref="TranscriptionBackend.External"/> for missing or unknown values (case-insensitive).
    /// </summary>
    public static TranscriptionBackend GetTranscriptionBackend(string? settingsFilePath = null) =>
        ParseTranscriptionBackend(AppSettingsFile.Load(settingsFilePath).TranscriptionBackend);

    /// <summary>
    /// Parses a backend name case-insensitively; unknown values fall back to
    /// <see cref="TranscriptionBackend.External"/>.
    /// </summary>
    public static TranscriptionBackend ParseTranscriptionBackend(string? value) =>
        string.Equals(value, "Local", StringComparison.OrdinalIgnoreCase)
            ? TranscriptionBackend.Local
            : TranscriptionBackend.External;

    /// <summary>
    /// Returns the configured summarization backend, falling back to
    /// <see cref="SummaryBackend.External"/> for missing or unknown values (case-insensitive).
    /// </summary>
    public static SummaryBackend GetSummaryBackend(string? settingsFilePath = null) =>
        ParseSummaryBackend(AppSettingsFile.Load(settingsFilePath).SummaryBackend);

    /// <summary>
    /// Parses a summary backend name case-insensitively; unknown values fall back to
    /// <see cref="SummaryBackend.External"/>.
    /// </summary>
    public static SummaryBackend ParseSummaryBackend(string? value) =>
        string.Equals(value, "Local", StringComparison.OrdinalIgnoreCase)
            ? SummaryBackend.Local
            : SummaryBackend.External;

    /// <summary>
    /// Returns the configured spoken language, or null when set to "auto" (parameter omitted).
    /// Falls back to <see cref="AudioPipeline.DefaultLanguage"/> when the settings file or
    /// the property is missing.
    /// </summary>
    public static string? GetLanguage(string? settingsFilePath = null)
    {
        string language = AppSettingsFile.Load(settingsFilePath).Language;
        if (string.IsNullOrWhiteSpace(language)
            || language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return language;
    }

    /// <summary>
    /// Returns the configured GigaAM-v3 model layout for the local backend,
    /// falling back to <see cref="GigaAmSettings"/> defaults when missing.
    /// </summary>
    public static GigaAmSettings GetGigaAmSettings(string? settingsFilePath = null) =>
        AppSettingsFile.Load(settingsFilePath).GigaAm;

    /// <summary>
    /// Returns the configured GigaChat GGUF layout for the local summarization backend,
    /// falling back to <see cref="GigaChatSettings"/> defaults when missing.
    /// </summary>
    public static GigaChatSettings GetGigaChatSettings(string? settingsFilePath = null) =>
        AppSettingsFile.Load(settingsFilePath).GigaChat;
}
