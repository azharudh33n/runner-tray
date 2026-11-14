# Assembles the website in _site: site/* plus images from docs/ and assets/, then
#   - joins the stylesheets in <head> into one file (site.css) so the page has one blocking request,
#   - minifies CSS and JS with esbuild (needs Node; `npx` fetches esbuild).
# Used by the Website workflow and for a local preview:  pwsh tools/build-site.ps1
#   -NoMinify   skip the join and minify steps (easier debugging)
param([switch]$NoMinify)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out = Join-Path $root '_site'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $root 'site\*') $out -Recurse
Copy-Item (Join-Path $root 'site\.well-known') $out -Recurse -Force
Copy-Item (Join-Path $root 'docs\*.png') $out
Copy-Item (Join-Path $root 'assets\logo.svg'), (Join-Path $root 'assets\logo-*.png'), (Join-Path $root 'assets\apple-touch-icon.png'), (Join-Path $root 'assets\og.png') $out
Copy-Item (Join-Path $root 'assets\brand\lockup-horizontal-reversed.svg'), (Join-Path $root 'assets\brand\tray-run-dark.svg') $out

if (-not $NoMinify) {
    # 1. One stylesheet for all local <link rel="stylesheet"> tags in index.html, placed where the
    #    first one was. Safe here because the inline <style> blocks between them only set variables
    #    or come after all of them.
    $index = Join-Path $out 'index.html'
    $html = [IO.File]::ReadAllText($index)
    # assets/ui/base.css and assets/ui/<fragment>/fragment.css stay separate: assets/ui/ui.js looks
    # for those <link> tags and adds them again (after the page styles) when it cannot find them.
    $links = [regex]::Matches($html, '<link rel="stylesheet" href="(?!https?:|assets/ui/base\.css|assets/ui/[\w-]+/fragment\.css)([^"]+)">\r?\n?')
    if ($links.Count -gt 1) {
        $css = New-Object System.Text.StringBuilder
        foreach ($m in $links) {
            $href = $m.Groups[1].Value
            if ($href -eq 'site.css') { continue }
            $dir = [IO.Path]::GetDirectoryName($href).Replace('\', '/')
            $text = [IO.File]::ReadAllText((Join-Path $out $href))
            # Relative url(...) values must now resolve from the site root.
            $text = [regex]::Replace($text, 'url\((["'']?)(?!data:|https?:|/|#|%23)([^)"'']+)\1\)', {
                param($u) "url($($u.Groups[1].Value)$(if ($dir) { "$dir/" })$($u.Groups[2].Value)$($u.Groups[1].Value))"
            })
            [void]$css.AppendLine("/* $href */").AppendLine($text)
        }
        [IO.File]::WriteAllText((Join-Path $out 'site.css'), $css.ToString())
        for ($i = $links.Count - 1; $i -ge 1; $i--) { $html = $html.Remove($links[$i].Index, $links[$i].Length) }
        $html = $html.Remove($links[0].Index, $links[0].Length).Insert($links[0].Index, "<link rel=`"stylesheet`" href=`"site.css`">`n")
        [IO.File]::WriteAllText($index, $html)
    }

    # 2. Minify every CSS and JS file in place (no bundling, so module paths stay the same).
    Push-Location $out
    try {
        $files = Get-ChildItem -Recurse -File -Include *.css, *.js | ForEach-Object { [IO.Path]::GetRelativePath($out, $_.FullName).Replace('\', '/') }
        npx --yes esbuild@0.24 @files --minify --outdir=. --outbase=. --allow-overwrite --log-level=warning
        if ($LASTEXITCODE -ne 0) { throw 'esbuild failed' }
    } finally { Pop-Location }
}
Write-Host "Site built in $out"
