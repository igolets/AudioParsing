using System.Net;
using Xunit;

namespace AudioParsing.Tests;

public sealed class GigaChatDownloaderTests
{
    [Fact]
    public void RequiredFilesListsExactlyTheGgufFile()
    {
        GigaChatModelFile single = Assert.Single(GigaChatDownloader.RequiredFiles);

        Assert.Equal(GigaChatDownloader.GgufFileName, single.FileName);
        Assert.Equal(GigaChatDownloader.DisplaySize, single.DisplaySize);
    }

    [Fact]
    public void ResolveModelFilePathPassesThroughRootedPaths()
    {
        string rooted = Path.Combine(Path.GetTempPath(), "GigaChat3.1-10B-A1.8B-q4_K_M.gguf");

        Assert.Equal(rooted, GigaChatDownloader.ResolveModelFilePath(rooted));
    }

    [Fact]
    public void ResolveModelFilePathResolvesRelativePathsAgainstAppDirectory()
    {
        string resolved = GigaChatDownloader.ResolveModelFilePath("models/GigaChat3.1-10B-A1.8B-q4_K_M.gguf");

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "models", "GigaChat3.1-10B-A1.8B-q4_K_M.gguf"),
            resolved);
    }

    [Fact]
    public void ResolveModelFilePathFallsBackToDefaultWhenMissing()
    {
        string resolved = GigaChatDownloader.ResolveModelFilePath(null);

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "models", "GigaChat3.1-10B-A1.8B-q4_K_M.gguf"),
            resolved);
    }

    [Fact]
    public void ApplyDownloadedFileNameKeepsConfiguredFilePath()
    {
        GigaChatSettings settings = new() { GgufPath = "models/custom.gguf" };

        GigaChatDownloader.ApplyDownloadedFileName(settings);

        Assert.Equal("models/custom.gguf", settings.GgufPath);
    }

    [Fact]
    public void ApplyDownloadedFileNameAppendsFileNameToDirectories()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"gigachat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            GigaChatSettings settings = new() { GgufPath = directory };

            GigaChatDownloader.ApplyDownloadedFileName(settings);

            Assert.Equal(Path.Combine(directory, GigaChatDownloader.GgufFileName), settings.GgufPath);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task DownloadAsyncWritesFileAndReportsCompletion()
    {
        string target = Path.Combine(Path.GetTempPath(), $"gigachat-dl-{Guid.NewGuid():N}", GigaChatDownloader.GgufFileName);
        try
        {
            using ModelStubHandler handler = new();
            using HttpClient httpClient = new(handler);
            List<double> reported = new();
            Progress<double> progress = new(reported.Add);

            await GigaChatDownloader.DownloadAsync(target, httpClient, progress);

            Assert.True(File.Exists(target));
            Assert.Equal($"content-of-{GigaChatDownloader.GgufFileName}", await File.ReadAllTextAsync(target));
            Assert.False(File.Exists(target + ".download"));
            Assert.Single(handler.RequestedPaths);
            Assert.NotEmpty(reported);
            Assert.Equal(1.0, reported[^1]);
            Assert.True(reported.All(p => p is >= 0.0 and <= 1.0));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(target)!, true);
        }
    }

    [Fact]
    public async Task DownloadAsyncSkipsExistingFileWithoutHttpCalls()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"gigachat-dl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, GigaChatDownloader.GgufFileName);
        try
        {
            await File.WriteAllTextAsync(target, "existing");
            using ModelStubHandler handler = new();
            using HttpClient httpClient = new(handler);

            await GigaChatDownloader.DownloadAsync(target, httpClient);

            Assert.Empty(handler.RequestedPaths);
            Assert.Equal("existing", await File.ReadAllTextAsync(target));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DownloadAsyncCleansUpPartialFileOnFailure()
    {
        string target = Path.Combine(Path.GetTempPath(), $"gigachat-dl-{Guid.NewGuid():N}", GigaChatDownloader.GgufFileName);
        try
        {
            using FailingHandler handler = new();
            using HttpClient httpClient = new(handler);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => GigaChatDownloader.DownloadAsync(target, httpClient));

            Assert.False(File.Exists(target));
            Assert.False(File.Exists(target + ".download"));
        }
        finally
        {
            string? directory = Path.GetDirectoryName(target);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task DownloadAsyncThrowsOnEmptyTargetPath()
    {
        using ModelStubHandler handler = new();
        using HttpClient httpClient = new(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => GigaChatDownloader.DownloadAsync("  ", httpClient));
    }

    [Fact]
    public async Task DownloadAsyncThrowsOnNullHttpClient()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => GigaChatDownloader.DownloadAsync(tempFile, null!));
        }
        finally
        {
            File.Delete(tempFile);
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
