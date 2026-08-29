# PHASE 5 — Data architecture

## 6.1 The principle: two kinds of data

| Kind | Content | Medium | Why |
|---|---|---|---|
| **Configuration** (declarative, versionable, shareable, hand-editable) | copilots, characters, commands, bindings, macros, profiles, settings | **JSON files** (+ YAML import/export) under `%APPDATA%\Optimus\` | Diffable, backupable, shareable, readable without a tool, repairable by hand |
| **Runtime** (bulky, indexed, queried) | history, analytics, embedding cache, Discord pairings, statistics | **SQLite** (`optimus.db`, WAL) | Queries, aggregations, volume |

The rule: **no configuration data exists *only* in the database.** The database can be deleted
without losing the configuration.

---

## 6.2 Logical ERD

```
                       ┌──────────────┐
                       │ UserProfile  │  (Flavien, language, PTT, wake word…)
                       └──────┬───────┘
             preferred_copilot│        │active_binding_profile
                 ┌────────────┘        └────────────┐
                 ▼                                  ▼
          ┌─────────────┐                   ┌────────────────┐
          │  Copilot    │                   │ BindingProfile │  (Default/Fighter/Mining)
          │  id, name   │                   │ id, game, ver. │
          └──┬───┬───┬──┘                   └────────┬───────┘
             │   │   │                               │ 1..n
   1│personality  │voice                             ▼
    ▼            ▼    │enabled_commands[]      ┌──────────────┐
┌───────────┐ ┌──────────────┐    │           │   Binding    │
│Personality│ │ VoiceConfig  │    │           │ action_id    │
│ traits{}  │ │ provider,    │    │           │ actionmap    │
│ style{}   │ │ voice_id,    │    │           │ InputSpec    │
│ rules[]   │ │ speed, pitch │    │           └──────┬───────┘
│ lexicon{} │ └──────────────┘    │                  │ resolved by
└─────┬─────┘                     │                  │
      │ selects                   ▼                  │
      │                    ┌─────────────┐           │
      │                    │  Command    │───────────┘
      │                    │ id, kind    │  action_ref
      │                    │ category    │
      │                    │ voice_      │──────┐ 1..n
      │                    │  phrases[]  │      ▼
      │                    │ requirements│  ┌────────────┐
      │                    │ cooldown    │  │  Action    │ (a sequence step)
      │                    └──────┬──────┘  │ type, mode │
      │                           │         │ target, ms │
      │  1..n                     │ 1..n    └────────────┘
      ▼                           ▼               ▲
┌──────────────┐          ┌──────────────┐        │ reuses
│ResponseSet   │          │    Macro     │────────┘
│ success[]    │          │ steps[], if  │
│ fail[] …     │          └──────────────┘
└──────────────┘

  ┌─────────────┐   ┌──────────────┐   ┌────────────────┐   ┌───────────────┐
  │   Plugin    │   │ DiscordLink  │──►│  Permission    │   │ HistoryEntry  │
  │ manifest,   │   │ pairing_code │   │ scope, allowed │   │ trace_id, …   │
  │ permissions │   │ discord_user │   └────────────────┘   └───────────────┘
  └─────────────┘   └──────────────┘        (SQLite)             (SQLite)
