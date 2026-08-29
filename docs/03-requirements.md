# PHASE 2 — Optimus requirements analysis

Every requirement carries a stable identifier, a priority (**M** = MVP v0.1, **1** = V1,
**2** = V2) and, where it makes sense, a measurable acceptance criterion.

---

## 3.1 Functional requirements (RF)

### Voice chain

| ID | Requirement | Prio | Acceptance criterion |
|---|---|---|---|
| RF-V01 | Capture the microphone continuously with VAD (voice activity detection) | M | Segments a 2 s sentence with ≤ 300 ms of trailing silence |
| RF-V02 | Transcribe speech to text through an **interchangeable** engine (`ISpeechToTextProvider`) | M | Switch provider without restarting the app |
| RF-V03 | **Push-to-talk** trigger (configurable, global key) | M | Works while SC has full-screen focus |
| RF-V04 | **Wake word** trigger (“Optimus”) | M (degraded) / 1 (native) | v0.1: prefix detected in the transcript. V1: a dedicated detector under 5% false positives per hour |
| RF-V05 | “Always listening” mode that can be switched on and off | 1 | |
| RF-V06 | Speech synthesis through an **interchangeable** engine (`ITextToSpeechProvider`) | M | Windows SAPI/OneCore works with nothing to install |
| RF-V07 | Cut or duck the TTS if the user speaks again (barge-in) | 1 | |
| RF-V08 | Audio output on a chosen device (including a virtual cable for streaming) | 1 | |

### Understanding and commands

| ID | Requirement | Prio |
|---|---|---|
| RF-C01 | Resolve a sentence to an **intent** through a deterministic local matcher (exact + normalised + fuzzy) | M |
| RF-C02 | A command has **N spoken phrases** (aliases), editable by the user | M |
| RF-C03 | Escalate to an **optional** LLM only when the local matcher fails or hesitates | 1 |
| RF-C04 | The LLM returns **only a structured intent** taken from a whitelist, never an action | 1 |
| RF-C05 | Disambiguation by follow-up question (“Which quadrant?”) | 1 |
| RF-C06 | Session memory: resolving references to the previous turn (“and to the front”) | 1 |
| RF-C07 | Extraction of numeric and enumerated parameters (“engine power to three notches”) | 1 |
| RF-C08 | `dialogue` and `lore` command kinds (no action, pure reply) | M |
| RF-C09 | Per-command cooldown, guarding against double triggering | M |
| RF-C10 | Execution prerequisites (`requirements`) evaluated before acting | M |

### Execution

| ID | Requirement | Prio |
|---|---|---|
| RF-E01 | Low-level keyboard injection (scancodes) compatible with a DirectInput/Raw Input game | M |
| RF-E02 | Mouse injection (buttons, wheel; movement in V1) | M |
| RF-E03 | `tap` / `hold` / `double_tap` / `press` / `release` modes with configurable durations | M |
| RF-E04 | **Sequences** of steps with delays, repetitions and combinations | M |
| RF-E05 | **Macros** = named, reusable sequences, editable by the user | 1 |
| RF-E06 | Conditions inside macros (`if` / `else` / `wait` / `repeat`) | 1 |
| RF-E07 | Execute **only while Star Citizen is in the foreground** (option can be turned off) | M |
| RF-E08 | **Simulation mode**: no key is sent, everything is logged | M |
| RF-E09 | Global **kill switch** (shortcut) suspending all execution instantly | M |
| RF-E10 | Test a command from the UI, without speaking | M |

### Keybinds

| ID | Requirement | Prio |
|---|---|---|
| RF-K01 | Import SC's `defaultProfile.xml` (defaults per version) | M |
| RF-K02 | Import a user export `layout_*.xml` (deltas) and merge it | M |
| RF-K03 | Edit a binding by **key capture** (modifiers and mouse included) | M |
| RF-K04 | Detect and display **conflicts** | M |
| RF-K05 | Several **binding profiles** (Default, Fighter, Mining, Cargo, Racing, FPS) | 1 |
| RF-K06 | Export / import / backup / restore / reset | 1 |
| RF-K07 | Detect a rebind made in game (file watcher) and offer to resynchronise | 2 |
| RF-K08 | Export a “flight sheet” (PDF/HTML cheat sheet) of the favourites | 1 |

