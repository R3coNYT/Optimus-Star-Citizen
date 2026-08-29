# PHASE 1bis — Star Citizen keybind analysis (screenshot + XML export)

## 2.1 What the supplied export contains: `layout_Keybinds_1_exported.xml`

The file contains **exactly this**:

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

**It contains no `<actionmap>` and no `<rebind>` at all.** Direct consequences:

1. The configuration is **100% Star Citizen defaults** — no key was rebound, or the export was
   taken from an untouched profile. The screenshot therefore matches the game's default layout
   (confirmed by its agreement with the known defaults: `L` doors, `R` power, `B` quantum,
   `N` landing gear, `Tab` scan, `Space` strafe up…).
2. **An SC export only contains the *delta***: only what the user changed. This is THE
   architectural trap: if Optimus merely imports the user's XML, it will know **no** key at all
   for a player who rebound nothing.
3. Two sources are therefore needed: an **embedded set of defaults** (per SC version) + the
   **user delta** applied on top. An Optimus `BindingProfile` = `defaults ⊕ overrides`.
4. The file does confirm the three declared devices (`keyboard`, `mouse`, `gamepad` instance 1)
   and the keyboard's DirectInput GUID — useful for device identification, useless for the MVP.

---

## 2.2 Star Citizen's XML format — what you need to know

### Full structure (with rebinds)

