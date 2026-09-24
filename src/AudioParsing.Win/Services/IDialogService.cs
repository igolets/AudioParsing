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

    public bool ShowConfirmation(string message, string caption);
}
