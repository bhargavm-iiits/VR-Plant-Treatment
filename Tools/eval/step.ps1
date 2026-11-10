# Runs one BTP editor step through the Pipeline and waits for its DONE/FAILED line.
# Usage: powershell -File Tools/eval/step.ps1 -Name xr -Call "BTP.Editor.XRProjectSetup.Configure" [-TimeoutMinutes 20]
param([Parameter(Mandatory)] [string]$Name, [Parameter(Mandatory)] [string]$Call, [int]$TimeoutMinutes = 20)

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$log = Join-Path $root 'Tools\logs\automation.log'
$file = Join-Path $PSScriptRoot "step_$Name.cs"
"return BTP.Editor.Automation.Run(`"$Name`", $Call);" | Set-Content -Encoding utf8 $file
$before = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

unity command eval_file --file $file --format json --no-banner 2>&1 | Out-Null

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    if (-not (Test-Path $log)) { continue }
    $new = Get-Content $log | Select-Object -Skip $before
    $end = $new | Where-Object { $_ -match "(DONE|FAILED) $Name" } | Select-Object -Last 1
    if ($end) { $new | Where-Object { $_ -match " $Name" }; exit ($(if ($end -match 'FAILED') { 1 } else { 0 })) }
}
"TIMEOUT waiting for $Name"
exit 2
