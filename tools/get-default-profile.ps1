<#
.SYNOPSIS
    Extrait defaultProfile.xml (les keybinds par défaut de Star Citizen) depuis Data.p4k.

.DESCRIPTION
    Spike S0-4. Le fichier n'existe pas en clair sur le disque : il est empaqueté dans l'archive
    Data.p4k du jeu, et souvent stocké au format binaire CryXML. Ce script :

      1. localise Data.p4k (paramètre, sinon processus StarCitizen en cours, sinon lecteurs) ;
      2. localise unp4k.exe / unforge.exe (paramètre, sinon dossier du script, PATH, Téléchargements) ;
      3. extrait le fichier en essayant plusieurs filtres, du plus précis au plus large ;
      4. convertit le CryXML en XML lisible si nécessaire ;
      5. affiche un aperçu et compte les actionmaps et actions trouvés.

    ATTENTION : le filtre d'unp4k est un simple MOT-CLE, pas un chemin ni un motif glob.
    Passer "Data\Libs\Config" ne correspond à rien - d'où la liste de candidats essayés
    en séquence.

    Le script ne télécharge RIEN : unp4k doit être récupéré manuellement depuis les releases
    officielles du projet (https://github.com/dolkensp/unp4k/releases), puis dézippé.
    Pense à cocher "Débloquer" dans les propriétés du zip AVANT de l'extraire.

    Note : si l'installeur de .NET Framework 4.6.2 annonce qu'une version supérieure est déjà
    présente, c'est normal sous Windows 11 (4.8 est intégré). Il n'y a rien à installer.

.EXAMPLE
    .\get-default-profile.ps1 -Unp4kDir 'C:\Users\moi\Downloads\unp4k-suite-win-x64-v4.0.87\publish'

.EXAMPLE
    .\get-default-profile.ps1 -P4kPath 'D:\...\LIVE\Data.p4k' -Unp4kDir 'C:\outils\unp4k' -Filter 'profile'
#>
[CmdletBinding()]
param(
    [string] $P4kPath,
    [string] $Unp4kDir,
    [string] $OutDir,
    [string] $Filter
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Message) Write-Host "    $Message" -ForegroundColor Green }
function Write-Warn { param([string] $Message) Write-Host "    $Message" -ForegroundColor Yellow }

# ---------------------------------------------------------------- 1. Localiser Data.p4k

function Find-DataP4k {
    param([string] $Explicit)

    if ($Explicit) {
        if (-not (Test-Path -LiteralPath $Explicit)) {
            Write-Warn "Chemin fourni introuvable : $Explicit"
            exit 1
        }
        return (Resolve-Path -LiteralPath $Explicit).Path
    }

    # a) Le jeu tourne : son exécutable donne le chemin d'installation.
    #    <...>\StarCitizen\LIVE\Bin64\StarCitizen.exe  ->  <...>\StarCitizen\LIVE\Data.p4k
    #    C'est exactement l'heuristique que le ScPathResolver d'Optimus utilisera.
    $process = Get-Process -Name 'StarCitizen' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($process -and $process.Path) {
        $channelDir = Split-Path -Parent (Split-Path -Parent $process.Path)
        $candidate = Join-Path $channelDir 'Data.p4k'
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    # b) Emplacements d'installation habituels.
    $roots = @()
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Free -ne $null })) {
        foreach ($suffix in @(
            'Program Files\Roberts Space Industries\StarCitizen',
            'Roberts Space Industries\StarCitizen',
            'Games\StarCitizen',
            'StarCitizen')) {
            $roots += (Join-Path $drive.Root $suffix)
        }
    }

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($channel in @('LIVE', 'PTU', 'EPTU', 'TECH-PREVIEW')) {
            $candidate = Join-Path $root (Join-Path $channel 'Data.p4k')
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }

    return $null
}

# ------------------------------------------------------------- 2. Localiser unp4k

