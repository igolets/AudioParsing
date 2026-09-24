# AudioParsing — Video File Support (extract text from video, same as audio)

Status: Proposed
Date: 2026-09-23
Scope: `src/AudioParsing.Core`, `src/AudioParsing` (console), `tests/AudioParsing.Tests`
Supersedes nothing. Extends the existing pipeline; does not change audio behaviour.

---

## 1. Context (verified in repo)

| Area | Current state |
|------|---------------|
| Pipeline | `skip-if-.md-exists -> compress via ffmpeg only if >25MB -> TranscribeAsync -> GenerateSummaryAsync -> MarkdownDocument.Build -> write sibling .md` |
| Discovery | `AudioFileFinder.SupportedExtensions` = `.mp3 .mp4 .mpeg .mpga .m4a .wav .webm .flac .ogg .opus .wma .aac`; `FindAudioFiles` recurses and filters by extension |
| ffmpeg args | `FfmpegCompressor.BuildArguments` = `-y -i "<in>" -vn -ac 1 -ar 16000 -b:a 32k "<out>"` — **already strips video (`-vn`) and extracts a mono 16 kHz speech track** |
| Compression gate | `FfmpegCompressor.NeedsCompression(size, max)`; `AudioPipeline.MaxUploadBytes = 25 MiB` |
| Upload MIME | `RouterAiClient.GetAudioMimeType` maps `.MP4 -> audio/mp4`, `.WEBM -> audio/webm` |
| Config | `appsettings.json`: `FfmpegPath`, `TranscriptionModel`, `SummaryModel`. `ANTHROPIC_AUTH_TOKEN` env var holds the key |
| Tests | `AudioFileFinderTests`, `FfmpegCompressorTests`, `AudioPipelineTests` (fake `HttpMessageHandler`), `RouterAi*Tests`, `AppSettingsTests`, `MarkdownDocumentTests`, `GreetingTests` |

### The gap

`.mp4`/`.webm` are discovered today, but a file **≤ 25 MiB is uploaded raw** (video container and all),
and `.mkv .avi .mov .wmv .flv ...` are not discovered at all. Whisper accepts a narrow container set;
sending a raw multi-hundred-`MB`-equivalent video or an unsupported container fails or wastes upload.

### ffmpeg build (verified: `C:\Program Files (x86)\ffmpeg\ffmpeg.exe`, n5.0)

- Demuxers: `matroska,webm`, `mov,mp4,m4a,3gp,3g2,mj2`, `avi`, `mpegts`
- Audio decoders: `aac`, `ac3`, `eac3`, `opus`, `vorbis`, `mp3`, `pcm_s16le`
- Encoder: `libmp3lame` (present) -> mp3 output works

End-to-end proof of the **exact production command** on a synthesized mp4:

```
# synthesize  ->  extract (production args)
ffmpeg -y -f lavfi -i "testsrc=d=1:s=64x64:r=5" -f lavfi -i "sine=f=440:d=1" -shortest -c:v mpeg4 -c:a aac lecture.mp4
ffmpeg -y -i "lecture.mp4" -vn -ac 1 -ar 16000 -b:a 32k "extracted.mp3"
    Stream #0:1: Audio: aac (LC) ...            <- from input
    Output #0: Audio: mp3, 16000 Hz, mono, fltp, 32 kb/s   <- extracted.mp3 (4,788 bytes for 1 s)

# video WITHOUT an audio track, same command:
    exit code 1, no output file
    stderr: "Output file #0 does not contain any stream"
```

---

## 2. Requirement -> Design mapping

| # | Requirement | Design |
|---|-------------|--------|
| 1 | Extract text from video like from audio | Discover video extensions; route them through the **existing** `-vn` ffmpeg extraction before transcription |
| 2 | Same `.md` sibling output | Title = `Path.GetFileNameWithoutExtension(videoPath)`; markdown path via `Path.ChangeExtension(path, ".md")` (unchanged) |
| 3 | Audio behaviour unchanged | Audio still compressed **only** when `> 25 MiB`; no ffmpeg arg change |
| 4 | Minimal change | One union set + one predicate + one branch in the pipeline; no new config keys, no new deps |
| 5 | CLI + WPF drag-drop accept video | Console scans the folder (extension-driven); WPF filters via `AudioFileFinder.IsAudioFile`, which the rule extends automatically |

---

## 3. Affected files

