using System.Diagnostics;

namespace AudioParsing;

/// <summary>
/// Compresses oversized audio files with an external ffmpeg executable.
/// </summary>
public static class FfmpegCompressor
{
    /// <summary>
    /// Builds ffmpeg arguments that extract/downmix to mono 16 kHz speech-friendly mp3,
    /// stripping any video stream (-vn).
    /// </summary>
    public static string BuildArguments(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path must not be empty.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));
        }

        return $"-y -i \"{inputPath}\" -vn -ac 1 -ar 16000 -b:a 32k \"{outputPath}\"";
    }

    /// <summary>
    /// Builds ffmpeg arguments that convert/extract to 16 kHz mono 16-bit PCM WAV (for GigaAM).
    /// </summary>
    public static string BuildPcmWavArguments(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path must not be empty.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));
        }

        return $"-y -i \"{inputPath}\" -vn -ac 1 -ar 16000 -c:a pcm_s16le \"{outputPath}\"";
    }

    public static bool NeedsCompression(long fileSizeBytes, long maxBytes) => fileSizeBytes > maxBytes;

    /// <summary>
    /// True when the file must be passed through ffmpeg before upload: video containers
    /// always (to drop the video stream and keep only audio), audio only above the upload limit.
    /// </summary>
    public static bool RequiresAudioExtraction(string path, long fileSizeBytes, long maxBytes)
        => AudioFileFinder.IsVideoFile(path) || NeedsCompression(fileSizeBytes, maxBytes);

    /// <summary>
    /// Runs ffmpeg to compress <paramref name="inputPath"/> into <paramref name="outputPath"/>.
    /// </summary>
    public static Task CompressAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        RunAsync(ffmpegPath, inputPath, outputPath, BuildArguments(inputPath, outputPath), cancellationToken);

    /// <summary>
    /// Runs ffmpeg with <see cref="BuildPcmWavArguments"/>.
    /// </summary>
    public static Task ExtractPcmWavAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        RunAsync(ffmpegPath, inputPath, outputPath, BuildPcmWavArguments(inputPath, outputPath), cancellationToken);

    private static async Task RunAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new ArgumentException("ffmpeg path must not be empty.", nameof(ffmpegPath));
        }

        if (!File.Exists(ffmpegPath))
        {
            throw new FileNotFoundException($"ffmpeg executable not found: {ffmpegPath}", ffmpegPath);
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input audio file not found: {inputPath}", inputPath);
        }

        ProcessStartInfo startInfo = new(ffmpegPath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        using Process process = new()
        {
            StartInfo = startInfo,
        };

        bool started = process.Start();
        if (!started)
        {
            throw new InvalidOperationException($"Failed to start ffmpeg: {ffmpegPath}");
        }

        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}. Details: {error}");
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException($"ffmpeg did not produce the output file: {outputPath}. Details: {error}");
        }
    }
}
