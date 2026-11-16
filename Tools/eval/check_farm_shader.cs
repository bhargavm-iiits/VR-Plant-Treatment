var shader = UnityEngine.Shader.Find("BTP/Farm Recovery");
if (shader == null) return "shader not found";
var messages = UnityEditor.ShaderUtil.GetShaderMessages(shader);
var sb = new System.Text.StringBuilder();
sb.Append($"supported={shader.isSupported} passes={shader.passCount} messages={messages.Length}");
foreach (var m in messages) sb.Append($" | {m.severity}: {m.message} ({m.file}:{m.line})");
return sb.ToString();
