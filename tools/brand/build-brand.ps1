# Generates every RunnerTray logo file from code, so the brand is reproducible.
#   pwsh tools/brand/build-brand.ps1
# Output: assets/brand/*.svg (symbol, small cut, tile, wordmark, lockups, tray status icons).
# The "Track" mark: a running track (the loop of continuous integration) with the runner,
# a light, breaking out on the bend. Geometry is computed exactly (arcs and their intersections).

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$out = Join-Path $PSScriptRoot '..\..\assets\brand'
New-Item -ItemType Directory -Force $out | Out-Null

# ---------- palette ----------
$Ink = '#0E1525'; $Orange = '#F26B3A'; $Paper = '#F7F6F2'; $White = '#FFFFFF'
$Status = [ordered]@{ pass = '#3FB950'; run = '#58A6FF'; fail = '#F85149'; error = '#D29922'; idle = '#6E7681' }

function F([double]$v) { [Math]::Round($v, 2).ToString($inv) }

# ---------- track geometry ----------
# Stadium with arc centres (cl, cy) and (cr, cy), outer radius R, inner radius r.
# The light sits on the track's centre line at angle $deg on the right bend, radius $lr,
# with a clear gap of radius $gr cut out of the track around it.
function Get-Track([double]$cl, [double]$cr, [double]$cy, [double]$Ro, [double]$Ri, [double]$deg, [double]$lr, [double]$gr) {
    $mid = ($Ro + $Ri) / 2
    $a = $deg * [Math]::PI / 180
    $px = $cr + $mid * [Math]::Cos($a); $py = $cy + $mid * [Math]::Sin($a)
    $d = $mid
    function Hit([double]$rad) {
        # Intersections of circle (cr,cy,rad) with the gap circle (px,py,gr): angles on the track circle.
        $aa = ($rad * $rad - $gr * $gr + $d * $d) / (2 * $d)
        $hh = [Math]::Sqrt([Math]::Max(0, $rad * $rad - $aa * $aa))
        $off = [Math]::Atan2($hh, $aa)
        return @(($a - $off), ($a + $off))
    }
    function Pt([double]$rad, [double]$ang, [double]$lineY) {
        # A point on the track edge: on the bend if the angle is within -90..90 degrees, else on the straight.
        if ($ang -lt -[Math]::PI / 2) {
            $x = $px - [Math]::Sqrt([Math]::Max(0, $gr * $gr - ($py - $lineY) * ($py - $lineY)))
            return @($x, $lineY)
        }
        return @(($cr + $rad * [Math]::Cos($ang)), ($cy + $rad * [Math]::Sin($ang)))
    }
    $ho = Hit $Ro; $hi = Hit $Ri
    $o1 = Pt $Ro $ho[0] ($cy - $Ro); $o2 = Pt $Ro $ho[1] 0
    $i1 = Pt $Ri $hi[0] ($cy - $Ri); $i2 = Pt $Ri $hi[1] 0
    $o1OnBend = $ho[0] -ge -[Math]::PI / 2
    $p = "M$(F $o1[0]) $(F $o1[1]) "
    if ($o1OnBend) { $p += "A$(F $Ro) $(F $Ro) 0 0 0 $(F $cr) $(F ($cy - $Ro)) " }
    $p += "H$(F $cl) A$(F $Ro) $(F $Ro) 0 0 0 $(F $cl) $(F ($cy + $Ro)) H$(F $cr) "
    $p += "A$(F $Ro) $(F $Ro) 0 0 0 $(F $o2[0]) $(F $o2[1]) "
    $p += "A$(F $gr) $(F $gr) 0 0 1 $(F $i2[0]) $(F $i2[1]) "
    $p += "A$(F $Ri) $(F $Ri) 0 0 1 $(F $cr) $(F ($cy + $Ri)) H$(F $cl) A$(F $Ri) $(F $Ri) 0 0 1 $(F $cl) $(F ($cy - $Ri)) H$(F $i1[0]) "
    $p += "A$(F $gr) $(F $gr) 0 0 1 $(F $o1[0]) $(F $o1[1]) Z"
    return @{ Path = $p; Lx = $px; Ly = $py; Lr = $lr }
}

# Master symbol on a 256 grid. Small cut on a 32 grid: thicker track, bigger light, wider gap.
$master = Get-Track 92 164 128 68 38 -45 26 42
$small = Get-Track 10 16 19.5 9.5 5 -45 7 8.6

function Symbol($t, [string]$track, [string]$light, [string]$extra = '') {
    "<path d=""$($t.Path)"" fill=""$track""/>`n  <circle cx=""$(F $t.Lx)"" cy=""$(F $t.Ly)"" r=""$(F $t.Lr)"" fill=""$light""/>$extra"
}

function Save([string]$name, [string]$viewBox, [string]$title, [string]$body) {
    $svg = "<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""$viewBox"" role=""img"">`n  <title>$title</title>`n  $body`n</svg>`n"
    [IO.File]::WriteAllText((Join-Path $out $name), $svg)
}

Save 'symbol-black.svg' '0 0 256 256' 'RunnerTray' (Symbol $master '#000000' '#000000')
Save 'symbol-white.svg' '0 0 256 256' 'RunnerTray' (Symbol $master $White $White)
Save 'symbol.svg' '0 0 256 256' 'RunnerTray' (Symbol $master $Ink $Orange)
Save 'symbol-reversed.svg' '0 0 256 256' 'RunnerTray' (Symbol $master $Paper $Orange)
Save 'symbol-small.svg' '0 0 32 32' 'RunnerTray' (Symbol $small $Ink $Orange)
Save 'symbol-small-reversed.svg' '0 0 32 32' 'RunnerTray' (Symbol $small $Paper $Orange)

