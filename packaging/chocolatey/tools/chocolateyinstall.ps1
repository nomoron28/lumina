$ErrorActionPreference = 'Stop'
Install-ChocolateyPackage -PackageName 'lumina' -FileType 'exe' `
  -Url64bit 'https://github.com/nomoron28/lumina/releases/download/v0.3.0/Lumina-0.3.0-setup.exe' `
  -Checksum64 'BA0E3FFDC951D106BB9E1E11ADA820E483BB99A70587B75D8EC42C3BB44161BD' -ChecksumType64 'sha256' `
  -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS' -ValidExitCodes @(0)
