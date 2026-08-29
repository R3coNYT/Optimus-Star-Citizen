# PHASE 7 — Character architecture (“the soul”)

## 8.1 The problem to solve

A believable character is not a voice, and it is not a system prompt. It shows itself in **four
independent dimensions**:

| Dimension | Question | Technical support |
|---|---|---|
| **What to say** | which information to convey | `ResponseSet` + `intent` |
| **How to say it** | tone, length, vocabulary | `PersonalityEngine` (traits) |
| **When to speak** | acknowledge? stay quiet? follow up? | `BehaviorRules` + context |
| **With which voice** | timbre, rate, pitch | `VoiceConfig` (modulated by the traits) |

Jean-Bot solves all of it by brute force (8,000 recorded WAVs). Optimus has to reach the same
result **parametrically and deterministically** — without depending on an LLM, which stays
optional.

---

## 8.2 Data model

```
Personality
├── traits{}          8 sliders, 0–100     ← the core
├── style{}           boolean flags        ← register (military, sci-fi, technical…)
├── speech{}          rate, pitch, maximum sentence length
├── lexicon{}         forms of address, preferred and forbidden phrases, replacements
├── rules[]           when → behavior (prioritised)
└── llm{}             (optional) system-prompt fragments generated from the traits
```

### The eight traits and their **mechanical** effect

