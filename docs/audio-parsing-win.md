# AudioParsing.Win — WPF Shell for the Existing Audio Parsing Pipeline

Status: Implemented
Date: 2026-09-23
Scope: Add a Windows WPF desktop app (`AudioParsing.Win`) that drives the existing
`AudioParsing.Core` pipeline via drag-and-drop, with an editable settings dialog.

> Implementation record (2026-09-23): built as specified. Decisions on §10 open
> questions: (Q1) the log stays visible after completion instead of auto-returning
> to the drop zone — dropping more files at any time starts a new run; (Q4) a drop
> while `IsProcessing` is declined with a warning, no queueing. Analyzer notes:
> `CA1515` suppressed project-wide (test assembly references the public VMs/services);
> UI-affine awaits use explicit `.ConfigureAwait(true)`; `Progress<T>` callbacks
> marshal to the dispatcher. Test projects: `tests/AudioParsing.Win.Tests`
> (19 tests) plus `ProcessFilesAsync` progress cases in `AudioParsing.Tests`.
> Verify: `dotnet build`, `dotnet test` (64/64 green), `dotnet format --verify-no-changes`.

---

## 1. Context (grounded in current repo)

| Area | Current state (verified) |
|------|--------------------------|
| Framework | .NET 10, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`, `EnforceCodeStyleInBuild=true` (`Directory.Build.props`) |
| Projects | `src/AudioParsing` (console, Exe), `src/AudioParsing.Core` (library), `tests/AudioParsing.Tests` (xUnit) |
| Config | `src/AudioParsing/appsettings.json` copied to output; keys: `FfmpegPath`, `TranscriptionModel`, `SummaryModel` |
| Config access | `AudioParsing.AppSettings` static reader; `GetFfmpegPath/GetTranscriptionModel/GetSummaryModel`; resolves `AppContext.BaseDirectory` then CWD; **read-only** |
| API key | `RouterAiClient.FromEnvironment()` reads env var `ANTHROPIC_AUTH_TOKEN` |
| Pipeline | `AudioParsing.AudioPipeline(RouterAiClient, ffmpegPath, model, language, summaryModel)`; `ProcessFolderAsync(folder, force, ct)` → `IReadOnlyList<AudioFileResult>` |
| Stages per file | skip-if-md-exists → compress via `FfmpegCompressor` (only if > 25 MB) → `TranscribeAsync` → `GenerateSummaryAsync` → `MarkdownDocument.Build` → write sibling `.md` |
| Language | CLI-only, default `ru`; not in appsettings.json |

**Gap:** `AudioPipeline.ProcessFolderAsync` scans a *folder* and `ProcessFileAsync` is
`private`. Drag-and-drop yields an explicit *file list* that may span folders, so a
public per-file entry point with progress reporting is required.

---

## 2. Requirements → Design Mapping

| # | Requirement | Design |
|---|-------------|--------|
| 1 | Non-resizable window, Settings button on top | `MainWindow` with `ResizeMode="NoResize"`, top `DockPanel` toolbar |
| 2 | Settings dialog edits all appsettings.json variables | `SettingsWindow` + `SettingsViewModel` over an editable `AppSettingsModel` |
| 3 | Drop area with Russian hint text | `Border` overlay bound to `IsDropZoneVisible` |
| 4 | Dropped files processed as console does; `.md` beside each audio file | `PipelineRunner` → `AudioPipeline.ProcessFilesAsync` (additive Core method) |
| 5 | Drop area swaps to log; per-stage updates | `IsDropZoneVisible`/`IsLogVisible` swap; `IProgress<PipelineProgress>` stream |
| 6 | MVVM, async/await, DI, error handling, logging | MVVM primitives, `Microsoft.Extensions.Hosting` DI, `IProgress`, `ILogger` |

---

## 3. Project Structure (affected files)

```
src/AudioParsing.Win/
  AudioParsing.Win.csproj          # NEW  WinExe, net10.0-windows, UseWPF
  App.xaml / App.xaml.cs           # NEW  Generic Host + DI bootstrap
  appsettings.json                 # NEW  copy-to-output (FfmpegPath/TranscriptionModel/SummaryModel)
  Views/
    MainWindow.xaml / .xaml.cs     # NEW  toolbar + drop zone + log; drag/drop handlers
    SettingsWindow.xaml / .xaml.cs # NEW  settings form
  ViewModels/
    ObservableObject.cs            # NEW  INotifyPropertyChanged base
    RelayCommand.cs                # NEW  sync ICommand
    AsyncRelayCommand.cs           # NEW  async ICommand (no UI-thread blocking)
    MainWindowViewModel.cs         # NEW  state machine + log stream
    SettingsViewModel.cs           # NEW  load/validate/save settings
    LogEntryViewModel.cs           # NEW  timestamp + level + message
  Services/
    ISettingsStore.cs / JsonSettingsStore.cs   # NEW  load/save appsettings.json
    IPipelineRunner.cs / PipelineRunner.cs     # NEW  builds client+pipeline, runs files, maps progress
    IApiKeyProvider.cs / EnvApiKeyProvider.cs  # NEW  ANTHROPIC_AUTH_TOKEN access
    IDialogService.cs / DialogService.cs       # NEW  opens windows/dialogs (keeps VMs WPF-free)
  Logging/
    FileLoggerProvider.cs          # NEW  ILogger → %LocalAppData%\AudioParsing\logs

