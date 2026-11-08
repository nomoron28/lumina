# The regression gate. Run before every commit and after every milestone. Exit code 0 only if everything passes.
#   powershell -File scripts\gate.ps1            normal gate
#   powershell -File scripts\gate.ps1 -Soak 10   adds a 10-minute playback soak (tier gates use 30)
param([double]$Soak = 0, [switch]$SkipUi, [switch]$Package)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$failed = @()
function Step($name, [scriptblock]$body) {
    Write-Host ""; Write-Host "=== $name ===" -ForegroundColor Cyan
    $t = [Diagnostics.Stopwatch]::StartNew()
    & $body
    $ok = ($LASTEXITCODE -eq 0)
    Write-Host ("{0} in {1:N0}s" -f $(if ($ok) { "PASS" } else { "FAIL" }), $t.Elapsed.TotalSeconds) -ForegroundColor $(if ($ok) { "Green" } else { "Red" })
    if (-not $ok) { $script:failed += $name }
}

Get-Process Lumina -ErrorAction SilentlyContinue | Stop-Process -Force

Step "Build (no errors, no warnings)" {
    $out = dotnet build Lumina.sln -nologo -v q --no-incremental 2>&1 | Out-String
    $out | Select-String "error|warning" | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line }
    $w = [regex]::Match($out, '(\d+) Warning\(s\)').Groups[1].Value
    $e = [regex]::Match($out, '(\d+) Error\(s\)').Groups[1].Value
    Write-Host "warnings: $w, errors: $e"
    if ($e -ne "0" -or $w -ne "0") { $global:LASTEXITCODE = 1 } else { $global:LASTEXITCODE = 0 }
}

Step "Design tokens (generated files fresh, contrast passes in dark and light)" {
    python "$repo/replica/design/build_wpf.py" --check
    if ($LASTEXITCODE -eq 0) { python "$repo/replica/design/contrast.py" "$repo/replica/design/tokens.json" | Select-Object -Last 1 }
    if ($LASTEXITCODE -eq 0) { python "$repo/replica/design/contrast.py" "$repo/replica/design/tokens.json" --theme light | Select-Object -Last 1 }
}

Step "Fixtures" { powershell -NoProfile -File "$PSScriptRoot\make-fixtures.ps1" | Select-Object -Last 1 }

Step "Unit and headless tests" { dotnet test tests/Lumina.Tests -nologo -v q 2>&1 | Select-Object -Last 4 }

# The build and the test run leave compiler servers and test hosts busy for a while, and a click test that starts then loses clicks.
# Stop them and wait (up to a minute) for the processor to calm down before anything drives the real mouse.
function Wait-Quiet {
    dotnet build-server shutdown 2>&1 | Out-Null
    $until = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $until) {
        $cpu = (Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 2 -MaxSamples 2).CounterSamples | Measure-Object CookedValue -Average
        if ($cpu.Average -lt 30) { return }
    }
    Write-Host "  (processor still busy after a minute, going on)" -ForegroundColor Yellow
}

if (-not $SkipUi) {
    Wait-Quiet
    Step "Real-input tour (settings, player)" { powershell -NoProfile -File "$PSScriptRoot\e2e.ps1" | Select-Object -Last 8 }
    Step "Budgets and soak" { powershell -NoProfile -File "$PSScriptRoot\smoke.ps1" -Soak $Soak | Select-Object -Last 8 }
}

if ($Package) {
    Step "Package and installer test (build, install, run, upgrade, downgrade refusal, uninstall, portable)" {
        powershell -NoProfile -File "$PSScriptRoot\package.ps1" | Select-Object -Last 3
        powershell -NoProfile -File "$PSScriptRoot\test-installer.ps1" | Select-Object -Last 6
    }
}

Write-Host ""
if ($failed.Count -eq 0) { Write-Host "GATE GREEN" -ForegroundColor Green; exit 0 }
Write-Host ("GATE RED: " + ($failed -join ", ")) -ForegroundColor Red; exit 1
