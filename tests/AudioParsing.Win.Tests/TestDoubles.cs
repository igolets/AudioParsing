using AudioParsing;
using AudioParsing.Win.Services;
using Microsoft.Extensions.Logging;

namespace AudioParsing.Win.Tests;

internal sealed class TestLogger<T> : ILogger<T>
{
    public static TestLogger<T> Instance { get; } = new();

    IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}


internal sealed class FakeSettingsStore : ISettingsStore
{
    public FakeSettingsStore(AppSettingsModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public AppSettingsModel Model { get; private set; }

    public int SaveCallCount { get; private set; }

    public string SettingsFilePath => Path.Combine(Path.GetTempPath(), "audioparsing-win-test-settings.json");

    public AppSettingsModel Load() => Model;

    public void Save(AppSettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Model = model;
        SaveCallCount++;
    }
}

internal sealed class FakeApiKeyProvider : IApiKeyProvider
{
    public FakeApiKeyProvider(string? apiKey)
    {
        ApiKey = apiKey;
    }

    public string? ApiKey { get; set; }

    public string? GetApiKey() => ApiKey;
}

internal sealed class FakePipelineRunner : IPipelineRunner
{
    private readonly Func<
        IReadOnlyList<string>,
        IProgress<PipelineProgress>?,
        CancellationToken,
        Task<IReadOnlyList<AudioFileResult>>> _handler;

    public FakePipelineRunner(
        Func<IReadOnlyList<string>, IProgress<PipelineProgress>?, CancellationToken, Task<IReadOnlyList<AudioFileResult>>> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public IReadOnlyList<string> ReceivedFiles { get; private set; } = [];

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<AudioFileResult>> ProcessAsync(
        IReadOnlyList<string> files,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        CallCount++;
        ReceivedFiles = files;
        return _handler(files, progress, cancellationToken);
    }
}

internal sealed class FakeDialogService : IDialogService
{
    public List<string> Warnings { get; } = new();

    public List<string> Errors { get; } = new();

    public int SettingsShownCount { get; private set; }

    public string? OpenFileDialogResult { get; set; }

    public void ShowSettings() => SettingsShownCount++;

    public void ShowWarning(string message) => Warnings.Add(message);

    public void ShowError(string message) => Errors.Add(message);

    public string? ShowOpenFileDialog(string? initialPath) => OpenFileDialogResult;
}
