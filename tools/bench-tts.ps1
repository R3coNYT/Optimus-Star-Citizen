<#
.SYNOPSIS
    Spike S0-5 - mesure la latence et la qualité des moteurs de synthèse vocale.

.DESCRIPTION
    Compare les moteurs candidats pour Optimus sur des répliques réelles du copilote :

      - SAPI5      (System.Speech)                 : présent partout, voix limitées
      - OneCore    (Windows.Media.SpeechSynthesis) : voix neurales Windows, dont des voix FR masculines
      - Piper      (optionnel, -PiperDir)          : TTS neural local, qualité supérieure

    Mesure, pour chaque voix : synthèse à froid (première utilisation, moteur non initialisé)
    puis à chaud, la durée audio produite et le facteur temps réel (RTF = temps de synthèse /
    durée audio). Un RTF de 0,1 signifie qu'une réplique de 2 s se synthétise en 200 ms.

    La distinction froid/chaud n'est pas cosmétique : elle décide si Optimus doit préchauffer
    le moteur au démarrage pour que la première réponse ne parte pas en retard.

.EXAMPLE
    .\bench-tts.ps1
    .\bench-tts.ps1 -Play -Repeat 5
    .\bench-tts.ps1 -PiperDir 'C:\outils\piper' -PiperModel 'fr_FR-siwis-medium.onnx'
#>
[CmdletBinding()]
param(
    [int] $Repeat = 3,
    [switch] $Play,
    [string] $PiperDir,
    [string] $PiperModel,
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param([string] $m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param([string] $m) Write-Host "    $m" -ForegroundColor Yellow }

# Répliques réelles du copilote, de longueurs variées (cf. docs/06 et docs/08).
$Phrases = @(
    'Reçu.',
    'Portes ouvertes, commandant.',
    'Calcul de trajectoire terminé. Accrochez-vous, commandant.',
    "Négatif. Aucun raccourci n'est configuré pour cette action.",
    'Tous les systèmes sont opérationnels. Réacteur nominal, boucliers à cent pour cent et aucune anomalie détectée.'
)

# --------------------------------------------------------- Utilitaires audio

function Get-WavDurationMs {
    param([byte[]] $Bytes)
    if ($Bytes.Length -lt 44) { return 0 }

    # Parcours des chunks RIFF pour lire le vrai format plutôt que de le supposer.
    $sampleRate = 16000; $channels = 1; $bits = 16; $dataSize = 0
    $offset = 12
    while ($offset + 8 -le $Bytes.Length) {
        $id = [System.Text.Encoding]::ASCII.GetString($Bytes, $offset, 4)
        $size = [BitConverter]::ToInt32($Bytes, $offset + 4)
        if ($id -eq 'fmt ') {
            $channels = [BitConverter]::ToInt16($Bytes, $offset + 10)
            $sampleRate = [BitConverter]::ToInt32($Bytes, $offset + 12)
            $bits = [BitConverter]::ToInt16($Bytes, $offset + 22)
        }
        elseif ($id -eq 'data') { $dataSize = $size; break }
        if ($size -le 0) { break }
        $offset += 8 + $size + ($size % 2)
    }
    if ($sampleRate -le 0 -or $channels -le 0 -or $bits -le 0) { return 0 }
    return [int](($dataSize / ($sampleRate * $channels * ($bits / 8))) * 1000)
}

# Await d'une IAsyncOperation WinRT depuis PowerShell 5.1.
Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction SilentlyContinue
$script:AsTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
        $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    })[0]

function Await-WinRT {
    param($Operation, [Type] $ResultType)
    $task = $script:AsTaskGeneric.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    $null = $task.Wait(30000)
    return $task.Result
}

$script:Results = @()

function Add-Result {
    param([string] $Engine, [string] $Voice, [string] $Phase,
          [double] $SynthMs, [int] $AudioMs)
    $script:Results += [pscustomobject]@{
        Engine = $Engine; Voice = $Voice; Phase = $Phase
        SynthMs = [math]::Round($SynthMs, 1)
        AudioMs = $AudioMs
        RTF = if ($AudioMs -gt 0) { [math]::Round($SynthMs / $AudioMs, 3) } else { $null }
    }
}

# ------------------------------------------------------------------- SAPI 5

Write-Step 'Moteur SAPI5 (System.Speech)'
Add-Type -AssemblyName System.Speech
$sapi = New-Object System.Speech.Synthesis.SpeechSynthesizer
$sapiVoices = @($sapi.GetInstalledVoices() |
    Where-Object { $_.VoiceInfo.Culture.Name -like 'fr*' } |
    ForEach-Object { $_.VoiceInfo.Name })

