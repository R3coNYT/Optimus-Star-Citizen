<#
.SYNOPSIS
    Convertit le defaultProfile.xml de Star Citizen en BindingProfile JSON pour Optimus.

.DESCRIPTION
    Spike S0-4, seconde moitié. Produit data/bindings/starcitizen/defaults-<version>.json :
    la table `actionmap/action -> InputSpec` qui sert de socle à tout profil de binding
    (cf. docs/02 et docs/06).

    Trois particularités du format, constatées sur un fichier réel (2026-08-24) :

      1. Les modificateurs peuvent précéder OU suivre la touche : `lalt+c` mais aussi `f6+lalt`.
         On les identifie donc par leur nom, jamais par leur position.
      2. Une même touche porte plusieurs actions distinguées par leur activationMode
         (`f10` = throttle_up en press, throttle_max en double_tap). Ce n'est pas un conflit.
      3. Les durées de maintien ne sont pas arbitraires : le jeu déclare lui-même ses
         activationModes avec leurs seuils (pressTriggerThreshold). On en DÉRIVE le hold_ms
         au lieu de le deviner - un `delayed_press_medium` exige 0,5 s, un tap de 45 ms
         échouerait silencieusement.

.EXAMPLE
    .\convert-default-profile.ps1 -XmlPath 'D:\defaultProfile.xml' -GameVersion '4.9'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $XmlPath,
    [string] $GameVersion = 'unknown',
    # Build complet affiché par le jeu (ex. 4.9-live.12344265). Sert de repère exact pour
    # diffuser les profils d'une version à l'autre.
    [string] $GameBuild = '',
    [string] $OutPath
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param([string] $m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param([string] $m) Write-Host "    $m" -ForegroundColor Yellow }

# ------------------------------------------------------- Normalisation des touches

# Noms Star Citizen -> noms canoniques Optimus (positions US, cf. ScanCodes.cs).
$KeyMap = @{
    'space' = 'SPACE'; 'tab' = 'TAB'; 'enter' = 'ENTER'; 'escape' = 'ESCAPE'
    'backspace' = 'BACKSPACE'; 'delete' = 'DELETE'; 'insert' = 'INSERT'
    'home' = 'HOME'; 'end' = 'END'; 'pgup' = 'PAGEUP'; 'pgdn' = 'PAGEDOWN'
    'up' = 'UP'; 'down' = 'DOWN'; 'left' = 'LEFT'; 'right' = 'RIGHT'
    'capslock' = 'CAPSLOCK'; 'numlock' = 'NUMLOCK'; 'scrolllock' = 'SCROLLLOCK'
    'slash' = 'SLASH'; 'backslash' = 'BACKSLASH'; 'comma' = 'COMMA'; 'period' = 'PERIOD'
    'semicolon' = 'SEMICOLON'; 'apostrophe' = 'APOSTROPHE'; 'lbracket' = 'LBRACKET'
    'rbracket' = 'RBRACKET'; 'minus' = 'MINUS'; 'equals' = 'EQUALS'; 'grave' = 'GRAVE'
    'np_0' = 'NP_0'; 'np_1' = 'NP_1'; 'np_2' = 'NP_2'; 'np_3' = 'NP_3'; 'np_4' = 'NP_4'
    'np_5' = 'NP_5'; 'np_6' = 'NP_6'; 'np_7' = 'NP_7'; 'np_8' = 'NP_8'; 'np_9' = 'NP_9'
    'np_add' = 'NP_PLUS'; 'np_subtract' = 'NP_MINUS'; 'np_multiply' = 'NP_MULTIPLY'
    'np_divide' = 'NP_DIVIDE'; 'np_period' = 'NP_PERIOD'; 'np_enter' = 'NP_ENTER'
}
$Modifiers = @{ 'lalt' = 'LALT'; 'ralt' = 'RALT'; 'lshift' = 'LSHIFT'; 'rshift' = 'RSHIFT'
                'lctrl' = 'LCTRL'; 'rctrl' = 'RCTRL'; 'alt' = 'LALT'; 'shift' = 'LSHIFT'; 'ctrl' = 'LCTRL' }
$MouseMap = @{ 'mouse1' = 'MOUSE1'; 'mouse2' = 'MOUSE2'; 'mouse3' = 'MOUSE3'; 'mouse4' = 'MOUSE4'
               'mouse5' = 'MOUSE5'; 'mouse6' = 'MOUSE6'; 'mouse7' = 'MOUSE7'; 'mouse8' = 'MOUSE8'
               'mwheel_up' = 'WHEEL_UP'; 'mwheel_down' = 'WHEEL_DOWN' }

function ConvertTo-InputSpec {
    param([string] $Raw)

    $result = [ordered]@{ key = $null; mods = @(); device = 'keyboard'; unsupported = $false; raw = $Raw }
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }

    # Le token peut mélanger modificateurs et touche dans n'importe quel ordre.
    $tokens = $Raw.Trim().ToLowerInvariant() -split '\+' | Where-Object { $_ -ne '' }
    $mods = @()
    $main = $null

    foreach ($token in $tokens) {
        if ($Modifiers.ContainsKey($token)) { $mods += $Modifiers[$token]; continue }
        # Le dernier token non-modificateur gagne (les fichiers réels n'en ont jamais deux).
        $main = $token
    }

    if (-not $main) { return $null }

    if ($MouseMap.ContainsKey($main)) {
        $result.key = $MouseMap[$main]
        $result.device = 'mouse'
    }
    elseif ($KeyMap.ContainsKey($main)) { $result.key = $KeyMap[$main] }
    elseif ($main -match '^f([1-9]|1[0-9]|2[0-4])$') { $result.key = $main.ToUpperInvariant() }
    elseif ($main -match '^[a-z0-9]$') { $result.key = $main.ToUpperInvariant() }
    else {
        # Axes souris, head tracking, périphériques non gérés au MVP.
        $result.key = $main.ToUpperInvariant()
        $result.unsupported = $true
    }

    $result.mods = @($mods | Sort-Object -Unique)
    return $result
}