```xml
<ActionMaps version="1" optionsVersion="2" rebindVersion="2" profileName="MyProfile">
  <CustomisationUIHeader label="MyProfile" description="" image="">
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

### Input token syntax

| Form | Meaning |
|---|---|
| `kb1_l` | keyboard instance 1, key `L` |
| `kb1_lshift+lctrl+1` | combination: modifiers do not repeat the device prefix |
| `kb1_np_5`, `kb1_f12`, `kb1_escape`, `kb1_lbracket` | named keys (numpad, function, punctuation) |
| `mo1_mouse1` … `mo1_mouse8`, `mo1_mwheel_up/down` | mouse |
| `js1_button3`, `js1_x`, `js1_hat1_up` | joystick / HOTAS |
| `gp1_a`, `gp1_shoulderl` | gamepad |
| `<rebind input=" "/>` (empty) | binding **removed** by the user — a case to handle explicitly |

### ⚠️ Two different XML formats, not one (confirmed on 2026-08-24)

Analysing the real `defaultProfile.xml` (220 KB, 50 actionmaps, 1,103 actions) shows the game
uses **two distinct syntaxes** depending on the file:

| File | Syntax | Example |
|---|---|---|
| `defaultProfile.xml` (game defaults) | attributes on `<action>` | `<action name="v_lights" keyboard="l" mouse=" " activationMode="press"/>` |
| `layout_*.xml` (user export) | a `<rebind>` element | `<action name="v_lights"><rebind input="kb1_l"/></action>` |

The importer has to handle both. Other traps found in the real file:

- **Modifiers may come before or after the key**: `lalt+c` but also `f6+lalt`, `u+lshift`. They
  are identified by name, **never by position**.
- **Inconsistent casing**: `ralt+K` sits next to `ralt+y`. Comparison is case-insensitive.
- **The mouse appears in the `keyboard` attribute** (`lalt+mwheel_up`, `ralt+mouse2`) as much as
  in the dedicated `mouse` attribute.
- **Unassigned means an empty string *or* a single space** (`mouse=" "`). 476 actions out of
  1,103 have no default binding at all.
- Special prefixes: `np_*` (numpad), `mwheel_*`, `maxis_*` (analogue axes), `HMD_*` (head
  tracking). The last two cannot be injected from a keyboard: 61 actions are affected and are
  excluded from the profile.

### Activation modes: declared by the game, not guessed

`defaultProfile.xml` contains an `<ActivationModes>` block which **defines the 18 modes itself**,
with their thresholds. This is a structuring discovery: the durations are not ours to invent.

| Mode | Fires on | Declared threshold | Consequence for injection |
|---|---|---|---|
| `press` (333 actions) | key down | — | a short tap is enough |
| `tap` (187) | key up | must be released in **< 0.25 s** | a short tap is **mandatory** |
| `hold` (79) | down + up | — | explicit hold |
| `delayed_press` (64) | key down | **≥ 0.25 s** | a 45 ms tap **fails silently** |
| `all` (48) | everything | — | |
| `smart_toggle` (19) | down/up | 0.25 s delay | a short tap toggles |
| `double_tap` / `_nonblocking` (10) | two presses | — | double tap |
| `delayed_press_medium` (3) | key down | **≥ 0.5 s** | self destruct |
| `delayed_hold*` (7) | key down | 0.15 to **1.5 s** | |

**Direct consequence**: a global `hold_ms` is wrong. The duration must be **derived from each
action's `activationMode`**, reading the thresholds from the game's own file. The converter
[`tools/convert-default-profile.ps1`](../tools/convert-default-profile.ps1) does it automatically
— 31 actions need more than 45 ms, up to 1,580 ms.

### One key, several actions: not always a conflict

```
spaceship_power/v_power_throttle_up    F10   activationMode = press
spaceship_power/v_power_throttle_max   F10   activationMode = double_tap
```

Same key, same actionmap, two actions — perfectly legitimate; it is the **activation mode** that
tells them apart. The Keybind Manager's conflict detection must therefore compare the triplet
`(actionmap, input, activationMode)` and not the key alone, or it would cry wolf on dozens of
valid bindings.

### Where the files live

| What | Location |
|---|---|
| User exports | `<SC_INSTALL>\<CHANNEL>\USER\Client\0\Controls\Mappings\*.xml` (channel = `LIVE`, `PTU`, `EPTU`…) |
| Game defaults | `<SC_INSTALL>\<CHANNEL>\Data.p4k` → `Data\Libs\Config\defaultProfile.xml` (extract with **unp4k**) |
| Reloading in game | console (`~`) → `pp_rebindkeys <file_name>` |

**Path discovery strategy** (never hard-code a path, see §59 of the brief):
1. The **RSI Launcher** registry key or file (`%APPDATA%\rsilauncher\log*` or its settings JSON);
2. failing that, a running `StarCitizen.exe` process → the executable's path → walk up to
   `<CHANNEL>`;
3. failing that, scan the drive roots for `*/StarCitizen/<CHANNEL>/Bin64/StarCitizen.exe`;
4. failing that, **ask the user** (folder picker), and remember the answer.

---

## 2.3 What the screenshot teaches (UX and technical analysis)

### Volume and modifiers

The screenshot shows a single dense page holding the whole mapping: around 90 keys in use,
**four layers of modifiers**, and a legend system:

| Modifier | Role observed on the screenshot |
|---|---|
| `M1` = **Alt** | Modifier 1 — cycling and variants (`M1 Target Next - Attacker`, `M1 SA Focus L`) |
| `M2` = **Shift** | Modifier 2 (`M2 Flight Mode Wheel`) — also Afterburner on its own |
| `M3` = **Right Alt** | Modifier 3 (`M3 SA Fire L`, `M3 Eject`) |
| `*` | **Hold** |
| `**` | **Double tap** |

And **11 “contextual modes”** flagged by coloured prefixes:
`SC` Scan · `MG` Mining · `SA` Salvage · `SFH` Salvage Focus Heads · `IM` Interaction Mode ·
`TU` Turret · `LN` Landing · `QT` Quantum Travel · `ML` Missile · `AD` Advanced Camera ·
`AC` Arena Commander.

### The six architectural conclusions that follow

1. **One key has N meanings depending on the mode.** `1` = `Lock Pin 1` in flight, but
   `AD Load/Save 1` in advanced camera mode. → A binding is not `action → key` but
   **`(action, context) → input`**. The `BindingProfile` must be indexed by *actionmap*
   (= context), exactly as SC does.
2. **The modes cannot be deduced from outside.** Optimus does not *know* whether the player is in
   scan mode. → Either we ask the player (“Optimus, go to mining mode” sets an internal
   `GameContext.mode`), or we run the sequence that *forces* the mode before the action. The
   `GameStateProvider` must exist from the MVP, even in a “declarative” implementation.
3. **Hold and double tap are first-class citizens**, not exotic options: `M1 SA Fracture`,
   `Eject (double tap)`, `Exit Seat *`. → The `Action` model must carry
   `mode: tap | hold | double_tap | press | release` **from the MVP**, along with `hold_ms`.
4. **Many actions are axes or cycles, not booleans.** `Power [-]/[+]`, `Shield Raise - Level Top`,
   `Speed Limiter [+/-]`. → The command language must handle **repetition** (`repeat: 5`) and
   **numeric parameters** extracted from speech (“Optimus, raise engine power three notches”).
5. **The mouse is part of the binding** (wheel = spacing, buttons = fire groups). → The injection
   engine must handle mouse **and** keyboard from v0.1, not “later”.
6. **The screenshot is a default layout, and therefore universally shareable as a preset.**
   → Optimus ships `bindings/starcitizen/default-4.x.json`, generated from `defaultProfile.xml`,
   and the player imports *their* deltas on top.

### Excerpt of the default mapping (read from the screenshot) — used as a test fixture

| Action | Key | Action | Key |
|---|---|---|---|
| Open/close doors | `L` | Quantum Drive (cruise toggle) | `B` |
| Ship power | `R` | Landing mode | `N` |
| Landing gear | `N` (LN) | Scan mode | `Tab` |
| Engines on/off | `I` | Shields on/off | `O` |
| Weapons on/off | `P` | Power distribution | `F8` |
| Engine power ± | `F5` | Shield power ± | `F6` |
| Weapon power ± | `F7` | Decoupled | `C` |
| VTOL | `K` | Interaction mode | `F` |
| Target nearest hostile | `4` | Next friendly target | `6` |
| Countermeasure decoy | `H` | Chaff | `J` |
| Space brake | `X` | Afterburner | `Shift` |
| Autoland | `N` (M1) | Eject | `Right Alt + Y` (double tap) |
| Salute / scoreboard (AC) | `F1` | mobiGlas | `F1` |
| Starmap | `F2` | Self destruct | `Backspace` (hold) |

> ⚠️ This table is **read from an image**, and must be re-checked automatically against
> `defaultProfile.xml` before shipping as a preset. It serves as a *test fixture* to validate the
> import, not as a source of truth.

---

## 2.4 Optimus's keybind strategy (the direct consequence)

```
defaultProfile.xml (per SC version)      layout_XXX.xml (user delta)
            │                                        │
            └────────────┬───────────────────────────┘
                         ▼
              SC ActionMap Importer
                         │  normalises: kb1_lalt+t → { key:"T", mods:["ALT"] }
                         ▼
              BindingProfile (JSON, versioned)
              { "spaceship_general/v_toggle_all_doors": { key:"L", mods:[], mode:"tap" } }
                         │
      Command.action_ref ─┘   (commands only ever know the action id)
                         ▼
                 BindingResolver ──► InputEngine (SendInput scancode)
