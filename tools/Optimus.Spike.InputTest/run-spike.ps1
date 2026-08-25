<#
.SYNOPSIS
    Lance le spike S0-1 (injection clavier/souris) avec ou sans SDK .NET.

.DESCRIPTION
    Si le SDK .NET est installé, le projet est compilé et exécuté normalement.
    Sinon, les sources C# sont compilées à la volée par le compilateur intégré à Windows
    (Add-Type / csc du .NET Framework) : aucune installation n'est nécessaire.

.EXAMPLE
    .\run-spike.ps1
    Vérification automatique, sans Star Citizen.

.EXAMPLE
    .\run-spike.ps1 --mode game --key L --hold-key SPACE
    Plan d'observation dans Star Citizen : la touche L doit ouvrir/fermer les portes.

.NOTES
    Arrêt d'urgence pendant un plan : touche Échap.
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $SpikeArgs
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $scriptRoot 'src'

# Le rapport est écrit dans <base>\docs\spikes. Si l'outil a été copié seul (clé USB sur le PC
# de jeu, par exemple), on écrit à côté du script plutôt que deux niveaux au-dessus.
#
# Attention : Split-Path -Parent renvoie une chaîne VIDE quand on atteint la racine d'un lecteur
# (« G:\ » -> «  »), et Join-Path refuse une chaîne vide. D'où les gardes ci-dessous : le script
# doit fonctionner aussi bien depuis le dépôt que depuis G:\Optimus.Spike.InputTest.
function Get-ParentOrNull {
    param([string] $Path)
    if ([string]::IsNullOrEmpty($Path)) { return $null }
    $parent = Split-Path -Parent $Path
    if ([string]::IsNullOrEmpty($parent)) { return $null }
    return $parent
}

function Test-IsRepositoryRoot {
    param([string] $Path)
    if ([string]::IsNullOrEmpty($Path)) { return $false }
    foreach ($marker in @('.git', 'docs')) {
        if (Test-Path -LiteralPath (Join-Path $Path $marker) -ErrorAction SilentlyContinue) { return $true }
    }
    return $false
}

$repoRoot = $scriptRoot
$candidateRoot = Get-ParentOrNull (Get-ParentOrNull $scriptRoot)
if (Test-IsRepositoryRoot $candidateRoot) { $repoRoot = $candidateRoot }

if (-not $SpikeArgs) { $SpikeArgs = @() }

if (-not (Test-Path $srcDir)) {
    throw "Dossier source introuvable : $srcDir"
}

Push-Location $repoRoot

# Attention : Set-Location / Push-Location ne modifient PAS le répertoire courant du processus
# .NET. Sans cette ligne, le rapport serait écrit là où PowerShell a été démarré.
$previousCurrentDirectory = [System.Environment]::CurrentDirectory
[System.Environment]::CurrentDirectory = $repoRoot

try {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue

    if ($dotnet) {
        Write-Host "SDK .NET détecté : compilation du projet." -ForegroundColor DarkGray
        $project = Join-Path $scriptRoot 'Optimus.Spike.InputTest.csproj'
        & dotnet run --project $project -- @SpikeArgs
        exit $LASTEXITCODE
    }

    Write-Host "SDK .NET absent : compilation à la volée via Add-Type." -ForegroundColor DarkGray

    if (-not ([System.Management.Automation.PSTypeName]'Optimus.Spike.SpikeRunner').Type) {
        $sources = Get-ChildItem -Path $srcDir -Filter *.cs | Select-Object -ExpandProperty FullName
        if (-not $sources) { throw "Aucun fichier .cs dans $srcDir" }
        Add-Type -Path $sources
    }
    else {
        # .NET ne sait pas décharger un assembly : les types compilés lors d'un lancement
        # précédent restent en place pour toute la vie de la console.
        Write-Host "Types déjà chargés dans cette console : si tu viens de mettre à jour les .cs, ferme et rouvre PowerShell." -ForegroundColor Yellow
    }

    $exitCode = [Optimus.Spike.SpikeRunner]::Run([string[]]$SpikeArgs)
    exit $exitCode
}
finally {
    [System.Environment]::CurrentDirectory = $previousCurrentDirectory
    Pop-Location
}
