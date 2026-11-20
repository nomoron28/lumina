$ErrorActionPreference = 'Stop'
Install-ChocolateyPackage -PackageName 'lumina' -FileType 'exe' `
  -Url64bit 'https://github.com/nomoron28/lumina/releases/download/v0.2.0/Lumina-0.2.0-setup.exe' `
  -Checksum64 'F9074822738E5C979C26639BC44C8F80DB4CBB4A4D7155C11C4AB5567C0815D7' -ChecksumType64 'sha256' `
  -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS' -ValidExitCodes @(0)
