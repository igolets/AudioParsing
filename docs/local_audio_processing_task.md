# Task Specification: Russian Lecture Processing Pipeline (.NET) — Local/External STT Backends

Status: Revised (adapted to repo reality)
Date: 2026-09-24
Supersedes: previous `docs/lecture_processing_task.md` (net8 / SherpaOnnx-only / Gemini-or-OpenAI / FFMpegCore draft)
Scope: `src/AudioParsing.Core`, `src/AudioParsing.LocalStt` (new), `src/AudioParsing.Console`,
`src/AudioParsing.Win`, `tests/*`, `appsettings.json`, docs.
Related: `docs/audio-parsing-win.md`, `docs/audio-parsing-video.md`, `AGENTS.md`.

---

## 1. Objective

Give the existing AudioParsing application a **selectable speech-to-text backend**, while keeping
everything else that already works unchanged:

- **Transcription backend (new, selectable):**
  - `External` (default) — RouterAI Whisper (`POST /audio/transcriptions`), unchanged.
  - `Local` (opt-in) — **GigaAM-v3** ONNX via **sherpa-onnx**, running on-device.
- **Summarization (unchanged):** RouterAI **luna** (`openai/gpt-6-luna`). The summarizer is **not**
  switched to Gemini / OpenAI-direct / DeepSeek.
- **API key (unchanged):** read only from the `ANTHROPIC_AUTH_TOKEN` environment variable.
- **Output (unchanged):** a sibling `.md` file with YAML frontmatter,
  `## Краткое содержание` and `## Полный транскрипт`, produced by `MarkdownDocument.Build`.
- **Backward compatibility:** with no config change the application behaves exactly as today
  (`External` backend).

The backend is selectable in `appsettings.json` (shared by Console and Win) and in the Win
**Settings** dialog.

---

## 2. Gap Analysis — original spec vs. current repo

| # | Original `lecture_processing_task.md` assumption | Current repo reality (verified) | Adaptation in this spec |
|---|--------------------------------------------------|----------------------------------|--------------------------|
| 1 | Target `.NET 8.0` or higher | `net10.0` set globally in `Directory.Build.props`; Win overrides to `net10.0-windows` | Target `net10.0`; no framework change. Do not pin net8. |
| 2 | Local STT mandatory: `SherpaOnnx` + GigaAM-v3 only | No local STT at all; transcription is RouterAI Whisper over HTTP multipart (`RouterAiClient.TranscribeAsync`) | Make STT a **selectable backend**; keep `External` as default (Do No Harm); add `Local` as opt-in. |
| 3 | `FFMpegCore` NuGet for preprocessing/VAD chunking | ffmpeg is invoked **out-of-process** via `FfmpegCompressor` (`System.Diagnostics.Process`), args `-y -i "<in>" -vn -ac 1 -ar 16000 -b:a 32k "<out>"`; no `FFMpegCore`; no VAD/chunking | Reuse `FfmpegCompressor`. Add a **16 kHz mono 16-bit PCM WAV** mode for the local backend. No `FFMpegCore`. No chunking (out of scope; see §11). |
| 4 | LLM configured via `Llm.BaseUrl`/`Llm.ApiKey`/`Llm.Model` in `appsettings.json`; provider = Gemini Flash-Lite or OpenAI | `RouterAiClient` with hard-coded base `https://routerai.ru/api/v1`; key from `ANTHROPIC_AUTH_TOKEN` env var (`RouterAiClient.FromEnvironment`); model `openai/gpt-6-luna` | Keep RouterAI + luna. **Do NOT** put an API key in settings (secret policy, `AGENTS.md` §5). Optional future: a `RouterAiBaseUrl` key (client already accepts `baseUri`). |
| 5 | Summarizer = Gemini Flash-Lite / GPT-4o-mini / DeepSeek | luna `openai/gpt-6-luna` via `GenerateSummaryAsync` with `RouterAiClient.SummarySystemPrompt` | Keep luna and the existing summarization call. |
| 6 | Single LLM system prompt generates the **whole** Markdown (frontmatter + strict headings + transcript cleanup) | Structure is **deterministic**: `MarkdownDocument.Build` emits frontmatter + `## Краткое содержание` + `## Полный транскрипт`; the LLM only returns summary text + a trailing `Keywords: ...` line; transcript is stored **verbatim** (test asserts "verbatim transcript") | Keep `MarkdownDocument.Build` as the single source of structure. The two required headings **already match** the original spec. Do **not** move heading/frontmatter generation into the prompt. Do **not** add transcript cleanup/hesitation removal (behavior change; see §11). |
| 7 | Config keys `GigaAM.ModelPath/*FileName`, `Llm.*` | Flat schema parsed by `AppSettingsFile.Load` → `AppSettingsModel { FfmpegPath, TranscriptionModel, SummaryModel }`; resolution override → `AppContext.BaseDirectory` → CWD | Extend the schema additively: add `TranscriptionBackend`, `Language`, and a nested `GigaAm` object. Missing keys keep current defaults. |
| 8 | Console/library app only | Three hosts (Console, Win WPF) + two test projects over one `AudioParsing.Core` pipeline | Add one small `AudioParsing.LocalStt` project; integrate it into both hosts. |
| 9 | No build-discipline constraints stated | `TreatWarningsAsErrors`, `AnalysisLevel=latest-all`, `EnforceCodeStyleInBuild`; xUnit 2.9.3; `dotnet format --verify-no-changes` gate | New code must satisfy analyzers; add tests; scope `NoWarn` narrowly if required. |
| 10 | Language unspecified; prompt is plain-English/Russian | Language is CLI-only (`--language`, default `ru`); Win hard-codes `AudioPipeline.DefaultLanguage` | Add a persistent `Language` setting (default `ru`); CLI `--language` still overrides. `auto` → omit the parameter. |
| 11 | Keywords inline `keywords: [тег1, тег2]` | `MarkdownDocument.Build` writes a YAML block list (`keywords:\n  - тег`) | Keep the block list (valid YAML). No change. |
| 12 | "Handles UTF-8 without missing Cyrillic" | Already satisfied (`File.WriteAllTextAsync` default UTF-8; verified sample `testdata/Новая запись 132.md`) | Keep; add a regression assertion. |

