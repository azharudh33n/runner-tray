# Soak test: runs RunnerTray and samples memory, handles and window resources over time, to catch leaks.
#   pwsh tools/soak-test.ps1 -Exe path\to\RunnerTray.exe -Minutes 60 [-Args "--test-ui panel"]
# Writes a CSV next to the script's working folder and prints a summary with growth per hour.
param(
    [Parameter(Mandatory)] [string]$Exe,
    [int]$Minutes = 60,
    [int]$IntervalSeconds = 30,
    [string]$Args = ''
)

Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Gui { [DllImport("user32.dll")] public static extern int GetGuiResources(IntPtr h, int flags); }
'@

Get-Process RunnerTray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
$p = if ($Args) { Start-Process $Exe -ArgumentList $Args -PassThru } else { Start-Process $Exe -PassThru }
$csv = Join-Path (Get-Location) "soak-$(Get-Date -Format yyyyMMdd-HHmm).csv"
'minute,working_mb,private_mb,handles,gdi,user,cpu_s' | Set-Content $csv
$end = (Get-Date).AddMinutes($Minutes)
$start = Get-Date
$rows = @()
while ((Get-Date) -lt $end) {
    Start-Sleep $IntervalSeconds
    $p.Refresh()
    if ($p.HasExited) { Write-Host "RunnerTray exited with code $($p.ExitCode)"; break }
    $row = [pscustomobject]@{
        minute = [math]::Round(((Get-Date) - $start).TotalMinutes, 1)
        working_mb = [math]::Round($p.WorkingSet64 / 1MB, 1)
        private_mb = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
        handles = $p.HandleCount
        gdi = [Gui]::GetGuiResources($p.Handle, 0)
        user = [Gui]::GetGuiResources($p.Handle, 1)
        cpu_s = [math]::Round($p.TotalProcessorTime.TotalSeconds, 2)
    }
    $rows += $row
    "$($row.minute),$($row.working_mb),$($row.private_mb),$($row.handles),$($row.gdi),$($row.user),$($row.cpu_s)" | Add-Content $csv
}
if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) }

if ($rows.Count -lt 4) { Write-Host 'Not enough samples.'; exit 1 }
# Compare the average of the second quarter with the last quarter, so start-up growth does not count.
$q = [math]::Floor($rows.Count / 4)
$early = $rows[$q..(2 * $q - 1)]; $late = $rows[(3 * $q)..($rows.Count - 1)]
$hours = ($late[-1].minute - $early[0].minute) / 60
function Avg($set, $prop) { ($set | Measure-Object $prop -Average).Average }
Write-Host "Samples: $($rows.Count) over $($rows[-1].minute) min. CSV: $csv"
foreach ($m in 'working_mb', 'private_mb', 'handles', 'gdi', 'user') {
    $a = Avg $early $m; $b = Avg $late $m
    Write-Host ("{0,-11} start {1,8:N1}  end {2,8:N1}  growth/hour {3,7:N1}" -f $m, $a, $b, (($b - $a) / [math]::Max(0.1, $hours)))
}
$cpu = ($rows[-1].cpu_s - $rows[0].cpu_s) / [math]::Max(1, ($rows[-1].minute - $rows[0].minute) * 60) * 100
Write-Host ("Average CPU: {0:N3} %" -f $cpu)
