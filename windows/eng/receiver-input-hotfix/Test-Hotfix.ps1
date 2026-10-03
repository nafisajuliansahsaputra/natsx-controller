param([string]$UpdateExe)
$ErrorActionPreference = 'Stop'
$payload = (Resolve-Path 'artifacts/receiver-input-hotfix').Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('natsx-hotfix-test-' + [Guid]::NewGuid().ToString('N'))
$names = @('Natsx.Controller.Receiver.dll', 'Natsx.Controller.Connection.dll')
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $receiver = Join-Path $testRoot 'Natsx.Controller.Receiver.exe'
    Copy-Item 'windows/src/Natsx.Controller.Receiver/bin/Release/net10.0-windows10.0.19041.0/Natsx.Controller.Receiver.exe' $receiver
    foreach ($name in $names) { Copy-Item (Join-Path $payload $name) $testRoot }
    $script = Join-Path $payload 'Apply-Receiver-Hotfix.ps1'
    $shell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $errorFile = Join-Path $testRoot 'error.txt'
    & $shell -NoProfile -NonInteractive -File $script -ReceiverPath $receiver -NonInteractive -NoLaunch -ValidateOnly -ErrorFile $errorFile
    if ($LASTEXITCODE -ne 0 -or @(Get-ChildItem $testRoot -Directory).Count -ne 0) { throw 'Validation failed or mutated installation.' }
    Remove-Item (Join-Path $testRoot $names[1])
    & $shell -NoProfile -NonInteractive -File $script -ReceiverPath $receiver -NonInteractive -NoLaunch -ErrorFile $errorFile
    if ($LASTEXITCODE -eq 0 -or -not (Test-Path $errorFile) -or @(Get-ChildItem $testRoot -Directory).Count -ne 0) { throw 'Missing DLL was not rejected before backup.' }
    Copy-Item (Join-Path $payload $names[0]) (Join-Path $testRoot $names[1])
    & $shell -NoProfile -NonInteractive -File $script -ReceiverPath $receiver -NonInteractive -NoLaunch -ErrorFile $errorFile
    if ($LASTEXITCODE -eq 0) { throw 'Assembly identity mismatch was not rejected.' }
    Copy-Item (Join-Path $payload $names[1]) $testRoot -Force
    foreach ($name in $names) {
        $stream = [IO.File]::Open((Join-Path $testRoot $name), [IO.FileMode]::Append)
        try { $stream.WriteByte(42) } finally { $stream.Dispose() }
    }
    $oldHashes = @{}
    foreach ($name in $names) { $oldHashes[$name] = (Get-FileHash (Join-Path $testRoot $name)).Hash }
    $lock = [IO.File]::Open((Join-Path $testRoot $names[1]), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        & $shell -NoProfile -NonInteractive -File $script -ReceiverPath $receiver -NonInteractive -NoLaunch -ErrorFile $errorFile
        if ($LASTEXITCODE -eq 0) { throw 'Locked target should fail.' }
    } finally { $lock.Dispose() }
    foreach ($name in $names) {
        if ((Get-FileHash (Join-Path $testRoot $name)).Hash -ne $oldHashes[$name]) { throw "Rollback changed $name." }
    }
    & $shell -NoProfile -NonInteractive -File $script -ReceiverPath $receiver -NonInteractive -NoLaunch -ErrorFile $errorFile
    if ($LASTEXITCODE -ne 0) { throw 'Valid update failed.' }
    $backups = @(Get-ChildItem $testRoot -Directory)
    if ($backups.Count -ne 2) { throw 'Failure and successful update backups missing.' }
    foreach ($name in $names) {
        if ((Get-FileHash (Join-Path $testRoot $name)).Hash -ne (Get-FileHash (Join-Path $payload $name)).Hash) { throw "Incorrect payload: $name" }
        foreach ($backup in $backups) {
            if ((Get-FileHash (Join-Path $backup.FullName $name)).Hash -ne $oldHashes[$name]) { throw 'Original backup corrupted.' }
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($UpdateExe)) {
        foreach ($name in $names) {
            $stream = [IO.File]::Open((Join-Path $testRoot $name), [IO.FileMode]::Append)
            try { $stream.WriteByte(43) } finally { $stream.Dispose() }
        }
        $installer = Start-Process -FilePath (Resolve-Path $UpdateExe).Path -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-',
            ('/DIR="' + $testRoot + '"'), ('/LOG="' + (Join-Path $testRoot 'setup.log') + '"')
        ) -Wait -PassThru
        if ($installer.ExitCode -ne 0) {
            Get-Content (Join-Path $testRoot 'setup.log') -ErrorAction SilentlyContinue
            throw "Update EXE failed: $($installer.ExitCode)."
        }
        foreach ($name in $names) {
            if ((Get-FileHash (Join-Path $testRoot $name)).Hash -ne (Get-FileHash (Join-Path $payload $name)).Hash) { throw "EXE failed to update $name." }
        }
        if (@(Get-ChildItem $testRoot -Directory).Count -ne 3) { throw 'EXE did not create backup.' }
        if (Test-Path (Join-Path $testRoot 'unins000.exe')) { throw 'Update must not replace the existing uninstaller.' }
        Write-Host 'PASS: compiled EXE applies the embedded update and creates a backup.'
    }
    Write-Host 'PASS: validation, missing DLL, identity mismatch, write failure rollback, backup and apply.'
} finally { Remove-Item $testRoot -Recurse -Force }
