<#
.SYNOPSIS
    Produit un Optimus.Cli autonome, exécutable sans rien installer.

.DESCRIPTION
    Publie en « self-contained » : le runtime .NET est embarqué dans l'exécutable. La machine
    cible n'a besoin ni du SDK, ni du runtime - c'est ce qui permet de porter le banc d'essai
    sur le PC de jeu par simple copie, et c'est le même principe que retiendra l'installeur
    final (docs/05, RT-01).

    Le dossier « data » est copié à côté de l'exécutable : le programme le cherche d'abord
    dans son propre répertoire, puis en remontant l'arborescence.

    Le rognage (trimming) est volontairement désactivé : la lecture des catalogues passe par
    la réflexion de System.Text.Json, que le rognage casserait silencieusement. On préfère
    quelques dizaines de mégaoctets à un plantage au chargement sur la machine de l'utilisateur.

.EXAMPLE
    .\publish-cli.ps1
    .\publish-cli.ps1 -OutputDir 'G:\Optimus'
#>
[CmdletBinding()]
param(
    [string] $OutputDir,
    [string] $Runtime = 'win-x64',

    <#
      Publie une version qui se lance par « dotnet Optimus.Cli.dll » au lieu d'un exécutable
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

if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'publish\Optimus.Cli' }

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

$project = Join-Path $repoRoot 'tools\Optimus.Cli\Optimus.Cli.csproj'

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

Write-Step 'Copie des données'
foreach ($relative in @('data\commands', 'data\bindings')) {
    $source = Join-Path $repoRoot $relative
    $destination = Join-Path $OutputDir $relative
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
    Write-Ok $relative
}

$size = [math]::Round(((Get-ChildItem -LiteralPath $OutputDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)

Write-Host ''
Write-Ok "Dossier    : $OutputDir"
Write-Ok "Poids total : $size Mo"
Write-Host ''
Write-Host 'Copie ce dossier entier sur la machine de jeu, puis :' -ForegroundColor Cyan

if ($FrameworkDependent) {
    Write-Host '  Prerequis, une seule fois :  winget install Microsoft.DotNet.Runtime.8'
    Write-Host ''
    Write-Host '  dotnet Optimus.Cli.dll --status'
    Write-Host '  dotnet Optimus.Cli.dll "Optimus, allume les lumieres"          (simulation)'
    Write-Host '  dotnet Optimus.Cli.dll --real "Optimus, allume les lumieres"   (touches reellement envoyees)'
}
else {
    Write-Host '  Unblock-File .\Optimus.Cli.exe        (marque de provenance externe)'
    Write-Host ''
    Write-Host '  .\Optimus.Cli.exe --status'
    Write-Host '  .\Optimus.Cli.exe "Optimus, allume les lumieres"          (simulation)'
    Write-Host '  .\Optimus.Cli.exe --real "Optimus, allume les lumieres"   (touches reellement envoyees)'
}
