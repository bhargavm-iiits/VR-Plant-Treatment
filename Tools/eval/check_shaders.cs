var sb = new System.Text.StringBuilder();
foreach (var name in new[] { "BTP/Leaf Disease", "BTP/Fade Overlay", "BTP/Unlit Glow" })
{
    var shader = UnityEngine.Shader.Find(name);
    if (shader == null) { sb.AppendLine($"{name}: NOT FOUND"); continue; }
    var messages = UnityEditor.ShaderUtil.GetShaderMessages(shader);
    sb.AppendLine($"{name}: hasError={UnityEditor.ShaderUtil.ShaderHasError(shader)} messages={messages.Length} supported={shader.isSupported}");
    foreach (var m in messages)
        sb.AppendLine($"   {m.severity} {m.platform} line {m.line}: {m.message}");
}
return sb.ToString();
