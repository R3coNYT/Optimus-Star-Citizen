# Structure du projet (adaptée à la stack .NET)

## 14.1 Arborescence de la solution

```
Optimus/
├── Optimus.sln
├── Directory.Build.props            nullable, warnings-as-errors, langversion, analyseurs
├── global.json                      SDK .NET 8 épinglé
│
├── src/
│   ├── Optimus.Core/                        ⭐ le cœur — aucune dépendance UI/OS/réseau
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
│   │   ├── Abstractions/          ⭐ TOUTES les interfaces d'infrastructure
│   │   │                          ISpeechToTextProvider, ITextToSpeechProvider, ILlmProvider,
│   │   │                          IInputEngine, IAudioCapture, IVoiceActivityDetector,
│   │   │                          IWakeWordDetector, IGameDetector, IGameStateProvider,
│   │   │                          IHistoryRepository, IConfigStore, IHotkeyService
│   │   └── Events/                EventBus, événements du domaine
│   │
│   ├── Optimus.Sdk/                         contrats publics des plugins (SemVer strict)
│   │
│   ├── Optimus.Infrastructure/              implémentations Windows/OS
│   │   ├── Audio/                 WasapiCapture, AudioPlayer, DeviceWatcher, RingBuffer
│   │   ├── Speech/                WhisperSttProvider, WindowsSpeechProvider, CloudSttProvider
│   │   ├── Synthesis/             OneCoreTtsProvider, PiperTtsProvider, TtsCache
│   │   ├── Vad/                   SileroVad, EnergyVad
│   │   ├── WakeWord/              PrefixWakeWord, OnnxWakeWord
│   │   ├── Ai/                    OllamaProvider, OpenAiCompatibleProvider, AnthropicProvider,
│   │   │                          IntentSchemaBuilder, WhitelistValidator
│   │   ├── Input/                 ⭐ SEUL endroit avec du code de touche
│   │   │                          SendInputEngine, SimulatedInputEngine, ScanCodeMap,
│   │   │                          HotkeyService, KeyCaptureService
│   │   ├── Game/                  GameProcessDetector, ForegroundWatcher,
│   │   │                          ScActionMapImporter, ScPathResolver, DeclarativeGameState
│   │   ├── Storage/               SqliteConnectionFactory, HistoryRepository, StatsRepository,
│   │   │                          JsonConfigStore, SchemaValidator, Migrations/
│   │   └── Diagnostics/           SerilogSetup, TraceRecorder, MetricsCollector
│   │
│   ├── Optimus.Plugins/                     hôte de plugins
│   │   ├── PluginHost.cs · PluginLoadContext.cs · PermissionBroker.cs · ManifestValidator.cs
│   │
│   ├── Optimus.Bridge/                      API locale (Minimal API + WebSocket)
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
│       └── Resources/            i18n (fr.resx, en.resx), icônes, sons
│
├── data/                                    contenu livré avec l'application
│   ├── commands/starcitizen.core.json
│   ├── bindings/starcitizen/defaults-4.9.json
│   ├── copilots/optimus/ · synthia/ · virgil/
│   └── schemas/                 copilot-1.json, command-1.json, bindingprofile-1.json, …
│
├── tools/
│   ├── Optimus.Tools.ScImport/  CLI : defaultProfile.xml → defaults-<ver>.json (+ diff de versions)
│   ├── Optimus.Tools.Lint/      validation des catalogues et des packs (utilisé en CI)
│   └── Optimus.Tools.Bench/     benchmark STT/TTS et mesure de latence de bout en bout
│
├── tests/
│   ├── Optimus.Core.Tests/            unitaires
│   ├── Optimus.Architecture.Tests/    ⭐ règles de dépendance (NetArchTest)
│   ├── Optimus.Infrastructure.Tests/  intégration
│   ├── Optimus.EndToEnd.Tests/        pipeline complet en simulation
│   └── fixtures/                      WAV de référence, XML SC de plusieurs versions,
│                                      catalogues valides et invalides
│
├── plugins/                             plugins de référence (hors solution principale)
│   ├── Optimus.Plugin.Spotify/
│   └── Optimus.Plugin.System/
│
├── installer/                           Velopack (config, icônes, EULA, scripts de release)
├── docs/                                cette documentation
└── .github/workflows/                   build · tests · lint · release
```

## 14.2 Graphe de dépendances entre projets

```
                      Optimus.Core   ◄── ne dépend de RIEN d'externe
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
                (composition root : c'est ici, et uniquement ici,
                 que les implémentations sont branchées aux interfaces)
```

Interdits vérifiés par la CI : `Core → Infrastructure`, `Core → App`, `Ai → Input`,
constante de touche hors de `Infrastructure.Input`.

## 14.3 Conventions

| Sujet | Règle |
|---|---|
| Nommage | `PascalCase` types/membres, `_camelCase` champs privés, un type par fichier |
| Async | `Async` en suffixe, `CancellationToken` sur toute méthode asynchrone publique, `ConfigureAwait(false)` hors UI |
| Erreurs | `Result<T>` pour les erreurs attendues (binding absent, jeu non détecté) ; exceptions réservées aux bugs |
| Journalisation | Serilog structuré, `trace_id` systématique, jamais de secret ni de transcription en niveau INFO |
| Immutabilité | `record` pour les modèles du domaine ; les registres retournent des vues en lecture seule |
| DI | `Microsoft.Extensions.DependencyInjection`, enregistrement explicite, pas de service locator |
| Documentation | XML doc obligatoire sur tout ce qui est public dans `Core` et `Sdk` |
| i18n | aucun texte utilisateur en dur, `.resx` côté UI, `ResponseSet` côté copilote |
| Commits | Conventional Commits, CI bloquante |