src/AudioParsing.Core/             # MODIFIED (additive, console behaviour preserved)
  AudioProgress.cs                 # NEW  PipelineStage enum + PipelineProgress record
  AudioPipeline.cs                 # MOD  add ProcessFilesAsync + IProgress param
  AppSettings.cs                   # MOD  delegate to AppSettingsFile (single parser)

src/AudioParsing.Core/
  AppSettingsFile.cs               # NEW  AppSettingsModel + Load/Save/ResolvePath

tests/AudioParsing.Win.Tests/      # NEW  net10.0-windows xUnit
  MainWindowViewModelTests.cs
  SettingsViewModelTests.cs
  PipelineRunnerTests.cs
  JsonSettingsStoreTests.cs

AudioParsing.sln                   # MOD  add AudioParsing.Win + AudioParsing.Win.Tests
```

### `AudioParsing.Win.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework> <!-- override Directory.Build.props -->
    <UseWPF>true</UseWPF>
    <RootNamespace>AudioParsing.Win</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AudioParsing.Core\AudioParsing.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

> Note: `TargetFramework` must be overridden because `Directory.Build.props` sets a global
> `net10.0`. WPF analyzers can emit warnings that `TreatWarningsAsErrors` promotes to errors;
> plan to add narrowly-scoped `NoWarn` entries only if the analyzer set requires it.

---

## 4. Reuse Strategy — Shared Library vs Reference

**Decision: reference the existing `AudioParsing.Core` library directly; do not fork logic.**

The console and Win shells become two hosts over one pipeline. Core needs two *additive* changes,
both backward compatible (default parameters preserve the console path):

### 4.1 Progress contract — `src/AudioParsing.Core/AudioProgress.cs` (NEW)

```csharp
namespace AudioParsing;

public enum PipelineStage { Reading, Compressing, Transcribing, Summarizing, Writing, Completed, Skipped, Failed }

public sealed record PipelineProgress(string AudioPath, PipelineStage Stage, string Message);
```

### 4.2 Public per-file entry point — `AudioPipeline` (MOD)

```csharp
public async Task<IReadOnlyList<AudioFileResult>> ProcessFilesAsync(
    IReadOnlyList<string> audioPaths,
    bool force = false,
    IProgress<PipelineProgress>? progress = null,
    CancellationToken cancellationToken = default)
{
    List<AudioFileResult> results = new();
    foreach (string path in audioPaths)
        results.Add(await ProcessFileAsync(path, force, progress, cancellationToken).ConfigureAwait(false));
    return results;
}

// ProcessFolderAsync now: find files, then delegate to ProcessFilesAsync (progress optional).
// ProcessFileAsync gains `IProgress<PipelineProgress>? progress` and calls
//   progress?.Report(new(sourcePath, PipelineStage.Compressing, "..."))
//   progress?.Report(new(sourcePath, PipelineStage.Transcribing, "..."))
//   progress?.Report(new(sourcePath, PipelineStage.Summarizing, "..."))
//   progress?.Report(new(sourcePath, PipelineStage.Writing, "..."))
//   progress?.Report(new(sourcePath, PipelineStage.Completed / Skipped / Failed, "..."))
```

