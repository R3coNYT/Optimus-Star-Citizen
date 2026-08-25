# PHASE 3 — Architecture générale

## 4.1 Principes directeurs

1. **Un seul processus applicatif**, des *sidecars* uniquement quand c'est techniquement
   nécessaire (TTS natif). Pas de microservices (§70 du brief).
2. **Le cœur ne connaît ni l'UI, ni Windows, ni le réseau.** `Optimus.Core` est une bibliothèque
   pure, testable en CI sans micro, sans clavier et sans jeu.
3. **Un pipeline, une direction.** Le flux va toujours de la voix vers le jeu ; aucune couche
   basse ne rappelle une couche haute autrement que par événement.
4. **Tout ce qui est variable est une donnée**, pas du code : keybinds, phrases, réponses,
   personnalités, catégories, providers.
5. **Le point de contrôle est unique.** Toute exécution passe par le `CommandExecutor`, qui est le
   seul à pouvoir parler à l'`InputEngine`. C'est là que vivent permissions, kill switch,
   simulation, cooldowns et journalisation.

---

## 4.2 Vue en couches

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  PRESENTATION                                                                │
│  Optimus.App (WPF)          Optimus.Bridge (API locale)     Optimus.Link     │
│  Dashboard, Commands,       REST + WebSocket 127.0.0.1      (bot Discord)    │
│  Keybinds, Copilots, …      auth token                      appairage local  │
└───────────────┬──────────────────────┬───────────────────────────┬──────────┘
                │  ViewModels/Commands │  DTO                      │  Intent
                ▼                      ▼                           ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  APPLICATION  —  Optimus.Core.Orchestration                                  │
│                                                                              │
│   VoicePipeline ──► IntentResolver ──► CommandExecutor ──► ResponseComposer   │
│        ▲                  ▲                   │                    │         │
│        │                  │                   ▼                    ▼         │
│   SessionState      ContextManager      ExecutionGuard         Personality    │
│                                        (perms, killswitch,       Engine       │
│                                         simulation, cooldown)                 │
└───────┬──────────────┬───────────────┬────────────────┬──────────────┬───────┘
        ▼              ▼               ▼                ▼              ▼
