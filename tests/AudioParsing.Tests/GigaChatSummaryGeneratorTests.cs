using AudioParsing.LocalSummary;
using Xunit;

namespace AudioParsing.Tests;

public sealed class GigaChatSummaryGeneratorTests
{
    [Fact]
    public void ConstructorThrowsWhenGgufIsMissing()
    {
        GigaChatSettings settings = new()
        {
            GgufPath = Path.Combine(Path.GetTempPath(), $"no-such-model-{Guid.NewGuid():N}.gguf"),
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new GigaChatSummaryGenerator(settings));

        Assert.Contains(".gguf", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorThrowsOnNullSettings() =>
        Assert.Throws<ArgumentNullException>(() => new GigaChatSummaryGenerator(null!));

    [Fact]
    public void ConstructorThrowsOnNonPositiveContextSize()
    {
        string gguf = Path.GetTempFileName();
        try
        {
            GigaChatSettings settings = new() { GgufPath = gguf, ContextSize = 0 };

            Assert.Throws<ArgumentOutOfRangeException>(() => new GigaChatSummaryGenerator(settings));
        }
        finally
        {
            File.Delete(gguf);
        }
    }

    [Fact]
    public void ConstructorThrowsOnNegativeGpuLayerCount()
    {
        string gguf = Path.GetTempFileName();
        try
        {
            GigaChatSettings settings = new() { GgufPath = gguf, GpuLayerCount = -1 };

            Assert.Throws<ArgumentOutOfRangeException>(() => new GigaChatSummaryGenerator(settings));
        }
        finally
        {
            File.Delete(gguf);
        }
    }

    [Fact]
    public void ResolveGgufPathReturnsExistingRootedFile()
    {
        string gguf = Path.GetTempFileName();
        try
        {
            GigaChatSettings settings = new() { GgufPath = gguf };

            Assert.Equal(gguf, GigaChatSummaryGenerator.ResolveGgufPath(settings));
        }
        finally
        {
            File.Delete(gguf);
        }
    }

    [Fact]
    public void ResolveGgufPathThrowsWhenFileIsMissing()
    {
        GigaChatSettings settings = new()
        {
            GgufPath = Path.Combine(Path.GetTempPath(), $"no-such-model-{Guid.NewGuid():N}.gguf"),
        };

        Assert.Throws<InvalidOperationException>(() => GigaChatSummaryGenerator.ResolveGgufPath(settings));
    }

    [Fact]
    public void ResolveGgufPathThrowsOnNullSettings() =>
        Assert.Throws<ArgumentNullException>(() => GigaChatSummaryGenerator.ResolveGgufPath(null!));

    [Fact]
    public async Task GenerateSummaryAsyncThrowsOnEmptyTranscriptWithoutNativeLoad()
    {
        string gguf = Path.GetTempFileName();
        try
        {
            using GigaChatSummaryGenerator generator = new(new GigaChatSettings { GgufPath = gguf });

            await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateSummaryAsync("  "));
        }
        finally
        {
            File.Delete(gguf);
        }
    }
}
