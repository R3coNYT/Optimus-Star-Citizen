# PHASE 3 — Overall architecture

## 4.1 Guiding principles

1. **One application process**, with *sidecars* only where technically necessary (native TTS). No
   microservices (§70 of the brief).
2. **The core knows neither the UI, nor Windows, nor the network.** `Optimus.Core` is a pure
   library, testable in CI with no microphone, no keyboard and no game.
3. **One pipeline, one direction.** The flow always goes from voice to game; no lower layer ever
   calls back into a higher one except through an event.
4. **Everything variable is data**, not code: keybinds, phrases, replies, characters, categories,
   providers.
5. **There is a single point of control.** Every execution goes through the `CommandExecutor`,
   which alone may talk to the `InputEngine`. That is where permissions, the kill switch,
   simulation, cooldowns and logging live.

---

## 4.2 Layered view

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  PRESENTATION                                                                │
│  Optimus.App (WPF)          Optimus.Bridge (local API)      Optimus.Link     │
│  Dashboard, Commands,       REST + WebSocket 127.0.0.1      (Discord bot)    │
│  Keybinds, Copilots, …      auth token                      local pairing    │
└───────────────┬──────────────────────┬───────────────────────────┬──────────┘
                │  ViewModels/Commands │  DTO                      │  Intent
                ▼                      ▼                           ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  APPLICATION  —  Optimus.Core.Orchestration                                  │
│                                                                              │
│   VoicePipeline ──► IntentResolver ──► CommandExecutor ──► ResponseComposer   │
│        ▲                  ▲                   │                    │         │
│        │                  │                   ▼                    ▼         │
│   SessionState      ContextManager      ExecutionGuard         Personality    │
│                                        (perms, kill switch,      Engine       │
│                                         simulation, cooldown)                 │
└───────┬──────────────┬───────────────┬────────────────┬──────────────┬───────┘
        ▼              ▼               ▼                ▼              ▼
┌───────────────┬───────────────┬───────────────┬──────────────┬──────────────┐
│ DOMAIN        │ DOMAIN        │ DOMAIN        │ DOMAIN       │ DOMAIN       │
│ Commands      │ Bindings      │ Copilots      │ Profiles     │ Plugins      │
│ Command,      │ BindingProfile│ Copilot,      │ UserProfile, │ IOptimusPlug.│
│ Action, Macro,│ Binding,      │ Personality,  │ AppSettings  │ PluginHost   │
│ Response      │ InputSpec     │ VoiceConfig   │              │ Permissions  │
└───────┬───────┴───────┬───────┴───────┬───────┴──────┬───────┴──────┬───────┘
        ▼               ▼               ▼              ▼              ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  INFRASTRUCTURE  (adapters — the only code that touches the OS and network)  │
│                                                                              │
│  Audio          Speech            Synthesis      AI            Input         │
│  WasapiCapture  WhisperProvider   SapiProvider   OllamaClient  SendInputEngine│
│  VadDetector    WindowsSpeechProv PiperProvider  OpenAIClient  SimulatedEngine│
│  DeviceWatcher  CloudSttProvider  ElevenLabsProv (optional)    HotkeyService  │
│                                                                              │
│  Game                     Storage                  Diagnostics               │
│  ProcessDetector          SqliteRepository         Serilog sinks             │
│  ForegroundWatcher        JsonConfigStore          TraceRecorder             │
│  ScActionMapImporter      FileWatcher              MetricsCollector          │
│  GameStateProvider(stub)                                                     │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Dependency rule**: arrows never point back up. `Domain` depends on nothing, `Application`
depends on the domain and on infrastructure **interfaces**, `Infrastructure` implements those
interfaces, `Presentation` depends only on `Application`. An architecture test (NetArchTest)
fails CI if the rule is broken.

---

## 4.3 What each component is responsible for

### Voice layer

| Component | Responsibility | Does NOT |
|---|---|---|
| `WasapiCapture` | Opens the input device, produces 16 kHz mono PCM frames | Decide what counts as speech |
| `VadDetector` | Cuts the stream into *utterances* (speech start/end, threshold, trailing silence) | Transcribe |
| `WakeWordDetector` | Signals the presence of the wake word | Transcribe the whole sentence |
| `ISpeechToTextProvider` | `Transcribe(audio, lang) → TranscriptionResult{text, confidence, segments}` | Interpret meaning |
| `VoicePipeline` | Orchestrates capture → VAD → (wake/PTT) → STT, raises `UtteranceRecognized` | Execute anything |

### Understanding layer

