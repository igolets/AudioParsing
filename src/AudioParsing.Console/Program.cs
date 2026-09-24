namespace AudioParsing.Console;

internal static class Program
{
    private const string RouterAiCheckFlag = "--routerai-check";

    private const string RouterAiCheckModel = "qwen/qwen3.7-flash";

    private const string FolderFlag = "--folder";

    private const string LanguageFlag = "--language";

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

        string folder = ResolveFolder(args);
        string? language = ResolveLanguage(args);
        bool force = args.Contains(ForceFlag, StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(folder))
        {
            await System.Console.Error.WriteLineAsync($"Folder not found: {folder}").ConfigureAwait(false);
            return 1;
        }

        string ffmpegPath = AppSettings.GetFfmpegPath();
        string model = AppSettings.GetTranscriptionModel();
        string summaryModel = AppSettings.GetSummaryModel();

        try
        {
            using RouterAiClient client = RouterAiClient.FromEnvironment();
            AudioPipeline pipeline = new(client, ffmpegPath, model, language, summaryModel);
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

        return AudioPipeline.DefaultLanguage;
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
            + "  folder              Folder with audio and video files to scan (default: current directory)\n"
            + "  --folder <path>     Folder with audio and video files to scan\n"
            + "  --language <code>   Spoken language (default: ru, use 'auto' to omit)\n"
            + "  --force             Re-process even when the .md file exists\n"
            + "  --routerai-check    Verify RouterAI connectivity\n"
            + "  --help              Show this help";
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