Rationale: single source of truth for stage ordering and error mapping; the Win shell only renders events.

### 4.3 Settings model + persistence — `src/AudioParsing.Core/AppSettingsFile.cs` (NEW)

```csharp
public sealed class AppSettingsModel
{
    public string FfmpegPath { get; set; } = AppSettings.DefaultFfmpegPath;
    public string TranscriptionModel { get; set; } = AudioPipeline.DefaultModel;
    public string SummaryModel { get; set; } = AudioPipeline.DefaultSummaryModel;
}

public static class AppSettingsFile
{
    public static string ResolvePath(string? overridePath = null); // BaseDirectory, then CWD
    public static AppSettingsModel Load(string? path = null);
    public static void Save(AppSettingsModel model, string? path = null); // atomic temp-write + replace
}
```

`AppSettings.cs` keeps its public getters, delegating to `AppSettingsFile.Load()` to avoid a second
JSON parser. `Save` writes via a temp file + `File.Move(overwrite: true)` to avoid partial writes.

**Decision on write target:** write to the same file `ResolvePath` returns (output-dir
`appsettings.json` in dev). If the app ships under `Program Files`, add a `%AppData%\AudioParsing\appsettings.json`
override consulted *before* the exe-dir file (flagged as an open question, §10).

---

## 5. Window Layouts (XAML outlines)

### 5.1 `MainWindow.xaml`

```xml
<Window x:Class="AudioParsing.Win.Views.MainWindow"
        Title="AudioParsing" Width="720" Height="480"
        ResizeMode="NoResize" AllowDrop="True"
        DragOver="OnDragOver" Drop="OnDrop">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <!-- Toolbar -->
    <DockPanel Grid.Row="0" Margin="8">
      <Button Content="Настройки" Command="{Binding OpenSettingsCommand}"
              HorizontalAlignment="Right" Padding="12,4"/>
    </DockPanel>

    <!-- Drop zone (visible when idle) -->
    <Border Grid.Row="1" Visibility="{Binding IsDropZoneVisible, Converter={StaticResource BoolToVis}}">
      <TextBlock Text="Перетащите файлы на это окно для обработки."
                 HorizontalAlignment="Center" VerticalAlignment="Center"
                 FontSize="20" TextWrapping="Wrap" TextAlignment="Center"/>
    </Border>

    <!-- Log (visible while processing) -->
    <ScrollViewer Grid.Row="1" Visibility="{Binding IsLogVisible, Converter={StaticResource BoolToVis}}"
                  VerticalScrollBarVisibility="Auto">
      <ItemsControl ItemsSource="{Binding Log}" Margin="12">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <TextBlock Text="{Binding Display}" TextWrapping="Wrap" Margin="0,2"/>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </ScrollViewer>
  </Grid>
</Window>
```

Code-behind (drag/drop + async boundary only):

```csharp
private void OnDragOver(object sender, DragEventArgs e)
{
    e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    e.Handled = true;
}

private async void OnDrop(object sender, DragEventArgs e) // async void only at event boundary
{
    if (ViewModel is null || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
    try { await ViewModel.ProcessDroppedFilesAsync(paths); }
    catch (Exception ex) { ViewModel.ReportUnhandled(ex); }
}
```

`MainWindow.DataContext` is injected (`MainWindowViewModel`), not resolved service-locator style.
`BoolToVis` is the built-in `BooleanToVisibilityConverter` registered in `App.xaml` resources.

### 5.2 `SettingsWindow.xaml`

