#!/bin/sh
# Usage: treat.sh '<actions>'  - runs Tools/eval/treat_drive.cs with the given actions in Play mode.
printf '%s' "$1" > Tools/eval/treat_action.txt
unity command eval_file --no-banner --caller plugin --skill unity-cli --timeout 60 --file Tools/eval/treat_drive.cs --result-only 2>&1 | python -c "import sys,json
t=sys.stdin.read()
try: print(json.loads(t).get('result'))
except Exception: print(t[:2000])"
