# Draws the app icon (dark bronze tile, gold frame, mini server list; "TQ2" at 48px and up)
# at the standard Windows sizes and packs them into icon.ico (PNG-compressed entries).
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot) 'icon.ico'

function Draw-Icon([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $gold = [System.Drawing.Color]::FromArgb(214, 170, 82)

    # Rounded dark tile with a gold frame
    $r = [Math]::Max(2, [int]($S * 0.16)); $inset = [Math]::Max(0.5, $S * 0.03)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = $S - 2 * $inset
    $path.AddArc($inset, $inset, $r, $r, 180, 90); $path.AddArc($inset + $w - $r, $inset, $r, $r, 270, 90)
    $path.AddArc($inset + $w - $r, $inset + $w - $r, $r, $r, 0, 90); $path.AddArc($inset, $inset + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 0, 0, $S, $S), ([System.Drawing.Color]::FromArgb(44, 34, 24)), ([System.Drawing.Color]::FromArgb(16, 13, 10)), 90
    $g.FillPath($bg, $path)
    $g.DrawPath((New-Object System.Drawing.Pen $gold, ([Math]::Max(1, $S * 0.045))), $path)

    # "TQ2" on top when there's room, then three lobby rows: green dot, name bar, player-slot pips
    $rowsTop = $S * 0.24; $rowsH = $S * 0.56
    if ($S -ge 48) {
        $font = New-Object System.Drawing.Font 'Georgia', ([float]($S * 0.24)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'
        $g.DrawString('TQ2', $font, (New-Object System.Drawing.SolidBrush $gold), (New-Object System.Drawing.RectangleF 0, ($S * 0.08), $S, ($S * 0.32)), $fmt)
        $rowsTop = $S * 0.42; $rowsH = $S * 0.46
    }
    $row = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 58, 42))
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 205, 110))
    $bar = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(215, 205, 185))
    $pipOn = New-Object System.Drawing.SolidBrush $gold
    $pipOff = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(100, 86, 64))
    $left = $S * 0.17; $right = $S * 0.83; $rh = $rowsH / 3
    for ($i = 0; $i -lt 3; $i++) {
        $y = $rowsTop + $i * $rh; $h = $rh * 0.72
        $g.FillRectangle($row, $left, $y, $right - $left, $h)
        $d = $h * 0.5; $g.FillEllipse($green, $left + $h * 0.25, $y + ($h - $d) / 2, $d, $d)
        if ($S -ge 32) {
            $g.FillRectangle($bar, $left + $h * 1.1, $y + $h * 0.4, ($right - $left) * (0.38 - $i * 0.07), [Math]::Max(1, $h * 0.22))
            $pw = ($right - $left) * 0.07
            for ($p = 0; $p -lt 4; $p++) {
                $b = if ($p -lt (3 - $i)) { $pipOn } else { $pipOff }
                $g.FillRectangle($b, $right - ($h * 0.25) - (4 - $p) * $pw * 1.35, $y + $h * 0.25, $pw, $h * 0.5)
            }
        } else {
            $g.FillRectangle($pipOn, $left + $h * 1.1, $y + $h * 0.3, ($right - $left) * 0.55, [Math]::Max(1, $h * 0.4))
        }
    }
    $g.Dispose()
    return $bmp
}

# Small sizes as 32-bit bitmaps (DIB: header, bottom-up BGRA pixels, empty AND mask), 256px as PNG.
function Get-IconImage([int]$S) {
    $bmp = Draw-Icon $S
    $ms = New-Object System.IO.MemoryStream
    if ($S -ge 256) { $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); return , $ms.ToArray() }
    $w = New-Object System.IO.BinaryWriter $ms
    $maskRow = [int][Math]::Ceiling($S / 32) * 4
    $w.Write([UInt32]40); $w.Write([Int32]$S); $w.Write([Int32]($S * 2)); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]0); $w.Write([UInt32]($S * $S * 4 + $maskRow * $S)); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $S - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $S; $x++) { $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) }
    }
    $w.Write((New-Object byte[] ($maskRow * $S)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 32, 48, 256
$pngs = foreach ($s in $sizes) { , (Get-IconImage $s) }

# ICO file: ICONDIR, one ICONDIRENTRY per image, then the image data
$fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"Wrote $out"
