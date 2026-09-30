# Refreshes the AssetDatabase through the Pipeline and waits until scripts have recompiled.
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runtime = Join-Path $root 'Library\ScriptAssemblies\BTP.Editor.dll'
$before = (Get-Item $runtime).LastWriteTime
unity command eval_file --file (Join-Path $PSScriptRoot 'refresh_now.cs') --format json --no-banner 2>&1 | Out-Null
$deadline = (Get-Date).AddMinutes(5)
do {
    Start-Sleep -Seconds 4
    $changed = (Get-Item $runtime).LastWriteTime -gt $before
    $status = unity command eval_file --file (Join-Path $PSScriptRoot 'status.cs') --format json --no-banner 2>&1 | Out-String
} while (-not ($changed -and $status -match 'compiling=False' -and $status -match 'BTP.Editor') -and (Get-Date) -lt $deadline)
$errors = unity command console --tail 30 --level error --format json --no-banner 2>&1 | ConvertFrom-Json
$compileErrors = $errors.data.result.entries | Where-Object { $_.message -match 'error CS' }
if ($compileErrors) { $compileErrors | ForEach-Object { $_.message }; exit 1 }
"recompiled=$changed"
