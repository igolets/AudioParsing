using System.IO;
using System.Net.Http;
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
    private readonly Func<HttpClient> _httpClientFactory;
    private string _ffmpegPath = string.Empty;
    private string _transcriptionModel = string.Empty;
    private string _summaryModel = string.Empty;
    private string _transcriptionBackend = "External";
    private string _summaryBackend = "External";
    private string _language = AudioPipeline.DefaultLanguage;
    private string _gigaAmModelPath = "models/gigaam-v3";
    private string _gigaAmEncoderFileName = "gigaam_v3_e2e_rnnt_encoder.onnx";
    private string _gigaAmDecoderFileName = "gigaam_v3_e2e_rnnt_decoder.onnx";
    private string _gigaAmJoinerFileName = "gigaam_v3_e2e_rnnt_joint.onnx";
    private string _gigaAmTokensFileName = "gigaam_v3_e2e_rnnt_tokens.txt";
    private string _gigaChatGgufPath = "models/GigaChat3.1-10B-A1.8B-q4_K_M.gguf";
    private int _gigaChatContextSize = 16384;
    private int _gigaChatGpuLayerCount;
    private string? _validationMessage;
    private string? _downloadStatus;
    private string? _summaryDownloadStatus;

    public SettingsViewModel(ISettingsStore store, IDialogService dialog, Func<HttpClient>? httpClientFactory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _httpClientFactory = httpClientFactory ?? (static () => new HttpClient());
        SaveCommand = new RelayCommand(_ => TrySave());
        BrowseFfmpegCommand = new RelayCommand(_ => BrowseFfmpeg());
        BrowseGigaAmModelCommand = new RelayCommand(_ => BrowseGigaAmModel());
        DownloadGigaAmModelCommand = new AsyncRelayCommand(DownloadGigaAmModelAsync);
        BrowseGigaChatModelCommand = new RelayCommand(_ => BrowseGigaChatModel());
        DownloadGigaChatModelCommand = new AsyncRelayCommand(DownloadGigaChatModelAsync);
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

    /// <summary>
    /// Selected transcription backend: "External" (RouterAI Whisper) or "Local" (GigaAM-v3).
    /// </summary>
    public string TranscriptionBackend
    {
        get => _transcriptionBackend;
        set => SetProperty(ref _transcriptionBackend, value);
    }

    /// <summary>
    /// Selected summarization backend: "External" (RouterAI luna) or "Local" (GigaChat GGUF).
    /// </summary>
    public string SummaryBackend
    {
        get => _summaryBackend;
        set => SetProperty(ref _summaryBackend, value);
    }

    public string Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    public string GigaAmModelPath
    {
        get => _gigaAmModelPath;
        set => SetProperty(ref _gigaAmModelPath, value);
    }

    public string GigaAmEncoderFileName
    {
        get => _gigaAmEncoderFileName;
        set => SetProperty(ref _gigaAmEncoderFileName, value);
    }

    public string GigaAmDecoderFileName
    {
        get => _gigaAmDecoderFileName;
        set => SetProperty(ref _gigaAmDecoderFileName, value);
    }

    public string GigaAmJoinerFileName
    {
        get => _gigaAmJoinerFileName;
        set => SetProperty(ref _gigaAmJoinerFileName, value);
    }

    public string GigaAmTokensFileName
    {
        get => _gigaAmTokensFileName;
        set => SetProperty(ref _gigaAmTokensFileName, value);
    }

    /// <summary>GGUF file for the local summarization backend (file, not a directory).</summary>
    public string GigaChatGgufPath
    {
        get => _gigaChatGgufPath;
        set => SetProperty(ref _gigaChatGgufPath, value);
    }

    /// <summary>LLamaSharp context size for the local summarization backend.</summary>
    public int GigaChatContextSize
    {
        get => _gigaChatContextSize;
        set => SetProperty(ref _gigaChatContextSize, value);
    }

    /// <summary>Offloaded layer count; 0 = CPU (the only supported backend).</summary>
    public int GigaChatGpuLayerCount
    {
        get => _gigaChatGpuLayerCount;
        set => SetProperty(ref _gigaChatGpuLayerCount, value);
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    /// <summary>Model download progress or completion note; null when idle.</summary>
    public string? DownloadStatus
    {
        get => _downloadStatus;
        private set => SetProperty(ref _downloadStatus, value);
    }

    /// <summary>GigaChat model download progress or completion note; null when idle.</summary>
    public string? SummaryDownloadStatus
    {
        get => _summaryDownloadStatus;
        private set => SetProperty(ref _summaryDownloadStatus, value);
    }

    public ICommand SaveCommand { get; }

    public ICommand BrowseFfmpegCommand { get; }

    public ICommand BrowseGigaAmModelCommand { get; }

    public ICommand DownloadGigaAmModelCommand { get; }

    public ICommand BrowseGigaChatModelCommand { get; }

    public ICommand DownloadGigaChatModelCommand { get; }

    public void Load()
    {
        AppSettingsModel model = _store.Load();
        FfmpegPath = model.FfmpegPath;
        TranscriptionModel = model.TranscriptionModel;
        SummaryModel = model.SummaryModel;
        TranscriptionBackend = AppSettings.ParseTranscriptionBackend(model.TranscriptionBackend) == AudioParsing.TranscriptionBackend.Local
            ? "Local"
            : "External";
        SummaryBackend = AppSettings.ParseSummaryBackend(model.SummaryBackend) == AudioParsing.SummaryBackend.Local
            ? "Local"
            : "External";
        Language = model.Language;
        GigaAmModelPath = model.GigaAm.ModelPath;
        GigaAmEncoderFileName = model.GigaAm.EncoderFileName;
        GigaAmDecoderFileName = model.GigaAm.DecoderFileName;
        GigaAmJoinerFileName = model.GigaAm.JoinerFileName;
        GigaAmTokensFileName = model.GigaAm.TokensFileName;
        GigaChatGgufPath = model.GigaChat.GgufPath;
        GigaChatContextSize = model.GigaChat.ContextSize;
        GigaChatGpuLayerCount = model.GigaChat.GpuLayerCount;
        ValidationMessage = null;
    }

    public bool TrySave()
    {
        bool isLocal = TranscriptionBackend.Equals("Local", StringComparison.OrdinalIgnoreCase);
        bool isLocalSummary = SummaryBackend.Equals("Local", StringComparison.OrdinalIgnoreCase);

        if (!isLocal && string.IsNullOrWhiteSpace(TranscriptionModel))
        {
            ValidationMessage = "Модель транскрипции не должна быть пустой.";
            return false;
        }

        if (!isLocalSummary && string.IsNullOrWhiteSpace(SummaryModel))
        {
            ValidationMessage = "Модель суммаризации не должна быть пустой.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(FfmpegPath) || !File.Exists(FfmpegPath))
        {
            ValidationMessage = "Укажите существующий путь к ffmpeg.";
            return false;
        }

        if (isLocal)
        {
            if (string.IsNullOrWhiteSpace(GigaAmModelPath) || !Directory.Exists(GigaAmModelPath))
            {
                ValidationMessage = "Укажите каталог с ONNX-моделью GigaAM-v3.";
                return false;
            }

            string[] modelFiles =
            [
                GigaAmEncoderFileName,
                GigaAmDecoderFileName,
                GigaAmJoinerFileName,
                GigaAmTokensFileName,
            ];
            foreach (string fileName in modelFiles)
            {
                if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(Path.Combine(GigaAmModelPath, fileName)))
                {
                    ValidationMessage = "Не найдены файлы модели GigaAM-v3.";
                    return false;
                }
            }
        }

        if (isLocalSummary)
        {
            if (string.IsNullOrWhiteSpace(GigaChatGgufPath) || !File.Exists(GigaChatGgufPath))
            {
                ValidationMessage = "Укажите существующий файл модели GigaChat (.gguf).";
                return false;
            }

            if (GigaChatContextSize <= 0)
            {
                ValidationMessage = "Размер контекста GigaChat должен быть положительным числом.";
                return false;
            }

            if (GigaChatGpuLayerCount < 0)
            {
                ValidationMessage = "Количество слоёв GPU не должно быть отрицательным (0 = CPU).";
                return false;
            }
        }

        _store.Save(new AppSettingsModel
        {
            FfmpegPath = FfmpegPath,
            TranscriptionModel = TranscriptionModel,
            SummaryModel = SummaryModel,
            TranscriptionBackend = isLocal ? "Local" : "External",
            SummaryBackend = isLocalSummary ? "Local" : "External",
            Language = Language,
            GigaAm = new GigaAmSettings
            {
                ModelPath = GigaAmModelPath,
                EncoderFileName = GigaAmEncoderFileName,
                DecoderFileName = GigaAmDecoderFileName,
                JoinerFileName = GigaAmJoinerFileName,
                TokensFileName = GigaAmTokensFileName,
            },
            GigaChat = new GigaChatSettings
            {
                GgufPath = GigaChatGgufPath,
                ContextSize = GigaChatContextSize,
                GpuLayerCount = GigaChatGpuLayerCount,
            },
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

    private void BrowseGigaAmModel()
    {
        string? picked = _dialog.ShowOpenFolderDialog(GigaAmModelPath);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            GigaAmModelPath = picked;
        }
    }

    private void BrowseGigaChatModel()
    {
        string? picked = _dialog.ShowOpenFileDialog(GigaChatGgufPath);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            GigaChatGgufPath = picked;
        }
    }

    private async Task DownloadGigaAmModelAsync()
    {
        string configured = string.IsNullOrWhiteSpace(GigaAmModelPath)
            ? new GigaAmSettings().ModelPath
            : GigaAmModelPath;
        string targetDirectory = GigaAmDownloader.ResolveModelDirectory(configured);
        bool confirmed = _dialog.ShowConfirmation(
            $"Скачать модель GigaAM-v3 ({GigaAmDownloader.TotalSizeDisplay}) с Hugging Face в каталог:{Environment.NewLine}{targetDirectory}",
            "Скачивание модели");
        if (!confirmed)
        {
            return;
        }

        DownloadStatus = "Скачивание модели… 0%";
        try
        {
            using HttpClient httpClient = _httpClientFactory();
            Progress<double> progress = new(fraction => DownloadStatus = $"Скачивание модели… {fraction:P0}");
            // Resume on the UI thread: the property setters below feed WPF bindings.
            await GigaAmDownloader.DownloadAsync(targetDirectory, httpClient, progress).ConfigureAwait(true);
            GigaAmModelPath = targetDirectory;
            GigaAmEncoderFileName = GigaAmDownloader.EncoderFileName;
            GigaAmDecoderFileName = GigaAmDownloader.DecoderFileName;
            GigaAmJoinerFileName = GigaAmDownloader.JoinerFileName;
            GigaAmTokensFileName = GigaAmDownloader.TokensFileName;
            ValidationMessage = null;
            DownloadStatus = "Модель скачана. Нажмите «Сохранить», чтобы применить.";
        }
        catch (Exception ex) when (ex is HttpRequestException
            or IOException
            or InvalidOperationException
            or TaskCanceledException)
        {
            DownloadStatus = null;
            ValidationMessage = $"Не удалось скачать модель: {ex.Message}";
        }
    }

    private async Task DownloadGigaChatModelAsync()
    {
        string configured = string.IsNullOrWhiteSpace(GigaChatGgufPath)
            ? new GigaChatSettings().GgufPath
            : GigaChatGgufPath;
        string targetFilePath = GigaChatDownloader.ResolveModelFilePath(configured);
        bool confirmed = _dialog.ShowConfirmation(
            $"Скачать модель GigaChat3.1 ({GigaChatDownloader.DisplaySize}) с Hugging Face в файл:{Environment.NewLine}{targetFilePath}",
            "Скачивание модели");
        if (!confirmed)
        {
            return;
        }

        SummaryDownloadStatus = "Скачивание модели… 0%";
        try
        {
            using HttpClient httpClient = _httpClientFactory();
            Progress<double> progress = new(fraction => SummaryDownloadStatus = $"Скачивание модели… {fraction:P0}");
            // Resume on the UI thread: the property setters below feed WPF bindings.
            await GigaChatDownloader.DownloadAsync(targetFilePath, httpClient, progress).ConfigureAwait(true);
            GigaChatGgufPath = targetFilePath;
            ValidationMessage = null;
            SummaryDownloadStatus = "Модель скачана. Нажмите «Сохранить», чтобы применить.";
        }
        catch (Exception ex) when (ex is HttpRequestException
            or IOException
            or InvalidOperationException
            or TaskCanceledException)
        {
            SummaryDownloadStatus = null;
            ValidationMessage = $"Не удалось скачать модель: {ex.Message}";
        }
    }
}
