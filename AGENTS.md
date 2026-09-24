# Coding Agent Guidelines (`AGENTS.md`)

This repository is designed to work seamlessly with autonomous coding agents (e.g., Cursor, Claude Code, GitHub Copilot, Aider, Devin). All agents working in this codebase **must** adhere to the principles and instructions outlined below.

---

## 1. Core Principles

1. **Do No Harm:** Never break existing functionality. Run relevant tests before submitting changes.
2. **Minimize Context Overhead:** Produce clean, focused, and minimal modifications. Avoid refactoring code outside the scope of the assigned task unless requested.
3. **Deterministic Output:** Prefer explicit code over implicit behavior. Follow existing patterns and code conventions used across the project.
4. **Single File Rule for Artifacts:** If instructed to generate web pages or single-file scripts, consolidate logic, styles, and templates into the single file unless multi-file architecture is explicitly specified.

---

## 2. Project Architecture & Setup

### 2.1 Solution layout (actual)

```text
AudioParsing.sln
LICENSE                      # MIT license text (declared as PackageLicenseExpression=MIT in Directory.Build.props)
Directory.Build.props          # global: net10.0, ImplicitUsings, Nullable, TreatWarningsAsErrors, AnalysisLevel latest-all, EnforceCodeStyleInBuild, PackageLicenseExpression=MIT
.editorconfig                  # lf, utf-8, 4 spaces; C# rules enforced in build
docs/
  audio-parsing-win.md         # Win WPF shell plan/spec (24 KB)
  audio-parsing-video.md       # video-handling plan/spec (16 KB)
  lecture_processing_task.md   # selectable STT backend spec (External RouterAI Whisper vs Local GigaAM-v3)
  local_summary_processing_task.md # selectable summary backend spec (External luna vs Local GigaChat GGUF)
src/
  AudioParsing.Core/           # class library, net10.0 (via Directory.Build.props)
  AudioParsing.LocalStt/       # on-device GigaAM-v3 via sherpa-onnx (Apache-2.0), net10.0, refs Core
  AudioParsing.LocalSummary/   # on-device GigaChat GGUF via LLamaSharp (MIT), net10.0, refs Core
  AudioParsing.Console/        # console host, OutputType=Exe, net10.0, refs Core + LocalStt + LocalSummary
  AudioParsing.Win/            # WPF host, OutputType=WinExe, net10.0-windows, UseWPF, refs Core + LocalStt + LocalSummary
tests/
  AudioParsing.Tests/          # xUnit, net10.0 — 19 files covering Core + LocalStt + LocalSummary + Console config
  AudioParsing.Win.Tests/      # xUnit, net10.0-windows — 4 files covering Win VM/services
```

### 2.2 Projects and dependencies

