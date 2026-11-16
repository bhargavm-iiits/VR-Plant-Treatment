#!/bin/sh
# Usage: play.sh <scene path>  - opens the scene, enters Play mode and clears Error Pause.
U="unity command --no-banner --caller plugin --skill unity-cli"
$U editor_stop >/dev/null 2>&1; sleep 2
$U open_scene --path "$1" >/dev/null 2>&1
$U clear_console >/dev/null 2>&1
$U editor_play >/dev/null 2>&1; sleep 12
$U eval_file --file Tools/eval/unpause.cs >/dev/null 2>&1; sleep 3
$U eval_file --file Tools/eval/unpause.cs >/dev/null 2>&1; sleep 5
$U console --tail 60 2>/dev/null | grep -o '"level":"\(error\|exception\)","logType":"[A-Za-z]*","message":"[^"]\{0,300\}' | head -10
echo "playing $1"