# --------------------------------------------------------------------- Lecture

Write-Step "Lecture de $XmlPath"
[xml] $xml = Get-Content -LiteralPath $XmlPath -Raw
$profileNode = $xml.profile
Write-Ok "profile version=$($profileNode.version) rebindVersion=$($profileNode.rebindVersion)"

# Les seuils viennent du fichier lui-même : rien n'est deviné.
Write-Step "Lecture des ActivationModes déclarés par le jeu"
$activationModes = @{}
foreach ($mode in $profileNode.ActivationModes.ChildNodes) {
    if (-not $mode.name) { continue }
    $press = [double] $mode.pressTriggerThreshold
    $activationModes[$mode.name] = [ordered]@{
        onPress = $mode.onPress -eq '1'
        onRelease = $mode.onRelease -eq '1'
        multiTap = [int] $mode.multiTap
        pressTriggerThreshold = $press
        retriggerable = $mode.retriggerable -eq '1'
    }
}
Write-Ok "$($activationModes.Count) modes déclarés"

function Resolve-Mode {
    param([string] $ActivationMode)

    $mode = 'tap'
    $holdMs = 45          # valeur de référence mesurée au spike S0-1 (le jeu accepte 16 ms)

    if ($ActivationMode -and $activationModes.ContainsKey($ActivationMode)) {
        $definition = $activationModes[$ActivationMode]

        if ($definition.multiTap -ge 2) { $mode = 'double_tap' }
        elseif ($ActivationMode -like 'hold*' -or $ActivationMode -like 'delayed_hold*') { $mode = 'hold' }

        # Un seuil positif = le jeu exige un maintien minimal avant de déclencher.
        if ($definition.pressTriggerThreshold -gt 0) {
            $holdMs = [int] ([math]::Round($definition.pressTriggerThreshold * 1000) + 80)
            if ($mode -eq 'tap') { $mode = 'hold' }
        }
    }

    return @{ mode = $mode; hold_ms = $holdMs }
}

# --------------------------------------------------------------------- Conversion

Write-Step "Conversion des actionmaps"
$bindings = [ordered]@{}
$unbound = @()
$unsupported = @()
$stats = [ordered]@{ actionmaps = 0; actions = 0; bound = 0; mouse = 0; combos = 0 }

foreach ($actionmap in $profileNode.actionmap) {
    $stats.actionmaps++
    $mapName = $actionmap.name

    foreach ($action in $actionmap.action) {
        $stats.actions++
        $id = "$mapName/$($action.name)"

        $activation = $action.activationMode
        if (-not $activation) { $activation = $action.ActivationMode }
        if (-not $activation) { $activation = $action.activationmode }

        # Le binding peut vivre dans `keyboard` (y compris avec de la souris dedans :
        # « lalt+mwheel_up ») ou dans l'attribut `mouse` séparé.
        $spec = ConvertTo-InputSpec -Raw $action.keyboard
        if (-not $spec) { $spec = ConvertTo-InputSpec -Raw $action.mouse }

        if (-not $spec) {
            $unbound += $id
            continue
        }

        $resolved = Resolve-Mode -ActivationMode $activation

        $entry = [ordered]@{
            key = $spec.key
            mods = $spec.mods
            device = $spec.device
            mode = $resolved.mode
            hold_ms = $resolved.hold_ms
            sc_activation_mode = if ($activation) { $activation } else { 'press' }
            sc_raw = $spec.raw.Trim()
        }
        if ($spec.unsupported) { $entry.unsupported = $true; $unsupported += "$id = $($spec.raw)" }
        if ($action.UILabel) { $entry.ui_label = $action.UILabel }

        $bindings[$id] = $entry
        $stats.bound++
        if ($spec.device -eq 'mouse') { $stats.mouse++ }
        if ($spec.mods.Count -gt 0) { $stats.combos++ }
    }
}

# ------------------------------------------------------------------------ Sortie

if (-not $OutPath) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
    $dir = Join-Path $repoRoot 'data\bindings\starcitizen'
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $OutPath = Join-Path $dir "defaults-$GameVersion.json"
}

$document = [ordered]@{
    '$schema' = 'optimus://schemas/bindingprofile-1.json'
    id = "starcitizen-defaults-$GameVersion"
    name = "Star Citizen $GameVersion - defaults"
    game = 'star-citizen'
    game_version = $GameVersion
    game_build = $GameBuild
    source = [ordered]@{
        file = 'Data/Libs/Config/defaultProfile.xml'
        profile_version = $profileNode.version
        rebind_version = $profileNode.rebindVersion
        imported_at = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    stats = $stats
    bindings = $bindings
    unbound = $unbound
}

$json = $document | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($OutPath, $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Ok "actionmaps      : $($stats.actionmaps)"
Write-Ok "actions         : $($stats.actions)"
Write-Ok "avec binding    : $($stats.bound)"
Write-Ok "  dont souris   : $($stats.mouse)"
Write-Ok "  dont combos   : $($stats.combos)"
Write-Ok "sans binding    : $($unbound.Count)"
if ($unsupported.Count -gt 0) {
    Write-Warn "non injectables : $($unsupported.Count) (axes souris, head tracking)"
    foreach ($item in ($unsupported | Select-Object -First 8)) { Write-Warn "  $item" }
}
Write-Host ""
Write-Host "Ecrit : $OutPath" -ForegroundColor Cyan
