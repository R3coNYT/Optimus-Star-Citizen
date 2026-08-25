# PHASE 4 — Stack technique

## 5.1 Choix du socle applicatif

| Critère (pondération) | .NET 8 + **WPF** | .NET 8 + WinUI 3 | Electron | Tauri (Rust) | Python + Qt |
|---|---|---|---|---|---|
| RAM au repos (×3) | ~120 Mo ✅ | ~150 Mo ✅ | 300–450 Mo ❌ | ~70 Mo ✅✅ | ~180 Mo 🟡 |
| Impact FPS à côté du jeu (×3) | négligeable ✅ | négligeable ✅ | GPU partagé ❌ | négligeable ✅ | négligeable ✅ |
| Audio bas niveau (×3) | WASAPI/NAudio, mature ✅ | idem ✅ | via natif ❌ | cpal, correct 🟡 | sounddevice ✅ |
| Injection clavier scancode (×3) | P/Invoke direct ✅ | idem ✅ | node-ffi fragile ❌ | winapi crate ✅ | pywin32/ctypes 🟡 |
| Latence / GC (×2) | GC serveur maîtrisable ✅ | idem ✅ | ❌ | pas de GC ✅✅ | GIL ❌ |
| Démarrage à froid (×2) | 1–2 s ✅ | 2–4 s 🟡 | 2–4 s 🟡 | < 1 s ✅✅ | 3–6 s ❌ |
| Tray, hotkeys globaux, single-instance (×2) | trivial ✅ | contraintes packaging 🟡 | plugins ✅ | ok ✅ | ok ✅ |
| Écosystème STT/TTS/IA (×2) | Whisper.net, ONNX Runtime ✅ | idem ✅ | via sidecars 🟡 | whisper-rs 🟡 | **le meilleur** ✅✅ |
| Discord (×1) | Discord.Net ✅ | idem ✅ | discord.js ✅ | serenity ✅ | discord.py ✅ |
| Vitesse de dev UI sci-fi (×2) | XAML + styles 🟡 | XAML 🟡 | HTML/CSS ✅✅ | HTML/CSS ✅✅ | QML 🟡 |
| Packaging / MAJ auto (×2) | Velopack ✅ | MSIX contraignant 🟡 | electron-updater ✅ | intégré ✅ | PyInstaller ❌ |
| Maturité / risque projet (×3) | très faible ✅ | moyen 🟡 | faible ✅ | moyen 🟡 | élevé ❌ |
| **Verdict** | **RETENU** | écarté | écarté | 2ᵉ choix | écarté comme socle |

### Décision : **C# / .NET 8 + WPF**

**Justification en trois points :**
1. **C'est la seule pile qui coche les trois contraintes dures simultanément** : audio temps réel
   de qualité, P/Invoke natif sans friction pour `SendInput`/hooks/`RegisterHotKey`, et empreinte
   compatible avec un jeu qui consomme déjà 16 Go de RAM et 100 % du GPU.
2. **Le risque projet est le plus faible.** WPF est stable depuis 15 ans, la documentation Win32
   en C# est pléthorique, et le pipeline IA existe désormais en .NET pur (Whisper.net, ONNX
   Runtime) — ce qui **élimine la dépendance à Python**, principal facteur de complexité de
   packaging pour ce type d'app.
3. **Tauri était le concurrent sérieux** (empreinte inférieure, UI sci-fi bien plus rapide à
   produire en CSS). Il est écarté sur la vélocité globale : deux langages (Rust + TS), un
   écosystème IA/Discord/Windows moins fourni, et une courbe qui pénaliserait précisément les
   parties les plus longues du projet (moteur de commandes, keybinds, plugins). **Si l'UI sci-fi
   devient le goulot d'étranglement**, la porte de sortie existe sans rien casser : `Optimus.Core`
   et `Optimus.Bridge` sont indépendants de l'UI, une coquille web pourrait consommer le Bridge.

**Modernisation de l'UI WPF** : `CommunityToolkit.Mvvm` (MVVM sans boilerplate),
`WPF-UI` ou `HandyControl` comme base de contrôles, thème custom « avionique » par-dessus,
`LiveChartsCore`/`ScottPlot` pour les graphes d'analytics, `H.NotifyIcon.Wpf` pour le tray.

