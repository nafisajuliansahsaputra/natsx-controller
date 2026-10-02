#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#ifndef PublishDir
  #define PublishDir "..\\..\\artifacts\\windows-receiver"
#endif

[Setup]
AppId={{6C2A8D3B-1D1C-4B91-9C15-4C8A6F1A9E12}
AppName=NATSX Controller
AppVersion={#MyAppVersion}
AppPublisher=NATSX
DefaultDirName={autopf}\\NATSX Controller
DefaultGroupName=NATSX Controller
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=natsx-controller-windows-x64-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\\Natsx.Controller.Receiver.exe

[Files]
Source: "{#PublishDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\\NATSX Controller"; Filename: "{app}\\Natsx.Controller.Receiver.exe"
Name: "{autodesktop}\\NATSX Controller"; Filename: "{app}\\Natsx.Controller.Receiver.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\\Microsoft\\Windows\\CurrentVersion\\Run"; ValueName: "NATSX Controller Receiver"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\\Natsx.Controller.Receiver.exe"; Description: "Launch NATSX Controller"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
