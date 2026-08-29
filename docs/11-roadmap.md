# PHASES 10 · 11 · 12 — MVP, V1, V2

## 11.1 MVP v0.1 — “Optimus obeys”

**A single goal**: that the sentence “Optimus, open the doors” actually opens the doors —
reliably, configurably and safely. Everything else is out of scope.

### Included

| # | Feature | Detail | Ref. |
|---|---|---|---|
| 1 | Star Citizen detection | process + foreground + install path + version | RF-E07 |
| 2 | Microphone capture + VAD | WASAPI, Silero VAD, 3 s pre-roll, device selection | RF-V01 |
| 3 | Local STT | Whisper.net `small` (downloaded at first launch), fr/en | RF-V02 |
| 4 | Trigger | push-to-talk (default) + a wake word by transcript prefix | RF-V03/04 |
| 5 | Local intent matcher | normalisation + exact + fuzzy, configurable thresholds, **no LLM** | RF-C01 |
| 6 | Command catalogue | **~60** carefully chosen Star Citizen commands (see §11.2), JSON validated by schema | RF-C02 |
| 7 | SC keybind import | `defaultProfile.xml` + `layout_*.xml`, merge, import report | RF-K01/02 |
| 8 | Keybind Manager | list, search, key capture, conflicts, unassigned | RF-K03/04 |
| 9 | Execution engine | scancode `SendInput`, tap/hold/double-tap, sequences + delays, mouse | RF-E01→04 |
| 10 | Execution guard | global kill switch, game focus, cooldown, confirmation of `dangerous` | RS-06/07 |
| 11 | **Simulation mode** | no key sent, a full trace | RF-E08 |
| 12 | TTS | Windows OneCore (nothing to install) + reply variants | RF-V06 |
| 13 | Character | 8 traits, weighted selection, anti-repetition, lexicon | RF-P02/03 |
| 14 | The Optimus copilot | one copilot shipped, editable (identity, voice, traits) | RF-P01 (partial) |
| 15 | UI | Dashboard · Commands · Keybinds · Voice · Personality · Logs · Settings | RF-U01→06 |
| 16 | History + debug trace | SQLite, the PIPELINE TRACE screen, incident export | RF-U04/05 |
| 17 | File logs | Serilog, rotation, levels | RF-U06 |
| 18 | Tray + start with Windows | + compact mode | RF-U10 |
| 19 | Installer | Velopack, auto-update, data in `%APPDATA%` | RNF-10/11 |

### Explicitly **excluded** from the MVP

Discord · loadable plugins (the **API** exists, no plugin ships) · LLM · conditional macros (`if`)
· Command Builder · multi-copilot · multiple binding profiles · local API · gamepad/HOTAS ·
overlay · telemetry · store · `.optcopilot` packs · advanced analytics · languages beyond fr/en.

### Definition of done for the MVP

- [ ] 20 commands executed end to end with **≥ 95% success** across 3 different voices.
- [ ] Voice → key latency **p50 ≤ 700 ms**, **p95 ≤ 1,200 ms**, measured automatically.
- [ ] Works with the **network adapter disabled**, after the model's initial download.
- [ ] FPS impact measured at **≤ 2%** over a 30-minute session.
- [ ] No key constant outside the `Input` layer (the architecture test is green).
- [ ] The kill switch cuts a running sequence **and releases every key** (tested).
- [ ] An 8-hour session with no memory leak and no latency drift.
- [ ] Clean install, update and uninstall on a fresh VM.
- [ ] The first-run wizard completed in under 3 minutes by an uninitiated tester.

### Indicative breakdown (full time; ×2.5 part time)

| Sprint | Content | Duration |
|---|---|---|
| **S0** | **Risk spikes**: ① scancode injection accepted by SC ② Whisper latency on the target machine ③ global hotkeys in full screen ④ parsing `defaultProfile.xml` | 1 wk |
| S1 | Solution skeleton, domain, JSON loading + schemas, `SimulatedInputEngine`, tests | 1 wk |
| S2 | SC importer + `BindingProfile` + `BindingResolver` + a real `SequenceRunner` | 1.5 wk |
| S3 | Audio + VAD + Whisper + normalisation + matcher + a 60-command catalogue | 2 wk |
| S4 | Character + TTS + reply composition | 1 wk |
| S5 | UI: Dashboard, Commands, Keybinds, Voice, Personality, Logs | 2.5 wk |
| S6 | Execution guard, kill switch, history, debug trace, first-run wizard | 1 wk |
| S7 | Packaging, updates, hardening, end-to-end tests, documentation | 1 wk |
| | **Total** | **≈ 11 weeks** |

