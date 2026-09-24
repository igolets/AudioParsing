namespace AudioParsing;

/// <summary>A single GigaAM-v3 model file to download.</summary>
/// <param name="FileName">File name on Hugging Face and on disk.</param>
/// <param name="DisplaySize">Human-readable size for confirmation prompts.</param>
public sealed record GigaAmModelFile(string FileName, string DisplaySize);

/// <summary>
/// Downloads the GigaAM-v3 sherpa-onnx model (transducer file set) from Hugging Face.
/// No third-party dependencies; plain <see cref="HttpClient"/> streaming.
/// </summary>
public static class GigaAmDownloader
{
    /// <summary>Hugging Face repository hosting the sherpa-onnx GigaAM-v3 files.</summary>
    public const string RepositoryUrl = "https://huggingface.co/Smirnov75/GigaAM-v3-sherpa-onnx";

    /// <summary>File name of the transducer encoder (e2e variant, with punctuation).</summary>
    public const string EncoderFileName = "gigaam_v3_e2e_rnnt_encoder.onnx";

    /// <summary>File name of the transducer decoder (e2e variant).</summary>
    public const string DecoderFileName = "gigaam_v3_e2e_rnnt_decoder.onnx";

    /// <summary>File name of the transducer joint network (e2e variant).</summary>
    public const string JoinerFileName = "gigaam_v3_e2e_rnnt_joint.onnx";

    /// <summary>File name of the tokens table (e2e variant).</summary>
    public const string TokensFileName = "gigaam_v3_e2e_rnnt_tokens.txt";

    /// <summary>Total download size for the confirmation prompt.</summary>
    public const string TotalSizeDisplay = "~890 MB";

    private const string ResolveBaseUrl = "https://huggingface.co/Smirnov75/GigaAM-v3-sherpa-onnx/resolve/main/";

    // Rough per-file weights for overall progress; the exact byte counts are not
    // pinned (model files may be re-uploaded), so the fraction is clamped and forced
    // to 1.0 at the end.
    private const long EncoderWeight = 885L * 1024L * 1024L;

    private const long DecoderWeight = 5L * 1024L * 1024L;

    private const long JoinerWeight = 3L * 1024L * 1024L;

    private const long TokensWeight = 1024L * 1024L;

    /// <summary>The four required files with display sizes.</summary>
    public static IReadOnlyList<GigaAmModelFile> RequiredFiles { get; } =
    [
        new(EncoderFileName, "~885 MB"),
        new(DecoderFileName, "~4.6 MB"),
        new(JoinerFileName, "~2.7 MB"),
        new(TokensFileName, "~13 KB"),
    ];

    /// <summary>
    /// Resolves a configured model path: rooted paths are used as-is, relative paths
    /// resolve against the application directory (next to the executable).
    /// </summary>
    public static string ResolveModelDirectory(string? modelPath)
    {
        string path = string.IsNullOrWhiteSpace(modelPath)
            ? new GigaAmSettings().ModelPath
            : modelPath;
        path = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }

    /// <summary>
    /// Points <paramref name="settings"/> at the downloaded (Hugging Face-named) file set.
    /// </summary>
    public static void ApplyDownloadedFileNames(GigaAmSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.EncoderFileName = EncoderFileName;
        settings.DecoderFileName = DecoderFileName;
        settings.JoinerFileName = JoinerFileName;
        settings.TokensFileName = TokensFileName;
    }

    /// <summary>
    /// Downloads the missing model files into <paramref name="targetDirectory"/>
    /// (created when absent). Files that already exist are skipped. Each file is
    /// streamed to a temporary sibling and moved into place, so interrupted downloads
    /// never leave a partial file behind. Reports overall 0.0-1.0 progress.
    /// </summary>
    public static async Task DownloadAsync(
        string targetDirectory,
        HttpClient httpClient,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("Target directory must not be empty.", nameof(targetDirectory));
        }

        ArgumentNullException.ThrowIfNull(httpClient);
        Directory.CreateDirectory(targetDirectory);

        long totalWeight = EncoderWeight + DecoderWeight + JoinerWeight + TokensWeight;
        long doneWeight = 0;
        for (int i = 0; i < RequiredFiles.Count; i++)
        {
            GigaAmModelFile file = RequiredFiles[i];
            long weight = WeightFor(file.FileName);
            string destination = Path.Combine(targetDirectory, file.FileName);
            if (File.Exists(destination))
            {
                doneWeight += weight;
                progress?.Report(Clamp((double)doneWeight / totalWeight));
                continue;
            }

            await DownloadFileAsync(
                httpClient,
                new Uri(ResolveBaseUrl + file.FileName, UriKind.Absolute),
                destination,
                doneWeight,
                weight,
                totalWeight,
                progress,
                cancellationToken).ConfigureAwait(false);
            doneWeight += weight;
            progress?.Report(Clamp((double)doneWeight / totalWeight));
        }

        progress?.Report(1.0);
    }

    private static async Task DownloadFileAsync(
        HttpClient httpClient,
        Uri url,
        string destination,
        long doneWeight,
        long weight,
        long totalWeight,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        string tempPath = destination + ".download";
        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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
            // as a proxy so progress keeps moving on large files.
            long expectedTotal = response.Content.Headers.ContentLength is long total && total > 0
                ? total
                : weight;
            byte[] buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                double fileFraction = Clamp((double)received / expectedTotal);
                progress?.Report(Clamp((doneWeight + (fileFraction * weight)) / totalWeight));
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

        File.Move(tempPath, destination, overwrite: false);
    }

    private static long WeightFor(string fileName)
    {
        if (fileName == EncoderFileName)
        {
            return EncoderWeight;
        }

        if (fileName == DecoderFileName)
        {
            return DecoderWeight;
        }

        if (fileName == JoinerFileName)
        {
            return JoinerWeight;
        }

        return TokensWeight;
    }

    private static double Clamp(double value) => Math.Min(1.0, Math.Max(0.0, value));
}