---

## 5.2 Décisions par composant (format ADR condensé)

| # | Composant | Décision | Justification | Alternative gardée en réserve |
|---|---|---|---|---|
| 1 | **Langage** | C# 12 / .NET 8 LTS, `nullable enable`, publication self-contained x64 | Zéro prérequis pour l'utilisateur, LTS jusqu'en 2026+ | .NET 9/10 quand LTS |
| 2 | **Desktop** | WPF + MVVM | cf. §5.1 | Tauri via le Bridge |
| 3 | **Audio in** | **NAudio** (WASAPI capture, événementiel, 16 kHz mono) | Référence .NET, faible latence, gestion du hot-plug | CSCore |
| 4 | **VAD** | **Silero VAD** en ONNX via `Microsoft.ML.OnnxRuntime` | Bien meilleur que le seuil d'énergie en environnement bruyant (ventilos, jeu, TS/Discord) ; ~1 Mo, < 1 ms/trame | WebRTC VAD (fallback simple, sans modèle) |
| 5 | **STT** | **Whisper.net** (whisper.cpp) — modèle `small` quantisé Q5 par défaut, `medium` optionnel | Local, offline, multilingue, excellent en français ; pas de Python ; GPU optionnel (CUDA/Vulkan) | `faster-whisper` en sidecar Python pour les GPU récents ; provider cloud (OpenAI/Azure) via l'interface |
| 6 | **Wake word** | v0.1 : préfixe détecté dans la transcription (coût nul). V1 : **openWakeWord** en ONNX, modèle « optimus » entraîné | Sans dépendance commerciale ni compte | **Picovoice Porcupine** (meilleure qualité, mais licence/compte à valider avant intégration) |
| 7 | **TTS** | v0.1 : **Windows OneCore** (`Windows.Media.SpeechSynthesis`) — voix FR natives, zéro installation. Option : **Piper** (ONNX, sidecar) pour une voix neurale locale | Deux niveaux : ça marche immédiatement, et ça devient beau si on le veut | ElevenLabs / Azure / OpenAI TTS via `ITextToSpeechProvider` (cloud, opt-in) |
| 8 | **LLM** | **Optionnel**, désactivé par défaut. Interface `ILlmProvider` ; implémentations : Ollama (local), OpenAI-compatible (OpenAI, OpenRouter, LM Studio), Anthropic | §84 du brief : rien ne doit dépendre du cloud. Sortie **JSON contrainte** (grammar/JSON mode) + validation liste blanche | — |
| 9 | **Matching flou** | **FuzzySharp** (ratio token-set) + Levenshtein pondéré ; V1 : embeddings ONNX (`multilingual-e5-small`) pour le rappel sémantique | Déterministe, testable, < 5 ms sur 1 000 phrases | — |
| 10 | **Injection** | **`SendInput`** avec `KEYEVENTF_SCANCODE` (P/Invoke), souris incluse, **table de scancodes fixe en positions US** (jamais `MapVirtualKey`), `timeBeginPeriod(1)` pendant les séquences | **Mesuré au spike S0-1** : une injection virtual-key seule arrive dans le Raw Input avec `MakeCode = 0x00`, donc invisible pour un moteur lisant le scancode ; et `MapVirtualKey` renvoie des scancodes faux en AZERTY | Pilote **Interception** en plugin si un cas résiste (installe un driver → jamais dans le MVP) |
| 11 | **Détection du jeu** | `Process.GetProcessesByName("StarCitizen")` + `GetForegroundWindow`/`GetWindowThreadProcessId` ; chemin via launcher/processus/scan/saisie manuelle | Sans chemin en dur (§59) | WMI `Win32_ProcessStartTrace` pour l'événementiel |
| 12 | **Keybinds SC** | Parser XML dédié (`System.Xml.Linq`) : `defaultProfile.xml` (défauts, extrait de `Data.p4k` via **unp4k**) ⊕ `layout_*.xml` (deltas) | cf. `docs/02` | Préréglages embarqués par version si `Data.p4k` inaccessible |
| 13 | **Base de données** | **SQLite** (`Microsoft.Data.Sqlite` + **Dapper**), mode WAL | Historique, analytics, cache d'embeddings, appairages. Léger, embarqué, pas d'ORM lourd | EF Core si les migrations deviennent complexes |
| 14 | **Configuration** | **JSON canonique** (`System.Text.Json`, source-generated) + **schémas JSON** validés au chargement ; **YAML accepté** en import/export (`YamlDotNet`) | JSON = outillage, diff, validation stricte. YAML = confort d'édition manuelle. Les deux, pas l'un contre l'autre | — |
| 15 | **API locale** | **ASP.NET Core Minimal API** hébergé dans le processus, **Kestrel lié à `127.0.0.1`**, token bearer, WebSocket pour le temps réel | Un seul processus, pas de service Windows, surface d'attaque minimale | gRPC si un client natif l'exige |
| 16 | **Discord** | **Discord.Net** (slash commands, embeds), mode « bot local » par défaut (token utilisateur) | Isolation garantie sans serveur central (§82) | DSharpPlus ; mode relais en V2 |
| 17 | **Logs** | **Serilog** : console + fichier rotatif quotidien (30 j) + sink UI en mémoire ; format structuré avec `trace_id` | Corrélation d'une phrase de bout en bout | OpenTelemetry en V2 |
| 18 | **Plugins** | `AssemblyLoadContext` collectible + manifeste de permissions + SDK versionné (`Optimus.Sdk`) | Chargement/déchargement à chaud, dépendances isolées | Scripts C# (Roslyn) ou Lua pour les plugins « légers » en V2 |
| 19 | **Tests** | xUnit + FluentAssertions + NetArchTest (règles de dépendance) + Verify (snapshots de séquences) + Testcontainers non requis | cf. `docs/13` | — |
| 20 | **Packaging / MAJ** | **Velopack** (installeur + mises à jour delta signées) ; option Inno Setup pour un installeur classique | Simple, moderne, MAJ auto sans MSIX ni store | MSIX écarté (contraintes sur hooks globaux et chemins) |
| 21 | **CI** | GitHub Actions : build, tests, lint de catalogue, publication self-contained, release Velopack | | |