if (-not $sapiVoices) {
    Write-Warn 'Aucune voix française SAPI5 installée.'
}
foreach ($voice in $sapiVoices) {
    $sapi.SelectVoice($voice)
    $cold = $true
    foreach ($i in 1..$Repeat) {
        foreach ($phrase in $Phrases) {
            $stream = New-Object System.IO.MemoryStream
            $sapi.SetOutputToWaveStream($stream)
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $sapi.Speak($phrase)
            $sw.Stop()
            $bytes = $stream.ToArray()
            $stream.Dispose()
            Add-Result -Engine 'SAPI5' -Voice $voice -Phase $(if ($cold) { 'froid' } else { 'chaud' }) `
                       -SynthMs $sw.Elapsed.TotalMilliseconds -AudioMs (Get-WavDurationMs -Bytes $bytes)
            $cold = $false
        }
    }
    Write-Ok "$voice : $($Phrases.Count * $Repeat) synthèses"
}
$sapi.Dispose()

# ----------------------------------------------------------------- OneCore

Write-Step 'Moteur OneCore (Windows.Media.SpeechSynthesis)'
try {
    $null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media, ContentType = WindowsRuntime]
    $null = [Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType = WindowsRuntime]

    $oneCoreVoices = @([Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices |
        Where-Object { $_.Language -like 'fr*' })

    foreach ($voiceInfo in $oneCoreVoices) {
        $synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
        $synth.Voice = $voiceInfo
        $cold = $true
        foreach ($i in 1..$Repeat) {
            foreach ($phrase in $Phrases) {
                $sw = [System.Diagnostics.Stopwatch]::StartNew()
                $stream = Await-WinRT $synth.SynthesizeTextToStreamAsync($phrase) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
                $sw.Stop()

                $size = [int] $stream.Size
                $reader = New-Object Windows.Storage.Streams.DataReader($stream.GetInputStreamAt(0))
                $null = Await-WinRT $reader.LoadAsync($size) ([uint32])
                $buffer = New-Object byte[] $size
                $reader.ReadBytes($buffer)
                $reader.Dispose()

                Add-Result -Engine 'OneCore' -Voice $voiceInfo.DisplayName -Phase $(if ($cold) { 'froid' } else { 'chaud' }) `
                           -SynthMs $sw.Elapsed.TotalMilliseconds -AudioMs (Get-WavDurationMs -Bytes $buffer)
                $cold = $false
            }
        }
        Write-Ok "$($voiceInfo.DisplayName) ($($voiceInfo.Gender)) : $($Phrases.Count * $Repeat) synthèses"
        $synth.Dispose()
    }
}
catch {
    Write-Warn "OneCore indisponible : $($_.Exception.Message)"
}

# ------------------------------------------------------------------- Piper

if ($PiperDir -and -not (Test-Path -LiteralPath $PiperDir)) {
    # Voir bench-stt.ps1 : une recursion sur un chemin inexistant bloque ~60 s.
    Write-Warn "Dossier Piper introuvable : $PiperDir"
    $PiperDir = $null
}

if ($PiperDir) {
    Write-Step 'Moteur Piper'
    $piperExe = Get-ChildItem -Path $PiperDir -Filter 'piper*.exe' -Recurse -ErrorAction SilentlyContinue |
                Select-Object -First 1
    if (-not $piperExe) {
        Write-Warn "piper.exe introuvable dans $PiperDir"
    }
    else {
        $model = if ($PiperModel) { $PiperModel } else {
            (Get-ChildItem -Path $PiperDir -Filter '*.onnx' -Recurse -ErrorAction SilentlyContinue |
             Select-Object -First 1).FullName
        }
        if (-not $model -or -not (Test-Path -LiteralPath $model)) {
            Write-Warn 'Aucun modèle .onnx trouvé. Télécharge une voix française depuis les releases de Piper.'
        }
        else {
            Write-Ok "modèle : $model"
            $tmp = Join-Path $env:TEMP 'optimus-piper'
            if (-not (Test-Path $tmp)) { New-Item -ItemType Directory $tmp | Out-Null }
            $cold = $true
            foreach ($i in 1..$Repeat) {
                foreach ($phrase in $Phrases) {
                    $out = Join-Path $tmp 'out.wav'
                    $sw = [System.Diagnostics.Stopwatch]::StartNew()
                    $phrase | & $piperExe.FullName -m $model -f $out 2>&1 | Out-Null
                    $sw.Stop()
                    $audioMs = 0
                    if (Test-Path $out) { $audioMs = Get-WavDurationMs -Bytes ([System.IO.File]::ReadAllBytes($out)) }
                    Add-Result -Engine 'Piper' -Voice ([System.IO.Path]::GetFileNameWithoutExtension($model)) `
                               -Phase $(if ($cold) { 'froid' } else { 'chaud' }) `
                               -SynthMs $sw.Elapsed.TotalMilliseconds -AudioMs $audioMs
                    $cold = $false
                }
            }
            Write-Ok "Piper : $($Phrases.Count * $Repeat) synthèses"
        }
    }
}
else {
    Write-Warn 'Piper non testé (utilise -PiperDir pour l ajouter au comparatif).'
}