| Trait | Concrete, measurable effect |
|---|---|
| `formality` | Choice of register: familiar or formal address, “copy” vs “ok”, form of address |
| `verbosity` | The reply's word budget: `max_words = 4 + verbosity × 0.20` (30 → 10 words) |
| `humor` | Unlocks the variants marked `requires.humor_min`, and weights their draw |
| `sarcasm` | The same, with a **contextual ceiling**: never in combat, never after a failure |
| `aggression` | Punctuation and sentence opening (“Executing.” vs “It's done, take your time.”) |
| `calmness` | Speaking rate (`speed` modulated −10%/+15%) and reaction to alerts |
| `warmth` | Markers of attention (“copy that, commander”, well-wishes, encouragement) |
| `confidence` | Whether hedges appear (“I think that”, “probably”) |

Every trait must have **at least one observable effect without an LLM**. A slider that changes
nothing in local mode is a decorative slider — forbidden.

---

## 8.3 The reply selection algorithm

```
Input: response_key, event (success/fail/unknown), context, character, recent history

1. CANDIDATES     the variants of responses[response_key][event]
                  + the event's generic variants (fallback)
2. ELIGIBILITY    filter on requires{}: humor_min, sarcasm_min, formality_range,
                  style flags, language, context (combat/calm)
3. CONTEXT        the active rules (see §8.4):
                  combat_active   → keep only the short variants (< 8 words)
                  command_failed  → forbid humour and sarcasm, demand a cause
4. ANTI-REPETITION  drop the last N=3 variants used for this key
                    (a circular memory within the session) — the point that makes ALL the naturalness
5. WEIGHTING      final_weight = weight × trait_affinity × inverse_recency
                  trait_affinity = the product of the proximities between required and actual traits
6. DRAW           a weighted random draw (seeded from the trace_id → reproducible in tests)
7. COMPOSITION    interpolate {variables}, apply the lexicon
                  (address_user, replacements), remove forbidden phrases,
                  truncate to the word budget
8. PROSODY        speed and pitch adjusted: calmness, the event's urgency, the length
```

No step calls the network. The LLM, when enabled, steps in only at stage 1 to **add** a generated
variant (for conversational commands, or when no variant exists) — and then goes through stages 3
to 8 like all the others. It never short-circuits the forbidden-phrase filter.

---

## 8.4 Behaviour rules

```json
{ "when": "combat_active", "behavior": "short_responses", "priority": 100,
  "params": { "max_words": 8, "disable_humor": true } }
```

| `when` | Detection | `behavior` |
|---|---|---|
| `combat_active` | `GameContext.mode == combat` (declarative in v0.1) | `short_responses` |
| `command_failed` | result = failed | `explain_reason` |
| `command_unknown` | resolution below the threshold | `ask_clarification` |
| `user_is_angry` | lexicon/prosody (V1) | `remain_calm` |
| `repeated_failure` | ≥ 3 failures of the same intent | `suggest_fix` (offers the Keybind Manager) |
| `idle_long` | ≥ N minutes without interaction | `occasional_banter` (can be disabled, off by default) |
| `startup` / `game_launched` / `game_closed` | system events | `greet` / `announce` |
| `dangerous_command` | `command.dangerous` | `require_confirmation` |

Resolution: the applicable rules are sorted by priority, their `params` are merged, and the
highest priority wins on conflict. The active set is shown in debug mode — without which the
copilot's behaviour becomes inexplicable to the user.

---

## 8.5 Generating the system prompt (when the LLM is enabled)

The prompt is **not** written by hand by the user: it is **composed** from the traits, with an
optional free fragment. This keeps local mode and LLM mode consistent with each other.

```
You are {name}, {role} aboard {user}'s ship.
Register: {style_sentences}                        ← derived from style{} + formality
Tone: {tone_sentences}                             ← derived from humor/sarcasm/warmth/confidence
Length: {max_words} words at most, one or two sentences.
Address the user as: {address_user}.
Never use: {forbidden_phrases}.
You can NEVER execute an action yourself: you propose an intent from the list supplied.
If no intent matches, answer conversationally.
{custom_prompt_fragment}
```

**The lock**: the system prompt is concatenated on the application side, never supplied raw by an
external source (a plugin, an imported pack, Discord). An `.optcopilot` pack can only offer a
`custom_prompt_fragment` **bounded in length and escaped**, never replace the safety rules.

---

## 8.6 Three reference characters

| | **Optimus** | **Synthia** | **Virgil** |
|---|---|---|---|
| Role | Military copilot | Synthetic assistant | Weapons officer |
| `formality` | 80 | 45 | 95 |
| `humor` | 40 | 75 | 5 |
| `sarcasm` | 25 | 65 | 0 |
| `verbosity` | 30 | 55 | 20 |
| `warmth` | 45 | 70 | 15 |
| `calmness` | 90 | 55 | 85 |
| `confidence` | 85 | 70 | 95 |
| `aggression` | 10 | 25 | 45 |
| Address | commander / captain | pilot / *(first name)* | sir / commander |
| Quantum | “Trajectory computed. Hold on, commander.” | “Course is ready. Try not to hit anything this time.” | “Quantum vector locked. Executing.” |
| Failure | “Negative. No shortcut is configured for that action.” | “That didn't work — you never set the key.” | “Action impossible. Shortcut unassigned.” |

These three profiles ship as **teaching examples**: they demonstrate that the same engine produces
three distinct copilots with no line of special code (§30, §78).

---

## 8.7 What makes the illusion believable (details not to neglect)

1. **Never repeat the same phrasing twice in a row** — anti-repetition is the number-one lever of
   realism, well ahead of voice quality.
2. **Acknowledge before acting on long sequences** (“Combat sequence engaged…”) rather than
   leaving two seconds of silence.
3. **Keep quiet when it matters**: in combat, a talkative copilot is unbearable. `verbosity` must
   be able to fall to nearly zero, with a simple audio confirmation (an acknowledgement beep)
   instead of a sentence.
4. **React to system events**, not only to orders: the game launching, focus being lost, repeated
   failure, coming back after a long absence.
5. **Own the error with a cause**: “I didn't understand” is worthless; “I didn't understand — I
   heard *open the ports*” is useful *and* immersive.
6. **Consistency between voice and traits**: a military copilot at `calmness: 90` must not speak
   at 1.3× the rate. The `PersonalityEngine` modulates the prosody; it is not a separate setting.
