$ErrorActionPreference = 'Stop'
Install-ChocolateyPackage -PackageName 'lumina' -FileType 'exe' `
  -Url64bit 'https://github.com/nomoron28/lumina/releases/download/v0.1.1/Lumina-0.1.1-setup.exe' `
  -Checksum64 'ABBBE707488EF4A0B07313A9349FE48E72F62DD08A0885C38D3DA01D8C2D6A8E' -ChecksumType64 'sha256' `
  -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS' -ValidExitCodes @(0)
