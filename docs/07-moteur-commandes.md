# PHASE 6 — Architecture des commandes

## 7.1 Les huit concepts et leurs relations

```
   phrase vocale                                       « ouvre-moi les portes »
        │
        ▼
   ┌─────────┐  ce que l'utilisateur veut (abstrait, scoré, paramétrable)
   │ INTENT  │  { intent_id, parameters, confidence, source }
   └────┬────┘
        │ 1:1  (un intent_id désigne exactement une Command)
        ▼
   ┌─────────┐  l'unité fonctionnelle déclarée : phrases, conditions, réponses
   │ COMMAND │  kind = action | macro | dialogue | lore | query
   └────┬────┘
        │ 1:n
        ▼
   ┌─────────┐  une étape exécutable
   │ ACTION  │  type = game_action | key | mouse | wait | repeat | if | say | plugin
   └────┬────┘
        │ (si type = game_action)  action_id abstrait, jamais une touche
        ▼
   ┌─────────┐  la table locale (action_id, actionmap) → InputSpec
   │ BINDING │  propre à la machine, importée de Star Citizen, éditable
   └────┬────┘
        ▼
   ┌───────────┐  ordre matériel : scancode down/up, bouton souris, molette
   │ INPUTSPEC │
   └───────────┘

   SEQUENCE  = liste ordonnée d'ACTIONs, exécutée par le SequenceRunner
   MACRO     = SEQUENCE nommée, réutilisable, déclenchable par la voix (= une Command kind=macro)
   CONDITION = prédicat évaluable (requirements d'une Command, branches `if` d'une Macro)
   RESPONSE  = ce que le copilote dit ; jamais le résultat direct de l'action, toujours filtré
               par la personnalité
```

**L'invariant fondamental** : le seul lien entre le monde « sens » (intent/command) et le monde
« matériel » (touche) est la table `BINDING`. Cette table est une **donnée**, jamais du code
(RT-02). Corollaire : on peut exécuter tout le moteur en CI, sans clavier, en injectant un
`BindingProfile` de test et un `SimulatedInputEngine`.

---

## 7.2 Types de commande (`kind`)

| `kind` | Exécute | Répond | Exemple | Origine de l'idée |
|---|---|---|---|---|
| `action` | 1..n étapes | oui | « ouvre les portes » | — |
| `macro` | séquence complexe avec conditions | oui | « passe en mode combat » | §46 |
| `dialogue` | **rien** | oui, variantes de personnalité | « t'as vu ça ?! » | 46 entrées « Dialogue » chez Jean-Bot |
| `lore` | rien, consultation de données | oui, contenu long | « parle-moi de Crusader » | 17 « Fiche » + catégorie LORE |
| `query` | lecture d'état interne | oui | « rapport système », « quelle est ma latence ? » | §39 |

Ce champ évite l'écueil du « programme qui appuie sur des touches » : 20 % du catalogue peut
n'appuyer sur rien et rester la partie la plus appréciée du produit.

---

## 7.3 Catégories (énumération fermée)

`ship` · `flight` · `navigation` · `quantum` · `combat` · `weapons` · `shields` · `power` ·
`targeting` · `scanning` · `mining` · `salvage` · `cargo` · `exploration` · `landing` ·
`takeoff` · `camera` · `communication` · `vehicle` · `fps` · `social` · `immersion` · `lore` ·
`system` · `ai` · `media` · `plugin`

Validées par schéma au chargement. Une catégorie inconnue = erreur de lint, pas un fallback
silencieux (leçon `category.id` cassé de Jean-Bot).

---

## 7.4 Algorithme de résolution d'intent