---

## 3. Revised Architecture

```
Input: .mp3 / .m4a / .wav / .mp4 / .mkv / ... (existing AudioFileFinder discovery)
        │
        ▼
[ AudioPipeline ]  (unchanged stages: skip → preprocess → transcribe → summarize → write
        │                                             per-file result, batch never aborts)
        │  preprocessing gate:
        │    External backend → video OR size > 25 MiB  → ffmpeg → mono mp3 (unchanged)
        │    Local backend    → ALWAYS                   → ffmpeg → 16 kHz mono PCM WAV (new)
        ▼
[ IAudioTranscriber ]  ← chosen by TranscriptionBackend
        ├─ External: RouterAiTranscriber  → RouterAiClient.TranscribeAsync   (Whisper)
        └─ Local:    GigaAmTranscriber    → sherpa-onnx OfflineRecognizer    (GigaAM-v3 ONNX)
        │  raw transcript
        ▼
[ RouterAiClient.GenerateSummaryAsync ]  (luna, UNCHANGED)
        │  summary + trailing "Keywords: ..." line
        ▼
[ MarkdownDocument.Build ]  → sibling .md (frontmatter + ## Краткое содержание + ## Полный транскрипт)
```

Design principle: **the pipeline keeps owning stage ordering, error mapping and results**; the backend
only swaps *how the transcript is produced*. The Win shell and Console stay thin hosts.

### 3.1 Core abstraction (new, dependency-free)

```csharp
namespace AudioParsing;

/// <summary>Selected speech-to-text backend. Persisted as a string in appsettings.json.</summary>
public enum TranscriptionBackend
{
    /// <summary>RouterAI Whisper over HTTP (default, current behaviour).</summary>
    External,
    /// <summary>On-device GigaAM-v3 via sherpa-onnx.</summary>
    Local,
}

/// <summary>Produces raw transcript text from a prepared audio input.</summary>
public interface IAudioTranscriber
{
    /// <summary>Short label used in progress messages (e.g. model name or "GigaAM-v3 (local)").</summary>
    string Name { get; }

    /// <summary>
    /// Transcribes <paramref name="audioFilePath"/> (already preprocessed for this backend).
    /// <paramref name="language"/> may be null ("auto"); the local backend ignores it.
    /// </summary>
    Task<string> TranscribeAsync(
        string audioFilePath,
        string? language,
        CancellationToken cancellationToken = default);
}
```

### 3.2 External adapter (Core, no new dependency)

```csharp
namespace AudioParsing;

/// <summary>Wraps the existing RouterAI Whisper transcription behind IAudioTranscriber.</summary>
public sealed class RouterAiTranscriber : IAudioTranscriber
{
    private readonly RouterAiClient _client;
    private readonly string _model;

    public RouterAiTranscriber(RouterAiClient client, string model) { /* null/empty guards */ }

    public string Name => _model;

    public Task<string> TranscribeAsync(string audioFilePath, string? language, CancellationToken ct) =>
        _client.TranscribeAsync(audioFilePath, _model, language, ct);
}
```

