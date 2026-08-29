# PHASE 6 — Command architecture

## 7.1 The eight concepts and how they relate

```
   spoken phrase                                       “open the doors for me”
        │
        ▼
   ┌─────────┐  what the user wants (abstract, scored, parameterisable)
   │ INTENT  │  { intent_id, parameters, confidence, source }
   └────┬────┘
        │ 1:1  (one intent_id names exactly one Command)
        ▼
   ┌─────────┐  the declared functional unit: phrases, conditions, replies
   │ COMMAND │  kind = action | macro | dialogue | lore | query
   └────┬────┘
        │ 1:n
        ▼
   ┌─────────┐  one executable step
   │ ACTION  │  type = game_action | key | mouse | wait | repeat | if | say | plugin
   └────┬────┘
        │ (when type = game_action)  an abstract action_id, never a key
        ▼
   ┌─────────┐  the local table (action_id, actionmap) → InputSpec
   │ BINDING │  specific to the machine, imported from Star Citizen, editable
   └────┬────┘
        ▼
   ┌───────────┐  the hardware order: scancode down/up, mouse button, wheel
   │ INPUTSPEC │
   └───────────┘

   SEQUENCE  = an ordered list of ACTIONs, run by the SequenceRunner
   MACRO     = a named, reusable SEQUENCE, triggerable by voice (= a Command with kind=macro)
   CONDITION = an evaluable predicate (a Command's requirements, a Macro's `if` branches)
   RESPONSE  = what the copilot says; never the direct result of the action, always filtered
               through the character
```

**The fundamental invariant**: the only link between the world of “meaning” (intent/command) and
the world of “hardware” (a key) is the `BINDING` table. That table is **data**, never code
(RT-02). It follows that the whole engine can run in CI, with no keyboard, by injecting a test
`BindingProfile` and a `SimulatedInputEngine`.

---

## 7.2 Command kinds (`kind`)

| `kind` | Executes | Answers | Example | Where the idea came from |
|---|---|---|---|---|
| `action` | 1..n steps | yes | “open the doors” | — |
| `macro` | a complex sequence with conditions | yes | “go to combat mode” | §46 |
| `dialogue` | **nothing** | yes, character variants | “did you see that?!” | Jean-Bot's 46 “Dialogue” entries |
| `lore` | nothing, a data lookup | yes, long-form content | “tell me about Crusader” | 17 “Fiche” + the LORE category |
| `query` | reads internal state | yes | “system report”, “what's my latency?” | §39 |

This field avoids the trap of “a program that presses keys”: 20% of the catalogue may press
nothing at all and still be the most appreciated part of the product.

---

## 7.3 Categories (a closed enumeration)

`ship` · `flight` · `navigation` · `quantum` · `combat` · `weapons` · `shields` · `power` ·
`targeting` · `scanning` · `mining` · `salvage` · `cargo` · `exploration` · `landing` ·
`takeoff` · `camera` · `communication` · `vehicle` · `fps` · `social` · `immersion` · `lore` ·
`system` · `ai` · `media` · `plugin`

