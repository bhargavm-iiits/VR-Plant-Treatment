<#
.SYNOPSIS
    Removes the per-user cropscanxr: link registration, and with -RemoveFiles the installed CropScan XR folder.
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\CropScanXR'),
    [switch] $RemoveFiles
)

$ErrorActionPreference = 'Stop'
$key = 'HKCU:\Software\Classes\cropscanxr'
if (Test-Path $key) {
    Remove-Item -Path $key -Recurse -Force
    Write-Host 'Removed the cropscanxr: link registration.'
}

if ($RemoveFiles -and (Test-Path $InstallDir)) {
    if (Get-Process -Name 'PlantDiseaseVR' -ErrorAction SilentlyContinue) { throw 'Close CropScan XR before removing its files.' }
    if (-not (Test-Path (Join-Path $InstallDir 'CropScanLauncher.exe'))) { throw "$InstallDir does not look like a CropScan XR install; not removing it." }
    Remove-Item -Path $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir."
}
