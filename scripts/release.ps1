# Cuts a release: bumps the version, dates the changelog, commits and tags. Pushes nothing.
#   powershell -File scripts\release.ps1 -Version 0.1.1
# Run from a clean, up-to-date main. Then: git push origin main --follow-tags  (via a PR, see docs/development.md).
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
if ((git branch --show-current) -ne 'main') { throw 'Run this on main.' }
if (git status --porcelain --untracked-files=no) { throw 'Working tree has uncommitted changes.' }
if (git tag -l "v$Version") { throw "Tag v$Version already exists." }

$cl = Get-Content CHANGELOG.md -Raw -Encoding UTF8   # Windows PowerShell reads ANSI otherwise and mangles · and …
if ($cl -notmatch '(?s)## \[Unreleased\]\s*\n(.+?)\n## \[') { throw 'CHANGELOG.md has no [Unreleased] entries.' }
$cl = $cl -replace '## \[Unreleased\]', "## [Unreleased]`n`n## [$Version] - $(Get-Date -Format yyyy-MM-dd)"
$cl = $cl -replace '(?m)^(\[\d+\.\d+\.\d+\]: )', "[$Version]: https://github.com/nomoron28/lumina/releases/tag/v$Version`n`$1" -replace "(?s)(\[$Version\]: [^\n]+\n)(.*?\1)", '$1'
[IO.File]::WriteAllText("$PWD\CHANGELOG.md", $cl)

$proj = Get-Content src\Lumina\Lumina.csproj -Raw
$proj = $proj -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>" `
              -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>" `
              -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
[IO.File]::WriteAllText("$PWD\src\Lumina\Lumina.csproj", $proj)

git add CHANGELOG.md src/Lumina/Lumina.csproj
git commit -m "Release $Version"
git tag -a "v$Version" -m "Lumina $Version"
Write-Host "Tagged v$Version. Build it with: scripts\package.ps1 -NoKeys -NoFonts"
