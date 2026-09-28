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
/// Both hosts render these: the Win shell appends log lines, the console writes
/// progress lines to stdout. Long local transcriptions report per-chunk
/// (processed/total minutes) so multi-hour files don't look stuck.
/// </summary>
public sealed record PipelineProgress(string AudioPath, PipelineStage Stage, string Message);