| File | Change | Why |
|------|--------|-----|
| `src/AudioParsing.Core/AudioFileFinder.cs` | **MOD** | Add `SupportedVideoExtensions` + `IsVideoFile`; `IsAudioFile` = audio ∪ video; XML docs |
| `src/AudioParsing.Core/FfmpegCompressor.cs` | **MOD** | Add `RequiresAudioExtraction(path, size, max)`; doc note that `BuildArguments` extracts audio |
| `src/AudioParsing.Core/AudioPipeline.cs` | **MOD** | Branch on `RequiresAudioExtraction`; clarify local names; update XML docs. **Record `AudioFileResult` unchanged** |
| `src/AudioParsing/Program.cs` | **MOD (text only)** | "No audio files found" -> "No supported media files found"; usage text mentions video |
| `src/AudioParsing.Core/RouterAiClient.cs` | **no change** | Video is always transcoded to `.mp3` before upload, so the MIME map is never asked about a video extension |
| `tests/AudioParsing.Tests/AudioFileFinderTests.cs` | **MOD** | Video extension theory + discovery test |
| `tests/AudioParsing.Tests/FfmpegCompressorTests.cs` | **MOD** | `RequiresAudioExtraction` theory; keep `-vn` assertion |
| `tests/AudioParsing.Tests/AudioPipelineTests.cs` | **MOD** | Video-skip unit test; ffmpeg-gated integration tests |

No `.csproj`, `Directory.Build.props`, `appsettings.json`, or solution changes.

---

## 4. Design

### 4.1 Extension allowlist — `AudioFileFinder` (MOD)

Keep the audio set **untouched** (pure addition -> easy review, Do No Harm). Add a video set and
union it in.

```csharp
// existing set — UNCHANGED
private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
{ ".mp3", ".mp4", ".mpeg", ".mpga", ".m4a", ".wav", ".webm", ".flac", ".ogg", ".opus", ".wma", ".aac" };

// NEW
private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm",
    ".wmv", ".flv", ".mpg", ".3gp", ".mts", ".m2ts",
};

/// <summary>True when <paramref name="path"/> is a video container whose audio track must be extracted.</summary>
public static bool IsVideoFile(string path) => SupportedVideoExtensions.Contains(Path.GetExtension(path));

/// <summary>True for any supported media file (audio or video).</summary>
public static bool IsAudioFile(string path) =>
    SupportedExtensions.Contains(Path.GetExtension(path)) || IsVideoFile(path);
```

Decisions:
- **`.ts` excluded** — collides with TypeScript source files; a stray `.ts` would be handed to ffmpeg.
- **`.mpeg` excluded from video** — ambiguous (MPEG audio vs PS video); it keeps its current raw-audio
  behaviour. (`.mpg` is unambiguously program stream -> video.)
- **`.mp4` / `.webm` become "video"** (they are in both sets). This is the one intentional
  behaviour change: a small `.mp4`/`.webm` is now transcoded instead of uploaded raw. It is required
  so that all video follows one code path, and transcode-then-transcribe is strictly more reliable.
- `IsAudioFile` keeps its name so `Program`, tests, and the WPF drop filter need no change
  (the WPF plan calls `AudioFileFinder.IsAudioFile`). Renaming would be churn with no benefit.

`FindAudioFiles` XML doc: "audio files" -> "audio and video files".

### 4.2 Extraction decision — `FfmpegCompressor` (MOD)

```csharp
/// <summary>
/// True when the file must be passed through ffmpeg before upload: video containers
/// always (to drop the video stream and keep only audio), audio only above the upload limit.
/// </summary>
public static bool RequiresAudioExtraction(string path, long fileSizeBytes, long maxBytes)
    => AudioFileFinder.IsVideoFile(path) || NeedsCompression(fileSizeBytes, maxBytes);
```

`BuildArguments` is **not changed** — it already emits `-vn -ac 1 -ar 16000 -b:a 32k`, which is
exactly the audio-extraction needed. Update its XML doc from "downmix" to "extract/downmix".

### 4.3 Pipeline — `AudioPipeline.ProcessFileAsync` (MOD)

Only the compression gate and local names change; stage order, error mapping, `finally` cleanup,
and the `AudioFileResult` record stay identical.

```csharp
string transcriptionInput = audioPath;
string? extractedPath = null;
try
{
    FileInfo info = new(audioPath);
    if (FfmpegCompressor.RequiresAudioExtraction(audioPath, info.Length, MaxUploadBytes))
    {
        extractedPath = Path.Combine(Path.GetTempPath(), $"audioparsing-{Guid.NewGuid():N}.mp3");
        await FfmpegCompressor
            .CompressAsync(_ffmpegPath, audioPath, extractedPath, cancellationToken)
            .ConfigureAwait(false);
        transcriptionInput = extractedPath;
    }

    string transcript = await _client
        .TranscribeAsync(transcriptionInput, _model, _language, cancellationToken)
        .ConfigureAwait(false);
    // ... summary / markdown unchanged, title = Path.GetFileNameWithoutExtension(audioPath)
}
```

