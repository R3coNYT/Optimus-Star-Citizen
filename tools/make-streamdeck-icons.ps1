<#
.SYNOPSIS
    Fabrique les icones du plugin Stream Deck a partir du logo.

.DESCRIPTION
    Fabrique les DEUX petites familles d'images, celles qui ne se voient jamais
    sur le boitier :

        categorie      28 px  (+ 56 en @2x)   la ligne dans la liste des plugins
        action         20 px  (+ 40 en @2x)   la vignette de chaque action

    Les images des TOUCHES ne sont plus fabriquees ici. Elles sont dessinees a la
    main, et decoupees de la planche par slice-streamdeck-sheet.ps1. Ce script les
    produisait autrefois en supplement ; il les ecraserait aujourd'hui, ce qui est
    exactement le genre de perte qu'on ne remarque qu'une fois le plugin recharge.

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

Write-Host '==> Categorie  (28 / 56)' -ForegroundColor Cyan
New-Icon -Size 28  -Name 'category.png'
New-Icon -Size 56  -Name 'category@2x.png'

Write-Host '==> Vignettes des actions  (20 / 40)' -ForegroundColor Cyan
foreach ($name in @('action-mic', 'action-stop', 'action-sim', 'action-command', 'action-speak')) {
    New-Icon -Size 20 -Name "$name.png"
    New-Icon -Size 40 -Name "$name@2x.png"
}

$logo.Dispose()

Write-Host ''
Write-Host "  Ecrit dans $target" -ForegroundColor Green
