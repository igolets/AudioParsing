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

    [Theory]
    [InlineData("Local", TranscriptionBackend.Local)]
    [InlineData("LOCAL", TranscriptionBackend.Local)]
    [InlineData("local", TranscriptionBackend.Local)]
    [InlineData("External", TranscriptionBackend.External)]
    [InlineData("EXTERNAL", TranscriptionBackend.External)]
    [InlineData("unknown", TranscriptionBackend.External)]
    [InlineData("", TranscriptionBackend.External)]
    public void GetTranscriptionBackendParsesCaseInsensitivelyWithExternalFallback(string configured, TranscriptionBackend expected)
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, $"{{ \"TranscriptionBackend\": \"{configured}\" }}");

            Assert.Equal(expected, AppSettings.GetTranscriptionBackend(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetTranscriptionBackendFallsBackToExternalWhenPropertyIsMissing()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Other\": 1 }");

            Assert.Equal(TranscriptionBackend.External, AppSettings.GetTranscriptionBackend(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetTranscriptionBackendFallsBackToExternalWhenFileIsMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");

        Assert.Equal(TranscriptionBackend.External, AppSettings.GetTranscriptionBackend(missing));
    }

    [Theory]
    [InlineData("Local", SummaryBackend.Local)]
    [InlineData("LOCAL", SummaryBackend.Local)]
    [InlineData("local", SummaryBackend.Local)]
    [InlineData("External", SummaryBackend.External)]
    [InlineData("EXTERNAL", SummaryBackend.External)]
    [InlineData("unknown", SummaryBackend.External)]
    [InlineData("", SummaryBackend.External)]
    public void GetSummaryBackendParsesCaseInsensitivelyWithExternalFallback(string configured, SummaryBackend expected)
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, $"{{ \"SummaryBackend\": \"{configured}\" }}");

            Assert.Equal(expected, AppSettings.GetSummaryBackend(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetSummaryBackendFallsBackToExternalWhenPropertyIsMissing()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Other\": 1 }");

            Assert.Equal(SummaryBackend.External, AppSettings.GetSummaryBackend(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetSummaryBackendFallsBackToExternalWhenFileIsMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");

        Assert.Equal(SummaryBackend.External, AppSettings.GetSummaryBackend(missing));
    }

    [Fact]
    public void GetLanguageReturnsConfiguredValue()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Language\": \"en\" }");

            Assert.Equal("en", AppSettings.GetLanguage(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("Auto")]
    public void GetLanguageReturnsNullForAuto(string configured)
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, $"{{ \"Language\": \"{configured}\" }}");

            Assert.Null(AppSettings.GetLanguage(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetLanguageFallsBackToDefaultWhenPropertyIsMissing()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Other\": 1 }");

            Assert.Equal(AudioPipeline.DefaultLanguage, AppSettings.GetLanguage(settings));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetGigaAmSettingsReturnsDefaultsWhenPropertyIsMissing()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Other\": 1 }");
            GigaAmSettings defaults = new();

            GigaAmSettings loaded = AppSettings.GetGigaAmSettings(settings);

            Assert.Equal(defaults.ModelPath, loaded.ModelPath);
            Assert.Equal(defaults.EncoderFileName, loaded.EncoderFileName);
            Assert.Equal(defaults.DecoderFileName, loaded.DecoderFileName);
            Assert.Equal(defaults.JoinerFileName, loaded.JoinerFileName);
            Assert.Equal(defaults.TokensFileName, loaded.TokensFileName);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetGigaAmSettingsReturnsConfiguredValues()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                settings,
                "{ \"GigaAm\": { \"ModelPath\": \"m\", \"EncoderFileName\": \"e.onnx\","
                + " \"DecoderFileName\": \"d.onnx\", \"JoinerFileName\": \"j.onnx\","
                + " \"TokensFileName\": \"t.txt\" } }");

            GigaAmSettings loaded = AppSettings.GetGigaAmSettings(settings);

            Assert.Equal("m", loaded.ModelPath);
            Assert.Equal("e.onnx", loaded.EncoderFileName);
            Assert.Equal("d.onnx", loaded.DecoderFileName);
            Assert.Equal("j.onnx", loaded.JoinerFileName);
            Assert.Equal("t.txt", loaded.TokensFileName);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetGigaAmSettingsMergesPartialOverridesWithDefaults()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"GigaAm\": { \"ModelPath\": \"custom\" } }");
            GigaAmSettings defaults = new();

            GigaAmSettings loaded = AppSettings.GetGigaAmSettings(settings);

            Assert.Equal("custom", loaded.ModelPath);
            Assert.Equal(defaults.EncoderFileName, loaded.EncoderFileName);
            Assert.Equal(defaults.TokensFileName, loaded.TokensFileName);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetGigaChatSettingsReturnsDefaultsWhenPropertyIsMissing()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"Other\": 1 }");
            GigaChatSettings defaults = new();

            GigaChatSettings loaded = AppSettings.GetGigaChatSettings(settings);

            Assert.Equal(defaults.GgufPath, loaded.GgufPath);
            Assert.Equal(defaults.ContextSize, loaded.ContextSize);
            Assert.Equal(defaults.GpuLayerCount, loaded.GpuLayerCount);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetGigaChatSettingsReturnsConfiguredValues()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                settings,
                "{ \"GigaChat\": { \"GgufPath\": \"m.gguf\", \"ContextSize\": 8192, \"GpuLayerCount\": 0 } }");

            GigaChatSettings loaded = AppSettings.GetGigaChatSettings(settings);

            Assert.Equal("m.gguf", loaded.GgufPath);
            Assert.Equal(8192, loaded.ContextSize);
            Assert.Equal(0, loaded.GpuLayerCount);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void GetGigaChatSettingsMergesPartialOverridesWithDefaults()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, "{ \"GigaChat\": { \"ContextSize\": 4096 } }");
            GigaChatSettings defaults = new();

            GigaChatSettings loaded = AppSettings.GetGigaChatSettings(settings);

            Assert.Equal(defaults.GgufPath, loaded.GgufPath);
            Assert.Equal(4096, loaded.ContextSize);
            Assert.Equal(defaults.GpuLayerCount, loaded.GpuLayerCount);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Theory]
    [InlineData("{ \"GigaChat\": { \"ContextSize\": \"big\" } }")]
    [InlineData("{ \"GigaChat\": { \"ContextSize\": 1.5 } }")]
    [InlineData("{ \"GigaChat\": { \"ContextSize\": null } }")]
    [InlineData("{ \"GigaChat\": { \"GpuLayerCount\": \"many\" } }")]
    public void GetGigaChatSettingsFallsBackToDefaultsForMalformedInts(string json)
    {
        string settings = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settings, json);
            GigaChatSettings defaults = new();

            GigaChatSettings loaded = AppSettings.GetGigaChatSettings(settings);

            Assert.Equal(defaults.ContextSize, loaded.ContextSize);
            Assert.Equal(defaults.GpuLayerCount, loaded.GpuLayerCount);
        }
        finally
        {
            File.Delete(settings);
        }
    }
}
