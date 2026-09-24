# Task Specification: Selectable Local/External Summarization Backend (.NET 10) — on-device GigaChat GGUF

Status: Revised (adapted to repo reality)
Date: 2026-09-24
Supersedes: previous `docs/local_summary_processing_task.md` (net8 / FFMpegCore / custom `<|system|>` whole-document prompt / new offline app draft)
Scope: `src/AudioParsing.Core`, `src/AudioParsing.LocalSummary` (new), `src/AudioParsing.Console`, `src/AudioParsing.Win`, `tests/*`, both `appsettings.json`, docs.
Related: `docs/local_audio_processing_task.md`, `docs/audio-parsing-win.md`, `docs/audio-parsing-video.md`, `AGENTS.md`.

---

## 0. Review Findings (severity-ranked)

The original spec is well-intentioned ("run the summarizer on-device") but was written as if the project were a green-field single-purpose console app. It collides with four load-bearing invariants of this repository. Findings are ordered by impact; each blocker is resolved by the revised design in sections 3-11.

### BLOCKER-1 — Custom `<|system|>` prompt re-invents the output format
The spec's prompt (lines 60-75) asks the model to emit YAML frontmatter **and** the `## Краткое содержание` / `## Полный транскрипт` headings, and to rewrite the transcript ("без слов-паразитов", `###` subheadings). In this repo the structure is **code-generated**: `MarkdownDocument.Build` owns frontmatter + both headings, the LLM returns only *summary text + a trailing `Keywords:` line*, and the transcript is stored **verbatim** (`docs/local_audio_processing_task.md` section 2 row 6, ADR-004; asserted in `AudioPipelineTests`). Adopting the spec prompt would (a) double-emit frontmatter/headings, (b) break the verbatim contract, and (c) change `RouterAiClient.SummarySystemPrompt`, which is an explicit invariant. **Rejected** — see section 3.4.

### BLOCKER-2 — `.NET 8` + `FFMpegCore` contradict the build and toolchain
Target framework is pinned to `net10.0` globally (`Directory.Build.props`) and ffmpeg is already invoked **out-of-process** by `FfmpegCompressor` (no `FFMpegCore`). Adding `FFMpegCore` is redundant with the existing wrapper, and downgrading/pinning `net8.0` breaks `TreatWarningsAsErrors` + `AnalysisLevel=latest-all`. **Resolution:** target `net10.0`; reuse `FfmpegCompressor` (already has `ExtractPcmWavAsync`).

### BLOCKER-3 — The model artifact named in the spec does not exist under a permissive license
`GigaChat-3.1-Lightning-Q4_K_M.gguf` is not an official `ai-sage` artifact. The HF repos literally named `*GigaChat3.1-Lightning-Uncensored*` are **third-party "Uncensored" derivatives with no license tag**, which violates the permissive-only rule (`AGENTS.md` section 2.5). The official, **MIT-licensed** artifact is `ai-sage/GigaChat3.1-10B-A1.8B-GGUF` -> `GigaChat3.1-10B-A1.8B-q4_K_M.gguf` (10B total / 1.8B active MoE, ctx 262144, `license: mit`). **Resolution:** pin the official repo/file — see section 5.

### BLOCKER-4 — Makes summarization offline by ignoring the shared pipeline, hosts and the client dependency
The spec implies a new standalone "single exe". This repo has **one** `AudioParsing.Core` pipeline consumed by **two** hosts (Console + Win WPF); `AudioPipeline` currently takes a non-null `RouterAiClient` and always calls `GenerateSummaryAsync`. A purely-local summary requires a new abstraction + a client-optional path, otherwise the pipeline must be forked into the new app (violates "one Core pipeline, thin hosts"). **Resolution:** `SummaryBackend` + `ISummaryGenerator` + a client-optional ctor — see sections 3.1-3.5.

### MAJOR-5 — No download UX parity
A ~6 GB single GGUF needs the same streaming/skip-existing/tmp+move/0.0-1.0-progress/yN-confirm pattern as `GigaAmDownloader`, surfaced identically in Console (`--download-gigachat`) and Win (button + status). The spec has no downloader at all. **Resolution:** `GigaChatDownloader` mirroring `GigaAmDownloader` — see section 5.

### MAJOR-6 — API-key coupling not addressed
`PipelineRunner` and `Program` hard-require `ANTHROPIC_AUTH_TOKEN`. A fully-local run must work **without** it. **Resolution:** a behavior matrix + key required only when STT or summary is `External` — see sections 3.5, 6, 7.

### MAJOR-7 — 16k context silently truncates real lectures
A ~30 min Russian lecture is 10-12k tokens (heuristic chars/3); a 60-90 min lecture does not fit in 16384 with a system prompt and output budget. **Resolution:** a deterministic `TranscriptChunker` + `ChunkingSummaryGenerator` (map-reduce) that reuses the same system prompt — see section 3.6.

### MAJOR-8 — CPU/GPU and "single exe" assumptions
`LLamaSharp.Backend.Cpu` is **MIT**, 34.65 MB nupkg, no dependencies; `GpuLayerCount > 0` on a CPU-only backend is a configuration error. "Single exe" is impossible with native runtimes (llama.cpp + sherpa-onnx) unless published self-contained/self-extracting. **Resolution:** `GpuLayerCount=0` default; CPU backend only; clarify DoD — see section 11.

### MAJOR-9 — No test strategy for native-backed code
Native inference can't run in CI. Tests must be validation/arg-construction only (mirror `GigaAmTranscriber.BuildRecognizerConfig` + `GigaAmTranscriberTests`). **Resolution:** section 10.

### MINOR-10 — Schema/type details
Spec key casing `GigaAM`/`GigaChat` vs repo `GigaAm`; `ContextSize`/`GpuLayerCount` are ints but `AppSettingsFile` only has a string helper; both hosts must stay in sync; README/AGENTS section 2.5 license updates. **Resolution:** sections 4, 8.

---
## 1. Objective

Add a **selectable summarization backend** that mirrors the existing selectable transcription backend, while keeping everything that already works unchanged:

- **Transcription (unchanged, selectable):** `External` RouterAI Whisper (default) / `Local` GigaAM-v3 (sherpa-onnx).
- **Summarization (NEW, selectable):**
  - `External` (default) — RouterAI **luna** (`openai/gpt-6-luna`), unchanged.
  - `Local` (opt-in) — **GigaChat3.1-10B-A1.8B** `q4_K_M` GGUF via **LLamaSharp** (in-process llama.cpp).
