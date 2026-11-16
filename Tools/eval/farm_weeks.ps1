# Drives the Farm six-week timeline in Play mode: desktop mouse presses (desktop_test.cs) plus farm_weeks.cs actions.
param([Parameter(Mandatory)][string[]]$Steps)
$eval = $PSScriptRoot
foreach ($step in ($Steps -join ",").Split(",")) {
    if ($step -match '^wait:(.+)$') { Start-Sleep -Milliseconds ([int]([double]$Matches[1] * 1000)); continue }
    if ($step -match '^(press|release)') {
        Set-Content -Path (Join-Path $eval 'desktop_action.txt') -Value $step
        $file = 'desktop_test.cs'
    } else {
        Set-Content -Path (Join-Path $eval 'farm_weeks_action.txt') -Value $step
        $file = 'farm_weeks.cs'
    }
    $r = unity command eval_file --file (Join-Path $eval $file) --format json --no-banner 2>&1 | ConvertFrom-Json
    "{0,-28} -> {1}{2}" -f $step, $r.data.result.result, ($r.data.result.diagnostics | ForEach-Object { " DIAG " + $_.message })
}
