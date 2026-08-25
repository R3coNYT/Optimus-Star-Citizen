# PHASE 9 — Interface

## 10.1 Principes de design

- **Avionique lisible, pas décorative.** Fond très sombre (`#05070f`), accent par copilote,
  typographies condensées pour les titres (Chakra Petch / Rajdhani) et grotesque neutre pour le
  corps (Inter). Effets (scanlines, glow) plafonnés à 8 % d'opacité et **désactivables**.
- **Un code couleur constant, partout** : vert = nominal, orange = dégradé, rouge = erreur,
  cyan = information/action, jaune = simulation active.
- **Toute erreur affichée porte son bouton de correction.**
- **La barre d'état est permanente** : micro, STT, TTS, LLM, jeu, simulation, kill switch —
  visibles depuis n'importe quel écran.
- **Densité maîtrisée** : la page de Jean-Bot montre 600 commandes ; la nôtre doit en montrer
  autant sans devenir illisible (cartes compactes + mode liste + mode HUD).

---

## 10.2 Navigation

```
┌────────────────────────────────────────────────────────────────────────────────┐
│  OPTIMUS  ▸ Command Center           ● SYSTEM ONLINE   ⏻ KILL SWITCH  [ – □ × ]│
├──────────────┬─────────────────────────────────────────────────────────────────┤
│ ▸ DASHBOARD  │                                                                 │
│   COMMANDS   │                                                                 │
│   KEYBINDS   │                        zone de contenu                          │
│   COPILOTS   │                                                                 │
│   PERSONALITY│                                                                 │
│   VOICE      │                                                                 │
│   AI         │                                                                 │
│   PROFILES   │                                                                 │
│   DISCORD    │                                                                 │
│   PLUGINS    │                                                                 │
│   LOGS       │                                                                 │
│   SETTINGS   │                                                                 │
├──────────────┴─────────────────────────────────────────────────────────────────┤
│ MIC ●  STT ●  TTS ●  LLM ○  SC ●  SIM ○      dernière: "ouvre les portes" 585ms│
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## 10.3 Écran par écran

### 1. DASHBOARD — *l'état du système en 2 secondes*

```
┌ SYSTEM ────────────┐┌ COPILOT ────────────┐┌ STAR CITIZEN ──────┐
│ ● ONLINE           ││   ┌───┐             ││ ● DETECTED  4.9.1  │
│ uptime 01:42:07    ││   │ ⬡ │  OPTIMUS    ││ ○ foreground: NON  │
│ mode: LOCAL        ││   └───┘  militaire  ││ profil: Default    │
│ simulation: OFF    ││ voix Denise · fr-FR ││ 312 binds · 4 libres│
└────────────────────┘└─────────────────────┘└────────────────────┘
┌ VOICE ─────────────────────────┐┌ PERFORMANCE ───────────────────┐
│ micro  Realtek Array  ▇▇▇▅▂    ││ latence moy.   612 ms          │
│ mode   push-to-talk (F10)      ││ p95            940 ms          │
│ STT    whisper-small ● prêt    ││ commandes/h    47              │
│ TTS    OneCore ● prêt          ││ taux de succès 96,2 %          │
│ LLM    ○ désactivé             ││ échecs 24 h    3               │
└────────────────────────────────┘└────────────────────────────────┘
┌ DERNIÈRE COMMANDE ─────────────────────────────────────────────────┐
│ 21:42:15  « optimus ouvre les portes »                             │
│ → ship.doors.toggle (1.00)  → L  → SUCCESS  585 ms   [ voir trace ]│
└────────────────────────────────────────────────────────────────────┘
┌ ACTIVITÉ ──────────────────────────────────────────────────────────┐
│ 21:42:15 ✓ ouvre les portes      21:41:02 ✓ mode combat            │
│ 21:40:47 ✗ « active le scan »  → aucun raccourci   [ configurer ]  │
└────────────────────────────────────────────────────────────────────┘
```

Chaque pastille est **cliquable** et mène à l'écran de réglage correspondant. Le bandeau jaune
« SIMULATION ACTIVE » barre l'écran quand le mode simulation est on — impossible de l'oublier.

### 2. COMMANDS — *le catalogue (réponse directe à `commandes.php`)*

```
COMMAND DATABASE                          312 commandes · 47 favorites
[ 🔍 rechercher…                    ]  [ ★ favoris ] [ ⊞ grille | ☰ liste | ▤ HUD ]
[Toutes][Ship][Combat][Navigation][Quantum][Mining][Salvage][Scanning][FPS][Social][Lore]…

