using SherpaOnnx;

namespace AudioParsing.LocalStt;

/// <summary>
/// On-device GigaAM-v3 transcription via sherpa-onnx (expects 16 kHz mono PCM WAV,
/// as produced by <see cref="FfmpegCompressor.ExtractPcmWavAsync"/>).
/// Uses the transducer (encoder/decoder/joiner + tokens) GigaAM-v3 file set.
/// </summary>
public sealed class GigaAmTranscriber : IAudioTranscriber, IDisposable
{
    private readonly Lazy<OfflineRecognizer> _recognizer;
    private bool _disposed;

    /// <summary>
    /// Validates the GigaAM-v3 model files from <paramref name="settings"/>.
    /// The native recognizer is loaded lazily on the first transcription so model-load
    /// failures surface per-file (as <c>AudioFileResult.Success=false</c>) instead of
    /// aborting the batch.
    /// </summary>
    public GigaAmTranscriber(GigaAmSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        OfflineRecognizerConfig config = BuildRecognizerConfig(settings);
        _recognizer = new Lazy<OfflineRecognizer>(
            () => new OfflineRecognizer(config),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string Name => "GigaAM-v3 (local)";

    /// <summary>
    /// Maximum audio window fed to a single <c>Decode</c> call (30 s at 16 kHz).
    /// The transducer encoder uses full-sequence self-attention, so decoding a
    /// 90-minute recording in one go blows up the attention matrix and crashes
    /// onnxruntime (BroadcastIterator axis error → SEHException). Public for tests.
    /// </summary>
    public const int MaxSamplesPerDecode = WavReader.ExpectedSampleRate * 30;

    /// <inheritdoc />
    public Task<string> TranscribeAsync(
        string audioFilePath,
        string? language,
        IProgress<PipelineProgress>? progress = null,
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

        // The local backend ignores the language: GigaAM-v3 is a Russian model.
        // Capture the progress sink explicitly: Task.Run does not flow the caller's
        // SynchronizationContext-free Progress<T> semantics issue, but passing it as
        // state keeps the callback target obvious.
        return Task.Run(() => TranscribeCore(audioFilePath, cancellationToken, progress), cancellationToken);
    }

    /// <summary>
    /// Resolves the sherpa-onnx transducer configuration for <paramref name="settings"/>,
    /// validating that the model directory and all configured files exist.
    /// Does not touch native code, so it is safe to unit-test without a model.
    /// </summary>
    public static OfflineRecognizerConfig BuildRecognizerConfig(GigaAmSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string modelDir = GigaAmDownloader.ResolveModelDirectory(settings.ModelPath);
        if (!Directory.Exists(modelDir))
        {
            throw new InvalidOperationException(
                $"GigaAM model directory not found: '{modelDir}'. " +
                "Download the GigaAM-v3 sherpa-onnx model and set GigaAm:ModelPath in appsettings.json.");
        }

        string encoderPath = RequireModelFile(modelDir, settings.EncoderFileName);
        string decoderPath = RequireModelFile(modelDir, settings.DecoderFileName);
        string joinerPath = RequireModelFile(modelDir, settings.JoinerFileName);
        string tokensPath = RequireModelFile(modelDir, settings.TokensFileName);

        OfflineRecognizerConfig config = new();
        config.FeatConfig.SampleRate = WavReader.ExpectedSampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.Transducer.Encoder = encoderPath;
        config.ModelConfig.Transducer.Decoder = decoderPath;
        config.ModelConfig.Transducer.Joiner = joinerPath;
        config.ModelConfig.NumThreads = Math.Max(1, Environment.ProcessorCount);
        config.ModelConfig.Debug = 0;
        config.DecodingMethod = "greedy_search";
        return config;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_recognizer.IsValueCreated)
        {
            _recognizer.Value.Dispose();
        }
    }

    private static string RequireModelFile(string modelPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException(
                $"GigaAM model file name must not be empty (model directory: '{modelPath}').");
        }

        string fullPath = Path.Combine(modelPath, fileName);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"GigaAM model file not found: '{fullPath}'.");
        }

        return fullPath;
    }

    /// <summary>
    /// Splits <paramref name="samples"/> into non-overlapping windows of at most
    /// <paramref name="chunkSize"/> samples. Pure slicing, no native calls, so it is unit-testable.
    /// </summary>
    public static IEnumerable<float[]> SplitSamples(float[] samples, int chunkSize)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
        }

        for (int offset = 0; offset < samples.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, samples.Length - offset);
            float[] chunk = new float[length];
            Array.Copy(samples, offset, chunk, 0, length);
            yield return chunk;
        }
    }

    private string TranscribeCore(string audioFilePath, CancellationToken cancellationToken, IProgress<PipelineProgress>? progress = null)
    {
        (int sampleRate, float[] samples) = WavReader.ReadMono(audioFilePath);
        cancellationToken.ThrowIfCancellationRequested();

        List<float[]> chunks = SplitSamples(samples, MaxSamplesPerDecode).ToList();
        double totalMinutes = samples.Length / (double)WavReader.ExpectedSampleRate / 60.0;

        List<string> parts = new(chunks.Count);
        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parts.Add(DecodeChunk(chunks[i], sampleRate, audioFilePath, cancellationToken));
            double doneMinutes = Math.Min(
                totalMinutes,
                (i + 1) * MaxSamplesPerDecode / (double)WavReader.ExpectedSampleRate / 60.0);
            progress?.Report(new PipelineProgress(
                audioFilePath,
                PipelineStage.Transcribing,
                $"Transcribing with {Name}: {doneMinutes:F1}/{totalMinutes:F1} min (chunk {i + 1}/{chunks.Count})."));
        }

        string text = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"GigaAM returned an empty transcription for: {audioFilePath}");
        }

        return text;
    }

    private string DecodeChunk(float[] chunk, int sampleRate, string audioFilePath, CancellationToken cancellationToken)
    {
        using OfflineStream stream = _recognizer.Value.CreateStream();
        stream.AcceptWaveform(sampleRate, chunk);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _recognizer.Value.Decode(stream);
        }
        catch (System.Runtime.InteropServices.SEHException ex)
        {
            throw new InvalidOperationException(
                $"GigaAM native decode failed for: {audioFilePath}. "
                + "The model file set may be corrupt or incomplete.",
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return stream.Result?.Text?.Trim() ?? string.Empty;
    }
}
