<#
.SYNOPSIS
    Decoupe la planche des neuf touches Stream Deck en fichiers distincts.

.DESCRIPTION
    La planche est une grille 3 x 3 sur fond noir, mais pas une grille reguliere :
    les tuiles n'ont ni la meme taille ni le meme espacement. Le script les
    trouve, puis leur impose une decoupe commune.

    COMMENT ELLES SONT TROUVEES. Non par une moyenne de luminosite - le rouge de
    « stop on » est trois fois plus lumineux que le reste, et un seuil relatif au
    maximum rejetait alors les tuiles bleues. Mais en COMPTANT les pixels clairs
    de chaque ligne et de chaque colonne : une gouttiere n'en a aucun, une tuile
    en a des centaines, et un pic isole ne change rien a ce compte.

    COMMENT ELLES SONT DECOUPEES. Chaque tuile est centree sur elle-meme, et
    toutes recoivent le MEME cote - le plus grand mesure, plus une marge. Sans
    cela les neuf touches auraient neuf echelles differentes, ce qui se voit
    immediatement quand elles sont posees cote a cote sur le boitier.

    Le cote est plafonne a l'ecart entre deux centres voisins : une tuile ne doit
    jamais mordre sur celle d'a cote.

.PARAMETER Sheet
    La planche a decouper.

.PARAMETER Preview
    N'ecrit rien : affiche les decoupes trouvees. A lancer d'abord.

.EXAMPLE
    .\slice-streamdeck-sheet.ps1 -Preview
    .\slice-streamdeck-sheet.ps1
#>

[CmdletBinding()]
param(
    [string] $Sheet,
    [switch] $Preview
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Sheet) {
    $Sheet = Join-Path $repoRoot 'images\streamdeck\Optimus_Stream_Deck_Buttons.png'
}

$target = Join-Path $repoRoot 'tools\streamdeck\com.optimus.copilot.sdPlugin\icons'

if (-not (Test-Path -LiteralPath $Sheet)) {
    throw "Planche introuvable : $Sheet"
}

# L'ordre de lecture de la planche, et rien d'autre. Si le dessin change d'ordre,
# c'est ici que ca se corrige - pas dans le manifeste.
$names = @(
    'mic-off',     'mic-on',     'stop-off',
    'stop-on',     'sim-off',    'sim-on',
    'command-off', 'command-on', 'speak'
)

$sheetImage = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Sheet).Path)
$bitmap = New-Object System.Drawing.Bitmap($sheetImage)
$sheetImage.Dispose()

$width  = $bitmap.Width
$height = $bitmap.Height

Write-Host "==> Planche : $width x $height" -ForegroundColor Cyan

# --------------------------------------------------------------- compter les pixels clairs
#
# LockBits plutot que GetPixel : sur 1254 x 1254, la difference est de dix
# secondes a une demie.

$rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
$data = $bitmap.LockBits(
    $rect,
    [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$stride = $data.Stride
$buffer = New-Object byte[] ($stride * $height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $buffer.Length)
$bitmap.UnlockBits($data)

$rowLit = New-Object int[] $height
$colLit = New-Object int[] $width

for ($y = 0; $y -lt $height; $y++) {
    $line = $y * $stride

    for ($x = 0; $x -lt $width; $x++) {
        $i = $line + $x * 4

        # BGRA. 48 sur 255 : au-dessus du fond et de son grain, en dessous du
        # trait le plus terne de la tuile la plus sombre.
        if ((($buffer[$i] + $buffer[$i + 1] + $buffer[$i + 2]) / 3) -gt 48) {
            $rowLit[$y]++
            $colLit[$x]++
        }
    }
}

function Get-Bands {
    param([int[]] $Lit, [int] $Across, [int] $Minimum)

    # Deux pour cent de la traversee : une gouttiere en a zero, une tuile bien
    # davantage. Le plancher de trois evite qu'une planche etroite ne passe tout.
    $threshold = [Math]::Max(3, [int]($Across * 0.02))

    $bands = New-Object System.Collections.Generic.List[psobject]
    $start = -1

    # « $isLit » et non « $lit » : PowerShell ignore la casse des variables, donc
    # « $lit » ECRASERAIT le parametre « $Lit ». Le tableau devenait un booleen
    # des la premiere iteration, la boucle s'arretait, et la fonction ne rendait
    # jamais rien - sans la moindre erreur.
    for ($i = 0; $i -lt $Lit.Length; $i++) {
        $isLit = $Lit[$i] -gt $threshold

        if ($isLit -and $start -lt 0) {
            $start = $i
        }
        elseif (-not $isLit -and $start -ge 0) {
            if (($i - $start) -ge $Minimum) {
                $bands.Add([pscustomobject]@{ Start = $start; End = $i - 1 })
            }
            $start = -1
        }
    }

    if ($start -ge 0 -and ($Lit.Length - $start) -ge $Minimum) {
        $bands.Add([pscustomobject]@{ Start = $start; End = $Lit.Length - 1 })
    }

    return $bands
}

$rows = Get-Bands -Lit $rowLit -Across $width  -Minimum ([int]($height / 6))
$cols = Get-Bands -Lit $colLit -Across $height -Minimum ([int]($width / 6))

Write-Host "    lignes   : $($rows.Count)" -ForegroundColor DarkGray
foreach ($b in $rows) {
    Write-Host ("        {0,5} -> {1,-5} ({2} px)" -f $b.Start, $b.End, ($b.End - $b.Start + 1)) -ForegroundColor DarkGray
}

Write-Host "    colonnes : $($cols.Count)" -ForegroundColor DarkGray
foreach ($b in $cols) {
    Write-Host ("        {0,5} -> {1,-5} ({2} px)" -f $b.Start, $b.End, ($b.End - $b.Start + 1)) -ForegroundColor DarkGray
}

if ($rows.Count -ne 3 -or $cols.Count -ne 3) {
    throw "Grille 3 x 3 attendue, trouve $($cols.Count) x $($rows.Count)."
}

# ------------------------------------------------------- chaque tuile, une par une
#
# Les bandes disent OU sont les trois lignes et les trois colonnes, pas ou est
# chaque tuile. Sur cette planche les tuiles ne sont pas alignees entre elles :
# une bande commune par ligne ecrasait le decalage, et les tuiles du bas
# ressortaient rognees de leur cadre inferieur.
#
# On decoupe donc la planche en neuf CASES - les gouttieres, prises en leur
# milieu - puis on mesure dans chaque case l'etendue reelle de son dessin.

function Get-Edges {
    param([psobject[]] $Bands, [int] $Extent)

    # Les frontieres : le bord, les milieux de gouttieres, le bord.
    $edges = New-Object System.Collections.Generic.List[int]
    $edges.Add(0)

    for ($i = 0; $i -lt ($Bands.Count - 1); $i++) {
        $edges.Add([int](($Bands[$i].End + $Bands[$i + 1].Start) / 2))
    }

    $edges.Add($Extent)

    return $edges
}

$rowEdges = Get-Edges -Bands $rows -Extent $height
$colEdges = Get-Edges -Bands $cols -Extent $width

function Get-Box {
    param([int] $Left, [int] $Top, [int] $Right, [int] $Bottom)

    $minX = $Right; $maxX = $Left
    $minY = $Bottom; $maxY = $Top

    for ($y = $Top; $y -lt $Bottom; $y++) {
        $line = $y * $stride

        for ($x = $Left; $x -lt $Right; $x++) {
            $i = $line + $x * 4

            if ((($buffer[$i] + $buffer[$i + 1] + $buffer[$i + 2]) / 3) -gt 48) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }

    return [pscustomobject]@{
        X      = $minX
        Y      = $minY
        Width  = $maxX - $minX + 1
        Height = $maxY - $minY + 1
    }
}

Write-Host '    tuiles :' -ForegroundColor DarkGray

$boxes = New-Object System.Collections.Generic.List[psobject]

for ($r = 0; $r -lt 3; $r++) {
    for ($c = 0; $c -lt 3; $c++) {
        $box = Get-Box -Left $colEdges[$c] -Top $rowEdges[$r] `
                       -Right $colEdges[$c + 1] -Bottom $rowEdges[$r + 1]

        $boxes.Add($box)

        Write-Host ("        {0,-14} {1},{2}  {3} x {4}" -f `
            $names[$boxes.Count - 1], $box.X, $box.Y, $box.Width, $box.Height) -ForegroundColor DarkGray
    }
}

# Un cote pour toutes : la plus grande tuile, plus un souffle. Neuf echelles
# differentes se verraient au premier coup d'oeil sur le boitier.
$longest = 0
foreach ($b in $boxes) {
    $longest = [Math]::Max($longest, [Math]::Max($b.Width, $b.Height))
}

$side = [int]($longest * 1.04)

Write-Host ("    cote retenu : {0} px  (plus grande tuile {1})" -f $side, $longest) -ForegroundColor Cyan

if ($Preview) {
    Write-Host ''
    Write-Host '  Apercu seulement : rien n a ete ecrit.' -ForegroundColor Yellow
    $bitmap.Dispose()
    return
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

$index = 0

foreach ($box in $boxes) {
    if ($true) {
        $name = $names[$index]
        $index++

        # Centre sur le dessin lui-meme, et non sur la case : c'est le cadre
        # dessine qui doit tomber juste, pas la grille supposee.
        $cx = $box.X + [int]($box.Width / 2)
        $cy = $box.Y + [int]($box.Height / 2)

        $x = [Math]::Max(0, [Math]::Min($cx - [int]($side / 2), $width - $side))
        $y = [Math]::Max(0, [Math]::Min($cy - [int]($side / 2), $height - $side))

        $source = New-Object System.Drawing.Rectangle($x, $y, $side, $side)

        foreach ($size in @(72, 144)) {
            $out = New-Object System.Drawing.Bitmap($size, $size)
            $g = [System.Drawing.Graphics]::FromImage($out)

            $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

            $destination = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
            $g.DrawImage($bitmap, $destination, $source, [System.Drawing.GraphicsUnit]::Pixel)

            $suffix = ''
            if ($size -eq 144) { $suffix = '@2x' }

            $out.Save((Join-Path $target "$name$suffix.png"), [System.Drawing.Imaging.ImageFormat]::Png)

            $g.Dispose()
            $out.Dispose()
        }

        Write-Host ("    {0,-14} depuis {1},{2}" -f $name, $x, $y) -ForegroundColor Green
    }
}

$bitmap.Dispose()

Write-Host ''
Write-Host "  Neuf touches ecrites dans $target" -ForegroundColor Green
