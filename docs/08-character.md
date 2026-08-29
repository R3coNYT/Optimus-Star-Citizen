# PHASE 7 — Architecture de personnalité (« l'âme »)

## 8.1 Le problème à résoudre

Une personnalité crédible ne se réduit ni à une voix, ni à un prompt système. Elle se manifeste
dans **quatre dimensions indépendantes** :

| Dimension | Question | Support technique |
|---|---|---|
| **Quoi dire** | quelle information transmettre | `ResponseSet` + `intent` |
| **Comment le dire** | ton, longueur, vocabulaire | `PersonalityEngine` (traits) |
| **Quand parler** | accuser réception ? se taire ? relancer ? | `BehaviorRules` + contexte |
| **Avec quelle voix** | timbre, débit, hauteur | `VoiceConfig` (modulée par les traits) |

Jean-Bot résout tout par la force brute (8 000 WAV enregistrés). Optimus doit obtenir le même
résultat de façon **paramétrique et déterministe** — sans dépendre d'un LLM, qui reste optionnel.

---

## 8.2 Modèle de données

```
Personality
├── traits{}          8 curseurs 0–100     ← le cœur
├── style{}           drapeaux booléens    ← registre (militaire, sci-fi, technique…)
├── speech{}          débit, pitch, longueur max de phrase
├── lexicon{}         formes d'adresse, phrases préférées/interdites, remplacements
├── rules[]           when → behavior (priorisées)
└── llm{}             (optionnel) fragments de prompt système générés depuis les traits
```

### Les huit traits et leur effet **mécanique**