| Component | Responsibility |
|---|---|
| `TextNormalizer` | Lower case, accents, punctuation, spelled-out numbers → digits, filler words (“er”, “please”) |
| `PhraseIndex` | An inverted index of the `voice_phrases` of every enabled command of the current copilot |
| `FastIntentMatcher` | Exact → prefix/normalised → fuzzy (token-set + Levenshtein). Returns scored `IntentCandidate[]` |
| `ContextManager` | `ConversationContext` (last N turns, open slot), `GameContext`, `CopilotContext`, `UserContext` |
| `SlotFiller` | Fills missing parameters from the context, or triggers a follow-up question |
| `ILlmProvider` (optional) | Receives text + an **intent whitelist** + context, returns **only** `{intent, parameters, confidence}` as constrained JSON |
| `IntentResolver` | The arbiter: local matcher vs LLM vs follow-up vs failure. A single output: `ResolvedIntent` |

### Execution layer

| Component | Responsibility |
|---|---|
| `CommandRegistry` | Loads, validates and indexes the commands; the source of truth for the whitelist |
| `ExecutionGuard` | Kill switch, simulation mode, game focus, permissions, cooldown, confirmation of `dangerous` actions |
| `BindingResolver` | `(action_id, actionmap) → InputSpec` from the active `BindingProfile`; fails cleanly when unbound |
| `SequenceRunner` | Interprets the steps (`key`, `wait`, `mouse`, `repeat`, `if`), guarantees keys are released in a `finally` |
| `IInputEngine` | `SendInputEngine` (scancodes) or `SimulatedInputEngine` (logs) |
| `CommandExecutor` | The single point of control: guard → resolve → run → result → history |

### Character and reply layer

| Component | Responsibility |
|---|---|
| `PersonalityEngine` | Picks the reply variant from the traits, the state (combat, failure) and the recent history (anti-repetition) |
| `ResponseComposer` | Interpolates the variables (`{ship}`, `{value}`), applies vocabulary and forbidden phrases, bounds the length |
| `ITextToSpeechProvider` | Synthesises; exposes voice, rate, pitch |
| `AudioPlayer` | Queue, priority (an alert cuts a lore line), ducking, barge-in |

### System layer

| Component | Responsibility |
|---|---|
| `GameProcessDetector` | Presence of `StarCitizen.exe`, version/channel, install path |
| `ForegroundWatcher` | Whether the game is in the foreground (the default execution condition) |
| `ScActionMapImporter` | Parses `defaultProfile.xml` + `layout_*.xml`, produces a `BindingProfile` |
| `IGameStateProvider` | The game-state interface. v0.1: a *declarative* implementation (mode set by voice). V2: parsing `Game.log`, OCR, or an API if one exists |
| `HotkeyService` | Global shortcuts (PTT, kill switch, mute) through `RegisterHotKey` or a low-level hook |

---

## 4.4 Process model

```
┌──────────────────────────────── Optimus.exe (1 process) ──────────────────────────────────┐
│                                                                                            │
│  UI thread (WPF)   Audio thread (real time)     Worker pool         Background services    │
│  ─────────────     ───────────────────────      ───────────         ───────────────────    │
│  Rendering,        WASAPI + VAD                 STT (Whisper)       Bridge (Kestrel loopb.)│
│  bindings          ring buffer                  Intent + execution  Discord (WebSocket)    │
│  never blocked     zero allocation in the loop  TTS                 File/process watchers  │
│                                                                                            │
│  Internal event bus (Channel<T> / IObservable) — no direct cross-calls                      │
└────────────────────────────────────────────────────────────────────────────────────────────┘
              │ stdin/stdout or a named pipe                    │ HTTP 127.0.0.1
              ▼                                                  ▼
      piper.exe (neural TTS, optional)                Tablet / browser / overlay
```

**Why this split:**
- The audio thread must **never** wait on a disk, a model or the network → it only fills a buffer
  and raises events.
- STT inference is CPU-heavy → a worker pool, one inference at a time, cancellable.
- Neural TTS (Piper) is a native binary → a sidecar process, restartable, whose crash carries no
  consequence (SAPI fallback).
- A crash of the Discord bot or the Bridge must not kill the voice loop → independently hosted
  services with a restart policy.

---

## 4.5 The nominal flow (detailed sequence)

```
User: “Optimus, open the doors”

 t+0     WasapiCapture      20 ms frames ─────────────────────────────────┐
 t+0     VadDetector        speech_start                                   │ trace_id = 7f3a
 t+1180  VadDetector        speech_end (280 ms of silence)                 │
 t+1185  VoicePipeline      1.18 s utterance → STT                         │
 t+1490  WhisperProvider    "optimus open the doors"  conf 0.94            │
 t+1492  WakeWordFilter     prefix "optimus" found → payload "open the doors"
 t+1493  TextNormalizer     "open the doors"
 t+1495  FastIntentMatcher  exact → ship.doors.toggle   score 1.00
 t+1496  ExecutionGuard     killswitch=off · simulation=off · SC foreground=true
                            · permission=local · cooldown ok · dangerous=false
 t+1497  BindingResolver    spaceship_general/v_toggle_all_doors → { key=L, mods=[], tap }
 t+1499  SequenceRunner     scancode 0x26 down → 45 ms → up
 t+1545  PersonalityEngine  variant “Compartments unlocked.” (military 80, humour 30)
 t+1548  TtsProvider        synthesis (streaming) → first audible sample
 t+1620  AudioPlayer        playback
 t+1621  HistoryRepository  entry persisted (phrase, intent, score, binding, 128 ms of execution)
```