Effect:
- **Audio**: `audioPath` unchanged below 25 MiB; above 25 MiB -> extract (identical to today).
- **Video**: always extracted to a temp mono-16 kHz mp3, then transcribed. Temp file deleted in the
  existing `finally`.
- The record field stays `AudioPath`; it now holds any media path. Renaming is out of scope (Do No Harm).

### 4.4 ffmpeg commands (no change to production args)

```bash
# video (mkv/avi/mov/mp4/webm/...): always
ffmpeg -y -i "lecture.mkv" -vn -ac 1 -ar 16000 -b:a 32k "audioparsing-<guid>.mp3"
# audio > 25MB: unchanged, same command
ffmpeg -y -i "call.mp3"     -vn -ac 1 -ar 16000 -b:a 32k "audioparsing-<guid>.mp3"
```

- `-vn` drops video; default stream selection takes the container's **default audio track**
  (honours the `default` disposition — e.g. a `ru` default track is chosen automatically). No `-map`
  is added, so multi-track files follow the muxer's default flag.
- Extension point for a later `--audio-track <n>` flag: `-map 0:a:<n>` (documented, not implemented).

---

## 5. Edge cases

| Case | Behaviour |
|------|-----------|
| Video **without** an audio track | ffmpeg exits 1 with `Output file #0 does not contain any stream`, no output file. `CompressAsync` throws `InvalidOperationException` -> caught in `ProcessFileAsync` -> `AudioFileResult(Success:false, Error:<ffmpeg detail>)`. No `.md` written. **Verified.** |
| Video **> 25 MiB** | Always extracted anyway (extraction is unconditional for video); the gate is a no-op for video. Extracted mp3 is far smaller -> under the limit. |
| Video **≤ 25 MiB** | New path: extracted (was uploaded raw). This is the fix. |
| `.md` already exists | Skipped before any ffmpeg/API call (unchanged); works for video. |
| Multiple audio streams / non-default language | ffmpeg takes the default/first track. Non-default second-language track needs the future `-map` option. |
| ffmpeg missing | `FileNotFoundException` (an `IOException`) -> existing catch -> `Success:false`. Consistent with audio. |
| `.ts` file dropped | Not a supported video extension (TypeScript collision, §4.1) -> not processed. |
| Long media | 16 kHz mono 32 kbps ≈ 4 KB/s ≈ **~1.7 h ≈ 25 MiB**. Beyond that the extracted file still exceeds `MaxUploadBytes` and is uploaded as-is — identical to the current audio limitation, not a regression. Chunking is out of scope (open question). |

---

## 6. Configuration

**No new `appsettings.json` keys.** Reused: `FfmpegPath`, `TranscriptionModel`, `SummaryModel`;
`ANTHROPIC_AUTH_TOKEN`; the language default `ru` and `MaxUploadBytes` constant. The extension
allowlist stays in code (as today).

---

## 7. CLI / WPF impact

- **CLI** (`src/AudioParsing`): no logic change. `ProcessFolderAsync` discovers video via the extended
  set, so `AudioParsing <folder>` picks up videos. Text-only copy edit: "No supported media files
  found in: …" and usage hints "audio and video".
- **WPF** (per `docs/audio-parsing-win.md`): `MainWindow.OnDragOver` already accepts `FileDrop`;
  `PipelineRunner` filters through `AudioFileFinder.IsAudioFile`, which now returns true for video,
  so **drag-drop accepts video with no WPF code change**. Optional: update the Russian drop-zone hint
  to mention video.

---

## 8. Test plan

### Unit (no ffmpeg, no network)
- `AudioFileFinderTests`
  - `IsAudioFile` theory: `.mp4 .m4v .mkv .mov .avi .webm .wmv .flv .mpg .3gp .mts .m2ts` -> true
    (upper-case variants true); `.ts`, `.txt`, `noextension` -> false; `.mp3` -> true.
  - `FindAudioFiles` discovers `a.mkv`, `b.mov`, `c.mp4` and **excludes** `notes.txt` / `clip.ts`.