# App icon: Ink tile, paper track, orange light. Large and small cuts.
$tileBig = "<rect width=""256"" height=""256"" rx=""56"" fill=""$Ink""/>`n  <g transform=""translate(25.6 25.6) scale(0.8)"">$(Symbol $master $Paper $Orange)</g>"
Save 'app-icon.svg' '0 0 256 256' 'RunnerTray' $tileBig
$tileSmall = "<rect width=""32"" height=""32"" rx=""7"" fill=""$Ink""/>`n  <g transform=""translate(1.6 1.6) scale(0.9)"">$(Symbol $small $Paper $Orange)</g>"
Save 'app-icon-small.svg' '0 0 32 32' 'RunnerTray' $tileSmall

# ---------- wordmark: "RunnerTray" in Bricolage Grotesque Bold, converted to outlines ----------
Add-Type -AssemblyName PresentationCore, WindowsBase
$family = [System.Windows.Media.Fonts]::SystemFontFamilies | Where-Object { $_.Source -like 'Bricolage Grotesque*' } | Select-Object -First 1
if (-not $family) { throw 'Bricolage Grotesque is not installed (OFL, fonts.google.com/specimen/Bricolage+Grotesque).' }
$typeface = New-Object System.Windows.Media.Typeface $family, ([System.Windows.FontStyles]::Normal), ([System.Windows.FontWeights]::Bold), ([System.Windows.FontStretches]::Normal)
$ft = New-Object System.Windows.Media.FormattedText 'RunnerTray', $inv, ([System.Windows.FlowDirection]::LeftToRight), $typeface, 100, ([System.Windows.Media.Brushes]::Black), 1.0
$geo = $ft.BuildGeometry((New-Object System.Windows.Point 0, 0))
$bounds = $geo.Bounds
$pg = [System.Windows.Media.PathGeometry]::CreateFromGeometry($geo)
$tg = New-Object System.Windows.Media.TranslateTransform (-$bounds.X), (-$bounds.Y)
$pg.Transform = $tg
$flat = [System.Windows.Media.PathGeometry]::CreateFromGeometry($pg)
$d = $flat.ToString($inv) -replace '^F[01]', ''
$ww = [Math]::Ceiling($bounds.Width); $wh = [Math]::Ceiling($bounds.Height)
Save 'wordmark.svg' "0 0 $ww $wh" 'RunnerTray' "<path d=""$d"" fill=""$Ink""/>"
Save 'wordmark-white.svg' "0 0 $ww $wh" 'RunnerTray' "<path d=""$d"" fill=""$Paper""/>"

# ---------- lockups ----------
# Horizontal: symbol 256 high; wordmark cap height matched to the track's height (136), gap 1/4 symbol.
$trackH = 136
$scale = $trackH / $wh
$wx = 256 + 40
$hw = [Math]::Ceiling($wx + $ww * $scale + 8)
function Lockup([string]$track, [string]$light, [string]$text) {
    "$(Symbol $master $track $light)`n  <path transform=""translate($(F $wx) $(F (128 - $trackH / 2))) scale($(F $scale))"" d=""$d"" fill=""$text""/>"
}
Save 'lockup-horizontal.svg' "0 0 $hw 256" 'RunnerTray' (Lockup $Ink $Orange $Ink)
Save 'lockup-horizontal-reversed.svg' "0 0 $hw 256" 'RunnerTray' (Lockup $Paper $Orange $Paper)
# Stacked: symbol above, wordmark centred below.
$sScale = 200 / $ww
$sh = 256 + 12 + $wh * $sScale
$stack = "<g transform=""translate($(F (100 - 128)) 0)"">$(Symbol $master $Ink $Orange)</g>`n  <path transform=""translate(0 $(F (256 + 12))) scale($(F $sScale))"" d=""$d"" fill=""$Ink""/>"
Save 'lockup-stacked.svg' "0 0 200 $(F $sh)" 'RunnerTray' $stack

# ---------- tray status icons (small cut, light = status colour with a shape inside) ----------
# Shapes inside the status light, relative to its centre and radius (so they scale with the small cut).
$cx = $small.Lx; $cy0 = $small.Ly; $q = $small.Lr
function P([double]$dx, [double]$dy) { "$(F ($cx + $dx * $q)) $(F ($cy0 + $dy * $q))" }
$glyph = @{
    pass  = "M$(P -0.46 0.02) L$(P -0.12 0.36) L$(P 0.48 -0.32)"
    fail  = "M$(P -0.36 -0.36) L$(P 0.36 0.36) M$(P 0.36 -0.36) L$(P -0.36 0.36)"
    run   = "M$(P -0.3 -0.5) L$(P 0.55 0) L$(P -0.3 0.5) Z"
    error = "M$(P 0 -0.52) L$(P 0 0.1) M$(P 0 0.46) L$(P 0 0.47)"
    idle  = "M$(P -0.46 0) L$(P 0.46 0)"
}
$sw = F (0.3 * $q)
foreach ($theme in 'dark', 'light') {
    $track = $(if ($theme -eq 'dark') { $Paper } else { $Ink })
    foreach ($k in $Status.Keys) {
        $g = $glyph[$k]
        $mark = if ($k -eq 'run') { "<path d=""$g"" fill=""$Ink""/>" } else { "<path d=""$g"" fill=""none"" stroke=""$Ink"" stroke-width=""$sw"" stroke-linecap=""round"" stroke-linejoin=""round""/>" }
        Save "tray-$k-$theme.svg" '0 0 32 32' "RunnerTray: $k" ((Symbol $small $track $Status[$k]) + "`n  $mark")
    }
}

Write-Host "Brand files written to $out"
Get-ChildItem $out -Filter *.svg | ForEach-Object { Write-Host "  $($_.Name)" }
