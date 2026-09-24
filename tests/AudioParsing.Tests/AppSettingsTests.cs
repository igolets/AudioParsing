using Xunit;

namespace AudioParsing.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void GetFfmpegPathReturnsConfiguredValue()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"FfmpegPath\": \"D:\\\\tools\\\\ffmpeg.exe\" }");

            Assert.Equal(@"D:\tools\ffmpeg.exe", AppSettings.GetFfmpegPath(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetFfmpegPathFallsBackToDefaultWhenFileIsMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");

        Assert.Equal(AppSettings.DefaultFfmpegPath, AppSettings.GetFfmpegPath(missing));
    }

    [Fact]
    public void GetFfmpegPathFallsBackToDefaultWhenPropertyIsMissing()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Other\": 1 }");

            Assert.Equal(AppSettings.DefaultFfmpegPath, AppSettings.GetFfmpegPath(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetTranscriptionModelReturnsConfiguredValue()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"TranscriptionModel\": \"openai/whisper-large-v3\" }");

            Assert.Equal("openai/whisper-large-v3", AppSettings.GetTranscriptionModel(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetTranscriptionModelFallsBackToDefaultWhenPropertyIsMissing()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Other\": 1 }");

            Assert.Equal(AudioPipeline.DefaultModel, AppSettings.GetTranscriptionModel(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetSummaryModelReturnsConfiguredValue()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"SummaryModel\": \"openai/gpt-4o\" }");

            Assert.Equal("openai/gpt-4o", AppSettings.GetSummaryModel(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetSummaryModelFallsBackToDefaultWhenPropertyIsMissing()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Other\": 1 }");

            Assert.Equal(AudioPipeline.DefaultSummaryModel, AppSettings.GetSummaryModel(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }
}
