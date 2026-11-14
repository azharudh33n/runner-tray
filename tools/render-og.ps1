# Renders the share card (tools/og/og.html) with headless Chrome:
#   assets/og.png      1200x630  Open Graph and Twitter image for the website
#   docs/social.png    1280x640  GitHub repository social preview
#   pwsh tools/render-og.ps1
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$chrome = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { throw 'Google Chrome not found.' }

$page = 'file:///' + (Join-Path $root 'tools\og\og.html').Replace('\', '/')
$profile = Join-Path ([IO.Path]::GetTempPath()) 'runnertray-og-chrome'

foreach ($target in @(
    @{ Path = 'assets\og.png'; W = 1200; H = 630 },
    @{ Path = 'docs\social.png'; W = 1280; H = 640 }
)) {
    $out = Join-Path $root $target.Path
    & $chrome --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
        --allow-file-access-from-files "--user-data-dir=$profile" `
        "--window-size=$($target.W),$($target.H)" "--screenshot=$out" --virtual-time-budget=3000 $page 2>$null | Out-Null
    if (-not (Test-Path $out)) { throw "Chrome did not write $out" }
    Write-Host "Wrote $($target.Path) ($($target.W)x$($target.H))"
}
