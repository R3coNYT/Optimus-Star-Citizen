# Optimus on Stream Deck

Five keys that drive the copilot, and — this is the part a keyboard shortcut cannot do —
**carry its live state**: microphone open or closed, emergency stop engaged, simulation on.

| Key | What it does |
|---|---|
| **Microphone** | Opens or closes listening. Lit while the microphone is open. |
| **Emergency stop** | Cuts every command. Turns red while engaged. |
| **Simulation** | Resolves and announces commands without touching the keyboard. |
| **Run a command** | One command from the catalogue, with its guard and its spoken reply. |
| **Speak** | Makes the copilot say a line out loud, in its own voice. |

## Why this and not a keyboard shortcut

A Stream Deck already sends keystrokes. What Optimus adds is everything around them:

- **The key does not break when you rebind.** A key configured on `RCTRL+RALT+SEMICOLON` dies the
  day you change the doors binding in the game. A key that calls `ship.doors.toggle` never does —
  Optimus looks up your *current* binding.
- **One key, a whole macro.** Conditions, directed polarity (turn *on*, not toggle), the execution
  guard, and the spoken reply. The pre-flight macro is seven steps and 5.8 seconds. One key.
- **The key is a lamp.** The plugin reads `/ws/events` and repaints as the state changes — whether
  you clicked in the window or pressed the deck.

## Install — no Marketplace involved

Elgato's Marketplace is a distribution channel, not a gate. A plugin is a folder.

```
%APPDATA%\Elgato\StreamDeck\Plugins\com.optimus.copilot.sdPlugin\
```

Copy `com.optimus.copilot.sdPlugin` there, then quit and reopen the Stream Deck application. The
five actions appear under an **Optimus** category.

To hand it to someone else as a single file, package it with Elgato's CLI:

```bash
npx @elgato/cli pack com.optimus.copilot.sdPlugin
```

That produces `com.optimus.copilot.streamDeckPlugin`, which installs on a double-click. Still no
Marketplace.

While working on the plugin, `npx @elgato/cli dev` enables developer mode, and
`npx @elgato/cli restart com.optimus.copilot` reloads it without restarting the whole application.

## Set it up

1. In Optimus: **Settings ▸ Local API**, switch it on, copy the token.
2. Drop any Optimus key on the deck, and paste the token in its panel.

Port and token are **shared by every key** — paste once. The command and the spoken line belong to
each key.

The panel says what it finds: connected, refused, or nothing answering. A key that fails on the
deck can only blink; the panel is where you learn *why*.

## What it needs from Optimus

Version **0.1.7** or later. Earlier builds had no route to open the microphone, sent no CORS
headers, and read the token only from the `Authorization` header — which a browser cannot set on a
WebSocket.

Everything stays on `127.0.0.1`. The plugin runs inside the Stream Deck application, on the same
machine as Optimus; nothing crosses the network, including for Stream Deck Mobile, which relays
through the desktop application.

## Your own icons

Every image below is a **stand-in**. Drop your own file over it, keep the name, restart the Stream
Deck application. Nothing else to edit — the manifest already points at these names.

All of them live in one folder:

```
tools/streamdeck/com.optimus.copilot.sdPlugin/icons/
```

Two files per image: `name.png` and `name@2x.png`, at double the size. The Stream Deck picks the
one that suits the screen; giving only the small file leaves a blurry key on current hardware.

### Keys — 72 × 72 and 144 × 144

| File | Shown when |
|---|---|
| `mic-off` · `mic-off@2x` | microphone closed |
| `mic-on` · `mic-on@2x` | microphone open |
| `stop-off` · `stop-off@2x` | emergency stop released |
| `stop-on` · `stop-on@2x` | emergency stop **engaged** |
| `sim-off` · `sim-off@2x` | real mode — keys really go out |
| `sim-on` · `sim-on@2x` | simulation |
| `command-off` · `command-off@2x` | the “run a command” key, at rest |
| `command-on` · `command-on@2x` | the same key, after a switch-on |
| `speak` · `speak@2x` | the speak key, which has only one state |

### Action thumbnails — 20 × 20 and 40 × 40

Shown in the action list on the right of the Stream Deck window, never on the hardware. They are
small and appear on a light background: a line drawing reads, a photograph does not.

`action-mic` · `action-stop` · `action-sim` · `action-command` · `action-speak`

### Category — 28 × 28 and 56 × 56

`category` · `category@2x` — the Optimus line in the plugin list.

### Per-key images, without touching a file

The “run a command” key carries **two states**, so one pair of files cannot cover every use: doors
and cockpit want different drawings. Set them per key instead, in the Stream Deck window itself —
select the key, and each state gets its own image slot. Any PNG anywhere on the disk will do.

That is the only way to get doors-open / doors-closed on one key and canopy-open / canopy-closed on
the next.

**What that state means, exactly.** Optimus does not know whether your doors are open: the game
reports nothing. It only knows the switches it caused itself, and the plugin cannot know more than
it does. The key therefore shows **the last successful press from that key** — useful, and not the
same thing as the state of the ship. Set a direction (`on` / `off`) in the key's panel and the
state stops guessing.

### Regenerating the stand-ins

```powershell
.\tools\make-streamdeck-icons.ps1
```

Rebuilds all thirty files from `images/Optimus.png`, in three treatments: full, dimmed, and red for
the engaged stop. It **overwrites** the folder — run it before you draw your own, not after.

## How it is built

No runtime to ship: the plugin is HTML and JavaScript, hosted by the Stream Deck application
itself. A native plugin would have needed either the .NET runtime present on the pilot's machine
or a self-contained executable of some fifteen megabytes.

```
manifest.json     the five actions and their states
plugin.html/js    invisible; holds both sockets — one to the deck, one to Optimus
inspector.*       the settings panel, and the only place a bad token gets explained
icons/            generated by tools/make-streamdeck-icons.ps1 from images/Optimus.png
```

The token travels as a **WebSocket subprotocol**, not as a header, because the browser API offers
no way to set one. It is issued as base64url without padding — `A-Za-z0-9-_` — which RFC 6455
accepts verbatim as a subprotocol name, so it needs no re-encoding.
