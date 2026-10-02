[CmdletBinding()]
param(
    [string]$ProcessName = "Natsx.Controller.Receiver",
    [int]$DurationMinutes = 30,
    [int]$SampleIntervalSeconds = 1,
    [string]$OutputPath = "artifacts/performance/windows-receiver-profile.csv"
)

$ErrorActionPreference = "Stop"

if ($DurationMinutes -lt 1) {
    throw "DurationMinutes must be at least 1."
}

if ($SampleIntervalSeconds -lt 1) {
    throw "SampleIntervalSeconds must be at least 1."
}

$process =
    Get-Process -Name $ProcessName -ErrorAction Stop |
        Select-Object -First 1

$outputFullPath =
    if ([System.IO.Path]::IsPathRooted($OutputPath)) {
        $OutputPath
    }
    else {
        Join-Path (Get-Location) $OutputPath
    }

$outputDirectory =
    Split-Path -Parent $outputFullPath

if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force |
        Out-Null
}

$processorCount =
    [Environment]::ProcessorCount

$duration =
    [TimeSpan]::FromMinutes(
        $DurationMinutes)

$deadline =
    [DateTimeOffset]::UtcNow +
    $duration

$previousCpu =
    $process.TotalProcessorTime

$previousTimestamp =
    [DateTimeOffset]::UtcNow

$samples =
    [System.Collections.Generic.List[object]]::new()

Write-Host "Profiling PID $($process.Id) for $DurationMinutes minute(s)."
Write-Host "Output: $outputFullPath"

while ([DateTimeOffset]::UtcNow -lt $deadline) {
    Start-Sleep -Seconds $SampleIntervalSeconds

    $process.Refresh()

    if ($process.HasExited) {
        throw "Receiver process exited during profiling."
    }

    $now =
        [DateTimeOffset]::UtcNow

    $cpu =
        $process.TotalProcessorTime

    $wallSeconds =
        ($now - $previousTimestamp)
            .TotalSeconds

    $cpuSeconds =
        ($cpu - $previousCpu)
            .TotalSeconds

    $cpuPercent =
        if ($wallSeconds -gt 0) {
            [Math]::Max(
                0,
                [Math]::Min(
                    100,
                    (
                        $cpuSeconds /
                        $wallSeconds /
                        $processorCount
                    ) *
                    100))
        }
        else {
            0
        }

    $samples.Add(
        [pscustomobject]@{
            TimestampUtc =
                $now.ToString("O")
            CpuPercentNormalized =
                [Math]::Round(
                    $cpuPercent,
                    3)
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
        })

    $previousCpu =
        $cpu

    $previousTimestamp =
        $now
}

$samples |
    Export-Csv -LiteralPath $outputFullPath -NoTypeInformation -Encoding UTF8

$cpuValues =
    @(
        $samples |
            ForEach-Object {
                [double]$_.CpuPercentNormalized
            }
    )

$workingSetValues =
    @(
        $samples |
            ForEach-Object {
                [double]$_.WorkingSetMiB
            }
    )

$privateValues =
    @(
        $samples |
            ForEach-Object {
                [double]$_.PrivateMemoryMiB
            }
    )

$summary =
    [ordered]@{
        ProcessName =
            $ProcessName
        ProcessId =
            $process.Id
        SampleCount =
            $samples.Count
        DurationMinutes =
            $DurationMinutes
        SampleIntervalSeconds =
            $SampleIntervalSeconds
        ProcessorCount =
            $processorCount
        AverageCpuPercentNormalized =
            if ($cpuValues.Count -gt 0) {
                [Math]::Round(
                    (
                        $cpuValues |
                            Measure-Object -Average
                    ).Average,
                    3)
            }
            else {
                0
            }
        PeakCpuPercentNormalized =
            if ($cpuValues.Count -gt 0) {
                (
                    $cpuValues |
                        Measure-Object -Maximum
                ).Maximum
            }
            else {
                0
            }
        PeakWorkingSetMiB =
            if ($workingSetValues.Count -gt 0) {
                (
                    $workingSetValues |
                        Measure-Object -Maximum
                ).Maximum
            }
            else {
                0
            }
        PeakPrivateMemoryMiB =
            if ($privateValues.Count -gt 0) {
                (
                    $privateValues |
                        Measure-Object -Maximum
                ).Maximum
            }
            else {
                0
            }
    }

$summaryPath =
    [System.IO.Path]::ChangeExtension(
        $outputFullPath,
        ".summary.json")

$summary |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ""
Write-Host "Profiling complete."
$summary |
    Format-List |
    Out-String |
    Write-Host