```xml
<Window x:Class="AudioParsing.Win.Views.SettingsWindow"
        Title="Настройки" Width="560" Height="300" ResizeMode="NoResize">
  <Grid Margin="16">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/><RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <!-- FfmpegPath + Browse -->
    <DockPanel Grid.Row="0" Margin="0,0,0,12">
      <TextBlock Text="Путь к ffmpeg" Width="150"/>
      <Button DockPanel.Dock="Right" Content="Обзор…" Command="{Binding BrowseFfmpegCommand}" Padding="8,2"/>
      <TextBox Text="{Binding FfmpegPath, UpdateSourceTrigger=PropertyChanged}"/>
    </DockPanel>

    <DockPanel Grid.Row="1" Margin="0,0,0,12">
      <TextBlock Text="Модель транскрипции" Width="150"/>
      <TextBox Text="{Binding TranscriptionModel, UpdateSourceTrigger=PropertyChanged}"/>
    </DockPanel>

    <DockPanel Grid.Row="2" Margin="0,0,0,12">
      <TextBlock Text="Модель суммаризации" Width="150"/>
      <TextBox Text="{Binding SummaryModel, UpdateSourceTrigger=PropertyChanged}"/>
    </DockPanel>

    <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Bottom">
      <TextBlock Text="{Binding ValidationMessage}" Foreground="#B00020" Margin="0,0,16,0" VerticalAlignment="Center"/>
      <Button Content="Сохранить" Command="{Binding SaveCommand}" IsDefault="True" Padding="16,4" Margin="0,0,8,0"/>
      <Button Content="Отмена" IsCancel="True" Padding="16,4"/>
    </StackPanel>
  </Grid>
</Window>
```

`SaveCommand` validates (non-empty models; ffmpeg path exists) and raises a `CloseRequested`
event (or sets `DialogResult` via the injected `IDialogService`) so the VM stays testable.

---

## 6. ViewModels

### `MainWindowViewModel`

```csharp
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IPipelineRunner _runner;
    private readonly IDialogService _dialog;
    private readonly ILogger<MainWindowViewModel> _log;

    public ObservableCollection<LogEntryViewModel> Log { get; } = new();
    public bool IsDropZoneVisible { get; private set; } = true;
    public bool IsLogVisible => !IsDropZoneVisible;
    public bool IsProcessing { get; private set; }

    public ICommand OpenSettingsCommand { get; }

    public async Task ProcessDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> audio = paths
            .Where(File.Exists)
            .Where(p => AudioFileFinder.IsAudioFile(p))
            .ToList();

        if (audio.Count == 0) { _dialog.ShowWarning("Не найдено поддерживаемых аудиофайлов."); return; }

        Log.Clear();
        IsDropZoneVisible = false;   // swap drop zone -> log
        IsProcessing = true;
        try
        {
            // Progress<T> created on the UI thread posts callbacks back to the UI thread.
            var progress = new Progress<PipelineProgress>(p => Log.Add(LogEntryViewModel.From(p)));
            IReadOnlyList<AudioFileResult> results =
                await _runner.ProcessAsync(audio, progress, CancellationToken.None);
            AppendSummary(results);
        }
        catch (Exception ex) { ReportUnhandled(ex); }
        finally { IsProcessing = false; }
    }

    public void ReportUnhandled(Exception ex)
    {
        _log.LogError(ex, "Unhandled failure during processing");
        Log.Add(LogEntryViewModel.Error(ex.Message));
    }
}
```

State machine: `Idle (drop zone)` → `Processing (log)` → `Idle (drop zone)` restored after completion
(or a "Готово" summary line is appended before restoring — see §10 open question on whether to keep the log visible).

### `SettingsViewModel`

```csharp
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    public string FfmpegPath { get; set; }
    public string TranscriptionModel { get; set; }
    public string SummaryModel { get; set; }
    public string? ValidationMessage { get; private set; }
    public ICommand SaveCommand { get; }
    public ICommand BrowseFfmpegCommand { get; } // uses Microsoft.Win32.OpenFileDialog via IDialogService

    public void Load() { var m = _store.Load(); FfmpegPath = m.FfmpegPath; /* ... */ }
    public bool TrySave() { /* validate -> _store.Save(new AppSettingsModel { ... }) */ }
}
```

---

## 7. Services

