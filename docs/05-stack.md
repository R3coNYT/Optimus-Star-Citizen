# PHASE 4 — Technical stack

## 5.1 Choosing the application foundation

| Criterion (weight) | .NET 8 + **WPF** | .NET 8 + WinUI 3 | Electron | Tauri (Rust) | Python + Qt |
|---|---|---|---|---|---|
| Idle RAM (×3) | ~120 MB ✅ | ~150 MB ✅ | 300–450 MB ❌ | ~70 MB ✅✅ | ~180 MB 🟡 |
| FPS impact alongside the game (×3) | negligible ✅ | negligible ✅ | shared GPU ❌ | negligible ✅ | negligible ✅ |
| Low-level audio (×3) | WASAPI/NAudio, mature ✅ | same ✅ | through native ❌ | cpal, adequate 🟡 | sounddevice ✅ |
| Scancode keyboard injection (×3) | direct P/Invoke ✅ | same ✅ | node-ffi, fragile ❌ | winapi crate ✅ | pywin32/ctypes 🟡 |
| Latency / GC (×2) | server GC, controllable ✅ | same ✅ | ❌ | no GC ✅✅ | GIL ❌ |
| Cold start (×2) | 1–2 s ✅ | 2–4 s 🟡 | 2–4 s 🟡 | < 1 s ✅✅ | 3–6 s ❌ |
| Tray, global hotkeys, single instance (×2) | trivial ✅ | packaging constraints 🟡 | plugins ✅ | ok ✅ | ok ✅ |
| STT/TTS/AI ecosystem (×2) | Whisper.net, ONNX Runtime ✅ | same ✅ | through sidecars 🟡 | whisper-rs 🟡 | **the best** ✅✅ |
| Discord (×1) | Discord.Net ✅ | same ✅ | discord.js ✅ | serenity ✅ | discord.py ✅ |
| Speed of building a sci-fi UI (×2) | XAML + styles 🟡 | XAML 🟡 | HTML/CSS ✅✅ | HTML/CSS ✅✅ | QML 🟡 |
| Packaging / auto-update (×2) | Velopack ✅ | MSIX, constraining 🟡 | electron-updater ✅ | built in ✅ | PyInstaller ❌ |
| Maturity / project risk (×3) | very low ✅ | medium 🟡 | low ✅ | medium 🟡 | high ❌ |
| **Verdict** | **CHOSEN** | rejected | rejected | 2nd choice | rejected as a foundation |

### Decision: **C# / .NET 8 + WPF**

**Three reasons:**
1. **It is the only stack that ticks all three hard constraints at once**: quality real-time
   audio, frictionless native P/Invoke for `SendInput`, hooks and `RegisterHotKey`, and a
   footprint compatible with a game that already eats 16 GB of RAM and 100% of the GPU.
2. **Project risk is the lowest.** WPF has been stable for fifteen years, the Win32 documentation
   in C# is abundant, and the AI pipeline now exists in pure .NET (Whisper.net, ONNX Runtime) —
   which **removes the dependency on Python**, the main packaging complexity for this kind of app.
3. **Tauri was the serious contender** (smaller footprint, a sci-fi UI far quicker to build in
   CSS). It is rejected on overall velocity: two languages (Rust + TS), a thinner
   AI/Discord/Windows ecosystem, and a learning curve that would penalise exactly the longest
   parts of the project (command engine, keybinds, plugins). **If the sci-fi UI becomes the
   bottleneck**, the exit exists without breaking anything: `Optimus.Core` and `Optimus.Bridge`
   are independent of the UI, and a web shell could consume the Bridge.

**Modernising the WPF UI**: `CommunityToolkit.Mvvm` (MVVM without boilerplate), `WPF-UI` or
`HandyControl` as a control base, a custom “avionics” theme on top, `LiveChartsCore`/`ScottPlot`
for the analytics charts, `H.NotifyIcon.Wpf` for the tray.

---

## 5.2 Decisions per component (condensed ADR format)

