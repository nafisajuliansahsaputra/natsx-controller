#ifndef PatchDir
  #define PatchDir "..\..\..\artifacts\receiver-input-hotfix"
#endif

[Setup]
AppId=NatsxReceiverInputUpdate
AppName=NATSX Receiver Input Update
AppVersion=0.1.1
AppPublisher=NATSX
DefaultDirName={autopf}\NATSX Controller
UsePreviousAppDir=no
DisableDirPage=no
DisableProgramGroupPage=yes
DirExistsWarning=no
CreateAppDir=yes
Uninstallable=no
CreateUninstallRegKey=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=NATSX-Receiver-Input-Update
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no

[Files]
Source: "{#PatchDir}\Natsx.Controller.Receiver.dll"; Flags: dontcopy
Source: "{#PatchDir}\Natsx.Controller.Connection.dll"; Flags: dontcopy
Source: "{#PatchDir}\Apply-Receiver-Hotfix.ps1"; Flags: dontcopy

[Run]
Filename: "{code:ReceiverDir}\Natsx.Controller.Receiver.exe"; WorkingDir: "{code:ReceiverDir}"; Description: "Buka NATSX Receiver"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  PayloadReady: Boolean;

function ReceiverDir(Param: String): String;
begin
  Result := WizardDirValue;
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption :=
    'Update receiver untuk memperbaiki input setelah pairing.' + #13#10 + #13#10 +
    'Tutup receiver melalui ikon tray > Exit. Pilih folder receiver yang sekarang dipakai, lalu klik Install.' + #13#10 + #13#10 +
    'File lama dibackup otomatis. Pairing dan driver tetap tersimpan.';
  WizardForm.SelectDirLabel.Caption :=
    'Pilih folder yang berisi Natsx.Controller.Receiver.exe.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ReceiverFile: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then begin
    ReceiverFile := AddBackslash(WizardDirValue) + 'Natsx.Controller.Receiver.exe';
    if not FileExists(ReceiverFile) then begin
      if GetOpenFileName('Pilih NATSX Receiver yang sekarang dipakai', ReceiverFile,
          WizardDirValue, 'NATSX Receiver|Natsx.Controller.Receiver.exe', 'exe') then
        WizardForm.DirEdit.Text := ExtractFileDir(ReceiverFile)
      else begin
        Result := False;
        exit;
      end;
    end;
    Result := FileExists(AddBackslash(WizardDirValue) + 'Natsx.Controller.Receiver.exe');
  end;
end;

function ApplyPatch(ValidateOnly: Boolean): String;
var
  Params, ErrorFile: String;
  ErrorText: AnsiString;
  Code: Integer;
begin
  Result := '';
  if not PayloadReady then begin
    ExtractTemporaryFile('Natsx.Controller.Receiver.dll');
    ExtractTemporaryFile('Natsx.Controller.Connection.dll');
    ExtractTemporaryFile('Apply-Receiver-Hotfix.ps1');
    PayloadReady := True;
  end;
  ErrorFile := ExpandConstant('{tmp}\natsx-update-error.txt');
  DeleteFile(ErrorFile);
  Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Apply-Receiver-Hotfix.ps1') + '" -NonInteractive -NoLaunch -ReceiverPath "' +
    AddBackslash(WizardDirValue) + 'Natsx.Controller.Receiver.exe' + '" -ErrorFile "' + ErrorFile + '"';
  if ValidateOnly then Params := Params + ' -ValidateOnly';
  if not Exec(ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
      Params, ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := 'Windows tidak dapat menjalankan pemasangan update.'
  else if Code <> 0 then begin
    if LoadStringFromFile(ErrorFile, ErrorText) then Result := ErrorText
    else Result := 'Update gagal. Tutup receiver melalui tray > Exit, lalu coba lagi.';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := ApplyPatch(True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorText: String;
begin
  if CurStep = ssInstall then begin
    ErrorText := ApplyPatch(False);
    if ErrorText <> '' then RaiseException(ErrorText);
  end;
end;
