<#
.SYNOPSIS
    Registers the CropScan XR website button for a USB-connected Meta Quest on this Windows user account.
.DESCRIPTION
    Use the updated Android APK and allow USB debugging on the Quest. The separate installer for the Windows player
    still works as before. This installer refuses to take over another installed launcher.
#>
[CmdletBinding()]
param(
    [string] $AdbPath = (Get-Command adb.exe -ErrorAction Stop).Source,
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\CropScanQuestBridge'),
    [string] $Launcher = (Join-Path $PSScriptRoot 'bin\Release\net48\CropScanLauncher.exe')
)

$ErrorActionPreference = 'Stop'
$AdbPath = (Resolve-Path -LiteralPath $AdbPath).Path
$Launcher = (Resolve-Path -LiteralPath $Launcher).Path
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
$launcherPath = Join-Path $InstallDir 'CropScanLauncher.exe'
$key = 'HKCU:\Software\Classes\cropscanxr'
$command = '"' + $launcherPath + '" "%1"'
if (Test-Path -LiteralPath $key) {
    $registered = (Get-Item -LiteralPath "$key\shell\open\command" -ErrorAction SilentlyContinue)
    if (!$registered -or $registered.GetValue('') -ne $command) {
        throw 'A different CropScan XR link handler is installed. Remove it before installing the Quest launcher.'
    }
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -LiteralPath $Launcher -Destination $launcherPath -Force
[System.IO.File]::WriteAllText((Join-Path $InstallDir 'QuestAdbPath.txt'), $AdbPath, [System.Text.UTF8Encoding]::new($false))
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name '(default)' -Value 'URL:CropScan XR'
New-ItemProperty -Path $key -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
New-Item -Path "$key\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$key\shell\open\command" -Name '(default)' -Value $command
Write-Host "Installed the CropScan Quest launcher to $InstallDir"
