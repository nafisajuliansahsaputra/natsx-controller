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

[Run]
Filename: "{app}\\Natsx.Controller.Receiver.exe"; Description: "Launch NATSX Controller"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\\Natsx.Controller.Receiver.exe"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "NatsxControllerUserCleanup"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if not Exec(
      ExpandConstant('{app}\\Natsx.Controller.Receiver.exe'),
      '--install-driver',
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) then
    begin
      RaiseException('Unable to start the NATSX virtual-controller driver bootstrap.');
    end;

    if ResultCode <> 0 then
    begin
      RaiseException(
        'NATSX virtual-controller driver bootstrap failed with exit code ' +
        IntToStr(ResultCode) +
        '. See %ProgramData%\\NATSX\\Controller\\setup-driver-error.log.'
      );
    end;
  end;
end;
