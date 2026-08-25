# PHASE 1bis — Analyse des keybinds Star Citizen (capture + export XML)

## 2.1 Constat sur ton export : `layout_Keybinds_1_exported.xml`

Le fichier fourni contient **exactement ceci** :

```xml
<ActionMaps version="1" optionsVersion="2" rebindVersion="2" profileName="Keybinds_1">
 <CustomisationUIHeader label="Keybinds_1" description="" image="">
  <devices>
   <keyboard instance="1"/>
   <mouse instance="1"/>
   <gamepad instance="1"/>
  </devices>
  <categories />
 </CustomisationUIHeader>
 <options type="keyboard" instance="1" Product="Clavier  {6F1D2B61-D5A0-11CF-BFC7-444553540000}"/>
 <options type="gamepad" instance="1" Product="Controller (Gamepad)"/>
 <modifiers />
</ActionMaps>
```

**Il ne contient aucun `<actionmap>` ni `<rebind>`.** Conséquences directes :

1. Ta configuration est **100 % les défauts Star Citizen** — tu n'as rebindé aucune touche, ou
   l'export a été fait sur un profil vierge. La capture d'écran correspond donc au layout par
   défaut du jeu (ce que confirme sa cohérence avec les defaults connus : `L` portes,
   `R` allumage, `B` quantum, `N` train d'atterrissage, `Tab` scan, `Space` strafe up…).
2. **Un export SC ne contient que le *delta*** : uniquement ce que l'utilisateur a modifié.
   C'est LE piège d'architecture : si Optimus se contente d'importer le XML utilisateur,
   il ne connaîtra **aucune** touche pour un joueur qui n'a rien rebindé.
3. Il faut donc **deux sources** : un **jeu de défauts embarqué** (par version de SC) + le
   **delta utilisateur** appliqué par-dessus. Un `BindingProfile` d'Optimus = `defaults ⊕ overrides`.
4. Le fichier confirme les 3 périphériques déclarés (`keyboard`, `mouse`, `gamepad` instance 1)
   et le GUID DirectInput du clavier — utile pour l'identification du device, inutile pour le MVP.

---

## 2.2 Format XML de Star Citizen — ce qu'il faut savoir

### Structure complète (avec rebinds)

```xml
<ActionMaps version="1" optionsVersion="2" rebindVersion="2" profileName="MonProfil">
  <CustomisationUIHeader label="MonProfil" description="" image="">
    <devices><keyboard instance="1"/><mouse instance="1"/><gamepad instance="1"/></devices>
    <categories/>
  </CustomisationUIHeader>
  <options type="keyboard" instance="1" Product="Clavier {GUID}"/>
  <modifiers/>

  <actionmap name="spaceship_general">
    <action name="v_toggle_all_doors">     <rebind input="kb1_l"/></action>
    <action name="v_toggle_landing_system"><rebind input="kb1_n"/></action>
  </actionmap>

  <actionmap name="spaceship_targeting">
    <action name="v_target_cycle_all_fwd"> <rebind input="kb1_lalt+t"/></action>
  </actionmap>
</ActionMaps>
```

### Syntaxe des tokens d'entrée

| Forme | Signification |
|---|---|
| `kb1_l` | clavier instance 1, touche `L` |
| `kb1_lshift+lctrl+1` | combinaison : les modificateurs ne re-préfixent pas le device |
| `kb1_np_5`, `kb1_f12`, `kb1_escape`, `kb1_lbracket` | touches nommées (pavé num., fonctions, ponctuation) |
| `mo1_mouse1` … `mo1_mouse8`, `mo1_mwheel_up/down` | souris |
| `js1_button3`, `js1_x`, `js1_hat1_up` | joystick / HOTAS |
| `gp1_a`, `gp1_shoulderl` | gamepad |
| `<rebind input=" "/>` (vide) | binding **supprimé** par l'utilisateur — cas à gérer explicitement |

### ⚠️ Deux formats XML différents, pas un seul (confirmé le 2026-08-24)

L'analyse du vrai `defaultProfile.xml` (220 Ko, 50 actionmaps, 1103 actions) montre que le jeu
utilise **deux syntaxes distinctes** selon le fichier :

| Fichier | Syntaxe | Exemple |
|---|---|---|
| `defaultProfile.xml` (défauts du jeu) | attributs sur `<action>` | `<action name="v_lights" keyboard="l" mouse=" " activationMode="press"/>` |
| `layout_*.xml` (export utilisateur) | élément `<rebind>` | `<action name="v_lights"><rebind input="kb1_l"/></action>` |

L'importeur doit gérer les deux. Autres pièges relevés sur le fichier réel :

- **Les modificateurs peuvent précéder ou suivre la touche** : `lalt+c` mais aussi `f6+lalt`,
  `u+lshift`. On les identifie par leur nom, **jamais par leur position**.
- **Casse incohérente** : `ralt+K` voisine avec `ralt+y`. Comparaison insensible à la casse.
- **La souris apparaît dans l'attribut `keyboard`** (`lalt+mwheel_up`, `ralt+mouse2`) autant que
  dans l'attribut `mouse` dédié.
- **Non assigné = chaîne vide *ou* une espace** (`mouse=" "`). 476 actions sur 1103 n'ont aucun
  binding par défaut.
- Préfixes spéciaux : `np_*` (pavé numérique), `mwheel_*`, `maxis_*` (axes analogiques),
  `HMD_*` (head tracking). Les deux derniers ne sont pas injectables au clavier : 61 actions
  concernées, exclues du profil.

### Les modes d'activation : déclarés par le jeu, pas devinés

`defaultProfile.xml` contient un bloc `<ActivationModes>` qui **définit lui-même les 18 modes**
avec leurs seuils. C'est une découverte structurante : les durées ne sont pas à inventer.

| Mode | Déclenche sur | Seuil déclaré | Conséquence pour l'injection |
|---|---|---|---|
| `press` (333 actions) | appui | — | tap court suffit |
| `tap` (187) | relâchement | doit être relâché en **< 0,25 s** | tap court **obligatoire** |
| `hold` (79) | appui + relâchement | — | maintien explicite |
| `delayed_press` (64) | appui | **≥ 0,25 s** | un tap de 45 ms **échoue silencieusement** |
| `all` (48) | tout | — | |
| `smart_toggle` (19) | appui/relâchement | délai 0,25 s | tap court = bascule |
| `double_tap` / `_nonblocking` (10) | 2 appuis | — | double appui |
| `delayed_press_medium` (3) | appui | **≥ 0,5 s** | autodestruction |
| `delayed_hold*` (7) | appui | 0,15 à **1,5 s** | |

**Conséquence directe** : un `hold_ms` global est faux. La durée doit être **dérivée du
`activationMode` de chaque action**, en lisant les seuils dans le fichier du jeu lui-même. Le
convertisseur [`tools/convert-default-profile.ps1`](../tools/convert-default-profile.ps1) le fait
automatiquement — 31 actions exigent plus de 45 ms, jusqu'à 1 580 ms.

### Une même touche, plusieurs actions : ce n'est pas toujours un conflit

```
spaceship_power/v_power_throttle_up    F10   activationMode = press
spaceship_power/v_power_throttle_max   F10   activationMode = double_tap
```

Même touche, même actionmap, deux actions — parfaitement légitime, c'est le **mode d'activation**
qui les distingue. La détection de conflits du Keybind Manager doit donc comparer le triplet
`(actionmap, input, activationMode)` et non la seule touche, sous peine de crier au loup sur des
dizaines de bindings valides.

### Où trouver les fichiers

| Quoi | Emplacement |
|---|---|
| Exports utilisateur | `<SC_INSTALL>\<CHANNEL>\USER\Client\0\Controls\Mappings\*.xml` (channel = `LIVE`, `PTU`, `EPTU`…) |
| Défauts du jeu | `<SC_INSTALL>\<CHANNEL>\Data.p4k` → `Data\Libs\Config\defaultProfile.xml` (extraction avec **unp4k**) |
| Rechargement in-game | console (`~`) → `pp_rebindkeys <nom_du_fichier>` |

**Stratégie de découverte du chemin** (ne jamais coder un chemin en dur, cf. §59 du brief) :
1. Clé de registre / fichier du **RSI Launcher** (`%APPDATA%\rsilauncher\log*` ou settings JSON) ;
2. sinon, processus `StarCitizen.exe` en cours → chemin de l'exécutable → remonter à `<CHANNEL>` ;
3. sinon, scan des racines de disques sur `*/StarCitizen/<CHANNEL>/Bin64/StarCitizen.exe` ;
4. sinon, **demander à l'utilisateur** (sélecteur de dossier), et mémoriser.

---

## 2.3 Ce que la capture d'écran apprend (analyse UX et technique)

### Volumétrie et modificateurs

La capture montre une seule page dense contenant l'intégralité du mapping : ~90 touches
utilisées, **4 couches de modificateurs** et un système de légende :

| Modificateur | Rôle observé sur la capture |
|---|---|
| `M1` = **Alt** | Modifier 1 — cyclage/variantes (`M1 Target Next - Attacker`, `M1 SA Focus L`) |
| `M2` = **Shift** | Modifier 2 (`M2 Flight Mode Wheel`) — aussi Afterburner en direct |
| `M3` = **Alt droit** | Modifier 3 (`M3 SA Fire L`, `M3 Eject`) |
| `*` | **Hold** (appui maintenu) |
| `**` | **Double Tap** |

Et **11 « modes contextuels »** signalés par des préfixes colorés :
`SC` Scan · `MG` Mining · `SA` Salvage · `SFH` Salvage Focus Heads · `IM` Interaction Mode ·
`TU` Turret · `LN` Landing · `QT` Quantum Travel · `ML` Missile · `AD` Advanced Camera ·
`AC` Arena Commander.

### Les 6 conclusions d'architecture qui en découlent

1. **Une même touche a N significations selon le mode.** `1` = `Lock Pin 1` en vol, mais
   `AD Load/Save 1` en mode caméra avancée. → Le binding n'est pas `action → touche` mais
   **`(action, contexte) → input`**. Le `BindingProfile` doit être indexé par *actionmap*
   (= contexte), exactement comme SC le fait.
2. **Les modes ne sont pas déductibles de l'extérieur.** Optimus ne *sait pas* si le joueur est
   en mode scan. → soit on demande au joueur (« Optimus, passe en mode minage » fixe un
   `GameContext.mode` interne), soit on exécute la séquence qui *force* le mode avant l'action.
   Le `GameStateProvider` doit exister dès le MVP, même en implémentation « déclarative ».
3. **Hold et Double Tap sont des citoyens de première classe**, pas des options exotiques :
   `M1 SA Fracture`, `Eject (double tap)`, `Exit Seat *`. → Le modèle `Action` doit porter
   `mode: tap | hold | double_tap | press | release` **dès le MVP**, avec `hold_ms`.
4. **Beaucoup d'actions sont des axes/cycles, pas des booléens.** `Power [-]/[+]`,
   `Shield Raise - Level Top`, `Speed Limiter [+/-]`. → Le langage de commande doit gérer la
   **répétition** (`repeat: 5`) et les **paramètres numériques** extraits de la voix
   (« Optimus, monte la puissance moteur de trois crans »).
5. **La souris fait partie du binding** (molette = spacing, boutons = fire groups). → Le moteur
   d'injection doit gérer souris **et** clavier dès la v0.1, pas en « plus tard ».
6. **La capture est un layout par défaut, donc universellement partageable comme préréglage.**
   → Optimus embarque `bindings/starcitizen/default-4.x.json` généré depuis `defaultProfile.xml`,
   et le joueur importe *ses* deltas par-dessus.

### Extrait du mapping par défaut (lu sur la capture) — sert de jeu de test

| Action | Touche | Action | Touche |
|---|---|---|---|
| Ouvrir/fermer les portes | `L` | Quantum Drive (Cruise Toggle) | `B` |
| Allumage vaisseau | `R` | Mode d'atterrissage | `N` |
| Train d'atterrissage | `N` (LN) | Scan Mode | `Tab` |
| Moteurs on/off | `I` | Boucliers on/off | `O` |
| Armes on/off | `P` | Distribution de puissance | `F8` |
| Puissance moteur ± | `F5` | Puissance boucliers ± | `F6` |
| Puissance armes ± | `F7` | Découplé | `C` |
| VTOL | `K` | Mode interaction | `F` |
| Cibler le plus proche hostile | `4` | Cible ami suivant | `6` |
| Contre-mesures leurre | `H` | Chaff | `J` |
| Frein spatial | `X` | Afterburner | `Shift` |
| Auto-atterrissage | `N` (M1) | Éjection | `Alt droit + Y` (double tap) |
| Salut / Scoreboard (AC) | `F1` | mobiGlas | `F1` |
| Carte stellaire | `F2` | Autodestruction | `Backspace` (hold) |

> ⚠️ Ce tableau est une **lecture d'image**, à re-vérifier automatiquement contre
> `defaultProfile.xml` avant d'être livré comme préréglage. Il sert de *fixture de test*
> pour valider l'import, pas de source de vérité.

---

## 2.4 Stratégie keybind d'Optimus (conséquence directe)

```
defaultProfile.xml (par version SC)      layout_XXX.xml (delta utilisateur)
            │                                        │
            └────────────┬───────────────────────────┘
                         ▼
              SC ActionMap Importer
                         │  normalise: kb1_lalt+t → { key:"T", mods:["ALT"] }
                         ▼
              BindingProfile (JSON, versionné)
              { "spaceship_general/v_toggle_all_doors": { key:"L", mods:[], mode:"tap" } }
                         │
      Command.action_ref ─┘   (les commandes ne connaissent QUE l'action id)
                         ▼
                 BindingResolver ──► InputEngine (SendInput scancode)
```

> **Mesuré au spike S0-1 (2026-08-23)** : la traduction `nom de touche → scancode` doit passer par
> une **table fixe en positions US**, jamais par `MapVirtualKey`. Sur une machine en AZERTY,
> `MapVirtualKey(VK_A)` renvoie `0x10` — la position QWERTY `Q` — alors que `kb1_a` de Star Citizen
> désigne `0x1E`. Cinq divergences sur six touches testées. Prototype de la table :
> `tools/Optimus.Spike.InputTest/src/ScanCodes.cs`.

**Règle d'or** : l'identifiant d'action d'Optimus est **calqué sur celui de Star Citizen**
(`spaceship_general/v_toggle_all_doors`). Bénéfice majeur : l'import devient une simple copie,
la mise à jour de SC devient un diff, et la couche « commande vocale » reste totalement découplée
du clavier. Les actions non-SC (plugins, système, Spotify…) utilisent leur propre espace de noms
(`plugin.spotify/play_pause`).

**Cas limites à traiter explicitement :**

| Cas | Comportement attendu |
|---|---|
| Action sans binding (`<rebind input=" "/>` ou absente) | Commande *connue* mais **non exécutable** → réponse vocale explicite : « La commande existe mais aucun raccourci n'est configuré. » |
| Binding en conflit (2 actions, même input, même actionmap) | Avertissement dans le Keybind Manager, exécution autorisée mais signalée |
| Binding sur un device absent (`js1_*` sans joystick) | Marqué `unbound_device`, proposé au remapping |
| Nouvelle version de SC introduisant/supprimant des actions | Rapport de migration : `+12 actions`, `-3 actions`, `5 renommées` |
| Utilisateur qui rebinde en jeu après l'import | Détection par watcher `FileSystemWatcher` sur `Mappings\` → proposition de re-synchro |
