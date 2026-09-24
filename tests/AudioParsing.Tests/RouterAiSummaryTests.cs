using System.Net;
using Xunit;

namespace AudioParsing.Tests;

public sealed class RouterAiSummaryTests
{
    [Fact]
    public async Task GenerateSummaryAsyncPostsTranscriptAndModel()
    {
        using RecordingHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"short\"}}]}"),
        });
        using HttpClient httpClient = new(handler);
        using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));

        string summary = await client.GenerateSummaryAsync("raw transcript", "openai/gpt-6-luna");

        Assert.Equal("short", summary);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://example.test/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.NotNull(handler.LastBody);
        Assert.Contains("openai/gpt-6-luna", handler.LastBody!, StringComparison.Ordinal);
        Assert.Contains("raw transcript", handler.LastBody!, StringComparison.Ordinal);
        Assert.Contains("temperature", handler.LastBody!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GenerateSummaryAsyncThrowsOnEmptyTranscript(string transcript)
    {
        using RecordingHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"x\"}}]}"),
        });
        using HttpClient httpClient = new(handler);
        using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GenerateSummaryAsync(transcript, "openai/gpt-6-luna"));
        Assert.Equal(0, handler.CallCount);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return _response;
        }
    }
}