- **Output (unchanged):** sibling `.md` with YAML frontmatter, `## Краткое содержание`, `## Полный транскрипт`, produced by `MarkdownDocument.Build`; transcript stored verbatim.
- **Prompt (unchanged):** the local summarizer reuses `RouterAiClient.SummarySystemPrompt` **verbatim**.
- **API key:** `ANTHROPIC_AUTH_TOKEN` required **only** when STT or summary is `External`.
- **Backward compatibility:** with no new config keys the application behaves exactly as today (External/External).

The backend is selectable in `appsettings.json` (shared by Console and Win) and in the Win **Settings** dialog.

---

## 2. Gap Analysis — original spec vs. current repo

| # | Original spec assumption | Current repo reality (verified) | Adaptation in this spec |
|---|--------------------------|----------------------------------|--------------------------|
| 1 | Target `.NET 8.0` or higher | `net10.0` global (`Directory.Build.props`); Win `net10.0-windows` | Target `net10.0`; no framework change. |
| 2 | `FFMpegCore` NuGet for preprocessing | ffmpeg invoked out-of-process by `FfmpegCompressor` (`Process`), incl. `ExtractPcmWavAsync` | Reuse `FfmpegCompressor`. No `FFMpegCore`. |
| 3 | `SherpaOnnx` package for STT | Already implemented: `org.k2fsa.sherpa.onnx` 1.13.8 in `AudioParsing.LocalStt` (`GigaAmTranscriber`) | Reuse as-is; STT is not the subject of this change. |
| 4 | `LLamaSharp` + `LLamaSharp.Backend.Cpu` in-process GGUF | Not present | NEW project `AudioParsing.LocalSummary` carrying LLamaSharp (MIT) — keeps Core dependency-free. |
| 5 | Model `GigaChat-3.1-Lightning-Q4_K_M.gguf` (no source) | No such official artifact; "Lightning" GGUF repos are third-party **Uncensored** quants (no license) | Pin `ai-sage/GigaChat3.1-10B-A1.8B-GGUF` / `GigaChat3.1-10B-A1.8B-q4_K_M.gguf` (MIT). |
| 6 | Single custom prompt generates the whole Markdown (frontmatter + strict headings + transcript cleanup) | Structure is deterministic in `MarkdownDocument.Build`; LLM returns summary + trailing `Keywords:` line; transcript verbatim | Keep `MarkdownDocument.Build` as the structural authority. **Reject** the custom template. Reuse `SummarySystemPrompt`. |
| 7 | Spec config `GigaAM`/`GigaChat` keys, no backend selector | `AppSettingsModel { FfmpegPath, TranscriptionModel, SummaryModel, TranscriptionBackend, Language, GigaAm }` parsed by `AppSettingsFile.Load` | Extend additively: `SummaryBackend` + nested `GigaChat { GgufPath, ContextSize, GpuLayerCount }`. |
| 8 | Console/library app only | Console (`Program.cs`) + Win WPF (`PipelineRunner`/`SettingsViewModel`/`SettingsWindow`) over one Core pipeline | Integrate into both hosts; no new host. |
| 9 | "Single executable" offline | ffmpeg + llama.cpp + sherpa-onnx native runtimes; Core hosts 2 GUIs | Clarify DoD: no network when both backends Local; publish self-contained/RID folders, not a literal single exe. |
| 10 | No long-input handling; `ContextSize: 16384` | No summarizer chunking anywhere | Add deterministic `TranscriptChunker` + `ChunkingSummaryGenerator` (map-reduce), same system prompt (section 3.6). |
| 11 | Hand-rolled `<|system|>...<|user|>...<|assistant|>` template | LLamaSharp models ship an embedded chat template; `SummarySystemPrompt` is an invariant | Use `LLamaChatSession` + `ChatHistory(AuthorRole.System/User)`, temperature ~0.2, system text = `SummarySystemPrompt`. |
| 12 | No GPU/CUDA guidance | `LLamaSharp.Backend.Cpu` (MIT, 34.65 MB) vs CUDA/Vulkan packages | CPU backend default, `GpuLayerCount=0`; GPU backend explicitly out of scope. |
| 13 | Output must be UTF-8 with exact headings | Already satisfied via `File.WriteAllTextAsync` + `MarkdownDocument.Build` | Keep; assert Keywords split still round-trips for local output. |

---

## 3. Revised Architecture

```
Input: .mp3 / .m4a / .wav / .mp4 / .mkv / ...   (existing AudioFileFinder discovery)
        |
        v
[ AudioPipeline ]  (stages unchanged: skip -> preprocess -> transcribe -> summarize -> write
        |            per-file AudioFileResult; batch never aborts; temp deleted in finally)
        |  preprocessing gate (unchanged):
        |    External STT -> video OR size > 25 MiB -> ffmpeg -> mono mp3
        |    Local    STT -> ALWAYS                  -> ffmpeg -> 16 kHz mono PCM WAV
        v
[ IAudioTranscriber ]  <- chosen by TranscriptionBackend   (UNCHANGED)
        +- External: RouterAiTranscriber  -> RouterAiClient.TranscribeAsync   (Whisper)
        +- Local:    GigaAmTranscriber    -> sherpa-onnx OfflineRecognizer    (GigaAM-v3 ONNX)
        |  raw transcript
        v
[ ISummaryGenerator ]  <- chosen by SummaryBackend          (NEW)
        +- External: RouterAiSummaryGenerator -> RouterAiClient.GenerateSummaryAsync (luna, UNCHANGED)
        +- Local:    ChunkingSummaryGenerator -> GigaChatSummaryGenerator -> LLamaSharp (GGUF)
        |  raw response: summary text + trailing "Keywords: ..." line
        v
[ MarkdownDocument.SplitSummaryAndKeywords + MarkdownDocument.Build ]   (UNCHANGED)
        -> sibling .md (frontmatter + ## Краткое содержание + ## Полный транскрипт)
```

Design principle: **the pipeline keeps owning stage ordering, error mapping and results**; a backend only swaps *how* a stage is produced. Console and Win stay thin hosts.

### 3.1 Core abstraction (new, dependency-free)