`RouterAiClient.TranscribeAsync` is **kept as-is** (still used by this adapter and its existing tests).

### 3.3 Local adapter (new project `AudioParsing.LocalStt`, Apache-2.0 dependency)

```csharp
namespace AudioParsing.LocalStt;

/// <summary>On-device GigaAM-v3 transcription via sherpa-onnx (expects 16 kHz mono PCM WAV).</summary>
public sealed class GigaAmTranscriber : IAudioTranscriber
{
    public GigaAmTranscriber(GigaAmSettings settings); // validates files exist; loads OfflineRecognizer

    public string Name => "GigaAM-v3 (local)";

    public Task<string> TranscribeAsync(string audioFilePath, string? language, CancellationToken ct);
}
```

- Built on `org.k2fsa.sherpa.onnx` (NuGet, **Apache-2.0**, latest `1.13.8` at time of writing) with the
  GigaAM-v3 ONNX model files from `GigaAmSettings`.
- Because it is a native-backed dependency it lives in its **own project**, so `AudioParsing.Core`
  stays third-party-free (preserves the current architecture invariant and keeps the external path
  testable without native libs).
- Implementation step must confirm the exact sherpa-onnx recognizer configuration for the shipped
  GigaAM-v3 file set (transducer `encoder/decoder/joiner/tokens` vs. CTC `model/tokens`). File names
  are configurable for that reason; see §4.
- Validation on construction: model directory + configured file names must exist, else
  `InvalidOperationException` (surfaced per-file as `AudioFileResult.Success=false`, never aborting the batch).

### 3.4 `AudioPipeline` integration (additive, backward compatible)

```csharp
// Existing ctor — SIGNATURE UNCHANGED (External behaviour identical).
public AudioPipeline(
    RouterAiClient client,
    string ffmpegPath,
    string model = DefaultModel,
    string? language = DefaultLanguage,
    string summaryModel = DefaultSummaryModel)
    : this(client, new RouterAiTranscriber(client, model),
           TranscriptionBackend.External, ffmpegPath, language, summaryModel) { }

// New ctor — backend-agnostic.
public AudioPipeline(
    RouterAiClient client,
    IAudioTranscriber transcriber,
    TranscriptionBackend backend,
    string ffmpegPath,
    string? language = DefaultLanguage,
    string summaryModel = DefaultSummaryModel) { /* assigns fields */ }
```

`ProcessFileAsync` change (only the preprocessing gate + the transcription call):

```csharp
bool pcmWav = _backend == TranscriptionBackend.Local;                     // local always normalizes
bool preprocess = pcmWav
    || FfmpegCompressor.RequiresAudioExtraction(audioPath, info.Length, MaxUploadBytes);

if (preprocess)
{
    string ext = pcmWav ? ".wav" : ".mp3";
    extractedPath = Path.Combine(Path.GetTempPath(), $"audioparsing-{Guid.NewGuid():N}{ext}");
    progress?.Report(new PipelineProgress(audioPath, PipelineStage.Compressing,
        pcmWav ? "Preparing 16 kHz WAV for GigaAM." : "Compressing with ffmpeg."));
    if (pcmWav)
        await FfmpegCompressor.ExtractPcmWavAsync(_ffmpegPath, audioPath, extractedPath, ct);
    else
        await FfmpegCompressor.CompressAsync(_ffmpegPath, audioPath, extractedPath, ct);
    transcriptionInput = extractedPath;
}

progress?.Report(new PipelineProgress(audioPath, PipelineStage.Transcribing, $"Transcribing with {_transcriber.Name}."));
string transcript = await _transcriber.TranscribeAsync(transcriptionInput, _language, ct);
// summary / MarkdownDocument.Build / write / finally-delete: UNCHANGED.
```

- **No new `PipelineStage`** (reuse `Compressing`); `AudioFileResult` and error mapping are unchanged.
- Both the local and external summarization still require `RouterAiClient` + the env var.

### 3.5 `FfmpegCompressor` additions (Core)