```

---

## 6.3 User file tree

```
%APPDATA%\Optimus\
├── config\
│   ├── settings.json            application settings (audio, providers, hotkeys, simulation)
│   ├── secrets.dat              API keys / Bridge token — DPAPI-encrypted, never in clear
│   └── schema-version.json
├── profiles\
│   ├── flavien.json             UserProfile
│   └── default.json
├── copilots\
│   ├── optimus\
│   │   ├── copilot.json         identity, abilities, voice, language
│   │   ├── personality.json     traits, style, rules, lexicon
│   │   ├── responses.fr.json    reply variants per event
│   │   ├── prompts\system.md    system prompt (when the LLM is enabled)
│   │   └── assets\avatar.png
│   └── synthia\…
├── commands\
│   ├── starcitizen.core.json    shipped catalogue (signed, not modifiable)
│   ├── starcitizen.mining.json
│   └── user.custom.json         commands created by the user
├── bindings\
│   ├── starcitizen\
│   │   ├── defaults-4.9.json    extracted from defaultProfile.xml
│   │   ├── default.json         active profile  (defaults ⊕ overrides)
│   │   ├── fighter.json
│   │   └── mining.json
├── macros\
│   └── combat-mode.json
├── plugins\
│   └── spotify\ (plugin.json, Optimus.Plugin.Spotify.dll)
├── voices\                      downloaded Piper models
├── models\                      Whisper / VAD / wake word models
├── cache\
├── backups\                     automatic snapshots before any destructive operation
├── logs\optimus-2026-08-23.log
└── optimus.db                   SQLite
```

---

## 6.4 Schemas and examples

### `Copilot`

```json
{
  "$schema": "optimus://schemas/copilot-1.json",
  "id": "optimus",
  "name": "Optimus",
  "version": "1.0.0",
  "description": "Military copilot, calm, loyal, dry humour.",
  "avatar": "assets/avatar.png",
  "accent_color": "#22d3ee",
  "language": "fr-FR",
  "wake_word": "optimus",
  "voice": {
    "provider": "windows-onecore",
    "voice_id": "Microsoft Denise",
    "speed": 1.0,
    "pitch": 0.0,
    "volume": 0.9,
    "fallback": { "provider": "piper", "voice_id": "fr_FR-siwis-medium" }
  },
  "personality_ref": "personality.json",
  "responses_ref": "responses.fr.json",
  "capabilities": {
    "voice": true, "llm": false, "conversation": true,
    "combat": true, "mining": true, "exploration": true, "social": true
  },
  "enabled_commands": { "mode": "all_except", "except": ["ship.self_destruct"] },
  "command_permissions": {
    "dangerous_requires_confirmation": true,
    "denied_categories": []
  },
  "system_prompt_ref": "prompts/system.md"
}
```

`enabled_commands.mode` ∈ `all` | `all_except` | `only` — this makes variants (“Optimus Lite”,
“Optimus Combat”) trivial to build **with no special code** (§30 of the brief).

### `Personality`

```json
{
  "$schema": "optimus://schemas/personality-1.json",
  "traits": {
    "humor": 40, "sarcasm": 25, "formality": 80, "verbosity": 30,
    "aggression": 10, "calmness": 90, "warmth": 45, "confidence": 85
  },
  "style": { "military": true, "sci_fi": true, "immersive": true, "technical": true },
  "speech": { "speed": 1.0, "pitch": 0.0, "max_sentence_words": 14 },
  "lexicon": {
    "address_user": ["commandant", "capitaine"],
    "preferred_phrases": ["Reçu.", "Affirmatif.", "Paramètres nominaux."],
    "forbidden_phrases": ["lol", "mdr", "je suis une IA", "en tant que modèle de langage"],
    "replacements": { "d'accord": "affirmatif", "ok": "reçu" }
  },
  "rules": [
    { "when": "combat_active",   "behavior": "short_responses",   "priority": 100 },
    { "when": "command_failed",  "behavior": "explain_reason",    "priority": 90 },
    { "when": "command_unknown", "behavior": "ask_clarification", "priority": 90 },
    { "when": "user_is_angry",   "behavior": "remain_calm",       "priority": 80 },
    { "when": "idle_long",       "behavior": "occasional_banter", "priority": 10 }
  ]
}
```

### `Command`

```json
{
  "id": "ship.doors.toggle",
  "kind": "action",
  "name": "Open / close the doors",
  "description": "Toggles the state of every airlock on the ship.",
  "category": "ship",
  "tags": ["door", "airlock"],
  "voice_phrases": [
    "open the doors", "open the doors for me", "doors open",
    "unlock the doors", "close the doors", "the doors"
  ],
  "parameters": [],
  "actions": [
    { "type": "game_action", "action_id": "spaceship_general/v_toggle_all_doors", "mode": "tap" }
  ],
  "requirements": [
    { "type": "game_running" },
    { "type": "game_foreground" },
    { "type": "binding_available", "action_id": "spaceship_general/v_toggle_all_doors" }
  ],
  "cooldown_ms": 1000,
  "dangerous": false,
  "responses_key": "ship.doors.toggle",
  "enabled": true,
  "source": "builtin"
}
```

> Note what is **not** in the command: no key. The command references an `action_id`; it is the
> `BindingProfile` that knows this means `L` on this machine (RT-02).

### `BindingProfile` + `Binding`

```json
{
  "$schema": "optimus://schemas/bindingprofile-1.json",
  "id": "default",
  "name": "Default",
  "game": "star-citizen",
  "game_version": "4.9",
  "source": { "defaults": "defaults-4.9.json", "imported_from": "layout_Keybinds_1_exported.xml",
              "imported_at": "2026-08-23T21:10:00Z" },
  "bindings": {
    "spaceship_general/v_toggle_all_doors":   { "key": "L", "mods": [], "mode": "tap" },
    "spaceship_general/v_toggle_landing_system": { "key": "N", "mods": [], "mode": "tap" },
    "spaceship_movement/v_ifcs_toggle_cruise_control": { "key": "B", "mods": [], "mode": "tap" },
    "spaceship_targeting/v_target_cycle_all_fwd": { "key": "T", "mods": ["ALT"], "mode": "tap" },
    "spaceship_weapons/v_weapon_toggle_launch_missile": { "key": "MOUSE2", "device": "mouse", "mode": "hold", "hold_ms": 400 },
    "spaceship_general/v_eject": { "key": "Y", "mods": ["RALT"], "mode": "double_tap" },
    "spaceship_general/v_self_destruct": { "key": "BACKSPACE", "mods": [], "mode": "hold", "hold_ms": 1500 }
  },
  "unbound": ["spaceship_scanning/v_scanning_toggle_focus_mode"]
}
```

`InputSpec` (the normalised form of a physical input):

```jsonc
{
  "device": "keyboard | mouse | gamepad",   // gamepad = V2
  "key": "L | F5 | NUMPAD5 | MOUSE4 | WHEEL_UP",
  "mods": ["SHIFT" | "CTRL" | "ALT" | "RALT" | "LSHIFT" | ...],
  "mode": "tap | hold | double_tap | press | release",
  "hold_ms": 0,          // for hold
  "repeat": 1,           // number of repetitions
  "interval_ms": 40      // between repetitions
}
```

### `Macro`

```json
{
  "id": "macro.combat_mode",
  "name": "Combat mode",
  "voice_phrases": ["combat mode", "go to combat mode", "prep for combat"],
  "dangerous": false,
  "steps": [
    { "type": "game_action", "action_id": "spaceship_power/v_power_preset_combat", "mode": "tap" },
    { "type": "wait", "ms": 150 },
    { "type": "game_action", "action_id": "spaceship_weapons/v_weapon_toggle_ads", "mode": "tap" },
    { "type": "wait", "ms": 150 },
    { "type": "if", "condition": { "type": "game_mode_is", "value": "combat" },
      "then": [ { "type": "game_action", "action_id": "spaceship_targeting/v_target_cycle_hostile_fwd", "mode": "tap" } ],
      "else": [ { "type": "say", "response_key": "combat.mode_uncertain" } ] },
    { "type": "say", "response_key": "macro.combat_mode.done" }
  ]
}
```

### `ResponseSet`

```json
{
  "locale": "fr-FR",
  "entries": {
    "ship.doors.toggle": {
      "success": [
        { "text": "Portes ouvertes, commandant.", "weight": 1.0 },
        { "text": "Compartiments déverrouillés.", "weight": 1.0, "requires": { "formality_min": 60 } },
        { "text": "Voilà. Essayez de ne pas tomber dehors.", "weight": 0.7, "requires": { "sarcasm_min": 50 } }
      ],
      "fail": [ { "text": "Impossible d'actionner les sas." } ]
    },
    "system.unknown_command": {
      "any": [ { "text": "Je ne connais pas cette commande." },
               { "text": "Négatif — cette instruction ne figure pas dans mes protocoles." } ]
    },
    "system.no_binding": {
      "any": [ { "text": "La commande existe, mais aucun raccourci n'est configuré pour {action}." } ]
    },
    "system.game_not_focused": {
      "any": [ { "text": "Compris, mais Star Citizen n'est pas au premier plan." } ]
    }
  }
}
```

### `UserProfile`

```json
{
  "id": "flavien",
  "display_name": "Flavien",
  "language": "fr-FR",
  "preferred_copilot": "optimus",
  "active_binding_profile": "default",
  "voice_input": {
    "device_id": "{0.0.1.00000000}.{...}",
    "mode": "push_to_talk",
    "push_to_talk_key": "INSERT",
    "wake_word_enabled": true,
    "vad_silence_ms": 280,
    "sensitivity": 0.6
  },
  "hotkeys": {
    "push_to_talk": "INSERT",
    "toggle_microphone": "CTRL+INSERT",
    "kill_switch": "CTRL+ALT+PAUSE",
    "toggle_simulation": "CTRL+ALT+S"
  },
  "safety": { "simulation_mode": false, "require_game_foreground": true,
              "confirm_dangerous": true }
}
```

### `Plugin` (manifest)

```json
{
  "id": "spotify",
  "name": "Spotify",
  "version": "0.1.0",
  "sdk_version": "1.0",
  "entry": "Optimus.Plugin.Spotify.dll",
  "permissions": ["commands.register", "network.outbound:api.spotify.com", "settings.own"],
  "provides": { "commands": ["media.play", "media.pause", "media.next"], "providers": [] }
}
```

---

## 6.5 SQLite schema (runtime data)

```sql
CREATE TABLE history (
  id            INTEGER PRIMARY KEY,
  trace_id      TEXT NOT NULL,
  ts_utc        TEXT NOT NULL,
  profile_id    TEXT NOT NULL,
  copilot_id    TEXT NOT NULL,
  source        TEXT NOT NULL,      -- voice | ui | api | discord | plugin
  raw_text      TEXT,               -- raw transcript
  normalized    TEXT,
  intent_id     TEXT,
  confidence    REAL,
  resolver      TEXT,               -- exact | fuzzy | llm | manual
  binding       TEXT,               -- human-readable form: "L", "ALT+T"
  result        TEXT NOT NULL,      -- success | failed | rejected | simulated | unknown
  error_code    TEXT,
  latency_stt_ms INTEGER, latency_intent_ms INTEGER,
  latency_exec_ms INTEGER, latency_total_ms INTEGER
);
CREATE INDEX ix_history_ts ON history(ts_utc DESC);
CREATE INDEX ix_history_intent ON history(intent_id);

