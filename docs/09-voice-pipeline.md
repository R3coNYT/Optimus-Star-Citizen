# PHASE 8 — Architecture vocale et budget de latence

## 9.1 Chaîne complète

```
 ┌──────────┐   PCM 16 kHz mono, trames de 20 ms
 │ MICRO    │──────────────┐
 └──────────┘              ▼
                    ┌─────────────┐  buffer circulaire de 3 s (pré-roll)
                    │ AudioBuffer │  → permet de récupérer le début de phrase
                    └──────┬──────┘     même si le déclenchement arrive tard
                           ▼
         ┌────────────────────────────────────┐
         │ Déclencheur (3 modes exclusifs)    │
         │  • PTT      : touche maintenue     │  ← le plus fiable, défaut du MVP
         │  • Wake word: "Optimus" détecté    │
         │  • Always   : VAD seul             │
         └──────┬─────────────────────────────┘
                ▼
         ┌─────────────┐  Silero VAD : speech_start / speech_end
         │     VAD     │  silence de fin 280 ms (paramétrable 150–600)
         └──────┬──────┘  durée min 250 ms · durée max 12 s (garde-fou)
                ▼
         ┌─────────────┐  Whisper small Q5, langue forcée (pas d'autodétection : +150 ms)
         │     STT     │  prompt d'amorçage = vocabulaire du domaine
         └──────┬──────┘  (noms de vaisseaux, « quantum », « boucliers », « mobiGlas »…)
                ▼
         ┌──────────────────┐  retire le wake word, normalise
         │   NORMALISATION  │
         └──────┬───────────┘
                ▼
         ┌─────────────┐  exact → flou → (LLM optionnel)
         │   INTENT    │
         └──────┬──────┘
                ├──────────────► EXÉCUTION (prioritaire, ne dépend jamais du TTS)
                ▼
         ┌─────────────┐  personnalité → variante → interpolation
         │  RÉPONSE    │
         └──────┬──────┘
                ▼
         ┌─────────────┐  streaming si le provider le permet
         │     TTS     │
         └──────┬──────┘
                ▼
         ┌─────────────┐  file d'attente priorisée, ducking, barge-in
         │  PLAYBACK   │
         └─────────────┘
```

**Décision structurante : l'action ne dépend jamais du TTS.** On appuie sur la touche, *puis* on
parle. Un TTS lent dégrade le confort, jamais la réactivité du jeu.

---

## 9.2 Budget de latence

Mesuré depuis `speech_end` (fin de parole détectée) jusqu'à l'événement clavier.

