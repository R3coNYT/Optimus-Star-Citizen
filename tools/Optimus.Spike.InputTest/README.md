# Spike S0-1 — Injection clavier / souris

> Répond à la question bloquante du projet (risque **R1** de [`docs/13`](../../docs/13-risques-tests-decisions.md)) :
> **Star Citizen accepte-t-il une injection `SendInput` en scancode ?**
> Tant que la réponse n'est pas connue, toute la couche d'exécution d'Optimus est hypothétique.

## Exécution

Aucune installation n'est nécessaire : les sources sont volontairement limitées à **C# 5** pour
être compilables à la fois par le SDK .NET 8 et par le compilateur intégré à Windows
(PowerShell `Add-Type`). Le script détecte ce qui est disponible.

```powershell
.\tools\Optimus.Spike.InputTest\run-spike.ps1
```

### Mode `probe` (défaut) — vérification automatique, sans le jeu

Injecte des touches sans effet (`F13`/`F14`) et vérifie ce que le système en fait, via deux sondes :

| Sonde | Ce qu'elle prouve |
|---|---|
| **Hook bas niveau** (`WH_KEYBOARD_LL`) | l'évènement entre dans la file d'entrée, avec son scancode ; expose `LLKHF_INJECTED`, le drapeau qu'inspectent les anti-triches |
| **Raw Input** (`WM_INPUT`, `RIDEV_INPUTSINK`) | l'évènement atteint **le chemin d'entrée que lisent les moteurs de jeu** (dont CryEngine) avec le bon make code |

Chaque injection porte une signature dans `dwExtraInfo` (`0x4F505431`), ce qui permet de
distinguer nos évènements synthétiques des frappes réelles de l'utilisateur.

### Mode `game` — plan d'observation dans Star Citizen

```powershell
.\run-spike.ps1 --mode game --key L --hold-key SPACE --modifier LALT
```

**Protocole recommandé :**

1. Lance Star Citizen, monte dans un vaisseau, **posé, moteurs coupés, hors combat**.
2. Vérifie sur la ligne `Élévation` du rapport : si Star Citizen est élevé et pas le spike,
   relance PowerShell en administrateur (sinon UIPI bloque l'injection et le test est invalide).
3. Lance la commande, appuie sur Entrée, puis **bascule sur la fenêtre du jeu** : le compte à
   rebours démarre dès que le jeu passe au premier plan.
4. Observe. Chaque test est annoncé par une série de bips (n bips = test n).
5. Reviens sur la console pour répondre aux questions ; un rapport Markdown est écrit dans
   `docs/spikes/`.
6. **Refais le test une seconde fois en plein écran exclusif** si tu joues dans ce mode : le
   comportement peut différer du plein écran fenêtré.

**Arrêt d'urgence : touche `Échap`** — détectée en continu par le hook (seules les frappes
*réelles* comptent, pas nos injections).

### Options

| Option | Défaut | Rôle |
|---|---|---|
| `--mode probe\|game` | `probe` | vérification automatique ou plan d'observation |
| `--target <processus>` | `StarCitizen` | processus cible en mode `game` |
| `--key <TOUCHE>` | `L` | touche testée (défaut SC : ouvrir/fermer les portes) |
| `--hold-key <TOUCHE>` | `SPACE` | touche du test de maintien long |
| `--modifier <TOUCHE>` | `LALT` | modificateur testé |
| `--doubletap-key <TOUCHE>` | *(désactivé)* | test de double appui |
| `--include-mouse-right` | non | ajoute un clic droit — **peut faire feu en jeu** |
| `--probe-key <TOUCHE>` | `F13` | touche d'essai du mode probe |
| `--gap <ms>` / `--countdown <s>` | 2500 / 5 | rythme du plan |
| `--report <chemin>` | `docs/spikes/…` | destination du rapport |
| `--list-keys` | | noms de touches reconnus |

> ⚠️ **Ne teste jamais l'éjection** (`RALT + Y` en double appui) ni l'autodestruction
> (`Retour arrière` maintenu) avec cet outil. Ce sont précisément les actions que le moteur
> classera `dangerous` et soumettra à confirmation.

## Résultats obtenus le 2026-08-23 (mode `probe`)

Machine : `R3CON-PORTABLE`, Windows 11 26200, x64, utilisateur standard.
Rapport complet : [`docs/spikes/S0-1-probe-20260823-232510.md`](../../docs/spikes/S0-1-probe-20260823-232510.md)