```csharp
public interface ISettingsStore { string SettingsFilePath { get; } AppSettingsModel Load(); void Save(AppSettingsModel model); }
public sealed class JsonSettingsStore : ISettingsStore { /* wraps AppSettingsFile */ }

public interface IPipelineRunner
{
    Task<IReadOnlyList<AudioFileResult>> ProcessAsync(
        IReadOnlyList<string> files, IProgress<PipelineProgress> progress, CancellationToken ct);
}

public sealed class PipelineRunner : IPipelineRunner
{
    private readonly ISettingsStore _settings;   // read latest values per run
    private readonly IApiKeyProvider _apiKey;
    public async Task<IReadOnlyList<AudioFileResult>> ProcessAsync(...)
    {
        AppSettingsModel s = _settings.Load();
        string key = _apiKey.GetApiKey()
            ?? throw new InvalidOperationException(
                $"Не задан ключ API. Задайте переменную среды {EnvApiKeyProvider.EnvVarName}.");
        using var client = new RouterAiClient(key);
        var pipeline = new AudioPipeline(client, s.FfmpegPath, s.TranscriptionModel, AudioPipeline.DefaultLanguage, s.SummaryModel);
        return await pipeline.ProcessFilesAsync(files, force: false, progress, ct).ConfigureAwait(false);
    }
}

public interface IDialogService { void ShowSettings(); void ShowWarning(string m); void ShowError(string m); }
```

Settings are re-read at the start of every run, so edits made in `SettingsWindow` take effect on the
next drop without restarting the app.

---

