<#
.SYNOPSIS
    Valide un catalogue de commandes Optimus contre un profil de binding.

.DESCRIPTION
    Trois familles de controles, toutes destinees a devenir des tests automatises dans
    Optimus.Core.Tests :

      1. INTEGRITE      - identifiants uniques, categories connues, champs obligatoires,
                          coherence kind / actions.
      2. RESOLUTION     - chaque action_id existe-t-il dans le profil de binding, et
                          possede-t-il une touche ? Une action connue mais non assignee
                          n'est pas une erreur : c'est un cas nominal (RF-ERR2) que
                          l'interface doit proposer de configurer.
      3. AMBIGUITE      - deux commandes partagent-elles une phrase vocale, ou des phrases
                          trop proches ? C'est la premiere cause de commande "qui part toute
                          seule" ; mieux vaut le savoir a la construction du catalogue.

.EXAMPLE
    .\validate-catalog.ps1
#>
[CmdletBinding()]
param(
    [string] $CatalogPath,
    [string] $BindingProfilePath
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param([string] $m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param([string] $m) Write-Host "    $m" -ForegroundColor Yellow }
function Write-Err  { param([string] $m) Write-Host "    $m" -ForegroundColor Red }

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $CatalogPath) { $CatalogPath = Join-Path $repoRoot 'data\commands\starcitizen.core.json' }
if (-not $BindingProfilePath) { $BindingProfilePath = Join-Path $repoRoot 'data\bindings\starcitizen\defaults-4.9.json' }

foreach ($path in @($CatalogPath, $BindingProfilePath)) {
    if (-not (Test-Path -LiteralPath $path)) { Write-Err "Introuvable : $path"; exit 1 }
}

$catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$profile = Get-Content -LiteralPath $BindingProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json

Write-Step "Catalogue : $(Split-Path -Leaf $CatalogPath)  ($($catalog.commands.Count) commandes)"
Write-Ok "Profil    : $(Split-Path -Leaf $BindingProfilePath)  (jeu $($profile.game_version), build $($profile.game_build))"

$bindings = @{}
foreach ($property in $profile.bindings.PSObject.Properties) { $bindings[$property.Name] = $property.Value }
$unbound = @{}
foreach ($id in $profile.unbound) { $unbound[$id] = $true }

$categories = @('ship','flight','navigation','quantum','combat','weapons','shields','power','targeting',
                'scanning','mining','salvage','exploration','landing','takeoff','camera','communication',
                'vehicle','fps','social','immersion','lore','system','ai','media','plugin')
$kinds = @('action','macro','dialogue','lore','query')

$errors = @()
$warnings = @()

# ------------------------------------------------------------------ 1. Integrite

Write-Step 'Integrite'
$seenIds = @{}
$seenPhrases = @{}

foreach ($command in $catalog.commands) {
    if (-not $command.id) { $errors += "commande sans id"; continue }
    if ($seenIds.ContainsKey($command.id)) { $errors += "id duplique : $($command.id)" }
    $seenIds[$command.id] = $true

    if ($kinds -notcontains $command.kind) { $errors += "$($command.id) : kind inconnu « $($command.kind) »" }
    if ($categories -notcontains $command.category) { $errors += "$($command.id) : categorie inconnue « $($command.category) »" }
    if (-not $command.voice_phrases -or $command.voice_phrases.Count -eq 0) { $errors += "$($command.id) : aucune phrase vocale" }

    # Une commande d'action sans action a executer n'a pas de sens ; l'inverse non plus.
    $actionCount = if ($command.actions) { @($command.actions).Count } else { 0 }
    if ($command.kind -eq 'action' -and $actionCount -eq 0) { $errors += "$($command.id) : kind=action mais aucune action" }
    if ($command.kind -in @('dialogue','lore') -and $actionCount -gt 0) { $errors += "$($command.id) : kind=$($command.kind) ne doit rien executer" }
}
if ($errors.Count -eq 0) { Write-Ok "$($seenIds.Count) identifiants uniques, kinds et categories valides" }

# ----------------------------------------------------------------- 2. Resolution

Write-Step 'Resolution des actions'
$resolved = 0
$needsBinding = @()
$missingAction = @()

$directedTotal = 0
$directedBound = 0

foreach ($command in $catalog.commands) {
    # Sens explicites : Star Citizen les declare (v_lights_on / v_lights_off) sans leur
    # assigner de touche. Leur absence n'est pas un defaut du catalogue - c'est ce que
    # l'editeur de keybinds sert a combler - donc on les compte a part.
    foreach ($directed in (@($command.actions_on) + @($command.actions_off))) {
        if (-not $directed -or $directed.type -ne 'game_action') { continue }
        $directedTotal++
        if ($bindings.ContainsKey($directed.action_id)) { $directedBound++ }
        elseif (-not $unbound.ContainsKey($directed.action_id)) {
            $errors += "$($command.id) : action dirigee inconnue du jeu - $($directed.action_id)"
        }
    }

    foreach ($action in @($command.actions)) {
        if (-not $action -or $action.type -ne 'game_action') { continue }
        $id = $action.action_id

        if ($bindings.ContainsKey($id)) {
            $resolved++
        }
        elseif ($unbound.ContainsKey($id)) {
            $needsBinding += [pscustomobject]@{ Command = $command.id; Action = $id }
        }
        else {
            $missingAction += [pscustomobject]@{ Command = $command.id; Action = $id }
        }
    }
}

