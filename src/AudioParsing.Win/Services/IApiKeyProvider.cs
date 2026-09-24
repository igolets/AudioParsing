namespace AudioParsing.Win.Services;

/// <summary>
/// Provides the RouterAI API key. Mirrors the console, which reads it from the environment.
/// </summary>
public interface IApiKeyProvider
{
    public string? GetApiKey();
}
