using System.Net;
using AudioParsing;
using AudioParsing.Win.Services;
using Xunit;

namespace AudioParsing.Win.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public void RoundTripPreservesValues()
    {
        string path = TempPath();
        try
        {
            JsonSettingsStore store = new(path);
            AppSettingsModel saved = new()
            {
                FfmpegPath = @"D:\tools\ffmpeg.exe",
                TranscriptionModel = "model-a",
                SummaryModel = "model-b",
            };

            store.Save(saved);
            AppSettingsModel loaded = store.Load();

            Assert.Equal(path, store.SettingsFilePath);
            Assert.Equal(saved.FfmpegPath, loaded.FfmpegPath);
            Assert.Equal(saved.TranscriptionModel, loaded.TranscriptionModel);
            Assert.Equal(saved.SummaryModel, loaded.SummaryModel);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void LoadReturnsDefaultsWhenFileIsMissing()
    {
        string path = TempPath();
        JsonSettingsStore store = new(path);

        AppSettingsModel loaded = store.Load();

        Assert.Equal(AppSettings.DefaultFfmpegPath, loaded.FfmpegPath);
        Assert.Equal(AudioPipeline.DefaultModel, loaded.TranscriptionModel);
        Assert.Equal(AudioPipeline.DefaultSummaryModel, loaded.SummaryModel);
    }

    [Fact]
    public void LoadReturnsDefaultsForMalformedJson()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, "{ not json");
            JsonSettingsStore store = new(path);

            AppSettingsModel loaded = store.Load();

            Assert.Equal(AppSettings.DefaultFfmpegPath, loaded.FfmpegPath);
            Assert.Equal(AudioPipeline.DefaultModel, loaded.TranscriptionModel);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void SaveWritesValidJson()
    {
        string path = TempPath();
        try
        {
            JsonSettingsStore store = new(path);

            store.Save(new AppSettingsModel { FfmpegPath = @"D:\tools\ffmpeg.exe" });

            string raw = File.ReadAllText(path);
            Assert.Contains("TranscriptionModel", raw, StringComparison.Ordinal);
            AppSettingsModel reloaded = store.Load();
            Assert.Equal(@"D:\tools\ffmpeg.exe", reloaded.FfmpegPath);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"winsettings-{Guid.NewGuid():N}.json");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}

public sealed class PipelineRunnerTests
{
    [Fact]
    public async Task ProcessAsyncThrowsWhenApiKeyIsMissing()
    {
        FakeSettingsStore store = new(new AppSettingsModel { FfmpegPath = @"C:\ffmpeg\ffmpeg.exe" });
        using StubHandler stub = new();
        using HttpClient httpClient = new(stub);
        Uri baseUri = new("https://example.test/v1");
        PipelineRunner runner = new(store, new FakeApiKeyProvider(null), key => new RouterAiClient(key, httpClient, baseUri));
        string[] files = new[] { @"C:\audio\a.mp3" };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.ProcessAsync(files, null, CancellationToken.None));

        Assert.Contains(EnvApiKeyProvider.EnvVarName, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task ProcessAsyncReportsStagesInOrder()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using StubHandler handler = new();
            using HttpClient httpClient = new(handler);
            Uri baseUri = new("https://example.test/v1");
            FakeSettingsStore store = new(new AppSettingsModel { FfmpegPath = @"C:\ffmpeg\ffmpeg.exe" }); PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider("test-key"),
                key => new RouterAiClient(key, httpClient, baseUri));
            List<PipelineStage> stages = new();
            Progress<PipelineProgress> progress = new(p => stages.Add(p.Stage));
            string[] files = new[] { audio };

            IReadOnlyList<AudioFileResult> results = await runner.ProcessAsync(files, progress, CancellationToken.None);
            await WaitForProgressAsync(() => stages.Count >= 4);

            AudioFileResult single = Assert.Single(results);
            Assert.True(single.Success);
            Assert.True(File.Exists(Path.ChangeExtension(audio, ".md")));
            List<PipelineStage> expected = new()
            {
                PipelineStage.Transcribing,
                PipelineStage.Summarizing,
                PipelineStage.Writing,
                PipelineStage.Completed,
            };
            Assert.Equal(expected, stages);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessAsyncRereadsSettingsOnEveryRun()
    {
        string root = CreateTempDirectory();
        try
        {
            string first = Path.Combine(root, "a.mp3");
            string second = Path.Combine(root, "b.mp3");
            await File.WriteAllBytesAsync(first, new byte[] { 1 });
            await File.WriteAllBytesAsync(second, new byte[] { 2 });
            using StubHandler handler = new();
            using HttpClient httpClient = new(handler);
            Uri baseUri = new("https://example.test/v1");
            FakeSettingsStore store = new(new AppSettingsModel
            {
                FfmpegPath = @"C:\ffmpeg\ffmpeg.exe",
                TranscriptionModel = "model-one",
            });
            PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider("test-key"),
                key => new RouterAiClient(key, httpClient, baseUri));
            string[] firstBatch = new[] { first };
            string[] secondBatch = new[] { second };

            await runner.ProcessAsync(firstBatch, null, CancellationToken.None);
            store.Model.TranscriptionModel = "model-two";
            await runner.ProcessAsync(secondBatch, null, CancellationToken.None);

            Assert.Contains("model-two", handler.LastTranscriptionBody, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessAsyncReportsFailedStageOnTranscriptionError()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using StubHandler handler = new();
            handler.FailTranscription = true;
            using HttpClient httpClient = new(handler);
            Uri baseUri = new("https://example.test/v1");
            FakeSettingsStore store = new(new AppSettingsModel { FfmpegPath = @"C:\ffmpeg\ffmpeg.exe" });
            PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider("test-key"),
                key => new RouterAiClient(key, httpClient, baseUri));
            List<PipelineStage> stages = new();
            Progress<PipelineProgress> progress = new(p => stages.Add(p.Stage));
            string[] files = new[] { audio };

            IReadOnlyList<AudioFileResult> results = await runner.ProcessAsync(files, progress, CancellationToken.None);
            await WaitForProgressAsync(() => stages.Count >= 2);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Success);
            Assert.Contains(PipelineStage.Failed, stages);
            Assert.False(File.Exists(Path.ChangeExtension(audio, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"audioparsing-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task ProcessAsyncSelectsLocalSummaryBackend()
    {
        FakeSettingsStore store = new(new AppSettingsModel
        {
            FfmpegPath = @"C:\ffmpeg\ffmpeg.exe",
            SummaryBackend = "Local",
            GigaChat = new GigaChatSettings
            {
                GgufPath = Path.Combine(Path.GetTempPath(), $"no-such-model-{Guid.NewGuid():N}.gguf"),
            },
        });
        using StubHandler stub = new();
        using HttpClient httpClient = new(stub);
        Uri baseUri = new("https://example.test/v1");
        PipelineRunner runner = new(
            store,
            new FakeApiKeyProvider("test-key"),
            key => new RouterAiClient(key, httpClient, baseUri));
        string[] files = new[] { @"C:\audio\a.mp3" };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.ProcessAsync(files, null, CancellationToken.None));

        Assert.Contains("GigaChat", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, stub.TranscriptionCallCount);
        Assert.Equal(0, stub.ChatCallCount);
    }

    [Fact]
    public async Task ProcessAsyncRunsFullyLocalWithoutApiKey()
    {
        string root = CreateTempDirectory();
        string modelDir = Path.Combine(Path.GetTempPath(), $"gigaam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelDir);
        string gguf = Path.Combine(Path.GetTempPath(), $"gigachat-{Guid.NewGuid():N}.gguf");
        try
        {
            foreach (string file in new[] { "gigaam_v3_e2e_rnnt_encoder.onnx", "gigaam_v3_e2e_rnnt_decoder.onnx", "gigaam_v3_e2e_rnnt_joint.onnx", "gigaam_v3_e2e_rnnt_tokens.txt" })
            {
                await File.WriteAllTextAsync(Path.Combine(modelDir, file), "dummy");
            }

            await File.WriteAllTextAsync(gguf, "dummy");
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using StubHandler handler = new();
            using HttpClient httpClient = new(handler);
            Uri baseUri = new("https://example.test/v1");
            FakeSettingsStore store = new(new AppSettingsModel
            {
                FfmpegPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
                TranscriptionBackend = "Local",
                SummaryBackend = "Local",
                GigaAm = new GigaAmSettings { ModelPath = modelDir },
                GigaChat = new GigaChatSettings { GgufPath = gguf },
            });
            PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider(null),
                key => new RouterAiClient(key, httpClient, baseUri));
            string[] files = new[] { audio };

            IReadOnlyList<AudioFileResult> results = await runner.ProcessAsync(files, null, CancellationToken.None);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Success);
            Assert.Contains("ffmpeg", single.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, handler.TranscriptionCallCount);
            Assert.Equal(0, handler.ChatCallCount);
            Assert.False(File.Exists(Path.ChangeExtension(audio, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(modelDir, true);
            File.Delete(gguf);
        }
    }

    [Fact]
    public async Task ProcessAsyncThrowsWithoutKeyWhenSummaryIsExternal()
    {
        string modelDir = Path.Combine(Path.GetTempPath(), $"gigaam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelDir);
        try
        {
            foreach (string file in new[] { "gigaam_v3_e2e_rnnt_encoder.onnx", "gigaam_v3_e2e_rnnt_decoder.onnx", "gigaam_v3_e2e_rnnt_joint.onnx", "gigaam_v3_e2e_rnnt_tokens.txt" })
            {
                await File.WriteAllTextAsync(Path.Combine(modelDir, file), "dummy");
            }

            FakeSettingsStore store = new(new AppSettingsModel
            {
                FfmpegPath = @"C:\ffmpeg\ffmpeg.exe",
                TranscriptionBackend = "Local",
                SummaryBackend = "External",
                GigaAm = new GigaAmSettings { ModelPath = modelDir },
            });
            using StubHandler stub = new();
            using HttpClient httpClient = new(stub);
            Uri baseUri = new("https://example.test/v1");
            PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider(null),
                key => new RouterAiClient(key, httpClient, baseUri));
            string[] files = new[] { @"C:\audio\a.mp3" };

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => runner.ProcessAsync(files, null, CancellationToken.None));

            Assert.Contains(EnvApiKeyProvider.EnvVarName, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public async Task ProcessAsyncThrowsWithoutKeyWhenTranscriptionIsExternal()
    {
        string gguf = Path.Combine(Path.GetTempPath(), $"gigachat-{Guid.NewGuid():N}.gguf");
        try
        {
            await File.WriteAllTextAsync(gguf, "dummy");
            FakeSettingsStore store = new(new AppSettingsModel
            {
                FfmpegPath = @"C:\ffmpeg\ffmpeg.exe",
                TranscriptionBackend = "External",
                SummaryBackend = "Local",
                GigaChat = new GigaChatSettings { GgufPath = gguf },
            });
            using StubHandler stub = new();
            using HttpClient httpClient = new(stub);
            Uri baseUri = new("https://example.test/v1");
            PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider(null),
                key => new RouterAiClient(key, httpClient, baseUri));
            string[] files = new[] { @"C:\audio\a.mp3" };

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => runner.ProcessAsync(files, null, CancellationToken.None));

            Assert.Contains(EnvApiKeyProvider.EnvVarName, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(gguf);
        }
    }

    [Fact]
    public async Task ProcessAsyncUsesLocalBackendPreprocessingWithoutExternalTranscription()
    {
        string root = CreateTempDirectory();
        string modelDir = Path.Combine(Path.GetTempPath(), $"gigaam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelDir);
        try
        {
            foreach (string file in new[] { "gigaam_v3_e2e_rnnt_encoder.onnx", "gigaam_v3_e2e_rnnt_decoder.onnx", "gigaam_v3_e2e_rnnt_joint.onnx", "gigaam_v3_e2e_rnnt_tokens.txt" })
            {
                await File.WriteAllTextAsync(Path.Combine(modelDir, file), "dummy");
            }

            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using StubHandler handler = new();
            using HttpClient httpClient = new(handler);
            Uri baseUri = new("https://example.test/v1");
            FakeSettingsStore store = new(new AppSettingsModel
            {
                FfmpegPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
                TranscriptionBackend = "Local",
                GigaAm = new GigaAmSettings { ModelPath = modelDir },
            });
            PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider("test-key"),
                key => new RouterAiClient(key, httpClient, baseUri));
            string[] files = new[] { audio };

            IReadOnlyList<AudioFileResult> results = await runner.ProcessAsync(files, null, CancellationToken.None);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Success);
            Assert.Contains("ffmpeg", single.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, handler.TranscriptionCallCount);
            Assert.False(File.Exists(Path.ChangeExtension(audio, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public async Task ProcessAsyncOmitsLanguageWhenAuto()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using StubHandler handler = new();
            using HttpClient httpClient = new(handler);
            Uri baseUri = new("https://example.test/v1");
            FakeSettingsStore store = new(new AppSettingsModel
            {
                FfmpegPath = @"C:\ffmpeg\ffmpeg.exe",
                Language = "auto",
            });
            PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider("test-key"),
                key => new RouterAiClient(key, httpClient, baseUri));
            string[] files = new[] { audio };

            IReadOnlyList<AudioFileResult> results = await runner.ProcessAsync(files, null, CancellationToken.None);

            AudioFileResult single = Assert.Single(results);
            Assert.True(single.Success);
            Assert.DoesNotContain("language", handler.LastTranscriptionBody, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessAsyncWithSkippedSummaryWritesTranscriptOnly()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using StubHandler handler = new();
            using HttpClient httpClient = new(handler);
            Uri baseUri = new("https://example.test/v1");
            FakeSettingsStore store = new(new AppSettingsModel
            {
                FfmpegPath = @"C:\ffmpeg\ffmpeg.exe",
                TranscriptionBackend = "External",
                SummaryBackend = "Skip",
            });
            PipelineRunner runner = new(
                store,
                new FakeApiKeyProvider("test-key"),
                key => new RouterAiClient(key, httpClient, baseUri));
            string[] files = new[] { audio };

            IReadOnlyList<AudioFileResult> results = await runner.ProcessAsync(files, null, CancellationToken.None);

            AudioFileResult single = Assert.Single(results);
            Assert.True(single.Success);
            Assert.Equal(1, handler.TranscriptionCallCount);
            Assert.Equal(0, handler.ChatCallCount);
            string markdown = await File.ReadAllTextAsync(Path.ChangeExtension(audio, ".md"));
            Assert.DoesNotContain("## Краткое содержание", markdown, StringComparison.Ordinal);
            Assert.Contains("## Полный транскрипт", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task WaitForProgressAsync(Func<bool> ready)
    {
        for (int i = 0; i < 200 && !ready(); i++)
        {
            await Task.Delay(10).ConfigureAwait(true);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string LastTranscriptionBody { get; private set; } = string.Empty;

        public int TranscriptionCallCount { get; private set; }

        public int ChatCallCount { get; private set; }

        public bool FailTranscription { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/audio/transcriptions", StringComparison.Ordinal))
            {
                TranscriptionCallCount++;
                if (request.Content is not null)
                {
                    LastTranscriptionBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
                }

                if (FailTranscription)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"error\":\"bad\"}"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"text\":\"recognized\"}"),
                };
            }

            ChatCallCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Summary line\\nKeywords: alpha\"}}]}"),
            };
        }
    }
}
