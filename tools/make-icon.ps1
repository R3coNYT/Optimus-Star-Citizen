<#
.SYNOPSIS
    Fabrique l'icone de l'application et les images de l'assistant a partir du logo.

.DESCRIPTION
    Le logo est fourni en PNG carre de grande taille (images/Optimus.png). Windows, lui,
    reclame un .ico contenant PLUSIEURS resolutions : la barre des taches en prend une,
    l'explorateur une autre, le bureau une troisieme. Laisser Windows reduire une seule
    grande image donne un resultat trouble a 16 pixels, ou l'anticrenelage noie les traits
    fins du cadran.

    Chaque taille est donc reduite ici, une par une, en bicubique de qualite.

    Le format .ico melange deux encodages : un DIB brut jusqu'a 64 pixels, ce que lisent
    tous les Windows, et du PNG au-dela, pour ne pas peser 400 Ko a lui seul. Le tout est
    assemble a la main, System.Drawing ne sachant ecrire qu'une seule taille.

    Rejouer ce script apres avoir remplace le PNG suffit a rafraichir l'icone partout.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Les valeurs par defaut se calculent ici : $PSScriptRoot n'est pas encore peuple
# dans le bloc param() quand le script est lance avec -File.
$root = Split-Path -Parent $PSCommandPath
if (-not $Source)    { $Source = Join-Path $root '..\images\Optimus.png' }
if (-not $OutputDir) { $OutputDir = Join-Path $root '..\images' }

$source = (Resolve-Path $Source).Path
$outputDir = (Resolve-Path $OutputDir).Path
$logo = [Drawing.Bitmap]::FromFile($source)

function Resize([Drawing.Bitmap] $image, [int] $size) {
    $canvas = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($canvas)
    $g.CompositingMode    = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode  = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode      = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode    = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($image, (New-Object Drawing.Rectangle 0, 0, $size, $size))
    $g.Dispose()
    return $canvas
}

# Une entree .ico au format DIB. La virgule devant le retour empeche PowerShell
# de derouler le tableau d'octets en autant de valeurs separees.
function DibBytes([Drawing.Bitmap] $image, [int] $size) {
    $rect = New-Object Drawing.Rectangle 0, 0, $size, $size
    $data = $image.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadOnly,
                            [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object 'byte[]' ($data.Stride * $size)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $stride = $data.Stride
    $image.UnlockBits($data)

    $stream = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $stream

    # BITMAPINFOHEADER. La hauteur est doublee : le format compte l'image ET son masque.
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
    $w.Write([uint16]1);  $w.Write([uint16]32);   $w.Write([uint32]0)
    $w.Write([uint32]($size * $size * 4))
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)

    # Le DIB se lit du bas vers le haut, contrairement au bitmap en memoire.
    for ($y = $size - 1; $y -ge 0; $y--) {
        $w.Write($pixels, $y * $stride, $size * 4)
    }

    # Masque AND : la transparence est deja portee par la couche alpha, mais le format
    # exige le masque. Lignes alignees sur quatre octets.
    $maskRow = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $w.Write((New-Object 'byte[]' ([int]($maskRow * $size))))

    $w.Flush()
    return ,$stream.ToArray()
}

function PngBytes([Drawing.Bitmap] $image) {
    $stream = New-Object IO.MemoryStream
    $image.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    return ,$stream.ToArray()
}

# 16 a 64 pixels en DIB, 128 et 256 en PNG. Les tailles intermediaires — 20, 40 — sont
# celles que Windows reclame sur un ecran a 125 % ou 150 %.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$blobs = New-Object 'System.Collections.Generic.List[byte[]]'
$frames = @()

foreach ($size in $sizes) {
    $frame = Resize $logo $size
    if ($size -ge 128) { $blobs.Add((PngBytes $frame)) } else { $blobs.Add((DibBytes $frame $size)) }
    $frames += $frame
}

$stream = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $stream

$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count

for ($i = 0; $i -lt $sizes.Count; $i++) {
    # 256 s'ecrit zero : le champ ne fait qu'un octet.
    $dimension = [byte]$(if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] })
    $w.Write($dimension); $w.Write($dimension)
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$blobs[$i].Length)
    $w.Write([uint32]$offset)
    $offset += $blobs[$i].Length
}

foreach ($blob in $blobs) { $w.Write($blob, 0, $blob.Length) }
$w.Flush()

$ico = Join-Path $outputDir 'Optimus.ico'
[IO.File]::WriteAllBytes($ico, $stream.ToArray())
"icone : {0} tailles, {1:N0} octets" -f $sizes.Count, (Get-Item $ico).Length

# L'assistant d'installation, lui, ne lit que du BMP sans transparence : le logo est
# donc aplati sur le blanc de la page. Quatre tailles, qu'Inno choisit selon la densite
# de l'ecran.
foreach ($size in @(55, 83, 110, 138)) {
    $flat = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($flat)
    $g.Clear([Drawing.Color]::White)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($logo, (New-Object Drawing.Rectangle 0, 0, $size, $size))
    $g.Dispose()

    $suffix = switch ($size) { 55 { '' } 83 { '@1.5x' } 110 { '@2x' } 138 { '@2.5x' } }
    $flat.Save((Join-Path $outputDir "wizard-small$suffix.bmp"), [Drawing.Imaging.ImageFormat]::Bmp)
    $flat.Dispose()
}

"assistant : 55, 83, 110 et 138 pixels"

foreach ($frame in $frames) { $frame.Dispose() }
$logo.Dispose()
