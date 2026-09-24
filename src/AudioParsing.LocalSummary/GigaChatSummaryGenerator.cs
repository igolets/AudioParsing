using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace AudioParsing.LocalSummary;

/// <summary>
/// On-device summarization via LLamaSharp (GigaChat3.1 GGUF, in-process llama.cpp, CPU backend).
/// Reuses <see cref="RouterAiClient.SummarySystemPrompt"/> verbatim and returns the same shape
/// as RouterAI (summary text + trailing <c>Keywords:</c> line), so the unchanged
/// <see cref="MarkdownDocument.SplitSummaryAndKeywords"/> + <see cref="MarkdownDocument.Build"/>
/// produce an identical <c>.md</c> file.
/// </summary>
public sealed class GigaChatSummaryGenerator : ISummaryGenerator, IDisposable
{
    /// <summary>Sampling temperature mirroring <see cref="RouterAiClient.GenerateSummaryAsync"/> (0.2).</summary>
    private const float SummaryTemperature = 0.2f;

    /// <summary>Generation cap mirroring the 2048-token output budget reserved by callers.</summary>
    private const int MaxResponseTokens = 2048;

    private readonly string _ggufPath;
    private readonly int _contextSize;
    private readonly int _gpuLayerCount;
    private readonly Lazy<LLamaWeights> _weights;
    private readonly Lazy<LLamaContext> _context;
    private bool _disposed;

    /// <summary>
    /// Validates the GGUF file from <paramref name="settings"/>.
    /// The native weights are loaded lazily on the first summary so model-load
    /// failures surface per-file (as <c>AudioFileResult.Success=false</c>) instead of
    /// aborting the batch.
    /// </summary>
    public GigaChatSummaryGenerator(GigaChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _ggufPath = ResolveGgufPath(settings);
        if (settings.ContextSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "GigaChat ContextSize must be positive.");
        }

        if (settings.GpuLayerCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "GigaChat GpuLayerCount must not be negative (0 = CPU).");
        }

        _contextSize = settings.ContextSize;
        _gpuLayerCount = settings.GpuLayerCount;
        _weights = new Lazy<LLamaWeights>(LoadWeights, LazyThreadSafetyMode.ExecutionAndPublication);
        _context = new Lazy<LLamaContext>(
            () => _weights.Value.CreateContext(new ModelParams(_ggufPath)
            {
                ContextSize = (uint)_contextSize,
                GpuLayerCount = _gpuLayerCount,
            }),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string Name => "GigaChat3.1-10B-A1.8B (local)";

    /// <inheritdoc />
    public async Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new ArgumentException("Transcript must not be empty.", nameof(transcript));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        // A fresh session per call so lectures never contaminate each other;
        // the underlying weights and context are shared.
        InteractiveExecutor executor = new(_context.Value);
        ChatHistory history = new();
        history.AddMessage(AuthorRole.System, RouterAiClient.SummarySystemPrompt);
        ChatSession session = new(executor, history);
        InferenceParams inferenceParams = new()
        {
            MaxTokens = MaxResponseTokens,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = SummaryTemperature,
            },
        };

        StringBuilder response = new();
        await foreach (string token in session
            .ChatAsync(new ChatHistory.Message(AuthorRole.User, transcript), inferenceParams, cancellationToken)
            .ConfigureAwait(false))
        {
            response.Append(token);
        }

        string text = response.ToString().Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("GigaChat returned an empty summary.");
        }

        return text;
    }

    /// <summary>
    /// Validates the GGUF path without touching native code (unit-testable).
    /// Rooted paths are used as-is, relative paths resolve next to the executable.
    /// </summary>
    public static string ResolveGgufPath(GigaChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string resolved = GigaChatDownloader.ResolveModelFilePath(settings.GgufPath);
        if (!File.Exists(resolved))
        {
            throw new InvalidOperationException(
                $"GigaChat GGUF model not found: '{resolved}'. " +
                "Download the GigaChat3.1 GGUF and set GigaChat:GgufPath in appsettings.json.");
        }

        return resolved;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_context.IsValueCreated)
        {
            _context.Value.Dispose();
        }

        if (_weights.IsValueCreated)
        {
            _weights.Value.Dispose();
        }
    }

    private LLamaWeights LoadWeights() => LLamaWeights.LoadFromFile(new ModelParams(_ggufPath)
    {
        ContextSize = (uint)_contextSize,
        GpuLayerCount = _gpuLayerCount,
    });
}
