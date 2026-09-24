using System.Windows;
using AudioParsing.Win.ViewModels;

namespace AudioParsing.Win.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= OnCloseRequested;
        }

        if (e.NewValue is SettingsViewModel newViewModel)
        {
            newViewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, DialogCloseRequestedEventArgs e)
    {
        if (e.DialogResult is not null)
        {
            DialogResult = e.DialogResult;
        }

        Close();
    }
}
