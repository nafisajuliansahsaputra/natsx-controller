[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetPath,

    [Parameter(Mandatory = $true)]
    [string]$PfxPath,

    [Parameter(Mandatory = $true)]
    [string]$PfxPassword,

    [Parameter(Mandatory = $true)]
    [string]$TimestampUrl
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $programFilesX86 =
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::ProgramFilesX86)

    $roots = @(
        (Join-Path $PSScriptRoot "..\driver\aoa-bootstrap\packages"),
        (Join-Path $programFilesX86 "Windows Kits\10\bin")
    ) | Where-Object {
        Test-Path $_
    }

    $matches = @(
        Get-ChildItem -Path $roots -Filter "signtool.exe" -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object {
                $_.DirectoryName -match '(?i)[\\/]x64(?:[\\/]|$)'
            } |
            Sort-Object FullName -Descending
    )

    if ($matches.Count -eq 0) {
        throw "signtool.exe was not found. Restore/install the Windows Driver Kit before release signing."
    }

    return $matches[0].FullName
}

if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
    throw "Windows release-signing PFX does not exist: $PfxPath"
}

if (-not [Uri]::IsWellFormedUriString($TimestampUrl, [UriKind]::Absolute)) {
    throw "WINDOWS_TIMESTAMP_URL must be an absolute RFC3161 timestamp URL."
}

$resolvedTarget =
    Resolve-Path -LiteralPath $TargetPath

$targets =
    if (Test-Path -LiteralPath $resolvedTarget.Path -PathType Container) {
        @(
            Get-ChildItem -LiteralPath $resolvedTarget.Path -File |
                Where-Object {
                    $_.Name -eq "Natsx.Controller.Receiver.exe" -or
                    $_.Name -like "Natsx.Controller.*.dll"
                } |
                Sort-Object Name
        )
    }
    else {
        @(
            Get-Item -LiteralPath $resolvedTarget.Path
        )
    }

if ($targets.Count -eq 0) {
    throw "No NATSX Windows release binaries were found to sign under: $TargetPath"
}

$signTool = Find-SignTool

foreach ($target in $targets) {
    Write-Host "Signing $($target.FullName)"

    $signArguments = @(
        "sign",
        "/fd",
        "SHA256",
        "/f",
        $PfxPath,
        "/p",
        $PfxPassword,
        "/tr",
        $TimestampUrl,
        "/td",
        "SHA256",
        $target.FullName
    )

    & $signTool @signArguments

    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed while signing '$($target.FullName)'."
    }

    & $signTool verify /pa /v $target.FullName

    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode verification failed after signing '$($target.FullName)'."
    }
}

Write-Host "Windows release signing and verification passed for $($targets.Count) file(s)."
