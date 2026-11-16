<#
.SYNOPSIS
    Installs CropScan XR for the current Windows user and registers the cropscanxr: link used by "Enter the field".

.DESCRIPTION
    Copies the Windows x64 OpenXR player build and CropScanLauncher.exe into one folder, writes CropScanXR.config.json
    (the API address: the link itself only carries a scan ID) and registers cropscanxr: under HKEY_CURRENT_USER, so no
    administrator rights are needed. Run it again to update the build; close CropScan XR first.

.EXAMPLE
    dotnet build Tools\CropScanLauncher -c Release
    .\Tools\CropScanLauncher\Install-CropScanXR.ps1 -PlayerBuild .\Builds\Windows -ApiBaseUrl https://cropscan-api-ovmb.onrender.com -SessionMode preview
#>
[CmdletBinding()]
param(
    # Folder that contains PlantDiseaseVR.exe (a Windows x64 build with OpenXR enabled).
    [Parameter(Mandatory = $true)] [string] $PlayerBuild,
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\CropScanXR'),
    # The CropScan API the player talks to (https). Empty keeps the offline demo; scans then show a setup message.
    [string] $ApiBaseUrl = '',
    # paired: the player creates sessions over the XR API (needs pairing). preview: staging proof of concept that reads
    # GET /api/scans/{id} and keeps results on this PC.
    [ValidateSet('paired', 'preview')] [string] $SessionMode = 'paired',
    [string] $Launcher = (Join-Path $PSScriptRoot 'bin\Release\net48\CropScanLauncher.exe')
)

$ErrorActionPreference = 'Stop'

$PlayerBuild = (Resolve-Path $PlayerBuild).Path
if (-not (Test-Path (Join-Path $PlayerBuild 'PlantDiseaseVR.exe'))) { throw "No PlantDiseaseVR.exe in $PlayerBuild." }
if (-not (Test-Path $Launcher)) { throw "No launcher at $Launcher. Build it first: dotnet build `"$PSScriptRoot`" -c Release" }
# https everywhere, except a backend running on this same PC (services/api/dev_local.py), which the player also allows.
if ($ApiBaseUrl -and -not ($ApiBaseUrl -match '^(https://[^\s/]+|http://(localhost|127\.0\.0\.1)(:\d+)?/?$)')) {
    throw 'ApiBaseUrl must be an https:// address (or http://127.0.0.1:<port> for a local backend).'
}
if (Get-Process -Name 'PlantDiseaseVR' -ErrorAction SilentlyContinue) { throw 'Close CropScan XR (PlantDiseaseVR.exe) before installing.' }

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Get-ChildItem -Path $PlayerBuild | Where-Object { $_.Name -notlike '*_DoNotShip' } |
    Copy-Item -Destination $InstallDir -Recurse -Force
Copy-Item -Path $Launcher -Destination $InstallDir -Force

$config = [ordered]@{ apiBaseUrl = $ApiBaseUrl.TrimEnd('/'); sessionMode = $SessionMode }
$configPath = Join-Path $InstallDir 'CropScanXR.config.json'
[System.IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))

$launcherPath = Join-Path $InstallDir 'CropScanLauncher.exe'
$key = 'HKCU:\Software\Classes\cropscanxr'
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name '(default)' -Value 'URL:CropScan XR'
New-ItemProperty -Path $key -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
New-Item -Path "$key\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "$key\DefaultIcon" -Name '(default)' -Value "`"$(Join-Path $InstallDir 'PlantDiseaseVR.exe')`",0"
New-Item -Path "$key\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$key\shell\open\command" -Name '(default)' -Value "`"$launcherPath`" `"%1`""

Write-Host "Installed CropScan XR to $InstallDir"
Write-Host "  link handler : cropscanxr: -> $launcherPath"
Write-Host "  config       : $configPath (sessionMode=$SessionMode, api=$(if ($ApiBaseUrl) { $ApiBaseUrl } else { 'none' }))"
Write-Host "Test without the browser:"
Write-Host "  & '$(Join-Path $InstallDir 'PlantDiseaseVR.exe')' '--crop-scan-id' '<staging-scan-uuid>'"
Write-Host "  Start-Process 'cropscanxr://open-field?scan=<staging-scan-uuid>'"