function Find-Tool {
    # Plusieurs noms possibles : la suite v4 a renommé unforge.exe en unforge.cli.exe,
    # alors que unp4k.exe a gardé son nom. On accepte les deux conventions.
    param([string[]] $Names, [string] $ExplicitDir)

    if ($ExplicitDir) {
        # Get-ChildItem -Recurse sur un chemin INEXISTANT met ~60 s a echouer (mesure :
        # 60 227 ms, contre 452 ms sur un chemin valide). On verifie avant toute recursion.
        if (-not (Test-Path -LiteralPath $ExplicitDir)) {
            Write-Warn "Dossier introuvable : $ExplicitDir"
            return $null
        }
        foreach ($name in $Names) {
            $candidate = Join-Path $ExplicitDir $name
            if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
        }
        # L'outil peut être ailleurs dans l'arborescence de la release.
        foreach ($name in $Names) {
            $nested = Get-ChildItem -Path $ExplicitDir -Filter $name -Recurse -ErrorAction SilentlyContinue |
                      Select-Object -First 1
            if ($nested) { return $nested.FullName }
        }
        return $null
    }

    foreach ($name in $Names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }

    $searchRoots = @(
        (Split-Path -Parent $MyInvocation.MyCommand.Path),
        (Join-Path $env:USERPROFILE 'Downloads'),
        (Join-Path $env:USERPROFILE 'Téléchargements'),
        (Join-Path $env:USERPROFILE 'Desktop')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    foreach ($root in $searchRoots) {
        foreach ($name in $Names) {
            $found = Get-ChildItem -Path $root -Filter $name -Recurse -Depth 4 -ErrorAction SilentlyContinue |
                     Select-Object -First 1
            if ($found) { return $found.FullName }
        }
    }

    return $null
}

function Find-Extracted {
    param([string] $Root)
    return Get-ChildItem -Path $Root -Filter 'defaultProfile*' -Recurse -File -ErrorAction SilentlyContinue |
           Select-Object -First 1
}

# ------------------------------------------------------------------------ Exécution

Write-Step "Recherche de Data.p4k"
$p4k = Find-DataP4k -Explicit $P4kPath
if (-not $p4k) {
    Write-Warn "Data.p4k introuvable automatiquement."
    Write-Warn "Lance le jeu (la détection devient immédiate), ou passe le chemin :"
    Write-Warn "  .\get-default-profile.ps1 -P4kPath 'D:\...\StarCitizen\LIVE\Data.p4k'"
    exit 1
}
Write-Ok "$p4k  ($([math]::Round((Get-Item -LiteralPath $p4k).Length / 1GB, 1)) Go)"

Write-Step "Recherche de unp4k.exe"
$unp4k = Find-Tool -Names @('unp4k.exe', 'unp4k.cli.exe') -ExplicitDir $Unp4kDir
if (-not $unp4k) {
    Write-Warn "unp4k.exe introuvable."
    Write-Warn ""
    Write-Warn "Télécharge la dernière release depuis la page officielle du projet :"
    Write-Warn "  https://github.com/dolkensp/unp4k/releases"
    Write-Warn "Coche 'Débloquer' dans les propriétés du zip, dézippe, puis relance avec :"
    Write-Warn "  -Unp4kDir 'C:\chemin\vers\unp4k'"
    exit 1
}
Write-Ok $unp4k

if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'sc-extract' }
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
Write-Ok "Sortie : $OutDir"

# Le filtre est un mot-clé, pas un chemin. On essaie du plus précis au plus large et on
# s'arrête dès que le fichier apparaît.
$filters = if ($Filter) { @($Filter) } else { @('defaultProfile.xml', 'defaultProfile', 'defaultprofile') }

$extracted = $null
foreach ($currentFilter in $filters) {
    Write-Step "Extraction avec le filtre : $currentFilter"

    Push-Location $OutDir
    try {
        # Un exe lancé depuis PowerShell hérite bien de l'emplacement courant (vérifié) :
        # unp4k écrira donc dans $OutDir.
        $output = & $unp4k $p4k $currentFilter 2>&1
    }
    finally {
        Pop-Location
    }

    if ($output) {
        foreach ($line in (@($output) | Select-Object -Last 6)) {
            Write-Host "    | $line" -ForegroundColor DarkGray
        }
    }
    else {
        Write-Warn "(aucune sortie produite par unp4k)"
    }

    $extracted = Find-Extracted -Root $OutDir
    if ($extracted) { break }

    Write-Warn "Rien trouvé avec ce filtre."
}