```csharp
namespace AudioParsing;

/// <summary>Selected summarization backend. Persisted as a string in appsettings.json.</summary>
public enum SummaryBackend
{
    /// <summary>RouterAI luna over HTTP (default, current behaviour).</summary>
    External,

    /// <summary>On-device GigaChat GGUF via LLamaSharp.</summary>
    Local,
}

/// <summary>Produces a summarizer response (summary text + trailing "Keywords: ..." line) from a transcript.</summary>
public interface ISummaryGenerator
{
    /// <summary>Short label used in progress messages (e.g. the model name or "GigaChat3.1-10B-A1.8B (local)").</summary>
    string Name { get; }

    /// <summary>Summarizes a raw transcript. Never returns an empty string; throws on failure.</summary>
    Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default);
}
```

### 3.2 External adapter (Core, no new dependency)

```csharp
namespace AudioParsing;

/// <summary>Wraps RouterAI luna summarization behind <see cref="ISummaryGenerator"/>.</summary>
public sealed class RouterAiSummaryGenerator : ISummaryGenerator
{
    private readonly RouterAiClient _client;
    private readonly string _model;

    public RouterAiSummaryGenerator(RouterAiClient client, string model) { /* null/empty guards */ }

    public string Name => _model; // preserves the current "Summarizing with openai/gpt-6-luna." message

    public Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default) =>
        _client.GenerateSummaryAsync(transcript, _model, cancellationToken);
}
```

`RouterAiClient.GenerateSummaryAsync` and `SummarySystemPrompt` are **kept byte-for-byte as-is**.

### 3.3 Local adapter (new project `AudioParsing.LocalSummary`, MIT dependency)

```csharp
namespace AudioParsing.LocalSummary;

/// <summary>On-device summarization via LLamaSharp (GigaChat3.1 GGUF, in-process llama.cpp).</summary>
public sealed class GigaChatSummaryGenerator : ISummaryGenerator, IDisposable
{
    // Validates the GGUF file from settings; native weights are loaded lazily (first call)
    // so a missing/corrupt model surfaces per-file as AudioFileResult.Success=false, never aborting the batch.
    public GigaChatSummaryGenerator(GigaChatSettings settings);

    public string Name => "GigaChat3.1-10B-A1.8B (local)";

    public Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default);

    /// <summary>Validates the GGUF path without touching native code (unit-testable).</summary>
    public static string ResolveGgufPath(GigaChatSettings settings);

    public void Dispose();
}
```

- Built on `LLamaSharp` + `LLamaSharp.Backend.Cpu` (**MIT**; confirmed 0.27.0, backend nupkg 34.65 MB, no deps).
- Isolated in its own project so `AudioParsing.Core` stays third-party-free and the native lib is not loaded for the external path (same rationale as `AudioParsing.LocalStt`, ADR-002).
- Lazy `Lazy<LLamaWeights>` + `Lazy<LLamaContext>` (reused across files); a fresh `LLamaChatSession` **per call** so lectures don't contaminate each other.
- `ModelParams`: `ContextSize = settings.ContextSize`, `GpuLayerCount = settings.GpuLayerCount`.

### 3.4 Prompt & output invariants (explicitly REJECTING the spec prompt)

The local summarizer MUST reproduce the external summarizer's contract exactly:

- **System text = `RouterAiClient.SummarySystemPrompt` (verbatim, same constant).** No dedicated Russian template, no `<|system|>...<|user|>...<|assistant|>` markers (the model's embedded GGUF chat template renders those).
- **User text = the transcript** (unmodified).
- **Temperature analogue = 0.2** via the sampling pipeline (mirrors `GenerateSummaryAsync`'s `0.2`).
- **Returns the same shape** as RouterAI (`summary text + trailing "Keywords: ..." line`) so the **unchanged** `MarkdownDocument.SplitSummaryAndKeywords` + `MarkdownDocument.Build` produce an identical `.md`.
- **No frontmatter/heading generation, no transcript rewrite** inside the prompt (the spec's items 1-4 are rejected).

Consequence: the required headings and YAML frontmatter stay code-owned and deterministic (ADR-004), and existing `MarkdownDocumentTests` / `AudioPipelineTests` need no change.

### 3.5 `AudioPipeline` integration (additive, backward compatible)

```csharp
// Existing ctor — SIGNATURE UNCHANGED (External STT + External summary; behaviour identical).
public AudioPipeline(
    RouterAiClient client, string ffmpegPath,
    string model = DefaultModel, string? language = DefaultLanguage,
    string summaryModel = DefaultSummaryModel)
    : this(client, new RouterAiTranscriber(client, model), TranscriptionBackend.External,
           new RouterAiSummaryGenerator(client, summaryModel), SummaryBackend.External,
           ffmpegPath, language) { }

// Existing backend-agnostic ctor — SIGNATURE UNCHANGED (STT selectable; summary still External).
public AudioPipeline(
    RouterAiClient client, IAudioTranscriber transcriber, TranscriptionBackend backend,
    string ffmpegPath, string? language = DefaultLanguage,
    string summaryModel = DefaultSummaryModel)
    : this(transcriber, backend, new RouterAiSummaryGenerator(client, summaryModel),
           SummaryBackend.External, ffmpegPath, language) { }

// NEW ctor — both stages explicit; tolerates a null client when neither stage is External.
public AudioPipeline(
    IAudioTranscriber transcriber, TranscriptionBackend backend,
    ISummaryGenerator summarizer, SummaryBackend summaryBackend,
    string ffmpegPath, string? language = DefaultLanguage) { /* assigns fields */ }
```

`ProcessFileAsync` replaces the direct summary call (everything else — preprocessing gate, `SplitSummaryAndKeywords`, `Build`, write, `finally`-delete, error mapping — is unchanged):

```csharp
progress?.Report(new PipelineProgress(audioPath, PipelineStage.Summarizing, $"Summarizing with {_summarizer.Name}."));
string summaryResponse = await _summarizer.GenerateSummaryAsync(transcript, cancellationToken).ConfigureAwait(false);
```

The `_client` and `_summaryModel` fields are removed from `ProcessFileAsync`; stage ordering, `AudioFileResult` and per-file error mapping are untouched.

**Behavior matrix (transcription x summary).** `ANTHROPIC_AUTH_TOKEN` is required iff any stage is `External`:

| `TranscriptionBackend` | `SummaryBackend` | ffmpeg preprocessing | `ANTHROPIC_AUTH_TOKEN` | Network calls |
|------------------------|------------------|----------------------|------------------------|---------------|
| External (default)     | External (default) | video OR > 25 MiB -> mp3 | **required** | STT + summary |
| Local                  | External           | always -> 16 kHz WAV  | **required** | summary only |
| External               | Local              | video OR > 25 MiB -> mp3 | **not required** | STT only |
| Local                  | Local              | always -> 16 kHz WAV  | **not required** | **none (fully offline)** |

### 3.6 Context window & chunking (long lectures)

`GigaChatSummaryGenerator` receives a `ContextSize`-limited transcript window. To avoid silent truncation, wrap it in a deterministic **map-reduce decorator** (Core-only, fully unit-testable with a fake inner generator):

```csharp
namespace AudioParsing;

/// <summary>Pure helper: splits a transcript into <= budget segments on paragraph/sentence boundaries.</summary>
public static class TranscriptChunker
{
    /// <summary>Heuristic token estimate (chars/3) plus a fixed system-prompt overhead. Deliberately approximate.</summary>
    public static int EstimateTokens(string text);

    public static IReadOnlyList<string> Split(string transcript, int maxTokensPerSegment);
}

/// <summary>
/// If the transcript fits the context budget, forwards to the inner generator once.
/// Otherwise summarizes each chunk, joins the partial summaries, and summarizes the join once more.
/// Uses the SAME inner generator (hence the SAME SummarySystemPrompt) for every pass, so the final
/// response still ends with a "Keywords:" line and SplitSummaryAndKeywords needs no change.
/// </summary>
public sealed class ChunkingSummaryGenerator : ISummaryGenerator
{
    public ChunkingSummaryGenerator(ISummaryGenerator inner, int contextSize, int reservedOutputTokens);
    public string Name => _inner.Name;
    public Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default);
}
```

- Budget: `maxInputTokens = ContextSize - reservedOutputTokens - EstimateTokens(SummarySystemPrompt)`; default `reservedOutputTokens` ~2048. If `EstimateTokens(transcript) <= maxInputTokens` -> single pass (no behaviour change for short inputs).
- `ChunkingSummaryGenerator` is only applied to the **Local** backend. The External path is untouched (luna handles long input server-side), preserving the current behaviour invariant.
- Segmentation splits on blank lines, then sentence terminators, then a hard character count so no segment exceeds the budget. Deterministic (no randomness) -> assertable in tests.

---

## 4. Configuration Schema

Both `src/AudioParsing.Console/appsettings.json` and `src/AudioParsing.Win/appsettings.json` share the schema. Resolution stays override -> `AppContext.BaseDirectory` -> CWD (`AppSettingsFile.ResolvePath`).

```json
{
  "FfmpegPath": "C:\\Program Files (x86)\\ffmpeg\\ffmpeg.exe",
  "TranscriptionBackend": "External",
  "TranscriptionModel": "openai/whisper-large-v3-turbo",
  "SummaryBackend": "External",
  "SummaryModel": "openai/gpt-6-luna",
  "Language": "ru",
  "GigaAm": {
    "ModelPath": "models/gigaam-v3",
    "EncoderFileName": "encoder.onnx",
    "DecoderFileName": "decoder.onnx",
    "JoinerFileName": "joiner.onnx",
    "TokensFileName": "tokens.txt"
  },
  "GigaChat": {
    "GgufPath": "models/GigaChat3.1-10B-A1.8B-q4_K_M.gguf",
    "ContextSize": 16384,
    "GpuLayerCount": 0
  }
}
```

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `SummaryBackend` | string | `"External"` | `"External"` = RouterAI luna; `"Local"` = GigaChat GGUF. Case-insensitive; unknown -> `External`. |
| `SummaryModel` | string | `AudioPipeline.DefaultSummaryModel` | RouterAI luna model; used **only** by the External summary backend. |
| `GigaChat.GgufPath` | string | `"models/GigaChat3.1-10B-A1.8B-q4_K_M.gguf"` | GGUF file path (file, not directory). Rooted as-is, else exe-relative. |
| `GigaChat.ContextSize` | int | `16384` | LLamaSharp `ModelParams.ContextSize`. Must be > 0. |
| `GigaChat.GpuLayerCount` | int | `0` | LLamaSharp `GpuLayerCount`. `0` = CPU (only supported value for the CPU backend). |

**Model shape (`AppSettingsFile`):**

```csharp
public sealed class GigaChatSettings
{
    public string GgufPath { get; set; } = "models/GigaChat3.1-10B-A1.8B-q4_K_M.gguf";
    public int ContextSize { get; set; } = 16384;
    public int GpuLayerCount { get; set; }
}

public sealed class AppSettingsModel
{
    // existing: FfmpegPath, TranscriptionModel, SummaryModel, TranscriptionBackend, Language, GigaAm
    public string SummaryBackend { get; set; } = "External";            // NEW
    public GigaChatSettings GigaChat { get; set; } = new();             // NEW
}
```

- `AppSettingsFile.Load`: parse `SummaryBackend` via the existing `GetString`, and a nested `GigaChat` object via a new **additive** `GetInt(JsonElement, string, int)` helper (mirrors `GetString` but for numbers, with defaults on missing/malformed). `Save` needs no code change (the serializer already emits nested objects).
- **Read-only facade (`AppSettings`):** add `GetSummaryBackend(string? path = null) : SummaryBackend`, `ParseSummaryBackend(string? value) : SummaryBackend` (case-insensitive, `External` fallback), `GetGigaChatSettings(string? path = null) : GigaChatSettings`. Existing getters unchanged.
- Naming note: the existing key is `GigaAm` (not `GigaAM`); keep that exact casing for continuity.

---

## 5. Model Download (`GigaChatDownloader`, Core, no third-party deps)

Mirrors `GigaAmDownloader` exactly (single file instead of a file set).

```csharp
namespace AudioParsing;

/// <summary>The GigaChat GGUF file to download.</summary>
public sealed record GigaChatModelFile(string FileName, string DisplaySize);

/// <summary>Downloads the GigaChat3.1 GGUF from Hugging Face. Plain HttpClient streaming; no new dependencies.</summary>
public static class GigaChatDownloader
{
    /// <summary>Official, MIT-licensed repository (ai-sage org).</summary>
    public const string RepositoryUrl = "https://huggingface.co/ai-sage/GigaChat3.1-10B-A1.8B-GGUF";

    /// <summary>Default quant (10B total / 1.8B active MoE; ctx 262144 upstream).</summary>
    public const string GgufFileName = "GigaChat3.1-10B-A1.8B-q4_K_M.gguf";

    /// <summary>Human-readable size for the confirmation prompt (~6 GB; verified against Content-Length at runtime).</summary>
    public const string DisplaySize = "~6 GB";

    public static IReadOnlyList<GigaChatModelFile> RequiredFiles { get; } = [ new(GgufFileName, DisplaySize) ];

    /// <summary>Rooted paths are used as-is; relative paths resolve next to the executable.</summary>
    public static string ResolveModelFilePath(string? ggufPath);

    /// <summary>Points settings at the downloaded (HF-named) file.</summary>
    public static void ApplyDownloadedFileName(GigaChatSettings settings);

    /// <summary>
    /// Streams the GGUF into <paramref name="targetFilePath"/> (parent dirs created). An existing file is skipped.
    /// Writes to a sibling ".download" temp and moves it into place, so an interrupted download never leaves a
    /// partial model. Reports overall 0.0-1.0 progress.
    /// </summary>
    public static Task DownloadAsync(
        string targetFilePath, HttpClient httpClient,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
```

- `ResolveModelFilePath` mirrors `GigaAmDownloader.ResolveModelDirectory` but resolves to a **file** (`Path.IsPathRooted ? path : Path.Combine(AppContext.BaseDirectory, path)`), with `/`->`\` normalization.
- The streaming/skip/tmp-move loop is the same shape as `GigaAmDownloader.DownloadFileAsync` (80 KiB buffer, `ResponseHeadersRead`, `ContentLength` when available else the rough weight, `Clamp`ed 0.0-1.0, `File.Move(temp, dest, overwrite:false)`, temp deleted on any failure). Small duplication is acceptable; an optional shared internal `HuggingFaceFileDownloader` helper may be extracted later.
- **Licensing:** the HF repo is `license: mit`; only the official `ai-sage` repo is used (the third-party `*Lightning-Uncensored*` quants are explicitly **not** used).

---

## 6. CLI Changes (`src/AudioParsing.Console`)

| Flag | Status | Behaviour |
|------|--------|-----------|
| `[folder]` / `--folder <path>` | unchanged | as today |
| `--language <code>` | unchanged | as today |
| `--force` / `--routerai-check` | unchanged | as today |
| `--backend <external\|local>` | unchanged | transcription backend |
| `--gigaam-model <path>` | unchanged | GigaAM model dir |
| `--download-gigaam` | unchanged | GigaAM downloader |
| `--help` | MOD | document the new flags |
| `--summary-backend <external\|local>` | **NEW** | overrides `SummaryBackend` |
| `--gigachat-model <path>` | **NEW** | overrides `GigaChat.GgufPath` (file path; if a directory is given, the default file name is appended) |
| `--download-gigachat` | **NEW** | download the GigaChat GGUF (y/N + progress + saves the `GigaChat` section; respects `--gigachat-model`) |

Wiring (mirrors `RunGigaAmDownloadAsync`; refactor shared construction into small private helpers):

```csharp
SummaryBackend summaryBackend = ResolveSummaryBackend(args) ?? AppSettings.GetSummaryBackend();
TranscriptionBackend backend  = ResolveBackend(args) ?? AppSettings.GetTranscriptionBackend();
GigaChatSettings gigaChat = AppSettings.GetGigaChatSettings();
if (ResolveOption(args, GigaChatModelFlag) is string gcPath && !string.IsNullOrWhiteSpace(gcPath))
    gigaChat.GgufPath = gcPath;

bool needsKey = backend == TranscriptionBackend.External || summaryBackend == SummaryBackend.External;
using RouterAiClient? client = needsKey ? RouterAiClient.FromEnvironment() : null;
using GigaAmTranscriber? localStt = backend == TranscriptionBackend.Local ? new GigaAmTranscriber(gigaAm) : null;
using GigaChatSummaryGenerator? localSummary = summaryBackend == SummaryBackend.Local ? new GigaChatSummaryGenerator(gigaChat) : null;

IAudioTranscriber transcriber = localStt ?? new RouterAiTranscriber(client!, model);
ISummaryGenerator summarizer = localSummary is not null
    ? new ChunkingSummaryGenerator(localSummary, gigaChat.ContextSize, reservedOutputTokens: 2048)
    : new RouterAiSummaryGenerator(client!, summaryModel);

AudioPipeline pipeline = new(transcriber, backend, summarizer, summaryBackend, ffmpegPath, language);
```

`RunGigaChatDownloadAsync`: resolve `GigaChatDownloader.ResolveModelFilePath(settings.GigaChat.GgufPath)`, print `RepositoryUrl` / target / `[exists]|[new]` marker / `DisplaySize`, ask `Proceed? [y/N]`, stream with a `Progress<double>` percent line, then `ApplyDownloadedFileName(settings.GigaChat)` + `AppSettingsFile.Save(settings)`, and set `SummaryBackend` to `Local` **only if the user did not already configure a backend** (do not silently flip behaviour). `--help` must state which combinations require `ANTHROPIC_AUTH_TOKEN`.

---

## 7. Win Changes (`src/AudioParsing.Win`)

**`SettingsViewModel` — new bindable properties + commands:**

| Property / Command | UI | Notes |
|--------------------|----|-------|
| `SummaryBackend` (string `"External"`/`"Local"`) | ComboBox «Движок суммаризации»: `Внешний (RouterAI luna)`, `Локальный (GigaChat GGUF)` | drives which fields matter |
| `GigaChatGgufPath` | TextBox + «Обзор…» (`BrowseGigaChatModelCommand`, `ShowOpenFileDialog` filtered to `*.gguf`) | file picker (GGUF is a file) |
| `GigaChatContextSize` (int) | TextBox | positive integer |
| `GigaChatGpuLayerCount` (int) | TextBox | default `0`; help text "0 = CPU" |
| `DownloadGigaChatModelCommand` | «Скачать… (~6 ГБ)» button | confirmation + progress, mirrors GigaAM |
| `SummaryDownloadStatus` | footer | «Скачивание модели… {P0}» / «Модель скачана. Нажмите «Сохранить»…» |
| `FfmpegPath`, `TranscriptionModel`, `SummaryModel` | unchanged | |

**Validation (`TrySave`)** — additive rules:
- If `SummaryBackend == "Local"`: `GigaChatGgufPath` must be a non-empty existing **file** -> else Russian message («Укажите существующий файл модели GigaChat (.gguf).»); `GigaChatContextSize` must be `> 0`; `GigaChatGpuLayerCount` must be `>= 0`.
- If `SummaryBackend == "External"`: `SummaryModel` must be non-empty (existing rule).
- Persist all new fields into `AppSettingsModel` via `ISettingsStore.Save`.

**`SettingsWindow.xaml`** — add a summarization-backend ComboBox row, a GGUF file row (+Обзор/Скачать), and ContextSize/GpuLayerCount rows; replace the current static note "Суммаризация всегда выполняется через RouterAI (luna)" with dynamic guidance (key required only for External), and increase window height (e.g. `Height="920"`) keeping `ResizeMode="NoResize"` (or switch to `CanResize` if the row count grows further).

**`PipelineRunner`** — build both backends from the latest settings on every run (same re-read pattern):

```csharp
TranscriptionBackend backend = AppSettings.ParseTranscriptionBackend(s.TranscriptionBackend);
SummaryBackend summary = AppSettings.ParseSummaryBackend(s.SummaryBackend);
bool needsKey = backend == TranscriptionBackend.External || summary == SummaryBackend.External;
string? key = _apiKey.GetApiKey();
if (needsKey && string.IsNullOrWhiteSpace(key))
    throw new InvalidOperationException($"Не задан ключ API. Задайте переменную среды {EnvApiKeyProvider.EnvVarName}.");

using RouterAiClient? client = needsKey ? _clientFactory(key!) : null;
using GigaAmTranscriber? localStt = backend == TranscriptionBackend.Local ? new GigaAmTranscriber(s.GigaAm) : null;
using GigaChatSummaryGenerator? localSummary = summary == SummaryBackend.Local ? new GigaChatSummaryGenerator(s.GigaChat) : null;
IAudioTranscriber transcriber = localStt ?? new RouterAiTranscriber(client!, s.TranscriptionModel);
ISummaryGenerator summarizer = localSummary is not null
    ? new ChunkingSummaryGenerator(localSummary, s.GigaChat.ContextSize, reservedOutputTokens: 2048)
    : new RouterAiSummaryGenerator(client!, s.SummaryModel);
AudioPipeline pipeline = new(transcriber, backend, summarizer, summary, s.FfmpegPath, language);
```

`IDialogService` needs **no change** (`ShowOpenFileDialog`/`ShowOpenFolderDialog`/`ShowConfirmation` already exist). `App.xaml.cs` DI registrations need no change.

---

## 8. Affected Files (per project)

### `src/AudioParsing.Core` (no new packages)
| File | Change |
|------|--------|
| `SummaryBackend.cs` | **NEW** — enum `External`/`Local`. |
| `ISummaryGenerator.cs` | **NEW** — `Name` + `GenerateSummaryAsync`. |
| `RouterAiSummaryGenerator.cs` | **NEW** — adapter over `RouterAiClient` + model. |
| `TranscriptChunker.cs` | **NEW** — `EstimateTokens` + `Split`. |
| `ChunkingSummaryGenerator.cs` | **NEW** — map-reduce decorator over `ISummaryGenerator`. |
| `GigaChatDownloader.cs` | **NEW** — single-GGUF Hugging Face downloader. |
| `AudioPipeline.cs` | **MOD** — new ctor; 2 existing ctors delegate; `_client`/`_summaryModel` -> `_summarizer`. |
| `AppSettingsFile.cs` | **MOD** — `GigaChatSettings`, `AppSettingsModel.SummaryBackend`/`GigaChat`, nested parse, `GetInt` helper. |
| `AppSettings.cs` | **MOD** — `GetSummaryBackend`, `ParseSummaryBackend`, `GetGigaChatSettings`. |
| `RouterAiClient.cs` | **NO CHANGE** — `SummarySystemPrompt` + `GenerateSummaryAsync` untouched. |
| `MarkdownDocument.cs` | **NO CHANGE** — structural authority. |

### `src/AudioParsing.LocalSummary` (NEW project; MIT runtime deps)
| File | Change |
|------|--------|
| `AudioParsing.LocalSummary.csproj` | **NEW** — `net10.0`; refs `AudioParsing.Core`; `PackageReference LLamaSharp` + `LLamaSharp.Backend.Cpu`. |
| `GigaChatSummaryGenerator.cs` | **NEW** — lazy `LLamaWeights`/`LLamaContext`, per-call `LLamaChatSession`, `SummarySystemPrompt`, temp 0.2, GGUF validation. |

### `src/AudioParsing.Console`
| File | Change |
|------|--------|
| `Program.cs` | **MOD** — `--summary-backend`, `--gigachat-model`, `--download-gigachat`; client-optional wiring; usage text. |
| `appsettings.json` | **MOD** — new schema (section 4). |
| `AudioParsing.Console.csproj` | **MOD** — ref `AudioParsing.LocalSummary`. |

### `src/AudioParsing.Win`
| File | Change |
|------|--------|
| `Services/PipelineRunner.cs` | **MOD** — summary backend + client-optional construction. |
| `ViewModels/SettingsViewModel.cs` | **MOD** — new props/commands/validation/save/load. |
| `Views/SettingsWindow.xaml` | **MOD** — new rows + height + dynamic key note. |
| `appsettings.json` | **MOD** — new schema (section 4). |
| `AudioParsing.Win.csproj` | **MOD** — ref `AudioParsing.LocalSummary`. |
| `Services/IDialogService.cs`, `App.xaml.cs` | **NO CHANGE**. |

### `tests`
| File | Change |
|------|--------|
| `tests/AudioParsing.Tests/AppSettingsTests.cs` | **MOD** — `SummaryBackend` parse/defaults, nested `GigaChat` defaults/overrides/partial, `GetInt` malformed handling. |
| `tests/AudioParsing.Tests/AudioPipelineTests.cs` | **MOD** — new ctor + `FakeSummaryGenerator` (Local summary, no HTTP); existing cases unchanged. |
| `tests/AudioParsing.Tests/RouterAiSummaryGeneratorTests.cs` | **NEW** — delegates to `GenerateSummaryAsync`; `Name == model`. |
| `tests/AudioParsing.Tests/TranscriptChunkerTests.cs` | **NEW** — estimate, budget split, determinism, boundary cases. |
| `tests/AudioParsing.Tests/ChunkingSummaryGeneratorTests.cs` | **NEW** — single-pass when it fits; map-reduce when oversized; same inner generator used. |
| `tests/AudioParsing.Tests/GigaChatDownloaderTests.cs` | **NEW** — mirrors `GigaAmDownloaderTests` (required file, resolve path, apply name, write, skip-existing, cleanup-on-failure). |
| `tests/AudioParsing.Tests/GigaChatSummaryGeneratorTests.cs` | **NEW** — GGUF-path validation throws when missing; no native/model needed. |
| `tests/AudioParsing.Tests/AudioParsing.Tests.csproj` | **MOD** — ref `AudioParsing.LocalSummary` (arg/validation tests only). |
| `tests/AudioParsing.Win.Tests/TestDoubles.cs` | **MOD** — `FakeSummaryGenerator`. |
| `tests/AudioParsing.Win.Tests/SettingsViewModelTests.cs` | **MOD** — summary-backend toggle + GigaChat validation + download. |
| `tests/AudioParsing.Win.Tests/WinServiceTests.cs` | **MOD** — `PipelineRunner` local summary selection; key **not** required when both Local; still required when any External. |

### Build / docs
| File | Change |
|------|--------|
| `AudioParsing.sln` | **MOD** — add `AudioParsing.LocalSummary`. |
| `AGENTS.md` | **MOD** — sections 2.1/2.2/2.5: new project, LLamaSharp + LLamaSharp.Backend.Cpu (MIT) dep note, GigaChat GGUF (MIT) note. |
| `README.md` | **MOD** — summary-backend selection, GigaChat model setup, CLI flags, offline matrix. |
| `docs/local_summary_processing_task.md` | **MOD** — this revised spec. |
| `Directory.Build.props` | **NO CHANGE** (`net10.0` global stays). |

---

## 9. Implementation Plan (ordered; backward compatible)

1. **Core summary abstraction (no behaviour change).** Add `SummaryBackend`, `ISummaryGenerator`, `RouterAiSummaryGenerator`. Add the new `AudioPipeline` ctor; re-express both existing ctors as delegating calls; swap `_client.GenerateSummaryAsync(...)` for `_summarizer.GenerateSummaryAsync(...)`. Run `AudioParsing.Tests` — the existing suite (built via the old ctors) must stay green.
2. **Config schema.** Extend `GigaChatSettings`/`AppSettingsModel`/`AppSettingsFile.Load` (incl. `GetInt`)/`AppSettings`. Update both `appsettings.json`. Add tests.
3. **Chunking (Core-only).** Add `TranscriptChunker` + `ChunkingSummaryGenerator`; unit-test with a fake inner generator (no native code, no HTTP).
4. **Local summary project.** Create `src/AudioParsing.LocalSummary` (`net10.0`) with `LLamaSharp` + `LLamaSharp.Backend.Cpu`; implement `GigaChatSummaryGenerator` (GGUF validation; lazy load; `SummarySystemPrompt`; temp 0.2). Add to `AudioParsing.sln`; add project refs in Console + Win + `tests/AudioParsing.Tests`.
5. **Downloader.** Add `GigaChatDownloader` (mirror `GigaAmDownloader`). Unit-test with a stub `HttpMessageHandler`.
6. **Console integration.** `--summary-backend`, `--gigachat-model`, `--download-gigachat`, client-optional wiring, usage/help; `--routerai-check` unchanged.
7. **Win integration.** Extend `SettingsViewModel` (props/commands/validation/save) + `SettingsWindow.xaml`; make `PipelineRunner` summary-backend-aware and client-optional.
8. **Tests** (Core + LocalSummary + Win) per section 8.
9. **Docs** — README, AGENTS.md, this file.
10. **Verify** (section 10). Iterate until build/test/format are green.

Backward-compatibility guarantees:
- No `SummaryBackend`/`GigaChat` keys -> `External` + luna, identical output to today.
- Existing `AudioPipeline` ctors, `RouterAiClient.SummarySystemPrompt`, `GenerateSummaryAsync`, `MarkdownDocument.Build`/`SplitSummaryAndKeywords`, `AudioFileResult`, `PipelineStage`, error mapping unchanged.
- `ANTHROPIC_AUTH_TOKEN` still required for every pre-existing (External-involving) configuration.

---

## 10. Verification Plan

```bash
# Build (Release)
dotnet build AudioParsing.sln --configuration Release

# Tests (no rebuild)
dotnet test AudioParsing.sln --configuration Release --no-build

# Formatting gate (must be clean)
dotnet format AudioParsing.sln --verify-no-changes
```

Automated coverage:
- `AppSettingsTests`: `SummaryBackend` parse (`"local"`/`"LOCAL"`/unknown->External) + missing->External; nested `GigaChat` defaults, full overrides, partial overrides, malformed ints -> defaults.
- `AudioPipelineTests`: External via old ctors unchanged; new ctor with `FakeSummaryGenerator` asserts the summarizer is called once and `.md` still has both strict headings (no `RouterAiClient`/HTTP needed).
- `TranscriptChunkerTests` / `ChunkingSummaryGeneratorTests`: deterministic splits; single-pass when it fits; map-reduce path otherwise; the same system prompt reaches the inner generator each pass.
- `GigaChatSummaryGeneratorTests`: ctor throws `InvalidOperationException` when the GGUF is absent; no native load.
- `GigaChatDownloaderTests`: `RequiredFiles` has exactly the one GGUF; `ResolveModelFilePath` rooted/relative; `ApplyDownloadedFileName`; download writes the file + progress ends at 1.0; existing file skipped with **zero** HTTP calls; partial temp cleaned up on failure.
- `RouterAiSummaryTests` (existing): unchanged — locks `SummarySystemPrompt`/temperature contract.
- Win: `SettingsViewModel` rejects Local summary with a missing GGUF and persists all fields; `PipelineRunner` selects `GigaChatSummaryGenerator` for Local summary, requires no API key when both backends are Local, and still throws when any backend is External with a missing key.

Manual smoke:
1. Default config -> process a `.mp3` and a `.mp4`; output identical to today.
2. `--summary-backend local --gigachat-model <path>` with a valid GGUF (STT External) -> `.md` produced with luna-free summary + `Keywords:`-derived tags, both strict headings, UTF-8 Cyrillic.
3. `--backend local --summary-backend local` with valid models and **no** `ANTHROPIC_AUTH_TOKEN` set -> whole run succeeds with zero network calls (offline DoD).
4. `--summary-backend local` with a missing GGUF -> per-file failure logged, batch continues, no `.md`.
5. `--download-gigachat` -> y/N prompt, progress to 100 %, `GigaChat` section persisted, resume (existing file) skips download.
6. Win: Settings shows the summary ComboBox + GGUF browse/download; «Сохранить» validates; a full local/local run works with no key.

---

## 11. Definition of Done (updated)

1. `SummaryBackend` is configurable in both `appsettings.json` files and in the Win Settings dialog (`External` default; `Local` opt-in); CLI exposes `--summary-backend` / `--gigachat-model` / `--download-gigachat`.
2. With no new keys, behaviour is byte-for-byte identical to the current pipeline (`External` STT + luna).
3. `Local` summarizes on-device via LLamaSharp + GigaChat3.1 GGUF, **reusing `RouterAiClient.SummarySystemPrompt` verbatim** and producing output that passes the unchanged `MarkdownDocument.SplitSummaryAndKeywords` + `Build`.
4. `Local` STT + `Local` summary runs with **no network calls and no `ANTHROPIC_AUTH_TOKEN`**; `ANTHROPIC_AUTH_TOKEN` is required only when STT or summary is `External` (matrix in section 3.5).
5. Output retains YAML frontmatter and the exact headings `## Краткое содержание` / `## Полный транскрипт`; UTF-8 Cyrillic preserved.
6. Per-file failures (missing GGUF, missing ffmpeg, bad model) never abort the batch and surface as `AudioFileResult.Success=false`; existing stages/results unchanged.
7. Long transcripts are handled by deterministic chunking (no silent truncation); `ContextSize` default 16384.
8. The GigaChat GGUF is downloadable from the official `ai-sage` MIT repo via `GigaChatDownloader` (Console + Win); existing files are skipped; partial downloads never persist.
9. Licensing: `LLamaSharp` + `LLamaSharp.Backend.Cpu` are MIT; GigaChat GGUF is MIT; `AudioParsing.Core` stays third-party-free; `AGENTS.md` section 2.5 updated. No GPL/AGPL anywhere.
10. NuGet closure unchanged for `Core`/`Console`/`Win` except the new `AudioParsing.LocalSummary` project; the native lib loads only when `SummaryBackend=Local`.
11. `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` are green.
12. `README.md`, `AGENTS.md` and this spec reflect the final schema and flags.

**Deployment note (DoD #4 clarification):** "offline" means *no network*, not "a literal single .exe". Ship either a self-contained RID publish (`win-x64` folder with native `llama.dll`/`onnxruntime.dll`) or single-file publish with `IncludeNativeLibrariesForSelfExtract=true`; ffmpeg remains an out-of-process executable.

---

## 12. Trade-offs, ADRs, Open Questions

**ADR-001 — Selectable summary backend, `External` default (Accepted).**
*Decision:* introduce `SummaryBackend`, mirroring `TranscriptionBackend`. *Positive:* zero behaviour change by default; local summarization is opt-in and symmetric with STT. *Negative:* another settings surface and two summary code paths. *Alternatives:* make Local the default (rejected — needs a ~6 GB model and may be slower); External only (rejected — user requirement).

**ADR-002 — Local summary isolated in `AudioParsing.LocalSummary` (Accepted).**
*Decision:* `GigaChatSummaryGenerator` lives in a new project carrying the MIT LLamaSharp packages. *Positive:* `AudioParsing.Core` stays dependency-free; native llama.cpp is never loaded for the External path. *Negative:* one more project; +~35 MB native backend. *Alternatives:* put LLamaSharp in Core (rejected — breaks the zero-dependency invariant and forces the native runtime on all consumers/CI).

**ADR-003 — Reuse `RouterAiClient.SummarySystemPrompt` verbatim; keep `MarkdownDocument` authoritative (Accepted, supersedes the original spec's prompt).**
*Decision:* the local summarizer uses the exact same system text + transcript user message + temp 0.2 and returns the same `summary + Keywords:` shape; frontmatter/headings/transcript stay code-owned. *Positive:* deterministic output; existing tests and consumers unchanged; one prompt to maintain. *Negative:* no LLM transcript cleanup/subheadings via the local model (the original spec's items 1-4). *Alternatives:* the spec's custom `<|system|>` whole-document prompt (rejected — non-deterministic headings, breaks verbatim contract, changes an invariant).

**ADR-004 — Pin the official `ai-sage` MIT GGUF (Accepted).**
*Decision:* download `ai-sage/GigaChat3.1-10B-A1.8B-GGUF` -> `GigaChat3.1-10B-A1.8B-q4_K_M.gguf`. *Positive:* MIT, official, MoE 10B/1.8B-active is CPU-viable, 262144 upstream ctx. *Negative:* ~6 GB download; CPU-only speed for the map/reduce passes. *Alternatives:* the spec's `GigaChat-3.1-Lightning-Q4_K_M.gguf` (rejected — artifact does not exist); the third-party `*Lightning-Uncensored*` GGUF repos (rejected — unlicensed/unsuitable content).

**ADR-005 — Deterministic chunking instead of a larger context (Accepted).**
*Decision:* `TranscriptChunker` + `ChunkingSummaryGenerator` map-reduce for oversized transcripts. *Positive:* correct long-lecture handling; testable without native code; works within 16k. *Negative:* multiple inference passes (slower). *Alternatives:* raise `ContextSize` (rejected — memory/time blow-up, not a correctness fix); no chunking (rejected — silent truncation).

**ADR-006 — CPU backend only; `GpuLayerCount` exposed but non-functional for CPU (Accepted).**
*Decision:* ship `LLamaSharp.Backend.Cpu`; keep `GpuLayerCount=0`. *Positive:* single permissive backend, no CUDA redistribution. *Negative:* no GPU acceleration. *Alternatives:* `LLamaSharp.Backend.Cuda12` (deferred — large CUDA runtime redistribution; requires explicit human approval per section 2.5).

Open questions:
1. Default quant/size: `q4_K_M` (~6 GB) vs `q6_K`/`q8_0` — confirm the trade-off against a real lecture.
2. Exact `reservedOutputTokens` and the token-estimate heuristic (chars/3) — tune after measuring real Russian output.
3. Whether to refactor a shared internal Hugging Face streaming helper out of `GigaAmDownloader`/`GigaChatDownloader` (duplication is acceptable for now).
4. Runtime GGUF presence check in `PipelineRunner`/`Program` before starting a batch (fail fast vs per-file failure).
5. Optional `--context-size` / Win numeric spinners; optional `RouterAiBaseUrl` setting (unchanged from the STT spec).