* **`src/AudioParsing.Core`** (no third-party packages): `AudioPipeline` (+ `AudioFileResult`), `AudioFileFinder`, `FfmpegCompressor`, `RouterAiClient`, `MarkdownDocument`, `AppSettings` (read-only facade) + `AppSettingsFile`/`AppSettingsModel`/`GigaAmSettings`/`GigaChatSettings` (shared JSON parser), `TranscriptionBackend` + `IAudioTranscriber` + `RouterAiTranscriber` (selectable STT backend), `SummaryBackend` + `ISummaryGenerator` + `RouterAiSummaryGenerator` (selectable summary backend), `TranscriptChunker` + `ChunkingSummaryGenerator` (deterministic map-reduce for long transcripts, local backend only), `GigaAmDownloader` (Hugging Face model download: `RequiredFiles` e2e_rnnt set, `ResolveModelDirectory` = rooted as-is else exe-relative, `ApplyDownloadedFileNames`), `GigaChatDownloader` (single-GGUF download: `RequiredFiles` one file, `ResolveModelFilePath` = rooted as-is else exe-relative, `ApplyDownloadedFileName`), `AudioProgress` (`PipelineStage`, `PipelineProgress`), `Greeting`.
* **`src/AudioParsing.LocalStt`** (refs Core; `org.k2fsa.sherpa.onnx` 1.13.8, Apache-2.0): `GigaAmTranscriber` (on-device GigaAM-v3 transducer via sherpa-onnx `OfflineRecognizer`, 16 kHz mono PCM WAV input, native load is lazy so model failures surface per-file), `WavReader` (16-bit PCM WAV parser).
* **`src/AudioParsing.LocalSummary`** (refs Core; `LLamaSharp` + `LLamaSharp.Backend.Cpu` 0.27.0, MIT): `GigaChatSummaryGenerator` (on-device GigaChat3.1 GGUF via `LLamaWeights`/`LLamaContext` + per-call `ChatSession`, reuses `RouterAiClient.SummarySystemPrompt` verbatim at temperature 0.2, native load is lazy so model failures surface per-file), `ResolveGgufPath` (unit-testable GGUF validation).
* **`src/AudioParsing.Console`** (`Exe`, refs Core + LocalStt + LocalSummary): `Program.cs` (`--backend <external|local>`, `--gigaam-model <path>`, `--download-gigaam` with y/N confirmation + progress, saves `GigaAm` section; `--summary-backend <external|local>`, `--gigachat-model <path>` (dir gets default file name appended), `--download-gigachat` with y/N confirmation + progress, saves `GigaChat` section and sets `SummaryBackend: Local` only when unconfigured; `ANTHROPIC_AUTH_TOKEN` required only when STT or summary is `External`), `appsettings.json` (`CopyToOutputDirectory=PreserveNewest`). No hosting/logging packages.
* **`src/AudioParsing.Win`** (`WinExe`, `net10.0-windows` override, `UseWPF`, refs Core + LocalStt + LocalSummary): `App.xaml(.cs)` Generic Host + DI bootstrap, `Views/MainWindow.xaml(.cs)` + `Views/SettingsWindow.xaml(.cs)` (transcription + summary backend ComboBoxes, language, GigaAM model dir + file names, GigaChat GGUF file + context size + GPU layers), `ViewModels/` (`MainWindowViewModel`, `SettingsViewModel`, `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`, `LogEntryViewModel`), `Services/` (`IPipelineRunner`/`PipelineRunner` (both backends aware, client-optional), `ISettingsStore`/`JsonSettingsStore`, `IApiKeyProvider`/`EnvApiKeyProvider`, `IDialogService`/`DialogService` (+ `ShowOpenFolderDialog`)), `Logging/FileLoggerProvider`, `appsettings.json` (copy-to-output). Packages: `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.Logging` 10.0.0. `NoWarn: CA1515` (public VM/service API intentionally referenced by tests).
* **`tests/AudioParsing.Tests`**: `AppSettingsTests`, `AudioFileFinderTests`, `AudioPipelineTests`, `AudioPipelineProgressTests`, `FfmpegCompressorTests`, `MarkdownDocumentTests`, `RouterAiTranscriptionTests`, `RouterAiSummaryTests`, `RouterAiTranscriberTests`, `RouterAiSummaryGeneratorTests`, `TranscriptChunkerTests`, `ChunkingSummaryGeneratorTests`, `GigaAmTranscriberTests`, `GigaAmDownloaderTests`, `GigaChatDownloaderTests`, `GigaChatSummaryGeneratorTests`, `WavReaderTests`, `GreetingTests`. Packages: `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.0 (test-only; see §2.5 Licensing). Refs Core + LocalStt + LocalSummary.
* **`tests/AudioParsing.Win.Tests`**: `MainWindowViewModelTests`, `SettingsViewModelTests`, `WinServiceTests` (`JsonSettingsStore`, `PipelineRunner`), `TestDoubles` (+ `FakeAudioTranscriber`, `FakeSummaryGenerator`). `NoWarn: CA1812`. Refs both `AudioParsing.Win` and `AudioParsing.Core`.

### 2.3 Domain: what the code actually does

Audio/video files are transcribed with whisper via RouterAI (`https://routerai.ru/api/v1`, key from `ANTHROPIC_AUTH_TOKEN` env var), summarized with luna, and written as sibling `.md` files (`MarkdownDocument.Build`).

