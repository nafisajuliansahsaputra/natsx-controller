[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$serviceName = "NatsxControllerGamepadHost"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $service -or $service.Status -eq "Stopped") {
    exit 0
}

& sc.exe stop $serviceName | Out-Null

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
do {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($null -eq $service -or $service.Status -eq "Stopped") {
        exit 0
    }
    Start-Sleep -Milliseconds 250
} while ([DateTimeOffset]::UtcNow -lt $deadline)

$wmi = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
if ($null -ne $wmi -and $wmi.ProcessId -gt 0) {
    Stop-Process -Id $wmi.ProcessId -Force -ErrorAction Stop
    Start-Sleep -Milliseconds 500
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $service.Status -ne "Stopped") {
    throw "Unable to stop the existing NATSX gamepad-host service before upgrade."
}
