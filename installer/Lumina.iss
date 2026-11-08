; Lumina installer. Per-user: no administrator rights, installs to %LOCALAPPDATA%\Programs\Lumina.
; Build with scripts\package.ps1 (it publishes first and passes the version in).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

#define AppId "{B6D1F5E2-6C1B-4B7D-9E64-6A1E5C0E5F10}"

[Setup]
AppId={{#AppId}
AppName=Lumina
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=Lumina
DefaultDirName={autopf}\Lumina
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=Lumina-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Lumina.exe
UninstallDisplayName=Lumina
WizardStyle=modern
SetupIconFile=..\assets\brand\app-icon\lumina.ico
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "assoc"; Description: "Show Lumina in the Open with list for video files"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Lumina"; Filename: "{app}\Lumina.exe"
Name: "{autodesktop}\Lumina"; Filename: "{app}\Lumina.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Lumina.exe"; Parameters: "--register-types"; Tasks: assoc; Flags: runhidden waituntilterminated
Filename: "{app}\Lumina.exe"; Description: "Start Lumina"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Lumina.exe"; Parameters: "--unregister-types"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterTypes"

[Code]
var
  PurgeData: Boolean;

// "1.2.3" -> 1002003-style number so versions compare by value, not as text
function PartAt(const V: String; Index: Integer): Integer;
var
  I, N, Cur: Integer;
  Part: String;
begin
  Result := 0;
  Part := '';
  N := 0;
  for I := 1 to Length(V) + 1 do
  begin
    if (I > Length(V)) or (V[I] = '.') then
    begin
      if N = Index then
      begin
        Cur := StrToIntDef(Part, 0);
        Result := Cur;
        Exit;
      end;
      N := N + 1;
      Part := '';
    end
    else
      Part := Part + V[I];
  end;
end;

function CompareVersions(const A, B: String): Integer;
var
  I, X, Y: Integer;
begin
  Result := 0;
  for I := 0 to 2 do
  begin
    X := PartAt(A, I);
    Y := PartAt(B, I);
    if X < Y then begin Result := -1; Exit; end;
    if X > Y then begin Result := 1; Exit; end;
  end;
end;

function InstalledVersion(): String;
var
  Key, V: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1';
  Result := '';
  if RegQueryStringValue(HKCU, Key, 'DisplayVersion', V) then Result := V
  else if RegQueryStringValue(HKLM, Key, 'DisplayVersion', V) then Result := V;
end;

// Refuse to replace a newer Lumina with an older one: the data may have been upgraded and could not be read back.
function InitializeSetup(): Boolean;
var
  Have: String;
begin
  Result := True;
  Have := InstalledVersion();
  if (Have <> '') and (CompareVersions(Have, '{#AppVersion}') > 0) then
  begin
    if not WizardSilent then
      MsgBox('Lumina ' + Have + ' is already installed. This installer is for the older version {#AppVersion}, so it will not replace it.', mbInformation, MB_OK);
    Result := False;
  end;
end;

// The question about your data is asked once, before anything is removed, and defaults to keeping it.
function InitializeUninstall(): Boolean;
begin
  Result := True;
  PurgeData := ExpandConstant('{param:PURGE|0}') = '1';
  if (not PurgeData) and (not UninstallSilent) then
    PurgeData := MsgBox('Also delete your Lumina settings, library, watch history and saved logins?' + #13#10 + #13#10 +
      'Choose No to keep them in case you install Lumina again.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
  Root: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // logins live in Windows Credential Manager, so the program has to remove them while it is still on disk
    if PurgeData then
      Exec(ExpandConstant('{app}\Lumina.exe'), '--forget-logins', '', SW_HIDE, ewWaitUntilTerminated, Code);
  end;
  if (CurUninstallStep = usPostUninstall) and PurgeData then
  begin
    // /DATAROOT=<folder> points the clean-up at a test folder instead of the real profile
    Root := ExpandConstant('{param:DATAROOT|}');
    if Root = '' then
    begin
      DelTree(ExpandConstant('{userappdata}\Lumina'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\Lumina'), True, True, True);
    end
    else
    begin
      DelTree(Root + '\Roaming\Lumina', True, True, True);
      DelTree(Root + '\Local\Lumina', True, True, True);
    end;
  end;
end;