* `AudioFileFinder`: supported audio (`.mp3/.m4a/.wav/.mpga/.mpeg/...`) vs. video (`.mp4/.mkv/.mov/.avi/.webm/...`) extensions; `FindAudioFiles(folder)` sorted; video always goes through ffmpeg, audio only when > 25 MB (`MaxUploadBytes`).
* `AudioPipeline` defaults: `DefaultModel=openai/whisper-large-v3-turbo`, `DefaultSummaryModel=openai/gpt-6-luna`, `DefaultLanguage=ru`. `ProcessFolderAsync(folder, force, progress, ct)` delegates to public `ProcessFilesAsync(files, force, progress, ct)` (explicit file list for drag-and-drop). Per-file stages reported via `IProgress<PipelineProgress>`: Compressing → Transcribing → Summarizing → Writing → Completed/Skipped/Failed. Failures are returned as `AudioFileResult`, never abort the batch; temp ffmpeg output is deleted in `finally`.
* Transcription backend: `TranscriptionBackend.External` (default, RouterAI Whisper via `RouterAiTranscriber`) vs `Local` (on-device GigaAM-v3 via `GigaAmTranscriber` in `AudioParsing.LocalStt`). Local always normalizes through ffmpeg to 16 kHz mono PCM WAV (`FfmpegCompressor.ExtractPcmWavAsync`).
* Summarization backend: `SummaryBackend.External` (default, RouterAI luna via `RouterAiSummaryGenerator`) vs `Local` (on-device GigaChat3.1 GGUF via `GigaChatSummaryGenerator` in `AudioParsing.LocalSummary`, wrapped in `ChunkingSummaryGenerator` map-reduce with the same `SummarySystemPrompt`). `ANTHROPIC_AUTH_TOKEN` is required iff STT or summary is `External`; Local/Local runs fully offline.
* Config: `appsettings.json` keys `FfmpegPath` (default `C:\Program Files (x86)\ffmpeg\ffmpeg.exe`), `TranscriptionBackend` (`External` default, case-insensitive, unknown → `External`), `TranscriptionModel`, `SummaryBackend` (`External` default, same fallback), `SummaryModel`, `Language` (`ru` default, `auto` omits), nested `GigaAm` (`ModelPath` + transducer file names), nested `GigaChat` (`GgufPath` file path, `ContextSize` 16384, `GpuLayerCount` 0 = CPU). `AppSettingsFile.Load(overridePath?)` resolves override → `AppContext.BaseDirectory` → CWD.
* Console CLI (`Program.Main`): `[folder] [--folder <path>] [--language <code>] [--backend <external|local>] [--gigaam-model <path>] [--summary-backend <external|local>] [--gigachat-model <path>] [--force] [--routerai-check] [--help]`. `--language`/`--backend`/`--gigaam-model`/`--summary-backend`/`--gigachat-model` override settings. `--download-gigaam` downloads the model with confirmation (respects `--gigaam-model`); `--download-gigachat` same for the GGUF (respects `--gigachat-model`, sets `SummaryBackend: Local` only when unconfigured). `--routerai-check` pings `qwen/qwen3.7-flash`; exit codes 0 ok / 1 failure.
* Win shell: drag-and-drop file list + folder pick via `IDialogService`, settings editing via `ISettingsStore`/`SettingsViewModel` persisted to `appsettings.json` (incl. both backend ComboBoxes, language, GigaAM model dir via `ShowOpenFolderDialog`, GigaChat GGUF file via `ShowOpenFileDialog`, model downloads via `DownloadGigaAmModelCommand`/`DownloadGigaChatModelCommand` + `ShowConfirmation`), API key via `IApiKeyProvider`, file logging via `FileLoggerProvider`.

### 2.4 Build / test / format

* **Build:**
  ```bash
  dotnet build AudioParsing.sln --configuration Release
  ```
* **Test:**
  ```bash
  dotnet test AudioParsing.sln --configuration Release --no-build
  ```
* **Format Verification:**
  ```bash
  dotnet format AudioParsing.sln --verify-no-changes
  ```
* **Static Analysis:** `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`, `EnforceCodeStyleInBuild=true` via `Directory.Build.props` + `.editorconfig`. Keep `NoWarn` additions narrowly scoped (see `AudioParsing.Win.csproj`, `AudioParsing.Win.Tests.csproj` precedent).

### 2.5 Licensing