Write-Ok "$resolved action(s) resolue(s) avec une touche"
if ($needsBinding.Count -gt 0) {
    Write-Warn "$($needsBinding.Count) action(s) existent dans le jeu mais SANS touche par defaut :"
    foreach ($item in $needsBinding) { Write-Warn "  $($item.Command)  ->  $($item.Action)" }
    Write-Warn "  Ce n'est pas une erreur : Optimus doit repondre « aucun raccourci configure »"
    Write-Warn "  et proposer de l'assigner. C'est le chemin RF-ERR2, a couvrir par un test."
}
if ($missingAction.Count -gt 0) {
    Write-Err "$($missingAction.Count) action(s) INTROUVABLE(S) dans le profil - faute de frappe ou action supprimee :"
    foreach ($item in $missingAction) { Write-Err "  $($item.Command)  ->  $($item.Action)" }
    $errors += "$($missingAction.Count) action_id introuvable(s)"
}

# ----------------------------------------------------------------- 3. Ambiguite

Write-Step 'Ambiguite des phrases vocales'

function ConvertTo-Normalized {
    param([string] $Text)
    $t = $Text.ToLowerInvariant()
    $t = $t -replace '[àâä]', 'a' -replace '[éèêë]', 'e' -replace '[îï]', 'i'
    $t = $t -replace '[ôö]', 'o' -replace '[ùûü]', 'u' -replace 'ç', 'c'
    $t = $t -replace "[^a-z0-9 ]", ' '
    return ($t -replace '\s+', ' ').Trim()
}

$phraseCount = 0
foreach ($command in $catalog.commands) {
    $allPhrases = @($command.voice_phrases) + @($command.phrases_on) + @($command.phrases_off)
    foreach ($phrase in $allPhrases) {
        if (-not $phrase) { continue }
        $phraseCount++
        $normalized = ConvertTo-Normalized $phrase
        if ($normalized -eq '') { $errors += "$($command.id) : phrase vide"; continue }
        if ($seenPhrases.ContainsKey($normalized)) {
            $errors += "phrase « $phrase » partagee par $($seenPhrases[$normalized]) et $($command.id)"
        }
        else { $seenPhrases[$normalized] = $command.id }
    }
}
Write-Ok "$phraseCount phrases vocales, $($seenPhrases.Count) distinctes apres normalisation"

# Proximite : une phrase entierement contenue dans une autre est un risque de mauvaise
# resolution. La comparaison se fait sur des SEQUENCES DE MOTS et non sur les caracteres :
# sinon « ping » serait signale comme contenu dans « retire les epingles », ce qui n'a aucun
# sens pour un matcher qui raisonne en tokens.
function Test-WordSubsequence {
    param([string[]] $Needle, [string[]] $Haystack)
    if ($Needle.Count -eq 0 -or $Needle.Count -gt $Haystack.Count) { return $false }
    for ($start = 0; $start -le $Haystack.Count - $Needle.Count; $start++) {
        $match = $true
        for ($k = 0; $k -lt $Needle.Count; $k++) {
            if ($Haystack[$start + $k] -ne $Needle[$k]) { $match = $false; break }
        }
        if ($match) { return $true }
    }
    return $false
}

$all = @($seenPhrases.Keys)
$words = @{}
foreach ($phrase in $all) { $words[$phrase] = @($phrase -split ' ' | Where-Object { $_ -ne '' }) }

$near = @()
for ($i = 0; $i -lt $all.Count; $i++) {
    for ($j = $i + 1; $j -lt $all.Count; $j++) {
        $a = $all[$i]; $b = $all[$j]
        if ($seenPhrases[$a] -eq $seenPhrases[$b]) { continue }
        if (Test-WordSubsequence -Needle $words[$a] -Haystack $words[$b]) {
            $near += "« $a » ($($seenPhrases[$a]))  incluse dans  « $b » ($($seenPhrases[$b]))"
        }
        elseif (Test-WordSubsequence -Needle $words[$b] -Haystack $words[$a]) {
            $near += "« $b » ($($seenPhrases[$b]))  incluse dans  « $a » ($($seenPhrases[$a]))"
        }
    }
}
if ($near.Count -gt 0) {
    Write-Warn "$($near.Count) paire(s) de phrases imbriquees - a arbitrer par le score du matcher :"
    foreach ($pair in $near) { Write-Warn "  $pair" }
    $warnings += $near
}
else { Write-Ok 'Aucune phrase imbriquee' }

# ------------------------------------------------------------------- Couverture

Write-Step 'Couverture'
$byCategory = $catalog.commands | Group-Object category | Sort-Object Count -Descending
foreach ($group in $byCategory) { Write-Ok ("{0,-16} {1,3}" -f $group.Name, $group.Count) }
$dangerous = @($catalog.commands | Where-Object { $_.dangerous })
Write-Ok "commandes dangereuses (confirmation requise) : $($dangerous.Count) - $(($dangerous | ForEach-Object { $_.id }) -join ', ')"

# ---------------------------------------------------------------------- Verdict

Write-Host ''
if ($errors.Count -gt 0) {
    Write-Err "ECHEC : $($errors.Count) erreur(s)"
    foreach ($e in $errors) { Write-Err "  $e" }
    exit 1
}
Write-Host "VALIDE" -ForegroundColor Green -NoNewline
Write-Host "  -  $($catalog.commands.Count) commandes, $phraseCount phrases, $resolved actions liees, $($needsBinding.Count) a configurer, $($warnings.Count) avertissement(s)"
Write-Host "     sens explicites : $directedTotal action(s) dirigee(s), $directedBound avec une touche"
exit 0
