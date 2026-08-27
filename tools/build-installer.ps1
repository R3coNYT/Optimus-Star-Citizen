<#
.SYNOPSIS
    Construit l'installateur d'Optimus.

.DESCRIPTION
    Publie l'application et le banc d'essai, puis compile installer\optimus.iss.

    L'enchaînement est volontairement d'un seul tenant : compiler le script Inno
    à la main produirait un installateur bâti sur une publication dont personne
    ne sait de quand elle date, et c'est exactement le genre de paquet qu'on
    diffuse par erreur.

    Piper n'est pas embarqué. Le moteur pèse 37 Mo et chaque voix 60 de plus,
    pour une fonction dont on peut se passer : l'installateur les télécharge si
    le pilote les demande, en vérifiant chaque empreinte.

.PARAMETER SkipPublish
    Compile à partir de ce qui est déjà dans publish\, sans republier. À
    réserver aux essais du script d'installation lui-même.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -SkipPublish
#>

[CmdletBinding()]
param(
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Message) Write-Host "    $Message" -ForegroundColor Green }
function Write-Warn { param([string] $Message) Write-Host "    $Message" -ForegroundColor Yellow }

$repoRoot = Split-Path -Parent $PSScriptRoot
$script   = Join-Path $repoRoot 'installer\optimus.iss'
$appDir   = Join-Path $repoRoot 'publish\Optimus.App'

# --------------------------------------------------------------------- le compilateur
# Inno s'installe aussi bien pour la machine que pour l'utilisateur : chercher
# aux deux endroits évite un « introuvable » qui ne dirait pas quoi faire.
$candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $iscc) {
    throw @"
ISCC.exe est introuvable. Inno Setup 6 n'est pas installé.

    winget install --id JRSoftware.InnoSetup

Cherché dans :
$($candidates | ForEach-Object { "    $_" } | Out-String)
"@
}

Write-Step "Compilateur : $iscc"

# ------------------------------------------------------------------------ publication
if ($SkipPublish) {
    Write-Warn 'Publication ignorée : le paquet existant est réutilisé tel quel.'
}
else {
    Write-Step 'Publication de l''application'
    & (Join-Path $PSScriptRoot 'publish-app.ps1') | Out-Null
    if ($LASTEXITCODE) { throw 'La publication de l''application a échoué.' }

}

if (-not (Test-Path -LiteralPath $appDir)) {
    throw "Dossier publié absent : $appDir. Relancez sans -SkipPublish."
}

# --------------------------------------------------------------------------- version
# La version vient de l'assembly, pas d'une constante à tenir à jour ici : deux
# numéros séparés finissent toujours par diverger, et c'est celui du binaire
# qu'affiche la fenêtre d'Optimus.
$exe = Join-Path $appDir 'Optimus.App.exe'
$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion

if (-not $version) { $version = '0.1.0' }

# Inno n'accepte qu'un numéro à quatre nombres : « 0.1.0+abc123 » le ferait échouer.
$version = ($version -split '[+-]')[0]

Write-Ok "Version : $version"

# -------------------------------------------------------------------------- compilation
Write-Step 'Compilation de l''installateur'

& $iscc `
    "/DVersion=$version" `
    "/DSourceDir=$appDir" `
    $script

if ($LASTEXITCODE) { throw "ISCC a échoué (code $LASTEXITCODE)." }

$output = Join-Path $repoRoot "publish\Optimus-$version-installateur.exe"

if (-not (Test-Path -LiteralPath $output)) {
    throw "ISCC dit avoir réussi mais $output n'existe pas."
}

$size = [math]::Round((Get-Item -LiteralPath $output).Length / 1MB, 1)

Write-Host ''
Write-Ok "$output  ($size Mo)"
Write-Host ''
Write-Host @"
  Ce que contient ce fichier :

    Optimus et ses donnees. Installation par utilisateur, sans UAC, dans
    %LOCALAPPDATA%\Programs\Optimus. Le banc d'essai n'y est pas : publie en
    fichier autonome, il pesait 41 Mo que tout le monde aurait telecharges.

  Ce qu'il telecharge, si le pilote le demande :

    Le moteur Piper (37 Mo) et les voix choisies (60 Mo chacune), verifies
    par empreinte SHA-256, poses dans %APPDATA%\Optimus\piper.

  Ce qu'il ne resout pas :

    La signature de code (risque R16). Cet executable n'est pas signe :
    SmartScreen avertira chaque personne qui le telechargera, et Smart App
    Control le refusera. Un seul fichier a signer au lieu de vingt-quatre,
    mais il reste a signer avant toute diffusion.
"@ -ForegroundColor DarkGray