### Copilots, character, profiles

| ID | Requirement | Prio |
|---|---|---|
| RF-P01 | Full copilot CRUD (create, edit, duplicate, delete) | M (partial: one copilot shipped + editing) / 1 (full CRUD) |
| RF-P02 | Parametric character (traits 0–100, style, vocabulary, forbidden phrases) | M |
| RF-P03 | Reply variants weighted by the character (never a single reply) | M |
| RF-P04 | Behaviour rules (`when` → `behavior`) | 1 |
| RF-P05 | Voice and language per copilot | M |
| RF-P06 | Abilities enabled or disabled per copilot (`enabled_commands`, permissions) | 1 |
| RF-P07 | **User** profiles (language, PTT, wake word, preferred copilot, SC profile) | 1 |
| RF-P08 | `.optcopilot` packs, importable and exportable (signed manifest) | 2 |
| RF-P09 | Several copilots active at once (cross-talk) | 2 |

### Interface, observability, integrations

| ID | Requirement | Prio |
|---|---|---|
| RF-U01 | Status dashboard (mic, STT, TTS, LLM, SC detected, copilot, latency, last command) | M |
| RF-U02 | Command browser (search, filters, categories, favourites) | M |
| RF-U03 | Keybind Manager | M |
| RF-U04 | Timestamped history (phrase, intent, action, result, latency, error) | M |
| RF-U05 | **Debug** mode showing each pipeline stage with its scores | M |
| RF-U06 | Multi-level file logs with rotation | M |
| RF-U07 | Command Builder (create a command without writing code) | 1 |
| RF-U08 | AI-assisted command generation (proposal → human validation) | 2 |
| RF-U09 | Analytics (most used commands, failures, unrecognised phrases, average latency) | 1 |
| RF-U10 | Minimise to the notification area, optional start with Windows | M |
| RF-U11 | Authenticated local HTTP API (loopback) | 1 |
| RF-U12 | Discord bot (status, catalogue, history; execution subject to permissions) | 1 |
| RF-U13 | Loadable plugin system | 1 (API from M) |
| RF-U14 | In-game overlay / HUD | 2 |
| RF-U15 | Game telemetry (a pluggable `IGameStateProvider`) | 2 |

### Error handling (RF-ERR)

| ID | Situation | Mandatory behaviour |
|---|---|---|
| RF-ERR1 | Sentence not understood | A spoken reply + an “unknown phrase” entry in the analytics |
| RF-ERR2 | Known command, missing binding | “The command exists but no shortcut is configured.” + a direct link to the Keybind Manager |
| RF-ERR3 | SC not detected / not in the foreground | “I understood, but Star Citizen is not in the foreground.” |
| RF-ERR4 | Sequence interrupted (kill switch, failed step) | An announcement + a guaranteed release of **every** held key |
| RF-ERR5 | Provider unavailable (STT/TTS/LLM) | Automatic degradation to a fallback + a visual notification |
| RF-ERR6 | Cooldown active | Silence or a short acknowledgement, never a double execution |
| — | **Cross-cutting rule** | **No silent failure.** Every error path produces user feedback *and* a trace. |

---

## 3.2 Non-functional requirements (RNF)

| ID | Requirement | Measurable target |
|---|---|---|
| RNF-01 | Latency of a simple command, from end of speech to keypress | **≤ 700 ms p50, ≤ 1,200 ms p95** (LLM excluded) |
| RNF-02 | Perceived latency with a spoken acknowledgement | First TTS sound within 900 ms |
| RNF-03 | Idle memory footprint (listening, UI open) | ≤ 400 MB RSS, small STT model included |
| RNF-04 | Idle CPU footprint | ≤ 3% of one modern core outside transcription |
| RNF-05 | FPS impact on Star Citizen | **≤ 2%** at 1440p (measured with and without Optimus) |
| RNF-06 | Cold start until listening | ≤ 4 s (loading the STT model in the background is allowed) |
| RNF-07 | **100% offline** operation for every deterministic command | Test: network adapter disabled → the whole pipeline still works |
| RNF-08 | Stability | ≥ 8 h of continuous session with no memory leak and no latency drift |
| RNF-09 | Recovery from error | A sidecar crash (STT/TTS) does not kill the app; automatic restart |
| RNF-10 | No user data written into `Program Files` | Verified on a machine-wide install with a standard account |
| RNF-11 | Clean install and uninstall, leaving nothing outside `%APPDATA%` (with a purge option) | |
| RNF-12 | Internationalisation: no hard-coded user text in the code | |

