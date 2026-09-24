using System.Net;
using Xunit;

namespace AudioParsing.Tests;

public sealed class RouterAiSummaryGeneratorTests
{
    [Fact]
    public void NameReturnsModel()
    {
        using HttpClient httpClient = new();
        using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
        RouterAiSummaryGenerator generator = new(client, "openai/gpt-6-luna");

        Assert.Equal("openai/gpt-6-luna", generator.Name);
    }

    [Fact]
    public async Task GenerateSummaryAsyncDelegatesToClient()
    {
        using RecordingHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"short\\nKeywords: a\"}}]}"),
        });
        using HttpClient httpClient = new(handler);
        using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));
        RouterAiSummaryGenerator generator = new(client, "openai/gpt-6-luna");

        string summary = await generator.GenerateSummaryAsync("raw transcript");

        Assert.Equal("short\nKeywords: a", summary);
        Assert.NotNull(handler.LastBody);
        Assert.Contains("openai/gpt-6-luna", handler.LastBody!, StringComparison.Ordinal);
        Assert.Contains("raw transcript", handler.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorThrowsOnNullClient() =>
        Assert.Throws<ArgumentNullException>(() => new RouterAiSummaryGenerator(null!, "model"));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ConstructorThrowsOnEmptyModel(string model)
    {
        using HttpClient httpClient = new();
        using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));

        Assert.Throws<ArgumentException>(() => new RouterAiSummaryGenerator(client, model));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return _response;
        }
    }
}
