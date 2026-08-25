<#
.SYNOPSIS
    Spike S0-2 - mesure la latence de transcription de Whisper sur cette machine.

.DESCRIPTION
    Optimus utilisera Whisper.net, qui n'est qu'une enveloppe .NET autour de whisper.cpp :
    mesurer le binaire whisper.cpp donne donc directement les chiffres de production, sans
    exiger le SDK .NET.

    Le script distingue deux temps que l'on confond souvent :

      - le CHARGEMENT du modèle, payé une seule fois au démarrage d'Optimus ;
      - l'INFÉRENCE proprement dite, payée à chaque phrase, et seule à compter dans le
        budget de latence de docs/09 (cible : p95 <= 500 ms).

    whisper.cpp publie lui-même ces temps en fin d'exécution : on les lit au lieu de les
    estimer depuis la durée du processus, qui inclurait le chargement à chaque fois.

    Les WAV analysés sont ceux produits par le mode `voice` du spike S0-3 : vrai micro,
    vraie voix, vrai bruit de fond.

.EXAMPLE
    .\bench-stt.ps1 -WhisperDir 'C:\outils\whisper' -Models 'ggml-base.bin','ggml-small.bin'

.NOTES
    À récupérer soi-même, depuis les sources officielles :
      - binaires   : https://github.com/ggml-org/whisper.cpp/releases
      - modèles    : https://huggingface.co/ggerganov/whisper.cpp  (ggml-tiny/base/small/medium)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $WhisperDir,
    [string[]] $Models,
    [string] $AudioDir,
    [string] $Language = 'fr',
    [int] $Repeat = 3,
    # Nombre de threads passé à whisper (-t). 0 = valeur par défaut du binaire.
    # Accepte une liste pour balayer plusieurs valeurs en un seul passage : -Threads 4,6,8
    [int[]] $Threads = @(0),
    # Taille du contexte audio (-ac). Whisper encode par défaut une fenêtre de 30 s, même pour
    # un énoncé de 2 s : l'essentiel du calcul porte sur du silence. Réduire ce contexte accélère
    # fortement les phrases courtes, au prix d'une possible perte de précision - que le WER
    # mesuré ici permet justement de quantifier. 0 = valeur par défaut (contexte complet).
    [int[]] $AudioContext = @(0),
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param([string] $m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param([string] $m) Write-Host "    $m" -ForegroundColor Yellow }

# ------------------------------------------------------------- Localisation

# Get-ChildItem -Recurse sur un chemin INEXISTANT met ~60 s a echouer (mesure : 60 227 ms,
# contre 452 ms sur un chemin valide). On verifie donc l'existence avant toute recursion.
if (-not (Test-Path -LiteralPath $WhisperDir)) {
    Write-Warn "Dossier introuvable : $WhisperDir"
    exit 1
}

# --------------------------------------------------- Contexte de la mesure

# Une transcription est une charge CPU lourde. Mesurer pendant que Star Citizen consomme la
# machine, ou avec la machine libre, donne deux chiffres radicalement différents - et les deux
# comptent : le premier est la réalité d'usage, le second dit ce dont le processeur est capable.
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$machine = [ordered]@{
    Processeur = $cpu.Name.Trim()
    Coeurs = "$($cpu.NumberOfCores) physiques / $($cpu.NumberOfLogicalProcessors) logiques"
    MemoireGo = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    Threads = ($Threads | ForEach-Object { if ($_ -gt 0) { "$_" } else { 'défaut' } }) -join ', '
    'Contexte audio' = ($AudioContext | ForEach-Object { if ($_ -gt 0) { "$_" } else { 'complet (30 s)' } }) -join ', '
}

$gameProcess = Get-Process -Name 'StarCitizen' -ErrorAction SilentlyContinue | Select-Object -First 1
$machine['StarCitizen pendant la mesure'] = if ($gameProcess) { "OUI (pid $($gameProcess.Id))" } else { 'non' }

Write-Step 'Contexte'
foreach ($key in $machine.Keys) { Write-Ok ("{0,-30} {1}" -f $key, $machine[$key]) }
if ($gameProcess) {
    Write-Warn ''
    Write-Warn 'Star Citizen tourne pendant la mesure : il consomme le processeur et va'
    Write-Warn 'gonfler les temps de transcription. Fais aussi un passage jeu fermé pour'
    Write-Warn 'distinguer « la machine est lente » de « le jeu affame Whisper ».'
}

