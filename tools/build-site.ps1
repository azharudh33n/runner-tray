# Assembles the website in _site: site/* plus images from docs/ and assets/.
# Used by the Pages workflow and for a local preview:  pwsh tools/build-site.ps1
$root = Join-Path $PSScriptRoot '..'
$out = Join-Path $root '_site'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $root 'site\*') $out -Recurse
Copy-Item (Join-Path $root 'docs\*.png') $out
Copy-Item (Join-Path $root 'assets\logo.svg'), (Join-Path $root 'assets\logo-*.png'), (Join-Path $root 'assets\apple-touch-icon.png') $out
Copy-Item (Join-Path $root 'assets\brand\lockup-horizontal-reversed.svg'), (Join-Path $root 'assets\brand\tray-run-dark.svg') $out
Write-Host "Site built in $out"