```csharp
/// <summary>Builds ffmpeg args that convert/extract to 16 kHz mono 16-bit PCM WAV (for GigaAM).</summary>
public static string BuildPcmWavArguments(string inputPath, string outputPath)
    => $"-y -i \"{inputPath}\" -vn -ac 1 -ar 16000 -c:a pcm_s16le \"{outputPath}\"";

/// <summary>Runs ffmpeg with <see cref="BuildPcmWavArguments"/>.</summary>
public static Task ExtractPcmWavAsync(string ffmpegPath, string inputPath, string outputPath,
    CancellationToken cancellationToken = default);
```

Refactor the process-run logic in `CompressAsync` into one private `RunAsync(ffmpegPath, arguments,
outputPath, ct)` shared by `CompressAsync` (arguments = `BuildArguments`) and `ExtractPcmWavAsync`.
`BuildArguments` and `CompressAsync` keep their exact current signatures and output.

---

## 4. Configuration Schema

Both `src/AudioParsing.Console/appsettings.json` and `src/AudioParsing.Win/appsettings.json` use the
same schema. Resolution stays override → `AppContext.BaseDirectory` → CWD (`AppSettingsFile.ResolvePath`).

```json
{
  "FfmpegPath": "C:\\Program Files (x86)\\ffmpeg\\ffmpeg.exe",
  "TranscriptionBackend": "External",
  "TranscriptionModel": "openai/whisper-large-v3-turbo",
  "SummaryModel": "openai/gpt-6-luna",
  "Language": "ru",
  "GigaAm": {
    "ModelPath": "models/gigaam-v3",
    "EncoderFileName": "encoder.onnx",
    "DecoderFileName": "decoder.onnx",
    "JoinerFileName": "joiner.onnx",
    "TokensFileName": "tokens.txt"
  }
}
```

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `FfmpegPath` | string | `AppSettings.DefaultFfmpegPath` | ffmpeg executable (existing). |
| `TranscriptionBackend` | string | `"External"` | `"External"` = RouterAI Whisper; `"Local"` = GigaAM-v3. Case-insensitive; unknown → `External`. |
| `TranscriptionModel` | string | `AudioPipeline.DefaultModel` (`openai/whisper-large-v3-turbo`) | Whisper model used **only** by the External backend. |
| `SummaryModel` | string | `AudioPipeline.DefaultSummaryModel` (`openai/gpt-6-luna`) | RouterAI luna model (unchanged). |
| `Language` | string | `"ru"` | Spoken language; `"auto"` → parameter omitted. CLI `--language` overrides. |
| `GigaAm.ModelPath` | string | `"models/gigaam-v3"` | Directory with GigaAM-v3 ONNX files (Local backend only). |
| `GigaAm.EncoderFileName` | string | `"encoder.onnx"` | GigaAM encoder. |
| `GigaAm.DecoderFileName` | string | `"decoder.onnx"` | GigaAM decoder. |
| `GigaAm.JoinerFileName` | string | `"joiner.onnx"` | GigaAM joiner. |
| `GigaAm.TokensFileName` | string | `"tokens.txt"` | GigaAM tokens. |

**Model shape (`AppSettingsModel`):**

```csharp
public sealed class GigaAmSettings
{
    public string ModelPath { get; set; } = "models/gigaam-v3";
    public string EncoderFileName { get; set; } = "encoder.onnx";
    public string DecoderFileName { get; set; } = "decoder.onnx";
    public string JoinerFileName { get; set; } = "joiner.onnx";
    public string TokensFileName { get; set; } = "tokens.txt";
}

public sealed class AppSettingsModel
{
    public string FfmpegPath { get; set; } = AppSettings.DefaultFfmpegPath;
    public string TranscriptionModel { get; set; } = AudioPipeline.DefaultModel;
    public string SummaryModel { get; set; } = AudioPipeline.DefaultSummaryModel;
    public string TranscriptionBackend { get; set; } = "External"; // NEW
    public string Language { get; set; } = "ru";                    // NEW
    public GigaAmSettings GigaAm { get; set; } = new();             // NEW
}
```

**Read-only facade (`AppSettings`):** add
`GetTranscriptionBackend(string? path = null) : TranscriptionBackend` (parses case-insensitively,
falls back to `External`), `GetLanguage(string? path = null) : string?` (`"auto"` → `null`),
`GetGigaAmSettings(string? path = null) : GigaAmSettings`. Existing getters unchanged.

**`AppSettingsFile.Load`:** parse `TranscriptionBackend`, `Language` (via existing `GetString`
helper) and a nested `GigaAm` object (`root.TryGetProperty("GigaAm", ...)` → per-field strings with
defaults). `Save` needs no code change (serializer emits the nested object). Missing/malformed →
defaults, exactly as today.

