# Project structure (fitted to the .NET stack)

## 14.1 Solution tree

```
Optimus/
├── Optimus.sln
├── Directory.Build.props            nullable, warnings-as-errors, langversion, analysers
├── global.json                      .NET 8 SDK pinned
│
├── src/
│   ├── Optimus.Core/                        ⭐ the core — no UI/OS/network dependency
│   │   ├── Domain/
│   │   │   ├── Commands/          Command, CommandKind, ActionStep, Macro, Condition, ResponseSet
│   │   │   ├── Bindings/          BindingProfile, Binding, InputSpec, InputMode, KeyCode
│   │   │   ├── Copilots/          Copilot, Capabilities, VoiceConfig
│   │   │   ├── Personality/       Personality, Traits, BehaviorRule, Lexicon
│   │   │   ├── Profiles/          UserProfile, AppSettings
│   │   │   └── Common/            Result<T>, TraceId, OptimusError
│   │   ├── Intent/                TextNormalizer, PhraseIndex, FastIntentMatcher,
│   │   │                          IntentResolver, SlotFiller, ResolvedIntent
│   │   ├── Execution/             CommandExecutor, ExecutionGuard, SequenceRunner,
│   │   │                          BindingResolver, CooldownTracker
│   │   ├── PersonalityEngine/     ResponseSelector, ResponseComposer, RuleEngine
│   │   ├── Context/               ConversationContext, GameContext, ContextManager
│   │   ├── Voice/                 VoicePipeline, UtteranceAssembler
│   │   ├── Registry/              CommandRegistry, CopilotRegistry, BindingRegistry
│   │   ├── Abstractions/          ⭐ EVERY infrastructure interface
│   │   │                          ISpeechToTextProvider, ITextToSpeechProvider, ILlmProvider,
│   │   │                          IInputEngine, IAudioCapture, IVoiceActivityDetector,
│   │   │                          IWakeWordDetector, IGameDetector, IGameStateProvider,
│   │   │                          IHistoryRepository, IConfigStore, IHotkeyService
│   │   └── Events/                EventBus, domain events
│   │
│   ├── Optimus.Sdk/                         public plugin contracts (strict SemVer)
│   │
│   ├── Optimus.Infrastructure/              Windows/OS implementations
│   │   ├── Audio/                 WasapiCapture, AudioPlayer, DeviceWatcher, RingBuffer
│   │   ├── Speech/                WhisperSttProvider, WindowsSpeechProvider, CloudSttProvider
│   │   ├── Synthesis/             OneCoreTtsProvider, PiperTtsProvider, TtsCache
│   │   ├── Vad/                   SileroVad, EnergyVad
│   │   ├── WakeWord/              PrefixWakeWord, OnnxWakeWord
│   │   ├── Ai/                    OllamaProvider, OpenAiCompatibleProvider, AnthropicProvider,
│   │   │                          IntentSchemaBuilder, WhitelistValidator
│   │   ├── Input/                 ⭐ the ONLY place holding key-code knowledge
│   │   │                          SendInputEngine, SimulatedInputEngine, ScanCodeMap,
│   │   │                          HotkeyService, KeyCaptureService
│   │   ├── Game/                  GameProcessDetector, ForegroundWatcher,
│   │   │                          ScActionMapImporter, ScPathResolver, DeclarativeGameState
│   │   ├── Storage/               SqliteConnectionFactory, HistoryRepository, StatsRepository,
│   │   │                          JsonConfigStore, SchemaValidator, Migrations/
│   │   └── Diagnostics/           SerilogSetup, TraceRecorder, MetricsCollector
│   │
│   ├── Optimus.Plugins/                     plugin host
│   │   ├── PluginHost.cs · PluginLoadContext.cs · PermissionBroker.cs · ManifestValidator.cs
│   │
│   ├── Optimus.Bridge/                      local API (Minimal API + WebSocket)
│   │   ├── Endpoints/ · Auth/ (TokenStore, ScopeMiddleware) · RateLimiting/ · Contracts/
│   │
│   ├── Optimus.Link/                        Discord
│   │   ├── DiscordBotService.cs · Commands/ · Pairing/ · Permissions/ · Notifications/
│   │
│   └── Optimus.App/                         WPF — Optimus Command Center
│       ├── App.xaml(.cs)         composition root, DI, single instance, tray
│       ├── Views/                Dashboard, Commands, Keybinds, Copilots, Personality,
│       │                         Voice, Ai, Profiles, Discord, Plugins, Logs, Settings,
│       │                         Onboarding/, Dialogs/ (KeyCapture, Confirm, Conflict)
│       ├── ViewModels/
│       ├── Controls/             StatusPill, WaveMeter, TraceView, KeyCaptureBox,
│       │                         CommandCard, TraitSlider
│       ├── Themes/               Avionics.xaml, Colors.xaml, Typography.xaml
│       ├── Services/             NavigationService, DialogService, TrayService, ThemeService
│       └── Resources/            i18n (fr.resx, en.resx), icons, sounds
│
├── data/                                    content shipped with the application
│   ├── commands/starcitizen.core.json
│   ├── bindings/starcitizen/defaults-4.9.json
│   ├── copilots/optimus/ · synthia/ · virgil/
│   └── schemas/                 copilot-1.json, command-1.json, bindingprofile-1.json, …
│
├── tools/
│   ├── Optimus.Tools.ScImport/  CLI: defaultProfile.xml → defaults-<ver>.json (+ version diff)
│   ├── Optimus.Tools.Lint/      catalogue and pack validation (used in CI)
│   └── Optimus.Tools.Bench/     STT/TTS benchmark and end-to-end latency measurement
│
├── tests/
│   ├── Optimus.Core.Tests/            unit
│   ├── Optimus.Architecture.Tests/    ⭐ dependency rules (NetArchTest)
│   ├── Optimus.Infrastructure.Tests/  integration
│   ├── Optimus.EndToEnd.Tests/        the whole pipeline in simulation
│   └── fixtures/                      reference WAVs, SC XML from several versions,
│                                      valid and invalid catalogues
│
├── plugins/                             reference plugins (outside the main solution)
│   ├── Optimus.Plugin.Spotify/
│   └── Optimus.Plugin.System/
│
├── installer/                           Velopack (config, icons, EULA, release scripts)
├── docs/                                this documentation
└── .github/workflows/                   build · tests · lint · release
```

