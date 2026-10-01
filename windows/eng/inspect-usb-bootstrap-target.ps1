[CmdletBinding()]
param(
    [string]$InstanceId,
    [switch]$IncludeAllUsb,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

function Get-NatsxPnpProperty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DeviceInstanceId,

        [Parameter(Mandatory = $true)]
        [string]$KeyName
    )

    try {
        return (Get-PnpDeviceProperty -InstanceId $DeviceInstanceId -KeyName $KeyName -ErrorAction Stop).Data
    }
    catch {
        return $null
    }
}

function Convert-NatsxPropertyToText {
    param([object]$Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [System.Array]) {
        return ($Value | ForEach-Object { $_.ToString() }) -join "; "
    }

    return $Value.ToString()
}

if (-not (Get-Command Get-PnpDevice -ErrorAction SilentlyContinue)) {
    throw "Get-PnpDevice is unavailable. Run this inspection script on supported Windows 10/11 PowerShell."
}

$devices =
    if ([string]::IsNullOrWhiteSpace($InstanceId)) {
        # Filter to USB device nodes before querying properties. The old implementation
        # queried eight properties for every present PnP device, which could take a very
        # long time on systems with many device nodes.
        @(
            Get-PnpDevice -PresentOnly |
                Where-Object { $_.InstanceId -like "USB\*" }
        )
    }
    else {
        @(Get-PnpDevice -InstanceId $InstanceId -PresentOnly -ErrorAction Stop)
    }

$report = foreach ($device in $devices) {
    $hardwareIds = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_HardwareIds"
    $compatibleIds = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_CompatibleIds"
    $manufacturer = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_Manufacturer"
    $service = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_Service"
    $parent = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_Parent"
    $driverInfPath = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_DriverInfPath"
    $driverProvider = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_DriverProvider"
    $driverVersion = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_DriverVersion"
    $upperFilters = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_UpperFilters"
    $lowerFilters = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_LowerFilters"
    $compoundUpperFilters = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_CompoundUpperFilters"
    $compoundLowerFilters = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_CompoundLowerFilters"
    $busReportedDesc = Get-NatsxPnpProperty -DeviceInstanceId $device.InstanceId -KeyName "DEVPKEY_Device_BusReportedDeviceDesc"

    $hardwareText = Convert-NatsxPropertyToText $hardwareIds
    $compatibleText = Convert-NatsxPropertyToText $compatibleIds

    $friendlyName =
        if ([string]::IsNullOrWhiteSpace($device.FriendlyName)) {
            $device.InstanceId
        }
        else {
            $device.FriendlyName
        }

    $looksLikeAndroid =
        ($device.Class -eq "WPD") -or
        ($device.Class -eq "AndroidUsbDeviceClass") -or
        ($friendlyName -match "(?i)android|mtp|phone|pixel|samsung|xiaomi|redmi|poco|oppo|vivo|realme|oneplus|motorola|asus|sony|huawei|honor") -or
        ($hardwareText -match "(?i)android|mtp|adb") -or
        ($compatibleText -match "(?i)android|mtp|adb")

    if (
        -not [string]::IsNullOrWhiteSpace($InstanceId) -or
        $IncludeAllUsb -or
        $looksLikeAndroid
    ) {
        [pscustomobject]@{
            Status = $device.Status
            Class = $device.Class
            FriendlyName = $friendlyName
            InstanceId = $device.InstanceId
            Manufacturer = Convert-NatsxPropertyToText $manufacturer
            Service = Convert-NatsxPropertyToText $service
            HardwareIds = $hardwareText
            CompatibleIds = $compatibleText
            Parent = Convert-NatsxPropertyToText $parent
            DriverInfPath = Convert-NatsxPropertyToText $driverInfPath
            DriverProvider = Convert-NatsxPropertyToText $driverProvider
            DriverVersion = Convert-NatsxPropertyToText $driverVersion
            UpperFilters = Convert-NatsxPropertyToText $upperFilters
            LowerFilters = Convert-NatsxPropertyToText $lowerFilters
            CompoundUpperFilters = Convert-NatsxPropertyToText $compoundUpperFilters
            CompoundLowerFilters = Convert-NatsxPropertyToText $compoundLowerFilters
            BusReportedDeviceDesc = Convert-NatsxPropertyToText $busReportedDesc
        }
    }
}

$report = @($report)

if ($report.Count -eq 0) {
    Write-Warning "No likely Android USB data device was found."
    Write-Warning "Confirm the phone is connected with a data-capable cable, then retry with -IncludeAllUsb if necessary."
    exit 2
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 4
}
else {
    $report | Format-List
}
