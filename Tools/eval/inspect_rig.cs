var path = "Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
if (prefab == null) return "missing " + path;
var sb = new System.Text.StringBuilder();
void Walk(UnityEngine.Transform t, int depth)
{
    var names = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<UnityEngine.Component>())
    {
        if (c == null || c is UnityEngine.Transform) continue;
        var n = c.GetType().Name;
        if (c is UnityEngine.Behaviour b && !b.enabled) n += "(off)";
        names.Add(n);
    }
    if (depth <= 3)
        sb.AppendLine(new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " [inactive]") + ": " + string.Join(", ", names));
    foreach (UnityEngine.Transform child in t) Walk(child, depth + 1);
}
Walk(prefab.transform, 0);
return sb.ToString();
