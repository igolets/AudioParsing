namespace AudioParsing.Win.Services;

/// <summary>
/// Runs an explicit file list through the shared <see cref="AudioPipeline"/>.
/// </summary>
public interface IPipelineRunner
{
    public Task<IReadOnlyList<AudioFileResult>> ProcessAsync(
        IReadOnlyList<string> files,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}