```

> **Measured in spike S0-1 (2026-08-23)**: translating `key name → scancode` must go through a
> **fixed table in US positions**, never through `MapVirtualKey`. On an AZERTY machine,
> `MapVirtualKey(VK_A)` returns `0x10` — the QWERTY `Q` position — whereas Star Citizen's `kb1_a`
> means `0x1E`. Five divergences out of six keys tested. Prototype of the table:
> `tools/Optimus.Spike.InputTest/src/ScanCodes.cs`.

**The golden rule**: Optimus's action identifier is **modelled on Star Citizen's**
(`spaceship_general/v_toggle_all_doors`). The main benefit: importing becomes a straight copy, an
SC update becomes a diff, and the “voice command” layer stays completely decoupled from the
keyboard. Non-SC actions (plugins, system, Spotify…) use their own namespace
(`plugin.spotify/play_pause`).

**Edge cases to handle explicitly:**

| Case | Expected behaviour |
|---|---|
| Action with no binding (`<rebind input=" "/>` or absent) | Command *known* but **not executable** → an explicit spoken reply: “The command exists but no shortcut is configured.” |
| Conflicting binding (two actions, same input, same actionmap) | A warning in the Keybind Manager; execution allowed but flagged |
| Binding on an absent device (`js1_*` with no joystick) | Marked `unbound_device`, offered for remapping |
| A new SC version adding or removing actions | A migration report: `+12 actions`, `-3 actions`, `5 renamed` |
| A user rebinding in game after the import | Detected by a `FileSystemWatcher` on `Mappings\` → offer to resynchronise |
