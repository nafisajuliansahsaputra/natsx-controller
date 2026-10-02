[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppRoot
)

$ErrorActionPreference = "Stop"

$serviceName = "NatsxControllerGamepadHost"
$displayName = "NATSX Controller Gamepad Host"
$description = "Minimal privileged host for the NATSX Xbox 360 virtual controller. Network and transport parsing remain in the unelevated Receiver."
$stateDirectory = Join-Path $env:ProgramData "NATSX\Controller"
$logPath = Join-Path $stateDirectory "gamepad-host-install.log"
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null

if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    $logInfo = Get-Item -LiteralPath $logPath

    if ($logInfo.Length -gt 524288) {
        Remove-Item -LiteralPath $logPath -Force
    }
}

function Write-InstallLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Add-Content -LiteralPath $logPath -Value (
        "[$([DateTimeOffset]::UtcNow.ToString('O'))] " +
        $Message)
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExpectedStatus,

        [int]$TimeoutSeconds = 20
    )

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds(
            $TimeoutSeconds)

    do {
        $service =
            Get-Service -Name $serviceName -ErrorAction SilentlyContinue

        if ($null -ne $service -and
            [string]$service.Status -eq $ExpectedStatus) {
            return
        }

        Start-Sleep -Milliseconds 250
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Service '$serviceName' did not reach state '$ExpectedStatus'."
}

function Stop-GamepadHost {
    $service =
        Get-Service -Name $serviceName -ErrorAction SilentlyContinue

    if ($null -eq $service -or
        $service.Status -eq "Stopped") {
        return
    }

    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue

    try {
        Wait-ServiceState -ExpectedStatus "Stopped" -TimeoutSeconds 15
    }
    catch {
        $serviceCim =
            Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue

        if ($null -ne $serviceCim -and
            $serviceCim.ProcessId -gt 0) {
            Stop-Process -Id $serviceCim.ProcessId -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
        }
    }
}

function Change-ServiceConfiguration {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathName,

        [Parameter(Mandatory = $true)]
        [string]$StartName
    )

    $serviceCim =
        Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop

    $result =
        Invoke-CimMethod -InputObject $serviceCim -MethodName Change -Arguments @{
            DisplayName = $displayName
            PathName = $PathName
            StartMode = "Automatic"
            StartName = $StartName
        }

    if ([int]$result.ReturnValue -ne 0) {
        throw "Win32_Service.Change failed with return value $($result.ReturnValue)."
    }
}

function Configure-ServiceRecovery {
    $output =
        & sc.exe failure $serviceName reset= 86400 actions= restart/2000/restart/5000/restart/10000 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw (
            "sc.exe failure failed with exit code $LASTEXITCODE. " +
            ($output -join " | "))
    }

    $output =
        & sc.exe failureflag $serviceName 1 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw (
            "sc.exe failureflag failed with exit code $LASTEXITCODE. " +
            ($output -join " | "))
    }
}

function Remove-NewServiceRegistration {
    $serviceCim =
        Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue

    if ($null -eq $serviceCim) {
        return
    }

    $result =
        Invoke-CimMethod -InputObject $serviceCim -MethodName Delete

    if ([int]$result.ReturnValue -ne 0) {
        Write-InstallLog (
            "Rollback service delete returned $($result.ReturnValue).")
    }
}

$appRootPath =
    (Resolve-Path -LiteralPath $AppRoot).Path

$programFilesPath =
    [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFiles)

$programFilesRoot =
    [IO.Path]::GetFullPath(
        $programFilesPath).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)

$appRootFull =
    [IO.Path]::GetFullPath(
        $appRootPath).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)

$requiredPrefix =
    $programFilesRoot +
    [IO.Path]::DirectorySeparatorChar

if (-not $appRootFull.StartsWith(
        $requiredPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw (
        "Refusing to register a LocalSystem service from outside Program Files. " +
        "AppRoot='$appRootFull', required root='$programFilesRoot'.")
}

$hostPath =
    Join-Path $appRootFull "Natsx.Controller.GamepadHost.exe"

if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw "Privileged gamepad-host executable is missing: $hostPath"
}

$serviceCommand =
    '"' +
    $hostPath +
    '" --service'

Write-InstallLog (
    "Installing/updating gamepad host from '$hostPath'.")

$existing =
    Get-Service -Name $serviceName -ErrorAction SilentlyContinue

$existingCim =
    Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue

$createdNew =
    $null -eq $existing

$previousPath =
    if ($null -ne $existingCim) {
        [string]$existingCim.PathName
    }
    else {
        $null
    }

$previousStartName =
    if ($null -ne $existingCim -and
        -not [string]::IsNullOrWhiteSpace(
            [string]$existingCim.StartName)) {
        [string]$existingCim.StartName
    }
    else {
        "LocalSystem"
    }

try {
    if ($createdNew) {
        New-Service `
            -Name $serviceName `
            -BinaryPathName $serviceCommand `
            -DisplayName $displayName `
            -StartupType Automatic |
            Out-Null
    }
    else {
        Stop-GamepadHost

        Change-ServiceConfiguration `
            -PathName $serviceCommand `
            -StartName "LocalSystem"
    }

    $serviceRegistryPath =
        "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"

    New-ItemProperty `
        -LiteralPath $serviceRegistryPath `
        -Name "Description" `
        -Value $description `
        -PropertyType String `
        -Force |
        Out-Null

    Configure-ServiceRecovery

    Start-Service -Name $serviceName -ErrorAction Stop
    Wait-ServiceState -ExpectedStatus "Running" -TimeoutSeconds 20

    $service =
        Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop

    if ($service.StartMode -ne "Auto") {
        throw "Gamepad-host service is not configured for automatic startup."
    }

    if ($service.StartName -ne "LocalSystem") {
        throw "Gamepad-host service is not running under LocalSystem."
    }

    if ([string]$service.PathName -notlike
        "*Natsx.Controller.GamepadHost.exe*--service*") {
        throw "Gamepad-host service command line is incorrect."
    }

    Write-InstallLog (
        "Gamepad-host service verified Running; " +
        "StartMode=$($service.StartMode); " +
        "StartName=$($service.StartName); " +
        "PathName=$($service.PathName)")

    Write-Host "NATSX privileged gamepad-host service installed and running."
}
catch {
    Write-InstallLog (
        "Gamepad-host install/update failed: " +
        $_.Exception.ToString())

    Stop-GamepadHost

    if ($createdNew) {
        Remove-NewServiceRegistration
    }
    elseif (-not [string]::IsNullOrWhiteSpace(
                $previousPath)) {
        try {
            Change-ServiceConfiguration `
                -PathName $previousPath `
                -StartName $previousStartName

            Start-Service -Name $serviceName -ErrorAction SilentlyContinue
        }
        catch {
            Write-InstallLog (
                "Unable to restore previous gamepad-host service configuration: " +
                $_.Exception.ToString())
        }
    }

    throw
}
