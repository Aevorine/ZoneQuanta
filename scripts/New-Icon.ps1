param(
    [string]$Out = (Join-Path $PSScriptRoot '..\src\ZoneQuanta\Assets\app.ico'),
    [string]$Preview = ''
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function C([string]$hex, [int]$alpha = 255) {
    [System.Drawing.Color]::FromArgb($alpha, [Convert]::ToInt32($hex.Substring(0, 2), 16), [Convert]::ToInt32($hex.Substring(2, 2), 16), [Convert]::ToInt32($hex.Substring(4, 2), 16))
}

function Round-Rect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Master([int]$px, [bool]$badge) {
    $bmp = New-Object System.Drawing.Bitmap $px, $px, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [double]$px

    $tile = Round-Rect 0 0 $s $s ($s * 0.22)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $s), (C '2A2E38'), (C '17191E')
    $g.FillPath($bg, $tile)
    $edge = New-Object System.Drawing.Pen (C 'FFFFFF' 28), ([single]($s * 0.012))
    $g.DrawPath($edge, (Round-Rect ($s * 0.006) ($s * 0.006) ($s * 0.988) ($s * 0.988) ($s * 0.215)))

    if ($badge) { $cx = $s * 0.45; $cy = $s * 0.45; $r = $s * 0.275 }
    else { $cx = $s * 0.5; $cy = $s * 0.5; $r = $s * 0.31 }
    $w = $s * ($(if ($badge) { 0.105 } else { 0.125 }))
    $box = New-Object System.Drawing.RectangleF ([single]($cx - $r)), ([single]($cy - $r)), ([single](2 * $r)), ([single](2 * $r))

    $night = New-Object System.Drawing.Pen (C '4F8F86' 235), ([single]$w)
    $g.DrawEllipse($night, $box)

    $day = New-Object System.Drawing.Pen (C 'EBB46E'), ([single]$w)
    $day.StartCap = 'Round'; $day.EndCap = 'Round'
    $g.DrawArc($day, $box, 193.5, 174)

    $tick = New-Object System.Drawing.Pen (C 'FFFFFF' 70), ([single]($s * 0.012))
    foreach ($h in 0, 6, 12, 18) {
        $phi = ($h - 12) / 24.0 * 2 * [Math]::PI
        $r1 = $r - $w * 0.9; $r2 = $r - $w * 1.9
        $g.DrawLine($tick, [single]($cx + $r1 * [Math]::Sin($phi)), [single]($cy - $r1 * [Math]::Cos($phi)), [single]($cx + $r2 * [Math]::Sin($phi)), [single]($cy - $r2 * [Math]::Cos($phi)))
    }

    $phiNow = (15.0 - 12) / 24.0 * 2 * [Math]::PI
    $hx = $cx + ($r - $w * 1.3) * [Math]::Sin($phiNow); $hy = $cy - ($r - $w * 1.3) * [Math]::Cos($phiNow)
    $hand = New-Object System.Drawing.Pen (C 'EEF1F6'), ([single]($s * 0.048))
    $hand.StartCap = 'Round'; $hand.EndCap = 'Round'
    $g.DrawLine($hand, [single]$cx, [single]$cy, [single]$hx, [single]$hy)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C 'EEF1F6')), [single]($cx - $s * 0.03), [single]($cy - $s * 0.03), [single]($s * 0.06), [single]($s * 0.06))

    $mx = $cx + $r * [Math]::Sin($phiNow); $my = $cy - $r * [Math]::Cos($phiNow)
    $gl = $s * 0.115
    $glow = New-Object System.Drawing.SolidBrush (C 'FFD27A' 90)
    $g.FillEllipse($glow, [single]($mx - $gl), [single]($my - $gl), [single](2 * $gl), [single](2 * $gl))
    $sr = $s * 0.068
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C 'FFE3A3')), [single]($mx - $sr), [single]($my - $sr), [single](2 * $sr), [single](2 * $sr))
    $sc = $s * 0.04
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C 'FFB74D')), [single]($mx - $sc), [single]($my - $sc), [single](2 * $sc), [single](2 * $sc))

    if ($badge) {
        $bw = $s * 0.062; $gap = $s * 0.03; $bottom = $s * 0.865; $left = $s * 0.655
        $heights = 0.095, 0.165, 0.245
        $colors = '6FB7AC', 'E2AE74', '6FB7AC'
        for ($i = 0; $i -lt 3; $i++) {
            $h = $s * $heights[$i]
            $bar = Round-Rect ($left + $i * ($bw + $gap)) ($bottom - $h) $bw $h ($bw * 0.35)
            $g.FillPath((New-Object System.Drawing.SolidBrush (C $colors[$i])), $bar)
        }
    }
    $g.Dispose()
    return $bmp
}

function New-Frame([int]$size) {
    $ss = 4
    $big = Draw-Master ($size * $ss) ($size -ge 32)
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($big, 0, 0, $size, $size)
    $g.Dispose(); $big.Dispose()
    if ($Preview -and $size -eq 256) { $bmp.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png) }
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