**Perceived latency = t+1499 − t+1180 ≈ 320 ms** between the end of speech and the keypress.
That is the RNF-01 target. The TTS arrives *after* the action: we never wait for the voice to act.

---

## 4.6 Alternative paths

```
                       ┌──────────────────────────────┐
   normalised text ───►│ FastIntentMatcher (local)    │
                       └───────┬──────────────┬───────┘
                score ≥ 0.85   │              │  0.55 ≤ score < 0.85
                               ▼              ▼
                        immediate      ┌───────────────────────┐
                        execution      │ several candidates?   │
                                       └───┬───────────────┬───┘
                                     yes   │               │ no
                                           ▼               ▼
                              disambiguating follow-up   confirmation
                              “Forward or aft             “Do you mean … ?”
                               shields?”
                       score < 0.55
                               │
                               ▼
                    ┌──────────────────────┐   LLM disabled
                    │ LLM enabled?         ├──────────────► “I do not know that
                    └──────────┬───────────┘                  command.”
                               │ yes                          + unknown_phrase log
                               ▼
                    ┌────────────────────────────────────┐
                    │ LLM: text + whitelist + context    │
                    │ → { intent, parameters, confidence}│
                    └──────────┬─────────────────────────┘
                               ▼
                    intent ∈ whitelist?  ── no ──► reject + security log
                               │ yes
                               ▼
                    pure conversation? ──► TTS reply only (no action)
                               │
                               ▼
                    ExecutionGuard → … (the nominal path)
```

---

## 4.7 Per-user isolation (requirements §81–83)

```
   A's PC                                    B's PC
┌──────────────────────────┐              ┌──────────────────────────┐
│ Optimus A                │              │ Optimus B                │
│  binds A · copilot A     │              │  binds B · copilot B     │
│  history A               │              │  history B               │
│  InputEngine ─► keyboard A│             │  InputEngine ─► keyboard B│
│         ▲                │              │         ▲                │
│         │ validated intent│             │         │                │
│  ┌──────┴───────┐        │              │  ┌──────┴───────┐        │
│  │ Local bridge │        │              │  │ Local bridge │        │
│  │ 127.0.0.1    │        │              │  │ 127.0.0.1    │        │
│  └──────▲───────┘        │              │  └──────▲───────┘        │
└─────────┼────────────────┘              └─────────┼────────────────┘
          │ OUTBOUND WebSocket, paired, encrypted   │
          └──────────────┬──────────────────────────┘
                         │
                 ┌───────┴────────┐   holds ONLY intents and states,
                 │ Discord / relay│   never keystrokes, never inbound access
                 └────────────────┘
```

Three non-negotiable invariants:
1. **Keystrokes are always produced locally**, by the user's own process, on their machine.
   Nothing else can call `SendInput`.
2. **The connection is outbound**: no instance listens on the network; the relay cannot “push” to
   an unpaired machine.
3. **Pairing is explicit and local**: a one-time code generated in the app, typed on the Discord
   side, revocable in one click, with `execute_commands = false` by default.

The recommended default for V1 is a **local Discord bot** (the user supplies their own token) —
no central server, isolation guaranteed by construction. A relay mode only appears in V2, and
stays optional.

---

## 4.8 Naming

| Item | Name | Role |
|---|---|---|
| Product | **Optimus** | The platform |
| Desktop application | **Optimus Command Center** (`Optimus.App`) | The interface |
| Engine | **Optimus Core** (`Optimus.Core`) | Pipeline, domain, orchestration |
| Local API | **Optimus Bridge** (`Optimus.Bridge`) | REST + WS on loopback |
| Discord integration | **Optimus Link** (`Optimus.Link`) | Bot + pairing |
| Plugin SDK | **Optimus SDK** (`Optimus.Sdk`) | Stable public contracts |
| Copilot pack | **`.optcopilot`** | A signed ZIP archive |
| A copilot | **Copilot** | Optimus, Synthia, Virgil, Atlas… |

> Note: *Optimus* is also a strong trademark associated with a well-known franchise. Before any
> public distribution, plan a name availability check, or a distinct product name with “Optimus”
> as the default copilot's name. To be settled before public packaging — with no impact on the
> architecture.