---

## 5. CLI Changes (`src/AudioParsing.Console`)

| Flag | Status | Behaviour |
|------|--------|-----------|
| `[folder]` / `--folder <path>` | unchanged | as today |
| `--language <code>` | unchanged | overrides `Language`; `auto` → omit (as today) |
| `--force` | unchanged | re-process |
| `--routerai-check` | unchanged | ping `qwen/qwen3.7-flash` |
| `--help` | MOD | document the new flags |
| `--backend <external\|local>` | **NEW** | overrides `TranscriptionBackend` |
| `--gigaam-model <path>` | **NEW** | overrides `GigaAm.ModelPath` (implies nothing about backend; only used when backend = Local) |

Wiring: after loading settings, resolve the effective backend and build the pipeline:

```csharp
TranscriptionBackend backend = ResolveBackend(args) ?? AppSettings.GetTranscriptionBackend();
string ffmpegPath = AppSettings.GetFfmpegPath();
string summaryModel = AppSettings.GetSummaryModel();
GigaAmSettings gigaAm = AppSettings.GetGigaAmSettings();
string? language = ResolveLanguage(args) ?? AppSettings.GetLanguage();

using RouterAiClient client = RouterAiClient.FromEnvironment(); // needed for summarization in BOTH backends
AudioPipeline pipeline = backend == TranscriptionBackend.Local
    ? new AudioPipeline(client, new GigaAmTranscriber(OverrideModelPath(gigaAm, args)),
                        TranscriptionBackend.Local, ffmpegPath, language, summaryModel)
    : new AudioPipeline(client, ffmpegPath, AppSettings.GetTranscriptionModel(), language, summaryModel);
```

`--backend local` still requires `ANTHROPIC_AUTH_TOKEN` (luna summarization). Document this. (Future
"local-only summarization" is out of scope — see §11.)

---

## 6. Win Settings Changes (`AudioParsing.Win`)

**`SettingsViewModel` — new bindable properties + validation:**

| Property | UI | Notes |
|----------|----|-------|
| `TranscriptionBackend` (string `"External"`/`"Local"`) | ComboBox «Движок распознавания»: `Внешний (RouterAI Whisper)`, `Локальный (GigaAM-v3)` | drives which fields matter |
| `GigaAmModelPath` | TextBox + «Обзор…» (`BrowseGigaAmModelCommand`) | folder picker |
| `GigaAmEncoderFileName` / `DecoderFileName` / `JoinerFileName` / `TokensFileName` | TextBoxes | advanced; prefilled from settings |
| `Language` | TextBox | default `ru`; `auto` allowed |
| `FfmpegPath`, `TranscriptionModel`, `SummaryModel` | unchanged | |

**Validation (`TrySave`)** — additive rules:
- If `TranscriptionBackend == "Local"`: `GigaAmModelPath` must be a non-empty existing directory and
  the four configured files must exist → else Russian `ValidationMessage` (e.g. «Укажите каталог с
  ONNX-моделью GigaAM-v3.» / «Не найдены файлы модели GigaAM-v3.»).
- If `External`: `TranscriptionModel` must be non-empty (existing rule).
- `FfmpegPath` must exist (existing rule, required by both backends).
- Persist all new fields into `AppSettingsModel` via `ISettingsStore.Save`.

**`IDialogService` / `DialogService`** — add `string? ShowOpenFolderDialog(string? initialPath)`
(folder picker) for the GigaAM model directory; keep the existing file picker.

**`SettingsWindow.xaml`** — add rows for backend ComboBox, GigaAM path (+Обзор), four file-name
fields, and language; increase window height (e.g. `Height="560"`) and keep `ResizeMode="NoResize"`.

**`PipelineRunner`** — backend-aware, re-reading settings on every run (unchanged pattern):

```csharp
AppSettingsModel s = _settings.Load();
TranscriptionBackend backend = s.TranscriptionBackend.Equals("Local", OrdinalIgnoreCase)
    ? TranscriptionBackend.Local : TranscriptionBackend.External;
string? language = string.IsNullOrWhiteSpace(s.Language) || s.Language == "auto" ? null : s.Language;

using RouterAiClient client = _clientFactory(key);            // key needed for summary (both backends)
AudioPipeline pipeline = backend == Local
    ? new AudioPipeline(client, new GigaAmTranscriber(s.GigaAm), Local, s.FfmpegPath, language, s.SummaryModel)
    : new AudioPipeline(client, s.FfmpegPath, s.TranscriptionModel, language, s.SummaryModel);
return await pipeline.ProcessFilesAsync(files, force: false, progress, ct);
```

