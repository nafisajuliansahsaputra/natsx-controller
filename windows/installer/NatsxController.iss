#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#ifndef PublishDir
  #define PublishDir "..\..\artifacts\windows-receiver"
#endif

#ifndef IncludeUsbDrivers
  #define IncludeUsbDrivers "0"
#endif

#ifndef BootstrapDriverDir
  #define BootstrapDriverDir "."
#endif

#ifndef WinUsbDriverDir
  #define WinUsbDriverDir "."
#endif

[Setup]
AppId={{6C2A8D3B-1D1C-4B91-9C15-4C8A6F1A9E12}
AppName=NATSX Controller
AppVersion={#MyAppVersion}
AppPublisher=NATSX
DefaultDirName={autopf}\NATSX Controller
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
UninstallDisplayIcon={app}\Natsx.Controller.Receiver.exe

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "install-production-usb-drivers.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "remove-production-usb-drivers.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "install-firewall-rules.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "remove-firewall-rules.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "install-gamepad-host.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "remove-gamepad-host.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
#if IncludeUsbDrivers == "1"
Source: "{#BootstrapDriverDir}\*"; DestDir: "{app}\drivers\aoa-bootstrap"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WinUsbDriverDir}\*"; DestDir: "{app}\drivers\aoa-winusb"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\NATSX Controller"; Filename: "{app}\Natsx.Controller.Receiver.exe"
Name: "{autodesktop}\NATSX Controller"; Filename: "{app}\Natsx.Controller.Receiver.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\Natsx.Controller.Receiver.exe"; Description: "Launch NATSX Controller"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sysnative}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\tools\remove-gamepad-host.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "NatsxControllerGamepadHostCleanup"
Filename: "{sysnative}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\tools\remove-firewall-rules.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "NatsxControllerFirewallCleanup"
Filename: "{sysnative}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\tools\remove-production-usb-drivers.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "NatsxControllerUsbDriverCleanup"
Filename: "{app}\Natsx.Controller.Receiver.exe"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "NatsxControllerUserCleanup"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure RunCleanupScript(const ScriptName: String);
var
  CleanupCode: Integer;
begin
  Exec(
    ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
    ExpandConstant('-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{app}\tools\' + ScriptName + '"'),
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    CleanupCode
  );
end;

procedure RollbackPostInstallSideEffects();
begin
  RunCleanupScript('remove-gamepad-host.ps1');
  RunCleanupScript('remove-firewall-rules.ps1');
#if IncludeUsbDrivers == "1"
  RunCleanupScript('remove-production-usb-drivers.ps1');
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
#if IncludeUsbDrivers == "1"
    if not Exec(
      ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
      ExpandConstant('-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{app}\tools\install-production-usb-drivers.ps1" -AppRoot "{app}"'),
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) then
    begin
      RaiseException('Unable to start NATSX production USB driver installation.');
    end;

    if ResultCode <> 0 then
    begin
      RollbackPostInstallSideEffects();
      RaiseException(
        'NATSX production USB driver installation failed with exit code ' +
        IntToStr(ResultCode) + '.'
      );
    end;
#endif

    if not Exec(
      ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
      ExpandConstant('-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{app}\tools\install-firewall-rules.ps1" -AppRoot "{app}"'),
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) then
    begin
      RollbackPostInstallSideEffects();
      RaiseException('Unable to start NATSX Windows Firewall configuration.');
    end;

    if ResultCode <> 0 then
    begin
      RollbackPostInstallSideEffects();
      RaiseException(
        'NATSX Windows Firewall configuration failed with exit code ' +
        IntToStr(ResultCode) + '.'
      );
    end;

    if not Exec(
      ExpandConstant('{app}\Natsx.Controller.Receiver.exe'),
      '--install-driver',
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) then
    begin
      RollbackPostInstallSideEffects();
      RaiseException('Unable to start the NATSX virtual-controller driver bootstrap.');
    end;

    if ResultCode <> 0 then
    begin
      RollbackPostInstallSideEffects();
      RaiseException(
        'NATSX virtual-controller driver bootstrap failed with exit code ' +
        IntToStr(ResultCode) +
        '. See %ProgramData%\NATSX\Controller\setup-driver-error.log.'
      );
    end;

    if not Exec(
      ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
      ExpandConstant('-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{app}\tools\install-gamepad-host.ps1" -AppRoot "{app}"'),
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) then
    begin
      RollbackPostInstallSideEffects();
      RaiseException('Unable to start NATSX privileged gamepad-host service installation.');
    end;

    if ResultCode <> 0 then
    begin
      RollbackPostInstallSideEffects();
      RaiseException(
        'NATSX privileged gamepad-host service installation failed with exit code ' +
        IntToStr(ResultCode) + '.'
      );
    end;

    if not ExecAsOriginalUser(
      ExpandConstant('{app}\Natsx.Controller.Receiver.exe'),
      '--verify-gamepad-host',
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) then
    begin
      RollbackPostInstallSideEffects();
      RaiseException('Unable to run the unelevated NATSX gamepad-host verification.');
    end;

    if ResultCode <> 0 then
    begin
      RollbackPostInstallSideEffects();
      RaiseException(
        'The unelevated NATSX Receiver could not use the privileged gamepad host. ' +
        'Verification exit code: ' + IntToStr(ResultCode) +
        '. See %ProgramData%\NATSX\Controller\setup-driver-error.log.'
      );
    end;
  end;
end;
