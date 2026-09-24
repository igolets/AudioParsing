namespace AudioParsing;

/// <summary>Selected summarization backend. Persisted as a string in appsettings.json.</summary>
public enum SummaryBackend
{
    /// <summary>RouterAI luna over HTTP (default, current behaviour).</summary>
    External,

    /// <summary>On-device GigaChat GGUF via LLamaSharp.</summary>
    Local,

    /// <summary>No summarization; output transcript only.</summary>
    Skip,
}
