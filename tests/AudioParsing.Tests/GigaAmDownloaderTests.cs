using System.Net;
using Xunit;

namespace AudioParsing.Tests;

public sealed class GigaAmDownloaderTests
{
    [Fact]
    public void RequiredFilesListsFourTransducerFiles()
    {
        Assert.Equal(4, GigaAmDownloader.RequiredFiles.Count);
        Assert.Contains(GigaAmDownloader.RequiredFiles, f => f.FileName == GigaAmDownloader.EncoderFileName);
        Assert.Contains(GigaAmDownloader.RequiredFiles, f => f.FileName == GigaAmDownloader.DecoderFileName);
        Assert.Contains(GigaAmDownloader.RequiredFiles, f => f.FileName == GigaAmDownloader.JoinerFileName);
        Assert.Contains(GigaAmDownloader.RequiredFiles, f => f.FileName == GigaAmDownloader.TokensFileName);
    }

    [Fact]
    public void ResolveModelDirectoryPassesThroughRootedPaths()
    {
        string rooted = Path.Combine(Path.GetTempPath(), "models");

        Assert.Equal(rooted, GigaAmDownloader.ResolveModelDirectory(rooted));
    }

    [Fact]
    public void ResolveModelDirectoryResolvesRelativePathsAgainstAppDirectory()
    {
        string resolved = GigaAmDownloader.ResolveModelDirectory("models/gigaam-v3");

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "models", "gigaam-v3"),
            resolved);
    }

    [Fact]
    public void ApplyDownloadedFileNamesPointsAtHuggingFaceNames()
    {
        GigaAmSettings settings = new();

        GigaAmDownloader.ApplyDownloadedFileNames(settings);

        Assert.Equal(GigaAmDownloader.EncoderFileName, settings.EncoderFileName);
        Assert.Equal(GigaAmDownloader.DecoderFileName, settings.DecoderFileName);
        Assert.Equal(GigaAmDownloader.JoinerFileName, settings.JoinerFileName);
        Assert.Equal(GigaAmDownloader.TokensFileName, settings.TokensFileName);
    }

    [Fact]
    public async Task DownloadAsyncWritesAllFilesAndReportsCompletion()
    {
        string target = Path.Combine(Path.GetTempPath(), $"gigaam-dl-{Guid.NewGuid():N}");
        try
        {
            using ModelStubHandler handler = new();
            using HttpClient httpClient = new(handler);
            List<double> reported = new();
            Progress<double> progress = new(reported.Add);

            await GigaAmDownloader.DownloadAsync(target, httpClient, progress);

            foreach (GigaAmModelFile file in GigaAmDownloader.RequiredFiles)
            {
                string path = Path.Combine(target, file.FileName);
                Assert.True(File.Exists(path));
                Assert.Equal($"content-of-{file.FileName}", await File.ReadAllTextAsync(path));
                Assert.False(File.Exists(path + ".download"));
            }

            Assert.Equal(4, handler.RequestedPaths.Count);
            Assert.NotEmpty(reported);
            Assert.Equal(1.0, reported[^1]);
            Assert.True(reported.All(p => p is >= 0.0 and <= 1.0));
        }
        finally
        {
            Directory.Delete(target, true);
        }
    }

    [Fact]
    public async Task DownloadAsyncSkipsExistingFilesWithoutHttpCalls()
    {
        string target = Path.Combine(Path.GetTempPath(), $"gigaam-dl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);
        try
        {
            foreach (GigaAmModelFile file in GigaAmDownloader.RequiredFiles)
            {
                await File.WriteAllTextAsync(Path.Combine(target, file.FileName), "existing");
            }

            using ModelStubHandler handler = new();
            using HttpClient httpClient = new(handler);

            await GigaAmDownloader.DownloadAsync(target, httpClient);

            Assert.Empty(handler.RequestedPaths);
            foreach (GigaAmModelFile file in GigaAmDownloader.RequiredFiles)
            {
                Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(target, file.FileName)));
            }
        }
        finally
        {
            Directory.Delete(target, true);
        }
    }

    [Fact]
    public async Task DownloadAsyncCleansUpPartialFileOnFailure()
    {
        string target = Path.Combine(Path.GetTempPath(), $"gigaam-dl-{Guid.NewGuid():N}");
        try
        {
            using FailingHandler handler = new();
            using HttpClient httpClient = new(handler);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => GigaAmDownloader.DownloadAsync(target, httpClient));

            Assert.Empty(Directory.GetFiles(target));
        }
        finally
        {
            Directory.Delete(target, true);
        }
    }

    private sealed class ModelStubHandler : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string fileName = request.RequestUri!.Segments[^1];
            RequestedPaths.Add(fileName);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"content-of-{fileName}"),
            });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":\"not found\"}"),
            });
    }
}
