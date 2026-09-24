using System.IO;
using System.Windows.Input;
using AudioParsing.Win.Services;

namespace AudioParsing.Win.ViewModels;

/// <summary>
/// Close request from <see cref="SettingsViewModel"/>; the view translates it into
/// <c>DialogResult</c> so the view model never touches WPF window types.
/// </summary>
public sealed class DialogCloseRequestedEventArgs : EventArgs
{
    public DialogCloseRequestedEventArgs(bool? dialogResult)
    {
        DialogResult = dialogResult;
    }

    public bool? DialogResult { get; }
}

/// <summary>
/// Edits the shared appsettings.json model with validation. Settings are re-read
/// by <see cref="IPipelineRunner"/> at the start of every run, so a save takes
/// effect on the next drop without restarting the app.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly IDialogService _dialog;
    private string _ffmpegPath = string.Empty;
    private string _transcriptionModel = string.Empty;
    private string _summaryModel = string.Empty;
    private string? _validationMessage;

    public SettingsViewModel(ISettingsStore store, IDialogService dialog)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        SaveCommand = new RelayCommand(_ => TrySave());
        BrowseFfmpegCommand = new RelayCommand(_ => BrowseFfmpeg());
        Load();
    }

    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

    public string FfmpegPath
    {
        get => _ffmpegPath;
        set => SetProperty(ref _ffmpegPath, value);
    }

    public string TranscriptionModel
    {
        get => _transcriptionModel;
        set => SetProperty(ref _transcriptionModel, value);
    }

    public string SummaryModel
    {
        get => _summaryModel;
        set => SetProperty(ref _summaryModel, value);
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public ICommand SaveCommand { get; }

    public ICommand BrowseFfmpegCommand { get; }

    public void Load()
    {
        AppSettingsModel model = _store.Load();
        FfmpegPath = model.FfmpegPath;
        TranscriptionModel = model.TranscriptionModel;
        SummaryModel = model.SummaryModel;
        ValidationMessage = null;
    }

    public bool TrySave()
    {
        if (string.IsNullOrWhiteSpace(TranscriptionModel))
        {
            ValidationMessage = "Модель транскрипции не должна быть пустой.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SummaryModel))
        {
            ValidationMessage = "Модель суммаризации не должна быть пустой.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(FfmpegPath) || !File.Exists(FfmpegPath))
        {
            ValidationMessage = "Укажите существующий путь к ffmpeg.";
            return false;
        }

        _store.Save(new AppSettingsModel
        {
            FfmpegPath = FfmpegPath,
            TranscriptionModel = TranscriptionModel,
            SummaryModel = SummaryModel,
        });
        ValidationMessage = null;
        CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(true));
        return true;
    }

    private void BrowseFfmpeg()
    {
        string? picked = _dialog.ShowOpenFileDialog(FfmpegPath);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            FfmpegPath = picked;
        }
    }
}
