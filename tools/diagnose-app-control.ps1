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

# ------------------------------------------------- Ce que Windows a REELLEMENT bloque

Write-Step 'Fichiers refuses par la politique, d''apres Windows'

# Smart App Control n'evalue pas seulement le point d'entree : il verifie CHAQUE binaire
# charge. Un .exe qui demarre peut donc se faire refuser un DLL en cours de route, avec pour
# seul symptome une notification vague. Windows, lui, nomme le fichier exact - autant le lui
# demander plutot que de deviner.
$blocked = @()
$logReadable = $true

# « Aucun evenement correspondant » n'est pas une erreur : c'est le cas normal quand rien
# n'a ete bloque. Le confondre avec un journal illisible enverrait chercher des droits
# administrateur pour rien.
$events = $null
try {
    $events = Get-WinEvent -FilterHashtable @{
        LogName = 'Microsoft-Windows-CodeIntegrity/Operational'
        StartTime = (Get-Date).AddDays(-2)
    } -ErrorAction Stop
}
catch [System.Diagnostics.Eventing.Reader.EventLogNotFoundException] {
    $logReadable = $false
    Write-Warn 'Journal CodeIntegrity absent : cette machine n''est pas concernee.'
}
catch {
    if ($_.Exception.Message -match 'Aucun|No events') {
        $events = @()
    }
    else {
        $logReadable = $false
        Write-Warn "Journal CodeIntegrity illisible : $($_.Exception.Message)"
        Write-Warn 'Relancez PowerShell en tant qu''administrateur pour y acceder.'
    }
}

# Le nom du fichier se lit dans les DONNEES de l'evenement, pas dans son message : celui-ci
# est traduit, et chercher « File Name: » sur un Windows francais - qui ecrit « Nom du
# fichier » - ne trouve evidemment rien. La structure XML, elle, garde des noms de champs
# invariants.
function Get-BlockedFile {
    param($Event)

    try {
        $xml = [xml] $Event.ToXml()

        foreach ($data in $xml.Event.EventData.Data) {
            if ($data.Name -match 'File|Path' -and $data.'#text') {
                $value = [string] $data.'#text'
                if ($value -match '\.(dll|exe|sys|ps1)$' -or $value -match '\\') { return $value }
            }
        }
    }
    catch {
        # On continue avec les autres pistes.
    }

    # Repli : n'importe quelle propriete qui ressemble a un chemin.
    foreach ($property in $Event.Properties) {
        $value = [string] $property.Value
        if ($value -match '\.(dll|exe|sys)$') { return $value }
    }

    # Dernier repli : un chemin dans le message, quelle que soit la langue.
    if ($Event.Message -match '(\\Device\\[^\s,;]+|[A-Za-z]:\\[^\s,;]+\.(?:dll|exe|sys))') {
        return $matches[1]
    }

    return $null
}

$unnamed = @()

foreach ($event in @($events)) {
    # 3077 = bloque, 3076 = signale en mode audit.
    if ($event.Id -ne 3077 -and $event.Id -ne 3076) { continue }

    $file = Get-BlockedFile -Event $event

    # Le filtre sur « Optimus » ne peut s'appliquer qu'une fois le nom connu : le message
    # traduit ne contient pas forcement le mot.
    if ($file -and $file -notmatch 'Optimus') { continue }

    if (-not $file) {
        $unnamed += $event
        continue
    }

    $blocked += [pscustomobject]@{
        Quand = $event.TimeCreated
        Mode  = if ($event.Id -eq 3077) { 'BLOQUE' } else { 'audit' }
        Fichier = $file
    }
}

# Si rien n'a pu etre nomme, on montre un evenement brut : mieux vaut du texte a lire que
# quinze lignes de « (nom absent) » qui n'apprennent rien.
if ($blocked.Count -eq 0 -and $unnamed.Count -gt 0) {
    Write-Warn "$($unnamed.Count) refus trouves, mais le nom du fichier n'a pas pu etre extrait."
    Write-Warn 'Evenement le plus recent, brut :'
    Write-Host ''
    Write-Host ($unnamed[0].Message) -ForegroundColor Gray
    Write-Host ''
    Write-Host '--- donnees structurees ---' -ForegroundColor Gray
    try {
        $xml = [xml] $unnamed[0].ToXml()
        foreach ($data in $xml.Event.EventData.Data) {
            Write-Host ("    {0} = {1}" -f $data.Name, $data.'#text') -ForegroundColor Gray
        }
    }
    catch {
        Write-Warn "XML illisible : $($_.Exception.Message)"
    }
    Write-Host ''
}