Write-Step 'Recherche du binaire whisper.cpp'
$exe = $null
foreach ($name in @('whisper-cli.exe', 'main.exe', 'whisper.exe')) {
    $found = Get-ChildItem -Path $WhisperDir -Filter $name -Recurse -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if ($found) { $exe = $found.FullName; break }
}
if (-not $exe) {
    Write-Warn "Aucun binaire trouvé dans $WhisperDir (cherché : whisper-cli.exe, main.exe, whisper.exe)."
    Write-Warn 'Télécharge une release depuis https://github.com/ggml-org/whisper.cpp/releases'
    exit 1
}
Write-Ok $exe

Write-Step 'Recherche des modèles'
$modelFiles = @()
if ($Models) {
    foreach ($model in $Models) {
        $path = $model
        if (-not (Test-Path -LiteralPath $path)) {
            $candidate = Get-ChildItem -Path $WhisperDir -Filter $model -Recurse -ErrorAction SilentlyContinue |
                         Select-Object -First 1
            if ($candidate) { $path = $candidate.FullName }
        }
        if (Test-Path -LiteralPath $path) { $modelFiles += (Resolve-Path -LiteralPath $path).Path }
        else { Write-Warn "modèle introuvable : $model" }
    }
}
else {
    $modelFiles = @(Get-ChildItem -Path $WhisperDir -Filter 'ggml-*.bin' -Recurse -ErrorAction SilentlyContinue |
                    Sort-Object Length | Select-Object -ExpandProperty FullName)
}
if (-not $modelFiles) {
    Write-Warn 'Aucun modèle .bin trouvé. Récupère par exemple ggml-base.bin et ggml-small.bin'
    Write-Warn 'depuis https://huggingface.co/ggerganov/whisper.cpp'
    exit 1
}
foreach ($m in $modelFiles) {
    Write-Ok ("{0}  ({1} Mo)" -f (Split-Path -Leaf $m), [math]::Round((Get-Item -LiteralPath $m).Length / 1MB))
}

Write-Step 'Recherche des échantillons audio'
if (-not $AudioDir) {
    # Les WAV sont écrits par le mode `voice` du spike S0-3, dont l'emplacement dépend de la
    # façon dont les outils ont été copiés (dépôt complet, dossier tools seul, clé USB...).
    # On essaie les dispositions plausibles et on retient la première qui contient des WAV.
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $parentDir = Split-Path -Parent $scriptDir

    $candidates = @()
    if ($parentDir) { $candidates += (Join-Path $parentDir 'docs\spikes\audio') }
    $candidates += (Join-Path $scriptDir 'docs\spikes\audio')
    $candidates += (Join-Path $scriptDir 'Optimus.Spike.InputTest\docs\spikes\audio')
    if ($parentDir) { $candidates += (Join-Path $parentDir 'Optimus.Spike.InputTest\docs\spikes\audio') }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            $found = @(Get-ChildItem -Path $candidate -Filter '*.wav' -ErrorAction SilentlyContinue)
            if ($found.Count -gt 0) { $AudioDir = $candidate; break }
            if (-not $AudioDir) { $AudioDir = $candidate }
        }
    }
    if (-not $AudioDir) { $AudioDir = $candidates[0] }
}
$wavs = @(Get-ChildItem -Path $AudioDir -Filter '*.wav' -ErrorAction SilentlyContinue)
if (-not $wavs) {
    Write-Warn "Aucun WAV dans $AudioDir"
    Write-Warn 'Enregistre d abord des énoncés avec le spike S0-3 :'
    Write-Warn '  .\Optimus.Spike.InputTest\run-spike.ps1 --mode voice --utterances 5'
    exit 1
}
Write-Ok "$($wavs.Count) échantillon(s) dans $AudioDir"

# ------------------------------------------------------------------ Mesures

