using System.Text.Json;
using AudioParsing.LocalStt;
using AudioParsing.LocalSummary;

namespace AudioParsing.Console;

internal static class Program
{
    private const string RouterAiCheckFlag = "--routerai-check";

    private const string RouterAiCheckModel = "qwen/qwen3.7-flash";

    private const string FolderFlag = "--folder";

    private const string LanguageFlag = "--language";

    private const string BackendFlag = "--backend";

    private const string GigaAmModelFlag = "--gigaam-model";

    private const string DownloadGigaAmFlag = "--download-gigaam";

    private const string SummaryBackendFlag = "--summary-backend";

    private const string GigaChatModelFlag = "--gigachat-model";

    private const string DownloadGigaChatFlag = "--download-gigachat";

    private const string ForceFlag = "--force";

    private const string HelpFlag = "--help";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains(RouterAiCheckFlag, StringComparer.OrdinalIgnoreCase))
        {
            return await RunRouterAiCheckAsync().ConfigureAwait(false);
        }

        if (args.Contains(HelpFlag, StringComparer.OrdinalIgnoreCase))
        {
            await System.Console.Out.WriteLineAsync(GetUsage()).ConfigureAwait(false);
            return 0;
        }

        if (args.Contains(DownloadGigaAmFlag, StringComparer.OrdinalIgnoreCase))
        {
            return await RunGigaAmDownloadAsync(args).ConfigureAwait(false);
        }

        if (args.Contains(DownloadGigaChatFlag, StringComparer.OrdinalIgnoreCase))
        {
            return await RunGigaChatDownloadAsync(args).ConfigureAwait(false);
        }

        string folder = ResolveFolder(args);
        string? language = ResolveLanguage(args) ?? AppSettings.GetLanguage();
        TranscriptionBackend? backendOverride = ResolveBackend(args);
        if (backendOverride is null && !string.IsNullOrWhiteSpace(ResolveOption(args, BackendFlag)))
        {
            await System.Console.Error.WriteLineAsync(
                $"Unknown {BackendFlag} value. Expected 'external' or 'local'.").ConfigureAwait(false);
            return 1;
        }

        SummaryBackend? summaryBackendOverride = ResolveSummaryBackend(args);
        if (summaryBackendOverride is null && !string.IsNullOrWhiteSpace(ResolveOption(args, SummaryBackendFlag)))
        {
            await System.Console.Error.WriteLineAsync(
                $"Unknown {SummaryBackendFlag} value. Expected 'external' or 'local'.").ConfigureAwait(false);
            return 1;
        }

        bool force = args.Contains(ForceFlag, StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(folder))
        {
            await System.Console.Error.WriteLineAsync($"Folder not found: {folder}").ConfigureAwait(false);
            return 1;
        }

        string ffmpegPath = AppSettings.GetFfmpegPath();
        string model = AppSettings.GetTranscriptionModel();
        string summaryModel = AppSettings.GetSummaryModel();
        TranscriptionBackend backend = backendOverride ?? AppSettings.GetTranscriptionBackend();
        SummaryBackend summaryBackend = summaryBackendOverride ?? AppSettings.GetSummaryBackend();
        GigaAmSettings gigaAm = AppSettings.GetGigaAmSettings();
        string? gigaAmOverride = ResolveOption(args, GigaAmModelFlag);
        if (!string.IsNullOrWhiteSpace(gigaAmOverride))
        {
            gigaAm.ModelPath = gigaAmOverride;
        }

        GigaChatSettings gigaChat = AppSettings.GetGigaChatSettings();
        string? gigaChatOverride = ResolveOption(args, GigaChatModelFlag);
        if (!string.IsNullOrWhiteSpace(gigaChatOverride))
        {
            gigaChat.GgufPath = NormalizeGigaChatModelPath(gigaChatOverride);
        }

        try
        {
            bool needsKey = backend == TranscriptionBackend.External || summaryBackend == SummaryBackend.External;
            using RouterAiClient? client = needsKey ? RouterAiClient.FromEnvironment() : null;
            using GigaAmTranscriber? localTranscriber = backend == TranscriptionBackend.Local
                ? new GigaAmTranscriber(gigaAm)
                : null;
            using GigaChatSummaryGenerator? localSummary = summaryBackend == SummaryBackend.Local
                ? new GigaChatSummaryGenerator(gigaChat)
                : null;
            IAudioTranscriber transcriber = localTranscriber is not null
                ? localTranscriber
                : new RouterAiTranscriber(client!, model);
            ISummaryGenerator summarizer = localSummary is not null
                ? new ChunkingSummaryGenerator(localSummary, gigaChat.ContextSize, reservedOutputTokens: 2048)
                : new RouterAiSummaryGenerator(client!, summaryModel);
            AudioPipeline pipeline = new(transcriber, backend, summarizer, summaryBackend, ffmpegPath, language);
            IReadOnlyList<AudioFileResult> results = await pipeline
                .ProcessFolderAsync(folder, force)
                .ConfigureAwait(false);

            if (results.Count == 0)
            {
                await System.Console.Out.WriteLineAsync($"No supported media files found in: {folder}").ConfigureAwait(false);
                return 0;
            }

            int failed = 0;
            foreach (AudioFileResult result in results)
            {
                if (result.Skipped)
                {
                    await System.Console.Out.WriteLineAsync($"[skip] {result.AudioPath} (markdown exists, use --force to redo)").ConfigureAwait(false);
                }
                else if (result.Success)
                {
                    await System.Console.Out.WriteLineAsync($"[ok] {result.AudioPath} -> {result.MarkdownPath}").ConfigureAwait(false);
                }
                else
                {
                    failed++;
                    await System.Console.Error.WriteLineAsync($"[fail] {result.AudioPath}: {result.Error}").ConfigureAwait(false);
                }
            }

            await System.Console.Out.WriteLineAsync($"Done: {results.Count - failed}/{results.Count} processed.").ConfigureAwait(false);
            return failed == 0 ? 0 : 1;
        }
        catch (HttpRequestException ex)
        {
            await System.Console.Error.WriteLineAsync($"AudioParsing.Console failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            await System.Console.Error.WriteLineAsync($"AudioParsing.Console failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await System.Console.Error.WriteLineAsync($"AudioParsing.Console failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            await System.Console.Error.WriteLineAsync($"AudioParsing.Console failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static string ResolveFolder(string[] args)
    {
        string? flagged = ResolveOption(args, FolderFlag);
        if (!string.IsNullOrWhiteSpace(flagged))
        {
            return flagged;
        }

        foreach (string arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                return arg;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? ResolveLanguage(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], LanguageFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    string value = args[i + 1].Trim();
                    if (value.Length == 0 || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    return value;
                }

                return AudioPipeline.DefaultLanguage;
            }
        }

        return null;
    }

    private static TranscriptionBackend? ResolveBackend(string[] args)
    {
        string? value = ResolveOption(args, BackendFlag);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value.Trim(), "Local", StringComparison.OrdinalIgnoreCase))
        {
            return TranscriptionBackend.Local;
        }

        if (string.Equals(value.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            return TranscriptionBackend.External;
        }

        return null;
    }

    private static SummaryBackend? ResolveSummaryBackend(string[] args)
    {
        string? value = ResolveOption(args, SummaryBackendFlag);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value.Trim(), "Local", StringComparison.OrdinalIgnoreCase))
        {
            return SummaryBackend.Local;
        }

        if (string.Equals(value.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            return SummaryBackend.External;
        }

        return null;
    }

    /// <summary>
    /// Normalizes a <c>--gigachat-model</c> value: directories get the default GGUF
    /// file name appended, file paths are used as-is.
    /// </summary>
    private static string NormalizeGigaChatModelPath(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.EndsWith(Path.DirectorySeparatorChar)
            || trimmed.EndsWith(Path.AltDirectorySeparatorChar)
            || Directory.Exists(trimmed))
        {
            return Path.Combine(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), GigaChatDownloader.GgufFileName);
        }

        return trimmed;
    }

    private static string? ResolveOption(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string GetUsage()
    {
        return "Usage: AudioParsing.Console [folder] [options]\n"
            + "Models and the ffmpeg path are read from appsettings.json.\n"
            + "ANTHROPIC_AUTH_TOKEN is required only when transcription or summarization\n"
            + "uses the external backend; local/local runs fully offline.\n"
            + "  folder              Folder with audio and video files to scan (default: current directory)\n"
            + "  --folder <path>     Folder with audio and video files to scan\n"
            + "  --language <code>   Spoken language (default: ru, use 'auto' to omit)\n"
            + "  --backend <name>    Transcription backend: external (RouterAI Whisper, default)\n"
            + "                      or local (on-device GigaAM-v3)\n"
            + "  --gigaam-model <path>  GigaAM-v3 model directory (local backend only)\n"
            + "  --download-gigaam   Download the GigaAM-v3 model from Hugging Face\n"
            + "                      (asks for confirmation, existing files are skipped)\n"
            + "  --summary-backend <name>  Summarization backend: external (RouterAI luna, default)\n"
            + "                      or local (on-device GigaChat GGUF)\n"
            + "  --gigachat-model <path>  GigaChat GGUF file path (local summary only;\n"
            + "                      a directory gets the default file name appended)\n"
            + "  --download-gigachat Download the GigaChat GGUF from Hugging Face\n"
            + "                      (asks for confirmation, existing files are skipped)\n"
            + "  --force             Re-process even when the .md file exists\n"
            + "  --routerai-check    Verify RouterAI connectivity\n"
            + "  --help              Show this help";
    }

    private static async Task<int> RunGigaAmDownloadAsync(string[] args)
    {
        AppSettingsModel settings = AppSettingsFile.Load();
        string? modelOverride = ResolveOption(args, GigaAmModelFlag);
        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            settings.GigaAm.ModelPath = modelOverride;
        }

        string targetDirectory = GigaAmDownloader.ResolveModelDirectory(settings.GigaAm.ModelPath);

        await System.Console.Out.WriteLineAsync(
            $"GigaAM-v3 model ({GigaAmDownloader.TotalSizeDisplay}) will be downloaded from:").ConfigureAwait(false);
        await System.Console.Out.WriteLineAsync($"  {GigaAmDownloader.RepositoryUrl}").ConfigureAwait(false);
        await System.Console.Out.WriteLineAsync("into:").ConfigureAwait(false);
        await System.Console.Out.WriteLineAsync($"  {targetDirectory}").ConfigureAwait(false);
        foreach (GigaAmModelFile file in GigaAmDownloader.RequiredFiles)
        {
            string marker = File.Exists(Path.Combine(targetDirectory, file.FileName)) ? "[exists] " : "[new]    ";
            await System.Console.Out.WriteLineAsync($"  {marker}{file.FileName} ({file.DisplaySize})").ConfigureAwait(false);
        }

        await System.Console.Out.WriteAsync("Proceed? [y/N]: ").ConfigureAwait(false);
        string? answer = await System.Console.In.ReadLineAsync().ConfigureAwait(false);
        if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
        {
            await System.Console.Out.WriteLineAsync("Download cancelled.").ConfigureAwait(false);
            return 0;
        }

        try
        {
            using HttpClient httpClient = new();
            int lastPercent = -1;
            Progress<double> progress = new(fraction =>
            {
                int percent = (int)Math.Round(fraction * 100);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    System.Console.Out.Write($"\rDownloading… {percent}%   ");
                }
            });
            await GigaAmDownloader
                .DownloadAsync(targetDirectory, httpClient, progress)
                .ConfigureAwait(false);
            await System.Console.Out.WriteLineAsync(string.Empty).ConfigureAwait(false);

            GigaAmDownloader.ApplyDownloadedFileNames(settings.GigaAm);
            AppSettingsFile.Save(settings);

            await System.Console.Out.WriteLineAsync(
                $"Done. Model saved to {targetDirectory} and referenced from {AppSettingsFile.ResolvePath()}.").ConfigureAwait(false);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            await System.Console.Error.WriteLineAsync($"Model download failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await System.Console.Error.WriteLineAsync($"Model download failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            await System.Console.Error.WriteLineAsync($"Model download failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (TaskCanceledException ex)
        {
            await System.Console.Error.WriteLineAsync($"Model download failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunGigaChatDownloadAsync(string[] args)
    {
        AppSettingsModel settings = AppSettingsFile.Load();
        string? modelOverride = ResolveOption(args, GigaChatModelFlag);
        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            settings.GigaChat.GgufPath = NormalizeGigaChatModelPath(modelOverride);
        }

        string targetFilePath = GigaChatDownloader.ResolveModelFilePath(settings.GigaChat.GgufPath);

        await System.Console.Out.WriteLineAsync(
            $"GigaChat3.1 GGUF model ({GigaChatDownloader.DisplaySize}) will be downloaded from:").ConfigureAwait(false);
        await System.Console.Out.WriteLineAsync($"  {GigaChatDownloader.RepositoryUrl}").ConfigureAwait(false);
        await System.Console.Out.WriteLineAsync("into:").ConfigureAwait(false);
        await System.Console.Out.WriteLineAsync($"  {targetFilePath}").ConfigureAwait(false);
        foreach (GigaChatModelFile file in GigaChatDownloader.RequiredFiles)
        {
            string marker = File.Exists(targetFilePath) ? "[exists] " : "[new]    ";
            await System.Console.Out.WriteLineAsync($"  {marker}{file.FileName} ({file.DisplaySize})").ConfigureAwait(false);
        }

        await System.Console.Out.WriteAsync("Proceed? [y/N]: ").ConfigureAwait(false);
        string? answer = await System.Console.In.ReadLineAsync().ConfigureAwait(false);
        if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
        {
            await System.Console.Out.WriteLineAsync("Download cancelled.").ConfigureAwait(false);
            return 0;
        }

        try
        {
            using HttpClient httpClient = new();
            int lastPercent = -1;
            Progress<double> progress = new(fraction =>
            {
                int percent = (int)Math.Round(fraction * 100);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    System.Console.Out.Write($"\rDownloading… {percent}%   ");
                }
            });
            await GigaChatDownloader
                .DownloadAsync(targetFilePath, httpClient, progress)
                .ConfigureAwait(false);
            await System.Console.Out.WriteLineAsync(string.Empty).ConfigureAwait(false);

            GigaChatDownloader.ApplyDownloadedFileName(settings.GigaChat);
            if (!SummaryBackendConfigured())
            {
                settings.SummaryBackend = "Local";
            }

            AppSettingsFile.Save(settings);

            await System.Console.Out.WriteLineAsync(
                $"Done. Model saved to {targetFilePath} and referenced from {AppSettingsFile.ResolvePath()}.").ConfigureAwait(false);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            await System.Console.Error.WriteLineAsync($"Model download failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await System.Console.Error.WriteLineAsync($"Model download failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            await System.Console.Error.WriteLineAsync($"Model download failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (TaskCanceledException ex)
        {
            await System.Console.Error.WriteLineAsync($"Model download failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// True when the settings file already carries an explicit
    /// <c>SummaryBackend</c> choice, so a model download must not flip behaviour.
    /// </summary>
    private static bool SummaryBackendConfigured()
    {
        string path = AppSettingsFile.ResolvePath();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("SummaryBackend", out _);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<int> RunRouterAiCheckAsync()
    {
        try
        {
            using RouterAiClient client = RouterAiClient.FromEnvironment();
            string reply = await client
                .GetChatCompletionAsync(RouterAiCheckModel, "Reply with the single word: ok")
                .ConfigureAwait(false);

            await System.Console.Out.WriteLineAsync($"RouterAI model '{RouterAiCheckModel}' replied: {reply}").ConfigureAwait(false);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            await System.Console.Error.WriteLineAsync($"RouterAI check failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            await System.Console.Error.WriteLineAsync($"RouterAI check failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (TaskCanceledException ex)
        {
            await System.Console.Error.WriteLineAsync($"RouterAI check failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