if ($blocked.Count -eq 0 -and $logReadable) {
    Write-Ok 'Aucun refus concernant Optimus dans les deux derniers jours.'
    Write-Ok 'Si une notification apparait quand meme, relevez le chemin exact qu''elle nomme.'
}
else {
    Write-Bad "$($blocked.Count) refus concernant Optimus :"
    foreach ($entry in ($blocked | Sort-Object Quand -Descending | Select-Object -First 15)) {
        Write-Bad ("  {0:HH:mm:ss}  {1,-7} {2}" -f $entry.Quand, $entry.Mode, $entry.Fichier)
    }
    Write-Host ''
    Write-Warn 'Ce sont ces fichiers-la qu''il faut faire accepter, pas seulement l''executable.'
}

# ------------------------------------------------------------------ Conclusion

Write-Step 'Ce qu''il faut en retenir'

if ($sacState -eq 1) {
    Write-Host ''
    Write-Host '    Smart App Control est ACTIF. Retirer la marque du web ne suffira pas :' -ForegroundColor Red
    Write-Host '    il refuse par principe tout binaire sans reputation etablie, et chaque' -ForegroundColor Red
    Write-Host '    publication d''Optimus en produit un nouveau.' -ForegroundColor Red
    Write-Host ''
    Write-Host '    Mesure sur R3CON-PC le 2026-08-26 : seuls les assemblys de POINT' -ForegroundColor Yellow
    Write-Host '    D''ENTREE sont refuses (Optimus.App.dll, Optimus.Cli.dll et leurs .exe).' -ForegroundColor Gray
    Write-Host '    Optimus.Core.dll et Optimus.Infrastructure.dll - celui qui injecte les' -ForegroundColor Gray
    Write-Host '    frappes - passent toujours. Ce n''est donc pas ce que fait Optimus qui le' -ForegroundColor Gray
    Write-Host '    rend suspect, mais son absence de reputation.' -ForegroundColor Gray
    Write-Host ''
    Write-Host '    Le lancement par dotnet.exe NE SUFFIT PAS : l''assembly d''entree est' -ForegroundColor Yellow
    Write-Host '    evalue de la meme facon. Ce contournement a ete tente et refuse.' -ForegroundColor Gray
    Write-Host ''
    Write-Host '    Ce qui reste :' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '      1. Compiler sur cette machine (dotnet build, puis lancer depuis bin).' -ForegroundColor Yellow
    Write-Host '         A ESSAYER UNE FOIS, sans garantie : un binaire compile localement n''a' -ForegroundColor Gray
    Write-Host '         pas la meme provenance qu''un fichier copie, et la politique en tient' -ForegroundColor Gray
    Write-Host '         parfois compte. Dix minutes pour trancher la question definitivement.' -ForegroundColor Gray
    Write-Host ''
    Write-Host '      2. Signer les binaires. Reponse definitive, prevue au risque R16, mais un' -ForegroundColor Yellow
    Write-Host '         certificat auto-signe ne satisfait pas SAC : il faut un certificat' -ForegroundColor Gray
    Write-Host '         d''editeur, de preference EV (~300-400 EUR/an).' -ForegroundColor Gray
    Write-Host ''
    Write-Host '      3. Desactiver Smart App Control.' -ForegroundColor Yellow
    Write-Host '         ATTENTION : IRREVERSIBLE. Une fois desactive, SAC ne peut plus etre' -ForegroundColor Red
    Write-Host '         reactive sans reinstaller Windows. Ce choix vous appartient : ce script' -ForegroundColor Red
    Write-Host '         ne le fera pas a votre place.' -ForegroundColor Red
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