| Test | Verdict | Enseignement |
|---|---|---|
| T1 injection scancode | **PASS** | `F13` (0x64) arrive **intact dans le Raw Input**, make + break. La méthode fonctionne au niveau système. |
| T2 virtual-key seule | **INFO déterminant** | Le Raw Input reçoit **`MakeCode = 0x00`**. Un moteur lisant le scancode physique ne verra **rien**. → `KEYEVENTF_SCANCODE` est **obligatoire**, ce n'est pas une préférence. |
| T3 touche étendue | **PASS** | Le préfixe `E0` est correctement propagé (RCtrl). |
| T4 durées de maintien | **PASS** | Écart mesuré **+0,6 à +1,4 ms** de 8 ms à 120 ms, grâce à `timeBeginPeriod(1)` + attente active. Un maintien de 16 ms est donc réellement produisible. |
| T5 double appui | **PASS** | 4 évènements, écart mesuré 61,6 ms pour 60 ms demandés. |
| T6 combinaison | **PASS** | Ordre `MOD↓ KEY↓ KEY↑ MOD↑` respecté. |
| T7 disposition clavier | **INFO critique** | **Ta machine est en AZERTY** : `MapVirtualKey(VK_A)` renvoie `0x10` (position QWERTY `Q`) alors que `kb1_a` de Star Citizen vaut `0x1E`. 5 divergences sur 6 touches testées. |
| T8 souris | **PASS** | Bouton X2 et molette acceptés par `SendInput`. |

### Ce que ces résultats changent pour Optimus

1. **`KEYEVENTF_SCANCODE` est obligatoire** — prouvé, pas supposé (T2).
2. **Interdiction d'utiliser `MapVirtualKey` dans le moteur** (T7). La table de scancodes doit
   être **fixe, en positions US**, comme le nommage `kb1_*` de Star Citizen. C'est exactement le
   piège dans lequel tombent la plupart des outils d'automatisation : ils fonctionnent chez leur
   auteur en QWERTY et envoient la mauvaise touche chez un joueur AZERTY.
   → `src/ScanCodes.cs` est le prototype de la table définitive de `Optimus.Infrastructure.Input`.
3. **`hold_ms` par défaut = 45 ms** reste le bon choix, avec la certitude que le moteur sait
   produire des durées précises (T4). `timeBeginPeriod(1)` devra être actif pendant l'exécution
   d'une séquence.
4. **Les évènements portent `LLKHF_INJECTED`** (T1). C'est inévitable avec `SendInput` : tout
   anti-triche filtrant sur ce drapeau verra nos injections. Seule la partie `game` du spike peut
   dire si Star Citizen s'en soucie — d'où l'importance de l'exécuter avant d'écrire le moteur.

## Résultats en jeu — 2026-08-24 (mode `game`, R3CON-PC)

Star Citizen lancé **sans élévation**, spike en utilisateur standard, vaisseau sous tension.
Rapport : [`docs/spikes/S0-1-game-20260824-005151-R3CON-PC-moteur-allume.md`](../../docs/spikes/S0-1-game-20260824-005151-R3CON-PC-moteur-allume.md)

| Test | Verdict | Enseignement |
|---|---|---|
| G1 injection scancode | **PASS** | **Star Citizen réagit.** Le risque R1 est levé, le plan A est validé. |
| G2 virtual-key seule | **FAIL** | Le jeu **n'y réagit pas** — exactement ce que la sonde Raw Input prédisait avec `MakeCode = 0x00` (T2). Prédiction système confirmée par le comportement réel. |
| G3 appui de 16 ms | **PASS** | Le jeu accepte des appuis très courts → `hold_ms` par défaut fixé à 45 ms (marge ×3). |
| G4 maintien de 800 ms | **PASS** | Un maintien est bien perçu comme tel sur toute sa durée. |
| G5 combinaison `LALT+L` | *non concluant* | Aucune action assignée à cette combinaison : rien à observer. Voir ci-dessous. |
| G7 bouton souris X2 | *non concluant* | Bouton non assigné en jeu. Voir ci-dessous. |

> Un premier passage a été fait moteur éteint : non observable, puisque `L` commande les lumières
> et `Espace` la montée du vaisseau. **Leçon de protocole : le vaisseau doit être sous tension**,
> et les touches choisies doivent produire un effet visible immédiatement.

### Confirmer G5 et G7 sans deviner : l'écran de keybindings du jeu

Pour valider une combinaison ou un bouton souris **sans dépendre d'une action assignée**, le plus
fiable est d'utiliser la capture de touche de Star Citizen lui-même :

1. En jeu : `Options ▸ Keybindings`, cliquer sur une action pour la mettre en attente de saisie.
2. Basculer sur la console, lancer le spike ciblant le jeu.
3. Le jeu **affiche la combinaison qu'il perçoit** (`kb1_lalt+l`, `mo1_mouse5`…).
4. Annuler sans enregistrer.

C'est la vérification la plus directe possible : on lit ce que le moteur du jeu comprend, pas ce
qu'on croit lui avoir envoyé. Utilisable aussi plus tard pour diagnostiquer n'importe quel
binding récalcitrant.

### Reste à faire

- Second passage en **plein écran exclusif**, si tu joues dans ce mode.

## Procédure sur un autre PC (celui où tourne le jeu)

