#!/bin/sh
# Clear console, request a recompile, wait for it, then print unique compile errors.
U="unity command --no-banner --caller plugin --skill unity-cli"
$U clear_console >/dev/null 2>&1
$U eval --code 'UnityEditor.AssetDatabase.Refresh(); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return "ok";' >/dev/null 2>&1
sleep 3
for i in $(seq 1 40); do
  s=$($U eval --code 'return UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating ? "busy" : "idle";' --result-only 2>/dev/null | grep -o '"result": *"[a-z]*"')
  case "$s" in *idle*) break;; esac
  sleep 3
done
sleep 2
$U console --level error --tail 60 2>&1 | grep -o '"message":"[^"]*' | grep -v ExecuteCommandByName | sort -u | head -25
echo "done ($s)"
