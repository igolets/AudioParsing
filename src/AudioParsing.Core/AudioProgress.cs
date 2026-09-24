namespace AudioParsing;

/// <summary>
/// Pipeline stage reported through <see cref="PipelineProgress"/>.
/// </summary>
public enum PipelineStage
{
    Reading,
    Compressing,
    Transcribing,
    Summarizing,
    Writing,
    Completed,
    Skipped,
    Failed,
}

/// <summary>
/// Per-file progress event pushed via <see cref="IProgress{T}"/>.
/// The Win shell renders these; the console ignores them (passes <c>null</c>).
/// </summary>
public sealed record PipelineProgress(string AudioPath, PipelineStage Stage, string Message);
