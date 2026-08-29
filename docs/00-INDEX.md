# OPTIMUS — design dossier

An AI voice copilot for Star Citizen. A local Windows application: self-contained and extensible.

## Contents

| Doc | Brief phase | Content |
|---|---|---|
| [01 — Jean-Bot analysis](01-jean-bot-analysis.md) | Phase 1 | Confirmed / inferred / unknown features, real endpoints, catalogue schema, UX analysis, what Optimus does better |
| [02 — Star Citizen keybinds](02-star-citizen-keybinds.md) | Phase 1bis | Analysis of the supplied XML export, the `ActionMaps` format, reading the screenshot, binding strategy |
| [03 — Requirements](03-requirements.md) | Phase 2 | Functional, non-functional, technical, safety and UX requirements |
| [04 — Architecture](04-architecture.md) | Phase 3 | Layers, responsibilities, process model, nominal flow, per-user isolation, naming |
| [05 — Technical stack](05-stack.md) | Phase 4 | Comparison of the candidate foundations, decisions per component, things to watch |
| [06 — Data model](06-data-model.md) | Phase 5 | ERD, user directory tree, JSON schemas, SQLite, migrations |
| [07 — Command engine](07-command-engine.md) | Phase 6 | Command / Intent / Action / Binding / Sequence / Macro / Condition / Response, resolution, the AI's contract |
| [08 — Character](08-character.md) | Phase 7 | Traits, reply selection, rules, generated prompt, three reference characters |
| [09 — Voice pipeline](09-voice-pipeline.md) | Phase 8 | The whole chain, latency budget, listening modes, providers, audio pitfalls, debug mode |
| [10 — Interface](10-interface.md) | Phase 9 | The 12 screens, mockups, first-run wizard |
| [11 — Roadmap](11-roadmap.md) | Phases 10–12 | MVP v0.1 (scope, definition of done, schedule), V1, V2, non-goals |
| [12 — API / Discord / plugins](12-api-discord-plugins.md) | cross-cutting | Local API, Discord strategy and isolation, plugin model, multi-copilot, packs |
| [13 — Risks, tests, decisions](13-risks-tests-decisions.md) | cross-cutting | Risk register, preliminary spikes, test strategy, the decision log, open questions |
| [14 — Project structure](14-project-structure.md) | Phase 77 | Solution tree, dependency graph, conventions |
| [15 — Code signing](15-code-signing.md) | cross-cutting | SignPath Foundation conditions, build pipeline, signing policy, what is left to do |

*The measurement records under [`spikes/`](spikes/) are kept in French, as written on the day.
They are dated evidence of what was measured on a given machine; a translation would be a
rewrite, and a rewritten measurement is worth less than none.*

## The ten non-negotiable rules

1. **No hard-coded key bindings.** Keys come from a `BindingProfile` loaded at runtime.
2. **The LLM is optional** and off by default; everything works offline.
3. **The AI only produces a structured intent**, validated against a whitelist; it never has
   access to the keyboard.
4. **A single point of control** (`ExecutionGuard`) for permissions, kill switch, simulation,
   cooldown and focus.
5. **Execution is always local.** Discord and the cloud carry intents, never keystrokes.
6. **Never fail silently**: every error produces a reply and a trace.
7. **Simulation mode exists from day one.**
8. **User configuration lives in `%APPDATA%`**, in files you can put under version control.
9. **The core is testable with no microphone, no keyboard and no game.**
10. **A copilot is data**, not code: creating “Optimus Combat” takes no line of C#.
