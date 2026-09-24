using Xunit;

namespace AudioParsing.Tests;

public sealed class AudioFileFinderTests
{
    [Fact]
    public void FindAudioFilesReturnsOnlySupportedFilesSorted()
    {
        string root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "b.mp3"), "x");
            File.WriteAllText(Path.Combine(root, "a.m4a"), "x");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "c.wav"), "x");
            File.WriteAllText(Path.Combine(sub, "photo.png"), "x");

            IReadOnlyList<string> files = AudioFileFinder.FindAudioFiles(root);

            Assert.Equal(3, files.Count);
            Assert.Equal(
                new[]
                {
                    Path.Combine(root, "a.m4a"),
                    Path.Combine(root, "b.mp3"),
                    Path.Combine(sub, "c.wav"),
                },
                files);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindAudioFilesDiscoversVideoAndExcludesUnsupported()
    {
        string root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.mkv"), "x");
            File.WriteAllText(Path.Combine(root, "b.mov"), "x");
            File.WriteAllText(Path.Combine(root, "c.mp4"), "x");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
            File.WriteAllText(Path.Combine(root, "clip.ts"), "x");

            IReadOnlyList<string> files = AudioFileFinder.FindAudioFiles(root);

            Assert.Equal(3, files.Count);
            Assert.Contains(Path.Combine(root, "a.mkv"), files);
            Assert.Contains(Path.Combine(root, "b.mov"), files);
            Assert.Contains(Path.Combine(root, "c.mp4"), files);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindAudioFilesThrowsForMissingFolder() => Assert.Throws<DirectoryNotFoundException>(
        () => AudioFileFinder.FindAudioFiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    [Fact]
    public void GetOpenFileDialogFilterCoversSupportedExtensions()
    {
        string filter = AudioFileFinder.GetOpenFileDialogFilter();

        Assert.StartsWith("Аудио и видео (", filter, StringComparison.Ordinal);
        Assert.Contains("*.mp3", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.mkv", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.m2ts", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Все файлы (*.*)|*.*", filter, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("song.mp3", true)]
    [InlineData("record.M4A", true)]
    [InlineData("voice.ogg", true)]
    [InlineData("clip.mp4", true)]
    [InlineData("clip.MP4", true)]
    [InlineData("clip.m4v", true)]
    [InlineData("clip.MKV", true)]
    [InlineData("clip.mov", true)]
    [InlineData("clip.AVI", true)]
    [InlineData("clip.webm", true)]
    [InlineData("clip.WEBM", true)]
    [InlineData("clip.wmv", true)]
    [InlineData("clip.flv", true)]
    [InlineData("clip.mpg", true)]
    [InlineData("clip.3gp", true)]
    [InlineData("clip.mts", true)]
    [InlineData("clip.m2ts", true)]
    [InlineData("clip.ts", false)]
    [InlineData("notes.txt", false)]
    [InlineData("noextension", false)]
    public void IsAudioFileMatchesExpectedExtensions(string fileName, bool expected) => Assert.Equal(expected, AudioFileFinder.IsAudioFile(fileName));

    [Theory]
    [InlineData("clip.mp4", true)]
    [InlineData("clip.MP4", true)]
    [InlineData("clip.m4v", true)]
    [InlineData("clip.mkv", true)]
    [InlineData("clip.MKV", true)]
    [InlineData("clip.mov", true)]
    [InlineData("clip.avi", true)]
    [InlineData("clip.webm", true)]
    [InlineData("clip.wmv", true)]
    [InlineData("clip.flv", true)]
    [InlineData("clip.mpg", true)]
    [InlineData("clip.3gp", true)]
    [InlineData("clip.mts", true)]
    [InlineData("clip.m2ts", true)]
    [InlineData("song.mp3", false)]
    [InlineData("voice.wav", false)]
    [InlineData("clip.mpeg", false)]
    [InlineData("clip.ts", false)]
    [InlineData("notes.txt", false)]
    public void IsVideoFileMatchesExpectedExtensions(string fileName, bool expected) => Assert.Equal(expected, AudioFileFinder.IsVideoFile(fileName));

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"audioparsing-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
