param([Parameter(Mandatory=$true)][string]$Receiver, [Parameter(Mandatory=$true)][string]$TrustRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$saved = @{}
$first = $null
try {
    foreach ($name in @('trusted-peers-v1.json', 'local-peer-id-v1.txt')) {
        $path = Join-Path $TrustRoot $name
        if (Test-Path $path) {
            $saved[$path] = [IO.File]::ReadAllBytes($path)
            Remove-Item $path
        }
    }
    $first = Start-Process -FilePath $Receiver -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $first.Refresh()
        if ($first.HasExited) { throw 'Primary Receiver exited before becoming ready.' }
        if ($first.MainWindowHandle -ne 0) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($first.MainWindowHandle)
            $condition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StatusText')
            $status = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($null -ne $status -and $status.Current.Name -like 'Receiver ready.*') { $ready = $true; break }
            if ($null -ne $status -and $status.Current.Name -like 'Receiver could not start:*') { throw $status.Current.Name }
        }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Actual Receiver UI did not become ready.' }
    foreach ($background in @($true, $false)) {
        $start = @{ FilePath=$Receiver; PassThru=$true }
        if ($background) { $start.ArgumentList='--background' }
        $second = Start-Process @start
        if (-not $second.WaitForExit(10000) -or $second.ExitCode -ne 0) { throw 'Repeated launch did not hand off to the existing Receiver.' }
        $first.Refresh()
        if ($first.HasExited) { throw 'Repeated launch terminated the existing Receiver.' }
    }
    $exit = Start-Process -FilePath $Receiver -ArgumentList '--exit' -PassThru
    if (-not $exit.WaitForExit(10000) -or $exit.ExitCode -ne 0 -or -not $first.WaitForExit(15000)) { throw 'Receiver exit activation failed.' }
    Write-Host 'PASS: actual Receiver ready, repeated foreground/background launch reuses primary, graceful exit.'
} finally {
    if ($null -ne $first -and -not $first.HasExited) { Stop-Process -Id $first.Id -Force -ErrorAction SilentlyContinue }
    foreach ($name in @('trusted-peers-v1.json', 'local-peer-id-v1.txt')) {
        $path = Join-Path $TrustRoot $name
        if (Test-Path $path) { Remove-Item $path }
        if ($saved.ContainsKey($path)) { [IO.File]::WriteAllBytes($path, $saved[$path]) }
    }
}
