[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/release-evidence/windows",
    [string]$ReceiverProcessName = "Natsx.Controller.Receiver"
)

$ErrorActionPreference = "Stop"

$output =
    if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory
    }
    else {
        Join-Path (Get-Location) $OutputDirectory
    }

New-Item -ItemType Directory -Path $output -Force |
    Out-Null

function Get-SecureBootState {
    try {
        return [bool](Confirm-SecureBootUEFI)
    }
    catch {
        return $null
    }
}

function Get-TestSigningState {
    $output =
        & bcdedit /enum "{current}" 2>$null |
            Out-String

    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    if ($output -match '(?im)^\s*testsigning\s+Yes\s*$') {
        return $true
    }

    if ($output -match '(?im)^\s*testsigning\s+No\s*$') {
        return $false
    }

    return $null
}

function Get-DriverInventory {
    $entries =
        @(
            & pnputil /enum-drivers 2>$null
        )

    if ($LASTEXITCODE -ne 0) {
        return @()
    }

    $text =
        $entries -join [Environment]::NewLine

    $blocks =
        $text -split '(?:\r?\n){2,}'

    return @(
        $blocks |
            Where-Object {
                $_ -match '(?i)Natsx\.AoaBootstrap|NatsxAoaWinUsb|NATSX'
            } |
            ForEach-Object {
                $_.Trim()
            }
    )
}

function Get-ReceiverSnapshot {
    $process =
        Get-Process -Name $ReceiverProcessName -ErrorAction SilentlyContinue |
            Select-Object -First 1

    if ($null -eq $process) {
        return $null
    }

    $startTimeUtc =
        try {
            $process.StartTime.ToUniversalTime().ToString("O")
        }
        catch {
            $null
        }

    return [ordered]@{
        ProcessId =
            $process.Id
        StartTimeUtc =
            $startTimeUtc
        WorkingSetMiB =
            [Math]::Round(
                $process.WorkingSet64 /
                1MB,
                3)
        PrivateMemoryMiB =
            [Math]::Round(
                $process.PrivateMemorySize64 /
                1MB,
                3)
        ThreadCount =
            $process.Threads.Count
        HandleCount =
            $process.HandleCount
    }
}

$os =
    Get-CimInstance Win32_OperatingSystem

$computer =
    Get-CimInstance Win32_ComputerSystem

$bios =
    Get-CimInstance Win32_BIOS

$evidence =
    [ordered]@{
        CollectedAtUtc =
            [DateTimeOffset]::UtcNow.ToString("O")
        Computer =
            [ordered]@{
                Manufacturer =
                    $computer.Manufacturer
                Model =
                    $computer.Model
                TotalMemoryGiB =
                    [Math]::Round(
                        $computer.TotalPhysicalMemory /
                        1GB,
                        2)
            }
        Windows =
            [ordered]@{
                Caption =
                    $os.Caption
                Version =
                    $os.Version
                BuildNumber =
                    $os.BuildNumber
                Architecture =
                    $os.OSArchitecture
            }
        Bios =
            [ordered]@{
                Manufacturer =
                    $bios.Manufacturer
                Version =
                    $bios.SMBIOSBIOSVersion
            }
        Security =
            [ordered]@{
                SecureBootEnabled =
                    Get-SecureBootState
                TestSigningEnabled =
                    Get-TestSigningState
            }
        NatsxDriverInventory =
            Get-DriverInventory
        Receiver =
            Get-ReceiverSnapshot
    }

$evidencePath =
    Join-Path $output "windows-environment.json"

$evidence |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $evidencePath -Encoding UTF8

$driverTextPath =
    Join-Path $output "pnputil-enum-drivers.txt"

& pnputil /enum-drivers |
    Set-Content -LiteralPath $driverTextPath -Encoding UTF8

$deviceTextPath =
    Join-Path $output "pnputil-enum-devices-connected.txt"

& pnputil /enum-devices /connected |
    Set-Content -LiteralPath $deviceTextPath -Encoding UTF8

Write-Host "Release evidence written to:"
Write-Host "  $output"

if ($evidence.Security.SecureBootEnabled -eq $false) {
    Write-Warning "Secure Boot is disabled. Retail clean-machine acceptance requires Secure Boot enabled."
}

if ($evidence.Security.TestSigningEnabled -eq $true) {
    Write-Warning "TESTSIGNING is enabled. Retail clean-machine acceptance requires TESTSIGNING disabled."
}
