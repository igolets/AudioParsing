namespace AudioParsing.Win.Services;

/// <summary>
/// Opens windows and dialogs on behalf of view models, keeping them free of WPF types.
/// </summary>
public interface IDialogService
{
    public void ShowSettings();

    public void ShowWarning(string message);

    public void ShowError(string message);

    public string? ShowOpenFileDialog(string? initialPath);

    public string? ShowOpenFolderDialog(string? initialPath);

    /// <summary>
    /// Shows a multi-select file dialog filtered to supported audio/video files.
    /// Returns <c>null</c> when the user cancels.
    /// </summary>
    public IReadOnlyList<string>? ShowOpenAudioFilesDialog();

    public bool ShowConfirmation(string message, string caption);
}