# ------------------------------------------------------------------ Synthèse

Write-Step 'Résultats'

$summary = $script:Results | Where-Object { $_.Phase -eq 'chaud' } | Group-Object Engine, Voice | ForEach-Object {
    $times = $_.Group | ForEach-Object { $_.SynthMs } | Sort-Object
    $rtfs = $_.Group | Where-Object { $_.RTF } | ForEach-Object { $_.RTF }
    [pscustomobject]@{
        Moteur = $_.Group[0].Engine
        Voix = $_.Group[0].Voice
        'Synthese p50 (ms)' = $times[[int]($times.Count / 2)]
        'Synthese max (ms)' = $times[-1]
        'RTF moyen' = if ($rtfs) { [math]::Round(($rtfs | Measure-Object -Average).Average, 3) } else { $null }
    }
}

$cold = $script:Results | Where-Object { $_.Phase -eq 'froid' } |
    Select-Object Engine, Voice, SynthMs, AudioMs

$summary | Format-Table -AutoSize | Out-Host
Write-Step 'Première synthèse (moteur à froid)'
$cold | Format-Table -AutoSize | Out-Host

if ($Play) {
    Write-Step 'Écoute comparative'
    $sapi2 = New-Object System.Speech.Synthesis.SpeechSynthesizer
    foreach ($voice in $sapiVoices) {
        Write-Ok "SAPI5 / $voice"
        $sapi2.SelectVoice($voice)
        $sapi2.Speak($Phrases[2])
    }
    $sapi2.Dispose()
    foreach ($voiceInfo in $oneCoreVoices) {
        Write-Ok "OneCore / $($voiceInfo.DisplayName)"
        $player = New-Object Windows.Media.Playback.MediaPlayer
        $synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
        $synth.Voice = $voiceInfo
        $stream = Await-WinRT $synth.SynthesizeTextToStreamAsync($Phrases[2]) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
        $player.Source = [Windows.Media.Core.MediaSource]::CreateFromStream($stream, $stream.ContentType)
        $player.Play()
        Start-Sleep -Seconds 5
        $synth.Dispose()
    }
}

# ------------------------------------------------------------------- Rapport

if (-not $ReportPath) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
    $dir = Join-Path $repoRoot 'docs\spikes'
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $ReportPath = Join-Path $dir ("S0-5-tts-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$env:COMPUTERNAME.md")
}

$lines = @()
$lines += "# Spike S0-5 - synthèse vocale"
$lines += ""
$lines += "*$env:COMPUTERNAME - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $($Phrases.Count) phrases x $Repeat répétitions*"
$lines += ""
$lines += "## Moteur à chaud"
$lines += ""
$lines += "| Moteur | Voix | Synthèse p50 (ms) | Synthèse max (ms) | RTF moyen |"
$lines += "|---|---|---|---|---|"
foreach ($row in $summary) {
    $lines += "| $($row.Moteur) | $($row.Voix) | $($row.'Synthese p50 (ms)') | $($row.'Synthese max (ms)') | $($row.'RTF moyen') |"
}
$lines += ""
$lines += "## Première synthèse (moteur à froid)"
$lines += ""
$lines += "| Moteur | Voix | Synthèse (ms) | Audio (ms) |"
$lines += "|---|---|---|---|"
foreach ($row in $cold) {
    $lines += "| $($row.Engine) | $($row.Voice) | $($row.SynthMs) | $($row.AudioMs) |"
}
$lines += ""
$lines += "## Détail"
$lines += ""
$lines += "| Moteur | Voix | Phase | Synthèse (ms) | Audio (ms) | RTF |"
$lines += "|---|---|---|---|---|---|"
foreach ($row in $script:Results) {
    $lines += "| $($row.Engine) | $($row.Voice) | $($row.Phase) | $($row.SynthMs) | $($row.AudioMs) | $($row.RTF) |"
}

[System.IO.File]::WriteAllLines($ReportPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ""
Write-Host "Rapport : $ReportPath" -ForegroundColor Cyan
