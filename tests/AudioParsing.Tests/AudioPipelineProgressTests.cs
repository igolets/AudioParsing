using System.Net;
using Xunit;

namespace AudioParsing.Tests;

public sealed class AudioPipelineProgressTests
{
    [Fact]
    public async Task ProcessFilesAsyncReturnsOneResultPerInput()
    {
        string root = CreateTempDirectory();
        try
        {
            string fresh = Path.Combine(root, "fresh.mp3");
            string existing = Path.Combine(root, "existing.mp3");
            await File.WriteAllBytesAsync(fresh, new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(existing, new byte[] { 4, 5, 6 });
            await File.WriteAllTextAsync(Path.ChangeExtension(existing, ".md"), "old");
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFilesAsync(new[] { fresh, existing });

            Assert.Equal(2, results.Count);
            Assert.False(results[0].Skipped);
            Assert.True(results[0].Success);
            Assert.True(results[1].Skipped);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFilesAsyncReportsSkippedProgressWhenMarkdownExists()
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
            List<PipelineProgress> events = new();
            Progress<PipelineProgress> progress = new(events.Add);

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFilesAsync(new[] { audio }, progress: progress);

            Assert.True(Assert.Single(results).Skipped);
            PipelineProgress single = Assert.Single(events);
            Assert.Equal(PipelineStage.Skipped, single.Stage);
            Assert.Equal(audio, single.AudioPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessFilesAsyncReportsStagesInOrder()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using CountingHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");
            List<PipelineStage> stages = new();
            Progress<PipelineProgress> progress = new(p => stages.Add(p.Stage));

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFilesAsync(new[] { audio }, progress: progress);

            Assert.True(Assert.Single(results).Success);
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
    public async Task ProcessFilesAsyncReportsFailedProgressWithoutAbortingBatch()
    {
        string root = CreateTempDirectory();
        try
        {
            string bad = Path.Combine(root, "bad.mp3");
            string good = Path.Combine(root, "good.mp3");
            await File.WriteAllBytesAsync(bad, new byte[] { 1 });
            await File.WriteAllBytesAsync(good, new byte[] { 2 });
            using CountingHandler handler = new() { FailTranscription = true };
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            AudioPipeline pipeline = new(client, @"C:\ffmpeg\ffmpeg.exe");
            List<PipelineProgress> events = new();
            Progress<PipelineProgress> progress = new(events.Add);

            IReadOnlyList<AudioFileResult> results = await pipeline.ProcessFilesAsync(new[] { bad, good }, progress: progress);

            Assert.Equal(2, results.Count);
            Assert.All(results, static r => Assert.False(r.Success));
            Assert.Equal(2, events.Count(static e => e.Stage == PipelineStage.Failed));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"audioparsing-progress-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public bool FailTranscription { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/audio/transcriptions", StringComparison.Ordinal))
            {
                if (FailTranscription)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"error\":\"bad\"}"),
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"text\":\"recognized\"}"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Summary line\\nKeywords: alpha\"}}]}"),
            });
        }
    }
}
