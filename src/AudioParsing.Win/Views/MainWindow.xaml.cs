using System.Windows;
using AudioParsing.Win.ViewModels;

namespace AudioParsing.Win.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is null || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        try
        {
            // Resume on the UI thread: failure reporting mutates the bound log.
            await ViewModel.ProcessDroppedFilesAsync(paths).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Event boundary: any failure surfaces as a log line instead of crashing the app.
        catch (Exception ex)
        {
            ViewModel.ReportUnhandled(ex);
        }
#pragma warning restore CA1031
    }
}