CREATE TABLE unknown_phrases (
  id INTEGER PRIMARY KEY, phrase TEXT NOT NULL, normalized TEXT NOT NULL,
  occurrences INTEGER NOT NULL DEFAULT 1, last_seen TEXT NOT NULL,
  suggested_intent TEXT, status TEXT NOT NULL DEFAULT 'new'   -- new | mapped | ignored
);
CREATE UNIQUE INDEX ux_unknown_norm ON unknown_phrases(normalized);

CREATE TABLE command_stats (
  intent_id TEXT PRIMARY KEY, uses INTEGER NOT NULL DEFAULT 0,
  failures INTEGER NOT NULL DEFAULT 0, avg_latency_ms REAL, last_used TEXT
);

CREATE TABLE discord_links (
  id INTEGER PRIMARY KEY, discord_user_id TEXT NOT NULL UNIQUE,
  discord_guild_id TEXT, display_name TEXT,
  paired_at TEXT NOT NULL, revoked_at TEXT,
  permissions_json TEXT NOT NULL     -- {"view_status":true,"execute_commands":false,...}
);

CREATE TABLE embeddings_cache (
  phrase_hash TEXT PRIMARY KEY, model TEXT NOT NULL, vector BLOB NOT NULL
);

CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
```

**Retention**: `history` is purged beyond N days (configurable, 90 by default);
`unknown_phrases` is kept (it is the fuel for improving the matcher).

---

## 6.6 Versioning and migrations

- Every configuration file carries `"$schema": "optimus://schemas/<type>-<major>.json"`.
- At startup: schema validation → if an earlier version is found, **migrate** and take an
  **automatic backup** into `backups\` before writing.
- `schema_meta.db_version` for SQLite, with numbered, idempotent, tested SQL migrations.
- An invalid configuration **never prevents startup**: the offending item is disabled, reported in
  the UI with the validator's error message, and the app starts in degraded mode.
