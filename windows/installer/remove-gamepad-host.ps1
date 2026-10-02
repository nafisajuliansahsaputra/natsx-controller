[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$serviceName = "NatsxControllerGamepadHost"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    Write-Host "NATSX privileged gamepad-host service is already absent."
    exit 0
}

if ($service.Status -ne "Stopped") {
    & sc.exe stop $serviceName | Out-Null

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($null -eq $service -or $service.Status -eq "Stopped") {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
}

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue

if ($null -ne $service -and $service.State -ne "Stopped" -and $service.ProcessId -gt 0) {
    Stop-Process -Id $service.ProcessId -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

& sc.exe delete $serviceName | Out-Null

if ($LASTEXITCODE -ne 0) {
    throw "Failed to delete NATSX privileged gamepad-host service."
}

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
do {
    if ($null -eq (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
        Write-Host "NATSX privileged gamepad-host service removed."
        exit 0
    }
    Start-Sleep -Milliseconds 250
} while ([DateTimeOffset]::UtcNow -lt $deadline)

throw "NATSX privileged gamepad-host service is still registered after delete."