# whisper.cpp écrit sa progression ET ses temps sur stderr. Sous PowerShell 5.1, `& exe 2>&1`
# enveloppe chaque ligne de stderr dans un ErrorRecord ; combiné à ErrorActionPreference='Stop',
# la première ligne fait échouer le script alors que le programme tourne très bien.
# On pilote donc le processus directement : les deux flux sont capturés tels quels, et le code
# de sortie reste exploitable.
function Invoke-CapturedProcess {
    param([string] $FilePath, [string[]] $Arguments)

    $quoted = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ($quoted -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    $null = $process.Start()

    # Lecture asynchrone des deux flux : lire l'un puis l'autre séquentiellement peut bloquer
    # si le tampon du second se remplit entre-temps.
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()

    $combined = ($stdout.Result + "`n" + $stderr.Result)
    $exitCode = $process.ExitCode
    $process.Dispose()

    return @{
        ExitCode = $exitCode
        Lines = @($combined -split "`r?`n")
    }
}

# whisper.cpp imprime ses propres temps : on les lit plutôt que de chronométrer le
# processus, dont la durée inclurait le chargement du modèle à chaque appel.
function Get-WhisperTimings {
    param([string[]] $Output)
    $result = @{ LoadMs = $null; TotalMs = $null; EncodeMs = $null; DecodeMs = $null; Text = '' }
    $textLines = @()

    foreach ($line in $Output) {
        if ($line -match 'load time\s*=\s*([\d\.]+)\s*ms') { $result.LoadMs = [double] $matches[1]; continue }
        if ($line -match 'total time\s*=\s*([\d\.]+)\s*ms') { $result.TotalMs = [double] $matches[1]; continue }
        if ($line -match 'encode time\s*=\s*([\d\.]+)\s*ms') { $result.EncodeMs = [double] $matches[1]; continue }
        if ($line -match 'decode time\s*=\s*([\d\.]+)\s*ms') { $result.DecodeMs = [double] $matches[1]; continue }
        # Les lignes de transcription commencent par un horodatage [00:00:00.000 --> ...]
        if ($line -match '^\[[\d:\.]+\s*-->\s*[\d:\.]+\]\s*(.+)$') { $textLines += $matches[1].Trim() }
    }

    $result.Text = ($textLines -join ' ').Trim()
    return $result
}

# -------------------------------------------------- Comparaison à la vérité terrain

# Le mode `voice` du spike S0-3 écrit un expected.tsv « fichier[TAB]phrase attendue ».
$expected = @{}
$manifest = Join-Path $AudioDir 'expected.tsv'
if (Test-Path -LiteralPath $manifest) {
    foreach ($line in (Get-Content -LiteralPath $manifest -Encoding UTF8)) {
        $parts = $line -split "`t", 2
        if ($parts.Count -eq 2) { $expected[$parts[0].Trim()] = $parts[1].Trim() }
    }
    Write-Ok "$($expected.Count) phrase(s) de référence lues dans expected.tsv"
}
else {
    Write-Warn 'Pas de expected.tsv : la précision ne sera pas mesurée, seulement la latence.'
}

function ConvertTo-ComparableWords {
    param([string] $Text)
    if (-not $Text) { return @() }
    $normalized = $Text.ToLowerInvariant()
    $normalized = $normalized -replace '[àâä]', 'a' -replace '[éèêë]', 'e' -replace '[îï]', 'i'
    $normalized = $normalized -replace '[ôö]', 'o' -replace '[ùûü]', 'u' -replace 'ç', 'c'
    $normalized = $normalized -replace "[^a-z0-9' ]", ' '
    return @($normalized -split '\s+' | Where-Object { $_ -ne '' })
}

# Taux d'erreur de mots : distance d'édition au niveau du MOT, rapportée au nombre de mots
# attendus. C'est la mesure standard en reconnaissance vocale - bien plus parlante qu'une
# comparaison exacte, qui punirait une virgule.
function Get-WordErrorRate {
    param([string] $Expected, [string] $Actual)

    $reference = ConvertTo-ComparableWords $Expected
    $hypothesis = ConvertTo-ComparableWords $Actual
    if ($reference.Count -eq 0) { return $null }

    $distance = New-Object 'int[,]' ($reference.Count + 1), ($hypothesis.Count + 1)
    for ($i = 0; $i -le $reference.Count; $i++) { $distance[$i, 0] = $i }
    for ($j = 0; $j -le $hypothesis.Count; $j++) { $distance[0, $j] = $j }

    for ($i = 1; $i -le $reference.Count; $i++) {
        for ($j = 1; $j -le $hypothesis.Count; $j++) {
            $cost = if ($reference[$i - 1] -eq $hypothesis[$j - 1]) { 0 } else { 1 }
            $substitution = $distance[($i - 1), ($j - 1)] + $cost
            $deletion = $distance[($i - 1), $j] + 1
            $insertion = $distance[$i, ($j - 1)] + 1
            $best = [math]::Min($substitution, [math]::Min($deletion, $insertion))
            $distance[$i, $j] = $best
        }
    }

    # L'indexation d'un tableau 2D contient une virgule ; imbriquée dans les arguments d'un
    # appel de méthode, PowerShell la prend pour un séparateur d'arguments et refuse de parser.
    # D'où l'affectation intermédiaire.
    $errors = $distance[$reference.Count, $hypothesis.Count]
    return [math]::Round(100.0 * $errors / $reference.Count, 1)
}

function Get-WavDurationMs {
    param([string] $Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 44) { return 0 }
    $sampleRate = [BitConverter]::ToInt32($bytes, 24)
    $bitsPerSample = [BitConverter]::ToInt16($bytes, 34)
    $channels = [BitConverter]::ToInt16($bytes, 22)
    $dataSize = $bytes.Length - 44
    if ($sampleRate -le 0) { return 0 }
    return [int](($dataSize / ($sampleRate * $channels * ($bitsPerSample / 8))) * 1000)
}

$results = @()

foreach ($model in $modelFiles) {
  $modelName = [System.IO.Path]::GetFileNameWithoutExtension($model)

  foreach ($threadCount in $Threads) {
   foreach ($audioCtx in $AudioContext) {
    $threadLabel = if ($threadCount -gt 0) { "$threadCount threads" } else { 'threads par défaut' }
    $ctxLabel = if ($audioCtx -gt 0) { ", contexte audio $audioCtx" } else { '' }
    Write-Step "Modèle $modelName - $threadLabel$ctxLabel"

    foreach ($wav in $wavs) {
        $audioMs = Get-WavDurationMs -Path $wav.FullName

        foreach ($run in 1..$Repeat) {
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $arguments = @('-m', $model, '-f', $wav.FullName, '-l', $Language)
            if ($threadCount -gt 0) { $arguments += @('-t', "$threadCount") }
            if ($audioCtx -gt 0) { $arguments += @('-ac', "$audioCtx") }
            $execution = Invoke-CapturedProcess -FilePath $exe -Arguments $arguments
            $sw.Stop()

            $output = $execution.Lines
            $timings = Get-WhisperTimings -Output $output

            if (-not $timings.TotalMs) {
                Write-Warn "Sortie inattendue pour $($wav.Name) (code de sortie $($execution.ExitCode)) - extrait :"
                foreach ($line in ($output | Where-Object { $_ -ne '' } | Select-Object -Last 6)) {
                    Write-Warn "  $line"
                }
                continue
            }

            $inference = if ($timings.LoadMs) { $timings.TotalMs - $timings.LoadMs } else { $timings.TotalMs }

            $results += [pscustomobject]@{
                Model = $modelName
                Threads = if ($threadCount -gt 0) { $threadCount } else { 0 }
                AudioCtx = if ($audioCtx -gt 0) { $audioCtx } else { 0 }
                File = $wav.Name
                Run = $run
                AudioMs = $audioMs
                LoadMs = [math]::Round($timings.LoadMs, 1)
                InferenceMs = [math]::Round($inference, 1)
                ProcessMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
                RTF = if ($audioMs -gt 0) { [math]::Round($inference / $audioMs, 3) } else { $null }
                Text = $timings.Text
                Expected = $expected[$wav.Name]
                WER = if ($expected.ContainsKey($wav.Name)) { Get-WordErrorRate $expected[$wav.Name] $timings.Text } else { $null }
            }
        }

        $last = $results | Where-Object { $_.Model -eq $modelName -and $_.File -eq $wav.Name } | Select-Object -Last 1
        if ($last) {
            Write-Ok ("{0,-22} {1,5} ms audio -> {2,6} ms inference   « {3} »" -f `
                      $wav.Name, $audioMs, $last.InferenceMs, $last.Text)
        }
    }
   }
  }
}

if (-not $results) { Write-Warn 'Aucune mesure exploitable.'; exit 2 }

# ------------------------------------------------------------------ Synthèse

Write-Step 'Synthèse'

$summary = $results | Group-Object Model, Threads, AudioCtx | ForEach-Object {
    $inferences = @($_.Group | ForEach-Object { $_.InferenceMs } | Sort-Object)
    $p95Index = [math]::Min($inferences.Count - 1, [int][math]::Floor($inferences.Count * 0.95))
    [pscustomobject]@{
        Modele = $_.Group[0].Model
        Threads = if ($_.Group[0].Threads -gt 0) { $_.Group[0].Threads } else { 'défaut' }
        AudioCtx = if ($_.Group[0].AudioCtx -gt 0) { $_.Group[0].AudioCtx } else { 'complet' }
        Mesures = $inferences.Count
        'Chargement (ms)' = [math]::Round((($_.Group | ForEach-Object { $_.LoadMs } | Measure-Object -Average).Average), 0)
        'Inference p50 (ms)' = $inferences[[int]($inferences.Count / 2)]
        'Inference p95 (ms)' = $inferences[$p95Index]
        'RTF moyen' = [math]::Round((($_.Group | Where-Object { $_.RTF } | ForEach-Object { $_.RTF } | Measure-Object -Average).Average), 3)
        'WER moyen (%)' = $(
            $wers = @($_.Group | Where-Object { $_.WER -ne $null } | ForEach-Object { $_.WER })
            if ($wers.Count -gt 0) { [math]::Round(($wers | Measure-Object -Average).Average, 1) } else { $null }
        )
        'Cible p95 <= 500 ms' = if ($inferences[$p95Index] -le 500) { 'OUI' } else { 'non' }
    }
}
$summary | Format-Table -AutoSize | Out-Host

Write-Step 'Transcriptions (une ligne par configuration)'
# Grouper sur Model+File seulement masquerait toutes les configurations sauf la dernière :
# on ne verrait jamais ce que produit réellement chaque réglage.
$results | Group-Object Model, Threads, AudioCtx, File | ForEach-Object { $_.Group | Select-Object -Last 1 } |
    Select-Object Model, Threads, AudioCtx, File, WER, Text | Format-Table -AutoSize -Wrap | Out-Host

# ------------------------------------------------------------------- Rapport

if (-not $ReportPath) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
    $dir = Join-Path $repoRoot 'docs\spikes'
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $ReportPath = Join-Path $dir ("S0-2-stt-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$env:COMPUTERNAME.md")
}

$lines = @()
$lines += '# Spike S0-2 - transcription Whisper'
$lines += ''
$lines += "*$env:COMPUTERNAME - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')*"
$lines += ''
$lines += "Binaire : ``$exe``"
$lines += ''
$lines += '## Contexte'
$lines += ''
$lines += '| | |'
$lines += '|---|---|'
foreach ($key in $machine.Keys) { $lines += "| $key | $($machine[$key]) |" }
$lines += ''
$lines += 'Whisper encode toujours une fenêtre de **30 secondes**, quelle que soit la durée réelle'
$lines += 'de l énoncé : un « ouvre les portes » de 2 s coûte autant qu une phrase de 25 s. Le RTF'
$lines += 'rapporté à la durée du clip est donc trompeur - c est le temps ABSOLU par énoncé qui'
$lines += 'compte. Bonne nouvelle au passage : les phrases longues ne coûtent pas plus cher.'
$lines += ''
$lines += '## Synthèse'
$lines += ''
$lines += '| Modèle | Threads | Contexte audio | Mesures | Chargement (ms) | Inférence p50 (ms) | Inférence p95 (ms) | RTF moyen | WER moyen (%) | Cible p95 <= 500 ms |'
$lines += '|---|---|---|---|---|---|---|---|---|---|'
foreach ($row in $summary) {
    $lines += "| $($row.Modele) | $($row.Threads) | $($row.AudioCtx) | $($row.Mesures) | $($row.'Chargement (ms)') | $($row.'Inference p50 (ms)') | $($row.'Inference p95 (ms)') | $($row.'RTF moyen') | $($row.'WER moyen (%)') | $($row.'Cible p95 <= 500 ms') |"
}
$lines += ''
$lines += 'Le chargement est payé une seule fois au démarrage d Optimus ; seule l inférence entre'
$lines += 'dans le budget de latence de docs/09.'
$lines += ''
$lines += '## Transcriptions'
$lines += ''
$lines += '| Modèle | Threads | Contexte audio | Fichier | Attendu | Transcription | WER (%) |'
$lines += '|---|---|---|---|---|---|---|'
foreach ($row in ($results | Group-Object Model, Threads, AudioCtx, File | ForEach-Object { $_.Group | Select-Object -Last 1 })) {
    $ctx = if ($row.AudioCtx -gt 0) { $row.AudioCtx } else { 'complet' }
    $lines += "| $($row.Model) | $($row.Threads) | $ctx | $($row.File) | $($row.Expected) | $($row.Text) | $($row.WER) |"
}
$lines += ''
$lines += '## Détail'
$lines += ''
$lines += '| Modèle | Threads | Fichier | Passage | Audio (ms) | Chargement (ms) | Inférence (ms) | RTF |'
$lines += '|---|---|---|---|---|---|---|---|'
foreach ($row in $results) {
    $lines += "| $($row.Model) | $($row.Threads) | $($row.File) | $($row.Run) | $($row.AudioMs) | $($row.LoadMs) | $($row.InferenceMs) | $($row.RTF) |"
}

[System.IO.File]::WriteAllLines($ReportPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ''
Write-Host "Rapport : $ReportPath" -ForegroundColor Cyan