if (-not $extracted) {
    Write-Warn ""
    Write-Warn "defaultProfile n'a pas pu être extrait."
    Write-Warn "Contenu actuel de $OutDir :"
    $listing = Get-ChildItem -Path $OutDir -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 15
    if ($listing) {
        foreach ($item in $listing) { Write-Warn "  $($item.FullName.Substring($OutDir.Length + 1))" }
    }
    else {
        Write-Warn "  (vide)"
    }
    Write-Warn ""
    Write-Warn "Pistes, dans l'ordre :"
    Write-Warn "  1. filtre manuel plus large :  -Filter 'profile'"
    Write-Warn "  2. lecteur virtuel (méthode la plus fiable, nécessite Dokan) :"
    Write-Warn "       unp4k.fs.exe `"$p4k`" S:"
    Write-Warn "     puis copier  S:\Data\Libs\Config\defaultProfile.xml"
    Write-Warn "     Ce mode sert directement le CryXML converti en XML standard."
    exit 2
}

Write-Ok "$($extracted.FullName)  ($([math]::Round($extracted.Length / 1KB)) Ko)"

# ------------------------------------------------- 3. CryXML binaire -> XML lisible

Write-Step "Vérification du format"
$header = [System.IO.File]::ReadAllBytes($extracted.FullName)[0..15]
$headerText = -join ($header | ForEach-Object { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { '.' } })
Write-Ok "En-tête : $headerText"

if ($headerText -like 'CryXmlB*') {
    Write-Warn "Format binaire CryXML : conversion nécessaire."
    $unforge = Find-Tool -Names @('unforge.exe', 'unforge.cli.exe') -ExplicitDir $Unp4kDir
    if (-not $unforge) {
        Write-Warn "unforge introuvable (livré dans la même archive qu'unp4k, sous le nom"
        Write-Warn "unforge.exe ou unforge.cli.exe selon la version de la suite)."
        exit 3
    }
    Write-Ok "Outil de conversion : $unforge"
    Write-Step "Conversion CryXML -> XML"
    & $unforge $extracted.FullName 2>&1 | ForEach-Object { Write-Host "    | $_" -ForegroundColor DarkGray }

    $converted = Get-ChildItem -Path (Split-Path -Parent $extracted.FullName) -Filter 'defaultProfile*' -File |
                 Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($converted) {
        Write-Ok "Résultat : $($converted.FullName)  ($([math]::Round($converted.Length / 1KB)) Ko)"
        $extracted = $converted
    }
}
elseif ($headerText -like '<*') {
    Write-Ok "Déjà du XML lisible, aucune conversion nécessaire."
}
else {
    Write-Warn "Format inattendu : le fichier est peut-être chiffré dans cette version du jeu."
}

# ----------------------------------------------------------------- 4. Aperçu utile

Write-Step "Aperçu"
Get-Content -LiteralPath $extracted.FullName -TotalCount 12 |
    ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

$raw = Get-Content -LiteralPath $extracted.FullName -Raw
$actionMaps = ([regex]::Matches($raw, '<actionmap\s')).Count
$actions = ([regex]::Matches($raw, '<action\s')).Count

Write-Host ""
Write-Ok "actionmaps trouves : $actionMaps"
Write-Ok "actions trouvees   : $actions"
if ($actions -lt 100) {
    Write-Warn "Nombre d'actions anormalement bas : ce n'est probablement pas le bon fichier,"
    Write-Warn "ou la conversion a échoué."
}
Write-Host ""
Write-Host "Fichier a recuperer : $($extracted.FullName)" -ForegroundColor Cyan
