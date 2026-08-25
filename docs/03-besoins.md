# PHASE 2 — Analyse des besoins Optimus

Chaque exigence porte un identifiant stable, une priorité (**M** = MVP v0.1, **1** = V1,
**2** = V2) et, quand c'est pertinent, un critère d'acceptation mesurable.

---

## 3.1 Exigences fonctionnelles (RF)

### Chaîne vocale

| ID | Exigence | Prio | Critère d'acceptation |
|---|---|---|---|
| RF-V01 | Capturer le micro en continu avec VAD (détection d'activité vocale) | M | Segmente une phrase de 2 s avec ≤ 300 ms de silence de fin |
| RF-V02 | Transcrire la parole en texte via un moteur **interchangeable** (`ISpeechToTextProvider`) | M | Changer de provider sans redémarrer l'app |
| RF-V03 | Déclenchement par **push-to-talk** (touche configurable, globale) | M | Fonctionne quand SC a le focus plein écran |
| RF-V04 | Déclenchement par **wake word** (« Optimus ») | M (dégradé) / 1 (natif) | v0.1 : préfixe détecté dans la transcription. V1 : détecteur dédié < 5 % faux positifs/h |
| RF-V05 | Mode « always listening » activable/désactivable | 1 | |
| RF-V06 | Synthèse vocale via un moteur **interchangeable** (`ITextToSpeechProvider`) | M | Windows SAPI/OneCore fonctionne sans aucune installation |
| RF-V07 | Coupure/ducking du TTS si l'utilisateur reparle (barge-in) | 1 | |
| RF-V08 | Sortie audio sur un périphérique choisi (dont câble virtuel pour le stream) | 1 | |

### Compréhension et commandes

| ID | Exigence | Prio |
|---|---|---|
| RF-C01 | Résoudre une phrase en **intent** via un matcher local déterministe (exact + normalisé + flou) | M |
| RF-C02 | Une commande possède **N phrases vocales** (alias), éditables par l'utilisateur | M |
| RF-C03 | Escalade vers un LLM **optionnel** uniquement si le matcher local échoue ou doute | 1 |
| RF-C04 | Le LLM ne renvoie **qu'un intent structuré** issu d'une liste blanche, jamais une action | 1 |
| RF-C05 | Désambiguïsation par question de relance (« Quel quadrant ? ») | 1 |
| RF-C06 | Mémoire de session : résolution des références au tour précédent (« à l'avant ») | 1 |
| RF-C07 | Extraction de paramètres numériques/énumérés (« puissance moteur à 3 crans ») | 1 |
| RF-C08 | Commandes de type `dialogue` et `lore` (aucune action, pure réponse) | M |
| RF-C09 | Cooldown par commande, anti-double-déclenchement | M |
| RF-C10 | Prérequis d'exécution (`requirements`) évalués avant action | M |

### Exécution

| ID | Exigence | Prio |
|---|---|---|
| RF-E01 | Injection clavier bas niveau (scancodes) compatible avec un jeu DirectInput/Raw Input | M |
| RF-E02 | Injection souris (boutons, molette ; déplacement en V1) | M |
| RF-E03 | Modes `tap` / `hold` / `double_tap` / `press` / `release` avec durées configurables | M |
| RF-E04 | **Séquences** d'étapes avec délais, répétitions, combinaisons | M |
| RF-E05 | **Macros** = séquences nommées et réutilisables, éditables par l'utilisateur | 1 |
| RF-E06 | Conditions dans les macros (`if` / `else` / `wait` / `repeat`) | 1 |
| RF-E07 | Exécution **uniquement si Star Citizen est au premier plan** (option désactivable) | M |
| RF-E08 | **Mode simulation** : aucune touche envoyée, tout est journalisé | M |
| RF-E09 | **Kill switch** global (raccourci) suspendant toute exécution instantanément | M |
| RF-E10 | Test d'une commande depuis l'UI, sans la voix | M |

### Keybinds

| ID | Exigence | Prio |
|---|---|---|
| RF-K01 | Import du `defaultProfile.xml` de SC (défauts par version) | M |
| RF-K02 | Import d'un export utilisateur `layout_*.xml` (deltas) et fusion | M |
| RF-K03 | Édition d'un binding par **capture de touche** (y compris modificateurs et souris) | M |
| RF-K04 | Détection et affichage des **conflits** | M |
| RF-K05 | Plusieurs **profils de binding** (Default, Fighter, Mining, Cargo, Racing, FPS) | 1 |
| RF-K06 | Export / import / backup / restore / reset | 1 |
| RF-K07 | Détection d'un rebind fait en jeu (watcher fichier) et proposition de re-synchro | 2 |
| RF-K08 | Export d'une « feuille de vol » (cheat-sheet PDF/HTML) des favoris | 1 |

### Copilotes, personnalité, profils

| ID | Exigence | Prio |
|---|---|---|
| RF-P01 | CRUD complet de copilotes (créer, modifier, dupliquer, supprimer) | M (partiel : 1 copilote livré + édition) / 1 (CRUD complet) |
| RF-P02 | Personnalité paramétrique (traits 0–100, style, vocabulaire, phrases interdites) | M |
| RF-P03 | Variantes de réponse pondérées par la personnalité (pas de réponse unique) | M |
| RF-P04 | Règles comportementales (`when` → `behavior`) | 1 |
| RF-P05 | Voix et langue par copilote | M |
| RF-P06 | Capacités activables/désactivables par copilote (`enabled_commands`, permissions) | 1 |
| RF-P07 | Profils **utilisateur** (langue, PTT, wake word, copilote préféré, profil SC) | 1 |
| RF-P08 | Packs `.optcopilot` importables/exportables (manifest signé) | 2 |
| RF-P09 | Plusieurs copilotes actifs simultanément (dialogue croisé) | 2 |

### Interface, observabilité, intégrations

| ID | Exigence | Prio |
|---|---|---|
| RF-U01 | Dashboard d'état (micro, STT, TTS, LLM, SC détecté, copilote, latence, dernière commande) | M |
| RF-U02 | Navigateur de commandes (recherche, filtres, catégories, favoris) | M |
| RF-U03 | Keybind Manager | M |
| RF-U04 | Historique horodaté (phrase, intent, action, résultat, latence, erreur) | M |
| RF-U05 | Mode **debug** montrant chaque étape du pipeline avec ses scores | M |
| RF-U06 | Logs fichiers multi-niveaux avec rotation | M |
| RF-U07 | Command Builder (créer une commande sans coder) | 1 |
| RF-U08 | Génération de commande assistée par IA (proposition → validation humaine) | 2 |
| RF-U09 | Analytics (commandes les plus utilisées, échecs, phrases non reconnues, latence moyenne) | 1 |
| RF-U10 | Réduction en zone de notification, démarrage Windows optionnel | M |
| RF-U11 | API locale HTTP authentifiée (loopback) | 1 |
| RF-U12 | Bot Discord (statut, catalogue, historique ; exécution soumise à permissions) | 1 |
| RF-U13 | Système de plugins chargeables | 1 (API dès M) |
| RF-U14 | Overlay / HUD in-game | 2 |
| RF-U15 | Télémétrie de jeu (`IGameStateProvider` branchable) | 2 |

### Gestion d'erreurs (RF-ERR)

| ID | Situation | Comportement obligatoire |
|---|---|---|
| RF-ERR1 | Phrase non comprise | Réponse vocale + entrée « unknown phrase » dans les analytics |
| RF-ERR2 | Commande connue, binding absent | « La commande existe mais aucun raccourci n'est configuré. » + lien direct vers le Keybind Manager |
| RF-ERR3 | SC non détecté / pas au premier plan | « J'ai compris, mais Star Citizen n'est pas au premier plan. » |
| RF-ERR4 | Séquence interrompue (kill switch, échec d'étape) | Annonce + relâchement garanti de **toutes** les touches maintenues |
| RF-ERR5 | Provider indisponible (STT/TTS/LLM) | Dégradation automatique vers un fallback + notification visuelle |
| RF-ERR6 | Cooldown actif | Silence ou accusé de réception court, jamais de double exécution |
| — | **Règle transverse** | **Aucun échec silencieux.** Tout chemin d'erreur produit un retour utilisateur *et* une trace. |

---

## 3.2 Exigences non fonctionnelles (RNF)

| ID | Exigence | Cible mesurable |
|---|---|---|
| RNF-01 | Latence commande simple, de la fin de parole au keypress | **≤ 700 ms p50, ≤ 1 200 ms p95** (hors LLM) |
| RNF-02 | Latence perçue avec accusé vocal | Premier son du TTS ≤ 900 ms |
| RNF-03 | Empreinte mémoire au repos (écoute active, UI ouverte) | ≤ 400 Mo RSS, modèle STT small inclus |
| RNF-04 | Empreinte CPU au repos | ≤ 3 % d'un cœur moderne hors transcription |
| RNF-05 | Impact FPS sur Star Citizen | **≤ 2 %** en 1440p (mesuré avec/sans Optimus) |
| RNF-06 | Démarrage à froid jusqu'à l'écoute | ≤ 4 s (chargement du modèle STT en arrière-plan autorisé) |
| RNF-07 | Fonctionnement **100 % hors ligne** pour toutes les commandes déterministes | Test : carte réseau désactivée → pipeline complet OK |
| RNF-08 | Stabilité | ≥ 8 h de session continue sans fuite mémoire ni dérive de latence |
| RNF-09 | Reprise sur erreur | Le crash d'un sidecar (STT/TTS) ne tue pas l'app ; redémarrage auto |
| RNF-10 | Aucune écriture de données utilisateur dans `Program Files` | Vérifié en installation machine + compte standard |
| RNF-11 | Installation/désinstallation propres, sans reliquat en dehors d'`%APPDATA%` (option de purge) | |
| RNF-12 | Internationalisation : aucun texte utilisateur en dur dans le code | |

---

## 3.3 Exigences techniques (RT)

| ID | Exigence |
|---|---|
| RT-01 | Windows 10 21H2+ / Windows 11, x64. Pas de dépendance à un runtime à installer séparément (self-contained). |
| RT-02 | **Aucun keybind en dur dans le code.** Toute touche vient d'un `BindingProfile` chargé au runtime. Interdiction vérifiée par un test d'architecture (aucune constante de touche hors couche `Input`). |
| RT-03 | Séparation stricte **IA → intent structuré → validation → binding → input**. Le LLM n'a aucun accès direct aux couches basses (vérifié par test d'architecture sur les dépendances de projet). |
| RT-04 | Providers STT/TTS/LLM derrière des interfaces ; découverte par configuration, pas par `switch` dans le cœur. |
| RT-05 | Config utilisateur en **fichiers texte** (JSON/YAML) versionnables + SQLite pour les données de runtime (historique, stats, cache). |
| RT-06 | Toute donnée persistée porte un numéro de schéma et une **migration** testée. |
| RT-07 | API locale liée à `127.0.0.1` uniquement, jamais `0.0.0.0`. |
| RT-08 | Journalisation structurée avec corrélation par `trace_id` de bout en bout (une phrase = un trace). |
| RT-09 | Le cœur (`Optimus.Core`) ne référence **ni** l'UI **ni** Windows : testable en console/CI. |
| RT-10 | Plugins isolés (contexte de chargement dédié, permissions déclarées, déchargeables). |

---

## 3.4 Exigences de sécurité (RS)

| ID | Exigence | Justification |
|---|---|---|
| RS-01 | **Isolation par machine** : une commande n'agit que sur le PC qui l'a reçue localement | §81 du brief |
| RS-02 | Discord et toute couche distante ne peuvent transmettre qu'un **intent** vers une instance *appairée*, jamais un input clavier | §82–83 |
| RS-03 | Appairage Discord ↔ instance par **code à usage unique généré localement**, révocable | |
| RS-04 | Permissions granulaires par utilisateur/rôle Discord : `view_status`, `view_commands`, `execute_commands`, `modify_config` — **`execute_commands` à `false` par défaut** | |
| RS-05 | Liste blanche d'intents : le LLM ne peut jamais nommer une action inexistante ni une commande système | §75 |
| RS-06 | Confirmation vocale obligatoire pour les actions marquées `dangerous` (éjection, autodestruction, largage de cargo) | |
| RS-07 | Kill switch matériel-like : raccourci global (défaut `F12`… **à valider**, `F12` est pris dans SC — proposer `Ctrl+Alt+Pause`) coupant toute injection | |
| RS-08 | API locale : token bearer généré au premier lancement, stocké chiffré (DPAPI), rotation possible | |
| RS-09 | Aucun secret en clair dans les fichiers de config ni transmis à un client | Leçon Jean-Bot |
| RS-10 | Plugins : permissions déclarées au manifeste, validation à l'installation, signature vérifiée pour les packs | |
| RS-11 | Aucune télémétrie sortante par défaut ; opt-in explicite | |
| RS-12 | Rate limiting local sur l'exécution d'intents d'origine distante | Anti-abus/anti-boucle |

---

## 3.5 Exigences UX (RUX)

| ID | Exigence |
|---|---|
| RUX-01 | Esthétique cockpit/avionique **au service de la lisibilité** : pas de texte sur fond animé, contraste AA minimum. |
| RUX-02 | L'état du système est compréhensible en **un coup d'œil** (pastilles de statut, code couleur constant). |
| RUX-03 | Chaque erreur affichée propose l'action corrective (bouton « Configurer le raccourci », « Choisir un micro »). |
| RUX-04 | Premier lancement guidé : micro → langue → import keybinds SC → test d'une commande en simulation → activation. **≤ 5 étapes, ≤ 3 min.** |
| RUX-05 | Le mode debug est accessible en 1 clic depuis le dashboard (c'est l'outil n°1 de support). |
| RUX-06 | Toute action destructive (suppression de copilote, reset des binds) est confirmée et réversible (corbeille/backup auto). |
| RUX-07 | L'app doit rester utilisable **à la souris uniquement**, en fenêtré, pendant que SC tourne en plein écran fenêtré. |
| RUX-08 | Feedback vocal court par défaut (< 2 s) ; la verbosité est un curseur de personnalité, pas une fatalité. |
