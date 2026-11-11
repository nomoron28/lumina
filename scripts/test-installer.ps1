# Installs, runs, upgrades, refuses a downgrade, and uninstalls the real installer and portable zip, in temporary folders.
# It never touches your real Lumina settings: data clean-up is aimed at a test folder, logins use a test prefix,
# and file types are registered in a private registry branch. Run scripts\package.ps1 first.
param([switch]$Force)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\e2e-lib.ps1"
$repo = Split-Path $PSScriptRoot -Parent
$art = Join-Path $repo "artifacts"
$fx = Join-Path $repo "scratch\fixtures"
$appId = "{B6D1F5E2-6C1B-4B7D-9E64-6A1E5C0E5F10}_is1"
$uninstKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId"

$v1 = (Get-ChildItem $art -Filter "Lumina-*-setup.exe" | Sort-Object Name | Select-Object -First 1)
if (-not $v1) { throw "no installer in artifacts, run scripts\package.ps1 first" }
$ver1 = [regex]::Match($v1.Name, 'Lumina-(.+)-setup').Groups[1].Value
if ((Test-Path $uninstKey) -and -not $Force) { throw "Lumina is already installed for this user. This test would replace it. Use -Force only on a development machine." }
Get-Process Lumina -ErrorAction SilentlyContinue | Stop-Process -Force

