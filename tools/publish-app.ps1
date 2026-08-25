<#
.SYNOPSIS
    Publie l'application de bureau Optimus dans un dossier autonome.

.DESCRIPTION
    Même forme que publish-cli.ps1, et pour les mêmes raisons : publication dépendante du
    runtime par défaut, marque du web retirée, VERSION.txt écrit pour qu'on sache d'un coup
    d'oeil quelle version a été copiée (risque R16).

    Le dossier « data » est recopié en entier. Une publication qui n'emporterait que le
    catalogue laisserait Optimus démarrer sans personnalité ni voix, sans la moindre erreur -
    c'est arrivé, et le silence est le pire des symptômes.

.EXAMPLE
    .\publish-app.ps1 -FrameworkDependent
#>
[CmdletBinding()]
param(
    [string] $OutputDir,
    [string] $Runtime = 'win-x64',

    <#
      Publie une version qui se lance par « dotnet Optimus.App.exe » au lieu d'un exécutable
      autonome.

      Raison d'être : Smart App Control, actif par défaut sur certaines installations de
      Windows 11, bloque les exécutables non signés numériquement - et refuse donc notre .exe.
      Il laisse en revanche passer les assemblages chargés par « dotnet.exe », signé par
      Microsoft. Le prix à payer est l'installation du runtime .NET 8 sur la machine cible
      (50 Mo, lui aussi signé Microsoft) :

          winget install Microsoft.DotNet.Runtime.8

      Le jour où Optimus sera distribué, la vraie réponse sera la signature de code ; en
      attendant, ceci évite de désactiver une protection système que Windows ne permet pas
      de réactiver.
    #>
    [switch] $FrameworkDependent
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param([string] $m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param([string] $m) Write-Host "    $m" -ForegroundColor Yellow }

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

# Le SDK n'est pas forcément dans le PATH de la session courante.
# Pas d'opérateur `?.` ici : Windows PowerShell 5.1 ne le connaît pas.
$dotnet = $null
$command = Get-Command dotnet -ErrorAction SilentlyContinue
if ($command) { $dotnet = $command.Source }

if (-not $dotnet) {
    $candidate = 'C:\Program Files\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) { $dotnet = $candidate }
}
if (-not $dotnet) {
    Write-Warn "SDK .NET introuvable. Installe-le avec : winget install Microsoft.DotNet.SDK.8"
    exit 1
}
Write-Step "SDK : $dotnet"

if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'publish\Optimus.App' }

# Le dossier de sortie peut être temporairement verrouillé : exécutable encore en cours,
# antivirus qui l'inspecte, ou simple terminal dont le répertoire courant s'y trouve.
# On réessaie brièvement avant d'abandonner, avec un message qui dit quoi faire.
if (Test-Path -LiteralPath $OutputDir) {
    $removed = $false
    foreach ($attempt in 1..5) {
        try {
            Remove-Item -LiteralPath $OutputDir -Recurse -Force -ErrorAction Stop
            $removed = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 400
        }
    }

    if (-not $removed) {
        Write-Warn "Impossible de vider $OutputDir : un processus le retient."
        Write-Warn "Ferme les terminaux ou explorateurs positionnés dans ce dossier, puis relance."
        exit 1
    }
}

$project = Join-Path $repoRoot 'src\Optimus.App\Optimus.App.csproj'

if ($FrameworkDependent) {
    Write-Step 'Publication dépendante du runtime (contourne Smart App Control)'
    & $dotnet publish $project `
        --configuration Release `
        --output $OutputDir `
        --nologo `
        --verbosity quiet | Out-Host
}
else {
    Write-Step "Publication autonome ($Runtime)"
    & $dotnet publish $project `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        --output $OutputDir `
        --nologo `
        --verbosity quiet | Out-Host
}

if ($LASTEXITCODE -ne 0) {
    Write-Warn "Échec de la publication."
    exit $LASTEXITCODE
}

# Tout data\, et non une liste choisie : oublier data\copilots a livre un Optimus sans
# personnalite ni voix, qui demarrait sans erreur en annoncant « 0 entrees, 0 variantes ».
# Une liste blanche se perime des qu'on ajoute un dossier ; copier la racine, non.
Write-Step 'Copie des données'
$dataSource = Join-Path $repoRoot 'data'
$dataDestination = Join-Path $OutputDir 'data'
New-Item -ItemType Directory -Path $dataDestination -Force | Out-Null
Copy-Item -Path (Join-Path $dataSource '*') -Destination $dataDestination -Recurse -Force

foreach ($folder in (Get-ChildItem -LiteralPath $dataDestination -Directory)) {
    $count = @(Get-ChildItem -LiteralPath $folder.FullName -Recurse -File).Count
    Write-Ok ("data\{0,-12} {1} fichier(s)" -f $folder.Name, $count)
}

# La marque du web se transmet par la copie et fait bloquer les binaires par Smart App
# Control. La retirer ici ne coute rien ; elle peut revenir a la copie vers la machine de
# jeu, d'ou tools/diagnose-app-control.ps1 pour la traiter la-bas (risque R16).
Get-ChildItem -LiteralPath $OutputDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# Repere de version lisible sans rien lancer. Le paquet se recopie a la main d'une machine a
# l'autre, et rien ne distingue a l'oeil un dossier de sa version precedente : plusieurs fois
# une option venait d'etre ajoutee sans etre dans le paquet copie, et le symptome n'en disait
# rien. Ce fichier se lit dans l'explorateur, avant meme d'ouvrir un terminal.
$cliDll = Join-Path $OutputDir 'Optimus.App.exe'
$builtAt = if (Test-Path -LiteralPath $cliDll) {
    (Get-Item -LiteralPath $cliDll).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
} else { 'inconnu' }

$commit = try { (& git -C $repoRoot rev-parse --short HEAD 2>$null) } catch { $null }
if (-not $commit) { $commit = 'hors depot' }

$versionLines = @(
    "Optimus - application",
    "compile le : $builtAt",
    "commit     : $commit",
    "",
    "Ce repere doit correspondre a la ligne « binaire » affichee au lancement.",
    "S'ils different, le dossier copie n'est pas celui qui vient d'etre publie."
)
Set-Content -LiteralPath (Join-Path $OutputDir 'VERSION.txt') -Value $versionLines -Encoding utf8
Write-Ok "VERSION.txt : compile le $builtAt (commit $commit)"


$size = [math]::Round(((Get-ChildItem -LiteralPath $OutputDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)

Write-Host ''
Write-Ok "Dossier    : $OutputDir"
Write-Ok "Poids total : $size Mo"
Write-Host ''
Write-Host 'Copie ce dossier entier sur la machine de jeu, puis :' -ForegroundColor Cyan

if ($FrameworkDependent) {
    Write-Host '  Prerequis, une seule fois :  winget install Microsoft.DotNet.Runtime.8'
    Write-Host ''
    Write-Host '  dotnet Optimus.App.exe --status'
    Write-Host '  dotnet Optimus.App.exe "Optimus, allume les lumieres"          (simulation)'
    Write-Host '  dotnet Optimus.App.exe --real "Optimus, allume les lumieres"   (touches reellement envoyees)'
}
else {
    Write-Host '  Unblock-File .\Optimus.App.exe        (marque de provenance externe)'
    Write-Host ''
    Write-Host '  .\Optimus.App.exe --status'
    Write-Host '  .\Optimus.App.exe "Optimus, allume les lumieres"          (simulation)'
    Write-Host '  .\Optimus.App.exe --real "Optimus, allume les lumieres"   (touches reellement envoyees)'
}
