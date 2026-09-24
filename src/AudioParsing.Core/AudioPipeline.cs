namespace AudioParsing;

/// <summary>
/// Per-file outcome of a transcription run.
/// </summary>
public sealed record AudioFileResult(
    string AudioPath,
    string MarkdownPath,
    bool Skipped,
    bool Success,
    string? Error);

/// <summary>
/// Scans a folder for audio and video files, extracts the audio track from video
/// (and compresses oversized audio) with ffmpeg,
/// transcribes each with whisper, summarizes the transcript with luna and stores
/// a Markdown document (summary plus verbatim transcript) next to the media file.
/// </summary>
public sealed class AudioPipeline
{
    public const string DefaultModel = "openai/whisper-large-v3-turbo";

    public const string DefaultLanguage = "ru";

    public const string DefaultSummaryModel = "openai/gpt-6-luna";

    public const long MaxUploadBytes = 25L * 1024L * 1024L;

    private readonly RouterAiClient _client;
    private readonly string _ffmpegPath;
    private readonly string _model;
    private readonly string? _language;
    private readonly string _summaryModel;

    public AudioPipeline(
        RouterAiClient client,
        string ffmpegPath,
        string model = DefaultModel,
        string? language = DefaultLanguage,
        string summaryModel = DefaultSummaryModel)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new ArgumentException("ffmpeg path must not be empty.", nameof(ffmpegPath));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must not be empty.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(summaryModel))
        {
            throw new ArgumentException("Summary model must not be empty.", nameof(summaryModel));
        }

        _ffmpegPath = ffmpegPath;
        _model = model;
        _language = language;
        _summaryModel = summaryModel;
    }

    /// <summary>
    /// Transcribes every audio and video file in <paramref name="folder"/> into a sibling .md file
    /// containing luna's summary and whisper's verbatim transcript.
    /// Video containers are always passed through ffmpeg to extract the audio track;
    /// audio files only when they exceed <see cref="MaxUploadBytes"/>.
    /// Existing outputs are skipped unless <paramref name="force"/> is set.
    /// </summary>
    public async Task<IReadOnlyList<AudioFileResult>> ProcessFolderAsync(
        string folder,
        bool force = false,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> audioFiles = AudioFileFinder.FindAudioFiles(folder);
        return await ProcessFilesAsync(audioFiles, force, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Transcribes an explicit list of audio and video files (e.g. from drag-and-drop, which may
    /// span folders) into sibling .md files. Existing outputs are skipped unless
    /// <paramref name="force"/> is set. Per-file failures are returned as
    /// <see cref="AudioFileResult"/> entries and never abort the batch.
    /// </summary>
    public async Task<IReadOnlyList<AudioFileResult>> ProcessFilesAsync(
        IReadOnlyList<string> audioPaths,
        bool force = false,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioPaths);

        List<AudioFileResult> results = new();
        foreach (string audioPath in audioPaths)
        {
            AudioFileResult result = await ProcessFileAsync(audioPath, force, progress, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    private async Task<AudioFileResult> ProcessFileAsync(
        string audioPath,
        bool force,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        string markdownPath = Path.ChangeExtension(audioPath, ".md");
        if (!force && File.Exists(markdownPath))
        {
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Skipped, "Markdown already exists; skipped."));
            return new AudioFileResult(audioPath, markdownPath, Skipped: true, Success: true, Error: null);
        }

        string transcriptionInput = audioPath;
        string? extractedPath = null;
        try
        {
            FileInfo info = new(audioPath);
            if (FfmpegCompressor.RequiresAudioExtraction(audioPath, info.Length, MaxUploadBytes))
            {
                progress?.Report(new PipelineProgress(audioPath, PipelineStage.Compressing, "Compressing with ffmpeg."));
                extractedPath = Path.Combine(Path.GetTempPath(), $"audioparsing-{Guid.NewGuid():N}.mp3");
                await FfmpegCompressor
                    .CompressAsync(_ffmpegPath, audioPath, extractedPath, cancellationToken)
                    .ConfigureAwait(false);
                transcriptionInput = extractedPath;
            }

            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Transcribing, $"Transcribing with {_model}."));
            string transcript = await _client
                .TranscribeAsync(transcriptionInput, _model, _language, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Summarizing, $"Summarizing with {_summaryModel}."));
            string summaryResponse = await _client
                .GenerateSummaryAsync(transcript, _summaryModel, cancellationToken)
                .ConfigureAwait(false);
            (string summary, IReadOnlyList<string> keywords) =
                MarkdownDocument.SplitSummaryAndKeywords(summaryResponse);
            string title = Path.GetFileNameWithoutExtension(audioPath);
            string markdown = MarkdownDocument.Build(
                title,
                DateOnly.FromDateTime(DateTime.Today),
                keywords,
                summary,
                transcript);
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Writing, $"Writing {markdownPath}."));
            await File.WriteAllTextAsync(markdownPath, markdown, cancellationToken).ConfigureAwait(false);
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Completed, $"Done: {markdownPath}."));
            return new AudioFileResult(audioPath, markdownPath, Skipped: false, Success: true, Error: null);
        }
        catch (HttpRequestException ex)
        {
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Failed, ex.Message));
            return new AudioFileResult(audioPath, markdownPath, Skipped: false, Success: false, Error: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Failed, ex.Message));
            return new AudioFileResult(audioPath, markdownPath, Skipped: false, Success: false, Error: ex.Message);
        }
        catch (ArgumentException ex)
        {
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Failed, ex.Message));
            return new AudioFileResult(audioPath, markdownPath, Skipped: false, Success: false, Error: ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Failed, ex.Message));
            return new AudioFileResult(audioPath, markdownPath, Skipped: false, Success: false, Error: ex.Message);
        }
        catch (IOException ex)
        {
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Failed, ex.Message));
            return new AudioFileResult(audioPath, markdownPath, Skipped: false, Success: false, Error: ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Failed, ex.Message));
            return new AudioFileResult(audioPath, markdownPath, Skipped: false, Success: false, Error: ex.Message);
        }
        finally
        {
            if (extractedPath is not null && File.Exists(extractedPath))
            {
                File.Delete(extractedPath);
            }
        }
    }
}
