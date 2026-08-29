# PHASE 8 — Voice architecture and latency budget

## 9.1 The whole chain

```
 ┌──────────┐   PCM 16 kHz mono, 20 ms frames
 │ MIC      │──────────────┐
 └──────────┘              ▼
                    ┌─────────────┐  a 3 s ring buffer (pre-roll)
                    │ AudioBuffer │  → lets us recover the start of a sentence
                    └──────┬──────┘     even when the trigger arrives late
                           ▼
         ┌────────────────────────────────────┐
         │ Trigger (3 exclusive modes)        │
         │  • PTT      : a key held down      │  ← the most reliable, the MVP default
         │  • Wake word: "Optimus" detected   │
         │  • Always   : VAD alone            │
         └──────┬─────────────────────────────┘
                ▼
         ┌─────────────┐  Silero VAD: speech_start / speech_end
         │     VAD     │  280 ms of trailing silence (configurable 150–600)
         └──────┬──────┘  minimum 250 ms · maximum 12 s (a guard rail)
                ▼
         ┌─────────────┐  Whisper small Q5, language forced (no autodetection: +150 ms)
         │     STT     │  priming prompt = the domain's vocabulary
         └──────┬──────┘  (ship names, “quantum”, “shields”, “mobiGlas”…)
                ▼
         ┌──────────────────┐  strips the wake word, normalises
         │  NORMALISATION   │
         └──────┬───────────┘
                ▼
         ┌─────────────┐  exact → fuzzy → (optional LLM)
         │   INTENT    │
         └──────┬──────┘
                ├──────────────► EXECUTION (takes priority, never waits on the TTS)
                ▼
         ┌─────────────┐  character → variant → interpolation
         │   REPLY     │
         └──────┬──────┘
                ▼
         ┌─────────────┐  streamed when the provider allows it
         │     TTS     │
         └──────┬──────┘
                ▼
         ┌─────────────┐  a prioritised queue, ducking, barge-in
         │  PLAYBACK   │
         └─────────────┘
```

**A structuring decision: the action never depends on the TTS.** We press the key, *then* speak.
A slow TTS degrades comfort, never the game's responsiveness.

---

## 9.2 Latency budget

Measured from `speech_end` (end of speech detected) to the keyboard event.

| Stage | p50 target | p95 target | How we hold it |
|---|---|---|---|
| VAD → end decision | 280 ms | 350 ms | configurable trailing silence; PTT = 0 ms (it ends on release) |
| Transcription (Whisper small, 1.5 s of audio, 8-core CPU) | 250 ms | 500 ms | quantised model, tuned `n_threads`, reduced context, no language autodetection |
| Normalisation + local matching | 3 ms | 10 ms | an in-memory index, no I/O |
| Guard + binding resolution | 1 ms | 3 ms | in-memory tables |
| Injection (45 ms tap) | 45 ms | 50 ms | `SendInput` directly |
| **Total voice → key** | **≈ 580 ms** | **≈ 900 ms** | **within RNF-01** |
| *(bonus)* first TTS sound | **7 ms** | **15 ms** | **Measured (S0-5)**: RTF 0.001–0.003 with the OneCore voices. The TTS is beside the point for latency — provided the engine is warmed up (429 ms on the very first synthesis) |
| *(with a local Ollama 7B)* | +400 to 900 ms | — | only when the local matcher fails |
| *(with a cloud LLM)* | +600 to 2,000 ms | — | never on the path of known commands |

### The local neural voice (Piper)

Windows voices are beyond reproach on latency and arguable on timbre. Piper reverses exactly that
trade-off, and the pilot picks which of the two they prefer — `voice.provider` in the copilot's
file, or the checkbox in the settings.

**Local in the strong sense**: the model runs on the pilot's machine. Nothing goes over the
network, Optimus stays usable offline, and what the copilot says reaches nobody. That is what
separates Piper from an online synthesis service, whose timbre might well be better.

#### Measurements from 2026-08-27 (12 cores, French models)

| | Loading the voice | Synthesis per reply | Real-time factor |
|---|---|---|---|
| Windows OneCore voice | 429 ms, once (D23) | **7 to 15 ms** | 0.003 |
| Piper `fr_FR-tom-medium` | **620 to 785 ms** | **377 to 455 ms** | 0.113 |
| Piper `fr_FR-gilles-low` | **318 ms** | **214 ms** | 0.047 |

