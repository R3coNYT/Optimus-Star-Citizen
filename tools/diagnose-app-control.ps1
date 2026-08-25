<#
.SYNOPSIS
    Diagnostique et lève un blocage « stratégie de contrôle d'application » (0x800711C7).

.DESCRIPTION
    Smart App Control refuse d'exécuter un binaire sans réputation. Chaque publication
    d'Optimus produit un fichier au hash inédit : le problème est donc structurel, pas
    accidentel, et il se reproduira à chaque mise à jour tant qu'un certificat reconnu
    ne signera pas les binaires (risque R16).

    Deux causes se ressemblent et ne se soignent pas pareil :

      1. La « marque du web » (Zone.Identifier), attrapée en copiant depuis une clé USB,
         un partage réseau ou un téléchargement. Elle se retire sans rien désactiver,
         et c'est la cause la plus fréquente.

      2. Smart App Control en mode « Actif ». Là, aucune manipulation de fichier ne suffit.

    Le script rapporte l'état, puis retire la marque du web si -Fix est passé. Il ne
    désactive JAMAIS Smart App Control : cette opération est irréversible sans
    réinstaller Windows, et ce choix appartient à l'utilisateur seul.

.EXAMPLE
    .\diagnose-app-control.ps1 -Path 'D:\app\80-Star Citizen\Optimus\OptimusCLI'

.EXAMPLE
    .\diagnose-app-control.ps1 -Path 'D:\app\80-Star Citizen\Optimus\OptimusCLI' -Fix
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Path,
    [switch] $Fix
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param([string] $m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param([string] $m) Write-Host "    $m" -ForegroundColor Yellow }
function Write-Bad  { param([string] $m) Write-Host "    $m" -ForegroundColor Red }

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Bad "Dossier introuvable : $Path"
    exit 1
}

# ---------------------------------------------------------- Smart App Control

Write-Step 'Etat de Smart App Control'

$sacState = $null
$policyKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy'

if (Test-Path -LiteralPath $policyKey) {
    $value = Get-ItemProperty -LiteralPath $policyKey -ErrorAction SilentlyContinue
    if ($null -ne $value) { $sacState = $value.VerifiedAndReputablePolicyState }
}

switch ($sacState) {
    0 { Write-Ok 'Desactive - ce n''est pas lui qui bloque.' }
    1 { Write-Bad 'ACTIF - il bloque tout binaire sans reputation, quoi qu''on fasse au fichier.' }
    2 { Write-Warn 'Mode evaluation - il peut bloquer par intermittence, ce qui explique qu''une publication passe et pas la suivante.' }
    default { Write-Warn "Etat indetermine (VerifiedAndReputablePolicyState = $sacState). Machine non concernee, ou cle absente." }
}

# ------------------------------------------------------------ Marque du web

Write-Step 'Marque du web sur les fichiers publies'

$targets = Get-ChildItem -LiteralPath $Path -Recurse -File -Include '*.dll', '*.exe', '*.json' -ErrorAction SilentlyContinue
if (-not $targets) {
    $targets = Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue
}

$marked = @()
foreach ($file in $targets) {
    $stream = Get-Item -LiteralPath $file.FullName -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue
    if ($null -ne $stream) { $marked += $file }
}

Write-Ok "$($targets.Count) fichier(s) examine(s)"

if ($marked.Count -eq 0) {
    Write-Ok 'Aucune marque du web : la copie n''est pas en cause.'
}
else {
    Write-Warn "$($marked.Count) fichier(s) portent la marque du web :"
    foreach ($file in $marked | Select-Object -First 10) {
        Write-Warn "  $($file.Name)"
    }
    if ($marked.Count -gt 10) { Write-Warn "  ... et $($marked.Count - 10) autres" }
}

# ------------------------------------------------------------------- Reparation

if ($Fix -and $marked.Count -gt 0) {
    Write-Step 'Retrait de la marque du web'
    foreach ($file in $marked) {
        Unblock-File -LiteralPath $file.FullName
    }
    Write-Ok "$($marked.Count) fichier(s) debloque(s)"
    Write-Ok 'Relancez : dotnet Optimus.Cli.dll --bindings'
}
elseif ($marked.Count -gt 0) {
    Write-Step 'Reparation'
    Write-Warn 'Relancez avec -Fix pour retirer la marque du web.'
}

# ------------------------------------------------------------------ Conclusion

Write-Step 'Ce qu''il faut en retenir'

if ($sacState -eq 1) {
    Write-Host ''
    Write-Host '    Smart App Control est ACTIF. Retirer la marque du web ne suffira pas :' -ForegroundColor Red
    Write-Host '    il refuse par principe tout binaire sans reputation etablie, et chaque' -ForegroundColor Red
    Write-Host '    publication d''Optimus en produit un nouveau.' -ForegroundColor Red
    Write-Host ''
    Write-Host '    Trois sorties, par ordre de preference :' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '      1. Executer depuis les sources sur cette machine :' -ForegroundColor Yellow
    Write-Host '         dotnet run --project tools/Optimus.Cli -- --bindings' -ForegroundColor Gray
    Write-Host '         Le SDK compile dans un dossier de travail que la politique traite' -ForegroundColor Gray
    Write-Host '         autrement. C''est la voie a essayer en premier : rien a desactiver.' -ForegroundColor Gray
    Write-Host ''
    Write-Host '      2. Signer les binaires avec un certificat reconnu. C''est la vraie' -ForegroundColor Yellow
    Write-Host '         reponse, prevue au risque R16, mais elle suppose un certificat' -ForegroundColor Gray
    Write-Host '         d''editeur - un certificat auto-signe ne satisfait pas SAC.' -ForegroundColor Gray
    Write-Host ''
    Write-Host '      3. Desactiver Smart App Control.' -ForegroundColor Yellow
    Write-Host '         ATTENTION : cette operation est IRREVERSIBLE. Une fois desactive,' -ForegroundColor Red
    Write-Host '         SAC ne peut plus etre reactive sans reinstaller Windows. Ce choix' -ForegroundColor Red
    Write-Host '         vous appartient : ce script ne le fera pas a votre place.' -ForegroundColor Red
    Write-Host '         Securite Windows > Controle des applications et du navigateur >' -ForegroundColor Gray
    Write-Host '         Parametres de Smart App Control.' -ForegroundColor Gray
    Write-Host ''
}
elseif ($marked.Count -gt 0 -and -not $Fix) {
    Write-Ok 'La marque du web explique tres probablement le blocage. Relancez avec -Fix.'
}
else {
    Write-Ok 'Rien de bloquant detecte ici. Si le blocage persiste, relevez le message exact.'
}