*Sprint S0 is not negotiable: if injection does not get through to Star Citizen, the whole
execution architecture changes. We find that out in week 1, not in week 9.*

---

## 11.2 The ~60 MVP commands

The selection is guided by the distribution observed at Jean-Bot (Navigation 33%, Combat 30%,
Mining 29%) **and** by the principle “a command is useful when you have no finger free”:

| Category | ~n | Examples |
|---|---|---|
| Ship & systems | 12 | doors, power on/off, engines, shields on/off, weapons on/off, lights, gear, VTOL, cockpit lock |
| Navigation & quantum | 10 | landing mode, autoland, quantum, cruise control, decoupled, space brake, speed limiter |
| Combat | 12 | weapon groups, missiles, countermeasures (decoy/chaff), gimbal, targeting mode, target nearest hostile/friendly/attacker, pin |
| Power & shields | 8 | power presets, ± engines/weapons/shields, shield quadrant (front/rear/left/right), reset |
| Scanning & mining | 8 | scan, ping, mining mode, laser, mining power, throttle, head cycling |
| Camera & comfort | 5 | free look, mobiGlas, starmap, contacts, subtitles |
| Optimus system | 5 | “system report”, “simulation mode”, “be quiet”, “repeat”, “cancel” |

**Three demonstration macros** (combat mode, mining mode, quantum preparation) to prove the
sequence language without opening the conditions worksite.

---

## 11.2 bis — The work plan, settled on 2026-08-26

What the foundation can already do is described in the decision log (D1 to D44). What follows is
the queue agreed with the maintainer, in order.

| # | Worksite | Why now | State |
|---|---|---|---|
| **1** | **Aliases for what was not understood** | A screen listing what Optimus heard without acting on, letting you attach the phrasing actually used. The calibration work — accents (D39), thresholds (D29), phrasings — becomes cumulative instead of depending on a development pass each time. It is the only feature that improves the others. | **done 2026-08-26** |
| **2** | **Conversational layer (LLM)** | “What do you make of this ship?” got a catalogue line. The model returns **only an intent validated against the whitelist, never a key** (§73, §75) and stays **optional** (§84): off by default, everything keeps working offline without it. | **done 2026-08-26** — see the reservation below |
| **3** | **Conditions inside macros** | `if`, `else` and `repeat`. Decided at expansion time (D51), over five subjects limited to what Optimus actually knows (D53) — no invented telemetry. First shipped use: the ATC request in the takeoff and landing sequences, which used to make the whole macro fail when the key was missing (D54). | **done 2026-08-26** |
| **4** | **Local neural voice (Piper)** | Measured: 0.6 s to load at startup, 377 to 455 ms per reply on `medium`, 214 ms on `low`, against 7 to 15 ms for Windows voices. Acceptable because **the action never depends on the TTS** — the delay lands on the comment. Stays local, doubled by the Windows voices (D55 to D57). | **done 2026-08-27** |
| **5** | **Key profiles** | Create, duplicate, rename, delete; switched hot from the screen **or by voice** (“mining profile”). A profile is a named set of assignments, not a copy of the game's defaults (D63 to D65). | **done 2026-08-27** |
| **6** | **Local API** | Eight routes and an event stream on `127.0.0.1`, an encrypted token, three scopes, an execution cap. Windows itself forbids network listening to Optimus, which lacks the rights for it (D66 to D68). | **done 2026-08-27** |
| **7** | **Discord** | A local bot, pairing, notifications. It carries **intents**, never keystrokes: no server touches anybody's keyboard. | to do |
| **8** | **Plugins** | Hot loading, permissions, two reference plugins. | to do |
| **9** | **Multi-copilot** | Duplicate, rename, delete, switch hot from the screen **or by voice** (“switch to Virgil”). The wake word, the voice and the character change together (D70, D71). | **done 2026-08-28** |
| **10** | **HOTAS and peripherals** | Stick, rudder pedals, Stream Deck. It changes how you trigger, not what gets triggered. | to do |
| **11** | **In-game overlay** | A transparent HUD: state, last command, confirmations. **A real uncertainty**: the interaction with anti-cheat has to be settled before committing anything to it. | to do |

| **12** | **Installer** | A single `.exe`, per-user installation without UAC, with a component selection page: Piper and its voices are **optional and downloaded** (D58 to D61). It is also what replaces the USB stick between two machines. | **done 2026-08-27** — see the reservation below |

