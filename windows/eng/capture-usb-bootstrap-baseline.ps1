[CmdletBinding()]
param(
    [string]$InstanceId = "USB\VID_22D9&PID_2764\W4U4SCSSGMIBLJ8H",
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "This baseline capture must run on Windows."
}

if (-not (Get-Command Get-PnpDevice -ErrorAction SilentlyContinue)) {
    throw "Get-PnpDevice is unavailable."
}

$device = Get-PnpDevice -InstanceId $InstanceId -PresentOnly -ErrorAction Stop

function Get-DevicePropertyText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$KeyName
    )

    try {
        $value = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName $KeyName -ErrorAction Stop).Data
        if ($null -eq $value) {
            return $null
        }

        if ($value -is [System.Array]) {
            return @($value | ForEach-Object { $_.ToString() })
        }

        return $value.ToString()
    }
    catch {
        return $null
    }
}

$hardwareIds = @(Get-DevicePropertyText -KeyName "DEVPKEY_Device_HardwareIds")
$expectedHardwareId = "USB\VID_22D9&PID_2764&REV_0404"

if ($hardwareIds -notcontains $expectedHardwareId) {
    throw "Refusing baseline capture for an unexpected device. Expected '$expectedHardwareId'."
}

$service = Get-DevicePropertyText -KeyName "DEVPKEY_Device_Service"
$driverInf = Get-DevicePropertyText -KeyName "DEVPKEY_Device_DriverInfPath"

if ($device.Class -ne "WPD" -or
    $service -ne "WUDFWpdMtp" -or
    $driverInf -ne "wpdmtp.inf") {
    throw "The target is not in the validated no-ADB OPPO A58 WPD/MTP state."
}

$pnpUtil = Join-Path $env:SystemRoot "System32\pnputil.exe"
$stackOutput = & $pnpUtil /enum-devices /instanceid $InstanceId /stack /drivers /services 2>&1

if ($LASTEXITCODE -ne 0) {
    throw "PnPUtil stack capture failed with exit code $LASTEXITCODE."
}

$networkAdapters = @()
if (Get-Command Get-NetAdapter -ErrorAction SilentlyContinue) {
    $networkAdapters = @(
        Get-NetAdapter |
            Select-Object Name, InterfaceDescription, ifIndex, Status, MacAddress, LinkSpeed
    )
}

$ipv4Routes = @()
$ipv6Routes = @()
if (Get-Command Get-NetRoute -ErrorAction SilentlyContinue) {
    $ipv4Routes = @(
        Get-NetRoute -AddressFamily IPv4 |
            Sort-Object DestinationPrefix, RouteMetric |
            Select-Object ifIndex, DestinationPrefix, NextHop, RouteMetric, State, PolicyStore
    )

    $ipv6Routes = @(
        Get-NetRoute -AddressFamily IPv6 |
            Sort-Object DestinationPrefix, RouteMetric |
            Select-Object ifIndex, DestinationPrefix, NextHop, RouteMetric, State, PolicyStore
    )
}

$adbPresent = @(
    Get-PnpDevice -PresentOnly |
        Where-Object {
            $_.InstanceId -like "USB\VID_22D9&PID_2765*" -or
            $_.FriendlyName -match "(?i)ADB"
        }
).Count -gt 0

$secureBoot = $null
if (Get-Command Confirm-SecureBootUEFI -ErrorAction SilentlyContinue) {
    try {
        $secureBoot = Confirm-SecureBootUEFI
    }
    catch {
        $secureBoot = "unavailable: $($_.Exception.Message)"
    }
}

$deviceGuard = $null
try {
    $deviceGuard = Get-CimInstance -Namespace "root\Microsoft\Windows\DeviceGuard" -ClassName "Win32_DeviceGuard" -ErrorAction Stop |
        Select-Object VirtualizationBasedSecurityStatus, SecurityServicesConfigured, SecurityServicesRunning
}
catch {
    $deviceGuard = $null
}

$report = [ordered]@{
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    Purpose = "NATSX USB bootstrap pre-install baseline"
    Windows = [ordered]@{
        Version = [System.Environment]::OSVersion.Version.ToString()
        SecureBoot = $secureBoot
        DeviceGuard = $deviceGuard
    }
    Target = [ordered]@{
        Status = $device.Status
        Class = $device.Class
        FriendlyName = $device.FriendlyName
        InstanceId = $device.InstanceId
        HardwareIds = $hardwareIds
        Service = $service
        DriverInfPath = $driverInf
        DriverProvider = Get-DevicePropertyText -KeyName "DEVPKEY_Device_DriverProvider"
        DriverVersion = Get-DevicePropertyText -KeyName "DEVPKEY_Device_DriverVersion"
        UpperFilters = Get-DevicePropertyText -KeyName "DEVPKEY_Device_UpperFilters"
        LowerFilters = Get-DevicePropertyText -KeyName "DEVPKEY_Device_LowerFilters"
        CompoundUpperFilters = Get-DevicePropertyText -KeyName "DEVPKEY_Device_CompoundUpperFilters"
        CompoundLowerFilters = Get-DevicePropertyText -KeyName "DEVPKEY_Device_CompoundLowerFilters"
        BusReportedDeviceDesc = Get-DevicePropertyText -KeyName "DEVPKEY_Device_BusReportedDeviceDesc"
        PnpUtilStack = ($stackOutput -join [Environment]::NewLine)
        AdbPresent = $adbPresent
    }
    Network = [ordered]@{
        Adapters = $networkAdapters
        IPv4Routes = $ipv4Routes
        IPv6Routes = $ipv6Routes
    }
}

$json = $report | ConvertTo-Json -Depth 8

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $json
    exit 0
}

$parent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

Set-Content -Path $OutputPath -Value $json -Encoding UTF8
Write-Host "Baseline written to: $OutputPath"
