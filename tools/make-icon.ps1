# Creates assets/app.ico (16, 32, 48, 256 px): a blue dot with a soft ring,
# the same design as the tray icon. Run from the repository root:
#   pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 32, 48, 256
$color = [System.Drawing.Color]::FromArgb(88, 166, 255)
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, $color))), 1, 1, $s - 3, $s - 3)
    $pad = $s * 0.2
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $color), $pad, $pad, $s - 2 * $pad - 1, $s - 2 * $pad - 1)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()

$path = Join-Path $PSScriptRoot '..\assets\app.ico'
New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
[System.IO.File]::WriteAllBytes($path, $out.ToArray())
Write-Host "Wrote $path"