## 8. DI Bootstrap (`App.xaml.cs`)

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
    builder.Services.AddSingleton<IApiKeyProvider, EnvApiKeyProvider>();
    builder.Services.AddSingleton<IPipelineRunner, PipelineRunner>();
    builder.Services.AddSingleton<IDialogService, DialogService>();
    builder.Services.AddSingleton<MainWindowViewModel>();
    builder.Services.AddTransient<SettingsViewModel>();
    builder.Logging.ClearProviders();
    builder.Logging.AddDebug();
    builder.Logging.AddProvider(new FileLoggerProvider());

    _host = builder.Build();
    await _host.StartAsync();
    var window = new MainWindow { DataContext = _host.Services.GetRequiredService<MainWindowViewModel>() };
    window.Show();
}
```

`IDialogService` holds `Func<SettingsViewModel>` (or the `IServiceProvider`) to construct the settings
window, keeping VMs free of WPF window types → unit-testable on `net10.0-windows` without a UI thread.

---

## 9. Threading, Progress & Error Handling

- **No UI blocking:** the drop handler is the only `async void`; it `await`s the VM, which `await`s the runner.
- **Progress marshaling:** `new Progress<PipelineProgress>(...)` is created on the UI thread, so its
  captured `SynchronizationContext` posts every callback to the dispatcher — safe `ObservableCollection` mutation.
- **Cancellation:** a `CancellationTokenSource` in the VM (Cancel button optional) is threaded through to `ProcessFilesAsync`.
- **Per-file errors:** already mapped to `AudioFileResult { Success=false, Error }` by Core; the VM logs
  `[fail] path: error` and prints a `Done: n/m` summary. A failed file never aborts the batch.
- **Global errors:** missing env var / missing ffmpeg / bad settings → caught in the VM, shown via
  `IDialogService`, and logged with `ILogger`.
- **Logging:** `ILogger` for diagnostics + `FileLoggerProvider` to `%LocalAppData%\AudioParsing\logs\win-yyyyMMdd.log`;
  the in-app `Log` collection is the user-facing progress surface.

---

## 10. Trade-Offs & Open Questions

| Decision | Chosen | Alternatives | Why |
|----------|--------|--------------|-----|
| Core reuse | Reference `AudioParsing.Core` | Duplicate logic in Win | One pipeline; console unaffected by additive defaults |
| Per-file API | Add `ProcessFilesAsync` + `IProgress` | Call `ProcessFolderAsync` per directory | Avoids reprocessing unrelated sibling files |
| Progress channel | `IProgress<T>` (push) | `event`/`Channel`/polling | `Progress<T>` auto-marshals to UI thread; no consumer back-pressure needed |
| MVVM primitives | Hand-rolled `ObservableObject`/`AsyncRelayCommand` | `CommunityToolkit.Mvvm` | Honors AGENTS.md "no new deps without checking"; tiny surface. **Alternative:** add CommunityToolkit.Mvvm to cut boilerplate |
| Settings write target | exe-dir `appsettings.json` | `%AppData%` user override | Matches current reader; **Open Q** for Program Files deployments |
| API key | Env var only | Persist key in settings dialog | Matches current `RouterAiClient.FromEnvironment`. **Open Q:** add optional key field (user env / DPAPI store) for Explorer-launched apps |
| Language | Keep Core default `ru` | Expose in settings | Console parity; **optional** to add a `Language` key |

**Open questions for review**
1. Should the log remain visible after completion, or auto-return to the drop zone?
2. Persist settings to exe-dir or a per-user `%AppData%` override file (Program Files scenario)?
3. Add optional API-key field to `SettingsWindow` (env var inheritance is unreliable for double-click launches)?
4. Allow multiple concurrent drops / queueing, or disable drop while `IsProcessing`?
5. Should `Language` and `--force` (re-process) be exposed as settings toggles?

---

## 11. Test Plan

| Layer | Project | Cases |
|-------|---------|-------|
| Settings persist | `AudioParsing.Win.Tests` | `JsonSettingsStore` round-trip (temp file); malformed JSON → defaults; atomic save |
| Progress mapping | `AudioParsing.Win.Tests` | `PipelineRunner` emits Compressing/Transcribing/Summarizing/Writing/Completed in order for a >25 MB file (fake `IApiKeyProvider` + stubbed pipeline seam) |
| Main VM state | `AudioParsing.Win.Tests` | drop zone → log swap; non-audio paths filtered; empty drop shows warning; per-file failure logged, batch continues; `IsDropZoneVisible` restored |
| Settings VM | `AudioParsing.Win.Tests` | validation rejects empty models; invalid ffmpeg path; `TrySave` persists |
| Core (existing) | `AudioParsing.Tests` | add: `ProcessFilesAsync` returns one result per input; `progress` reports `Skipped` when `.md` exists; `ProcessFolderAsync` unchanged |
| Manual UI | — | Non-resizable window; drag/drop `.mp3`; Russian hint text; log shows each stage; `.md` created beside the audio file |

VM tests require `net10.0-windows` (to reference the Win project) but need no UI thread because VMs
carry no WPF types (`ICommand` lives in `System.ObjectModel`).

---

## 12. Implementation Steps

1. **Scaffold** `src/AudioParsing.Win` (`WinExe`, `net10.0-windows`, `UseWPF`) + `appsettings.json` copy rule; add project to `AudioParsing.sln`.
2. **Core additive** — `AppSettingsFile.cs`, `AudioProgress.cs`; add `ProcessFilesAsync` + `IProgress` to `AudioPipeline`; delegate `AppSettings` getters. Run `AudioParsing.Tests`.
3. **Services** — `JsonSettingsStore`, `EnvApiKeyProvider`, `PipelineRunner`, `DialogService`, `FileLoggerProvider`.
4. **ViewModels** — `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`, `LogEntryViewModel`, `MainWindowViewModel`, `SettingsViewModel`.
5. **Views** — `MainWindow.xaml(.cs)` (toolbar, drop zone/log swap, drag/drop), `SettingsWindow.xaml(.cs)`; register `BoolToVis`.
6. **DI bootstrap** — `App.xaml(.cs)` with `Host.CreateApplicationBuilder`.
7. **Tests** — `tests/AudioParsing.Win.Tests`; add project to solution.
8. **Verify** per AGENTS.md: `dotnet build AudioParsing.sln -c Release`, `dotnet test AudioParsing.sln -c Release`, `dotnet format AudioParsing.sln --verify-no-changes`.

---

## 13. ADR Summary

- **ADR-001 — Reuse Core via project reference (Accepted).** One pipeline, two hosts. Negative: Core gains an additive public API and must stay UI-agnostic (no `System.Windows` references).
- **ADR-002 — `IProgress<PipelineProgress>` as the progress channel (Accepted).** Marshals to UI automatically. Negative: synchronous callback; large batches add GC churn (negligible at audio-file scale).
- **ADR-003 — Write settings to the resolved `appsettings.json` (Proposed).** Simple and matches the reader. Negative: fails under Program Files; per-user override tracked in §10.
