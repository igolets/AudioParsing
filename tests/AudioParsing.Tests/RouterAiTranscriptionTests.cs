using System.Net;
using Xunit;

namespace AudioParsing.Tests;

public sealed class RouterAiTranscriptionTests
{
    [Fact]
    public async Task TranscribeAsyncPostsMultipartAndReturnsText()
    {
        string audio = Path.Combine(Path.GetTempPath(), $"audio-{Guid.NewGuid():N}.mp3");
        try
        {
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using FakeHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\":\"hello world\"}"),
            });
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));

            string text = await client.TranscribeAsync(audio, "openai/whisper-large-v3-turbo", "ru");

            Assert.Equal("hello world", text);
            Assert.NotNull(handler.LastRequest);
            Assert.Equal("https://example.test/v1/audio/transcriptions", handler.LastRequest!.RequestUri!.ToString());
            Assert.IsType<MultipartFormDataContent>(handler.LastRequest.Content);
        }
        finally
        {
            File.Delete(audio);
        }
    }

    [Fact]
    public async Task TranscribeAsyncThrowsOnServerError()
    {
        string audio = Path.Combine(Path.GetTempPath(), $"audio-{Guid.NewGuid():N}.mp3");
        try
        {
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            using FakeHandler handler = new(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"bad\"}"),
            });
            using HttpClient httpClient = new(handler);
            using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));

            await Assert.ThrowsAsync<HttpRequestException>(() => client.TranscribeAsync(audio, "openai/whisper-large-v3-turbo", "ru"));
        }
        finally
        {
            File.Delete(audio);
        }
    }

    [Fact]
    public async Task TranscribeAsyncThrowsWhenAudioFileIsMissing()
    {
        using FakeHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"text\":\"hi\"}"),
        });
        using HttpClient httpClient = new(handler);
        using RouterAiClient client = new("test-key", httpClient, new Uri("https://example.test/v1"));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            client.TranscribeAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.mp3"), "openai/whisper-large-v3-turbo", "ru"));
    }

    [Theory]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData(".m4a", "audio/m4a")]
    [InlineData(".wav", "audio/wav")]
    [InlineData(".xyz", "application/octet-stream")]
    public void GetAudioMimeTypeMapsExtensions(string extension, string expected) => Assert.Equal(expected, RouterAiClient.GetAudioMimeType(extension));

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }
}