* **Project license:** `MIT` (`LICENSE` at repo root, `PackageLicenseExpression=MIT` in `Directory.Build.props`).
* **Runtime/shipped deps are MIT-only:** `src/AudioParsing.Win` refs `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.Logging` 10.0.0 (plus transitive `Microsoft.Extensions.*` / `System.Diagnostics.EventLog`, all MIT); `Core` and `Console` have zero third-party packages. .NET 10 SDK/runtime is MIT.
* **Test-only deps add Apache-2.0:** `Microsoft.NET.Test.Sdk` / `TestPlatform.*` / `Newtonsoft.Json` (MIT) + `xunit*` 2.9.3 / `xunit.runner.visualstudio` 3.1.0 (`Apache-2.0`, not shipped). `tests/AudioParsing.Win.Tests` pins `Microsoft.Extensions.Logging.Abstractions` 10.0.5 (floor required by LLamaSharp's transitive closure). No GPL/LGPL/AGPL anywhere in the NuGet closure.
* **Local STT runtime dep is Apache-2.0:** `src/AudioParsing.LocalStt` refs `org.k2fsa.sherpa.onnx` 1.13.8 (`Apache-2.0`, shipped only when the Local backend is used); `AudioParsing.Core` stays third-party-free.
* **Local summary runtime deps are MIT:** `src/AudioParsing.LocalSummary` refs `LLamaSharp` + `LLamaSharp.Backend.Cpu` 0.27.0 (both `MIT`, loaded only when `SummaryBackend=Local`); the GigaChat3.1 GGUF from `ai-sage/GigaChat3.1-10B-A1.8B-GGUF` is `license: mit`. Never use third-party `*Uncensored*` GGUF quants (no license tag).
* **External tool caveat:** `ffmpeg` is invoked out-of-process (not linked/bundled), so its GPL/LGPL build license does not constrain this project — but do not bundle `ffmpeg.exe` into an installer/zip without checking that build's license.
* **Rule:** keep new dependencies within permissive licenses (`MIT` / `Apache-2.0` / `BSD`). A copyleft (`GPL/AGPL`) or ambiguous-license package requires explicit human approval and a `LICENSE`/docs update.

---

## 3. Workflow for Coding Agents

When executing a task, agents must follow this sequential lifecycle:

1. **Context & Discovery:**
   * Read the relevant source files and existing test coverage (Core lives in `src/AudioParsing.Core/*.cs`; hosts are thin shells over `AudioPipeline`).
   * Understand the interfaces, domain models, and dependencies involved. Reuse `AudioParsing.Core` directly — do not fork pipeline logic into hosts.
2. **Plan First:**
   * For non-trivial changes, outline the intended modifications step-by-step before executing file writes.
   * For Win work, read `docs/audio-parsing-win.md`; for video/ffmpeg behavior, read `docs/audio-parsing-video.md`.
3. **Execution:**
   * Make modular, precise edits. Preserve backward compatibility via default parameters (console passes `null` progress; Win passes `IProgress<PipelineProgress>`).
   * Maintain consistent formatting, types, and error handling.
4. **Verification:**
   * Run unit/integration tests to verify the fix or feature.
   * Verify that no unused imports, lingering debug logs, or broken dependencies remain.
5. **Documentation & Commit:**
   * Keep git commit messages concise and intent-driven (e.g., `feat(auth): add refresh token handler`).

---

## 4. Coding Standards & Conventions

### General Conventions
* Use explicit, descriptive variable and function names.
* Keep functions small, focused, and single-purpose.
* Handle errors gracefully; never swallow exceptions without logging or rethrowing appropriately. Pipeline convention: per-file `try/catch` → `AudioFileResult(..., Success: false, Error: ex.Message)`;-Host convention: `HttpRequestException`/`InvalidOperationException` → stderr/message box, non-zero exit.
* Avoid magic strings or raw numbers; use constants or configuration entries (see `AudioPipeline.Default*`, `AppSettings.DefaultFfmpegPath`, `RouterAiClient.ApiKeyEnvironmentVariable`).
* File-scoped namespaces, `ImplicitUsings`, `Nullable enable`; XML doc comments on public Core API.

### Comments & Documentation
* Document public interfaces, API signatures, and complex algorithm steps.
* Do not add redundant comments for self-explanatory code (e.g., avoid `// increments count` above `count++`).

---

## 5. Safety & Security Rules

* **Secrets & Credentials:** Never commit API keys, database credentials, or tokens. RouterAI key is read only from `ANTHROPIC_AUTH_TOKEN` env var (`RouterAiClient.FromEnvironment()`); use environment variables (`.env` locally, never committed).
* **Input Validation:** Always sanitize and validate user input at boundaries/endpoints (console validates folder existence + non-empty model strings; `AudioPipeline` ctor throws `ArgumentException` on empty ffmpeg path/models).
* **Dependencies:** Do not add third-party libraries without checking for existing dependencies in the manifest that solve the same problem. Core intentionally has zero external packages; Win uses only `Microsoft.Extensions.*`; only `AudioParsing.LocalStt` carries the native `sherpa-onnx` dep. New packages must stay within permissive licenses (`MIT` / `Apache-2.0` / `BSD`); see §2.5.

---

## 6. Prompting Shortcuts for Human Operators

Operators can use the following shorthand keywords when prompting agents in this repo:

* `@plan`: Instructs the agent to only draft a plan and list affected files without editing.
* `@fix-tests`: Directs the agent to analyze test failures and modify implementation or test assertions to pass.
* `@refactor`: Requests clean-up, readability improvement, or performance optimization without changing behavior.
* `@docs`: Directs the agent to update inline comments, `README.md`, or API specs.
