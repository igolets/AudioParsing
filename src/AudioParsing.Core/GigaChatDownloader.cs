namespace AudioParsing;

/// <summary>The GigaChat GGUF file to download.</summary>
/// <param name="FileName">File name on Hugging Face and on disk.</param>
/// <param name="DisplaySize">Human-readable size for confirmation prompts.</param>
public sealed record GigaChatModelFile(string FileName, string DisplaySize);

/// <summary>
/// Downloads the GigaChat3.1 GGUF from Hugging Face (official <c>ai-sage</c> MIT repository).
/// No third-party dependencies; plain <see cref="HttpClient"/> streaming.
/// Mirrors <see cref="GigaAmDownloader"/> for a single file instead of a file set.
/// </summary>
public static class GigaChatDownloader
{
    /// <summary>Official, MIT-licensed repository (ai-sage org).</summary>
    public const string RepositoryUrl = "https://huggingface.co/ai-sage/GigaChat3.1-10B-A1.8B-GGUF";

    /// <summary>Default quant (10B total / 1.8B active MoE; ctx 262144 upstream).</summary>
    public const string GgufFileName = "GigaChat3.1-10B-A1.8B-q4_K_M.gguf";

    /// <summary>Human-readable size for the confirmation prompt (~6 GB; verified against Content-Length at runtime).</summary>
    public const string DisplaySize = "~6 GB";

    private const string ResolveBaseUrl = "https://huggingface.co/ai-sage/GigaChat3.1-10B-A1.8B-GGUF/resolve/main/";

    // Rough weight for overall progress; the exact byte count is not pinned
    // (the file may be re-uploaded), so the fraction is clamped and forced to 1.0 at the end.
    private const long GgufWeight = 6L * 1024L * 1024L * 1024L;

    /// <summary>The single required GGUF file with its display size.</summary>
    public static IReadOnlyList<GigaChatModelFile> RequiredFiles { get; } =
    [
        new(GgufFileName, DisplaySize),
    ];

    /// <summary>
    /// Resolves a configured GGUF path: rooted paths are used as-is, relative paths
    /// resolve against the application directory (next to the executable).
    /// </summary>
    public static string ResolveModelFilePath(string? ggufPath)
    {
        string path = string.IsNullOrWhiteSpace(ggufPath)
            ? new GigaChatSettings().GgufPath
            : ggufPath;
        path = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }

    /// <summary>
    /// Points <paramref name="settings"/> at the downloaded (Hugging Face-named) file.
    /// File paths are left untouched (the download wrote exactly there); a missing path
    /// falls back to the default, and a directory path gets the file name appended.
    /// </summary>
    public static void ApplyDownloadedFileName(GigaChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string path = (settings.GgufPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(path))
        {
            settings.GgufPath = new GigaChatSettings().GgufPath;
        }
        else if (path.EndsWith(Path.DirectorySeparatorChar) || Directory.Exists(path))
        {
            settings.GgufPath = Path.Combine(path.TrimEnd(Path.DirectorySeparatorChar), GgufFileName);
        }
    }

    /// <summary>
    /// Streams the GGUF into <paramref name="targetFilePath"/> (parent directories created).
    /// An existing file is skipped. Writes to a sibling ".download" temp and moves it into
    /// place, so an interrupted download never leaves a partial model behind.
    /// Reports overall 0.0-1.0 progress.
    /// </summary>
    public static async Task DownloadAsync(
        string targetFilePath,
        HttpClient httpClient,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            throw new ArgumentException("Target file path must not be empty.", nameof(targetFilePath));
        }

        ArgumentNullException.ThrowIfNull(httpClient);

        if (File.Exists(targetFilePath))
        {
            progress?.Report(1.0);
            return;
        }

        string? directory = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = targetFilePath + ".download";
        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(new Uri(ResolveBaseUrl + GgufFileName, UriKind.Absolute), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using Stream source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using FileStream target = new(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            // The server-reported length when available, otherwise the rough weight
            // as a proxy so progress keeps moving on a large file.
            long expectedTotal = response.Content.Headers.ContentLength is long total && total > 0
                ? total
                : GgufWeight;
            byte[] buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report(Clamp((double)received / expectedTotal));
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        File.Move(tempPath, targetFilePath, overwrite: false);
        progress?.Report(1.0);
    }

    private static double Clamp(double value) => Math.Min(1.0, Math.Max(0.0, value));
}
