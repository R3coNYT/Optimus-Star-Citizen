# Cross-cutting strategies — local API, Discord, plugins, multi-copilot

## 12.1 The local API (Optimus Bridge)

Hosted **inside the application's process**, bound to `127.0.0.1` only. It serves: the UI
(eventually), the Discord bot, plugins, a tablet/SIMPIT companion, and future clients. It is
**never** exposed on the local network.

### What is shipped (2026-08-27)

The table below describes the target. What exists today is its executable foundation:

| Method | Route | Scope |
|---|---|---|
| `GET` | `/api/status` | `read` |
| `GET` | `/api/commands` | `read` |
| `POST` | `/api/intents/resolve` | `read` — resolves an utterance **without executing anything** |
| `POST` | `/api/commands/{id}/execute` | `execute` |
| `POST` | `/api/utterance` | `execute` — the same path as the voice |
| `POST` | `/api/say` | `write` |
| `POST` | `/api/system/killswitch` · `/api/system/simulation` | `write` |
| `POST` | `/api/system/listening` | `write` — opens or closes the microphone |
| `WS` | `/ws/events` | `read` — `activity` and `state` frames |

The rest of the target table — CRUD for copilots, commands and bindings, history, statistics —
remains to be done: the screen already covers those gestures, and opening them to the API with no
expressed need would have been attack surface offered for nothing.

**`HttpListener` rather than Kestrel.** A handful of routes on the loopback does not justify
embedding ASP.NET Core in a self-contained publish that already weighs 76 MB. And above all:
measured on 2026-08-27, `HttpListener` accepts `http://127.0.0.1:port/` **without any privilege**
but refuses `http://+:port/` — listening on every interface — to anyone who is not an
administrator. Since Optimus installs per user, without UAC (D58), it is therefore *impossible*
for it to expose itself to the network, even through a programming mistake. The §81-83 promise is
carried by the operating system, not only by a line somebody might one day change.

**One route toggles, the others do not.** `/api/system/listening` accepts
`{"listening": true}` to impose a state, and **toggles** when the body says nothing. The reason is
physical: a Stream Deck key is a single button, and without a plugin it cannot read the current
state to decide what to send. `killswitch` and `simulation` keep their set-only semantics — cutting
commands by mistake is harmless, reopening them by mistake is not.

Starting the microphone can fail, on a machine without the speech feature for the copilot's
language. That answers **503**, not 500: the service is unavailable, the client did nothing wrong,
and the message names the language and what to install.

**An execution takes as long as the speech takes.** `/execute` and `/utterance` only return once
the copilot's reply has been spoken — measured at around 4 s for a talkative refusal. This is
deliberate: the API follows exactly the voice's path, reply included. A client that does not want
to wait listens to `/ws/events` rather than to the HTTP response.

### Routes

| Method | Route | Role | Protection |
|---|---|---|---|
| GET | `/api/status` | full state (voice, game, copilot, latencies, simulation) | token · read |
| GET | `/api/copilots` · `/api/copilots/{id}` | list / detail | token · read |
| POST | `/api/copilots` · PUT/DELETE `/api/copilots/{id}` | CRUD | token · **write** |
| POST | `/api/copilots/{id}/activate` | switch the active copilot | token · write |
| GET | `/api/commands` (`?q=&category=&favorite=`) | catalogue | token · read |
| POST/PUT/DELETE | `/api/commands[/{id}]` | CRUD for user commands | token · **write** |
| POST | `/api/commands/{id}/test` | execution **forced into simulation** | token · write |
| POST | `/api/commands/{id}/execute` | real execution | token · **execute** + guard |
| POST | `/api/intents/resolve` | text → intent (without executing) | token · read |
| GET/PUT | `/api/bindings/{profile}` | binding profile | token · write |
| POST | `/api/bindings/import` | import an SC XML | token · write |
| GET/PUT | `/api/profiles[/{id}]` | user profiles | token · write |
| GET | `/api/history?limit=&since=` | history | token · read |
| GET | `/api/analytics/*` | statistics | token · read |
| POST | `/api/say` | make the copilot speak | token · write |
| POST | `/api/system/killswitch` · `/api/system/simulation` | safety | token · **write** |
| POST | `/api/system/listening` | open / close the microphone | token · **write** |
| WS | `/ws/events` | real-time stream (state, commands, traces) | token |