Validated by schema at load. An unknown category is a lint error, not a silent fallback (the
lesson of Jean-Bot's broken `category.id`).

---

## 7.4 The intent resolution algorithm

```
Input: the raw transcript text, the context, the active copilot

1. NORMALISATION
   lower case → strip accents → punctuation → elisions ("ouvre-moi" → "ouvre moi")
   → spelled-out numbers → digits ("three" → "3")
   → drop command filler words ("please", "er", "come on")
   → drop the leading wake word

2. CANDIDATES
   a. EXACT match on the index of normalised voice_phrases              → score 1.00
   b. PREFIX / phrase-inclusion match                                   → 0.90–0.98
   c. FUZZY match: token-set ratio + normalised Levenshtein             → 0.50–0.92
      weighted by: phrase length, recent usage (command_stats),
      category compatible with the GameContext, the user's favourites
   d. (V1) SEMANTIC recall through embeddings, if a/b/c fall below the threshold → 0.50–0.85

3. FILTERING
   disabled commands, those outside the copilot's abilities, forbidden categories → dropped

4. DECISION
   best ≥ 0.85 and gap to the runner-up ≥ 0.15   → EXECUTE
   best ≥ 0.85 and gap < 0.15                    → DISAMBIGUATE (a closed question)
   0.55 ≤ best < 0.85                            → CONFIRM (“Do you mean … ?”)
   < 0.55 and the LLM is enabled                 → ESCALATE TO THE LLM
   < 0.55 and the LLM is disabled                → UNKNOWN (a reply + an unknown_phrase log)

5. PARAMETER EXTRACTION
   patterns declared by the command: {quadrant}, {value:int}, {target}
   missing values → SlotFiller (context) → otherwise a targeted follow-up
```

**Every threshold is a parameter**, exposed in the advanced settings and logged in debug mode.
They will be tuned empirically from the `unknown_phrases` data.

### Anaphora and open slots (§18)

```
turn 1  “prepare the shields”
        → intent shields.set_quadrant, parameter {quadrant} missing
        → ConversationContext.pending_slot = { intent, slot: "quadrant", ttl: 15 s }
        → reply: “Which quadrant?”
turn 2  “to the front”
        → the matcher sees an active pending_slot: it FIRST tries to fill the slot
        → "front" ∈ enum{front, rear, left, right, balanced} → OK
        → executes shields.set_quadrant(front)
```

The `pending_slot` expires (TTL) and is cancelled by: a new command with a high score, the kill
switch, or “never mind”.

---

## 7.5 The AI's contract (§73–74, §86)

The LLM receives only a **catalogue of allowed intents** (id + description + parameters), the
text, and a summary of the context. It returns **nothing but** this:

```json
{
  "type": "command",
  "intent": "ship.doors.toggle",
  "parameters": {},
  "confidence": 0.94,
  "requires_confirmation": false,
  "reasoning": "the user is asking for the airlocks to be opened"
}
```

or

```json
{ "type": "conversation", "reply_hint": "the user is commenting on a combat event" }
```

or

```json
{ "type": "clarification", "question_key": "shields.which_quadrant",
  "options": ["front", "rear", "left", "right"] }
```

**Five locks applied after the LLM's answer, in this order:**
1. Strict JSON parsing (JSON/grammar-constrained mode on the provider side) — a failure means
   rejection.
2. `intent` ∈ the whitelist of commands **enabled for this copilot** — otherwise rejection plus a
   `llm_intent_rejected` security log.
3. Parameters validated against the schema the command declares (types, enumerations, bounds).
4. The `ExecutionGuard` applied as usual (permissions, dangerous, focus, cooldown).
5. The LLM's `confidence` is capped: it can **never** bypass the confirmation a `dangerous`
   command demands.

The LLM never sees a keybind, can never produce a key, and has no access to `IInputEngine`. An
architecture test verifies that no `*.Ai.*` project references `*.Input.*`.

---

## 7.6 The sequence language

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

**Guarantees the `SequenceRunner` makes:**

| Guarantee | Mechanism |
|---|---|
| No key is left held down | `try/finally`: every key or button pressed without a matching release is released on the way out, including on exception, cancellation or kill switch |
| Immediate cancellation | A `CancellationToken` propagated to every step; the kill switch means `Cancel()` + a global release |
| No overlap | One sequence at a time per profile; a new command either cancels or queues depending on `sequence_policy` |
| Realistic delays | `hold` defaults to 45 ms (a game often ignores anything under 16 ms); optional ±10 ms jitter |
| Focus lost mid-run | The sequence is interrupted and the result marked `aborted_focus_lost` |
| Traceability | Every step logged with its `trace_id`, visible in debug mode |

---

## 7.7 Available conditions (`requirements` and `if`)

| Type | True when | Available |
|---|---|---|
| `game_running` | `StarCitizen.exe` is detected | M |
| `game_foreground` | the game has focus | M |
| `binding_available` | the `action_id` has a non-empty binding | M |
| `simulation_off` | we are not in simulation mode | M |
| `cooldown_elapsed` | implicit, handled by the guard | M |
| `copilot_capability` | the ability is enabled on the copilot | M |
| `game_mode_is` | `GameContext.mode` (declarative in v0.1) | 1 |
| `previous_command_was` | the last executed intent | 1 |
| `plugin_condition` | a predicate supplied by a plugin | 1 |
| `telemetry` | real game state (V2) | 2 |

An unsatisfiable condition **always** produces an explicit message (RF-ERR), never a silent
failure: “no shortcut configured for *open the doors* — would you like to set one now?”

---

## 7.8 The life of a user command (Command Builder, §48)

```
Command Builder UI
   name, category, spoken phrases
   steps: [Press F] [Wait 100 ms] [Press 1] [Hold right click]
      │
      ▼
 Validation: phrases unambiguous against the existing catalogue (a warning if the score
             exceeds 0.85 against an existing command), known action_ids, plausible durations
      │
      ▼
 Written to commands/user.custom.json  (source: "user", never mixed into the shipped catalogue)
      │
      ▼
 Hot reload of the CommandRegistry + reindexing of the PhraseIndex
      │
      ▼
 One-click test: run in simulation mode with a step-by-step display
```

AI-assisted generation (§49) slots in **before validation**: the LLM proposes a draft command,
which then follows exactly the same path of human validation. The AI never writes into the
catalogue directly.