| Étape | Cible p50 | Cible p95 | Comment on la tient |
|---|---|---|---|
| VAD → décision de fin | 280 ms | 350 ms | silence de fin paramétrable ; PTT = 0 ms (fin au relâchement) |
| Transcription (Whisper small, 1,5 s d'audio, CPU 8 cœurs) | 250 ms | 500 ms | modèle quantisé, `n_threads` adapté, contexte réduit, pas d'autodétection de langue |
| Normalisation + matching local | 3 ms | 10 ms | index en mémoire, aucune I/O |
| Guard + résolution de binding | 1 ms | 3 ms | tables en mémoire |
| Injection (tap 45 ms) | 45 ms | 50 ms | `SendInput` direct |
| **Total voix → touche** | **≈ 580 ms** | **≈ 900 ms** | **conforme à RNF-01** |
| *(bonus)* premier son du TTS | **7 ms** | **15 ms** | **Mesuré (S0-5)** : RTF 0,001–0,003 avec les voix OneCore. Le TTS est hors sujet côté latence — à condition de préchauffer le moteur (429 ms sur la toute première synthèse) |
| *(si LLM local Ollama 7B)* | +400 à 900 ms | — | uniquement sur échec du matcher local |
| *(si LLM cloud)* | +600 à 2 000 ms | — | jamais sur le chemin des commandes connues |

### La voix neuronale locale (Piper)

Les voix Windows sont irréprochables en latence et discutables en timbre. Piper renverse
exactement ce rapport, et le pilote choisit lequel des deux compromis il préfère —
`voice.provider` dans le fichier du copilote, ou la case dans les réglages.

**Locale au sens fort** : le modèle tourne sur la machine du pilote. Rien ne part sur le réseau,
Optimus reste utilisable hors ligne, et ce que dit le copilote ne parvient à personne. C'est ce
qui distingue Piper d'un service de synthèse en ligne, dont le timbre serait peut-être meilleur.

#### Mesures du 2026-08-27 (12 cœurs, modèles français)

| | Chargement de la voix | Synthèse par réplique | Facteur temps réel |
|---|---|---|---|
| Voix Windows OneCore | 429 ms, une fois (D23) | **7 à 15 ms** | 0,003 |
| Piper `fr_FR-tom-medium` | **620 à 785 ms** | **377 à 455 ms** | 0,113 |
| Piper `fr_FR-gilles-low` | **318 ms** | **214 ms** | 0,047 |

Piper coûte donc environ **quarante fois** plus qu'une voix Windows par réplique. Ce qui rend
l'échange acceptable est écrit plus haut, et c'est la décision structurante du pipeline :
**l'action ne dépend jamais du TTS**. La touche est déjà partie quand Optimus commente. Le délai
porte sur le commentaire, jamais sur la réactivité du jeu.

Une voix `low` divise l'attente par deux pour un timbre à peine moins riche : c'est le réglage à
essayer avant de renoncer.

#### Un processus persistant, et pourquoi

Relancer `piper.exe` à chaque phrase ferait payer le chargement du modèle — 0,6 s — **avant
chaque mot**, soit près d'une seconde d'attente par réplique. Le processus reste donc ouvert, la
voix chargée, et le préchauffage du démarrage attend réellement l'annonce de disponibilité :
sans cette attente, la première réplique payait 740 ms, ce qui vidait D23 de son sens.

Le protocole retenu est celui qui a été vérifié : **une ligne de texte** sur l'entrée standard,
**un chemin de fichier WAV** sur la sortie standard, les journaux sur l'erreur standard. Le mode
`--json-input` a été essayé et écarté — il ignore `length_scale` dans cette version, ce qui
aurait rendu le réglage de débit inopérant sans que rien ne le signale.

#### Installation

Piper n'est pas livré avec Optimus : 22 Mo de binaire et 63 Mo par voix, pour une fonction dont
on peut se passer. L'installation vit dans `%APPDATA%\Optimus\piper`, hors de `data/` que le
script de publication remplace (même principe que D35, D43 et D46).

```
%APPDATA%\Optimus\piper\
├── piper.exe            (+ ses DLL et espeak-ng-data, tels que livrés dans l'archive)
└── voices\
    ├── fr_FR-tom-medium.onnx
    └── fr_FR-tom-medium.onnx.json
```

Le binaire vient des versions publiées de `rhasspy/piper` (`piper_windows_amd64.zip`), les voix
de `huggingface.co/rhasspy/piper-voices` — chaque voix étant un `.onnx` **et** son `.onnx.json`,
les deux étant nécessaires. Optimus n'accepte l'installation que si les deux sont là : un Piper
sans modèle est une installation à moitié faite, et la retenir rendrait le copilote muet le
temps que le pilote comprenne pourquoi.

**Cette installation est propre à chaque machine.** Le dossier ne suit pas la publication : sur
un second poste, il faut soit le recopier, soit décocher la case — auquel cas les voix Windows
reprennent la main, avec une ligne de journal qui le dit.

#### Rien ne peut rendre le copilote muet

Piper est un processus externe : un antivirus qui le tue, un modèle corrompu, un disque plein.
Les voix Windows, elles, sont toujours là. Le moteur principal est donc doublé, et **abandonné
après deux échecs consécutifs** — réessayer indéfiniment ferait payer son délai d'attente à
chaque réplique, ce qui serait bien pire qu'un changement de timbre. Le pilote entend une autre
voix, ce qui est un signal en soi, et le journal dit pourquoi.

---

### Optimisations prévues

| Technique | Gain | Complexité |
|---|---|---|
| **Pré-roll de 3 s** : on transcrit depuis avant le déclenchement | évite les débuts coupés (première cause d'échec de reconnaissance). **Mesuré (S0-3) : ouvrir le périphérique de capture coûte 419 ms** — ouvrir au moment du PTT ferait perdre le premier tiers de seconde de chaque phrase | faible |
| **PTT par défaut au MVP** | supprime les 280 ms de VAD et les faux déclenchements | nulle |
| **Modèle chargé et « chauffé » au démarrage** | évite 800 ms sur la 1ʳᵉ commande | faible |
| **Transcription incrémentale** (décoder pendant que l'utilisateur parle) | −100 à 200 ms | moyenne, V1 |
| **Cache des réponses TTS fréquentes** (WAV en cache par hash de texte + voix) | premier son quasi instantané sur les 30 réponses les plus utilisées | faible, gros effet perçu |
| **Bip d'accusé de réception** (10 ms, immédiat au déclenchement) | latence *perçue* divisée, coût nul | trivial — à faire dès le MVP |
| **Choix auto du modèle** par benchmark au 1ᵉʳ lancement | évite de mettre `medium` sur un CPU faible | faible |

---

## 9.3 Modes d'écoute

| Mode | Déclenchement | Fin | Avantages | Inconvénients | Statut |
|---|---|---|---|---|---|
| **Push-to-talk** | touche maintenue | relâchement | zéro faux positif, latence minimale, robuste au bruit | occupe une touche/un doigt | **défaut MVP** |
| **Wake word** | « Optimus » | VAD | mains libres, immersif | faux positifs, coût CPU permanent | MVP dégradé, V1 natif |
| **Always listening** | VAD seul | VAD | le plus naturel | tout ce que vous dites est transcrit (vocal Discord !) | V1, opt-in explicite |
| **Toggle** | touche = on/off | touche | compromis | oubli possible | V1 |

**Wake word en v0.1 (mode dégradé, coût nul)** : on transcrit l'utterance complète et on vérifie
que le texte commence par le wake word (avec tolérance floue : « optimus », « optimousse »,
« optimus, », « ok optimus »). Coût : une transcription inutile quand ce n'était pas pour lui —
acceptable en PTT, discutable en always-on, d'où la priorité au détecteur natif en V1.

---

## 9.4 Interfaces des providers

```csharp
public interface ISpeechToTextProvider : IAsyncDisposable
{
    string Id { get; }                       // "whisper-local", "windows-speech", "azure"
    SttCapabilities Capabilities { get; }     // langues, streaming, offline, GPU
    Task InitializeAsync(SttOptions o, CancellationToken ct);
    Task<TranscriptionResult> TranscribeAsync(AudioSegment audio, CancellationToken ct);
    IAsyncEnumerable<PartialTranscription> StreamAsync(IAsyncEnumerable<AudioFrame> f, CancellationToken ct);
}

public interface ITextToSpeechProvider : IAsyncDisposable
{
    string Id { get; }
    Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken ct);
    Task<SynthesisResult> SynthesizeAsync(SynthesisRequest r, CancellationToken ct); // stream possible
}

public interface ILlmProvider
{
    string Id { get; }
    Task<StructuredIntent> ResolveIntentAsync(IntentRequest r, CancellationToken ct);
    Task<string> ChatAsync(ChatRequest r, CancellationToken ct);
}

public interface IVoiceActivityDetector { VadEvent Process(ReadOnlySpan<float> frame); }
public interface IWakeWordDetector      { bool Process(ReadOnlySpan<float> frame, out float score); }
```

**Politique de repli automatique** : chaque provider déclare un fallback. En cas d'échec
d'initialisation ou de 3 erreurs consécutives, Optimus bascule sur le repli, l'annonce
visuellement (pastille orange sur le dashboard) et le journalise. Il ne se met jamais en panne
silencieuse.

---

## 9.5 Traitement audio et pièges connus

| Problème | Solution retenue |
|---|---|
| **La voix du TTS déclenche Optimus** | Fenêtre de suppression : le VAD ignore l'entrée pendant la lecture TTS + 200 ms (et non pas simple AEC, inutilement complexe) |
| **Le jeu ou Discord occupe le micro** | WASAPI en mode **partagé** exclusivement ; jamais de mode exclusif |
| **Bruit de ventilateur / clavier mécanique** | VAD neural (Silero) plutôt qu'un seuil d'énergie ; gain d'entrée normalisé (AGC douce) |
| **Micro débranché / changé en cours de session** | `MMDeviceEnumerator` + notification de changement → reconnexion auto, message UI |
| **Plusieurs personnes parlent (Discord en fond)** | PTT résout tout ; en always-on, seuil de confiance STT relevé + rejet des phrases sans wake word |
| **Sortie audio à router vers OBS/stream** | Choix du périphérique de sortie par copilote ; support des câbles virtuels |
| **Phrase coupée au début** | Pré-roll de 3 s (voir §9.2) |
| **Termes du jeu mal transcrits** (« quantum », « Crusader », « mobiGlas ») | `initial_prompt` Whisper contenant le lexique du domaine + post-correction par dictionnaire phonétique du jeu |

---

## 9.6 Mode debug de la chaîne vocale (§23)

```
┌─ PIPELINE TRACE ──────────────────────── trace 7f3a ── 21:42:15.318 ─┐
│ MICRO      Realtek Array   −18 dBFS   ▇▇▇▇▇▅▂                        │
│ VAD        speech_start 15.114 → speech_end 16.294   (1 180 ms)      │
│ TRIGGER    push_to_talk (F10)                                        │
│ STT        whisper-small-q5 · fr-FR · 268 ms                         │
│            « optimus ouvre les portes »          conf 0.94           │
│ WAKE       préfixe « optimus » retiré                                │
│ NORMALISÉ  « ouvre les portes »                                      │
│ INTENT     ship.doors.toggle          score 1.00  (exact)            │
│            2ᵉ : ship.doors.close      score 0.71   Δ 0.29 → OK       │
│ GUARD      killswitch off · sim off · SC foreground ✓ · cooldown ✓   │
│ BINDING    spaceship_general/v_toggle_all_doors → L                  │
│ EXEC       scancode 0x26 ↓ 45 ms ↑                       128 ms      │
│ RESPONSE   « Compartiments déverrouillés. »   (var. 2/3, humor 40)   │
│ TTS        windows-onecore · Denise · 141 ms                          │
│ TOTAL      voix → touche 585 ms │ voix → parole 742 ms               │
└──────────────────────────────────────────────────────────────────────┘
```

C'est l'écran qui répond à « pourquoi ma commande n'a pas marché ? » — donc l'écran le plus
rentable du produit. Il doit être copiable en un clic (support Discord) et exportable en JSON.
