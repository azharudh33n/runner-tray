# Renders the brand SVGs (assets/brand) to PNG and packs the Windows icon and tray icons.
#   pwsh tools/brand/build-brand.ps1   # first: writes the SVGs
#   pwsh tools/make-icon.ps1           # then: PNGs, app.ico, tray icons
# Needs Python 3 and a renderer the logo-design skill's render_png.py can use (Chrome, Inkscape or rsvg).
# Set LOGO_SKILL to the skill's scripts folder if it is not in ../_external/logo-design-skill.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$brand = Join-Path $root 'assets\brand'
$skill = $env:LOGO_SKILL
if (-not $skill) { $skill = Join-Path $root '..\_external\logo-design-skill\skills\logo-design\scripts' }
$render = Join-Path $skill 'render_png.py'
if (-not (Test-Path $render)) { throw "render_png.py not found. Set LOGO_SKILL to the logo-design skill's scripts folder." }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('rt-icons-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null

function Render([string]$svg, [int]$size, [string]$dest) {
    $dir = Join-Path $tmp $size
    New-Item -ItemType Directory -Force $dir | Out-Null
    & python -I $render (Join-Path $brand $svg) --out-dir $dir --size $size | Out-Null
    Copy-Item (Join-Path $dir ([IO.Path]::ChangeExtension($svg, '.png'))) $dest -Force
}

# Web and README images.
$png = Join-Path $root 'assets'
Render 'app-icon.svg' 1024 (Join-Path $png 'logo-1024.png')
Render 'app-icon.svg' 256 (Join-Path $png 'logo-256.png')
Render 'app-icon.svg' 512 (Join-Path $png 'logo-512.png')
Render 'app-icon.svg' 192 (Join-Path $png 'logo-192.png')
Render 'app-icon.svg' 180 (Join-Path $png 'apple-touch-icon.png')
Render 'app-icon-small.svg' 32 (Join-Path $png 'favicon-32.png')
Render 'app-icon-small.svg' 16 (Join-Path $png 'favicon-16.png')
Copy-Item (Join-Path $brand 'app-icon.svg') (Join-Path $png 'logo.svg') -Force
# Lockups for the README and images: 1200 px wide.
foreach ($l in 'lockup-horizontal', 'lockup-horizontal-reversed') {
    & python -I $render (Join-Path $brand "$l.svg") --out-dir $png --width 1200 | Out-Null
}

# Windows icon: small cut for 16-32 px, full mark from 40 px.
function Pack([string[]]$files, [int[]]$sizes, [string]$dest) {
    $pngs = $files | ForEach-Object { , [IO.File]::ReadAllBytes($_) }
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = $(if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] })
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Flush()
    [IO.File]::WriteAllBytes($dest, $ms.ToArray())
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$files = foreach ($s in $sizes) {
    $f = Join-Path $tmp "app-$s.png"
    Render $(if ($s -le 32) { 'app-icon-small.svg' } else { 'app-icon.svg' }) $s $f
    $f
}
Pack $files $sizes (Join-Path $root 'assets\app.ico')
Copy-Item (Join-Path $root 'assets\app.ico') (Join-Path $root 'site\favicon.ico') -Force

# Tray icons: one PNG per status, taskbar theme and size, embedded in the app.
$trayDir = Join-Path $root 'src\RunnerTray\TrayIcons'
New-Item -ItemType Directory -Force $trayDir | Out-Null
foreach ($state in 'pass', 'run', 'fail', 'error', 'idle') {
    foreach ($theme in 'dark', 'light') {
        foreach ($s in 16, 20, 24, 32, 40, 48) {
            Render "tray-$state-$theme.svg" $s (Join-Path $trayDir "$state-$theme-$s.png")
        }
    }
}

# Brand mark for the panel header (reversed small cut on the dark panel).
foreach ($s in 20, 24, 32, 40) { Render 'symbol-small-reversed.svg' $s (Join-Path $trayDir "brand-$s.png") }

# Installer splash (Velopack): horizontal lockup on Ink.
Render 'lockup-horizontal-reversed.svg' 600 (Join-Path $tmp 'lockup.png')
Add-Type -AssemblyName System.Drawing
$lock = [Drawing.Image]::FromFile((Join-Path $tmp 'lockup.png'))
$splash = New-Object Drawing.Bitmap 720, 300
$g = [Drawing.Graphics]::FromImage($splash)
$g.Clear([Drawing.Color]::FromArgb(14, 21, 37))
$g.InterpolationMode = 'HighQualityBicubic'
$h = [int](480 * $lock.Height / $lock.Width)
$g.DrawImage($lock, 120, [int]((300 - $h) / 2), 480, $h)
$g.Dispose(); $lock.Dispose()
$splash.Save((Join-Path $root 'assets\splash.png'), [Drawing.Imaging.ImageFormat]::Png)
$splash.Dispose()

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Wrote assets/app.ico, logo PNGs, favicons, splash.png, and src/RunnerTray/TrayIcons/*.png'