`App.xaml.cs` DI registrations need no change (same interfaces; `PipelineRunner` constructs the
transcriber internally). Existing MainWindow log/state machine is unchanged.

---

## 7. Affected Files (per project)

### `src/AudioParsing.Core` (no new packages)
| File | Change |
|------|--------|
| `TranscriptionBackend.cs` | **NEW** — enum `External`/`Local`. |
| `IAudioTranscriber.cs` | **NEW** — `Name` + `TranscribeAsync`. |
| `RouterAiTranscriber.cs` | **NEW** — adapter over `RouterAiClient` + model. |
| `AudioPipeline.cs` | **MOD** — new backend-aware ctor; keep existing ctor; backend-aware preprocessing + `_transcriber` call. |
| `FfmpegCompressor.cs` | **MOD** — add `BuildPcmWavArguments` + `ExtractPcmWavAsync`; extract shared `RunAsync` (signatures of `BuildArguments`/`CompressAsync` unchanged). |
| `AppSettingsFile.cs` | **MOD** — `GigaAmSettings`, new `AppSettingsModel` fields, nested `GigaAm` parsing. |
| `AppSettings.cs` | **MOD** — `GetTranscriptionBackend`, `GetLanguage`, `GetGigaAmSettings`. |
| `RouterAiClient.cs` | **NO CHANGE** — still used for Whisper (External) + luna summary. |

### `src/AudioParsing.LocalStt` (NEW project; Apache-2.0 runtime dep)
| File | Change |
|------|--------|
| `AudioParsing.LocalStt.csproj` | **NEW** — `net10.0`, refs `AudioParsing.Core` + `PackageReference org.k2fsa.sherpa.onnx`. |
| `GigaAmTranscriber.cs` | **NEW** — sherpa-onnx `OfflineRecognizer` over 16 kHz mono WAV; ctor validates model files. |
| `GigaAmRecognizerFactory.cs` (optional) | **NEW** — isolates native recognizer construction for testability. |

### `src/AudioParsing.Console`
| File | Change |
|------|--------|
| `Program.cs` | **MOD** — `--backend`, `--gigaam-model`; branch to build the transcriber; usage text. |
| `appsettings.json` | **MOD** — new schema (§4). |
| `AudioParsing.Console.csproj` | **MOD** — ref `AudioParsing.LocalStt`. |

### `src/AudioParsing.Win`
| File | Change |
|------|--------|
| `Services/PipelineRunner.cs` | **MOD** — backend-aware construction. |
| `Services/IDialogService.cs` + `DialogService.cs` | **MOD** — `ShowOpenFolderDialog`. |
| `ViewModels/SettingsViewModel.cs` | **MOD** — new props, browse command, validation, save. |
| `Views/SettingsWindow.xaml` | **MOD** — new rows + height. |
| `appsettings.json` | **MOD** — new schema (§4). |
| `AudioParsing.Win.csproj` | **MOD** — ref `AudioParsing.LocalStt`. |
| `App.xaml.cs` | **NO CHANGE** (DI registrations unchanged). |

### `tests`
| File | Change |
|------|--------|
| `tests/AudioParsing.Tests/AppSettingsTests.cs` | **MOD** — backend/language/GigaAm parsing + defaults. |
| `tests/AudioParsing.Tests/AudioPipelineTests.cs` | **MOD** — fake `IAudioTranscriber`; Local backend preprocessing + transcriber invocation; existing cases unchanged. |
| `tests/AudioParsing.Tests/FfmpegCompressorTests.cs` | **MOD** — `BuildPcmWavArguments` theory (contains `-ar 16000`, `-ac 1`, `pcm_s16le`, `-vn`). |
| `tests/AudioParsing.Tests/RouterAiTranscriberTests.cs` | **NEW** — delegates to `RouterAiClient.TranscribeAsync`. |
| `tests/AudioParsing.Tests/AudioParsing.Tests.csproj` | **MOD** — ref `AudioParsing.LocalStt` (for arg/validation unit tests). |
| `tests/AudioParsing.Tests/GigaAmTranscriberTests.cs` | **NEW** — ctor validation + arg construction (no model/native needed). |
| `tests/AudioParsing.Win.Tests/TestDoubles.cs` | **MOD** — `FakeAudioTranscriber`, extend `FakeSettingsStore`/`FakeDialogService`. |
| `tests/AudioParsing.Win.Tests/SettingsViewModelTests.cs` | **MOD** — backend toggle + GigaAM validation. |
| `tests/AudioParsing.Win.Tests/WinServiceTests.cs` | **MOD** — `PipelineRunner` picks transcriber per backend. |

