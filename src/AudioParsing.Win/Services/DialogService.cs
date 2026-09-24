using System.IO;
using System.Windows;
using AudioParsing.Win.ViewModels;
using AudioParsing.Win.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AudioParsing.Win.Services;

/// <summary>
/// WPF implementation of <see cref="IDialogService"/>.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _services;

    public DialogService(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void ShowSettings()
    {
        SettingsViewModel viewModel = _services.GetRequiredService<SettingsViewModel>();
        SettingsWindow window = new()
        {
            Owner = Application.Current?.MainWindow,
            DataContext = viewModel,
        };
        window.ShowDialog();
    }

    public void ShowWarning(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        MessageBox.Show(Application.Current?.MainWindow, message, "AudioParsing", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        MessageBox.Show(Application.Current?.MainWindow, message, "AudioParsing", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public string? ShowOpenFileDialog(string? initialPath)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Выберите ffmpeg",
            Filter = "ffmpeg (ffmpeg*.exe)|ffmpeg*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            try
            {
                dialog.FileName = Path.GetFileName(initialPath);
                string? directory = Path.GetDirectoryName(initialPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }
            catch (ArgumentException)
            {
                // Fall through with dialog defaults when the stored path is malformed.
            }
        }

        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
}
