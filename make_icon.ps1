# Convertit assets\icon.png en assets\app.ico multi-tailles (16 -> 256 px) avec System.Drawing (natif Windows)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcPng = Join-Path $projectDir "assets\icon_source.jpg"
$outIco = Join-Path $projectDir "assets\app.ico"

if (-not (Test-Path $srcPng)) { Write-Error "Image source introuvable : $srcPng"; exit 1 }

# --- Étape 1 : recadrer sur le carré arrondi et rendre les coins transparents ---
$raw = New-Object System.Drawing.Bitmap $srcPng
function Test-Dark($c) { return ($c.R -lt 200 -or $c.G -lt 200 -or $c.B -lt 200) }
$midY = [int]($raw.Height / 2); $midX = [int]($raw.Width / 2)
$left = 0;  while ($left -lt $raw.Width -and -not (Test-Dark $raw.GetPixel($left, $midY))) { $left++ }
$right = $raw.Width - 1; while ($right -gt 0 -and -not (Test-Dark $raw.GetPixel($right, $midY))) { $right-- }
$top = 0;   while ($top -lt $raw.Height -and -not (Test-Dark $raw.GetPixel($midX, $top))) { $top++ }
$inset = 6
$side = [Math]::Min($right - $left, $raw.Width) - 2 * $inset
$cropX = $left + $inset; $cropY = $top + $inset

$clean = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($clean)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)
$brush = New-Object System.Drawing.TextureBrush $raw
$brush.TranslateTransform(-$cropX, -$cropY)
$r = [int]($side * 0.22); $d = 2 * $r
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc(0, 0, $d, $d, 180, 90)
$path.AddArc($side - $d - 1, 0, $d, $d, 270, 90)
$path.AddArc($side - $d - 1, $side - $d - 1, $d, $d, 0, 90)
$path.AddArc(0, $side - $d - 1, $d, $d, 90, 90)
$path.CloseFigure()
$g.FillPath($brush, $path)
$g.Dispose(); $brush.Dispose(); $path.Dispose(); $raw.Dispose()
$cleanPng = Join-Path $projectDir "assets\icon.png"
$clean.Save($cleanPng, [System.Drawing.Imaging.ImageFormat]::Png)
$clean.Dispose()
$srcPng = $cleanPng

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$src = [System.Drawing.Image]::FromFile($srcPng)
$pngBlobs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngBlobs += ,($ms.ToArray())
    $ms.Dispose()
}
$src.Dispose()

$fs = [System.IO.File]::Create($outIco)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngBlobs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($blob in $pngBlobs) { $bw.Write($blob) }
$bw.Close(); $fs.Close()
Write-Host "Icône générée : $outIco" -ForegroundColor Green
