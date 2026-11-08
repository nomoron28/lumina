# Startup and memory budgets plus an optional playback soak. Exit code 0 only if every budget holds.
#   -Soak <minutes>   play the interlaced transport stream on a loop and watch memory and dropped frames
param([double]$Soak = 0, [int]$StartupBudgetMs = 2500, [int]$IdleMemoryMB = 250)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\e2e-lib.ps1"
$fx = Join-Path (Split-Path $PSScriptRoot -Parent) "scratch\fixtures"
Get-Process Lumina -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Startup and idle memory"
$sw = [Diagnostics.Stopwatch]::StartNew()
$app = Start-Lumina "" @{} "home"
try {
    # Start-Lumina polls every 250 ms, so subtract its settle time; this measures process start to a visible window.
    $ms = $sw.ElapsedMilliseconds - 800
    CheckTrue "window appears within ${StartupBudgetMs} ms (took $ms)" ($ms -le $StartupBudgetMs)
    Start-Sleep -Seconds 4
    $app.Proc.Refresh()
    $mb = [int]($app.Proc.WorkingSet64 / 1MB)
    CheckTrue "idle memory under ${IdleMemoryMB} MB (is $mb)" ($mb -le $IdleMemoryMB)
    Check "no errors logged at startup" (Error-Count $app) 0
} finally { Stop-Lumina $app }

if ($Soak -gt 0) {
    Write-Host "Soak: $Soak minute(s) of playback"
    $app = Start-Lumina "$fx\interlaced.ts" @{}   # starts paused with an IPC pipe; the soak unpauses it
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Mpv-Set $app 'loop-file' 'inf'; Mpv-Set $app 'pause' $false
        Start-Sleep -Seconds 20
        $app.Proc.Refresh(); $mem0 = $app.Proc.WorkingSet64 / 1MB
        $d0 = [int](Mpv-Get $app 'frame-drop-count') + [int](Mpv-Get $app 'decoder-frame-drop-count')
        $t0 = [double](Mpv-Get $app 'playback-time')
        Start-Sleep -Seconds ([int]($Soak * 60))
        $app.Proc.Refresh(); $mem1 = $app.Proc.WorkingSet64 / 1MB
        $d1 = [int](Mpv-Get $app 'frame-drop-count') + [int](Mpv-Get $app 'decoder-frame-drop-count')
        Check "still playing" ((Mpv-Get $app 'pause')) $false
        CheckTrue ("memory growth under 80 MB ({0:N0} to {1:N0})" -f $mem0, $mem1) (($mem1 - $mem0) -lt 80)
        CheckTrue "dropped frames under 1 per 10 s ($($d1 - $d0) in $Soak min)" (($d1 - $d0) -le [math]::Ceiling($Soak * 6))
        Check "no errors during the soak" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

Write-Host ""
Write-Host "$script:passes passed, $($script:fails.Count) failed"
$script:fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit $(if ($script:fails.Count -eq 0) { 0 } else { 1 })