### Security model

1. A **bearer token** generated at first launch, 256 bits, stored encrypted (DPAPI), shown in
   Settings, revocable and regenerable.
2. **Three scopes**: `read`, `write`, `execute`. A client gets the minimum (the Discord bot starts
   on `read` alone).
3. `execute` **always goes back through the `ExecutionGuard`**: simulation, kill switch, game
   focus, permissions, `dangerous`. No route short-circuits the single point of control.
4. **Rate limiting** per client (30 executions per minute by default) + logging of the source
   (`source = api | discord | plugin`) in the history.
5. **CORS open to every origin, and that is deliberate** (2026-08-29). The guard on this API is
   the token, not the origin: CORS stops a page from *reading* an answer, it does not stop it
   being sent, and a page that does not know a 256-bit secret gets a 401 whatever origin it
   declares. `*` rather than a list, because a Stream Deck plugin loads from a local file and so
   presents the origin `null`, which no list would have covered.
   **`Access-Control-Allow-Credentials` is never sent**, and that is the line not to cross:
   Optimus authenticates through no cookie, so no page can forge an authenticated request behind
   the pilot's back. Adding it would create the very hole its absence makes impossible.
6. **The token may also travel as a WebSocket subprotocol.** `new WebSocket(url, protocols)` is
   the only way a browser client can carry a secret on that handshake — the JavaScript API cannot
   set a header. The client announces `["optimus.v1", "<token>"]`; the server answers with the
   protocol name alone, never the secret. It travels verbatim: tokens are issued as base64url
   without padding, so `A-Za-z0-9-_` only, which RFC 6455 accepts as a subprotocol name.
7. LAN listening is possible **only** through an option shown with a clear warning, with a
   mandatory token, and never enabled by default.

---

## 12.2 Discord (Optimus Link)

### Two modes, one principle

```
LOCAL MODE (V1, the default)                RELAY MODE (V2, optional)

 Discord ──► bot hosted INSIDE               Discord ──► relay ──outbound WS──► Optimus
             Optimus (your own token)                     (holds only intents)
             │                                                        │
             └► ExecutionGuard ► local keyboard                       └► ExecutionGuard ► local keyboard
```

In **both cases**: the relay and Discord **never** carry a keystroke, only an `intent_id` plus
parameters. The connection is **outbound**. The target machine validates everything locally.

Local mode is the recommended default: it needs no infrastructure, and it makes the isolation
(§81–83) true *by construction* rather than by policy.

### Pairing

```
1. Optimus (Settings ▸ Discord): [ Generate a pairing code ]  →  OPT-7K3F-92XA (10 min)
2. Discord: /optimus pair OPT-7K3F-92XA
3. Optimus checks the code and creates a DiscordLink:
   { discord_user_id, permissions: { view_status:true, view_commands:true,
                                     execute_commands:FALSE, modify_config:FALSE } }
4. The owner raises the permissions by hand, per user, in the UI.
5. Revocation in one click; automatic expiry after N days of inactivity (optional).
```

### Bot commands

| Command | Permission required |
|---|---|
| `/optimus status` | `view_status` |
| `/optimus commands [search] [category]` | `view_commands` |
| `/optimus command <name>` (detail + binding) | `view_commands` |
| `/optimus history [n]` | `view_history` |
| `/optimus profiles` · `/optimus profile <id>` | `view_status` / `modify_config` |
| `/optimus say <text>` | `execute_commands` |
| `/optimus exec <command>` | `execute_commands` (+ the local guard, + confirmation when `dangerous`) |
| `/optimus pair <code>` | — (this is the entry point) |
| `/optimus help` | — |

Short aliases: `/opt …`.

### Notifications (opt-in, event by event)

`🟢 Optimus started` · `🟡 Star Citizen detected / closed` · `🔵 command executed` ·
`🔴 command failed` · `⚠️ unknown command` · `🟠 provider degraded` · `⛔ kill switch engaged`.

### Extra guard rails

- `execute_commands` is **off by default**, including for the owner.
- `dangerous` commands require a **confirmation in the application**, never on Discord.
- A dedicated rate limit, stricter than the API's.
- Every Discord-originated execution is marked in the history with the Discord identity.
- The local kill switch cuts Discord-originated executions **too**.
- A Discord user can be linked to **one** instance at a time (which removes the “which machine?”
  ambiguity).