---

## 3.3 Technical requirements (RT)

| ID | Requirement |
|---|---|
| RT-01 | Windows 10 21H2+ / Windows 11, x64. No dependency on a separately installed runtime (self-contained). |
| RT-02 | **No hard-coded key binding in the code.** Every key comes from a `BindingProfile` loaded at runtime. The ban is enforced by an architecture test (no key constant outside the `Input` layer). |
| RT-03 | Strict separation **AI → structured intent → validation → binding → input**. The LLM has no direct access to the lower layers (verified by an architecture test on project dependencies). |
| RT-04 | STT/TTS/LLM providers behind interfaces; discovered through configuration, not through a `switch` in the core. |
| RT-05 | User configuration in **text files** (JSON/YAML) suitable for version control + SQLite for runtime data (history, stats, cache). |
| RT-06 | Every persisted piece of data carries a schema number and a tested **migration**. |
| RT-07 | The local API binds to `127.0.0.1` only, never `0.0.0.0`. |
| RT-08 | Structured logging correlated end to end by `trace_id` (one sentence = one trace). |
| RT-09 | The core (`Optimus.Core`) references **neither** the UI **nor** Windows: testable in a console and in CI. |
| RT-10 | Plugins isolated (dedicated load context, declared permissions, unloadable). |

---

## 3.4 Safety requirements (RS)

| ID | Requirement | Rationale |
|---|---|---|
| RS-01 | **Isolation per machine**: a command only acts on the PC that received it locally | §81 of the brief |
| RS-02 | Discord and any remote layer may only carry an **intent** to a *paired* instance, never a keystroke | §82–83 |
| RS-03 | Discord ↔ instance pairing through a **locally generated one-time code**, revocable | |
| RS-04 | Granular permissions per Discord user or role: `view_status`, `view_commands`, `execute_commands`, `modify_config` — **`execute_commands` defaults to `false`** | |
| RS-05 | Intent whitelist: the LLM can never name a non-existent action or a system command | §75 |
| RS-06 | Mandatory spoken confirmation for actions marked `dangerous` (eject, self destruct, cargo jettison) | |
| RS-07 | A kill switch that behaves like a hardware one: a global shortcut (default `F12`… **to be confirmed**, `F12` is taken in SC — propose `Ctrl+Alt+Pause`) cutting all injection | |
| RS-08 | Local API: a bearer token generated at first launch, stored encrypted (DPAPI), rotatable | |
| RS-09 | No secret in clear text in configuration files, and none ever sent to a client | The Jean-Bot lesson |
| RS-10 | Plugins: permissions declared in the manifest, validated at install, signature checked for packs | |
| RS-11 | No outbound telemetry by default; explicit opt-in | |
| RS-12 | Local rate limiting on the execution of remotely originated intents | Anti-abuse and anti-loop |

---

## 3.5 UX requirements (RUX)

| ID | Requirement |
|---|---|
| RUX-01 | The cockpit/avionics aesthetic **serves legibility**: no text over an animated background, AA contrast minimum. |
| RUX-02 | The state of the system is understandable **at a glance** (status pills, a constant colour code). |
| RUX-03 | Every displayed error offers the corrective action (a “Configure the shortcut” or “Pick a microphone” button). |
| RUX-04 | A guided first run: microphone → language → import SC keybinds → test a command in simulation → enable. **≤ 5 steps, ≤ 3 min.** |
| RUX-05 | Debug mode is one click away from the dashboard (it is the number-one support tool). |
| RUX-06 | Every destructive action (deleting a copilot, resetting the binds) is confirmed and reversible (bin or automatic backup). |
| RUX-07 | The app must stay usable **with the mouse alone**, windowed, while SC runs in borderless full screen. |
| RUX-08 | Short spoken feedback by default (< 2 s); verbosity is a character slider, not a fate. |
