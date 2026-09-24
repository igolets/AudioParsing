namespace AudioParsing;

/// <summary>
/// Finds supported audio and video files in a folder.
/// </summary>
public static class AudioFileFinder
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".mp4",
        ".mpeg",
        ".mpga",
        ".m4a",
        ".wav",
        ".webm",
        ".flac",
        ".ogg",
        ".opus",
        ".wma",
        ".aac",
    };

    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".m4v",
        ".mkv",
        ".mov",
        ".avi",
        ".webm",
        ".wmv",
        ".flv",
        ".mpg",
        ".3gp",
        ".mts",
        ".m2ts",
    };

    /// <summary>
    /// Returns all supported audio and video files under <paramref name="folder"/>, sorted by path.
    /// </summary>
    public static IReadOnlyList<string> FindAudioFiles(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("Folder must not be empty.", nameof(folder));
        }

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folder}");
        }

        List<string> files = Directory
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(IsAudioFile)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return files;
    }

    /// <summary>True when <paramref name="path"/> is a video container whose audio track must be extracted.</summary>
    public static bool IsVideoFile(string path) => SupportedVideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>True for any supported media file (audio or video).</summary>
    public static bool IsAudioFile(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path)) || IsVideoFile(path);
}
