# PHASE 9 — Interface

## 10.1 Design principles

- **Avionics that read, not avionics that decorate.** A very dark background (`#05070f`), an
  accent colour per copilot, condensed typefaces for headings (Chakra Petch / Rajdhani) and a
  neutral grotesque for body text (Inter). Effects (scanlines, glow) capped at 8% opacity and
  **switchable off**.
- **One colour code, everywhere**: green = nominal, amber = degraded, red = error, cyan =
  information/action, yellow = simulation active.
- **Every displayed error carries its own fix button.**
- **The status bar is permanent**: microphone, STT, TTS, LLM, game, simulation, kill switch —
  visible from any screen.
- **Controlled density**: Jean-Bot's page shows 600 commands; ours must show as many without
  becoming unreadable (compact cards + list mode + HUD mode).

---

## 10.2 Navigation

```
┌────────────────────────────────────────────────────────────────────────────────┐
│  OPTIMUS  ▸ Command Center           ● SYSTEM ONLINE   ⏻ KILL SWITCH  [ – □ × ]│
├──────────────┬─────────────────────────────────────────────────────────────────┤
│ ▸ DASHBOARD  │                                                                 │
│   COMMANDS   │                                                                 │
│   KEYBINDS   │                        content area                             │
│   COPILOTS   │                                                                 │
│   PERSONALITY│                                                                 │
│   VOICE      │                                                                 │
│   AI         │                                                                 │
│   PROFILES   │                                                                 │
│   DISCORD    │                                                                 │
│   PLUGINS    │                                                                 │
│   LOGS       │                                                                 │
│   SETTINGS   │                                                                 │
├──────────────┴─────────────────────────────────────────────────────────────────┤
│ MIC ●  STT ●  TTS ●  LLM ○  SC ●  SIM ○         last: "open the doors"  585ms  │
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## 10.3 Screen by screen

### 1. DASHBOARD — *the state of the system in two seconds*

```
┌ SYSTEM ────────────┐┌ COPILOT ────────────┐┌ STAR CITIZEN ──────┐
│ ● ONLINE           ││   ┌───┐             ││ ● DETECTED  4.9.1  │
│ uptime 01:42:07    ││   │ ⬡ │  OPTIMUS    ││ ○ foreground: NO   │
│ mode: LOCAL        ││   └───┘  military   ││ profile: Default   │
│ simulation: OFF    ││ voice Denise · fr-FR││ 312 binds · 4 free │
└────────────────────┘└─────────────────────┘└────────────────────┘
┌ VOICE ─────────────────────────┐┌ PERFORMANCE ───────────────────┐
│ mic    Realtek Array  ▇▇▇▅▂    ││ average latency  612 ms        │
│ mode   push-to-talk (F10)      ││ p95              940 ms        │
│ STT    whisper-small ● ready   ││ commands/hour     47           │
│ TTS    OneCore ● ready         ││ success rate     96.2%         │
│ LLM    ○ disabled              ││ failures 24 h      3           │
└────────────────────────────────┘└────────────────────────────────┘
┌ LAST COMMAND ──────────────────────────────────────────────────────┐
│ 21:42:15  “optimus open the doors”                                 │
│ → ship.doors.toggle (1.00)  → L  → SUCCESS  585 ms   [ see trace ] │
└────────────────────────────────────────────────────────────────────┘
┌ ACTIVITY ──────────────────────────────────────────────────────────┐
│ 21:42:15 ✓ open the doors        21:41:02 ✓ combat mode            │
│ 21:40:47 ✗ “scanning on”  → no shortcut            [ configure ]   │
└────────────────────────────────────────────────────────────────────┘
```

Every pill is **clickable** and leads to the matching settings screen. A yellow “SIMULATION
ACTIVE” banner crosses the screen while simulation mode is on — impossible to forget.

### 2. COMMANDS — *the catalogue (a direct answer to `commandes.php`)*

```
COMMAND DATABASE                          312 commands · 47 favourites
[ 🔍 search…                        ]  [ ★ favourites ] [ ⊞ grid | ☰ list | ▤ HUD ]
[All][Ship][Combat][Navigation][Quantum][Mining][Salvage][Scanning][FPS][Social][Lore]…

┌──────────────────────────────┐ ┌──────────────────────────────┐
│ Open / close the doors     ★ │ │ Combat mode               ★  │
│ ship · action                │ │ combat · macro (5 steps)     │
│ “open the doors” +5          │ │ “combat mode” +2             │
│ ⌨  L                         │ │ ⌨  sequence                  │
│ used 42×      ⌀ 590 ms       │ │ used 12×      ⌀ 1,240 ms     │
│ [ ▶ test ] [ ✎ edit ]        │ │ [ ▶ test ] [ ✎ edit ]        │
└──────────────────────────────┘ └──────────────────────────────┘
```

Deliberately taken from Jean-Bot: search with a **strict word mode** (a query ending in a space),
category filters, favourites, a compact HUD mode, a “flight sheet” export.
Added: search **by spoken phrase and by key**, real usage statistics, one-click testing, and a
“no shortcut” badge impossible to miss.

### 3. KEYBINDS — *the strategic screen*

```
KEYBIND MANAGER          profile: [ Default ▾ ]  [ import from SC ] [ export ] [ reset ]
source: defaults-4.9.json ⊕ layout_Keybinds_1_exported.xml (23/08 21:10)   [ resync ]
[ 🔍 action or key… ]   ☐ unassigned only   ☐ conflicts only

