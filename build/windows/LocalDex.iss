#ifndef AppVersion
  #define AppVersion "2.1.0-localdex.12"
#endif
#ifndef PackageDir
  #define PackageDir "..\..\dist\LocalDex"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

[Setup]
AppId={{A494F4A9-5864-47F0-9436-75D62B2C6D65}
AppName=LocalDex
AppVersion={#AppVersion}
AppPublisher=happy4ed
AppPublisherURL=https://github.com/happy4ed/LocalDex
AppSupportURL=https://github.com/happy4ed/LocalDex/issues
AppUpdatesURL=https://github.com/happy4ed/LocalDex/releases
DefaultDirName={localappdata}\Programs\LocalDex
DefaultGroupName=LocalDex
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=LocalDex-v{#AppVersion}-win-x64-setup
SetupIconFile=..\..\DexManager\Resources\DXManager.ico
UninstallDisplayIcon={app}\LocalDex.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
AppMutex=DexManager-73D79582-CC69-4AEC-A24E-F3755E77A32C
CloseApplications=no
RestartApplications=no
UninstallDisplayName=LocalDex

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Excludes: "config\settings.json*,config\freeform-recovery\*,logs\*,screenshot\*,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LocalDex"; Filename: "{app}\LocalDex.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\LocalDex"; Filename: "{app}\LocalDex.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\LocalDex.exe"; Description: "{cm:LaunchProgram,LocalDex}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := not CheckForMutexes('DexManager-73D79582-CC69-4AEC-A24E-F3755E77A32C');
  if not Result then
    MsgBox('Close LocalDex and its desktop windows before uninstalling. LocalDex must restore the phone settings first.', mbError, MB_OK);
end;
