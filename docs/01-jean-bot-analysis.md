# PHASE 1 — Jean-Bot analysis

> Method: reading the raw HTML of `jean-bot.fr/index.php` and `jean-bot.fr/commandes.php`,
> extracting and reading the embedded JavaScript (~2,100 lines, a single 132 KB file), calling
> directly the public endpoints found in that JS, and a statistical analysis of the JSON
> catalogue returned. No binary was downloaded or executed.
> Date of the analysis: 2026-08-23. The site keeps changing — this is a snapshot.

Confidence legend: **[C]** confirmed (directly observed) · **[D]** deduced (reasonable inference
from observed evidence) · **[U]** unknown (not reachable from the outside).

---

## 1.1 What the product is

| Item | Status | Detail |
|---|---|---|
| Positioning | **[C]** | “Voice packs for **Voice Attack**” (home page text). Jean-Bot is therefore not a standalone engine: it is **content** (profiles + audio banks) for VoiceAttack. |
| Tagline | **[C]** | “The psychopathic robot that flies with you in Star Citizen” |
| Author | **[C]** | © 2026 Joffré Larrieu (French-speaking streamer “Joffre_Tiboy”) |
| Business model | **[C]** | One-off purchase, **no subscription**. Premium pack €30 (€20 promo at the start of the month, capped at 10 units). Free demos. Steam page and official site linked. |
| Advertised volume | **[C]** | Jean-Bot Premium “~500 commands”, Synthia “650 commands and over 8,000 phrases”, Virgil “639 commands”. |
| Voice production | **[D]** | Recorded human voices (the author's, processed), **no TTS or AI** — consistent with “over 8,000 phrases” being fixed, and with third-party sources. |
| STT | **[D]** | No proprietary STT layer: VoiceAttack uses **Windows (SAPI)** speech recognition. This is the product's Achilles heel — accuracy, and no NLU. |
| Compatibility | **[C]** | “Star Citizen 4.9”, “Jean-Bot LIVE version”. |

### The three copilots **[C]**

| Copilot | Identity colour | Stated role | Highest tier shipped |
|---|---|---|---|
| Jean-Bot | red `#ef4444` | Onboard companion, dark humour | PREMIUM |
| Synthia | purple `#8b5cf6` | Synthetic assistant, “elegant, clear, responsive”, “behavioural chip enabled” | PREMIUM |
| Virgil | blue `#3b82f6` | Military assistant, “precise, disciplined” | **STANDARD** (Premium announced but not yet served by the catalogue) |

Jean-Bot's central idea is therefore already **“one common base, several characters”** — exactly
what Optimus has to generalise, but *parametrically* rather than by hand-recording 8,000 audio
files.

---

## 1.2 Observed technical architecture of the site

**[C]** Stack: PHP on the server, a single page with no JS framework (vanilla + `<template>`),
Tailwind over CDN, FontAwesome 6.4, Google Fonts (Chakra Petch / Exo 2). PWA (`manifest.json`,
`display: standalone`, `start_url: ./commandes.php`, `orientation: portrait-primary`) → **tablet
/ second screen** use is explicitly intended.

### Endpoints actually found **[C]**

| Endpoint | Method | Role |
|---|---|---|
| `auth.php?action=login` / `?action=logout` | GET (redirect) | **Discord** OAuth. URL obfuscated in base64 inside the `onclick`. |
| `get_commandes.php?bot={Jean-Bot\|Synthia\|Virgil}&tier={LITE\|STANDARD\|PREMIUM}` | GET | Command catalogue as JSON. **Tested: answers 200 without authentication** (262 KB for Jean-Bot/LITE). |
| `app/load_config.php` | GET (`credentials: include`) | User config → `{success, config:{binds:{}, favorites:[], settings:{}}}`. Answers `{"success":false,"config":null}` outside a session. |
| `app/save_config.php` | POST JSON | Server-side persistence of favourites (and presumably of binds). |
| `./{CODE}.json` | GET | Prize draw: one JSON file per secret code, containing a **Discord webhook**. |
| `HUB/Jean-Bot HUB.exe`, `DEMOS/Jean-Bot HUB DEMO.exe` | GET | Proprietary Windows launcher/installer. URL in base64. |

**[D]** Tier assignment goes through **Discord roles**: Discord auth + “Discord channel reserved
for Tipeee supporters” + a sign-in error offering to “Join the Discord server”. On the page side
the state is injected inline by PHP: `const BOT_TIERS = {'Jean-Bot':'LITE', 'Synthia':'LOCKED',
'Virgil':'LOCKED'}` and `MAX_AVAILABLE_TIERS` caps the request.

**[U]** How the HUB works internally (does it install VoiceAttack profiles? update them? DRM?),
the format of the VoiceAttack profiles shipped, the server logic of `save_config.php`, licence
handling.

---

## 1.3 Catalogue data shape (real excerpt)

```json
{
  "catalog_version": "1.0",
  "total": 310,
  "commands": [{
    "id": 234550785755266,
    "code_name": "allumage_activation_du_systeme",
    "is_active": true,
    "is_hidden": false,
    "bot_targets": ["Jean-Bot"],
    "tier_required": "DEMO",
    "category": { "id": "avigation", "name": "Navigation" },
    "default_key": "R",
    "locales": { "fr_FR": {
        "name": "Allumage / Activation du système",
        "description": "Mettre sous tension l'ordinateur de bord et initialiser le HUD général." } },
    "metadata": { "actions_count": 1, "usage_count": 6 }
  }]
}
```

### Statistics measured on `Jean-Bot / LITE` (310 entries) **[C]**

| Measure | Value |
|---|---|
| Categories | Navigation 102, Combat 93, Mining 89, Social 13, Exploration 11, LORE 2 |
| Tiers inside the file | LITE 298, DEMO 12 |
| **Non-executable** `default_key` values | `Macro` ×73, `Dialogue` ×46, `Fiche` ×17, `Souris`, `HUD`, `F5/F6/F7`, `Échap`… |
| `actions_count == 0` | 192 / 310 (≈ 62%) |
| Names containing “/” aliases | **269 / 310** |
| Locales | `fr_FR` only (100%) |
| `usage_count` | 0 → 312 |
| `category.id` | **always missing its first character**: `avigation`, `ombat`, `inage`, `ocial`, `xploration`, `""` |

### Five major lessons

1. **`default_key` is a display label, not a binding.** `"Macro"`, `"Dialogue"`, `"Fiche"`,
   `"HUD"` are not keys. The site is **documentation**, not an engine: the real execution lives
   in the VoiceAttack profile, invisible and not editable here.
   → *Optimus must do the opposite: the binding shown IS the binding executed.*
2. **Voice aliases are encoded in the label**, separated by `/`: “Allume / Active / Met / Remet le
   ressort caméra”. There is **no structured `aliases` field**, therefore no search by spoken
   phrase, no scoring, no disambiguation.
   → *Optimus: `voice_phrases[]` as a first-class field, indexed and normalised.*
3. **46 “Dialogue” + 17 “Fiche” + the LORE category**: a significant share of the catalogue
   executes nothing at all — it is **pure immersion content**. That is what gives the product its
   charm, and what a purely “macro” project forgets every time.
4. **`category.id` broken on 100% of entries**: the catalogue is not normalised on the server, and
   the client compensates with a `normalizeCategory()` function remapping
   `combat/mining/minage/flight/navigation/social/socials/lore`. The mark of a hand-rolled
   generation pipeline (probably a VoiceAttack export followed by an ad hoc transform).
   → *Optimus: categories are a closed enumeration, validated at load time (JSON schema).*
5. **Inconsistent volumes**: the page hard-codes `COMMANDES_REELLES['Jean-Bot']['LITE'] = 246`,
   but the endpoint returns 310 entries (298 LITE + 12 DEMO). Even removing the categories locked
   in LITE (Social 13 + LORE 2) gives 295, not 246. **[U]** The gap cannot be explained from the
   outside — a frozen marketing counter, or extra server-side filtering per session.

### Volumes declared in the page's own code **[C]**

| Bot | Commands LITE / STD / PREMIUM | Actions LITE / STD / PREMIUM |
|---|---|---|
| Jean-Bot | 246 / 447 / 642 | 1112 / 1313 / 1498 |
| Synthia | 246 / 443 / 666 | 1153 / 1724 / 1999 |
| Virgil | 251 / 442 / 646 | 1166 / 1739 / 1781 |

---

## 1.4 What the `commandes.php` interface does

**[C] Observed and read in the code:**

- **Copilot selector** (Jean-Bot / Synthia / Virgil) with a dynamic colour theme and logo.
- **Search** that ignores accents, with a clever **“strict word” mode**: if the query ends with a
  space, matching switches to a whole-word regex. It covers both the name *and* the description.
- **Category filters**: All, Combat, Mining, Exploration, Navigation, LORE, Social, Favourites.
- **Tier locking**: `categorieVerrouillee()` → LORE blocked on LITE, Social blocked on LITE and
  STANDARD, Favourites forbidden on LITE.
- **Favourites**: a star per command, storage key `botTarget::code_name`, persisted in
  `localStorage['jeanbot_favorites_v2']` **and** on the server when a session is open, with
  **automatic migration** of the older formats (`jeanbot_favorites_by_ia`, `jeanbot_favorites`) by
  matching on the label.
- **HUD mode ON/OFF**: switches to a compact card template (`tmpl-hud-card`, LED, border coloured
  by category) for a second screen.
- **SIMPIT mode ON/OFF**: full screen + **Wake Lock API** (prevents sleep) — designed for a
  physical cockpit or a built-in tablet.
- **Export**: `exportFavoritesToPDF()` — generates a PDF “flight sheet” of the favourites, grouped
  by category, with description and shortcut (the personal one if it exists, otherwise the
  default).
- **Custom binds**: `userBinds[botTarget::code_name]` is **read** from `load_config.php` and shown
  with a “custom key” marker… but **no editing UI exists on this page** → editing happens
  elsewhere (the HUB? another page?) **[U]**.
- **Collapsible avionics console**, a toast system, a Discord call-to-action banner.
- **“Quantum Link Terminal”**: a prize draw. The user types a code → the client does
  `fetch('./{CODE}.json')` → the JSON contains a **Discord webhook** → the client POSTs a nickname
  and an e-mail address straight to it. A 24-hour replay lock in `localStorage`.

> ⚠️ **A security anti-pattern never to reproduce.** The Discord webhook is handed to the browser:
> anyone holding the code can spam it indefinitely, and the code files are enumerable. The replay
> lock is client-side and therefore bypassable, and users' e-mail addresses travel to an exposed
> webhook. **Lesson for Optimus: no secret (webhook, token, API key) must ever reach an untrusted
> client; every quota and replay guard belongs on the server.**

---

## 1.5 UX analysis

### What works — worth keeping

| Strength | Why it works | How Optimus takes it up |
|---|---|---|
| A coherent cockpit/avionics identity | You *believe* in the product before trying it; immersion starts on the website | A single “avionics” design system, shared by app and web |
| One copilot = one colour + one logo + one voice | Immediate recognition, makes you want to collect them | `Copilot` carries its own theme (`accent_color`, `avatar`) |
| HUD mode + SIMPIT + PWA | Acknowledges real use: second screen, tablet, physical cockpit | HUD/overlay mode + a local API a tablet can consume |
| Favourites + PDF “flight sheet” export | A player does not memorise 500 commands: they use 20 | Favourites + a printable cheat sheet, generated from the *real* binds |
| Search with strict-word mode | A smart detail, a big win on a 600-entry catalogue | To be taken up as is in the Command Browser |
| Dialogue and lore (63 entries with no action) | This is what “having a copilot” means — not a button-pusher | `Command.kind: action \| macro \| dialogue \| lore` from the model up |
| One-off purchase, no subscription | Aligned with Star Citizen culture | Optimus is local, with no mandatory account |

### What falls short — to improve on

| Weakness | Impact | Optimus's answer |
|---|---|---|
| Dependency on VoiceAttack + Windows Speech | Mediocre accuracy, fixed phrases to recite word for word, no NLU | Local neural STT (Whisper) + fuzzy matcher + optional LLM |
| Voice = pre-recorded WAV files | Not editable, not translatable, not extensible: every line is a studio session | Neural TTS, an interchangeable `VoiceProvider`, replies derived from the character |
| Purely decorative `default_key` | The user sees “Macro” and does not know what to do; if they rebind in SC, everything breaks silently | Real bindings, imported from the SC XML, editable, with conflict detection |
| Aliases buried in the label | No search by phrase, no scoring, no personal aliases | `voice_phrases[]` + user aliases + learning from unrecognised phrases |
| Single language (`fr_FR` only) | A limited market | Per-copilot i18n from the data model up |
| Aggressive gating (favourites forbidden on LITE) | Punishes on a *convenience* feature rather than on value | No convenience feature is locked |
| Unnormalised catalogue (broken `category.id`) | Silent debt, display bugs | JSON schema validated at load, catalogue linting in CI |
| No conversational context | Every sentence is isolated: you cannot say “and to the front” | `ConversationContext` + anaphora resolution |
| No explicit failure feedback | If the command is not understood: silence | Never fail silently (see RF-ERR) |
| Webhook exposed client-side | An exploitable hole | No secret on the client, quotas on the server |

### What Optimus can do that Jean-Bot structurally cannot

1. **Understand instead of recognise** — Whisper + normalisation + a fuzzy matcher tolerate “open
   the doors for me please”, where Windows Speech demands the exact phrase.
2. **Execute what is displayed** — importing Star Citizen's `layout_*.xml`: Optimus's binds *are*
   the player's, and a rebind inside the game resynchronises.
3. **Parametric character** — sliders (humour, sarcasm, formality…) instead of 8,000 WAVs.
4. **Extensibility** — plugins, conditional macros, a Command Builder with no code.
5. **Offline and free to run** — zero mandatory network call, zero API cost.
6. **Transparency** — a debug mode showing STT → intent → confidence → binding → execution; a
   simulation mode that presses nothing.