### Build / docs
| File | Change |
|------|--------|
| `AudioParsing.sln` | **MOD** — add `AudioParsing.LocalStt`. |
| `AGENTS.md` | **MOD** — §2.1/§2.2/§2.5: new project, Apache-2.0 runtime dep note. |
| `README.md` | **MOD** — document backend selection, GigaAM model setup, CLI flags. |
| `docs/lecture_processing_task.md` | **MOD** — this revised spec. |
| `Directory.Build.props` | **NO CHANGE** (`net10.0` global stays). |

---

## 8. Implementation Plan (ordered; backward compatible)

1. **Core abstraction (no behaviour change).** Add `TranscriptionBackend`, `IAudioTranscriber`,
   `RouterAiTranscriber`. Add the new `AudioPipeline` ctor; re-express the old ctor as a delegating
   call. Replace the direct `_client.TranscribeAsync(...)` call with `_transcriber.TranscribeAsync(...)`.
   Run `AudioParsing.Tests` — the existing suite (constructed via the old ctor, External) must stay green.
2. **Preprocessing for the local backend.** Add `FfmpegCompressor.BuildPcmWavArguments` +
   `ExtractPcmWavAsync` (refactor shared runner). Add the backend-aware gate in `AudioPipeline`.
   Unit-test arg building; leave the external path output byte-identical.
3. **Config schema.** Extend `AppSettingsModel`/`AppSettingsFile.Load`/`AppSettings` with
   `TranscriptionBackend`, `Language`, `GigaAm`. Update both `appsettings.json` files. Add tests.
4. **Local backend project.** Create `src/AudioParsing.LocalStt` (net10.0) with
   `org.k2fsa.sherpa.onnx`; implement `GigaAmTranscriber` (validate model files, build recognizer,
   transcribe WAV). Confirm the GigaAM-v3 file set mapping with the package version in use. Add to
   `AudioParsing.sln`; add project refs in Console + Win (+ tests).
5. **Console integration.** `--backend`, `--gigaam-model`, backend-aware pipeline construction,
   usage/help text; `--routerai-check` unchanged.
6. **Win integration.** `IDialogService.ShowOpenFolderDialog`; extend `SettingsViewModel`
   (props/validation/save) and `SettingsWindow.xaml`; make `PipelineRunner` backend-aware.
7. **Tests** — Core (settings/pipeline/transcriber/ffmpeg), LocalStt (validation/args, model-gated),
   Win (settings VM, runner). Add `FakeAudioTranscriber`.
8. **Docs** — README, AGENTS.md, this file.
9. **Verify** (§9). Iterate until build/test/format are green.

Backward-compatibility guarantees after all steps:
- No `TranscriptionBackend`/`Language`/`GigaAm` keys → `External` + `ru`, identical output to today.
- Existing 5-arg `AudioPipeline` ctor and `RouterAiClient.TranscribeAsync` remain.
- `AudioFileResult`, `PipelineStage`, error mapping and `MarkdownDocument.Build` unchanged.
- Summarizer remains RouterAI luna; API key remains env-var only.

---

## 9. Verification Plan

```bash
# Build (Release)
dotnet build AudioParsing.sln --configuration Release

# Tests (no rebuild)
dotnet test AudioParsing.sln --configuration Release --no-build

# Formatting gate (must be clean)
dotnet format AudioParsing.sln --verify-no-changes
```

Automated coverage:
- `AppSettingsTests`: backend parse (`"local"`/`"LOCAL"`/unknown→External), `Language` default +
  `auto`→null, nested `GigaAm` defaults and overrides, missing-file → defaults.
- `AudioPipelineTests`: External via old ctor unchanged; new ctor with a `FakeAudioTranscriber`
  (Local) calls the transcriber once and reports the expected stages; `.md` still written.
- `FfmpegCompressorTests`: `BuildPcmWavArguments` contains `-vn`, `-ac 1`, `-ar 16000`, `pcm_s16le`;
  existing mp3 args assertion unchanged.
- `GigaAmTranscriberTests`: ctor throws when model files are absent; no native/model needed.
- `Win.Tests`: `PipelineRunner` selects `GigaAmTranscriber` for `Local` and `RouterAiTranscriber`
  semantics for `External` (via injected fake); `SettingsViewModel` rejects `Local` with missing model
  files and persists all fields.

