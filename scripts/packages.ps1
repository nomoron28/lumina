# Writes the winget, Scoop and Chocolatey manifests for a published release, from that release's SHA256SUMS.txt.
#   powershell -File scripts\packages.ps1 -Version 0.1.1
# Then commit the files through a PR. Submitting is a separate, manual step (see docs/development.md, Package managers):
#   winget:     a PR to microsoft/winget-pkgs with packaging\winget\manifests\...  (wingetcreate submit <folder>)
#   Scoop:      nothing to submit. The bucket is this repository: scoop bucket add lumina https://github.com/nomoron28/lumina
#   Chocolatey: choco pack packaging\chocolatey\lumina.nuspec, then choco push with your API key.
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)

$repo = 'nomoron28/lumina'
$base = "https://github.com/$repo/releases/download/v$Version"
$tmp = Join-Path $env:TEMP ("lumina-sums-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory $tmp | Out-Null
try { gh release download "v$Version" -p SHA256SUMS.txt -D $tmp; if ($LASTEXITCODE) { throw "Release v$Version has no SHA256SUMS.txt (is it published?)" } }
catch { Remove-Item $tmp -Recurse -Force; throw }
$sums = @{}
foreach ($line in Get-Content (Join-Path $tmp 'SHA256SUMS.txt')) { if ($line -match '^([0-9a-fA-F]{64})\s+\*?(\S+)$') { $sums[$Matches[2]] = $Matches[1].ToUpper() } }
Remove-Item $tmp -Recurse -Force
$setup = "Lumina-$Version-setup.exe"; $zip = "Lumina-$Version-portable.zip"
if (-not $sums[$setup] -or -not $sums[$zip]) { throw "SHA256SUMS.txt does not list $setup and $zip" }

$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-File($path, [string]$text) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText((Join-Path $PWD $path), ($text.Trim() -replace "`r?`n", "`n") + "`n", $utf8)
    Write-Host "  $path"
}

$short = 'A media library and video player for Windows: your folders as a poster wall, and a libmpv player.'
$desc = 'Lumina scans your local folders and network shares, fills in posters, summaries, cast and ratings, remembers where you stopped, and plays everything through libmpv with hardware decoding, HDR tone mapping and bit-perfect audio. No account and no telemetry.'

# ---- winget (schema 1.6.0), one folder per version ----
$w = "packaging/winget/manifests/n/nomoron28/Lumina/$Version"
Write-File "$w/nomoron28.Lumina.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: nomoron28.Lumina
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@
Write-File "$w/nomoron28.Lumina.installer.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: nomoron28.Lumina
PackageVersion: $Version
InstallerType: inno
Scope: user
UpgradeBehavior: install
ProductCode: '{B6D1F5E2-6C1B-4B7D-9E64-6A1E5C0E5F10}_is1'
Installers:
- Architecture: x64
  InstallerUrl: $base/$setup
  InstallerSha256: $($sums[$setup])
ManifestType: installer
ManifestVersion: 1.6.0
"@
Write-File "$w/nomoron28.Lumina.locale.en-US.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: nomoron28.Lumina
PackageVersion: $Version
PackageLocale: en-US
Publisher: Lumina
PublisherUrl: https://github.com/nomoron28
PublisherSupportUrl: https://github.com/$repo/issues
PackageName: Lumina
PackageUrl: https://github.com/$repo
License: GPL-3.0-or-later
LicenseUrl: https://github.com/$repo/blob/main/LICENSE
ShortDescription: '$($short -replace "'","''")'
Description: '$($desc -replace "'","''")'
Moniker: lumina
Tags:
- media-player
- video-player
- media-library
- mpv
- movies
- tv-shows
ReleaseNotesUrl: https://github.com/$repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

# ---- Scoop: the bucket is this repository's bucket/ folder; the portable zip keeps its data in data\ ----
$scoop = [ordered]@{
    version     = $Version
    description = $short
    homepage    = "https://github.com/$repo"
    license     = 'GPL-3.0-or-later'
    architecture = [ordered]@{ '64bit' = [ordered]@{ url = "$base/$zip"; hash = $sums[$zip].ToLower() } }
    bin         = 'Lumina.exe'
    shortcuts   = @(, @('Lumina.exe', 'Lumina'))
    persist     = 'data'
    checkver    = 'github'
    autoupdate  = [ordered]@{
        architecture = [ordered]@{ '64bit' = [ordered]@{ url = "https://github.com/$repo/releases/download/v`$version/Lumina-`$version-portable.zip" } }
        hash = [ordered]@{ url = "https://github.com/$repo/releases/download/v`$version/SHA256SUMS.txt" }
    }
}
Write-File 'bucket/lumina.json' ($scoop | ConvertTo-Json -Depth 6)

# ---- Chocolatey: installs for all users (choco runs elevated) and lets choco's auto-uninstaller remove it ----
Write-File 'packaging/chocolatey/lumina.nuspec' @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    <id>lumina</id>
    <version>$Version</version>
    <title>Lumina</title>
    <authors>nomoron28</authors>
    <owners>nomoron28</owners>
    <projectUrl>https://github.com/$repo</projectUrl>
    <packageSourceUrl>https://github.com/$repo/tree/main/packaging/chocolatey</packageSourceUrl>
    <licenseUrl>https://github.com/$repo/blob/main/LICENSE</licenseUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <bugTrackerUrl>https://github.com/$repo/issues</bugTrackerUrl>
    <releaseNotes>https://github.com/$repo/releases/tag/v$Version</releaseNotes>
    <tags>media-player video-player media-library mpv movies tv-shows</tags>
    <summary>$short</summary>
    <description>$desc</description>
  </metadata>
  <files>
    <file src="tools\**" target="tools" />
  </files>
</package>
"@
Write-File 'packaging/chocolatey/tools/chocolateyinstall.ps1' @"
`$ErrorActionPreference = 'Stop'
Install-ChocolateyPackage -PackageName 'lumina' -FileType 'exe' ``
  -Url64bit '$base/$setup' ``
  -Checksum64 '$($sums[$setup])' -ChecksumType64 'sha256' ``
  -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS' -ValidExitCodes @(0)
"@
Write-Host "Wrote the package manifests for $Version."
