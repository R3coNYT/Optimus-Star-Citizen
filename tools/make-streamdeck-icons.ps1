<#
.SYNOPSIS
    Fabrique les icones du plugin Stream Deck a partir du logo.

.DESCRIPTION
    Le Stream Deck reclame trois familles de tailles, et il n'accepte pas qu'on
    lui en donne une seule en esperant qu'il redimensionne :

        categorie      28 px  (+ 56 en @2x)   la ligne dans la liste des plugins
        action         20 px  (+ 40 en @2x)   la vignette de chaque action
        touche         72 px  (+144 en @2x)   ce que le pilote voit sur le boitier

    Chaque touche existe en trois etats, parce qu'un bouton qui ne dit pas dans
    quel etat il est ne vaut pas mieux qu'un raccourci clavier :

        allume         le logo tel quel
        eteint         attenue et desature - lisible, mais manifestement inactif
        alerte         vire au rouge, pour l'arret d'urgence engage

    L'attenuation passe par une ColorMatrix et non par une opacite globale : sur
    le fond noir d'un Stream Deck, baisser l'alpha donne un gris sale, tandis
    que baisser les canaux garde le dessin net.

.EXAMPLE
    .\make-streamdeck-icons.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$source   = Join-Path $repoRoot 'images\Optimus.png'
$target   = Join-Path $repoRoot 'tools\streamdeck\com.optimus.copilot.sdPlugin\icons'

if (-not (Test-Path -LiteralPath $source)) {
    throw "Logo introuvable : $source"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

$logo = [System.Drawing.Image]::FromFile($source)

function New-Icon {
    param(
        [Parameter(Mandatory)] [int]    $Size,
        [Parameter(Mandatory)] [string] $Name,
        [float[][]] $Matrix
    )

    $bitmap   = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    $graphics.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)

    if ($null -eq $Matrix) {
        $graphics.DrawImage($logo, $rect)
    }
    else {
        $colors = New-Object System.Drawing.Imaging.ColorMatrix
        $colors.Matrix00 = $Matrix[0][0]; $colors.Matrix01 = $Matrix[0][1]; $colors.Matrix02 = $Matrix[0][2]
        $colors.Matrix10 = $Matrix[1][0]; $colors.Matrix11 = $Matrix[1][1]; $colors.Matrix12 = $Matrix[1][2]
        $colors.Matrix20 = $Matrix[2][0]; $colors.Matrix21 = $Matrix[2][1]; $colors.Matrix22 = $Matrix[2][2]

        $attributes = New-Object System.Drawing.Imaging.ImageAttributes
        $attributes.SetColorMatrix($colors)

        $graphics.DrawImage(
            $logo, $rect, 0, 0, $logo.Width, $logo.Height,
            [System.Drawing.GraphicsUnit]::Pixel, $attributes)

        $attributes.Dispose()
    }

    $path = Join-Path $target $Name
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)

    $graphics.Dispose()
    $bitmap.Dispose()

    Write-Host ("    {0,-22} {1} px" -f $Name, $Size) -ForegroundColor DarkGray
}

# Attenuation : les trois canaux ramenes a 40 %, plus un leger melange vers le gris.
# Le dessin reste net, mais aucun pilote ne le confondra avec un bouton actif.
$dim = @(
    @(0.32, 0.14, 0.14),
    @(0.14, 0.32, 0.14),
    @(0.14, 0.14, 0.32)
)

# Alerte : tout le signal verse dans le rouge. L'arret d'urgence n'a pas a etre joli.
$alert = @(
    @(0.90, 0.05, 0.05),
    @(0.55, 0.08, 0.08),
    @(0.55, 0.08, 0.08)
)

# Chaque nom sort du manifeste : une image manquante ne fait pas echouer le
# chargement du plugin, elle donne une touche noire, ce qui se cherche longtemps.
# Ces fichiers sont des SUPPLEANTS - ils tiennent la place jusqu'a ce que le
# pilote pose les siens, au meme nom.

Write-Host '==> Categorie  (28 / 56)' -ForegroundColor Cyan
New-Icon -Size 28  -Name 'category.png'
New-Icon -Size 56  -Name 'category@2x.png'

Write-Host '==> Vignettes des actions  (20 / 40)' -ForegroundColor Cyan
foreach ($name in @('action-mic', 'action-stop', 'action-sim', 'action-command', 'action-speak')) {
    New-Icon -Size 20 -Name "$name.png"
    New-Icon -Size 40 -Name "$name@2x.png"
}

Write-Host '==> Touches  (72 / 144)' -ForegroundColor Cyan

# Les etats « allume » gardent le logo entier ; les « eteint » sont attenues.
foreach ($name in @('mic-on', 'sim-on', 'command-on', 'speak')) {
    New-Icon -Size 72  -Name "$name.png"
    New-Icon -Size 144 -Name "$name@2x.png"
}

foreach ($name in @('mic-off', 'stop-off', 'sim-off', 'command-off')) {
    New-Icon -Size 72  -Name "$name.png"     -Matrix $dim
    New-Icon -Size 144 -Name "$name@2x.png"  -Matrix $dim
}

# L'arret d'urgence engage est le seul rouge : c'est le seul etat qu'on doit
# reconnaitre du coin de l'oeil, sans lire.
New-Icon -Size 72  -Name 'stop-on.png'    -Matrix $alert
New-Icon -Size 144 -Name 'stop-on@2x.png' -Matrix $alert

$logo.Dispose()

Write-Host ''
Write-Host "  Ecrit dans $target" -ForegroundColor Green
