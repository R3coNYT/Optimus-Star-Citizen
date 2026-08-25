<#
.SYNOPSIS
    Spike S0-6 - reconnaissance vocale a grammaire contrainte, comparee a Whisper.

.DESCRIPTION
    Mesure le moteur de reconnaissance integre a Windows (System.Speech) alimente par une
    grammaire fermee, construite depuis les phrases vocales du catalogue.

    Pourquoi cette piste : le spike S0-2 a mesure 2,7 s de transcription Whisper avec le jeu
    lance, contre 500 ms vises. Le build GPU est exclu sur la machine cible (6 Go de VRAM,
    Star Citizen en reclame deja 7,3). Reste a cesser d'employer un transcripteur generaliste
    pour choisir parmi 59 possibilites connues d'avance.

    Difference de nature, et c'est tout l'interet : Whisper transcrit librement, puis on
    rapproche le texte d'une commande. Un moteur a grammaire ne PEUT produire qu'une phrase
    autorisee. « boucliers » ne peut pas devenir « bouquilles » si « bouquilles » n'existe pas
    dans la grammaire. La metrique pertinente n'est donc plus le taux d'erreur de mots mais :
    a-t-il designe la bonne commande.

    Le meme jeu de WAV et le meme expected.tsv que S0-2 sont utilises : la comparaison est
    directe.

    ATTENTION - ne jamais mesurer ce moteur sur de la parole de SYNTHESE. Essai fait le
    2026-08-25 avec des echantillons produits par le TTS Windows : audio pourtant impeccable
    (PCM 22 kHz, 16 bits, crete a 50 %), et 0 % de reconnaissance, avec des confiances autour
    de 0,2. Le modele acoustique est entraine sur des voix humaines ; la synthese est trop
    reguliere, privee des micro-variations qu'il attend. Seuls des enregistrements reels,
    faits au micro de l'utilisateur, donnent une mesure de justesse exploitable.

.EXAMPLE
    .\bench-grammar-stt.ps1
    .\bench-grammar-stt.ps1 -AudioDir 'D:\enregistrements' -Repeat 3
