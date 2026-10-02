param([string]$Out = (Join-Path $PSScriptRoot '..\src\ZoneQuanta\Assets\app.ico'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [double]$size

    $r = $s * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1C, 0x1E, 0x23))), $path)

    $m = $s * 0.2
    $w = $s - 2 * $m
    $pen1 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x6F, 0xB7, 0xAC)), ([single]($s * 0.09))
    $pen2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0xE2, 0xAE, 0x74)), ([single]($s * 0.09))
    foreach ($p in @($pen1, $pen2)) { $p.StartCap = 'Round'; $p.EndCap = 'Round' }
    $g.DrawArc($pen1, $m, $m, $w, $w, 100, 170)
    $g.DrawArc($pen2, $m, $m, $w, $w, 280, 170)

    $hand = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0xD8, 0xDB, 0xE2)), ([single]($s * 0.065))
    $hand.StartCap = 'Round'; $hand.EndCap = 'Round'
    $c = $s / 2
    $g.DrawLine($hand, $c, $c, $c, $s * 0.32)
    $g.DrawLine($hand, $c, $c, $s * 0.64, $s * 0.58)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($sz in $sizes) { , (New-Frame $sz) }

New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$frames[$i].Length); $bw.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close(); $fs.Close()
Write-Output "OK $Out"
