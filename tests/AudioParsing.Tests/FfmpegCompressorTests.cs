using Xunit;

namespace AudioParsing.Tests;

public sealed class FfmpegCompressorTests
{
    [Fact]
    public void BuildArgumentsDownmixesToMonoSpeechMp3()
    {
        string args = FfmpegCompressor.BuildArguments(@"C:\in\record.m4a", @"C:\out\record.mp3");

        Assert.Contains("-y", args, StringComparison.Ordinal);
        Assert.Contains("-vn", args, StringComparison.Ordinal);
        Assert.Contains("-ac 1", args, StringComparison.Ordinal);
        Assert.Contains("-ar 16000", args, StringComparison.Ordinal);
        Assert.Contains(@"C:\in\record.m4a", args, StringComparison.Ordinal);
        Assert.Contains(@"C:\out\record.mp3", args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(25L * 1024L * 1024L, false)]
    [InlineData((25L * 1024L * 1024L) + 1L, true)]
    [InlineData(0L, false)]
    public void NeedsCompressionComparesAgainstLimit(long size, bool expected) => Assert.Equal(expected, FfmpegCompressor.NeedsCompression(size, AudioPipeline.MaxUploadBytes));

    [Theory]
    [InlineData("clip.mkv", 1024L, true)]
    [InlineData("clip.mkv", 26L * 1024L * 1024L, true)]
    [InlineData("clip.mp4", 1024L, true)]
    [InlineData("clip.webm", 1024L, true)]
    [InlineData("song.mp3", 1024L, false)]
    [InlineData("song.mp3", 25L * 1024L * 1024L, false)]
    [InlineData("song.mp3", (25L * 1024L * 1024L) + 1L, true)]
    public void RequiresAudioExtractionMatchesExpected(string fileName, long size, bool expected) => Assert.Equal(
        expected,
        FfmpegCompressor.RequiresAudioExtraction(fileName, size, AudioPipeline.MaxUploadBytes));

    [Fact]
    public async Task CompressAsyncThrowsWhenFfmpegIsMissing() => await Assert.ThrowsAsync<FileNotFoundException>(
        () => FfmpegCompressor.CompressAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
            "input.mp3",
            "output.mp3"));
}
