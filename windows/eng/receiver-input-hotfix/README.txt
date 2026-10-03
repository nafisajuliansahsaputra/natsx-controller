NATSX Receiver input hotfix (existing 0.1.1 installation)

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
