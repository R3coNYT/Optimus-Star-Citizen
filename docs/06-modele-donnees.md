# PHASE 5 — Architecture des données

## 6.1 Principe : deux natures de données

| Nature | Contenu | Support | Pourquoi |
|---|---|---|---|
| **Configuration** (déclarative, versionnable, partageable, éditable à la main) | copilotes, personnalités, commandes, bindings, macros, profils, paramètres | **Fichiers JSON** (+ import/export YAML) sous `%APPDATA%\Optimus\` | Diffable, sauvegardable, partageable, lisible sans outil, réparable à la main |
| **Runtime** (volumineux, indexé, requêté) | historique, analytics, cache d'embeddings, appairages Discord, statistiques | **SQLite** (`optimus.db`, WAL) | Requêtes, agrégations, volume |

Règle : **aucune donnée de configuration n'existe *uniquement* en base**. La base peut être
supprimée sans perdre la configuration.

---

## 6.2 ERD logique

```
                       ┌──────────────┐
                       │ UserProfile  │  (Flavien, langue, PTT, wake word…)
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
│ lexicon{} │ └──────────────┘    │                  │ résolu par
└─────┬─────┘                     │                  │
      │ sélectionne               ▼                  │
      │                    ┌─────────────┐           │
      │                    │  Command    │───────────┘
      │                    │ id, kind    │  action_ref
      │                    │ category    │
      │                    │ voice_      │──────┐ 1..n
      │                    │  phrases[]  │      ▼
      │                    │ requirements│  ┌────────────┐
      │                    │ cooldown    │  │  Action    │ (étape de séquence)
      │                    └──────┬──────┘  │ type, mode │
      │                           │         │ target, ms │
      │  1..n                     │ 1..n    └────────────┘
      ▼                           ▼               ▲
┌──────────────┐          ┌──────────────┐        │ réutilise
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

## 6.3 Arborescence des fichiers utilisateur

```
%APPDATA%\Optimus\
├── config\
│   ├── settings.json            paramètres application (audio, providers, hotkeys, simulation)
│   ├── secrets.dat              clés API / token Bridge — chiffré DPAPI, jamais en clair
│   └── schema-version.json
├── profiles\
│   ├── flavien.json             UserProfile
│   └── default.json
├── copilots\
│   ├── optimus\
│   │   ├── copilot.json         identité, capacités, voix, langue
│   │   ├── personality.json     traits, style, règles, lexique
│   │   ├── responses.fr.json    variantes de réponses par événement
│   │   ├── prompts\system.md    prompt système (si LLM activé)
│   │   └── assets\avatar.png
│   └── synthia\…
├── commands\
│   ├── starcitizen.core.json    catalogue livré (signé, non modifiable)
│   ├── starcitizen.mining.json
│   └── user.custom.json         commandes créées par l'utilisateur
├── bindings\
│   ├── starcitizen\
│   │   ├── defaults-4.9.json    extrait de defaultProfile.xml
│   │   ├── default.json         profil actif  (defaults ⊕ overrides)
│   │   ├── fighter.json
│   │   └── mining.json
├── macros\
│   └── combat-mode.json
├── plugins\
│   └── spotify\ (plugin.json, Optimus.Plugin.Spotify.dll)
├── voices\                      modèles Piper téléchargés
├── models\                      modèles Whisper / VAD / wake word
├── cache\
├── backups\                     snapshots automatiques avant toute opération destructive
├── logs\optimus-2026-08-23.log
└── optimus.db                   SQLite
```

---

## 6.4 Schémas et exemples

### `Copilot`

```json
{
  "$schema": "optimus://schemas/copilot-1.json",
  "id": "optimus",
  "name": "Optimus",
  "version": "1.0.0",
  "description": "Copilote militaire, calme, loyal, humour sec.",
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

`enabled_commands.mode` ∈ `all` | `all_except` | `only` — permet de fabriquer trivialement des
variantes (« Optimus Lite », « Optimus Combat ») **sans code spécifique** (§30 du brief).

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
  "name": "Ouvrir / fermer les portes",
  "description": "Bascule l'état de tous les sas du vaisseau.",
  "category": "ship",
  "tags": ["porte", "sas"],
  "voice_phrases": [
    "ouvre les portes", "ouvre-moi les portes", "ouverture des portes",
    "déverrouille les portes", "ferme les portes", "les portes"
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

> Noter ce qui **n'est pas** dans la commande : aucune touche. La commande référence une
> `action_id` ; c'est le `BindingProfile` qui sait que cela vaut `L` sur cette machine (RT-02).

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

`InputSpec` (forme normalisée d'une entrée physique) :

```jsonc
{
  "device": "keyboard | mouse | gamepad",   // gamepad = V2
  "key": "L | F5 | NUMPAD5 | MOUSE4 | WHEEL_UP",
  "mods": ["SHIFT" | "CTRL" | "ALT" | "RALT" | "LSHIFT" | ...],
  "mode": "tap | hold | double_tap | press | release",
  "hold_ms": 0,          // pour hold
  "repeat": 1,           // nb de répétitions
  "interval_ms": 40      // entre répétitions
}
```

### `Macro`

```json
{
  "id": "macro.combat_mode",
  "name": "Mode combat",
  "voice_phrases": ["mode combat", "passe en mode combat", "prépare le combat"],
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

### `Plugin` (manifeste)

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

## 6.5 Schéma SQLite (données de runtime)

```sql
CREATE TABLE history (
  id            INTEGER PRIMARY KEY,
  trace_id      TEXT NOT NULL,
  ts_utc        TEXT NOT NULL,
  profile_id    TEXT NOT NULL,
  copilot_id    TEXT NOT NULL,
  source        TEXT NOT NULL,      -- voice | ui | api | discord | plugin
  raw_text      TEXT,               -- transcription brute
  normalized    TEXT,
  intent_id     TEXT,
  confidence    REAL,
  resolver      TEXT,               -- exact | fuzzy | llm | manual
  binding       TEXT,               -- représentation lisible : "L", "ALT+T"
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

**Rétention** : `history` purgé au-delà de N jours (paramétrable, défaut 90) ;
`unknown_phrases` conservé (c'est le carburant de l'amélioration du matcher).

---

## 6.6 Versionnement et migrations

- Chaque fichier de configuration porte `"$schema": "optimus://schemas/<type>-<major>.json"`.
- Au démarrage : validation par schéma → si version antérieure, **migration** + **backup
  automatique** dans `backups\` avant écriture.
- `schema_meta.db_version` pour SQLite, migrations SQL numérotées, idempotentes, testées.
- Une configuration invalide **n'empêche jamais le démarrage** : l'élément fautif est désactivé,
  signalé dans l'UI avec le message d'erreur du validateur, et l'app démarre en mode dégradé.
