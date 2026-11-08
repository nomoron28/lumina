# Builds everything a release needs into artifacts\:
#   Lumina-<version>-setup.exe      per-user installer (Inno Setup)
#   Lumina-<version>-portable.zip   unzip anywhere; keeps its data in a "data" folder beside the program
#   SHA256SUMS.txt                  checksums for both
#   latest.json                     the file the in-app update check reads (upload it next to the installer)
param([string]$Version, [switch]$SkipPublish, [switch]$SkipInstaller, [switch]$NoKeys, [switch]$NoFonts)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$art = Join-Path $repo "artifacts"
New-Item -ItemType Directory -Force $art | Out-Null

if (-not $Version) { $Version = ([xml](Get-Content "src\Lumina\Lumina.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "version '$Version' is not like 1.2.3" }
Write-Host "Packaging Lumina $Version"

$pub = Join-Path $art "publish"
if (-not $SkipPublish) {
    if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
    dotnet publish src\Lumina -c Release -r win-x64 --self-contained true -o $pub -p:DebugType=none -p:DebugSymbols=false -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" -p:NoBakedKeys=$(if ($NoKeys) { "true" } else { "false" }) -p:NoFonts=$(if ($NoFonts) { "true" } else { "false" }) --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
}
foreach ($must in "Lumina.exe", "libmpv-2.dll", "mpv.conf", "input.conf", "scripts\lumina-osc.lua") {
    if (-not (Test-Path (Join-Path $pub $must))) { throw "the published folder is missing $must" }
}

# --- portable zip ---
$zip = Join-Path $art "Lumina-$Version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
$stage = Join-Path $art "portable-stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Copy-Item $pub $stage -Recurse
Set-Content (Join-Path $stage "portable.txt") "Lumina is running in portable mode: settings, library and logs are kept in the data folder next to Lumina.exe.`r`nDelete this file to use your Windows user profile instead." -Encoding ascii
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force

# --- installer ---
$setup = Join-Path $art "Lumina-$Version-setup.exe"
if (-not $SkipInstaller) {
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }
    & $iscc /Qp "/DAppVersion=$Version" "/DSourceDir=$pub" installer\Lumina.iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
}

# --- checksums and the update feed ---
$lines = foreach ($f in @($setup, $zip) | Where-Object { Test-Path $_ }) { "{0}  {1}" -f (Get-FileHash $f -Algorithm SHA256).Hash.ToLower(), (Split-Path $f -Leaf) }
Set-Content (Join-Path $art "SHA256SUMS.txt") $lines -Encoding ascii
if (Test-Path $setup) {
    $feed = [ordered]@{
        version = $Version
        file    = (Split-Path $setup -Leaf)
        sha256  = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
        size    = (Get-Item $setup).Length
        notes   = ""
    }
    ($feed | ConvertTo-Json) | Set-Content (Join-Path $art "latest.json") -Encoding utf8
}
Get-ChildItem $art -File | Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize | Out-String | Write-Host