┌───────────────┬───────────────┬───────────────┬──────────────┬──────────────┐
│ DOMAINE       │ DOMAINE       │ DOMAINE       │ DOMAINE      │ DOMAINE      │
│ Commands      │ Bindings      │ Copilots      │ Profiles     │ Plugins      │
│ Command,      │ BindingProfile│ Copilot,      │ UserProfile, │ IOptimusPlug.│
│ Action, Macro,│ Binding,      │ Personality,  │ AppSettings  │ PluginHost   │
│ Response      │ InputSpec     │ VoiceConfig   │              │ Permissions  │
└───────┬───────┴───────┬───────┴───────┬───────┴──────┬───────┴──────┬───────┘
        ▼               ▼               ▼              ▼              ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  INFRASTRUCTURE  (adaptateurs — seuls à toucher l'OS et le réseau)           │
│                                                                              │
│  Audio          Speech            Synthesis      AI            Input         │
│  WasapiCapture  WhisperProvider   SapiProvider   OllamaClient  SendInputEngine│
│  VadDetector    WindowsSpeechProv PiperProvider  OpenAIClient  SimulatedEngine│
│  DeviceWatcher  CloudSttProvider  ElevenLabsProv (optionnels)  HotkeyService  │
│                                                                              │
│  Game                     Storage                  Diagnostics               │
│  ProcessDetector          SqliteRepository         Serilog sinks             │
│  ForegroundWatcher        JsonConfigStore          TraceRecorder             │
│  ScActionMapImporter      FileWatcher              MetricsCollector          │
│  GameStateProvider(stub)                                                     │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Règle de dépendance** : les flèches ne remontent jamais. `Domaine` ne dépend de rien,
`Application` dépend du domaine et d'**interfaces** d'infrastructure, `Infrastructure` implémente
ces interfaces, `Présentation` ne dépend que d'`Application`. Un test d'architecture
(NetArchTest) échoue la CI si la règle est violée.

---

## 4.3 Responsabilité de chaque composant

### Couche voix

| Composant | Responsabilité | Ne fait PAS |
|---|---|---|
| `WasapiCapture` | Ouvre le périphérique d'entrée, produit des trames PCM 16 kHz mono | Décider quand c'est de la parole |
| `VadDetector` | Découpe le flux en *utterances* (début/fin de parole, seuil, silence de fin) | Transcrire |
| `WakeWordDetector` | Signale la présence du mot d'éveil | Transcrire la phrase entière |
| `ISpeechToTextProvider` | `Transcribe(audio, lang) → TranscriptionResult{text, confidence, segments}` | Interpréter le sens |
| `VoicePipeline` | Orchestre capture → VAD → (wake/PTT) → STT, émet `UtteranceRecognized` | Exécuter quoi que ce soit |

### Couche compréhension

| Composant | Responsabilité |
|---|---|
| `TextNormalizer` | Minuscules, accents, ponctuation, nombres en toutes lettres → chiffres, mots parasites (« euh », « s'il te plaît ») |
| `PhraseIndex` | Index inversé des `voice_phrases` de toutes les commandes activées du copilote courant |
| `FastIntentMatcher` | Exact → préfixe/normalisé → flou (token-set + Levenshtein). Retourne `IntentCandidate[]` scorés |
| `ContextManager` | `ConversationContext` (N derniers tours, slot ouvert), `GameContext`, `CopilotContext`, `UserContext` |
| `SlotFiller` | Complète les paramètres manquants depuis le contexte ou déclenche une relance |
| `ILlmProvider` (optionnel) | Reçoit texte + **liste blanche d'intents** + contexte, renvoie **uniquement** `{intent, parameters, confidence}` en JSON contraint |
| `IntentResolver` | Arbitre : matcher local vs LLM vs relance vs échec. Sortie unique : `ResolvedIntent` |

### Couche exécution

| Composant | Responsabilité |
|---|---|
| `CommandRegistry` | Charge/valide/indexe les commandes ; source de vérité de la liste blanche |
| `ExecutionGuard` | Kill switch, mode simulation, focus jeu, permissions, cooldown, confirmation des actions `dangerous` |
| `BindingResolver` | `(action_id, actionmap) → InputSpec` depuis le `BindingProfile` actif ; échoue proprement si non lié |
| `SequenceRunner` | Interprète les étapes (`key`, `wait`, `mouse`, `repeat`, `if`), garantit le relâchement des touches en `finally` |
| `IInputEngine` | `SendInputEngine` (scancodes) ou `SimulatedInputEngine` (journalise) |
| `CommandExecutor` | Point de contrôle unique : guard → resolve → run → résultat → historique |

### Couche personnalité et réponse

| Composant | Responsabilité |
|---|---|
| `PersonalityEngine` | Sélectionne la variante de réponse selon les traits, l'état (combat, échec) et l'historique récent (anti-répétition) |
| `ResponseComposer` | Interpole les variables (`{ship}`, `{value}`), applique vocabulaire/phrases interdites, borne la longueur |
| `ITextToSpeechProvider` | Synthétise ; expose voix, vitesse, pitch |
| `AudioPlayer` | File d'attente, priorité (une alerte coupe une réplique de lore), ducking, barge-in |

### Couche système

| Composant | Responsabilité |
|---|---|
| `GameProcessDetector` | Présence de `StarCitizen.exe`, version/canal, chemin d'installation |
| `ForegroundWatcher` | Le jeu est-il au premier plan (condition d'exécution par défaut) |
| `ScActionMapImporter` | Parse `defaultProfile.xml` + `layout_*.xml`, produit un `BindingProfile` |
| `IGameStateProvider` | Interface d'état de jeu. v0.1 : implémentation *déclarative* (mode posé par la voix). V2 : parsing `Game.log`, OCR, ou API si elle existe |
| `HotkeyService` | Raccourcis globaux (PTT, kill switch, mute) via `RegisterHotKey`/hook bas niveau |

---

## 4.4 Modèle de processus

```
┌──────────────────────────────── Optimus.exe (1 processus) ────────────────────────────────┐
│                                                                                            │
│  UI thread (WPF)   Audio thread (temps réel)   Worker pool          Background services    │
│  ─────────────     ────────────────────────    ───────────          ───────────────────    │
│  Rendu, bindings   WASAPI + VAD                STT (Whisper)        Bridge (Kestrel loopb.) │
│  Dispatcher        buffer circulaire           Intent + exécution   Discord (WebSocket)     │
│  jamais bloqué     zéro allocation en boucle   TTS                  Watchers fichiers/proc  │
│                                                                                            │
│  Bus d'événements interne (Channel<T> / IObservable) — pas d'appels croisés directs         │
└────────────────────────────────────────────────────────────────────────────────────────────┘
              │ stdin/stdout ou pipe nommé                      │ HTTP 127.0.0.1
              ▼                                                  ▼
      piper.exe (TTS neural, optionnel)               Tablette / navigateur / overlay
```

**Pourquoi ce découpage :**
- Le thread audio ne doit **jamais** attendre un disque, un modèle ou le réseau → il ne fait que
  remplir un buffer et lever des événements.
- L'inférence STT est CPU-lourde → pool de workers, une seule inférence à la fois, annulable.
- Le TTS neural (Piper) est un binaire natif → sidecar process, redémarrable, son crash est sans
  conséquence (fallback SAPI).
- Un crash du bot Discord ou du Bridge ne doit pas tuer la boucle voix → services hébergés
  indépendants avec politique de redémarrage.

---

## 4.5 Flux nominal (séquence détaillée)

```
Utilisateur : « Optimus, ouvre les portes »

 t+0     WasapiCapture      trames 20 ms ──────────────────────────────────┐
 t+0     VadDetector        speech_start                                    │ trace_id = 7f3a
 t+1180  VadDetector        speech_end (silence 280 ms)                     │
 t+1185  VoicePipeline      utterance 1.18 s → STT                          │
 t+1490  WhisperProvider    "optimus ouvre les portes"  conf 0.94           │
 t+1492  WakeWordFilter     préfixe "optimus" trouvé → payload "ouvre les portes"
 t+1493  TextNormalizer     "ouvre les portes"
 t+1495  FastIntentMatcher  exact → ship.doors.toggle   score 1.00
 t+1496  ExecutionGuard     killswitch=off · simulation=off · SC foreground=true
                            · permission=local · cooldown ok · dangerous=false
 t+1497  BindingResolver    spaceship_general/v_toggle_all_doors → { key=L, mods=[], tap }
 t+1499  SequenceRunner     scancode 0x26 down → 45 ms → up
 t+1545  PersonalityEngine  variante « Compartiments déverrouillés. » (military 80, humour 30)
 t+1548  TtsProvider        synthèse (streaming) → premier échantillon audible
 t+1620  AudioPlayer        lecture
 t+1621  HistoryRepository  entrée persistée (phrase, intent, score, binding, 128 ms d'exécution)
```

**Latence perçue = t+1499 − t+1180 ≈ 320 ms** entre la fin de la parole et l'appui touche.
C'est l'objectif RNF-01. Le TTS arrive *après* l'action : on n'attend jamais la voix pour agir.

---

## 4.6 Chemins alternatifs

```
                       ┌──────────────────────────────┐
   texte normalisé ───►│ FastIntentMatcher (local)    │
                       └───────┬──────────────┬───────┘
                score ≥ 0.85   │              │  0.55 ≤ score < 0.85
                               ▼              ▼
                        exécution      ┌───────────────────────┐
                        immédiate      │ plusieurs candidats ? │
                                       └───┬───────────────┬───┘
                                     oui   │               │ non
                                           ▼               ▼
                              relance de désambiguïsation  confirmation
                              « Boucliers avant ou         « Vous voulez dire … ? »
                                arrière ? »
                       score < 0.55
                               │
                               ▼
                    ┌──────────────────────┐   LLM désactivé
                    │ LLM activé ?         ├──────────────► « Je ne connais pas
                    └──────────┬───────────┘                  cette commande. »
                               │ oui                           + log unknown_phrase
                               ▼
                    ┌────────────────────────────────────┐
                    │ LLM : texte + liste blanche + ctx  │
                    │ → { intent, parameters, confidence}│
                    └──────────┬─────────────────────────┘
                               ▼
                    intent ∈ liste blanche ?  ── non ──► rejet + log de sécurité
                               │ oui
                               ▼
                    conversation pure ? ──► réponse TTS seule (aucune action)
                               │
                               ▼
                    ExecutionGuard → … (chemin nominal)
```

---

## 4.7 Isolation par utilisateur (exigences §81–83)

```
   PC de A                                   PC de B
┌──────────────────────────┐              ┌──────────────────────────┐
│ Optimus A                │              │ Optimus B                │
│  binds A · copilote A    │              │  binds B · copilote B    │
│  historique A            │              │  historique B            │
│  InputEngine ─► clavier A│              │  InputEngine ─► clavier B│
│         ▲                │              │         ▲                │
│         │ intent validé  │              │         │                │
│  ┌──────┴───────┐        │              │  ┌──────┴───────┐        │
│  │ Bridge local │        │              │  │ Bridge local │        │
│  │ 127.0.0.1    │        │              │  │ 127.0.0.1    │        │
│  └──────▲───────┘        │              │  └──────▲───────┘        │
└─────────┼────────────────┘              └─────────┼────────────────┘
          │ WebSocket SORTANT, appairé, chiffré     │
          └──────────────┬──────────────────────────┘
                         │
                 ┌───────┴────────┐   ne détient QUE des intents et des états,
                 │ Discord / relai│   jamais de touches, jamais d'accès entrant
                 └────────────────┘
```

Trois invariants non négociables :
1. **La frappe est toujours produite localement**, par le processus de l'utilisateur, sur sa
   machine. Rien d'autre ne peut appeler `SendInput`.
2. **La connexion est sortante** : aucune instance n'écoute sur le réseau ; le relais ne peut pas
   « pousser » vers une machine non appairée.
3. **L'appairage est explicite et local** : code à usage unique généré dans l'app, saisi côté
   Discord, révocable en un clic, avec `execute_commands = false` par défaut.

Mode par défaut recommandé pour la V1 : **bot Discord local** (l'utilisateur fournit son propre
token) — zéro serveur central, isolation garantie par construction. Le mode relais n'apparaît
qu'en V2, et reste optionnel.

---

## 4.8 Nomenclature retenue

| Élément | Nom | Rôle |
|---|---|---|
| Produit | **Optimus** | La plateforme |
| Application desktop | **Optimus Command Center** (`Optimus.App`) | L'interface |
| Moteur | **Optimus Core** (`Optimus.Core`) | Pipeline, domaine, orchestration |
| API locale | **Optimus Bridge** (`Optimus.Bridge`) | REST + WS loopback |
| Intégration Discord | **Optimus Link** (`Optimus.Link`) | Bot + appairage |
| SDK plugins | **Optimus SDK** (`Optimus.Sdk`) | Contrats publics stables |
| Pack de copilote | **`.optcopilot`** | Archive ZIP signée |
| Un copilote | **Copilot** | Optimus, Synthia, Virgil, Atlas… |

> Remarque : *Optimus* est aussi une marque forte associée à une franchise connue. Pour une
> diffusion publique, prévoir une vérification de disponibilité du nom, ou un nom de produit
> distinct avec « Optimus » comme nom du copilote par défaut. Point à trancher avant le
> packaging public — sans impact sur l'architecture.