| # | Component | Decision | Rationale | Alternative kept in reserve |
|---|---|---|---|---|
| 1 | **Language** | C# 12 / .NET 8 LTS, `nullable enable`, self-contained x64 publish | Nothing for the user to install first, LTS until 2026+ | .NET 9/10 once LTS |
| 2 | **Desktop** | WPF + MVVM | see §5.1 | Tauri through the Bridge |
| 3 | **Audio in** | **NAudio** (WASAPI capture, event-driven, 16 kHz mono) | The .NET reference, low latency, handles hot-plug | CSCore |
| 4 | **VAD** | **Silero VAD** in ONNX through `Microsoft.ML.OnnxRuntime` | Far better than an energy threshold in a noisy environment (fans, game, TS/Discord); ~1 MB, < 1 ms per frame | WebRTC VAD (a simple fallback, no model) |
| 5 | **STT** | **Two tiers** (D28). ① **Constrained grammar**: `System.Speech` + the Windows engine, with a grammar built from the catalogue's phrases → the 59 commands. ② **Whisper.net** with the `base` model → conversation and free speech | Measured (S0-2 and S0-6): **16.7 ms against 3,336 ms** with the game running, and 21/21 commands correct. `base` chosen over `small` (no more accurate, 3.4× slower) and over `tiny` (59% WER, misses the wake word). Tier ① needs neither a GPU nor a download | Vosk if the Windows engine is missing; a cloud provider through the interface. A GPU build of whisper.cpp is **rejected on the target machine**: 6 GB of VRAM against the 7.3 the game claims |
| 6 | **Wake word** | v0.1: the prefix detected in the transcript (free). V1: **openWakeWord** in ONNX, with an “optimus” model trained | No commercial dependency and no account | **Picovoice Porcupine** (better quality, but licence and account to be cleared before integrating) |
| 7 | **TTS** | v0.1: **Windows OneCore** (`Windows.Media.SpeechSynthesis`) — native voices, nothing to install. Option: **Piper** (ONNX, sidecar) for a local neural voice | Two levels: it works immediately, and it becomes beautiful if you want it to | ElevenLabs / Azure / OpenAI TTS through `ITextToSpeechProvider` (cloud, opt-in) |
| 8 | **LLM** | **Optional**, off by default. An `ILlmProvider` interface; implementations: Ollama (local), OpenAI-compatible (OpenAI, OpenRouter, LM Studio), Anthropic | §84 of the brief: nothing may depend on the cloud. **Constrained JSON** output (grammar/JSON mode) + whitelist validation | — |
| 9 | **Fuzzy matching** | **FuzzySharp** (token-set ratio) + weighted Levenshtein; V1: ONNX embeddings (`multilingual-e5-small`) for semantic recall | Deterministic, testable, under 5 ms over 1,000 phrases | — |
| 10 | **Injection** | **`SendInput`** with `KEYEVENTF_SCANCODE` (P/Invoke), mouse included, a **fixed scancode table in US positions** (never `MapVirtualKey`), `timeBeginPeriod(1)` during sequences | **Measured in spike S0-1**: a virtual-key-only injection arrives in Raw Input with `MakeCode = 0x00`, and is therefore invisible to an engine reading the scancode; and `MapVirtualKey` returns wrong scancodes on AZERTY | The **Interception** driver as a plugin if a case resists (it installs a driver → never in the MVP) |
| 11 | **Game detection** | `Process.GetProcessesByName("StarCitizen")` + `GetForegroundWindow`/`GetWindowThreadProcessId`; the path through the launcher, the process, a scan, or manual entry | No hard-coded path (§59) | WMI `Win32_ProcessStartTrace` for an event-driven version |
| 12 | **SC keybinds** | A dedicated XML parser (`System.Xml.Linq`): `defaultProfile.xml` (defaults, extracted from `Data.p4k` with **unp4k**) ⊕ `layout_*.xml` (deltas) | see `docs/02` | Presets shipped per version if `Data.p4k` is unreachable |
| 13 | **Database** | **SQLite** (`Microsoft.Data.Sqlite` + **Dapper**), WAL mode | History, analytics, embedding cache, pairings. Light, embedded, no heavy ORM | EF Core if the migrations become complex |
| 14 | **Configuration** | **Canonical JSON** (`System.Text.Json`, source-generated) + **JSON schemas** validated at load; **YAML accepted** for import and export (`YamlDotNet`) | JSON for tooling, diffs and strict validation. YAML for comfortable hand editing. Both, not one against the other | — |
| 15 | **Local API** | **ASP.NET Core Minimal API** hosted in-process, **Kestrel bound to `127.0.0.1`**, bearer token, WebSocket for real time | One process, no Windows service, minimal attack surface | gRPC if a native client demands it |
| 16 | **Discord** | **Discord.Net** (slash commands, embeds), “local bot” mode by default (the user's own token) | Isolation guaranteed with no central server (§82) | DSharpPlus; relay mode in V2 |
| 17 | **Logs** | **Serilog**: console + a daily rotating file (30 days) + an in-memory UI sink; structured format with `trace_id` | End-to-end correlation of one sentence | OpenTelemetry in V2 |
| 18 | **Plugins** | A collectible `AssemblyLoadContext` + a permissions manifest + a versioned SDK (`Optimus.Sdk`) | Hot load and unload, isolated dependencies | C# scripts (Roslyn) or Lua for “light” plugins in V2 |
| 19 | **Tests** | xUnit + FluentAssertions + NetArchTest (dependency rules) + Verify (sequence snapshots); Testcontainers not required | see `docs/13` | — |
| 20 | **Packaging / updates** | **Velopack** (installer + signed delta updates); Inno Setup as an option for a classic installer | Simple, modern, auto-update without MSIX or a store | MSIX rejected (constraints on global hooks and paths) |
| 21 | **CI** | GitHub Actions: build, tests, catalogue lint, self-contained publish, Velopack release | | |

---

## 5.3 Technical watch points on these choices

| Subject | Risk | Mitigation |
|---|---|---|
| **Anti-cheat** | Star Citizen ships anti-cheat protection. Synthetic input *may* be filtered or treated as suspicious. VoiceAttack works today, which is a strong hint — **not a guarantee**. | **To be settled first, before any other line of code** (spike no. 1, see `docs/13`). Plan B: the Interception driver; plan C: an emulated HID device. Never implement continuous automation (aiming, farming): one sentence means one deliberate action. |
| **Elevation** | If SC runs as administrator, a non-elevated app cannot send it input (UIPI). | Detect the case and say so plainly; offer an opt-in elevated mode. |
| **Exclusive full screen** | Global hotkeys and the overlay behave differently. | Recommend borderless full screen; test both. |
| **Whisper `small` on a weak CPU** | Transcription over 1 s → latency outside target. | Automatic model selection from CPU/GPU at first launch + a built-in benchmark; `tiny`/`base` as fallbacks. |
| **The microphone captured by the game and by Discord** | Device conflicts. | WASAPI in shared mode (never exclusive); explicit device selection. |
| **The TTS voice heard by the microphone** (a loop) | Self-triggering. | Ducking + suppressing the TTS window in the VAD + controlled barge-in. |
| **Large models in the installer** | An installer of several hundred MB. | A light installer + **downloading the model at first launch**, with a choice of size. |