```
Entrée : texte brut de la transcription, contexte, copilote actif

1. NORMALISATION
   minuscules → suppression des accents → ponctuation → élisions ("ouvre-moi" → "ouvre moi")
   → nombres en lettres → chiffres ("trois" → "3")
   → retrait des mots vides de commande ("stp", "s'il te plait", "euh", "allez")
   → retrait du wake word en tête

2. CANDIDATS
   a. Correspondance EXACTE sur l'index des voice_phrases normalisées   → score 1.00
   b. Correspondance de PRÉFIXE / d'inclusion de phrase                 → 0.90–0.98
   c. Correspondance FLOUE : token-set ratio + Levenshtein normalisé    → 0.50–0.92
      pondérée par : longueur de phrase, usage récent (command_stats),
      catégorie compatible avec le GameContext, favoris de l'utilisateur
   d. (V1) Rappel SÉMANTIQUE par embeddings, si a/b/c < seuil           → 0.50–0.85

3. FILTRAGE
   commandes désactivées, hors capacités du copilote, catégories interdites → écartées

4. DÉCISION
   meilleur ≥ 0.85 et écart au 2ᵉ ≥ 0.15   → EXÉCUTION
   meilleur ≥ 0.85 et écart < 0.15         → DÉSAMBIGUÏSATION (question fermée)
   0.55 ≤ meilleur < 0.85                  → CONFIRMATION ("Vous voulez dire … ?")
   < 0.55 et LLM activé                    → ESCALADE LLM
   < 0.55 et LLM désactivé                 → UNKNOWN (réponse + log unknown_phrase)

5. EXTRACTION DE PARAMÈTRES
   patterns déclarés par la commande : {quadrant}, {value:int}, {target}
   valeurs manquantes → SlotFiller (contexte) → sinon relance ciblée
```

**Tous les seuils sont des paramètres**, exposés dans les réglages avancés et journalisés dans le
mode debug. Ils seront ajustés empiriquement avec les données de `unknown_phrases`.

### Anaphores et slots ouverts (§18)

```
tour 1  « prépare les boucliers »
        → intent shields.set_quadrant, paramètre {quadrant} manquant
        → ConversationContext.pending_slot = { intent, slot: "quadrant", ttl: 15 s }
        → réponse : « Quel quadrant ? »
tour 2  « à l'avant »
        → le matcher voit pending_slot actif : il tente D'ABORD de résoudre le slot
        → "avant" ∈ enum{avant, arrière, gauche, droite, équilibré} → OK
        → exécution de shields.set_quadrant(front)
```

Le `pending_slot` expire (TTL) et est annulé par : nouvelle commande à score élevé, kill switch,
ou « laisse tomber ».

---

## 7.5 Le contrat de l'IA (§73–74, §86)

Le LLM ne reçoit qu'un **catalogue d'intents autorisés** (id + description + paramètres), le
texte, et un résumé de contexte. Il ne renvoie **que** ceci :

```json
{
  "type": "command",
  "intent": "ship.doors.toggle",
  "parameters": {},
  "confidence": 0.94,
  "requires_confirmation": false,
  "reasoning": "l'utilisateur demande l'ouverture des sas"
}
```

ou

```json
{ "type": "conversation", "reply_hint": "l'utilisateur commente un événement de combat" }
```

ou

```json
{ "type": "clarification", "question_key": "shields.which_quadrant",
  "options": ["front", "rear", "left", "right"] }
```

**Cinq verrous appliqués après la réponse du LLM, dans cet ordre :**
1. Parsing strict du JSON (mode JSON/grammar contrainte côté provider) — échec ⇒ rejet.
2. `intent` ∈ liste blanche des commandes **activées pour ce copilote** — sinon rejet + log de
   sécurité `llm_intent_rejected`.
3. Paramètres validés contre le schéma déclaré par la commande (types, énumérations, bornes).
4. `ExecutionGuard` appliqué normalement (permissions, dangerous, focus, cooldown).
5. `confidence` du LLM plafonnée : elle ne peut **jamais** contourner la confirmation exigée par
   une commande `dangerous`.

Le LLM ne voit jamais un keybind, ne peut jamais produire une touche, et n'a aucun accès à
`IInputEngine`. Le test d'architecture vérifie qu'aucun projet `*.Ai.*` ne référence `*.Input.*`.

