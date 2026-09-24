using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using AudioParsing.Win.Services;
using Microsoft.Extensions.Logging;

namespace AudioParsing.Win.ViewModels;

/// <summary>
/// Main window state machine: idle (drop zone) → processing (log).
/// The log stays visible after completion so the user can review results;
/// dropping more files at any time starts a new run (resolves open question §10/Q1).
/// New drops while a run is active are declined with a warning (§10/Q4).
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IPipelineRunner _runner;
    private readonly IDialogService _dialog;
    private readonly ILogger<MainWindowViewModel> _log;
    private CancellationTokenSource? _cts;
    private bool _isDropZoneVisible = true;
    private bool _isProcessing;

    public MainWindowViewModel(IPipelineRunner runner, IDialogService dialog, ILogger<MainWindowViewModel> log)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        OpenSettingsCommand = new RelayCommand(_ => _dialog.ShowSettings());
        CancelCommand = new RelayCommand(_ => _cts?.Cancel());
        SelectFilesCommand = new AsyncRelayCommand(SelectFilesAsync);
    }

    public ObservableCollection<LogEntryViewModel> Log { get; } = new();

    public bool IsDropZoneVisible
    {
        get => _isDropZoneVisible;
        private set
        {
            if (SetProperty(ref _isDropZoneVisible, value))
            {
                OnPropertyChanged(nameof(IsLogVisible));
            }
        }
    }

    public bool IsLogVisible => !IsDropZoneVisible;

    public bool IsProcessing
    {
        get => _isProcessing;
        private set => SetProperty(ref _isProcessing, value);
    }

    public ICommand OpenSettingsCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand SelectFilesCommand { get; }

    /// <summary>
    /// Opens the multi-select file dialog and processes the picked files.
    /// Cancellation (null) is a no-op; an empty pick keeps the drop zone visible.
    /// </summary>
    public async Task SelectFilesAsync()
    {
        if (IsProcessing)
        {
            _dialog.ShowWarning("Дождитесь завершения текущей обработки.");
            return;
        }

        IReadOnlyList<string>? picked = _dialog.ShowOpenAudioFilesDialog();
        if (picked is null)
        {
            return;
        }

        await ProcessDroppedFilesAsync(picked).ConfigureAwait(true);
    }

    /// <summary>
    /// Filters <paramref name="paths"/> to supported audio files and runs them through
    /// the shared <see cref="AudioPipeline"/>. Created on the UI thread, the
    /// <see cref="Progress{T}"/> callback marshals every log line back to the dispatcher.
    /// </summary>
    public async Task ProcessDroppedFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (IsProcessing)
        {
            _dialog.ShowWarning("Дождитесь завершения текущей обработки.");
            return;
        }

        List<string> audio = paths
            .Where(File.Exists)
            .Where(AudioFileFinder.IsAudioFile)
            .ToList();

        if (audio.Count == 0)
        {
            _dialog.ShowWarning("Не найдено поддерживаемых аудиофайлов.");
            return;
        }

        Log.Clear();
        IsDropZoneVisible = false;
        IsProcessing = true;
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cts = cts;
        try
        {
            // Created on the UI thread: callbacks post back to the UI thread,
            // so mutating the ObservableCollection here is safe.
            Progress<PipelineProgress> progress = new(p => Log.Add(LogEntryViewModel.From(p)));
            // Resume on the UI thread: AppendSummary mutates the bound Log collection.
            IReadOnlyList<AudioFileResult> results = await _runner.ProcessAsync(audio, progress, cts.Token).ConfigureAwait(true);
            AppendSummary(results);
        }
        catch (OperationCanceledException)
        {
            Log.Add(LogEntryViewModel.Info("Обработка отменена."));
        }
#pragma warning disable CA1031 // UI boundary: any failure surfaces as a log line instead of crashing the app.
        catch (Exception ex)
        {
            ReportUnhandled(ex);
        }
#pragma warning restore CA1031
        finally
        {
            _cts = null;
            IsProcessing = false;
        }
    }

    public void ReportUnhandled(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        LogUnhandledFailure(_log, ex);
        Log.Add(LogEntryViewModel.Error(ex.Message));
    }

    [LoggerMessage(0, LogLevel.Error, "Unhandled failure during processing")]
    private static partial void LogUnhandledFailure(ILogger logger, Exception ex);

    private void AppendSummary(IReadOnlyList<AudioFileResult> results)
    {
        int failed = results.Count(static r => !r.Success);
        int skipped = results.Count(static r => r.Skipped);
        int ok = results.Count - failed - skipped;
        Log.Add(LogEntryViewModel.Info($"Готово: {ok}/{results.Count} обработано (пропущено: {skipped}, ошибок: {failed})."));
    }
}