Piper therefore costs about **forty times** a Windows voice per reply. What makes the trade
acceptable is written above, and it is the pipeline's structuring decision: **the action never
depends on the TTS**. The key has already been sent when Optimus comments. The delay lands on the
comment, never on the game's responsiveness.

A `low` voice halves the wait for a barely less rich timbre: that is the setting to try before
giving up.

#### A persistent process, and why

Restarting `piper.exe` for every sentence would pay the model loading cost — 0.6 s — **before
every word**, which is nearly a second of waiting per reply. The process therefore stays open with
the voice loaded, and the startup warm-up genuinely waits for the readiness announcement: without
that wait, the first reply cost 740 ms, which emptied D23 of its meaning.

The protocol chosen is the one that was verified: **one line of text** on standard input, **one
WAV file path** on standard output, logs on standard error. The `--json-input` mode was tried and
rejected — it ignores `length_scale` in this version, which would have made the rate setting
useless without anything saying so.

#### Installation

Piper does not ship with Optimus: 22 MB of binary and 63 MB per voice, for a feature you can do
without. The installation lives in `%APPDATA%\Optimus\piper`, outside the `data/` that the publish
script overwrites (the same principle as D35, D43 and D46).

```
%APPDATA%\Optimus\piper\
├── piper.exe            (+ its DLLs and espeak-ng-data, as shipped in the archive)
└── voices\
    ├── fr_FR-tom-medium.onnx
    └── fr_FR-tom-medium.onnx.json
```

The binary comes from the `rhasspy/piper` releases (`piper_windows_amd64.zip`), the voices from
`huggingface.co/rhasspy/piper-voices` — each voice being a `.onnx` **and** its `.onnx.json`, both
required. Optimus only accepts the installation when both are there: a Piper with no model is a
half-done installation, and accepting it would leave the copilot mute while the pilot works out
why.

**This installation is specific to each machine.** The folder does not follow the publish: on a
second machine you either copy it across or untick the box — in which case the Windows voices take
over, with a log line saying so.

#### Nothing can leave the copilot mute

Piper is an external process: an antivirus can kill it, a model can be corrupt, a disk can fill up.
The Windows voices, on the other hand, are always there. The main engine is therefore doubled, and
**abandoned after two consecutive failures** — retrying forever would make every reply pay its
timeout, which would be far worse than a change of timbre. The pilot hears a different voice,
which is a signal in itself, and the log says why.

---

### Planned optimisations

| Technique | Gain | Complexity |
|---|---|---|
| **A 3 s pre-roll**: we transcribe from before the trigger | avoids clipped openings (the leading cause of recognition failure). **Measured (S0-3): opening the capture device costs 419 ms** — opening it on the PTT press would lose the first third of a second of every sentence | low |
| **PTT by default in the MVP** | removes the 280 ms of VAD and the false triggers | none |
| **Model loaded and “warmed” at startup** | avoids 800 ms on the first command | low |
| **Incremental transcription** (decoding while the user is still speaking) | −100 to 200 ms | medium, V1 |
| **A cache of frequent TTS replies** (WAV cached by hash of text + voice) | a near-instant first sound on the 30 most used replies | low, large perceived effect |
| **An acknowledgement beep** (10 ms, immediate on trigger) | halves *perceived* latency at no cost | trivial — to do from the MVP |
| **Automatic model choice** by benchmark at first launch | avoids putting `medium` on a weak CPU | low |

---

## 9.3 Listening modes

| Mode | Trigger | End | Advantages | Drawbacks | Status |
|---|---|---|---|---|---|
| **Push-to-talk** | a key held down | release | zero false positives, minimal latency, robust to noise | occupies a key and a finger | **MVP default** |
| **Wake word** | “Optimus” | VAD | hands free, immersive | false positives, constant CPU cost | degraded in the MVP, native in V1 |
| **Always listening** | VAD alone | VAD | the most natural | everything you say gets transcribed (Discord voice!) | V1, explicit opt-in |
| **Toggle** | a key = on/off | the key | a compromise | easy to forget | V1 |

**Wake word in v0.1 (degraded mode, free)**: we transcribe the whole utterance and check that the
text starts with the wake word (with fuzzy tolerance: “optimus”, “optimouse”, “optimus,”, “ok
optimus”). The cost: one useless transcription when it was not meant for him — acceptable with
PTT, arguable in always-on, hence the priority given to a native detector in V1.

