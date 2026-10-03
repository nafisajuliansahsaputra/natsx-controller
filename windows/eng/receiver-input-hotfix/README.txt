ARCHIVED: Legacy Receiver-only patch tooling (existing 0.1.1 installation)

Do not use this patch with current NATSX builds. It leaves GamepadHost and its
input mapping unchanged. Its automatic packaging workflow has been removed.
Use complete NATSX Receiver Setup to update both components and preserve pairing.
Current Receiver requires input mapping revision 2 from GamepadHost.
The instructions below are retained only as historical documentation.

Easy Windows application:
1. Double-click NATSX-Receiver-Input-Update.exe and approve the Windows prompt.
2. Exit the running receiver through its tray menu > Exit.
3. Select the existing receiver folder (or browse to its EXE when prompted).
4. Click Install. Leave "Buka NATSX Receiver" selected to reopen it.

The EXE contains its DLL payload and requires no manual extraction or script commands.
It checks the existing assembly identities/versions, backs up both DLLs and restores
them if copying fails. The receiver is launched as the original unelevated user.

This patch updates the receiver and connection DLLs. It does not replace drivers
or the privileged GamepadHost service. Keep every other installed file.

1. Extract this ZIP.
2. Right-click Apply-Receiver-Hotfix.ps1, Run with PowerShell.
3. Select your existing Natsx.Controller.Receiver.exe.
4. If the receiver is running, the script asks you to Exit it from the tray.
5. Existing DLLs are backed up, replaced, and the receiver opens again.

Manual option: Exit the receiver completely, copy the two DLLs beside its EXE,
replacing the old DLLs, and open the EXE again. Use administrator PowerShell only
if your existing installation directory is protected. Pairing data is retained.