┌──────────────────────────────┐ ┌──────────────────────────────┐
│ Ouvrir / fermer les portes ★ │ │ Mode combat               ★  │
│ ship · action                │ │ combat · macro (5 étapes)    │
│ « ouvre les portes » +5      │ │ « mode combat » +2           │
│ ⌨  L                         │ │ ⌨  séquence                  │
│ utilisée 42×   ⌀ 590 ms      │ │ utilisée 12×   ⌀ 1 240 ms    │
│ [ ▶ tester ] [ ✎ éditer ]    │ │ [ ▶ tester ] [ ✎ éditer ]    │
└──────────────────────────────┘ └──────────────────────────────┘
```

Reprises assumées de Jean-Bot : recherche avec **mode mot strict** (requête finissant par une
espace), filtres par catégorie, favoris, mode HUD compact, export « feuille de vol ».
Ajouts : recherche **par phrase vocale et par touche**, statistiques d'usage réelles, test en un
clic, badge « aucun raccourci » impossible à manquer.

### 3. KEYBINDS — *l'écran stratégique*

```
KEYBIND MANAGER          profil: [ Default ▾ ]  [ importer SC ] [ exporter ] [ reset ]
source: defaults-4.9.json ⊕ layout_Keybinds_1_exported.xml (23/08 21:10)   [ resynchro ]
[ 🔍 action ou touche… ]   ☐ non assignés seulement   ☐ conflits seulement

ACTION                              ACTIONMAP              TOUCHE         ÉTAT
Ouvrir/fermer les portes            spaceship_general      L              défaut
Allumage vaisseau                   spaceship_general      R              défaut
Quantum drive                       spaceship_movement     B              défaut
Cible suivante — hostile            spaceship_targeting    ALT + T        modifié
Éjection                            spaceship_general      RALT + Y ⚠     double tap
Scan mode                           spaceship_scanning     —  non assigné  ⚠
Autodestruction                     spaceship_general      RETOUR (1,5 s) dangereux 🔒

┌ CONFLIT ──────────────────────────────────────────────────────────┐
│ ⚠ « L » est assignée à : Ouvrir les portes  ET  Train (mode LN)   │
│   Actionmaps différents → OK dans SC, mais Optimus doit connaître │
│   le mode actif. [ définir la règle de mode ] [ ignorer ]         │
└───────────────────────────────────────────────────────────────────┘

