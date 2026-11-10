# Drives the Farm -> Lab -> Farm journey in Play mode and prints the state after each step.
$eval = $PSScriptRoot
function Act([string]$action, [int]$wait = 2) {
    Set-Content -Path (Join-Path $eval 'play_action.txt') -Value $action
    $r = unity command eval_file --file (Join-Path $eval 'play_action.cs') --format json --no-banner 2>&1 | ConvertFrom-Json
    Start-Sleep -Seconds $wait
    $s = unity command eval_file --file (Join-Path $eval 'play_state.cs') --format json --no-banner 2>&1 | ConvertFrom-Json
    "{0,-22} -> {1}" -f $r.data.result.result, $s.data.result.result
}
Act 'select:corn'
Act 'EnterLab' 5
Act 'ViewTomatoDemo'
Act 'Week3'
Act 'Next'
Act 'Reset'
Act 'Plant_cherry'
Act 'Plant_tomato'
Act 'Week6'
Act 'ReturnToFarm' 5