| Trait | Effet concret et mesurable |
|---|---|
| `formality` | Choix du registre : tutoiement/vouvoiement, « reçu » vs « ok », forme d'adresse |
| `verbosity` | Budget de mots de la réponse : `max_words = 4 + verbosity × 0.20` (30 → 10 mots) |
| `humor` | Débloque les variantes `requires.humor_min`, et pondère leur tirage |
| `sarcasm` | Idem, avec un **plafond contextuel** : jamais en combat, jamais après un échec |
| `aggression` | Ponctuation et attaque de phrase (« Exécution. » vs « C'est fait, prenez votre temps. ») |
| `calmness` | Vitesse de parole (`speed` modulée −10 %/+15 %) et réaction aux alertes |
| `warmth` | Marqueurs d'attention (« bien reçu, commandant », vœux, encouragements) |
| `confidence` | Présence ou non de modalisateurs (« je pense que », « probablement ») |

Chaque trait doit avoir **au moins un effet observable sans LLM**. Un curseur qui ne change rien
en mode local est un curseur décoratif — interdit.

---

## 8.3 Algorithme de sélection de réponse

```
Entrée : response_key, événement (success/fail/unknown), contexte, personnalité, historique récent

1. CANDIDATS      variantes de responses[response_key][event]
                  + variantes génériques de l'événement (fallback)
2. ÉLIGIBILITÉ    filtre sur requires{} : humor_min, sarcasm_min, formality_range,
                  style flags, langue, contexte (combat/calme)
3. CONTEXTE       règles actives (voir §8.4) :
                  combat_active   → ne garder que les variantes courtes (< 8 mots)
                  command_failed  → interdire humour et sarcasme, exiger une cause
4. ANTI-RÉPÉTITION  on écarte les N=3 dernières variantes utilisées pour cette clé
                    (mémoire circulaire en session) — le point qui fait TOUT le naturel
5. PONDÉRATION    poids_final = weight × affinité_traits × récence_inverse
                  affinité_traits = produit des proximités entre les traits requis et réels
6. TIRAGE         aléatoire pondéré (graine dérivée du trace_id → reproductible en test)
7. COMPOSITION    interpolation {variables}, application du lexique
                  (address_user, replacements), suppression des phrases interdites,
                  troncature au budget de mots
8. PROSODIE       speed/pitch ajustés : calmness, urgence de l'événement, longueur
```

Aucune étape n'appelle le réseau. Le LLM, s'il est activé, n'intervient qu'à l'étape 1 pour
**ajouter** une variante générée (pour les commandes conversationnelles ou quand aucune variante
n'existe) — puis subit les étapes 3 à 8 comme les autres. Il ne court-circuite jamais le filtre
de phrases interdites.

---

## 8.4 Règles comportementales

```json
{ "when": "combat_active", "behavior": "short_responses", "priority": 100,
  "params": { "max_words": 8, "disable_humor": true } }
```

| `when` | Détection | `behavior` |
|---|---|---|
| `combat_active` | `GameContext.mode == combat` (déclaratif v0.1) | `short_responses` |
| `command_failed` | résultat = failed | `explain_reason` |
| `command_unknown` | résolution < seuil | `ask_clarification` |
| `user_is_angry` | lexique/prosodie (V1) | `remain_calm` |
| `repeated_failure` | ≥ 3 échecs du même intent | `suggest_fix` (propose le Keybind Manager) |
| `idle_long` | ≥ N min sans interaction | `occasional_banter` (désactivable, off par défaut) |
| `startup` / `game_launched` / `game_closed` | événements système | `greet` / `announce` |
| `dangerous_command` | `command.dangerous` | `require_confirmation` |

Résolution : les règles applicables sont triées par priorité, leurs `params` sont fusionnés,
la plus prioritaire l'emporte en cas de conflit. L'ensemble actif est affiché en mode debug —
sans quoi le comportement du copilote devient inexplicable pour l'utilisateur.

---

## 8.5 Génération du prompt système (quand le LLM est activé)

Le prompt n'est **pas** écrit à la main par l'utilisateur : il est **composé** depuis les traits,
avec un fragment libre optionnel. Cela garantit la cohérence entre le mode local et le mode LLM.

```
Tu es {name}, {role} à bord du vaisseau de {user}.
Registre : {style_sentences}                        ← dérivé de style{} + formality
Ton : {tone_sentences}                              ← dérivé de humor/sarcasm/warmth/confidence
Longueur : {max_words} mots maximum, une à deux phrases.
Adresse-toi à l'utilisateur par : {address_user}.
N'utilise jamais : {forbidden_phrases}.
Tu ne peux JAMAIS exécuter d'action toi-même : tu proposes un intent parmi la liste fournie.
Si aucun intent ne correspond, réponds en conversation.
{custom_prompt_fragment}
```

**Verrou** : le prompt système est concaténé côté application, jamais fourni brut par une source
externe (plugin, pack importé, Discord). Un pack `.optcopilot` ne peut proposer qu'un
`custom_prompt_fragment` **borné en longueur et échappé**, jamais remplacer les règles de sécurité.

---

## 8.6 Trois personnalités de référence

| | **Optimus** | **Synthia** | **Virgil** |
|---|---|---|---|
| Rôle | Copilote militaire | Assistante synthétique | Officier d'armement |
| `formality` | 80 | 45 | 95 |
| `humor` | 40 | 75 | 5 |
| `sarcasm` | 25 | 65 | 0 |
| `verbosity` | 30 | 55 | 20 |
| `warmth` | 45 | 70 | 15 |
| `calmness` | 90 | 55 | 85 |
| `confidence` | 85 | 70 | 95 |
| `aggression` | 10 | 25 | 45 |
| Adresse | commandant / capitaine | pilote / *(prénom)* | monsieur / commandant |
| Quantum | « Calcul de trajectoire terminé. Accrochez-vous, commandant. » | « Trajectoire prête. Essaie de ne rien percuter cette fois. » | « Vecteur quantique verrouillé. Exécution. » |
| Échec | « Négatif. Aucun raccourci n'est configuré pour cette action. » | « Ça n'a pas marché — tu n'as pas configuré la touche. » | « Action impossible. Raccourci non assigné. » |

Ces trois profils sont livrés comme **exemples pédagogiques** : ils démontrent que le même moteur
produit trois copilotes distincts sans une ligne de code spécifique (§30, §78).

---

## 8.7 Ce qui rend l'illusion crédible (détails à ne pas négliger)

1. **Ne jamais répéter la même formule deux fois de suite** — l'anti-répétition est le levier
   n°1 du réalisme, bien avant la qualité de la voix.
2. **Accuser réception avant d'agir sur les séquences longues** (« Séquence de combat engagée… »)
   plutôt que de laisser 2 s de silence.
3. **Se taire quand il faut** : en combat, un copilote bavard est insupportable. `verbosity` doit
   pouvoir tomber à ~0 avec une simple confirmation sonore (bip d'accusé) au lieu d'une phrase.
4. **Réagir aux événements système**, pas seulement aux ordres : lancement du jeu, perte de
   focus, échec répété, retour après une longue absence.
5. **Assumer l'erreur avec une cause** : « Je n'ai pas compris » est nul ; « Je n'ai pas compris —
   j'ai entendu *ouvre les ports* » est utile *et* immersif.
6. **Cohérence voix/traits** : un copilote militaire à `calmness: 90` ne doit pas parler à
   1,3× la vitesse. Le `PersonalityEngine` module la prosodie, ce n'est pas un réglage isolé.
