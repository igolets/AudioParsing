using System.Text.Json;

namespace AudioParsing;

/// <summary>
/// GigaAM-v3 ONNX model layout for the local transcription backend.
/// Defaults match the transducer (encoder/decoder/joiner + tokens) file set.
/// </summary>
public sealed class GigaAmSettings
{
    public string ModelPath { get; set; } = "models/gigaam-v3";

    public string EncoderFileName { get; set; } = "gigaam_v3_e2e_rnnt_encoder.onnx";

    public string DecoderFileName { get; set; } = "gigaam_v3_e2e_rnnt_decoder.onnx";

    public string JoinerFileName { get; set; } = "gigaam_v3_e2e_rnnt_joint.onnx";

    public string TokensFileName { get; set; } = "gigaam_v3_e2e_rnnt_tokens.txt";
}

/// <summary>
/// GigaChat GGUF model layout for the local summarization backend.
/// Defaults point at the official MIT-licensed quant next to the executable.
/// </summary>
public sealed class GigaChatSettings
{
    public string GgufPath { get; set; } = "models/GigaChat3.1-10B-A1.8B-q4_K_M.gguf";

    public int ContextSize { get; set; } = 16384;

    public int GpuLayerCount { get; set; }
}

/// <summary>
/// Editable settings model bound to appsettings.json.
/// Defaults mirror the console fallbacks so a missing file behaves identically.
/// </summary>
public sealed class AppSettingsModel
{
    public string FfmpegPath { get; set; } = AppSettings.DefaultFfmpegPath;

    public string TranscriptionModel { get; set; } = AudioPipeline.DefaultModel;

    public string SummaryModel { get; set; } = AudioPipeline.DefaultSummaryModel;

    public string TranscriptionBackend { get; set; } = "External";

    public string SummaryBackend { get; set; } = "External";

    public string Language { get; set; } = "ru";

    public GigaAmSettings GigaAm { get; set; } = new();

    public GigaChatSettings GigaChat { get; set; } = new();
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
                TranscriptionBackend = GetString(root, "TranscriptionBackend", "External"),
                SummaryBackend = GetString(root, "SummaryBackend", "External"),
                Language = GetString(root, "Language", "ru"),
                GigaAm = GetGigaAm(root),
                GigaChat = GetGigaChat(root),
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

    private static GigaAmSettings GetGigaAm(JsonElement root)
    {
        GigaAmSettings defaults = new();
        if (!root.TryGetProperty("GigaAm", out JsonElement element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return defaults;
        }

        return new GigaAmSettings
        {
            ModelPath = GetString(element, "ModelPath", defaults.ModelPath),
            EncoderFileName = GetString(element, "EncoderFileName", defaults.EncoderFileName),
            DecoderFileName = GetString(element, "DecoderFileName", defaults.DecoderFileName),
            JoinerFileName = GetString(element, "JoinerFileName", defaults.JoinerFileName),
            TokensFileName = GetString(element, "TokensFileName", defaults.TokensFileName),
        };
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

    private static GigaChatSettings GetGigaChat(JsonElement root)
    {
        GigaChatSettings defaults = new();
        if (!root.TryGetProperty("GigaChat", out JsonElement element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return defaults;
        }

        return new GigaChatSettings
        {
            GgufPath = GetString(element, "GgufPath", defaults.GgufPath),
            ContextSize = GetInt(element, "ContextSize", defaults.ContextSize),
            GpuLayerCount = GetInt(element, "GpuLayerCount", defaults.GpuLayerCount),
        };
    }

    private static int GetInt(JsonElement root, string property, int defaultValue)
    {
        if (root.TryGetProperty(property, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out int configured))
        {
            return configured;
        }

        return defaultValue;
    }
}