---

## 12.3 Plugins

### The contract

```csharp
// Optimus.Sdk — a stable, versioned public surface (SemVer)
public interface IOptimusPlugin
{
    PluginMetadata Metadata { get; }                 // id, name, version, sdk_version
    Task InitializeAsync(IPluginContext ctx, CancellationToken ct);
    Task ShutdownAsync(CancellationToken ct);
}

public interface IPluginContext
{
    IReadOnlyList<CommandDefinition> RegisterCommands();      // the commands contributed
    void RegisterActionHandler(string ns, IActionHandler h);  // the "plugin" step type
    void RegisterProvider<T>(T provider);                     // an alternative STT/TTS/LLM/GameState
    void RegisterCondition(string id, IConditionEvaluator e);
    IEventBus Events { get; }        // subscription to core events (read only)
    ILogger Logger { get; }
    IPluginStorage Storage { get; }  // a folder and keys of the plugin's own
    IPluginSettings Settings { get; }// a settings schema rendered automatically in the UI
}
```

### Permission model

Declared in the manifest, shown at install time, refusable:

| Permission | Grants the right to |
|---|---|
| `commands.register` | add commands to the catalogue |
| `commands.execute` | trigger an existing command |
| `providers.register` | supply an STT/TTS/LLM/GameState |
| `network.outbound:<host>` | reach a specific host (never `*` without a warning) |
| `filesystem.own` | write inside its own folder |
| `filesystem.read:<path>` | read a specific path |
| `input.raw` | **inject input directly** — the highest-level permission, with an explicit warning, reserved for cases commands do not cover |
| `events.subscribe` | listen to core events |

Loaded into a collectible `AssemblyLoadContext` (hot unloading), with isolated dependencies and
wrapped calls (`try/catch` + timeout): **a plugin that crashes does not bring Optimus down**.
Distributed packs are signed; an invalid signature means the installation is refused.

### Planned reference plugins

`starcitizen` (built into the core for the MVP, extracted into a plugin later) · `system` (volume,
clipboard, launching applications) · `spotify` · `obs` · `twitch` · `telemetry` ·
`voiceattack-import`.

---

## 12.4 Multi-copilot

| Level | How it works | Version |
|---|---|---|
| **1 — Selection** | One active copilot, switched hot (UI, voice, API); each has its wake word, its voice, its commands | MVP (one shipped) / V1 (n) |
| **2 — Variants** | One copilot declined (`Optimus Lite/Combat/Mining`) through `enabled_commands` + abilities, with no special code | V1 |
| **3 — Routing** | The wake word decides the recipient: “Synthia, …” wakes Synthia even while Optimus is active | V1/V2 |
| **4 — Multi-agent** | Several copilots active, cross-talk, speech scheduling, a single holder of the microphone | V2 |

**The hard problem at level 4** is not technical but theatrical: two voices talking over each
other are unbearable. It needs a `ConversationDirector` (a speaking queue, priorities, turns,
interruptions allowed or not) — which is why it is V2 and not V1.

---

## 12.5 `.optcopilot` packs

```
optimus-synthia-1.2.0.optcopilot   (a signed ZIP)
├── manifest.json        id, name, version, sdk_version, author, licence, checksum
├── copilot.json
├── personality.json
├── responses.fr.json / responses.en.json
├── commands/            additional commands (validated by schema)
├── bindings/            suggested bindings (NEVER applied without confirmation)
├── prompts/system.md    a bounded, escaped fragment
├── voices/              Piper models or voice references
├── assets/              avatar, sounds
└── plugins/             optional, with declared permissions
```

**Import rules** (an attack surface to take seriously):
1. The signature is verified; otherwise an explicit warning and a manual installation.
2. Every file is validated by schema **before** being written; paths are normalised (*zip slip*
   protection).
3. Suggested bindings are offered as a **diff**, never applied silently.
4. The prompt fragment is bounded in length and cannot contain a safety directive.
5. Bundled plugins go through the normal permission circuit.
6. Sandboxed import: the pack is first loaded in **forced simulation mode** for a trial run.
