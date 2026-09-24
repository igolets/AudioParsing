using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioParsing;

/// <summary>
/// Minimal OpenAI-compatible wrapper for the RouterAI API (https://routerai.ru/docs/guides).
/// Reads the API key from the <c>ANTHROPIC_AUTH_TOKEN</c> environment variable.
/// </summary>
public sealed class RouterAiClient : IDisposable
{
    public const string ApiKeyEnvironmentVariable = "ANTHROPIC_AUTH_TOKEN";

    public static readonly Uri DefaultBaseUri = new("https://routerai.ru/api/v1");

    public const string SummarySystemPrompt = @"You are a professional academic editor.
Given a raw transcript of a lecture, write a concise summary in Russian including key insights and bullet points. Fully preserve the original meaning.
On the very last line of your response, output 3-7 keywords exactly in this format and nothing after it:
Keywords: word1, word2, word3";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public RouterAiClient(string apiKey, HttpClient? httpClient = null, Uri? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("RouterAI API key must not be empty.", nameof(apiKey));
        }

        BaseUri = baseUri ?? DefaultBaseUri;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public Uri BaseUri { get; }

    /// <summary>
    /// Creates a client using the API key stored in the <c>ANTHROPIC_AUTH_TOKEN</c> environment variable.
    /// </summary>
    public static RouterAiClient FromEnvironment(HttpClient? httpClient = null, Uri? baseUri = null)
    {
        string? apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Environment variable '{ApiKeyEnvironmentVariable}' is not set. " +
                "Set it to your RouterAI API key (see https://routerai.ru/settings/keys).");
        }

        return new RouterAiClient(apiKey, httpClient, baseUri);
    }

    /// <summary>
    /// Sends a single user message to <c>POST /chat/completions</c> and returns the assistant text.
    /// </summary>
    public async Task<string> GetChatCompletionAsync(
        string model,
        string userMessage,
        string? systemMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must not be empty.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("User message must not be empty.", nameof(userMessage));
        }

        List<RouterAiChatMessage> messages = new();
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            messages.Add(new RouterAiChatMessage("system", systemMessage));
        }

        messages.Add(new RouterAiChatMessage("user", userMessage));

        return await GetChatCompletionAsync(model, messages, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a full message history to <c>POST /chat/completions</c> and returns the assistant text.
    /// </summary>
    public async Task<string> GetChatCompletionAsync(
        string model,
        IReadOnlyList<RouterAiChatMessage> messages,
        double? temperature = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must not be empty.", nameof(model));
        }

        if (messages is null || messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(messages));
        }

        RouterAiChatRequest request = new(model, messages, temperature);
        string payload = JsonSerializer.Serialize(request, SerializerOptions);

        string baseUrl = BaseUri.ToString().TrimEnd('/');
        Uri endpoint = new($"{baseUrl}/chat/completions", UriKind.Absolute);
        using StringContent content = new(payload, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient
            .PostAsync(endpoint, content, cancellationToken)
            .ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"RouterAI request failed with {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}");
        }

        RouterAiChatResponse? chatResponse = JsonSerializer.Deserialize<RouterAiChatResponse>(body, SerializerOptions);
        string? text = null;
        if (chatResponse?.Choices is not null && chatResponse.Choices.Count > 0)
        {
            text = chatResponse.Choices[0].Message?.Content;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"RouterAI returned an empty completion. Body: {body}");
        }

        return text;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Sends an audio file to <c>POST /audio/transcriptions</c> (multipart/form-data)
    /// and returns the recognized text.
    /// </summary>
    public async Task<string> TranscribeAsync(
        string audioFilePath,
        string model,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath))
        {
            throw new ArgumentException("Audio file path must not be empty.", nameof(audioFilePath));
        }

        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException($"Audio file not found: {audioFilePath}", audioFilePath);
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must not be empty.", nameof(model));
        }

        string baseUrl = BaseUri.ToString().TrimEnd('/');
        Uri endpoint = new($"{baseUrl}/audio/transcriptions", UriKind.Absolute);

        using MultipartFormDataContent form = new();
        using FileStream fileStream = File.OpenRead(audioFilePath);
        using StreamContent fileContent = new(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetAudioMimeType(Path.GetExtension(audioFilePath)));
        form.Add(fileContent, "file", Path.GetFileName(audioFilePath));
        using StringContent modelContent = new(model);
        form.Add(modelContent, "model");
        if (!string.IsNullOrWhiteSpace(language))
        {
            using StringContent languageContent = new(language);
            form.Add(languageContent, "language");
            return await SendTranscriptionAsync(endpoint, form, cancellationToken).ConfigureAwait(false);
        }

        return await SendTranscriptionAsync(endpoint, form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Summarizes a raw <paramref name="transcript"/> via <c>POST /chat/completions</c>
    /// and returns luna's raw response (summary text with a trailing keywords line).
    /// </summary>
    public async Task<string> GenerateSummaryAsync(
        string transcript,
        string model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new ArgumentException("Transcript must not be empty.", nameof(transcript));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must not be empty.", nameof(model));
        }

        List<RouterAiChatMessage> messages = new()
        {
            new RouterAiChatMessage("system", SummarySystemPrompt),
            new RouterAiChatMessage("user", transcript),
        };

        return await GetChatCompletionAsync(model, messages, 0.2, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SendTranscriptionAsync(
        Uri endpoint,
        MultipartFormDataContent form,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient
            .PostAsync(endpoint, form, cancellationToken)
            .ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"RouterAI transcription failed with {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}");
        }

        RouterAiTranscriptionResponse? transcription =
            JsonSerializer.Deserialize<RouterAiTranscriptionResponse>(body, SerializerOptions);
        if (string.IsNullOrWhiteSpace(transcription?.Text))
        {
            throw new InvalidOperationException($"RouterAI returned an empty transcription. Body: {body}");
        }

        return transcription.Text;
    }

    public static string GetAudioMimeType(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        return extension.ToUpperInvariant() switch
        {
            ".MP3" => "audio/mpeg",
            ".MP4" => "audio/mp4",
            ".MPEG" => "audio/mpeg",
            ".MPGA" => "audio/mpeg",
            ".M4A" => "audio/m4a",
            ".WAV" => "audio/wav",
            ".WEBM" => "audio/webm",
            ".FLAC" => "audio/flac",
            ".OGG" => "audio/ogg",
            ".OPUS" => "audio/opus",
            ".WMA" => "audio/x-ms-wma",
            ".AAC" => "audio/aac",
            _ => "application/octet-stream",
        };
    }
}

/// <summary>
/// A single chat message in OpenAI-compatible format.
/// </summary>
public sealed record RouterAiChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record RouterAiChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<RouterAiChatMessage> Messages,
    [property: JsonPropertyName("temperature")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Temperature = null);

public sealed record RouterAiChatResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<RouterAiChatChoice>? Choices);

public sealed record RouterAiChatChoice(
    [property: JsonPropertyName("message")] RouterAiChatResponseMessage? Message);

public sealed record RouterAiChatResponseMessage(
    [property: JsonPropertyName("content")] string? Content);

public sealed record RouterAiTranscriptionResponse(
    [property: JsonPropertyName("text")] string? Text);
