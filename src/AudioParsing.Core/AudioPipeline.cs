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

    private readonly IAudioTranscriber _transcriber;
    private readonly TranscriptionBackend _backend;
    private readonly ISummaryGenerator _summarizer;
    private readonly string _ffmpegPath;
    private readonly string? _language;

    public AudioPipeline(
        RouterAiClient client,
        string ffmpegPath,
        string model = DefaultModel,
        string? language = DefaultLanguage,
        string summaryModel = DefaultSummaryModel)
        : this(client, new RouterAiTranscriber(client, model), TranscriptionBackend.External, ffmpegPath, language, summaryModel)
    {
    }

    /// <summary>
    /// Backend-agnostic constructor. The pipeline keeps owning stage ordering, error mapping
    /// and results; <paramref name="transcriber"/> only swaps how the transcript is produced.
    /// </summary>
    public AudioPipeline(
        RouterAiClient client,
        IAudioTranscriber transcriber,
        TranscriptionBackend backend,
        string ffmpegPath,
        string? language = DefaultLanguage,
        string summaryModel = DefaultSummaryModel)
        : this(transcriber, backend, new RouterAiSummaryGenerator(client, summaryModel), SummaryBackend.External, ffmpegPath, language)
    {
    }

    /// <summary>
    /// Fully backend-agnostic constructor. Both stages are explicit; no
    /// <see cref="RouterAiClient"/> is needed when neither stage is external.
    /// <paramref name="summaryBackend"/> selects which summarizer <paramref name="summarizer"/>
    /// implements; the pipeline itself only drives stage ordering and results.
    /// </summary>
    public AudioPipeline(
        IAudioTranscriber transcriber,
        TranscriptionBackend backend,
        ISummaryGenerator summarizer,
        SummaryBackend summaryBackend,
        string ffmpegPath,
        string? language = DefaultLanguage)
    {
        _transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        _summarizer = summarizer ?? throw new ArgumentNullException(nameof(summarizer));
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new ArgumentException("ffmpeg path must not be empty.", nameof(ffmpegPath));
        }

        _backend = backend;
        _ffmpegPath = ffmpegPath;
        _language = language;
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
            bool pcmWav = _backend == TranscriptionBackend.Local;
            bool preprocess = pcmWav
                || FfmpegCompressor.RequiresAudioExtraction(audioPath, info.Length, MaxUploadBytes);

            if (preprocess)
            {
                string ext = pcmWav ? ".wav" : ".mp3";
                progress?.Report(new PipelineProgress(audioPath, PipelineStage.Compressing,
                    pcmWav ? "Preparing 16 kHz WAV for GigaAM." : "Compressing with ffmpeg."));
                extractedPath = Path.Combine(Path.GetTempPath(), $"audioparsing-{Guid.NewGuid():N}{ext}");
                if (pcmWav)
                {
                    await FfmpegCompressor
                        .ExtractPcmWavAsync(_ffmpegPath, audioPath, extractedPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await FfmpegCompressor
                        .CompressAsync(_ffmpegPath, audioPath, extractedPath, cancellationToken)
                        .ConfigureAwait(false);
                }

                transcriptionInput = extractedPath;
            }

            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Transcribing, $"Transcribing with {_transcriber.Name}."));
            string transcript = await _transcriber
                .TranscribeAsync(transcriptionInput, _language, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new PipelineProgress(audioPath, PipelineStage.Summarizing, $"Summarizing with {_summarizer.Name}."));
            string summaryResponse = await _summarizer
                .GenerateSummaryAsync(transcript, cancellationToken)
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
