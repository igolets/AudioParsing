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
}
