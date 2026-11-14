# End-to-end test of the real installer: install an older version, update to the new one with the
# app's own updater, then uninstall, and check that nothing is left behind.
# Runs in the Release workflow on a clean Windows runner. It installs into the current user's
# profile, so only run it locally if you are fine with RunnerTray being installed and removed.
#
#   pwsh tools/test-install.ps1 -OldReleases releases-old -NewReleases releases -NewVersion 1.0.0
param(
    [Parameter(Mandatory)] [string]$OldReleases,
    [Parameter(Mandatory)] [string]$NewReleases,
    [Parameter(Mandatory)] [string]$NewVersion
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'RunnerTray'
$data = Join-Path $env:LOCALAPPDATA 'RunnerTray-data'
$settings = Join-Path $env:APPDATA 'RunnerTray'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RunnerTray'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\RunnerTray.lnk'
$failures = New-Object System.Collections.Generic.List[string]

function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

function Wait-Until([scriptblock]$condition, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "  timed out waiting for: $what"
    return $false
}

function Installed-Version {
    # Velopack's manifest of the installed package.
    $manifest = Join-Path $root 'current\sq.version'
    if (-not (Test-Path $manifest)) { return $null }
    $m = [regex]::Match((Get-Content $manifest -Raw), '<version>([^<]+)</version>')
    return $(if ($m.Success) { $m.Groups[1].Value } else { $null })
}

function Stop-RunnerTray {
    Get-Process RunnerTray -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

Write-Host "1. Install the older version"
Stop-RunnerTray
$setup = Get-ChildItem $OldReleases -Filter '*Setup.exe' | Select-Object -First 1
$p = Start-Process $setup.FullName -ArgumentList '--silent' -Wait -PassThru
Check ($p.ExitCode -eq 0) "Setup.exe exit code 0 (was $($p.ExitCode))"
$oldVersion = Installed-Version
Check ($null -ne $oldVersion) "app installed in $root (version $oldVersion)"
Check (Test-Path $uninstallKey) 'Settings > Apps entry exists'
Check (Test-Path $shortcut) 'Start menu shortcut exists'
Check (Test-Path (Join-Path $root 'RunnerTray.exe')) 'stable launcher exists'
Stop-RunnerTray

Write-Host "2. Start it once and add the things a user would have"
$env:RUNNERTRAY_UPDATE_DIR = (Resolve-Path $NewReleases).Path
Start-Process (Join-Path $root 'RunnerTray.exe')
Check (Wait-Until { Get-Process RunnerTray -ErrorAction SilentlyContinue } 20 'RunnerTray running') 'app starts from the stable launcher'
New-Item -Path $runKey -Force -ErrorAction SilentlyContinue | Out-Null
New-ItemProperty -Path $runKey -Name RunnerTray -Value "`"$(Join-Path $root 'RunnerTray.exe')`"" -Force | Out-Null
cmdkey /generic:RunnerTray:github /user:RunnerTray /pass:test-token | Out-Null
Check (Wait-Until { Test-Path $settings } 20 'settings folder') 'settings folder created'

Write-Host "3. Update with the app's own updater"
$downloaded = Wait-Until { Test-Path (Join-Path $root "packages\RunnerTray-$NewVersion-full.nupkg") } 90 'update downloaded'
Check $downloaded "app found and downloaded $NewVersion"
Stop-RunnerTray
if ($downloaded) {
    $p = Start-Process (Join-Path $root 'Update.exe') -ArgumentList 'apply', '--norestart' -Wait -PassThru
    Check ($p.ExitCode -eq 0) "Update.exe apply exit code 0 (was $($p.ExitCode))"
}
Check ((Installed-Version) -eq $NewVersion) "installed version is $NewVersion (is $(Installed-Version))"
Remove-Item Env:RUNNERTRAY_UPDATE_DIR

Write-Host "4. Start the new version, then uninstall the way Settings > Apps does"
Start-Process (Join-Path $root 'RunnerTray.exe')
Check (Wait-Until { Get-Process RunnerTray -ErrorAction SilentlyContinue } 20 'RunnerTray running') 'updated app starts'
Check (Wait-Until { Test-Path $data } 20 'data folder') 'data folder created'
$uninstall = (Get-ItemProperty $uninstallKey).QuietUninstallString
$exe, $args1 = $uninstall -split '"\s+', 2
$p = Start-Process $exe.Trim('"') -ArgumentList $args1 -Wait -PassThru
Check ($p.ExitCode -eq 0) "uninstaller exit code 0 (was $($p.ExitCode))"

Write-Host "5. Nothing left behind"
Check (Wait-Until { -not (Get-Process RunnerTray -ErrorAction SilentlyContinue) } 20 'app stopped') 'app is not running'
Check (Wait-Until { -not (Test-Path $root) } 30 'install folder removed') 'install folder removed'
Check (-not (Test-Path $uninstallKey)) 'Settings > Apps entry removed'
Check (-not (Test-Path $shortcut)) 'Start menu shortcut removed'
Check ($null -eq (Get-ItemProperty $runKey -Name RunnerTray -ErrorAction SilentlyContinue)) 'Start with Windows removed'
Check (-not ((cmdkey /list:RunnerTray:github) -match 'Target:')) 'sign-in removed from Credential Manager'
Check (-not (Test-Path $data)) 'cache and logs removed'
Check (Test-Path $settings) 'settings kept for a reinstall (by design)'

Remove-Item $settings -Recurse -Force -ErrorAction SilentlyContinue
if ($failures.Count -gt 0) {
    Write-Host "`n$($failures.Count) check(s) failed:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
Write-Host "`nAll install, update and uninstall checks passed."
