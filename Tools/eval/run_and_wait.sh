#!/bin/sh
# Usage: run_and_wait.sh '<C# statement>' '<log text to wait for>' [timeout seconds]
# Runs a long Editor operation (eval returns after 5 s but the work continues on the main thread)
# and waits until the expected log line or an error appears.
U="unity command --no-banner --caller plugin --skill unity-cli"
$U clear_console >/dev/null 2>&1
$U eval --code "$1 return \"started\";" >/dev/null 2>&1
limit=${3:-300}; waited=0
while [ $waited -lt $limit ]; do
  out=$($U console --tail 60 2>/dev/null)
  if echo "$out" | grep -q "$2"; then echo "OK: $2"; break; fi
  if echo "$out" | grep -q '"level":"exception"\|InvalidOperationException\|NullReferenceException'; then echo "FAILED"; break; fi
  sleep 4; waited=$((waited+4))
done
$U console --tail 60 2>/dev/null | grep -o '"message":"[^"]\{0,350\}' | grep -v "CS0618\|api/exec\|ExecuteCommandByName" | tail -12