#>
[CmdletBinding()]
param(
    [string] $AudioDir,
    [string] $CatalogPath,
    [int] $Repeat = 3,
    [string] $Culture = 'fr-FR',
    # Seuil de confiance en dessous duquel on considere que le moteur n'a rien reconnu de sur.
    [double] $ConfidenceThreshold = 0.5,
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param([string] $m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param([string] $m) Write-Host "    $m" -ForegroundColor Yellow }

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if (-not $CatalogPath) { $CatalogPath = Join-Path $repoRoot 'data\commands\starcitizen.core.json' }
if (-not (Test-Path -LiteralPath $CatalogPath)) { Write-Warn "Catalogue introuvable : $CatalogPath"; exit 1 }

# ------------------------------------------------------- Moteur de reconnaissance

Add-Type -AssemblyName System.Speech

Write-Step 'Moteurs de reconnaissance installes'
$recognizers = [System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers()
if ($recognizers.Count -eq 0) {
    Write-Warn "Aucun moteur de reconnaissance installe."
    Write-Warn "Ajoute le module vocal francais : Parametres > Heure et langue > Langue > Francais > Options."
    exit 1
}
foreach ($r in $recognizers) { Write-Ok ("{0,-38} {1}" -f $r.Name, $r.Culture.Name) }

$selected = $recognizers | Where-Object { $_.Culture.Name -eq $Culture } | Select-Object -First 1
if (-not $selected) {
    Write-Warn "Aucun moteur pour la culture $Culture."
    exit 1
}
Write-Ok "retenu : $($selected.Name)"

# ------------------------------------------------------------------- Grammaire

Write-Step 'Construction de la grammaire depuis le catalogue'
$catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json

# Chaque phrase est declinee avec et sans le mot d'eveil : c'est ainsi qu'elle sera prononcee.
# Le moteur choisira parmi ces alternatives et rien d'autre.
$alternatives = New-Object System.Collections.Generic.List[string]
$phraseToCommand = @{}

foreach ($command in $catalog.commands) {
    foreach ($phrase in $command.voice_phrases) {
        $clean = ($phrase -replace "[^\p{L}\p{N} ']", ' ') -replace '\s+', ' '
        $clean = $clean.Trim()
        if ($clean.Length -eq 0) { continue }

        foreach ($variant in @($clean, "optimus $clean")) {
            if (-not $phraseToCommand.ContainsKey($variant)) {
                $phraseToCommand[$variant] = $command.id
                $alternatives.Add($variant)
            }
        }
    }
}

Write-Ok "$($alternatives.Count) alternatives pour $($catalog.commands.Count) commandes"

$engine = New-Object System.Speech.Recognition.SpeechRecognitionEngine($selected.Id)

$choices = New-Object System.Speech.Recognition.Choices
$choices.Add([string[]] $alternatives.ToArray())

$builder = New-Object System.Speech.Recognition.GrammarBuilder
$builder.Culture = [System.Globalization.CultureInfo]::GetCultureInfo($Culture)
$builder.Append($choices)

$grammar = New-Object System.Speech.Recognition.Grammar($builder)

$loadWatch = [System.Diagnostics.Stopwatch]::StartNew()
$engine.LoadGrammar($grammar)
$loadWatch.Stop()
Write-Ok ("grammaire chargee en {0:F0} ms (cout paye une seule fois au demarrage)" -f $loadWatch.Elapsed.TotalMilliseconds)

# --------------------------------------------------------------- Echantillons

Write-Step 'Recherche des echantillons'
if (-not $AudioDir) {
    $candidates = @(
        (Join-Path $repoRoot 'docs\spikes\audio'),
        (Join-Path $scriptRoot 'Optimus.Spike.InputTest\docs\spikes\audio')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { $AudioDir = $candidate; break }
    }
}

if (-not $AudioDir -or -not (Test-Path -LiteralPath $AudioDir)) {
    Write-Warn "Aucun dossier d'echantillons. Utilise -AudioDir, ou enregistre-en avec :"
    Write-Warn "  .\Optimus.Spike.InputTest\run-spike.ps1 --mode voice --utterances 8"
    exit 1
}

$wavs = @(Get-ChildItem -Path $AudioDir -Filter '*.wav' | Sort-Object Name)
if ($wavs.Count -eq 0) { Write-Warn "Aucun WAV dans $AudioDir"; exit 1 }
Write-Ok "$($wavs.Count) echantillon(s) dans $AudioDir"

$expected = @{}
$manifest = Join-Path $AudioDir 'expected.tsv'
if (Test-Path -LiteralPath $manifest) {
    foreach ($line in (Get-Content -LiteralPath $manifest -Encoding UTF8)) {
        $parts = $line -split "`t", 2
        if ($parts.Count -eq 2) { $expected[$parts[0].Trim()] = $parts[1].Trim() }
    }
    Write-Ok "$($expected.Count) phrase(s) de reference"
}
else {
    Write-Warn 'Pas de expected.tsv : la justesse ne sera pas mesuree, seulement la latence.'
}

function Get-NormalizedText {
    param([string] $Text)
    if (-not $Text) { return '' }
    $t = $Text.ToLowerInvariant()
    $t = $t -replace '[àâä]', 'a' -replace '[éèêë]', 'e' -replace '[îï]', 'i'
    $t = $t -replace '[ôö]', 'o' -replace '[ùûü]', 'u' -replace 'ç', 'c'
    $t = $t -replace "[^a-z0-9 ]", ' '
    return ($t -replace '\s+', ' ').Trim()
}

# La phrase attendue est rapprochee de la commande qu'elle designe, pour juger sur la
# commande resolue plutot que sur le texte exact.
$normalizedToCommand = @{}
foreach ($key in $phraseToCommand.Keys) {
    $normalizedToCommand[(Get-NormalizedText $key)] = $phraseToCommand[$key]
}

# ------------------------------------------------------------------- Mesures

Write-Step 'Reconnaissance'
$results = @()

foreach ($wav in $wavs) {
    $expectedText = $expected[$wav.Name]
    $expectedCommand = $null
    if ($expectedText) {
        $normalized = Get-NormalizedText $expectedText
        if ($normalizedToCommand.ContainsKey($normalized)) { $expectedCommand = $normalizedToCommand[$normalized] }
    }

    foreach ($pass in 1..$Repeat) {
        $engine.SetInputToWaveFile($wav.FullName)

        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $recognition = $null
        try { $recognition = $engine.Recognize() } catch { }
        $watch.Stop()

        $text = if ($recognition) { $recognition.Text } else { '' }
        $confidence = if ($recognition) { [math]::Round($recognition.Confidence, 3) } else { 0 }
        $command = $null
        $normalizedResult = Get-NormalizedText $text
        if ($normalizedResult -and $normalizedToCommand.ContainsKey($normalizedResult)) {
            $command = $normalizedToCommand[$normalizedResult]
        }

        $accepted = $confidence -ge $ConfidenceThreshold

        $results += [pscustomobject]@{
            File = $wav.Name
            Pass = $pass
            LatencyMs = [math]::Round($watch.Elapsed.TotalMilliseconds, 1)
            Text = $text
            Confidence = $confidence
            Accepted = $accepted
            Command = $command
            ExpectedText = $expectedText
            ExpectedCommand = $expectedCommand
            Correct = ($null -ne $command -and $command -eq $expectedCommand -and $accepted)
        }
    }

    $last = $results | Where-Object { $_.File -eq $wav.Name } | Select-Object -Last 1
    $mark = if ($last.Correct) { 'OK  ' } elseif ($last.Command) { 'FAUX' } else { 'RIEN' }
    Write-Ok ("{0} {1,-22} {2,6} ms  conf {3:F2}  « {4} »" -f $mark, $wav.Name, $last.LatencyMs, $last.Confidence, $last.Text)
}

$engine.Dispose()

# ------------------------------------------------------------------ Synthese

Write-Step 'Synthese'

$latencies = @($results | ForEach-Object { $_.LatencyMs } | Sort-Object)
$p50 = $latencies[[int]($latencies.Count / 2)]
$p95Index = [math]::Min($latencies.Count - 1, [int][math]::Floor($latencies.Count * 0.95))
$p95 = $latencies[$p95Index]

# Quatre issues, et il faut absolument les distinguer : confondre un rejet prudent avec une
# meprise donne une lecture fausse du moteur. Seule la troisieme ligne est reellement grave -
# c'est le cas ou Optimus declencherait une action que personne n'a demandee.
$measured = @($results | Where-Object { $_.ExpectedCommand })
$correct = @($measured | Where-Object { $_.Command -eq $_.ExpectedCommand -and $_.Accepted }).Count
$rejected = @($measured | Where-Object { $_.Command -eq $_.ExpectedCommand -and -not $_.Accepted }).Count
$mistaken = @($measured | Where-Object { $_.Command -and $_.Command -ne $_.ExpectedCommand }).Count
$silent = @($measured | Where-Object { -not $_.Command }).Count

$textOk = $correct + $rejected
$rate = if ($measured.Count -gt 0) { [math]::Round(100.0 * $textOk / $measured.Count, 1) } else { 0 }

Write-Ok "latence p50                    : $p50 ms"
Write-Ok "latence p95                    : $p95 ms"
Write-Ok "bonne commande identifiee      : $textOk / $($measured.Count)  ($rate %)"
Write-Ok "  dont acceptees (conf >= $ConfidenceThreshold) : $correct"
if ($rejected -gt 0) { Write-Warn "  dont rejetees par le seuil   : $rejected  (bonne commande, confiance jugee trop basse)" }
if ($mistaken -gt 0) { Write-Warn "MEPRISES (action non demandee) : $mistaken  <- le seul cas reellement grave" }
if ($silent -gt 0)   { Write-Warn "aucune reconnaissance          : $silent" }

# --- Calibrage du seuil ---
# Le seuil arbitre entre deux echecs opposes : rejeter une vraie commande, ou en declencher
# une que l'utilisateur n'a pas prononcee. Les enonces hors grammaire servent de temoins :
# ils montrent a partir de quelle confiance le moteur cesse de confondre.
$outOfGrammar = @($results | Where-Object { -not $_.ExpectedCommand })

Write-Host ''
Write-Step 'Calibrage du seuil de confiance'
Write-Host '    seuil   commandes acceptees   faux declenchements (hors grammaire)'
foreach ($threshold in @(0.2, 0.3, 0.35, 0.4, 0.45, 0.5, 0.6, 0.7)) {
    # Pas de variable nommee $false : c'est une constante reservee de PowerShell.
    $accepted = @($measured | Where-Object { $_.Command -eq $_.ExpectedCommand -and $_.Confidence -ge $threshold }).Count
    $falseTriggers = @($outOfGrammar | Where-Object { $_.Command -and $_.Confidence -ge $threshold }).Count
    $flag = if ($falseTriggers -gt 0) { '  <- declenche a tort' } else { '' }
    Write-Host ("    {0,-7} {1,6} / {2,-14} {3,6}{4}" -f $threshold, $accepted, $measured.Count, $falseTriggers, $flag)
}

if ($outOfGrammar.Count -eq 0) {
    Write-Host ''
    Write-Warn "Aucun enonce hors grammaire dans le jeu d'essai : le seuil ne peut pas etre"
    Write-Warn "calibre serieusement. Enregistre quelques phrases sans rapport avec les commandes."
}
Write-Host ''
Write-Host "Rappel Whisper base, jeu lance (spike S0-2) : p50 3336 ms" -ForegroundColor DarkGray
Write-Host "Cible du budget de latence (docs/09)        : p95 <= 500 ms" -ForegroundColor DarkGray

# ------------------------------------------------------------------- Rapport

if (-not $ReportPath) {
    $dir = Join-Path $repoRoot 'docs\spikes'
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $ReportPath = Join-Path $dir ("S0-6-grammar-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$env:COMPUTERNAME.md")
}

$lines = @()
$lines += '# Spike S0-6 - reconnaissance a grammaire contrainte'
$lines += ''
$lines += "*$env:COMPUTERNAME - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')*"
$lines += ''
$lines += "Moteur : ``$($selected.Name)`` ($($selected.Culture.Name))"
$lines += ''
$lines += '| | |'
$lines += '|---|---|'
$lines += "| Alternatives dans la grammaire | $($alternatives.Count) |"
$lines += "| Commandes couvertes | $($catalog.commands.Count) |"
$lines += "| Chargement de la grammaire | $([math]::Round($loadWatch.Elapsed.TotalMilliseconds)) ms (une seule fois) |"
$lines += "| Seuil de confiance | $ConfidenceThreshold |"
$lines += "| Latence p50 | **$p50 ms** |"
$lines += "| Latence p95 | **$p95 ms** |"
$lines += "| Bonne commande identifiee | **$textOk / $($measured.Count)** ($rate %) |"
$lines += "| dont acceptees au seuil $ConfidenceThreshold | $correct |"
$lines += "| dont rejetees par le seuil | $rejected |"
$lines += "| Meprises (action non demandee) | **$mistaken** |"
$lines += "| Aucune reconnaissance | $silent |"
$lines += ''
$lines += '## Calibrage du seuil'
$lines += ''
$lines += '| Seuil | Commandes acceptees | Faux declenchements |'
$lines += '|---|---|---|'
foreach ($threshold in @(0.2, 0.3, 0.35, 0.4, 0.45, 0.5, 0.6, 0.7)) {
    $accepted = @($measured | Where-Object { $_.Command -eq $_.ExpectedCommand -and $_.Confidence -ge $threshold }).Count
    $falsePositives = @($outOfGrammar | Where-Object { $_.Command -and $_.Confidence -ge $threshold }).Count
    $lines += "| $threshold | $accepted / $($measured.Count) | $falsePositives |"
}
$lines += ''
$lines += 'Comparaison S0-2 (Whisper base, jeu lance) : p50 3336 ms, WER 9,8 %.'
$lines += 'Un moteur a grammaire ne peut produire qu une phrase autorisee : la metrique'
$lines += 'pertinente est la commande resolue, pas le texte exact.'
$lines += ''
$lines += '## Detail'
$lines += ''
$lines += '| Fichier | Passage | Latence (ms) | Confiance | Reconnu | Commande | Attendue | Correct |'
$lines += '|---|---|---|---|---|---|---|---|'
foreach ($row in $results) {
    $lines += "| $($row.File) | $($row.Pass) | $($row.LatencyMs) | $($row.Confidence) | $($row.Text) | $($row.Command) | $($row.ExpectedCommand) | $(if ($row.Correct) { 'oui' } else { 'non' }) |"
}

[System.IO.File]::WriteAllLines($ReportPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ''
Write-Host "Rapport : $ReportPath" -ForegroundColor Cyan