ACTION                              ACTIONMAP              KEY            STATE
Open/close the doors                spaceship_general      L              default
Ship power                          spaceship_general      R              default
Quantum drive                       spaceship_movement     B              default
Next target — hostile               spaceship_targeting    ALT + T        modified
Eject                               spaceship_general      RALT + Y ⚠     double tap
Scan mode                           spaceship_scanning     —  unassigned  ⚠
Self destruct                       spaceship_general      BACKSPACE (1.5 s) dangerous 🔒

┌ CONFLICT ─────────────────────────────────────────────────────────┐
│ ⚠ “L” is assigned to: Open the doors  AND  Landing gear (LN mode) │
│   Different actionmaps → fine in SC, but Optimus needs to know    │
│   the active mode. [ define the mode rule ] [ ignore ]            │
└───────────────────────────────────────────────────────────────────┘

┌ CAPTURE ──────────────────────────────────────────────────────────┐
│ Press a key…                       detected: CTRL + ALT + F       │
│ mode: ( ) tap  (•) hold 300 ms  ( ) double tap                    │
│ [ Save ]  [ Clear the binding ]  [ Cancel ]                       │
└───────────────────────────────────────────────────────────────────┘
```

### 4. COPILOTS

A gallery of cards (avatar, colour, language, voice, number of active commands, an “active”
badge). Actions: **Activate · Edit · Duplicate · Export (.optcopilot) · Delete**.
A tabbed editor: *Identity · Voice · Character · Abilities · Allowed commands · AI*.

### 5. PERSONALITY — *the screen that sells the product*

```
CHARACTER — Optimus                             [ ⟲ reset ] [ 🔊 preview ]
Formality    ▓▓▓▓▓▓▓▓░░ 80     Humour      ▓▓▓▓░░░░░░ 40
Verbosity    ▓▓▓░░░░░░░ 30     Sarcasm     ▓▓░░░░░░░░ 25
Calm         ▓▓▓▓▓▓▓▓▓░ 90     Warmth      ▓▓▓▓░░░░░░ 45
Confidence   ▓▓▓▓▓▓▓▓▓░ 85     Aggression  ▓░░░░░░░░░ 10
Style  ☑ military ☑ sci-fi ☑ immersive ☑ technical
Address [ commander, captain ]   Forbidden [ lol, lmao, as an AI ]

┌ LIVE PREVIEW ─────────────────────────────────────────────────────┐
│ quantum success  “Trajectory computed. Hold on, commander.”        │
│                                                    [ ▶ listen ]   │
│ failure          “Negative. No shortcut configured.”              │
│ unknown          “That instruction is not in my protocols.”       │
└───────────────────────────────────────────────────────────────────┘
RULES  combat_active → short replies   ·   failure → explain the cause      [ + ]
```

The preview recomputes **on every slider movement**: that is what makes the traits tangible.

### 6. VOICE — devices, an input meter, the listening mode, PTT, VAD thresholds, STT/TTS
providers, model management (download, size, benchmark), a “speak now” test showing the
transcript and the time taken.

### 7. AI — enabling the LLM (**off by default, with an explanatory box**: what is sent, to whom,
at what cost), provider, fast and reasoning models, escalation threshold, monthly budget, a token
counter, a “handle everything locally” button.

### 8. PROFILES — user profiles (language, wake word, PTT, preferred copilot, associated binding
profile), import/export, automatic backups and restore.

### 9. DISCORD — bot status, mode (local/relay), pairing by one-time code, a table of linked users
with their permissions (checkboxes), which events get notified, and a log of the commands received
from Discord.

### 10. PLUGINS — the list, enabling, the permissions requested (shown explicitly), SDK version,
the commands contributed, the install folder, hot reload.

### 11. LOGS — a real-time view filterable by level, component and trace, a **PIPELINE TRACE** mode
(see `docs/09`), and an incident export (JSON + logs + anonymised config) for support.

### 12. SETTINGS — start with Windows, minimise to the notification area, global shortcuts,
**simulation mode**, whether game focus is required, theme, interface language, the SC
installation path, updates, reset.

---

## 10.4 First run (a wizard, ≤ 5 steps)

```
1. Welcome         →  interface language · pilot name
2. Microphone      →  device · level test · PTT (F10 by default)
3. Star Citizen    →  automatic path detection · keybind import
                      “312 actions imported, 4 without a binding”
4. Copilot         →  Optimus (voice, spoken preview, 3 quick sliders)
5. Try it          →  SIMULATION MODE FORCED: say “Optimus, open the doors”
                      the full trace is shown, then [ enable for real ]
```

Step 5 running in simulation is deliberate: the user sees the system work **before** anything
touches their keyboard. That is the best possible argument for trust in an application asking for
the right to press keys.

---

## 10.5 Notification area and compact mode

- Notification icon: state shown by colour, with a context menu (enable/disable the microphone,
  simulation, kill switch, open, quit).
- **Compact mode**: a small window (~360 × 120), always on top, showing the listening state, the
  last command and the kill switch button — for a second screen or a cockpit.
- **HUD/overlay mode** (V2): a transparent in-game overlay.
