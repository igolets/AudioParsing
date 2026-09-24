using System.Text.Json;

namespace AudioParsing;

/// <summary>
/// Editable settings model bound to appsettings.json.
/// Defaults mirror the console fallbacks so a missing file behaves identically.
/// </summary>
public sealed class AppSettingsModel
{
    public string FfmpegPath { get; set; } = AppSettings.DefaultFfmpegPath;

    public string TranscriptionModel { get; set; } = AudioPipeline.DefaultModel;

    public string SummaryModel { get; set; } = AudioPipeline.DefaultSummaryModel;
}

/// <summary>
/// Single JSON parser for appsettings.json. <see cref="AppSettings"/> delegates here
/// so the console (read-only) and the Win shell (read/write) share one implementation.
/// </summary>
public static class AppSettingsFile
{
    private const string SettingsFileName = "appsettings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Resolves the settings path: <paramref name="overridePath"/> when given,
    /// otherwise the file next to the app, falling back to the current directory.
    /// </summary>
    public static string ResolvePath(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        string nextToApp = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (File.Exists(nextToApp))
        {
            return nextToApp;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), SettingsFileName);
    }

    /// <summary>
    /// Loads settings, falling back to defaults when the file is missing,
    /// malformed, or individual properties are absent.
    /// </summary>
    public static AppSettingsModel Load(string? path = null)
    {
        string resolved = ResolvePath(path);
        if (!File.Exists(resolved))
        {
            return new AppSettingsModel();
        }

        try
        {
            using FileStream stream = File.OpenRead(resolved);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            return new AppSettingsModel
            {
                FfmpegPath = GetString(root, "FfmpegPath", AppSettings.DefaultFfmpegPath),
                TranscriptionModel = GetString(root, "TranscriptionModel", AudioPipeline.DefaultModel),
                SummaryModel = GetString(root, "SummaryModel", AudioPipeline.DefaultSummaryModel),
            };
        }
        catch (JsonException)
        {
            return new AppSettingsModel();
        }
        catch (IOException)
        {
            return new AppSettingsModel();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettingsModel();
        }
    }

    /// <summary>
    /// Saves settings atomically via temp-file write plus move to avoid partial writes.
    /// </summary>
    public static void Save(AppSettingsModel model, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        string resolved = ResolvePath(path);
        string? directory = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string payload = JsonSerializer.Serialize(model, SerializerOptions);
        string tempPath = resolved + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, payload);
            File.Move(tempPath, resolved, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string GetString(JsonElement root, string property, string defaultValue)
    {
        if (root.TryGetProperty(property, out JsonElement element)
            && element.ValueKind == JsonValueKind.String)
        {
            string? configured = element.GetString();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
        }

        return defaultValue;
    }
}
