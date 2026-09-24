using System.Net;
using Xunit;

namespace AudioParsing.Tests;

public sealed class RouterAiTranscriberTests
{
    [Fact]
    public void NameReturnsModel()
    {
        using HttpClient httpClient = new();
        using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
        RouterAiTranscriber transcriber = new(client, "openai/whisper-large-v3-turbo");

        Assert.Equal("openai/whisper-large-v3-turbo", transcriber.Name);
    }

    [Fact]
    public async Task TranscribeAsyncDelegatesToRouterAiClient()
    {
        string audio = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using CaptureHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            RouterAiTranscriber transcriber = new(client, "test-model");

            string text = await transcriber.TranscribeAsync(audio, "ru");

            Assert.Equal("recognized", text);
            Assert.Equal(1, handler.CallCount);
            Assert.Contains("test-model", handler.LastBody, StringComparison.Ordinal);
            Assert.Contains("ru", handler.LastBody, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(audio);
        }
    }

    [Fact]
    public async Task TranscribeAsyncOmitsLanguageWhenNull()
    {
        string audio = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using CaptureHandler handler = new();
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
            RouterAiTranscriber transcriber = new(client, "test-model");

            await transcriber.TranscribeAsync(audio, null);

            Assert.DoesNotContain("language", handler.LastBody, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(audio);
        }
    }

    [Fact]
    public void ConstructorRejectsNullClient() =>
        Assert.Throws<ArgumentNullException>(() => new RouterAiTranscriber(null!, "model"));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ConstructorRejectsEmptyModel(string model)
    {
        using HttpClient httpClient = new();
        using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));

        Assert.Throws<ArgumentException>(() => new RouterAiTranscriber(client, model));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\":\"recognized\"}"),
            };
        }
    }
}