---

## 7.6 Langage de séquence

```jsonc
[
  { "type": "game_action", "action_id": "spaceship_power/v_power_preset_combat", "mode": "tap" },
  { "type": "key",   "key": "F", "mods": ["SHIFT"], "mode": "hold", "hold_ms": 300 },
  { "type": "mouse", "button": "right", "mode": "press" },
  { "type": "wait",  "ms": 150 },
  { "type": "repeat","times": 3, "interval_ms": 60,
    "steps": [ { "type": "game_action", "action_id": "spaceship_power/v_power_increase_weapons" } ] },
  { "type": "if",    "condition": { "type": "game_mode_is", "value": "combat" },
    "then": [ … ], "else": [ … ] },
  { "type": "say",   "response_key": "macro.combat_mode.done" },
  { "type": "plugin","plugin": "spotify", "call": "pause" },
  { "type": "mouse", "button": "right", "mode": "release" }
]
```

**Garanties d'exécution du `SequenceRunner` :**

| Garantie | Mécanisme |
|---|---|
| Aucune touche ne reste enfoncée | `try/finally` : toutes les touches/boutons `press` sans `release` sont relâchés à la sortie, y compris sur exception, annulation ou kill switch |
| Annulation immédiate | `CancellationToken` propagé à chaque étape ; kill switch = `Cancel()` + relâchement global |
| Pas de chevauchement | Une seule séquence à la fois par profil ; une nouvelle commande annule ou est mise en file selon `sequence_policy` |
| Délais réalistes | `hold` par défaut 45 ms (un jeu ignore souvent < 16 ms) ; jitter optionnel ±10 ms |
| Perte de focus en cours | La séquence est interrompue et le résultat marqué `aborted_focus_lost` |
| Traçabilité | Chaque étape journalisée avec son `trace_id`, visible dans le mode debug |

---

## 7.7 Conditions disponibles (`requirements` et `if`)

| Type | Vrai quand | Dispo |
|---|---|---|
| `game_running` | `StarCitizen.exe` détecté | M |
| `game_foreground` | le jeu a le focus | M |
| `binding_available` | l'`action_id` a un binding non vide | M |
| `simulation_off` | on n'est pas en mode simulation | M |
| `cooldown_elapsed` | implicite, géré par le guard | M |
| `copilot_capability` | la capacité est activée sur le copilote | M |
| `game_mode_is` | `GameContext.mode` (déclaratif en v0.1) | 1 |
| `previous_command_was` | dernier intent exécuté | 1 |
| `plugin_condition` | prédicat fourni par un plugin | 1 |
| `telemetry` | état de jeu réel (V2) | 2 |

Une condition non satisfiable produit **toujours** un message explicite (RF-ERR), jamais un
échec silencieux : « aucun raccourci configuré pour *ouvrir les portes* — voulez-vous le
définir maintenant ? »

---

## 7.8 Cycle de vie d'une commande utilisateur (Command Builder, §48)

```
UI Command Builder
   nom, catégorie, phrases vocales
   étapes : [Appuyer F] [Attendre 100 ms] [Appuyer 1] [Maintenir clic droit]
      │
      ▼
 Validation : phrases non ambiguës vs catalogue existant (avertissement si score > 0.85
              avec une commande existante), action_id connus, durées plausibles
      │
      ▼
 Écriture dans commands/user.custom.json  (source: "user", jamais mélangé au catalogue livré)
      │
      ▼
 Rechargement à chaud du CommandRegistry + réindexation du PhraseIndex
      │
      ▼
 Test en un clic : exécution en mode simulation avec affichage pas-à-pas
```

La génération assistée par IA (§49) s'insère **avant la validation** : le LLM propose un
brouillon de commande, qui suit ensuite exactement le même chemin de validation humaine. L'IA
n'écrit jamais directement dans le catalogue.