**Reservation on worksite 12.** The installer **does not solve R16**, it concentrates it. The
executable is not signed: SmartScreen will warn everyone who downloads it, and Smart App Control
will refuse it, exactly as it refused the bare binaries. The gain is real but indirect — there is
now **one** file to sign instead of twenty-four, which makes the problem solvable with a single
certificate. **Until that signature exists, public distribution stays blocked**, and that is the
next lock, not a formality.

**Worksite 13 — free speech (Whisper): done on 2026-08-28.** The free-speech layer is built, and
the reservation on worksite 2 is lifted. Three positions the pilot chooses between, with their
measured cost shown on screen: off, when the fast engine hesitates, or on everything. Whisper
never opens a microphone — it transcribes the audio the fast engine hands it (D74 to D76).

**What remains to be proven**: the whole chain, microphone → doubt → transcription → escalation,
on a real voice. The transcriber is verified against the real binary, the installer puts the
engine in place in sixty seconds, and the assembly shows up in the log — but the
speaker-to-microphone loop test is inconclusive on this laptop, whose echo cancellation filters
the playback. That measurement needs someone speaking into the microphone.

*The historical reservation on worksite 2, kept for the record.* The conversational layer was in
place but **free conversation by voice was not reachable**. The grammar is closed (D28, D30): an
utterance outside the catalogue never reaches Optimus as text — the engine returns the closest
known phrasing with a low confidence, never what was actually said. The model therefore served
two inputs: the “try a phrase” field, and the spoken utterances that came back as `Unknown`.
Talking freely to your copilot needs the free-speech layer (Whisper, D28 phase B), which was not
yet built.

**Reservation on worksite 7.** Postponed at the maintainer's request on 2026-08-28, for a reason
that is not technical: a Discord bot requires an **application created on their own Discord
account**, which nobody can create or own on their behalf. Without a token the gateway connection
would stay unverified — and a bot that disconnects silently is worse than no bot.

Nothing is lost: the design holds in docs/12.2, and **the local API is already the foundation it
needs** (D66 to D68). The bot will not have to reinvent the guard, the scopes or the execution
cap: it will plug into them.

Two points are settled for the day this is picked up again: **Discord.Net** as the library (D69),
and the bot token **encrypted with DPAPI** like the API tokens (D68), never in `data/` and never
in the repository.

**Reservation on worksite 10.** Postponed on 2026-08-28, for want of hardware within reach. The
whole feature hangs on a single property: Optimus must read the stick **while Star Citizen has
focus**. An API that only delivers input to the foreground window would make the worksite entirely
useless — and that cannot be measured on paper, only with a device plugged in.

The analysis is done and recorded (D72): three candidate APIs, their limits, and the constraint
inherited from D36 — stay proportionate, since one global hook too many already got Optimus
blocked by Smart App Control.

The **Stream Deck** is waiting for nothing: it already goes through the local API (D66). That is
the part of the worksite that has, in fact, already shipped.

**Reservation on worksite 11.** Removed from the queue at the maintainer's request on 2026-08-28,
and kept as an **optional feature with no deadline**. The calculation is not technical but
asymmetric: the gain is comfort — not taking your eyes off the game — while the possible loss is a
banned account, on which ships were bought with real money.

For the day the question reopens, a distinction the decision must keep in mind: “overlay” covers
**two techniques with nothing in common**.

- **Injection** into the game's presentation chain, as Steam and Discord do. That is what
  anti-cheat watches, and that is what justifies the caution. **Rejected.**
- A **separate, transparent, always-on-top window** that never enters the game's process —
  indistinguishable from a browser placed on top. It only works in borderless windowed mode, never
  in exclusive full screen: that is its one real limit. **Still open.**

In the meantime Optimus's state is readable in its own window, and a client hooked to
`/ws/events` (D66) can already show it wherever it likes — a second screen, a tablet, a Stream
Deck.

**The last worksite, opened on 2026-08-29: code signing (R16).**
The detail now lives in [`15-code-signing.md`](15-code-signing.md): the conditions taken from the
foundation, the build pipeline, the signing policy and what is left to do. **Three of the
conditions were missing, and they all hung on a single decision by the maintainer** — making the
repository public under an open source licence.

The route chosen on 2026-08-27: the **SignPath Foundation**, which signs open source projects for
free. Commercial certificates cost €100 to €600 a year, which is out of budget — and a
certificate you fail to renew leaves a binary signed by an expired key, which is worse than
nothing.