---

## 5.3 Points de vigilance techniques sur ces choix

| Sujet | Risque | Mitigation |
|---|---|---|
| **Anti-triche** | Star Citizen embarque une protection anti-triche. Les entrées synthétiques *peuvent* être filtrées ou considérées comme suspectes. VoiceAttack fonctionne aujourd'hui, ce qui est un indice fort — **pas une garantie**. | **À valider en tout premier, avant toute autre ligne de code** (spike n°1, cf. `docs/13`). Plan B : pilote Interception ; plan C : périphérique HID émulé. Ne jamais implémenter d'automatisation continue (aim, farming) : 1 phrase = 1 action délibérée. |
| **Élévation** | Si SC tourne en administrateur, une app non élevée ne peut pas lui envoyer d'entrées (UIPI). | Détecter le cas et le dire clairement ; proposer un mode élevé opt-in. |
| **Plein écran exclusif** | Les hotkeys globaux et l'overlay se comportent différemment. | Recommander « plein écran fenêtré » ; tester les deux. |
| **Whisper `small` sur CPU faible** | Transcription > 1 s → latence hors cible. | Sélection auto du modèle selon le CPU/GPU au premier lancement + benchmark intégré ; `tiny`/`base` en repli. |
| **Micro capté par le jeu et Discord** | Conflits de périphérique. | WASAPI en mode partagé (jamais exclusif) ; sélection explicite du périphérique. |
| **Voix TTS entendue par le micro** (boucle) | Auto-déclenchement. | Ducking + suppression de la fenêtre TTS dans le VAD + barge-in maîtrisé. |
| **Modèles volumineux dans l'installeur** | Installeur de plusieurs centaines de Mo. | Installeur léger + **téléchargement du modèle au premier lancement**, avec choix de la taille. |
