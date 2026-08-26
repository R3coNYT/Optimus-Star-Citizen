# PHASES 10 · 11 · 12 — MVP, V1, V2

## 11.1 MVP v0.1 — « Optimus obéit »

**Objectif unique** : que la phrase « Optimus, ouvre les portes » ouvre réellement les portes,
de façon fiable, configurable et sûre. Tout le reste est hors périmètre.

### Inclus

| # | Fonction | Détail | Réf. |
|---|---|---|---|
| 1 | Détection de Star Citizen | processus + premier plan + chemin d'installation + version | RF-E07 |
| 2 | Capture micro + VAD | WASAPI, Silero VAD, pré-roll 3 s, choix du périphérique | RF-V01 |
| 3 | STT local | Whisper.net `small` (téléchargé au 1ᵉʳ lancement), fr/en | RF-V02 |
| 4 | Déclenchement | push-to-talk (défaut) + wake word par préfixe de transcription | RF-V03/04 |
| 5 | Matcher d'intent local | normalisation + exact + flou, seuils configurables, **sans LLM** | RF-C01 |
| 6 | Catalogue de commandes | **~60 commandes** Star Citizen soigneusement choisies (voir §11.2), JSON validé par schéma | RF-C02 |
| 7 | Import keybinds SC | `defaultProfile.xml` + `layout_*.xml`, fusion, rapport d'import | RF-K01/02 |
| 8 | Keybind Manager | liste, recherche, capture de touche, conflits, non-assignés | RF-K03/04 |
| 9 | Moteur d'exécution | `SendInput` scancode, tap/hold/double-tap, séquences + délais, souris | RF-E01→04 |
| 10 | Garde d'exécution | kill switch global, focus jeu, cooldown, confirmation des `dangerous` | RS-06/07 |
| 11 | **Mode simulation** | aucune touche envoyée, trace complète | RF-E08 |
| 12 | TTS | Windows OneCore (zéro installation) + variantes de réponse | RF-V06 |
| 13 | Personnalité | 8 traits, sélection pondérée, anti-répétition, lexique | RF-P02/03 |
| 14 | Copilote Optimus | 1 copilote livré, éditable (identité, voix, traits) | RF-P01 (partiel) |
| 15 | UI | Dashboard · Commands · Keybinds · Voice · Personality · Logs · Settings | RF-U01→06 |
| 16 | Historique + trace debug | SQLite, écran PIPELINE TRACE, export d'incident | RF-U04/05 |
| 17 | Logs fichiers | Serilog, rotation, niveaux | RF-U06 |
| 18 | Tray + démarrage Windows | + mode compact | RF-U10 |
| 19 | Installeur | Velopack, MAJ auto, données dans `%APPDATA%` | RNF-10/11 |

### Explicitement **exclu** du MVP