- `FfmpegCompressorTests`
  - `RequiresAudioExtraction` theory:
    `(clip.mkv, 1 KiB) -> true`, `(clip.mkv, 26 MiB) -> true`, `(song.mp3, 1 KiB) -> false`,
    `(song.mp3, 25 MiB) -> false`, `(song.mp3, 25 MiB + 1) -> true`.
  - Keep `BuildArgumentsDownmixesToMonoSpeechMp3` and add an explicit assertion that the args contain
    `-vn` (the video-stripping proof).
- `AudioPipelineTests`
  - Video with an existing sibling `.md` -> `Skipped:true`, **0** transcription calls, **0** chat
    calls, original `.md` untouched (pure unit; proves skip precedes ffmpeg).

### Integration (gated on ffmpeg availability, no new packages)
Helper: resolve ffmpeg from `AppSettings.GetFfmpegPath()` then `PATH`; if absent, return early and
record why (xUnit 2.9.3 has no runtime skip without a new dependency — avoid `Xunit.SkippableFact`).
Synthesize the fixture at test time (no binary committed):

```bash
ffmpeg -y -f lavfi -i "testsrc=d=1:s=64x64:r=5" -f lavfi -i "sine=f=440:d=1" -shortest -c:v mpeg4 -c:a aac fixture.mp4
```

- Video -> extraction -> transcript -> `.md` beside the video whose title is the file name
  (assert via the existing `CountingHandler`: 1 transcription, 1 chat, `.md` exists).
- No-audio video (`-an`) -> `Success:false`, `Error` contains "does not contain any stream",
  no `.md`.
- Large video (>25 MiB) is covered by the `RequiresAudioExtraction` unit theory; not synthesized
  (slow, redundant).

### Verify (AGENTS.md)
```
dotnet build AudioParsing.sln --configuration Release
dotnet test  AudioParsing.sln --configuration Release --no-build
dotnet format AudioParsing.sln --verify-no-changes
```

---

## 9. Implementation steps (ordered)

1. `AudioFileFinder`: add `SupportedVideoExtensions`, `IsVideoFile`, extend `IsAudioFile`, fix XML docs.
2. `FfmpegCompressor`: add `RequiresAudioExtraction`; update `BuildArguments` doc (no arg change).
3. `AudioPipeline`: gate on `RequiresAudioExtraction`; rename `sourcePath`/`compressedPath` ->
   `transcriptionInput`/`extractedPath`; update XML docs. Record untouched.
4. `Program.cs`: "No supported media files found" + usage text.
5. Tests: finder theory/discovery, compressor `RequiresAudioExtraction`, pipeline video-skip unit,
   ffmpeg-gated video integration (+ no-audio case).
6. Run build / test / format (§8). Update `docs/audio-parsing-win.md` drop-hint text only if that
   spec is implemented in the same change (otherwise leave it).
7. When the WPF shell lands: no pipeline change needed; confirm drag-drop accepts `.mkv`/`.mov`.

---

## 10. Trade-offs / ADR

**ADR-001: Always extract the audio track for video (never upload raw video).**

| | |
|---|---|
| **Decision** | Video containers are always passed through the existing `-vn -ac 1 -ar 16000 -b:a 32k` ffmpeg command before transcription. Audio keeps its size-only gate. |
| **Context** | ffmpeg already emits the correct extraction args; the only defect is the >25 MiB gate and the missing extensions. |
| **Positive** | One code path for every video container; uploads are small (audio-only); works for `mkv/avi/mov`; no new config, deps, or ffmpeg args. |
| **Negative** | Small `mp4/webm` now incur an ffmpeg pass (fast, ms for short clips); every video requires ffmpeg to be present. |
| **Alternatives** | (a) Extend the raw allowlist to `mp4/webm` only — rejected: `mkv/avi/mov` unsupported by the API. (b) `ffprobe` pre-check for an audio stream — rejected: extra binary/dependency; ffmpeg's own error is clear. (c) Chunk long media — deferred (not a regression). |
| **Status** | Proposed |

**ADR-002: Keep `AudioFileResult.AudioPath` and `IsAudioFile` names.**

| | |
|---|---|
| **Decision** | Do not rename public members; document that they now cover video. |
| **Positive** | Console, tests, and the WPF drop filter are unchanged (Do No Harm). |
| **Negative** | Naming is slightly imprecise. |
| **Status** | Proposed |

### Open questions
1. Expose an `--audio-track <n>` flag (or a `Language`/track setting) for multi-track videos where the
   desired language is not the container default?
2. Add chunking/splitting for media whose extracted audio still exceeds 25 MiB (>~1.7 h), shared by the
   audio and video paths?
3. Move the extension allowlist into `appsettings.json` for field-tunable formats, or keep it in code?