Manual smoke:
1. Default config (no new keys) → process a `.mp3` and a `.mp4`; output identical to today.
2. `--backend local` (or Win backend = Local) with a valid GigaAM-v3 model dir → Russian `.wav`
   transcribes; sibling `.md` has both strict headings and UTF-8 Cyrillic.
3. `--backend local` with a missing model dir → per-file failure logged, batch continues, no `.md`.
4. `--help` lists `--backend`/`--gigaam-model`.

---

## 10. Definition of Done (updated)

1. `TranscriptionBackend` is configurable in both `appsettings.json` files and in the Win Settings
   dialog (`External` default; `Local` opt-in); CLI exposes `--backend` / `--gigaam-model`.
2. With no new keys, behaviour is byte-for-byte identical to the current pipeline (`External`, luna).
3. `Local` transcribes Russian audio on-device via GigaAM-v3 + sherpa-onnx (16 kHz mono PCM WAV input).
4. Summarization remains RouterAI **luna** (`openai/gpt-6-luna`); API key remains
   `ANTHROPIC_AUTH_TOKEN` only (never written to `appsettings.json`).
5. Output retains YAML frontmatter and the exact headings `## Краткое содержание` and
   `## Полный транскрипт` (via `MarkdownDocument.Build`); UTF-8 Cyrillic preserved.
6. Per-file failures (missing model, missing ffmpeg, bad API response) never abort the batch and
   surface as `AudioFileResult.Success=false` with an error message.
7. `sherpa-onnx` is Apache-2.0; `AudioParsing.Core` stays third-party-free; `AGENTS.md` §2.5 updated.
8. `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` are green.
9. `README.md`, `AGENTS.md` and this spec reflect the final schema and flags.

---

## 11. Trade-offs, ADRs, Open Questions

**ADR-001 — Selectable backend with `External` default (Accepted).**
*Decision:* introduce `TranscriptionBackend`; default `External`. *Positive:* zero behaviour change by
default; local STT is opt-in. *Negative:* a settings surface and two code paths to maintain.
*Alternatives:* make Local the default (rejected — breaks existing installs and requires a model);
keep External only (rejected — user requirement).

**ADR-002 — Local STT isolated in `AudioParsing.LocalStt` (Accepted).**
*Decision:* `GigaAmTranscriber` lives in a new project carrying the Apache-2.0 `sherpa-onnx` package.
*Positive:* `AudioParsing.Core` stays dependency-free; native lib not loaded for the external path.
*Negative:* one more project + solution entry. *Alternatives:* put sherpa-onnx in Core (rejected —
violates the zero-dependency invariant and forces native runtime on all consumers/CI).

**ADR-003 — Reuse out-of-process ffmpeg, add a PCM-WAV mode (Accepted).**
*Decision:* do **not** adopt `FFMpegCore`; extend `FfmpegCompressor` with a 16 kHz mono PCM-WAV mode
for the local backend. *Positive:* no new dependency, one process wrapper, existing tests intact.
*Negative:* manual `Process` handling (already present). *Alternatives:* `FFMpegCore` (rejected —
new dependency, redundant with existing wrapper); sherpa-onnx internal resampling (rejected — WAV
normalization is already needed for video extraction).

**ADR-004 — Keep `MarkdownDocument.Build` as the structural authority (Accepted).**
*Decision:* the LLM returns only summary text + a `Keywords:` line; frontmatter and the two strict
headings remain code-generated. *Positive:* deterministic structure, immune to prompt drift; the
required headings already match the original spec. *Negative:* transcript stays verbatim (no LLM
hesitation cleanup/subheadings as the original prompt wanted). *Alternatives:* LLM-generated whole
document (rejected — non-deterministic headings; would break existing tests/consumers).

**ADR-005 — Keep API key in the environment; keep RouterAI for summarization (Accepted).**
*Decision:* no `ApiKey` in settings; luna stays the summarizer. *Positive:* secret hygiene; honors
requirement 1. *Negative:* `--backend local` still needs the env var for summary (documented).

Open questions (out of scope for this change):
1. Chunking/splitting for media whose normalized audio still exceeds 25 MiB (>~1.7 h) — shared by both backends.
2. Optional `RouterAiBaseUrl` setting (client already supports a `baseUri` override).
3. Local (offline) summarization to make the Local backend fully independent of RouterAI.
4. Optional LLM transcript cleanup/subheadings (changes verbatim-output contract).
5. Expose an `--audio-track <n>` selector for multi-track video (tracked in the video spec).
