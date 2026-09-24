using System.Diagnostics;
using System.Net;
using Xunit;

namespace AudioParsing.Tests;

public sealed class AudioPipelineTests
{
    [Fact]
    public async Task ProcessFolderSkipsExistingMarkdownWithoutCallingApi()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            await File.WriteAllTextAsync(Path.ChangeExtension(audio, ".md"), "old");
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.True(single.Skipped);
            Assert.True(single.Success);
            Assert.Equal(0, handler.TranscriptionCallCount);
            Assert.Equal(0, handler.ChatCallCount);
            Assert.Equal("old", await File.ReadAllTextAsync(Path.ChangeExtension(audio, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderWritesMarkdownWithVerbatimTranscript()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "b lecture.m4a");
            await File.WriteAllBytesAsync(audio, new byte[] { 4, 5, 6 });
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Skipped);
            Assert.True(single.Success);
            Assert.Equal(1, handler.TranscriptionCallCount);
            Assert.Equal(1, handler.ChatCallCount);
            string markdown = await File.ReadAllTextAsync(Path.ChangeExtension(audio, ".md"));
            Assert.Contains("title: \"b lecture\"", markdown, StringComparison.Ordinal);
            Assert.Contains("  - alpha", markdown, StringComparison.Ordinal);
            Assert.Contains("## Краткое содержание", markdown, StringComparison.Ordinal);
            Assert.Contains("Summary line", markdown, StringComparison.Ordinal);
            Assert.Contains("## Полный транскрипт", markdown, StringComparison.Ordinal);
            Assert.Contains("recognized", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("Keywords:", markdown, StringComparison.Ordinal);
            Assert.Contains("recognized", handler.LastChatBody, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderForcesReprocessWhenRequested()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "c.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 7, 8, 9 });
            await File.WriteAllTextAsync(Path.ChangeExtension(audio, ".md"), "old");
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root, force: true);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Skipped);
            Assert.True(single.Success);
            Assert.Contains("## Полный транскрипт", await File.ReadAllTextAsync(Path.ChangeExtension(audio, ".md")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderReportsTranscriptionFailureWithoutMarkdown()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "d.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using CountingHandler handler = new() { FailTranscription = true };
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Success);
            Assert.NotNull(single.Error);
            Assert.Equal(0, handler.ChatCallCount);
            Assert.False(File.Exists(Path.ChangeExtension(audio, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderReportsSummaryFailureWithoutFile()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "e.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using CountingHandler handler = new() { FailChat = true };
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Success);
            Assert.NotNull(single.Error);
            Assert.False(File.Exists(Path.ChangeExtension(audio, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderSkipsVideoWithExistingMarkdownWithoutCallingApi()
    {
        string root = CreateTempDirectory();
        try
        {
            string video = Path.Combine(root, "lecture.mkv");
            await File.WriteAllBytesAsync(video, new byte[] { 1, 2, 3 });
            await File.WriteAllTextAsync(Path.ChangeExtension(video, ".md"), "old");
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.True(single.Skipped);
            Assert.True(single.Success);
            Assert.Equal(0, handler.TranscriptionCallCount);
            Assert.Equal(0, handler.ChatCallCount);
            Assert.Equal("old", await File.ReadAllTextAsync(Path.ChangeExtension(video, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderExtractsVideoAudioWithFfmpeg()
    {
        string? ffmpegPath = ResolveFfmpegPath();
        if (ffmpegPath is null)
        {
            return;
        }

        string root = CreateTempDirectory();
        try
        {
            string video = Path.Combine(root, "lecture.mp4");
            RunFfmpeg(
                ffmpegPath,
                $"-y -f lavfi -i \"testsrc=d=1:s=64x64:r=5\" -f lavfi -i \"sine=f=440:d=1\" -shortest -c:v mpeg4 -c:a aac \"{video}\"");
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, ffmpegPath);

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Skipped);
            Assert.True(single.Success);
            Assert.Equal(1, handler.TranscriptionCallCount);
            Assert.Equal(1, handler.ChatCallCount);
            string markdownPath = Path.ChangeExtension(video, ".md");
            Assert.True(File.Exists(markdownPath));
            string markdown = await File.ReadAllTextAsync(markdownPath);
            Assert.Contains("title: \"lecture\"", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderReportsVideoWithoutAudioTrackWithoutMarkdown()
    {
        string? ffmpegPath = ResolveFfmpegPath();
        if (ffmpegPath is null)
        {
            return;
        }

        string root = CreateTempDirectory();
        try
        {
            string video = Path.Combine(root, "silent.mp4");
            RunFfmpeg(
                ffmpegPath,
                $"-y -f lavfi -i \"testsrc=d=1:s=64x64:r=5\" -shortest -c:v mpeg4 -an \"{video}\"");
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, ffmpegPath);

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Success);
            Assert.NotNull(single.Error);
            Assert.Contains("does not contain any stream", single.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.ChangeExtension(video, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderWithLocalBackendReportsFailureWhenFfmpegIsMissing()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "f.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            FakeTranscriber transcriber = new();
            AudioPipeline pipeline = new(
                client,
                transcriber,
                TranscriptionBackend.Local,
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"));

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Success);
            Assert.NotNull(single.Error);
            Assert.Contains("ffmpeg", single.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, transcriber.CallCount);
            Assert.Equal(0, handler.ChatCallCount);
            Assert.False(File.Exists(Path.ChangeExtension(audio, ".md")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderWithLocalBackendTranscribesNormalizedWav()
    {
        string? ffmpegPath = ResolveFfmpegPath();
        if (ffmpegPath is null)
        {
            return;
        }

        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "local.wav");
            RunFfmpeg(
                ffmpegPath,
                $"-y -f lavfi -i \"sine=f=440:d=1\" -c:a pcm_s16le \"{audio}\"");
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            FakeTranscriber transcriber = new();
            AudioPipeline pipeline = new(
                client,
                transcriber,
                TranscriptionBackend.Local,
                ffmpegPath);
            SyncProgress progress = new();

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root, progress: progress);

            AudioFileResult single = Assert.Single(results);
            Assert.True(single.Success);
            string wavInput = Assert.Single(transcriber.ReceivedPaths);
            Assert.Equal(".wav", Path.GetExtension(wavInput));
            Assert.Contains(PipelineStage.Compressing, progress.Stages);
            Assert.Contains(PipelineStage.Transcribing, progress.Stages);
            Assert.Contains(PipelineStage.Completed, progress.Stages);
            string markdown = await File.ReadAllTextAsync(Path.ChangeExtension(audio, ".md"));
            Assert.Contains("## Краткое содержание", markdown, StringComparison.Ordinal);
            Assert.Contains("## Полный транскрипт", markdown, StringComparison.Ordinal);
            Assert.Contains("local transcript", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderWithExplicitSummarizerUsesItWithoutHttpClient()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "g.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            FakeTranscriber transcriber = new();
            FakeSummaryGenerator summarizer = new();
            AudioPipeline pipeline = new(
                transcriber,
                TranscriptionBackend.External,
                summarizer,
                SummaryBackend.Local,
                @"C:\ffmpeg\ffmpeg.exe");

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Skipped);
            Assert.True(single.Success);
            Assert.Equal(1, transcriber.CallCount);
            Assert.Equal(1, summarizer.CallCount);
            Assert.Equal("local transcript", summarizer.ReceivedTranscript);
            string markdown = await File.ReadAllTextAsync(Path.ChangeExtension(audio, ".md"));
            Assert.Contains("## Краткое содержание", markdown, StringComparison.Ordinal);
            Assert.Contains("Local summary text", markdown, StringComparison.Ordinal);
            Assert.Contains("## Полный транскрипт", markdown, StringComparison.Ordinal);
            Assert.Contains("local transcript", markdown, StringComparison.Ordinal);
            Assert.Contains("alpha", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("Keywords:", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFolderWithSkippedSummaryWritesTranscriptOnly()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "h.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            FakeTranscriber transcriber = new();
            FakeSummaryGenerator summarizer = new();
            AudioPipeline pipeline = new(
                transcriber,
                TranscriptionBackend.External,
                summarizer: null,
                SummaryBackend.Skip,
                @"C:\ffmpeg\ffmpeg.exe");
            SyncProgress progress = new();

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFolderAsync(root, progress: progress);

            AudioFileResult single = Assert.Single(results);
            Assert.False(single.Skipped);
            Assert.True(single.Success);
            Assert.Equal(1, transcriber.CallCount);
            Assert.Equal(0, summarizer.CallCount);
            string markdown = await File.ReadAllTextAsync(Path.ChangeExtension(audio, ".md"));
            Assert.DoesNotContain("## Краткое содержание", markdown, StringComparison.Ordinal);
            Assert.Contains("## Полный транскрипт", markdown, StringComparison.Ordinal);
            Assert.Contains("local transcript", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(PipelineStage.Summarizing, progress.Stages);
            Assert.Contains(PipelineStage.Completed, progress.Stages);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string? ResolveFfmpegPath()
    {
        try
        {
            string configured = AppSettings.GetFfmpegPath();
            if (File.Exists(configured))
            {
                return configured;
            }
        }
        catch (IOException)
        {
            // Fall through to PATH lookup.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall through to PATH lookup.
        }

        string fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (string dir in pathEnv.Split(Path.PathSeparator))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim(), fileName);
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void RunFfmpeg(string ffmpegPath, string arguments)
    {
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

        Assert.True(process.Start());
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"ffmpeg failed: {error}");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"audioparsing-pipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeTranscriber : IAudioTranscriber
    {
        public string Name => "fake-local";

        public List<string> ReceivedPaths { get; } = new();

        public int CallCount { get; private set; }

        public Task<string> TranscribeAsync(string audioFilePath, string? language, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(audioFilePath);

            CallCount++;
            ReceivedPaths.Add(audioFilePath);
            return Task.FromResult("local transcript");
        }
    }

    private sealed class FakeSummaryGenerator : ISummaryGenerator
    {
        public string Name => "fake-summary";

        public string? ReceivedTranscript { get; private set; }

        public int CallCount { get; private set; }

        public Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transcript);

            CallCount++;
            ReceivedTranscript = transcript;
            return Task.FromResult("Local summary text\nKeywords: alpha, beta");
        }
    }

    private sealed class SyncProgress : IProgress<PipelineProgress>
    {
        public List<PipelineStage> Stages { get; } = new();

        public void Report(PipelineProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);

            Stages.Add(value.Stage);
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int TranscriptionCallCount { get; private set; }

        public int ChatCallCount { get; private set; }

        public string? LastChatBody { get; private set; }

        public bool FailTranscription { get; set; }

        public bool FailChat { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/audio/transcriptions", StringComparison.Ordinal))
            {
                TranscriptionCallCount++;
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
            if (request.Content is not null)
            {
                LastChatBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            if (FailChat)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"bad\"}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Summary line\\n- point\\nKeywords: alpha\"}}]}"),
            };
        }
    }
}