Discord · plugins chargeables (l'**API** existe, aucun plugin livré) · LLM · macros conditionnelles
(`if`) · Command Builder · multi-copilotes · profils de binding multiples · API locale ·
gamepad/HOTAS · overlay · télémétrie · store · packs `.optcopilot` · analytics avancées ·
multi-langue au-delà de fr/en.

### Définition de « terminé » pour le MVP

- [ ] 20 commandes exécutées de bout en bout avec **≥ 95 % de succès** sur 3 voix différentes.
- [ ] Latence voix → touche **p50 ≤ 700 ms**, **p95 ≤ 1 200 ms**, mesurée automatiquement.
- [ ] Fonctionne **carte réseau désactivée**, après le téléchargement initial du modèle.
- [ ] Impact FPS mesuré **≤ 2 %** sur une session de 30 min.
- [ ] Aucune constante de touche hors de la couche `Input` (test d'architecture vert).
- [ ] Kill switch coupe une séquence en cours **et relâche toutes les touches** (test).
- [ ] Session de 8 h sans fuite mémoire ni dérive de latence.
- [ ] Installation, mise à jour et désinstallation propres sur une VM vierge.
- [ ] Assistant de premier lancement complété en < 3 min par un testeur non initié.

### Découpage indicatif (temps plein ; ×2,5 en temps partiel)

| Sprint | Contenu | Durée |
|---|---|---|
| **S0** | **Spikes de risque** : ① injection scancode acceptée par SC ② latence Whisper sur la machine cible ③ hotkeys globaux en plein écran ④ parsing de `defaultProfile.xml` | 1 sem. |
| S1 | Squelette de solution, domaine, chargement JSON + schémas, `SimulatedInputEngine`, tests | 1 sem. |
| S2 | Importer SC + `BindingProfile` + `BindingResolver` + `SequenceRunner` réel | 1,5 sem. |
| S3 | Audio + VAD + Whisper + normalisation + matcher + catalogue de 60 commandes | 2 sem. |
| S4 | Personnalité + TTS + composition de réponses | 1 sem. |
| S5 | UI : Dashboard, Commands, Keybinds, Voice, Personality, Logs | 2,5 sem. |
| S6 | Garde d'exécution, kill switch, historique, trace debug, assistant de 1ᵉʳ lancement | 1 sem. |
| S7 | Packaging, MAJ, durcissement, tests de bout en bout, documentation | 1 sem. |
| | **Total** | **≈ 11 semaines** |

*Le sprint S0 n'est pas négociable : si l'injection ne passe pas dans Star Citizen, toute
l'architecture d'exécution change. On le découvre en semaine 1, pas en semaine 9.*

---

## 11.2 Les ~60 commandes du MVP

Sélection guidée par la répartition observée chez Jean-Bot (Navigation 33 %, Combat 30 %,
Minage 29 %) **et** par le principe « une commande est utile si l'on n'a pas le doigt libre » :

| Catégorie | ~n | Exemples |
|---|---|---|
| Ship & systèmes | 12 | portes, allumage/extinction, moteurs, boucliers on/off, armes on/off, lumières, train, VTOL, verrouillage cockpit |
| Navigation & quantum | 10 | mode d'atterrissage, auto-atterrissage, quantum, cruise control, découplé, frein spatial, limiteur de vitesse |
| Combat | 12 | groupes d'armes, missiles, contre-mesures (leurre/chaff), gimbal, mode ciblage, cibler hostile/ami/attaquant le plus proche, pin |
| Puissance & boucliers | 8 | presets de puissance, +/- moteur/armes/boucliers, quadrant de bouclier (avant/arrière/gauche/droite), reset |
| Scan & minage | 8 | scan, ping, mode minage, laser, puissance de minage, throttle, cycle de tête |
| Caméra & confort | 5 | vue libre, mobiGlas, carte stellaire, contacts, sous-titres |
| Système Optimus | 5 | « rapport système », « mode simulation », « tais-toi », « répète », « annule » |

**Trois macros de démonstration** (mode combat, mode minage, préparation au quantum) pour prouver
le langage de séquence sans ouvrir le chantier des conditions.

---

## 11.2 bis — Le plan de travail, arrêté le 2026-08-26

Ce que le socle sait déjà faire est décrit dans le registre des décisions (D1 à D44). Ce qui
suit est la file d'attente convenue avec le pilote, dans l'ordre.

| # | Chantier | Pourquoi maintenant | État |
|---|---|---|---|
| **1** | **Alias sur ce qui n'a pas été compris** | Un écran qui liste ce qu'Optimus a entendu sans agir, et permet d'y attacher la formulation réellement employée. Le travail de calibration — accents (D39), seuils (D29), formulations — devient cumulatif au lieu de dépendre d'une passe de développement à chaque fois. C'est la seule fonction qui améliore les autres. | **fait le 2026-08-26** |
| **2** | **Étage conversationnel (LLM)** | « Qu'est-ce que tu penses de ce vaisseau ? » obtient aujourd'hui une réplique de catalogue. Le LLM ne rendra **jamais qu'une intention validée contre la liste blanche, jamais une touche** (§73, §75) et reste **facultatif** (§84) : tout continue de fonctionner hors ligne sans lui. | à faire |
| **3** | **Conditions dans les macros** | `si` et `répéter`. Le moteur de séquence ne sait qu'enchaîner ; une macro ne peut ni s'adapter ni boucler. Dépend en partie d'un état de jeu fiable, donc des actions dirigées (D41) plutôt que d'une télémétrie. | à faire |
| **4** | **Voix neurale locale (Piper)** | Le jour où Microsoft Paul lasse. Mesuré au spike S0-5 : les voix Windows suffisent en latence, l'enjeu est le timbre. Reste local, aucun service distant. | à faire |
| **5** | **Profils de binding** | Chasse / Minage / Cargo, avec bascule à chaud. Les touches diffèrent selon ce qu'on pilote, et tout recharger à la main est une friction certaine. | à faire |
| **6** | **API locale** | REST et WebSocket, jeton, documentation. Socle de tout ce qui suit — et le seul moyen de garder l'exécution **strictement locale** (§81–83) quand d'autres surfaces s'y branchent. | à faire |
| **7** | **Discord** | Bot local, appairage, notifications. Transmet des **intentions**, jamais des frappes : aucun serveur ne touche au clavier de personne. | à faire |
| **8** | **Plugins** | Chargement à chaud, permissions, deux plugins de référence. | à faire |
| **9** | **Multi-copilotes** | Le modèle le prévoit depuis l'origine ; seul Optimus est livré. CRUD, duplication, import et export. | à faire |
| **10** | **HOTAS et périphériques** | Manette, palonnier, Stream Deck. Change la façon de déclencher, pas ce qui est déclenché. | à faire |
| **11** | **Overlay in-game** | HUD transparent : état, dernière commande, confirmations. **Incertitude réelle** : l'interaction avec l'anti-triche reste à valider avant d'y engager quoi que ce soit. | à faire |

**Hors file, mais ouvert** : la signature de code (R16). Sans conséquence tant qu'Optimus reste
sur le poste de son auteur — bloquant dès qu'il faut le diffuser.


---

## 11.3 V1 — « Optimus devient une plateforme » (+ 3 à 4 mois)

| Bloc | Contenu |
|---|---|
| **Multi-copilotes** | CRUD complet, Synthia et Virgil livrés, duplication, import/export `.optcopilot` (sans signature) |
| **Personnalité avancée** | règles comportementales, événements système, banter d'inactivité, aperçu vocal en direct |
| **Voix** | Piper (TTS neural local, FR/EN), cache TTS, providers cloud optionnels (ElevenLabs, Azure, OpenAI) |
| **Wake word natif** | openWakeWord ONNX, modèle « Optimus » + mots personnalisés |
| **LLM optionnel** | Ollama / OpenAI-compatible / Anthropic, sortie JSON contrainte, liste blanche, budget et compteur de tokens, désambiguïsation et conversation |
| **Contexte** | `ConversationContext`, slots, anaphores, `GameContext` déclaratif |
| **Macros & conditions** | `if` / `repeat` / `wait`, éditeur visuel, bibliothèque de macros |
| **Command Builder** | création sans code, validation d'ambiguïté, test en simulation |
| **Profils de binding** | Default / Fighter / Mining / Cargo / Racing / FPS, bascule à chaud, import/export/backup |
| **API locale** | REST + WebSocket, token, documentation OpenAPI |
| **Discord** | mode bot local, appairage, permissions, embeds de statut, notifications d'événements |
| **Plugins** | chargement à chaud, permissions, 2 plugins de référence (Spotify, System) |
| **Analytics** | commandes les plus utilisées, échecs, **phrases non reconnues → proposition d'alias en un clic** |
| **Feuille de vol** | export PDF/HTML des favoris avec les vraies touches |
| **i18n** | interface FR/EN, copilotes multilingues |

---

## 11.4 V2 — « Optimus comprend le vaisseau » (+ 6 mois et plus)

| Bloc | Contenu | Incertitude |
|---|---|---|
| **Télémétrie de jeu** | `IGameStateProvider` réel. **Spike fait le 2026-08-26, résultat négatif pour l'essentiel** : `Game.log` ne consigne <b>ni le mode de vol, ni l'état des portes</b>. Les occurrences de « NAV » y sont des faux positifs venus de `ItemNavigation` ; ce qu'on y trouve est du routage quantique (`CSCItemNavigation`, `FinalStop=`) et des entités voisines. Sans conséquence : les actions dirigées du jeu (D41) rendent portes et mode de vol exacts sans rien observer. Restent à explorer si le besoin revient — l'OCR ciblé du HUD, et le vaisseau courant que le log semble nommer | ⚠ `Game.log` écarté pour l'état du vaisseau |
| **Overlay in-game** | HUD transparent (DirectX/overlay externe) : état, dernière commande, confirmations | ⚠ interaction avec l'anti-triche à valider |
| **Multi-agents** | plusieurs copilotes actifs, dialogues croisés, routage par rôle (combat/minage/navigation) | |
| **Store de copilotes** | packs signés, catalogue, notation, mise à jour — **sans jamais donner au serveur le moindre pouvoir sur le clavier** | |
| **Synchronisation** | sauvegarde chiffrée de bout en bout des configs, multi-PC | |
| **Compagnon mobile / SIMPIT** | consommation de l'API locale, PWA plein écran + wake lock (idée reprise de Jean-Bot) | |
| **Périphériques** | gamepad, HOTAS, Stream Deck, MIDI | |
| **Compatibilité VoiceAttack** | plugin d'import de profils VAP, jamais une dépendance | |
| **Relais Discord** | mode multi-machines, appairage, connexions sortantes uniquement | |
| **IA avancée** | génération de commandes, coaching, résumé de session, mémoire longue durée | |

---

## 11.5 Ce qui ne sera jamais fait (décisions de principe)

| Non-fonctionnalité | Raison |
|---|---|
| Automatisation continue de gameplay (aim assist, farming, macros en boucle) | Sort du cadre « copilote » et met l'utilisateur en risque vis-à-vis des règles du jeu |
| Contrôle d'un PC tiers depuis Discord ou le cloud | §81–83 : l'exécution est toujours locale |
| LLM obligatoire | §84 |
| Keybinds en dur | §72 |
| Envoi de télémétrie sans consentement explicite | RS-11 |
| Dépendance obligatoire à VoiceAttack | §71 |
