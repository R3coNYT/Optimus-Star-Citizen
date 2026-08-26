# Risques, stratégie de tests et décisions techniques

## 13.1 Risques techniques

Cotation : **P** probabilité (1–5) × **I** impact (1–5).

| # | Risque | P | I | Score | Mitigation | Quand le lever |
|---|---|---|---|---|---|---|
| ~~R1~~ | ~~**L'injection `SendInput` est ignorée ou filtrée par Star Citizen**~~ → **RISQUE LEVÉ le 2026-08-24** | — | — | — | **Star Citizen réagit aux injections `SendInput` en scancode**, en utilisateur standard, sans élévation (S0-1/game, R3CON-PC). Le plan A est validé ; les plans B (pilote Interception) et C (HID émulé) sont abandonnés. Reste la règle de conduite : jamais d'automatisation continue, un énoncé = une action délibérée. | ✅ fait |
| ~~R2~~ | ~~**Latence STT hors budget**~~ → **RÉSOLU le 2026-08-25 par le spike S0-6** | 1 | 5 | **5** | Le budget est tenu par un **reconnaisseur à grammaire contrainte** au lieu d'un transcripteur généraliste : **16,7 ms p50, 27,2 ms p95, 21/21 commandes justes**, sans GPU ni téléchargement (D28). Whisper reste sur le chemin conversationnel, où 3 s ne dérangent personne. Le build GPU est écarté sur la machine cible : 6 Go de VRAM quand Star Citizen en réclame 7,3. **Risque résiduel : les faux déclenchements**, non mesurables faute d'assez d'énoncés hors grammaire | ✅ résolu |
| ~~R2 *(mesures d'origine)*~~ | ~~Latence STT~~ (2026-08-24) | 5 | 5 | ~~25~~ | Mesuré sur R3CON-PC **avec Star Citizen lancé** : `base` p50 **5,2 s**, `small` p50 **17,5 s**, contre une cible de 500 ms — un à deux ordres de grandeur au-dessus. Causes à départager : concurrence du jeu sur le processeur, nombre de threads, jeu d'instructions du binaire. **C'est désormais le risque n°1 du projet, devant R14.** Pistes : mesure jeu fermé, modèle `tiny`, `-t` ajusté, build accéléré (BLAS/CUDA/Vulkan), priorité et affinité de thread, et à défaut changement de moteur STT | **Contre-mesure à trouver avant le MVP** |
| R3 | **Reconnaissance médiocre du vocabulaire Star Citizen** (« quantum », noms de vaisseaux, « mobiGlas ») | 4 | 3 | 12 | `initial_prompt` avec le lexique du domaine, post-correction phonétique, alias appris depuis `unknown_phrases` | S3 |
| R4 | **Faux déclenchements du wake word** en mode always-on | 4 | 3 | 12 | PTT par défaut ; détecteur natif en V1 ; seuil réglable ; « annule » toujours disponible | V1 |
| R5 | **Format de `defaultProfile.xml` / `layout_*.xml` changeant** entre versions de SC | 3 | 3 | 9 | Parser tolérant + tests sur plusieurs versions + rapport de migration + préréglages embarqués | S2 |
| R6 | **L'utilisateur rebinde en jeu** et Optimus n'est plus synchrone | 4 | 2 | 8 | Watcher sur `Mappings\`, bannière de resynchro, vérification à chaque démarrage | V1 |
| R7 | **Modes contextuels** (scan/minage/turret) : la même touche fait autre chose | 4 | 3 | 12 | Binding indexé par actionmap ; mode déclaratif posé par la voix ; séquences qui forcent le mode | S2/S3 |
| R8 | **Élévation de privilèges** : SC en admin ⇒ injection bloquée (UIPI) | 2 | 4 | 8 | Détection + message clair + mode élevé opt-in | S0 |
| R9 | **Boucle audio** : le TTS déclenche Optimus | 3 | 3 | 9 | Fenêtre de suppression pendant la lecture + 200 ms | S3 |
| R10 | **Un plugin tiers plante ou abuse** | 3 | 3 | 9 | ALC isolé, timeouts, permissions, signature, désactivation à chaud | V1 |
| R11 | **Fuite de clé API / token Bridge** | 2 | 4 | 8 | DPAPI, jamais de secret côté client, rotation, portées minimales (leçon Jean-Bot) | S6 |
| R12 | **Conformité aux règles du jeu** : perception de l'automatisation | 2 | 5 | 10 | Positionnement strict « 1 énoncé = 1 action délibérée » ; pas de boucle, pas d'aide à la visée, pas de lecture mémoire ; kill switch visible ; documentation publique de ce qu'Optimus fait et ne fait pas | Dès le départ, dans le README |
| R13 | **Empreinte de l'installeur** (modèles) | 4 | 2 | 8 | Installeur léger + téléchargement à la demande | S7 |
| R14 | **Dérive de périmètre** (le brief couvre 3 ans de travail) | 5 | 4 | **20** | Périmètre MVP gelé, « ne sera jamais fait » écrit, roadmap publique | Permanent |
| R15 | **Nom « Optimus »** : conflit potentiel avec une marque connue | 3 | 3 | 9 | Vérification avant diffusion publique ; le nom du produit est une donnée, pas du code | Avant packaging public |
| **R16** | **Smart App Control bloque les exécutables non signés** — **rencontré le 2026-08-25** | 5 | 4 | **20** | Constaté sur R3CON-PC : « Une stratégie de contrôle d'application a bloqué ce fichier ». Actif par défaut sur certaines installations de Windows 11, et **Windows ne permet pas de le réactiver après désactivation** : le contourner en le coupant n'est pas une option acceptable à proposer à un utilisateur. Contournement de développement : publication dépendante du runtime, lancée par `dotnet.exe` (signé Microsoft) — **mais il ne suffit pas**. Le 2026-08-25, une seconde occurrence a frappé le DLL lui-même (`0x800711C7`), alors que la même commande passait la veille : la politique évalue **chaque fichier**, et toute publication en produit un au hash inédit. Le blocage est donc structurel et intermittent, pas accidentel. `tools/diagnose-app-control.ps1` distingue les deux causes qui se ressemblent — marque du web héritée d'une copie USB, qui se retire sans rien désactiver, et SAC actif, contre lequel aucune manipulation de fichier ne peut rien. **Cause de la 2ᵉ occurrence identifiée et levée le 2026-08-25** : le hook clavier global introduit par l'éditeur de keybinds. Sa suppression au profit du tampon d'entrée de la console (D36) a débloqué la publication sur une machine où SAC est pourtant **actif** — ce qui apprend que la politique juge le contenu, pas seulement la signature, et qu'un binaire sobre passe là où un binaire au comportement suspect échoue. **Mesuré le 2026-08-26** en lisant le journal `CodeIntegrity/Operational` : seuls les assemblys de **point d'entrée** sont refusés — `Optimus.App.dll`, `Optimus.Cli.dll` et leurs apphosts. `Optimus.Core.dll` et `Optimus.Infrastructure.dll`, qui contient pourtant `SendInput`, ne sont jamais évalués. Ce n'est donc pas ce que fait Optimus qui le rend suspect, mais son absence de réputation. **Corollaire : lancer par `dotnet.exe` ne suffit pas** — l'assembly d'entrée est évalué de la même façon, et ce contournement, qui avait semblé marcher, a été refusé à son tour. Il n'existe donc aucun contournement d'emballage. **Levé sur le poste de jeu le 2026-08-26** : l'utilisateur a désactivé Smart App Control, en connaissance du caractère irréversible de l'opération. Le risque n'est donc plus bloquant pour le développement, mais il reste entier pour toute diffusion — et il reviendra intact sur ce poste après une réinstallation de Windows. **Réponse définitive : signature de code de l'exécutable et de l'installeur** — à budgéter avant toute diffusion (certificat OV/EV, ~100–400 €/an). Concerne aussi l'installeur Velopack | **Avant la V1** |

> **R14 est le risque le plus élevé du projet.** Le brief décrit une plateforme ambitieuse ;
> la seule façon de l'atteindre est de livrer un MVP étroit et excellent, puis d'élargir.

---

## 13.2 Spikes à réaliser AVANT le développement

| # | Question | Protocole | Critère de succès |
|---|---|---|---|
| **S0-1** | Star Citizen accepte-t-il `SendInput` en scancode ? | Outil livré : [`tools/Optimus.Spike.InputTest`](../tools/Optimus.Spike.InputTest/README.md). Mode `probe` (automatique, sans le jeu) puis mode `game` (plan d'observation, fenêtré **et** plein écran) | Action visible en jeu dans les deux modes d'affichage |
| **S0-2** | Quelle latence Whisper sur la machine cible ? | Whisper.net, 20 échantillons FR de 1–3 s, modèles `tiny`/`base`/`small`, CPU et GPU | `small` p95 ≤ 500 ms, sinon retenir `base` par défaut |
| **S0-3** | Hotkeys globaux et capture micro en jeu plein écran | `RegisterHotKey` + hook bas niveau + WASAPI, pendant une session | PTT fonctionne, aucune interférence avec le jeu |
| **S0-4** | Structure réelle de `defaultProfile.xml` | Script fourni : [`tools/get-default-profile.ps1`](../tools/get-default-profile.ps1) — localise `Data.p4k`, extrait `Data\Libs\Config`, convertit le CryXML binaire via `unforge`, compte actionmaps et actions | Table `action_id → InputSpec` produite, ≥ 95 % des actions du jeu couvertes |
| **S0-5** | Piper en sidecar : latence et qualité FR | Synthèse de 10 phrases, mesure du temps jusqu'au premier échantillon | ≤ 400 ms, qualité jugée acceptable en écoute |

### État d'avancement

| Spike | État | Résultat |
|---|---|---|
| S0-1 *(partie système)* | ✅ **fait** — 2026-08-23, sur **les deux machines** | **PASS partout.** Injection scancode intacte dans le Raw Input ; injection virtual-key seule ⇒ `MakeCode = 0x00` (donc invisible d'un moteur lisant le scancode) ; touches étendues, combinaisons et double appui corrects ; **AZERTY sur les deux machines ⇒ `MapVirtualKey` renvoie de mauvais scancodes** (voir D19). Durées de maintien : ±1,4 ms sur le portable, **±0,4 ms sur le PC de jeu** (machine de référence). Rapports : `docs/spikes/S0-1-probe-20260823-232510.md` (R3CON-PORTABLE) et `S0-1-probe-20260823-235954-R3CON-PC.md` (**R3CON-PC, machine cible**) |
| S0-1 *(partie en jeu)* | ✅ **fait** — 2026-08-24, R3CON-PC, Star Citizen non élevé | **G1 PASS** : le jeu réagit à l'injection scancode. **G2 FAIL** : il ignore l'injection virtual-key seule — exactement ce que la sonde Raw Input prédisait (`MakeCode = 0x00`). **G3 PASS** : un appui de **16 ms** suffit. **G4 PASS** : un maintien de 800 ms est vu comme un maintien. G5 (combinaison) et G7 (souris X2) non concluants faute d'action assignée observable — à confirmer via l'écran de keybindings du jeu. Rapports : `S0-1-game-20260824-005151-R3CON-PC-moteur-allume.md` (le premier passage, moteur éteint, était non observable : `L` = lumières, `Espace` = montée) |
| **S0-4** | ✅ **fait** — 2026-08-24, build **4.9-live.12344265** | `defaultProfile.xml` extrait (220 Ko, **50 actionmaps, 1103 actions**) et converti en `data/bindings/starcitizen/defaults-4.9.json` : **627 bindings** (106 souris, 86 combinaisons), 476 actions sans binding par défaut, 61 non injectables (axes analogiques, head tracking). Trois découvertes structurantes : deux formats XML distincts, modificateurs de position libre, et **modes d'activation déclarés par le jeu avec leurs seuils** (voir docs/02 et D21) |
| **S0-5** *(TTS Windows)* | ✅ **fait** — 2026-08-24 | **Le TTS n'est pas un problème de latence.** À chaud : **7 à 15 ms** de synthèse pour des répliques de 1,3 à 5 s, soit un RTF de 0,001 à 0,003. À froid, la toute première synthèse coûte jusqu'à **429 ms** (voix Paul) ⇒ **préchauffer le moteur au démarrage** (D23). Voix FR disponibles : SAPI5 n'expose qu'Hortense (féminine) ; **OneCore expose Hortense, Julie et Paul — la seule voix masculine française** ⇒ l'API OneCore est requise, pas optionnelle. Rapport : `S0-5-tts-20260824-020436-R3CON-PORTABLE.md`. Reste à comparer Piper pour la qualité (`-PiperDir`) |
| **S0-3** | ✅ **fait** — 2026-08-24, R3CON-PC, **Star Citizen au premier plan sur 8 énoncés sur 8** | **PASS complet.** La capture micro fonctionne pendant que le jeu tourne. Latence d'ouverture du périphérique : **123 à 177 ms** (médiane 135) — trois fois mieux que sur le portable (419 ms), mais toujours de quoi tronquer une attaque de phrase ⇒ pré-roll confirmé (D24). **`RegisterHotKey` n'a délivré aucun `WM_HOTKEY`** alors que l'enregistrement avait réussi et que le hook voyait chaque appui ⇒ le hook bas niveau est le seul mécanisme retenu (anomalie à élucider avant d'utiliser `RegisterHotKey` pour le kill switch). Périphérique utilisé : **« Microphone (Voicemod) »**, un device virtuel — à refaire avec le micro physique pour écarter son influence |
| **S0-2** *(jeu fermé, balayage de threads)* | ✅ **fait** — 2026-08-24, R3CON-PC (Ryzen 5 3600, 6c/12t, 32 Go) | **Le jeu coûtait 5×** : `base` passe de 5 166 ms (jeu lancé) à **1 025 ms** (jeu fermé, 8 threads). **Le SMT aide, contrairement à ce que j'avais annoncé** : 8 threads > 6 > 4 sur les trois modèles. **`tiny` est éliminé** — WER **59,4 %**, il ne reconnaît même pas le mot d'éveil ; la dégradation n'est pas graduelle, il y a une falaise entre `tiny` et `base` en français. **`small` n'apporte rien** : WER 10,4 % contre 9,8 % pour `base`, pour 3,4× le temps. ⇒ **`base` retenu** (D26). Reste à combler l'écart avec la cible : `--audio-ctx` et build GPU |
| **S0-2** *(contexte audio)* | ✅ **fait** — 2026-08-24, jeu fermé, `base`, 12 threads | `--audio-ctx` accélère réellement, mais la précision s'effondre vite : **complet 933 ms / WER 9,8 %** · **768 → 553 ms / 14,8 %** · **512 → 424 ms / 31,1 %**. À 512 les commandes deviennent méconnaissables (« Optimus ne passe pas sur notre combat »). ⇒ **512 écarté**, 768 envisageable en « mode rapide » optionnel, contexte complet par défaut. **12 threads > 8** partout, ce qui confirme D26 |
| **S0-2** *(1ᵉʳ passage, jeu lancé)* | ⚠️ **résultat alarmant, expliqué depuis** — 2026-08-24 | **Latence : `base` p50 5,2 s, `small` p50 17,5 s** pour une cible de 500 ms. Voir R2. **Précision : excellente** — WER moyen 7 à 10 % pour `base`, les erreurs se concentrant sur un seul mot du domaine (« boucliers » → « bouquillés », « bouts qui y est »), et **« Optimus » reconnu à 100 %** ⇒ le mot d'éveil est sûr. `small` n'est pas plus précis que `base` sur notre vocabulaire, tout en étant 3,4× plus lent. Rappel technique : Whisper encode toujours une fenêtre de **30 s**, donc une phrase courte coûte autant qu'une longue |

---

## 13.3 Stratégie de tests

### Tests unitaires (rapides, sans matériel)

| Cible | Cas critiques |
|---|---|
| `TextNormalizer` | accents, élisions, nombres en lettres, ponctuation, mots parasites, wake word |
| `FastIntentMatcher` | exact, flou, seuils, égalités, commandes désactivées, **anti-régression sur 200 phrases de référence** |
| `BindingResolver` | binding absent, vide, conflit, actionmap, modificateurs |
| `SequenceRunner` | ordre, délais, `repeat`, `if`, **relâchement garanti en cas d'exception/annulation** |
| `PersonalityEngine` | filtre par traits, anti-répétition, phrases interdites, budget de mots, déterminisme sous graine fixe |
| `ExecutionGuard` | kill switch, simulation, focus, cooldown, `dangerous`, permissions |
| `ScActionMapImporter` | XML vide (**le cas réel de `layout_Keybinds_1_exported.xml`**), deltas, `input=" "`, combos, XML malformé |
| Validation de schéma | catalogue invalide ⇒ élément désactivé, app démarrée, erreur remontée |
| Permissions | matrice Discord/API/plugin |

### Tests d'architecture (NetArchTest, bloquants en CI)

```
✗ Aucun type hors de Optimus.Infrastructure.Input ne référence SendInput / VK_* / scancodes
✗ Optimus.Core ne référence ni PresentationFramework, ni System.Windows, ni Discord.Net
✗ Aucun projet *.Ai.* ne référence *.Input.*
✗ Aucune chaîne de touche littérale dans Optimus.Core (analyseur Roslyn dédié)
✗ Tous les types publics de Optimus.Sdk sont documentés (XML doc obligatoire)
```

### Tests d'intégration

STT sur fichiers WAV de référence (30 phrases, 3 locuteurs, avec et sans bruit de fond) ·
TTS (voix disponibles, synthèse, repli) · SQLite (migrations aller/retour) ·
import XML SC (jeux de fichiers de plusieurs versions) · API (auth, portées, rate limit) ·
Discord (permissions, appairage, révocation) · plugin de test (chargement, crash, déchargement).

### Tests de bout en bout (mode simulation, sans le jeu)

```
WAV « optimus ouvre les portes »
  → pipeline complet avec SimulatedInputEngine
  → assertion : intent = ship.doors.toggle, binding = L, résultat = simulated
  → assertion : latence budgétée respectée
  → assertion : réponse conforme à la personnalité, sans phrase interdite
```

**35 scénarios** couvrant : succès, binding absent, jeu absent, jeu non au premier plan,
cooldown, kill switch pendant une séquence, désambiguïsation, slot en attente, commande
`dangerous` confirmée/refusée, phrase inconnue, provider en panne, plugin absent.

### Tests manuels (checklist de release, en jeu)

Injection réelle en fenêtré et en plein écran · latence perçue · impact FPS mesuré ·
session longue (8 h) · débranchement du micro · changement de périphérique audio ·
mise à jour applicative en cours de session · désinstallation propre.

---

## 13.4 Décisions techniques recommandées — récapitulatif

| # | Décision | Alternative écartée | Raison |
|---|---|---|---|
| D1 | C# / .NET 8 + WPF, un seul processus | Tauri, Electron, Python/Qt | Audio + P/Invoke + empreinte + risque projet |
| D2 | STT local Whisper.net par défaut | cloud par défaut | Offline, coût nul, confidentialité (§84) |
| D3 | TTS Windows OneCore par défaut, Piper en option | ElevenLabs par défaut | Fonctionne immédiatement, sans compte ni coût |
| D4 | **LLM désactivé par défaut** | LLM au cœur du pipeline | §84–85 : déterminisme, latence, coût, vie privée |
| D5 | Matcher local déterministe en première ligne | tout au LLM | ~300 ms vs ~1 500 ms, et testable |
| D6 | `action_id` calqués sur les actionmaps de Star Citizen | identifiants maison | Import/diff/migration triviaux |
| D7 | Configuration en fichiers JSON, runtime en SQLite | tout en base | Diffable, réparable, partageable |
| D8 | `ExecutionGuard` = point de contrôle unique | vérifications dispersées | Un seul endroit à auditer |
| D9 | Mode simulation dès le premier jour | ajouté plus tard | Rend le développement et les tests possibles sans le jeu |
| D10 | Kill switch global obligatoire | option | Sécurité de base d'une app qui pilote un clavier |
| D11 | Bot Discord **local** par défaut | bot central | Isolation par construction (§81–83) |
| D12 | Plugins en `AssemblyLoadContext` + permissions | chargement libre | Résilience et sécurité |
| D13 | Velopack pour l'installation et les MAJ | MSIX, Squirrel | MAJ delta simples, pas de contrainte de store |
| D14 | Modèles téléchargés après installation | embarqués | Installeur léger, choix de la taille |
| D15 | Bip d'accusé de réception immédiat | rien | Le meilleur rapport effet perçu / effort du projet |
| ~~D16~~ → **D30** | **Écoute permanente par défaut**, mot d'éveil obligatoire en tête de grammaire ; push-to-talk au choix, touche configurable | ~~PTT par défaut~~ | **Décision révisée le 2026-08-25.** Le PTT s'imposait tant que le STT était Whisper : celui-ci transcrit *tout* ce qu'il entend, ce qui posait un problème de vie privée et de faux déclenchements. Avec le moteur à grammaire (D28), l'écoute permanente devient structurellement plus sûre : la grammaire n'accepte que les phrases **commençant par le mot d'éveil**, et une conversation ordinaire ne correspond à aucune alternative — elle est rejetée par construction, sans avoir été transcrite. Les deux modes restent offerts, réglés dans le profil utilisateur |
| D17 | Catégories en énumération fermée validée | chaînes libres | Évite exactement le bug `category.id` de Jean-Bot |
| D18 | `trace_id` de bout en bout | logs indépendants | Le support devient possible |
| **D19** | **Table de scancodes fixe en positions US ; `MapVirtualKey` interdit dans le moteur** | conversion via la disposition Windows active | **Mesuré au spike S0-1** : en AZERTY, `MapVirtualKey(VK_A)` renvoie `0x10` (position `Q`) alors que `kb1_a` de Star Citizen vaut `0x1E`. Une conversion dépendante du layout envoie la mauvaise touche chez la majorité des joueurs francophones. Prototype de la table : `tools/Optimus.Spike.InputTest/src/ScanCodes.cs` |
| **D20** | **`timeBeginPeriod(1)` actif pendant l'exécution d'une séquence** | `Thread.Sleep` seul | Sans cela la granularité est d'environ 15 ms ; avec, les maintiens sont précis à ±1,4 ms (mesuré, S0-1/T4) |
| **D21** | **`hold_ms` dérivé par action du `activationMode` de Star Citizen**, avec 45 ms comme plancher | une constante globale | Le jeu **déclare lui-même** ses 18 modes d'activation et leurs seuils dans `defaultProfile.xml`. Un `delayed_press` exige ≥ 0,25 s, un `delayed_press_medium` ≥ 0,5 s, un `delayed_hold_long` ≥ 1,5 s : **31 actions sur 627 échoueraient silencieusement** avec un tap de 45 ms. Le plancher de 45 ms vient de S0-1/G3 (le jeu accepte 16 ms) |
| **D28** | **STT à deux étages : grammaire contrainte pour les commandes, Whisper pour la conversation** | Whisper seul pour tout | Mesuré (S0-6) sur les mêmes enregistrements que S0-2 : **16,7 ms contre 3 336 ms**, et 21/21 commandes justes. Un moteur à grammaire ne peut produire qu'une phrase autorisée — « boucliers » ne peut pas devenir « bouquilles » si le mot n'existe pas dans la grammaire. Employer un transcripteur généraliste pour choisir parmi 59 possibilités connues d'avance était le mauvais outil. `ISpeechToTextProvider` rendait ce changement indolore |
| **D29** | **Deux seuils : bruit sous 0,35, exécution à partir de 0,65, proposition entre les deux** | un seuil unique à 0,40 | **Recalibré au micro le 2026-08-25**, le banc sur fichiers ayant menti : l'audio enregistré produit des confiances plus basses que le micro en direct. Mesures réelles — bruit ambiant 0,00–0,06 · commandes 0,69–0,93 · phrases hors catalogue 0,51–0,64. Mais les bandes **se chevauchent** (une commande valide est descendue à 0,55), donc aucun seuil unique ne suffit : dans l'intervalle, Optimus propose la commande et attend « Optimus, confirme » pendant 12 s. Refuser une commande valide est aussi pénible qu'en exécuter une non demandée |
| **D31** | **Les questions au copilote sont des commandes du catalogue**, pas des erreurs à filtrer | traiter « qui es-tu ? » comme du hors-sujet | Constaté au micro : « Optimus, qui es-tu ? » était rabattu sur `system.cancel`. Un moteur à grammaire rend toujours sa meilleure alternative — si la question légitime n'y figure pas, il en désigne une autre. Le remède n'est pas de durcir le filtre mais d'**enrichir le catalogue** : `dialogue.identity` et `dialogue.wellbeing` obtiennent depuis 0,69 et 0,91 de confiance |
| **D32** | **Aucun chiffre sur l'état du vaisseau tant qu'il n'est pas mesuré.** Les répliques de `system.status` restent du décor assumé jusqu'à l'arrivée d'un `IGameStateProvider` | énoncer des relevés vraisemblables, faute de mieux | **Décidé le 2026-08-25.** « Réacteur nominal, boucliers à cent pour cent » se dit bien, mais Optimus n'a aucune télémétrie : c'est un relevé inventé, énoncé avec l'assurance d'une mesure. Tant que c'est de l'ambiance, cela passe ; le jour où un pilote s'y fierait en vol, un chiffre faux vaudrait moins que le silence. Le décor est donc conservé **pour l'instant**, et ces répliques seront les premières réécrites quand la télémétrie existera — pas les dernières. Voir la note dans `responses.fr.json`, qui porte le même avertissement à côté des textes concernés |
| **D33** | **Le sens demandé fait partie de l'intention**, porté de la grammaire jusqu'à l'exécution. Séquence dirigée quand le jeu en déclare une et qu'elle a une touche ; bascule assortie d'un état supposé sinon | traiter « éteins » comme un synonyme d'« allume » | **Décidé le 2026-08-25**, sur demande. Presque tout est bascule dans Star Citizen : une touche qui inverse. « Éteins les lumières » envoyait donc la même touche qu'« allume », et éteignait une fois sur deux. Le jeu déclare pourtant 16 actions dirigées (`v_lights_on`/`v_lights_off`) — **aucune n'a de touche par défaut**, ce qui donne à l'éditeur de keybinds sa seconde raison d'être. En attendant, Optimus retombe sur la bascule et se fie à ce qu'il a lui-même commuté (voir [[ToggleBelief]]). Cette croyance est faillible — le pilote peut agir au clavier — d'où la porte de sortie : **redemander la même chose passe outre**. Une croyance fausse coûte un aller-retour, jamais un blocage |
| **D34** | **Le score d'inclusion est gouverné par la couverture**, sans plancher au-dessus du seuil d'exécution | plancher à 0,90 pour toute phrase contenue | **Corrigé le 2026-08-25.** La formule `0,90 + 0,08 × couverture` plaçait toute inclusion au-dessus du seuil d'exécution (0,85) : un mot isolé revendiquait n'importe quel énoncé le contenant, et « priorité aux armes » basculait l'armement à 0,93. Le commentaire du code affirmait pourtant que le score décroissait avec les mots en trop — il ne le faisait pas. Désormais `0,72 + 0,26 × couverture` : un mot dans trois tombe à 0,81, donc dans la bande de proposition. Trouvé en écrivant le test qui devait prouver que les phrases imbriquées signalées par le validateur étaient inoffensives |
| **D35** | **L'éditeur de keybinds écrit des deux côtés** : une couche d'assignations pour Optimus, et un fichier de mappage que Star Citizen sait relire | n'enregistrer la touche que du côté d'Optimus | **Décidé le 2026-08-25.** Optimus envoie des touches, il ne parle pas au jeu : assigner `K` aux portes côté Optimus ne produit rien tant que Star Citizen n'associe pas, lui aussi, `K` à `v_toggle_all_doors`. Un éditeur qui ne remplirait que la moitié Optimus serait un placebo — l'utilisateur verrait une touche assignée et une commande qui ne marche pas, sans aucun moyen de comprendre pourquoi. D'où les deux sens : `--import-layout` apprend ce que le pilote a déjà réglé dans le jeu, `--export-layout` produit le `ActionMaps` à charger par `pp_RebindKeys` |
| **D36** | **La capture de touche lit le scancode par le tampon d'entrée de la console**, jamais un hook clavier global ni le code virtuel | `SetWindowsHookEx(WH_KEYBOARD_LL)`, ou `Console.ReadKey` | **Révisé le 2026-08-25, le jour même.** Deux exigences se cumulent. D'abord le scancode : c'est D19 vue depuis l'autre bout — sur AZERTY, la touche marquée « A » porte le code virtuel `A` mais occupe la position US `Q`, seule connue du jeu et de l'injection ; capturer par le code virtuel ferait enregistrer une touche pour en presser une autre, ce qui *paraîtrait* juste. Ensuite le périmètre : la première implémentation employait un hook bas niveau, qui intercepte les frappes de **toutes** les applications pour lire une seule touche destinée à celle-ci. Disproportionné, et c'est la signature d'un enregistreur de frappe — la première publication qui en contenait un s'est fait bloquer par Smart App Control, là où les précédentes passaient (R16). `KEY_EVENT_RECORD.wVirtualScanCode` donne le même scancode sans rien voir de ce qui ne nous est pas adressé |
| **D37** | **Une macro désigne des commandes et un sens, jamais des identifiants d'action** | enchaîner directement les `action_id` | **Décidé le 2026-08-26.** Presque tout est bascule dans Star Citizen : une macro qui enchaînerait des identifiants bruts enchaînerait des bascules, et chaque pas serait à pile ou face selon l'état du vaisseau — une séquence de cinq pas n'aurait qu'une chance sur trente-deux d'aboutir. En désignant `command_id` + `polarity`, chaque pas hérite de la résolution de polarité (D33) : action dirigée quand elle a une touche, repli sur la bascule sinon |
| **D38** | **Les renvois sont dépliés avant la garde**, jamais pendant l'exécution | résoudre chaque pas au fil de la séquence | **Décidé le 2026-08-26.** Une macro dont le cinquième pas n'a pas de raccourci jouerait sinon les quatre premiers puis s'arrêterait, laissant le vaisseau dans un état intermédiaire que personne n'a demandé — alimentation en route, portes ouvertes, boucliers absents. La garde vérifie donc la séquence complète avant qu'un seul appui ne parte, et nomme l'action fautive : sur dix pas, savoir **lequel** manque fait toute la différence entre un diagnostic et une énigme |
| **D39** | **La grammaire reçoit le texte accentué**, la table de correspondance garde la forme normalisée | construire la grammaire à partir du texte normalisé | **Mesuré en vol le 2026-08-26.** Un moteur à grammaire dérive la prononciation attendue du texte qu'on lui donne : « prepare le decollage » se modélise « pre-pare le de-collage », deux syllabes fausses en français. Confiances relevées : 0,41 à 0,67 pour cette phrase, contre 0,87 et plus pour les commandes dont la perte d'accent change peu la prononciation. Le rapprochement, lui, reste insensible aux accents — la normalisation intervient après la reconnaissance. 75 formulations du catalogue ré-accentuées |
| **D40** | **Un pas de macro peut exiger un sens garanti** (`require_directed`) et être écarté plutôt que de retomber sur une bascule | appliquer le repli partout, ou l'interdire partout | **Constaté en vol le 2026-08-26** : « prépare le décollage » a **ouvert** les portes au lieu de les fermer. Le jeu n'expose aucun sens pour `v_toggle_all_doors`, le pas retombait donc sur la bascule et faisait l'inverse une fois sur deux. Interdire le repli partout aurait vidé les macros de leur substance — allumer un vaisseau froid par une bascule est justement ce qu'on veut. Le jugement appartient donc à la donnée, pas au moteur : le drapeau se pose sur les seuls pas dont l'état de départ est incertain. Un pas écarté apparaît dans la trace avec sa raison — une macro qui saute une étape en silence laisse croire qu'elle a tout fait |
| **D27** | **Chemin du jeu lu par `QueryFullProcessImageName`**, jamais par `Process.MainModule` | l'API .NET habituelle | Constaté en jeu le 2026-08-25 : `MainModule` échoue sur Star Citizen alors même que le jeu n'est pas élevé — l'anti-triche refuse les droits qu'elle réclame. `QueryFullProcessImageName` se contente de `PROCESS_QUERY_LIMITED_INFORMATION`. Sans ce chemin, pas de canal, donc pas d'import automatique des keybinds ni d'accès à `Data.p4k` |
| **D26** | **Modèle STT par défaut : `base`**, threads = nombre de processeurs **logiques** | `small` « pour la précision », `tiny` « pour la vitesse » | Mesuré (S0-2) : `small` n'est pas plus précis que `base` sur notre vocabulaire (10,4 % contre 9,8 % de WER) pour 3,4× le temps ; `tiny` s'effondre à 59,4 % et rate le mot d'éveil. Le SMT apporte un gain réel (8 threads > 6 > 4), contrairement à l'intuition |
| **D23** | **Préchauffage des moteurs vocaux au démarrage** (une synthèse à vide, un chargement de modèle STT) | initialisation paresseuse | Mesuré (S0-5) : la première synthèse coûte jusqu'à **429 ms** contre 7 à 15 ms ensuite. Sans préchauffage, c'est la toute première réponse d'Optimus — celle qui fait la première impression — qui serait la plus lente |
| **D24** | **Le flux de capture audio reste ouvert en permanence**, avec un tampon circulaire de pré-roll | ouvrir le micro à l'appui du PTT | Mesuré (S0-3) : ouvrir le périphérique coûte **419 ms**. Ouvrir à la demande ferait perdre le début de chaque phrase — première cause d'échec de reconnaissance |
| **D25** | **`INSERT` comme touche PTT par défaut** | `F10` | `F10` porte déjà `v_power_throttle_up` et `v_power_throttle_max` dans Star Citizen (vérifié sur `defaults-4.9.json`) |
| **D22** | **Une sonde ou un composant d'entrée ne doit jamais pouvoir tuer le processus** : boucle de messages et callbacks natifs enveloppés, délégués maintenus en vie, classe de fenêtre à nom unique | laisser remonter les exceptions | Vécu pendant le spike : une classe de fenêtre à nom fixe réutilisée au 2ᵉ lancement dans le même processus a provoqué une exception native qui a tué PowerShell. `Optimus.Infrastructure.Input` utilisera les mêmes mécanismes, avec une durée de vie bien plus longue |

---

## 13.5 Points à trancher par toi

| Sujet | Options | Recommandation |
|---|---|---|
| **Nom du produit** | garder « Optimus » / renommer le produit et garder Optimus comme copilote par défaut | Trancher avant toute diffusion publique ; sans impact technique |
| **Raccourci kill switch** | `F12` (occupé dans SC : *Toggle Chat Window*) / `Ctrl+Alt+Pause` / `Ctrl+Alt+K` | **`Ctrl+Alt+Pause`** — impossible à déclencher par erreur, non utilisé par le jeu |
| **Touche PTT** | ~~`F10`~~ / `INSERT` / `RCTRL` / bouton souris latéral | **`INSERT` par défaut.** `F10` est à écarter : le dépouillement de `defaults-4.9.json` montre qu'il porte déjà `v_power_throttle_up` et `v_power_throttle_max`. Sur les 81 touches utilisées par Star Citizen, restent libres : `INSERT`, `DELETE`, `RCTRL`, `RSHIFT`, `PAUSE`, `SCROLLLOCK`, `NUMLOCK`, `NP_ENTER`, `F13`–`F15` |
| **Langue du catalogue livré** | FR seul / FR+EN | FR d'abord (ton usage), structure i18n en place dès le départ |
| **Diffusion** | privé / open source / produit | Change les priorités de packaging, de signature et de documentation — à décider avant la V1 |
| **Modèle Whisper par défaut** | `base` (rapide) / `small` (précis) | Décidé par le benchmark du spike S0-2, pas à l'avance |
