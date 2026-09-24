namespace AudioParsing;

/// <summary>Selected speech-to-text backend. Persisted as a string in appsettings.json.</summary>
public enum TranscriptionBackend
{
    /// <summary>RouterAI Whisper over HTTP (default, current behaviour).</summary>
    External,

    /// <summary>On-device GigaAM-v3 via sherpa-onnx.</summary>
    Local,
}
