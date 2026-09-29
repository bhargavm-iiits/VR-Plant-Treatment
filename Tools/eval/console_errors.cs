// Recent errors/exceptions from the Editor log file (last 400 lines).
var lines = System.IO.File.ReadAllLines("Logs/Editor.log");
var sb = new System.Text.StringBuilder();
var start = System.Math.Max(0, lines.Length - 400);
for (var i = start; i < lines.Length; i++)
{
    var l = lines[i];
    if (l.Contains("Exception") || l.Contains("[BTP]") || l.Contains("error") || l.Contains("Error") || l.Contains("not in the build") || l.Contains("couldn't be loaded"))
        sb.AppendLine(l.Length > 240 ? l.Substring(0, 240) : l);
}
sb.AppendLine($"runInBackground={UnityEngine.Application.runInBackground} buildScenes={string.Join(",", UnityEditor.EditorBuildSettings.scenes.Select(s => s.path + (s.enabled ? "" : "(off)")))}");
return sb.ToString();