## 14.2 Dependency graph between projects

```
                      Optimus.Core   ◄── depends on NOTHING external
                      (Abstractions)
                       ▲    ▲    ▲    ▲
            ┌──────────┘    │    │    └──────────┐
   Optimus.Infrastructure   │    │        Optimus.Sdk
            ▲               │    │               ▲
            │        Optimus.Bridge  Optimus.Link│
            │               ▲    ▲               │
            └───────┬───────┘    │        Optimus.Plugins
                    │            │               ▲
                Optimus.App ─────┴───────────────┘
                (composition root: here, and only here, are the
                 implementations wired to the interfaces)
```

Forbidden and checked by CI: `Core → Infrastructure`, `Core → App`, `Ai → Input`, and any key
constant outside `Infrastructure.Input`.

## 14.3 Conventions

| Subject | Rule |
|---|---|
| Naming | `PascalCase` for types and members, `_camelCase` for private fields, one type per file |
| Async | `Async` suffix, `CancellationToken` on every public async method, `ConfigureAwait(false)` outside the UI |
| Errors | `Result<T>` for expected failures (missing binding, game not detected); exceptions reserved for bugs |
| Logging | Structured Serilog, `trace_id` everywhere, never a secret nor a transcript at INFO level |
| Immutability | `record` for domain models; registries return read-only views |
| DI | `Microsoft.Extensions.DependencyInjection`, explicit registration, no service locator |
| Documentation | XML doc required on everything public in `Core` and `Sdk` |
| i18n | no hard-coded user text, `.resx` on the UI side, `ResponseSet` on the copilot side |
| Commits | Conventional Commits, CI blocking |