In return, SignPath requires a public repository, an OSI licence, and a build from source on a
public CI. **It is therefore as much a distribution decision as a technical one**: the
“private / open source / product” question from 13.5 is settled by the same gesture.

What will remain to be done, in this order:

1. Publish the repository and choose the licence.
2. Set up the build in GitHub Actions, reproducible from source.
3. Submit the application to the SignPath Foundation.
4. Wire signing into `tools/build-installer.ps1`, for the executable **and** the installer.

Until then Optimus installs and runs: the SmartScreen warning is cleared through “More info → Run
anyway”, once per version.


---

## 11.3 V1 — “Optimus becomes a platform” (+ 3 to 4 months)

| Block | Content |
|---|---|
| **Multi-copilot** | Full CRUD, Synthia and Virgil shipped, duplication, `.optcopilot` import/export (unsigned) |
| **Advanced character** | Behaviour rules, system events, idle banter, live spoken preview |
| **Voice** | Piper (local neural TTS, FR/EN), TTS cache, optional cloud providers (ElevenLabs, Azure, OpenAI) |
| **Native wake word** | openWakeWord in ONNX, an “Optimus” model + custom words |
| **Optional LLM** | Ollama / OpenAI-compatible / Anthropic, constrained JSON output, whitelist, budget and token counter, disambiguation and conversation |
| **Context** | `ConversationContext`, slots, anaphora, a declarative `GameContext` |
| **Macros & conditions** | `if` / `repeat` / `wait`, a visual editor, a macro library |
| **Command Builder** | Creation without code, ambiguity validation, testing in simulation |
| **Binding profiles** | Default / Fighter / Mining / Cargo / Racing / FPS, hot switching, import/export/backup |
| **Local API** | REST + WebSocket, token, OpenAPI documentation |
| **Discord** | Local bot mode, pairing, permissions, status embeds, event notifications |
| **Plugins** | Hot loading, permissions, 2 reference plugins (Spotify, System) |
| **Analytics** | Most used commands, failures, **unrecognised phrases → a one-click alias proposal** |
| **Flight sheet** | PDF/HTML export of the favourites with the real keys |
| **i18n** | FR/EN interface, multilingual copilots |

---

## 11.4 V2 — “Optimus understands the ship” (+ 6 months and beyond)

| Block | Content | Uncertainty |
|---|---|---|
| **Game telemetry** | A real `IGameStateProvider`. **Spike done on 2026-08-26, mostly a negative result**: `Game.log` records <b>neither the flight mode nor the state of the doors</b>. The occurrences of “NAV” in it are false positives coming from `ItemNavigation`; what it does hold is quantum routing (`CSCItemNavigation`, `FinalStop=`) and nearby entities. Without consequence: the game's directed actions (D41) give exact doors and flight mode without observing anything. Still to explore if the need returns — targeted OCR of the HUD, and the current ship, which the log appears to name | ⚠ `Game.log` rejected for ship state |
| **In-game overlay** | A transparent HUD (DirectX / external overlay): state, last command, confirmations | ⚠ interaction with anti-cheat to be settled |
| **Multi-agent** | Several copilots active, cross-talk, routing by role (combat/mining/navigation) | |
| **Copilot store** | Signed packs, catalogue, ratings, updates — **without ever giving the server the slightest power over the keyboard** | |
| **Synchronisation** | End-to-end encrypted backup of the configuration, across PCs | |
| **Mobile / SIMPIT companion** | Consuming the local API, a full-screen PWA + wake lock (an idea taken from Jean-Bot) | |
| **Peripherals** | Gamepad, HOTAS, Stream Deck, MIDI | |
| **VoiceAttack compatibility** | A VAP profile import plugin, never a dependency | |
| **Discord relay** | Multi-machine mode, pairing, outbound connections only | |
| **Advanced AI** | Command generation, coaching, session summaries, long-term memory | |

---

## 11.5 What will never be done (decisions of principle)

| Non-feature | Reason |
|---|---|
| Continuous gameplay automation (aim assist, farming, looping macros) | Falls outside the “copilot” frame and puts the user at risk against the game's rules |
| Controlling a third-party PC from Discord or the cloud | §81–83: execution is always local |
| A mandatory LLM | §84 |
| Hard-coded key bindings | §72 |
| Sending telemetry without explicit consent | RS-11 |
| A mandatory dependency on VoiceAttack | §71 |
