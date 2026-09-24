using System.Windows;
using AudioParsing.Win.Logging;
using AudioParsing.Win.Services;
using AudioParsing.Win.ViewModels;
using AudioParsing.Win.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AudioParsing.Win;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        builder.Services.AddSingleton<IApiKeyProvider, EnvApiKeyProvider>();
        builder.Services.AddSingleton<IPipelineRunner, PipelineRunner>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
#pragma warning disable CA2000 // Ownership transfers to the host's LoggerFactory, which disposes providers.
        builder.Logging.AddProvider(new FileLoggerProvider());
#pragma warning restore CA2000

        _host = builder.Build();
        // Resume on the UI thread: the continuation creates and shows the main window.
        await _host.StartAsync().ConfigureAwait(true);

        MainWindow window = new()
        {
            DataContext = _host.Services.GetRequiredService<MainWindowViewModel>(),
        };
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(true);
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