$tmp = Join-Path $env:TEMP ("lumina-inst-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$dir = Join-Path $tmp "Lumina"
$dataRoot = Join-Path $tmp "dataroot"
New-Item -ItemType Directory -Force "$dataRoot\Roaming\Lumina", "$dataRoot\Local\Lumina" | Out-Null
Set-Content "$dataRoot\Roaming\Lumina\config.json" '{"general.tour":"false","updates.onStart":"false"}' ; Set-Content "$dataRoot\Local\Lumina\art.txt" "x"
$regBranch = "Software\LuminaInstTest\" + [guid]::NewGuid().ToString("N")
$credPrefix = "LuminaInstTest." + [guid]::NewGuid().ToString("N").Substring(0, 8) + ":"
$env:LUMINA_REG_BRANCH = $regBranch
$env:LUMINA_CRED_PREFIX = $credPrefix

function Set-TestEnv { $env:LUMINA_REG_BRANCH = $regBranch; $env:LUMINA_CRED_PREFIX = $credPrefix }   # Start-Lumina clears these, so set them before every setup or uninstall

function Run-Setup($file, $extra) {
    Set-TestEnv
    $p = Start-Process $file -ArgumentList (@("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=`"$dir`"", "/LOG=`"$tmp\setup.log`"") + $extra) -PassThru -Wait
    return $p.ExitCode
}
function Installed-Version { (Get-ItemProperty $uninstKey -ErrorAction SilentlyContinue).DisplayVersion }

try {
    Write-Host "Install $ver1"
    Check "installer exits 0" (Run-Setup $v1.FullName @("/TASKS=assoc")) 0
    CheckTrue "program files are in place" ((Test-Path "$dir\Lumina.exe") -and (Test-Path "$dir\libmpv-2.dll") -and (Test-Path "$dir\scripts\lumina-osc.lua") -and (Test-Path "$dir\mpv.conf"))
    Check "Windows lists it with the right version" (Installed-Version) $ver1
    CheckTrue "no administrator rights were needed (HKCU entry)" (Test-Path $uninstKey)
    CheckTrue "a Start menu shortcut exists" (Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Lumina.lnk")
    CheckTrue "the file types task registered Open with" ((Get-Item "HKCU:\$regBranch\Classes\.mkv\OpenWithProgids" -ErrorAction SilentlyContinue).GetValueNames() -contains "Lumina.Video")
    Check "the executable reports the same version" ((Get-Item "$dir\Lumina.exe").VersionInfo.FileVersion) "$ver1.0"

    Write-Host "Run the installed program"
    $Exe = "$dir\Lumina.exe"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "installed Lumina starts and its controller loads (libmpv and scripts shipped)" (Mpv-Ready $app)
        CheckTrue "and plays the fixture" ((Wait-Path $app 'multi') -ne $null)
        Mpv-Set $app 'pause' $false; Start-Sleep -Milliseconds 1500
        CheckTrue "time moves" ([double](Mpv-Get $app 'time-pos') -gt 0.5)
        Check "no errors in its log" (Error-Count $app) 0
    } finally { Stop-Lumina $app }

    Write-Host "Upgrade"
    & "$PSScriptRoot\package.ps1" -Version "0.1.1" -SkipPublish | Out-Null
    $v2 = Join-Path $art "Lumina-0.1.1-setup.exe"
    Check "upgrade installer exits 0" (Run-Setup $v2 @("/TASKS=")) 0
    Check "version is now 0.1.1" (Installed-Version) "0.1.1"
    CheckTrue "program files still there" (Test-Path "$dir\Lumina.exe")
    CheckTrue "data folder untouched by the upgrade" (Test-Path "$dataRoot\Roaming\Lumina\config.json")

    Write-Host "Downgrade is refused"
    $rc = Run-Setup $v1.FullName @("/TASKS=")
    CheckTrue "older installer exits non-zero" ($rc -ne 0)
    Check "version stays 0.1.1" (Installed-Version) "0.1.1"

    Write-Host "Uninstall keeps your data by default"
    $un = (Get-ItemProperty $uninstKey).UninstallString.Trim('"'); Set-TestEnv
    $p = Start-Process $un -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DATAROOT=`"$dataRoot`"" -PassThru -Wait
    Check "uninstaller exits 0" $p.ExitCode 0
    Start-Sleep -Milliseconds 800
    CheckTrue "program folder is gone" (-not (Test-Path "$dir\Lumina.exe"))
    CheckTrue "Windows no longer lists it" (-not (Test-Path $uninstKey))
    CheckTrue "the Start menu shortcut is gone" (-not (Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Lumina.lnk"))
    CheckTrue "settings were kept" (Test-Path "$dataRoot\Roaming\Lumina\config.json")
    # the first install registered file types (with the assoc task); the upgrade did not, but uninstall must clean up either way
    $ow = Get-Item "HKCU:\$regBranch\Classes\.mkv\OpenWithProgids" -ErrorAction SilentlyContinue
    CheckTrue "Open with entry removed at uninstall" ((-not $ow) -or (-not ($ow.GetValueNames() -contains "Lumina.Video")))
    CheckTrue "and the program id is gone" (-not (Test-Path "HKCU:\$regBranch\Classes\Lumina.Video"))

    Write-Host "Uninstall with data removal"
    Check "reinstall for the purge test" (Run-Setup $v2 @("/TASKS=")) 0
    cmdkey /generic:"${credPrefix}http://purge.test:80" /user:u /pass:p | Out-Null
    CheckTrue "a saved login exists before" ((cmdkey /list | Out-String) -match [regex]::Escape($credPrefix))
    $un = (Get-ItemProperty $uninstKey).UninstallString.Trim('"'); Set-TestEnv
    $p = Start-Process $un -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/PURGE=1", "/DATAROOT=`"$dataRoot`"" -PassThru -Wait
    Check "purging uninstaller exits 0" $p.ExitCode 0
    Start-Sleep -Milliseconds 800
    CheckTrue "settings folder removed" (-not (Test-Path "$dataRoot\Roaming\Lumina"))
    CheckTrue "cache folder removed" (-not (Test-Path "$dataRoot\Local\Lumina"))
    Check "saved logins forgotten" ((cmdkey /list | Out-String) -match [regex]::Escape($credPrefix)) $false

    Write-Host "Portable zip"
    $zip = Get-ChildItem $art -Filter "Lumina-$ver1-portable.zip" | Select-Object -First 1
    $pdir = Join-Path $tmp "portable"
    Expand-Archive $zip.FullName $pdir
    CheckTrue "zip holds the program, libmpv and the portable marker" ((Test-Path "$pdir\Lumina.exe") -and (Test-Path "$pdir\libmpv-2.dll") -and (Test-Path "$pdir\portable.txt"))
    # run it with NO data overrides: portable mode must keep everything beside the program
    foreach ($k in "LUMINA_DB", "LUMINA_ART", "LUMINA_CONFIG", "LUMINA_LOG", "LUMINA_SUBS", "LUMINA_INSTANCE", "LUMINA_PAGE", "LUMINA_SEED_ROOT") { Remove-Item "Env:\$k" -ErrorAction SilentlyContinue }
    $env:LUMINA_INSTANCE = "portable-" + [guid]::NewGuid().ToString("N").Substring(0, 6)
    $pp = Start-Process "$pdir\Lumina.exe" -PassThru
    Start-Sleep -Seconds 4
    $logs = Get-ChildItem "$pdir\data\logs" -Filter *.log -ErrorAction SilentlyContinue
    CheckTrue "portable run wrote its log inside data\logs" ($logs -and (Get-Content $logs[0].FullName -Raw) -match "Lumina started")
    CheckTrue "and its database inside data" (Test-Path "$pdir\data\lumina.db")
    if (-not $pp.HasExited) { Stop-Process $pp -Force }
} finally {
    Get-Process Lumina -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Test-Path $uninstKey) { try { $un = (Get-ItemProperty $uninstKey).UninstallString.Trim('"'); Start-Process $un -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DATAROOT=`"$dataRoot`"" -Wait } catch { } }
    (cmdkey /list | Out-String) -split "`n" | Where-Object { $_ -match "Target:.*$([regex]::Escape($credPrefix))" } | ForEach-Object { if ($_ -match "Target:\s*(\S.*?)\s*$") { cmdkey /delete:$($Matches[1]) | Out-Null } }
    Remove-Item "HKCU:\Software\LuminaInstTest" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $art "Lumina-0.1.1-setup.exe") -ErrorAction SilentlyContinue
}
Write-Host ""
Write-Host "$script:passes passed, $($script:fails.Count) failed"
$script:fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit $(if ($script:fails.Count -eq 0) { 0 } else { 1 })
