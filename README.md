# OPTIMUS

**An AI voice copilot for Star Citizen.** A local Windows application: Optimus listens,
understands, runs the game's actions through *your* key bindings, and answers with a voice and a
character you choose.

```
voice → local STT → intent → validation → local binding → local keyboard → Star Citizen
                                      ↑
                     optional LLM (never required, never at the controls)
```

## Installation

Take the installer from [the releases](../../releases) and run it. Per-user installation, no
administrator rights, into `%LOCALAPPDATA%\Programs\Optimus`.

One page of the wizard offers the optional components — neural voices and free speech — which are
downloaded then, each verified against its hash. Without them Optimus works in full: Windows
voices and a closed grammar.

> **The installer is not signed yet.** SmartScreen will show “Windows protected your PC”: the
> button to continue hides behind *More info*. Signing is under way, see below. Check the SHA-256
> published next to the file.

## Status

**The application is complete and the engine has been put to work.** The risk spikes are behind
us (`docs/13`): scancode injection is proven inside the game, the 627 real Star Citizen 4.9
bindings are imported, and the `utterance → intent → guard → binding → input` pipeline runs end
to end.

The desktop application covers listening, the catalogue, key bindings, macros, settings, and what
Optimus failed to understand. A command-line test bench doubles all of it, with no microphone, no
keyboard and no game.

```bash
dotnet run --project tools/Optimus.Cli -- "Optimus, lights on"
```

```
trace f06bb79f · Simulated · 13.5 ms
  utterance   « Optimus lights on »
  normalised  « lights on »
  intent      ship.lights.toggle  score 1.00  (Exact)
  guard       Allowed
  step 0      lights_controller/v_lights → L
```

With no argument the program goes interactive; `?` lists the commands and their keys, `--status`
details game detection. `dotnet test` runs the suite against the repository's real data.

**Simulation is the default mode**: not one key leaves the machine until `--real` is asked for
explicitly. Real mode requires Star Citizen to be running and in the foreground, and refuses to
start if the game is elevated while Optimus is not — Windows would silently filter the input.

```bash
dotnet run --project tools/Optimus.Cli -- --real "Optimus, lights on"
```

## Talking to Optimus

```bash
dotnet run --project tools/Optimus.Cli -- --listen
```

Optimus listens, recognises the command, runs it, and answers out loud.

Two modes, set in [`data/profiles/default.json`](data/profiles/default.json):

| Mode | Trigger | Grammar |
|---|---|---|
| `always_on` *(default)* | the wake word | only `Optimus <command>` |
| `push_to_talk` | a key you choose | both forms, disabled while the key is up |

While always listening, the grammar only accepts sentences that begin with “Optimus”: ordinary
conversation matches no alternative and is not even transcribed.

## Architecture

| Project | Target | Role |
|---|---|---|
| `Optimus.Core` | `net8.0`, **platform-neutral** | Domain, intents, execution, simulation. No system API: testable anywhere. |
| `Optimus.Infrastructure` | `net8.0-windows` | `SendInput` in scancodes, key table, game detection. |
| `Optimus.Cli` | `net8.0-windows` | Engine test bench. |

## Principles

- **Local first**: works offline, with no account and no API bill.
- **No hard-coded key bindings**: keys are imported from Star Citizen and editable.
- **The AI proposes, the engine disposes**: the LLM can only emit an intent from a whitelist.
- **Isolation per machine**: a command only acts on the PC that received it.
- **Self-contained**: no dependency on VoiceAttack.
- **A copilot is data**: character, voice, abilities and commands are files.

## What Optimus will not do

No continuous gameplay automation (aiming, farming, looping macros), no control of someone else's
machine, no telemetry without consent. One spoken utterance means one deliberate action by the
player.

## Documentation

See [docs/00-INDEX.md](docs/00-INDEX.md) — the analysis, the architecture, the stack, the data
models, the command engine, the character system, the voice pipeline, the interface, the roadmap,
the risks and the decisions.

## Code signing

Published binaries will be signed by the [SignPath Foundation](https://signpath.org), which
offers this service to open source projects. The **code signing policy** — who signs, what is
signed, what is never signed — lives in [docs/15](docs/15-code-signing.md).

Nothing is signed from a development machine: only the [build
pipeline](.github/workflows/release.yml), triggered by a tag on this repository, can submit an
artefact for signing.

Free code signing is provided by [SignPath.io](https://signpath.io/), with a certificate from the
[SignPath Foundation](https://signpath.org/). No maintainer of this project ever holds the private
key.

> **Status: the application to the foundation is pending.** The binaries published so far are
> **not** signed, and Windows will say so. Verify the SHA-256 published beside each release.

## Privacy policy

Optimus collects nothing, sends no telemetry, and has no account. **This program will not transfer
any information to other networked systems unless specifically requested by the user or the person
installing or operating it.**

Concretely, three things reach the network, and only three:

| What | When | Where |
|---|---|---|
| Neural voice and free-speech models | if you tick them in the installer | `huggingface.co` |
| The text of an utterance | only if *you* enable the optional LLM and supply your own key | the provider *you* choose |
| Nothing else | — | — |

Speech recognition, intent resolution and key injection all run on your machine. The LLM is off by
default; with it off, Optimus never opens a connection while running.

## Licence

[GNU General Public License v3.0](LICENSE). You may use, study, modify and redistribute Optimus;
any redistributed version must stay free under the same licence.

The optional components downloaded at install time are neither rebuilt nor redistributed by this
repository, and keep their own licences: [Piper](https://github.com/rhasspy/piper) (MIT),
[whisper.cpp](https://github.com/ggml-org/whisper.cpp) (MIT) and their models.

---

*Optimus is an independent project, unaffiliated with Cloud Imperium Games. Star Citizen is a
trademark of Cloud Imperium Rights LLC. No game file is redistributed here: Optimus reads the key
bindings you export yourself from the game.*