┌ CAPTURE ──────────────────────────────────────────────────────────┐
│ Appuyez sur une touche…            détecté : CTRL + ALT + F       │
│ mode : ( ) tap  (•) hold 300 ms  ( ) double tap                   │
│ [ Enregistrer ]  [ Effacer le binding ]  [ Annuler ]              │
└───────────────────────────────────────────────────────────────────┘
```

### 4. COPILOTS

Galerie de cartes (avatar, couleur, langue, voix, nb de commandes actives, badge « actif »).
Actions : **Activer · Éditer · Dupliquer · Exporter (.optcopilot) · Supprimer**.
Éditeur en onglets : *Identité · Voix · Personnalité · Capacités · Commandes autorisées · IA*.

### 5. PERSONALITY — *l'écran qui vend le produit*

```
PERSONNALITÉ — Optimus                          [ ⟲ réinitialiser ] [ 🔊 aperçu ]
Formalisme   ▓▓▓▓▓▓▓▓░░ 80     Humour      ▓▓▓▓░░░░░░ 40
Verbosité    ▓▓▓░░░░░░░ 30     Sarcasme    ▓▓░░░░░░░░ 25
Calme        ▓▓▓▓▓▓▓▓▓░ 90     Chaleur     ▓▓▓▓░░░░░░ 45
Assurance    ▓▓▓▓▓▓▓▓▓░ 85     Agressivité ▓░░░░░░░░░ 10
Style  ☑ militaire ☑ sci-fi ☑ immersif ☑ technique
Adresse [ commandant, capitaine ]   Interdits [ lol, mdr, en tant qu'IA ]

┌ APERÇU EN DIRECT ─────────────────────────────────────────────────┐
│ succès quantum  « Calcul de trajectoire terminé. Accrochez-vous,  │
│                   commandant. »                    [ ▶ écouter ]  │
│ échec           « Négatif. Aucun raccourci configuré. »           │
│ inconnu         « Cette instruction ne figure pas dans mes        │
│                   protocoles. »                                   │
└───────────────────────────────────────────────────────────────────┘
RÈGLES  combat_active → réponses courtes   ·   échec → expliquer la cause   [ + ]
```

L'aperçu se recalcule **à chaque mouvement de curseur** : c'est ce qui rend les traits tangibles.

### 6. VOICE — périphériques, jauge d'entrée, mode d'écoute, PTT, seuils VAD, providers STT/TTS,
gestion des modèles (téléchargement, taille, benchmark), test « parlez maintenant » avec
affichage de la transcription et du temps.

### 7. AI — activation du LLM (**off par défaut, encadré explicatif** : ce qui est envoyé, à qui,
à quel coût), provider, modèles rapide/raisonnement, seuil d'escalade, budget mensuel,
compteur de tokens, bouton « tout traiter en local ».

### 8. PROFILES — profils utilisateur (langue, wake word, PTT, copilote préféré, profil de binding
associé), import/export, sauvegardes automatiques et restauration.

### 9. DISCORD — statut du bot, mode (local/relais), appairage par code à usage unique,
tableau des utilisateurs liés avec leurs permissions (cases à cocher), sélection des événements
notifiés, journal des commandes reçues de Discord.

### 10. PLUGINS — liste, activation, permissions demandées (affichées explicitement),
version SDK, commandes apportées, dossier d'installation, rechargement à chaud.

### 11. LOGS — vue temps réel filtrable par niveau/composant/trace, mode **PIPELINE TRACE**
(cf. `docs/09`), export d'un incident (JSON + logs + config anonymisée) pour le support.

### 12. SETTINGS — démarrage Windows, réduction dans la zone de notification, raccourcis globaux,
**mode simulation**, exigence de focus jeu, thème, langue de l'interface, chemin d'installation
de SC, mises à jour, réinitialisation.

---

## 10.4 Premier lancement (assistant, ≤ 5 étapes)

```
1. Bienvenue        →  langue de l'interface · nom du pilote
2. Micro            →  périphérique · test de niveau · PTT (défaut F10)
3. Star Citizen     →  détection auto du chemin · import des keybinds
                       « 312 actions importées, 4 sans binding »
4. Copilote         →  Optimus (voix, aperçu vocal, 3 curseurs rapides)
5. Essai            →  MODE SIMULATION FORCÉ : dites « Optimus, ouvre les portes »
                       affichage de la trace complète, puis [ activer pour de vrai ]
```

L'étape 5 en simulation est délibérée : l'utilisateur voit le système fonctionner **avant** que
quoi que ce soit ne touche son clavier. C'est le meilleur argument de confiance possible pour une
application qui demande le droit d'appuyer sur des touches.

---

## 10.5 Zone de notification et mode compact

- Icône de notification : état par la couleur, menu contextuel
  (activer/désactiver le micro, simulation, kill switch, ouvrir, quitter).
- **Mode compact** : fenêtre réduite (~360 × 120), toujours au-dessus, affichant l'état d'écoute,
  la dernière commande et le bouton kill switch — pour un second écran ou un cockpit.
- **Mode HUD/overlay** (V2) : superposition transparente in-game.