L'outil est autonome : **rien à installer**, il suffit de copier le dossier
`Optimus.Spike.InputTest` (clé USB, OneDrive, `git clone`…). Le rapport est écrit à côté du
script si le dépôt n'est pas là.

```powershell
# Depuis le dossier copié, si PowerShell bloque les scripts :
powershell -ExecutionPolicy Bypass -File .\run-spike.ps1 --mode game --key L
```

**Checklist d'une session sur le PC de jeu** — tout se fait en une fois :

| # | Action | Produit |
|---|---|---|
| 1 | `run-spike.ps1` (mode probe, jeu fermé) | rapport A — valide l'injection **et la disposition clavier de cette machine** (T7 est propre à chaque PC) |
| 2 | Lancer SC, s'installer dans un vaisseau **posé, moteurs coupés, hors combat**, en **plein écran fenêtré** | |
| 3 | `run-spike.ps1 --mode game --key L --hold-key SPACE` | rapport B — la réponse à R1 |
| 4 | Si tu joues en **plein écran exclusif**, refaire l'étape 3 dans ce mode | rapport C |
| 5 | Extraire `Data\Libs\Config\defaultProfile.xml` du `Data.p4k` avec **unp4k**, et récupérer ce fichier | alimente le spike S0-4 et le catalogue de bindings livré |
| 6 | Noter CPU / GPU / RAM de la machine | dimensionne le spike S0-2 (modèle Whisper par défaut) |

Ramène simplement les fichiers `.md` produits : ils contiennent tout le détail des mesures.

> Si la ligne `Élévation` du rapport indique `utilisateur standard` alors que Star Citizen est
> lancé en administrateur, l'injection sera bloquée par UIPI et le test sera **invalide** :
> relance PowerShell en administrateur.

## Mode `voice` — spike S0-3 (push-to-talk et capture micro)

```powershell
.\run-spike.ps1 --mode voice --utterances 5
```

Maintiens `Inser`, prononce la phrase affichée, relâche. L'outil enregistre un WAV par énoncé
(PCM 16 kHz mono, le format attendu par Whisper) dans `docs\spikes\audio\`, et mesure :

- si `RegisterHotKey` reçoit la touche **pendant que Star Citizen a le focus** ;
- si le hook bas niveau la voit, lui seul donnant l'appui **et** le relâchement ;
- le délai entre l'appui et le premier échantillon capturé ;
- le niveau crête et le niveau RMS de chaque énoncé.

`--ptt <TOUCHE>` change la touche (`Inser` par défaut : vérifié libre de toute action Star
Citizen — contrairement à `F10`, qui porte `v_power_throttle_up`). `--list-mics` liste les
périphériques, `--mic <index>` en choisit un.

**Les WAV produits alimentent le spike S0-2** ([`tools/bench-stt.ps1`](../bench-stt.ps1)) : vrai
micro, vraie voix, vrai bruit de fond — bien plus représentatif que des échantillons de synthèse.

## Dépannage

| Symptôme | Cause | Solution |
|---|---|---|
| `Impossible de lier l'argument au paramètre «Path»` | version antérieure au 2026-08-24 : l'outil placé à la **racine d'un lecteur** (`G:\Optimus.Spike.InputTest`) faisait remonter `Split-Path` au-delà de la racine | corrigé ; sinon, placer le dossier un cran plus bas (`G:\optimus\…`) |
| PowerShell **se ferme brutalement** au 2ᵉ lancement, `Sonde rawinput : INDISPONIBLE` | version antérieure au 2026-08-24 : la classe de fenêtre Win32 portait un nom fixe et survivait au 1ᵉʳ lancement dans le même processus ; au 2ᵉ, Windows rappelait un délégué déjà collecté par le GC → exception native fatale | corrigé (nom de classe unique par instance, délégués maintenus en vie, désenregistrement de la classe, et boucle de messages qui ne peut plus tuer l'hôte) |
| Un correctif des `.cs` ne semble pas pris en compte | .NET ne sait pas décharger un assembly : les types compilés restent chargés pour toute la vie de la console | **fermer et rouvrir PowerShell** après chaque mise à jour des sources (le script l'annonce en jaune) |
| `Sonde hook : INDISPONIBLE` | politique de sécurité ou logiciel tiers bloquant les hooks | les résultats Raw Input restent exploitables, mais `LLKHF_INJECTED` ne sera pas rapporté |

## Limites connues de l'outil

- La sonde Raw Input **n'écoute que le clavier** : la souris n'est validée que par la valeur de
  retour de `SendInput` et par l'observation en jeu.
- `LLKHF_INJECTED` n'est pas exposé par Raw Input : seul le hook peut le rapporter.
- L'outil ne teste **pas** le plan B (pilote *Interception*), qui n'a de sens que si G1 échoue.
- Aucun gamepad / HOTAS : hors périmètre du MVP.