---

## 9.4 Provider interfaces

```csharp
public interface ISpeechToTextProvider : IAsyncDisposable
{
    string Id { get; }                       // "whisper-local", "windows-speech", "azure"
    SttCapabilities Capabilities { get; }     // languages, streaming, offline, GPU
    Task InitializeAsync(SttOptions o, CancellationToken ct);
    Task<TranscriptionResult> TranscribeAsync(AudioSegment audio, CancellationToken ct);
    IAsyncEnumerable<PartialTranscription> StreamAsync(IAsyncEnumerable<AudioFrame> f, CancellationToken ct);
}

public interface ITextToSpeechProvider : IAsyncDisposable
{
    string Id { get; }
    Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken ct);
    Task<SynthesisResult> SynthesizeAsync(SynthesisRequest r, CancellationToken ct); // may stream
}

public interface ILlmProvider
{
    string Id { get; }
    Task<StructuredIntent> ResolveIntentAsync(IntentRequest r, CancellationToken ct);
    Task<string> ChatAsync(ChatRequest r, CancellationToken ct);
}

public interface IVoiceActivityDetector { VadEvent Process(ReadOnlySpan<float> frame); }
public interface IWakeWordDetector      { bool Process(ReadOnlySpan<float> frame, out float score); }
```

**Automatic fallback policy**: every provider declares a fallback. On an initialisation failure or
three consecutive errors, Optimus switches to the fallback, announces it visually (an amber pill on
the dashboard) and logs it. It never fails silently.

---

## 9.5 Audio processing and known pitfalls

| Problem | Chosen solution |
|---|---|
| **The TTS voice triggers Optimus** | A suppression window: the VAD ignores input while the TTS plays, plus 200 ms (rather than plain AEC, needlessly complex) |
| **The game or Discord holds the microphone** | WASAPI in **shared** mode only; never exclusive mode |
| **Fan noise / a mechanical keyboard** | A neural VAD (Silero) rather than an energy threshold; normalised input gain (gentle AGC) |
| **The microphone unplugged or changed mid-session** | `MMDeviceEnumerator` + a change notification → automatic reconnection, a UI message |
| **Several people talking (Discord in the background)** | PTT solves everything; in always-on, a raised STT confidence threshold + rejection of phrases without the wake word |
| **Audio output to route to OBS/stream** | Output device chosen per copilot; virtual cables supported |
| **A sentence clipped at the start** | The 3 s pre-roll (see §9.2) |
| **Game terms badly transcribed** (“quantum”, “Crusader”, “mobiGlas”) | A Whisper `initial_prompt` holding the domain lexicon + post-correction through a phonetic dictionary of the game |

---

## 9.6 Debug mode for the voice chain (§23)

```
┌─ PIPELINE TRACE ──────────────────────── trace 7f3a ── 21:42:15.318 ─┐
│ MIC        Realtek Array   −18 dBFS   ▇▇▇▇▇▅▂                        │
│ VAD        speech_start 15.114 → speech_end 16.294   (1,180 ms)      │
│ TRIGGER    push_to_talk (F10)                                        │
│ STT        whisper-small-q5 · fr-FR · 268 ms                         │
│            “optimus ouvre les portes”            conf 0.94           │
│ WAKE       prefix “optimus” stripped                                 │
│ NORMALISED “ouvre les portes”                                        │
│ INTENT     ship.doors.toggle          score 1.00  (exact)            │
│            2nd: ship.doors.close      score 0.71   Δ 0.29 → OK       │
│ GUARD      killswitch off · sim off · SC foreground ✓ · cooldown ✓   │
│ BINDING    spaceship_general/v_toggle_all_doors → L                  │
│ EXEC       scancode 0x26 ↓ 45 ms ↑                       128 ms      │
│ RESPONSE   “Compartiments déverrouillés.”     (var. 2/3, humor 40)   │
│ TTS        windows-onecore · Denise · 141 ms                          │
│ TOTAL      voice → key 585 ms │ voice → speech 742 ms                │
└──────────────────────────────────────────────────────────────────────┘
```

This is the screen that answers “why didn't my command work?” — and therefore the most profitable
screen in the product. It must be copyable in one click (for Discord support) and exportable as
JSON.
