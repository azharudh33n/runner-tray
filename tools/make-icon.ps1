# Draws the RunnerTray logo and writes:
#   assets/app.ico        16, 20, 24, 32, 40, 48, 64, 128, 256 px (Windows app icon)
#   assets/logo-1024.png  1024 px (web, social preview)
#   assets/logo-256.png   256 px (README)
# Same design as assets/logo.svg: blue tile, white run symbol in a progress ring, green status dot.
# Small sizes (32 px and below) drop the ring so the shape stays clear.
# Run from the repository root:  pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $s = [double]$size
    $white = [System.Drawing.Color]::White
    $green = [System.Drawing.Color]::FromArgb(63, 185, 80)
    $dark = [System.Drawing.Color]::FromArgb(11, 61, 145)

    # Tile with rounded corners and a diagonal gradient.
    $r = $s * 0.22
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $tile.AddArc($s - 2 * $r - 1, 0, 2 * $r, 2 * $r, 270, 90)
    $tile.AddArc($s - 2 * $r - 1, $s - 2 * $r - 1, 2 * $r, 2 * $r, 0, 90)
    $tile.AddArc(0, $s - 2 * $r - 1, 2 * $r, 2 * $r, 90, 90)
    $tile.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, $s, $s), ([System.Drawing.Color]::FromArgb(56, 139, 253)), $dark, 45
    $g.FillPath($grad, $tile)

    $small = $size -le 32
    if (-not $small) {
        # Progress ring: 300 degrees, gap at the top right.
        $ringR = $s * 0.30
        $pen = New-Object System.Drawing.Pen $white, ([single]($s * 0.075))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawArc($pen, [single]($s / 2 - $ringR), [single]($s / 2 - $ringR), [single](2 * $ringR), [single](2 * $ringR), -30, 300)
    }

    # Run (play) symbol with rounded corners.
    if ($small) { $pts = @(@(0.36, 0.25), @(0.80, 0.50), @(0.36, 0.75)) } else { $pts = @(@(0.43, 0.36), @(0.67, 0.50), @(0.43, 0.64)) }
    $tri = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tri.AddPolygon([System.Drawing.PointF[]]($pts | ForEach-Object { New-Object System.Drawing.PointF ([single]($_[0] * $s)), ([single]($_[1] * $s)) }))
    $g.FillPath((New-Object System.Drawing.SolidBrush $white), $tri)
    $round = New-Object System.Drawing.Pen $white, ([single]([Math]::Max(1, $s * ($(if ($small) { 0.06 } else { 0.04 })))))
    $round.LineJoin = 'Round'
    $g.DrawPath($round, $tri)

    # Status dot with a dark edge, in the ring's gap (top right).
    if ($small) { $cx = $s * 0.76; $cy = $s * 0.24; $dr = $s * 0.17 } else { $cx = $s * 0.65; $cy = $s * 0.24; $dr = $s * 0.072 }
    $edge = $dr * 1.3
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $dark), [single]($cx - $edge), [single]($cy - $edge), [single](2 * $edge), [single](2 * $edge))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $green), [single]($cx - $dr), [single]($cy - $dr), [single](2 * $dr), [single](2 * $dr))

    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$assets = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force $assets | Out-Null

foreach ($size in 1024, 256) {
    $bmp = New-Logo $size
    $bmp.Save((Join-Path $assets "logo-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# ICO with PNG-compressed images.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { $b = New-Logo $s; , (Get-PngBytes $b); $b.Dispose() }
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = $(if ($s -ge 256) { 0 } else { $s })
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())
Write-Host "Wrote app.ico, logo-1024.png, logo-256.png in $assets"
