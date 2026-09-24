namespace AudioParsing.Win.Services;

/// <summary>
/// <see cref="IApiKeyProvider"/> reading the same environment variable as
/// <see cref="RouterAiClient.FromEnvironment"/>.
/// </summary>
public sealed class EnvApiKeyProvider : IApiKeyProvider
{
    public const string EnvVarName = RouterAiClient.ApiKeyEnvironmentVariable;

    public string? GetApiKey() => Environment.GetEnvironmentVariable(EnvVarName);
}
