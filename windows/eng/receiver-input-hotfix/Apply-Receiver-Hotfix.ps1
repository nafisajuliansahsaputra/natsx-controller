[CmdletBinding()]
param(
    [string]$ReceiverPath,
    [switch]$NonInteractive,
    [switch]$NoLaunch,
    [switch]$ValidateOnly,
    [string]$ErrorFile
)
$ErrorActionPreference = "Stop"
try {
    if ([string]::IsNullOrWhiteSpace($ReceiverPath)) {
        if ($NonInteractive) { throw "Receiver path is required." }
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Title = "Select the existing NATSX Receiver"
        $dialog.Filter = "NATSX Receiver|Natsx.Controller.Receiver.exe"
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
        $ReceiverPath = $dialog.FileName
    }
    $exe = Get-Item -LiteralPath $ReceiverPath
    if ($exe.Name -ne "Natsx.Controller.Receiver.exe") { throw "Select Natsx.Controller.Receiver.exe." }
    if (@(Get-Process -Name "Natsx.Controller.Receiver" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Exit NATSX Receiver from its tray menu, then run this script again. The X button may only hide it."
    }
    $names = @("Natsx.Controller.Receiver.dll", "Natsx.Controller.Connection.dll")
    foreach ($name in $names) {
        $source = Join-Path $PSScriptRoot $name
        $target = Join-Path $exe.DirectoryName $name
        if (-not (Test-Path -LiteralPath $source) -or -not (Test-Path -LiteralPath $target)) {
            throw "Missing DLL: $name. Use the extracted patch and an existing complete installation."
        }
        $old = [System.Reflection.AssemblyName]::GetAssemblyName($target)
        $new = [System.Reflection.AssemblyName]::GetAssemblyName($source)
        if ($old.Name -ne $new.Name -or $old.Version -ne $new.Version) {
            throw "Assembly version mismatch for $name. Do not apply this patch to a different receiver release."
        }
    }
    if ($ValidateOnly) { exit 0 }
    $backup = Join-Path $exe.DirectoryName ("input-hotfix-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $backup | Out-Null
    foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $exe.DirectoryName $name) -Destination $backup }
    try {
        foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $exe.DirectoryName $name) -Force }
    } catch {
        foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $exe.DirectoryName $name) -Force }
        throw
    }
    if (-not $NoLaunch) { Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName }
    Write-Host "Receiver updated. Existing pairing, drivers and GamepadHost remain installed."
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($ErrorFile)) {
        [IO.File]::WriteAllText($ErrorFile, $_.Exception.Message)
    }
    if (-not $NonInteractive) { Read-Host "Press Enter to close" }
    exit 1
}
