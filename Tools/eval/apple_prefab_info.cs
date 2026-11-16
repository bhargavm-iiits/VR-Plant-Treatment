var sb = new System.Text.StringBuilder();
foreach (var path in new[]{"Assets/_Project/Prefabs/Plants/Apple_Farm.prefab","Assets/_Project/Prefabs/Plants/Apple_Lab.prefab"})
{
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    sb.AppendLine(path + " guid=" + UnityEditor.AssetDatabase.AssetPathToGUID(path));
    foreach (var t in go.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        var comps = string.Join(",", System.Array.ConvertAll(t.GetComponents<UnityEngine.Component>(), c => c == null ? "null" : c.GetType().Name));
        var r = t.GetComponent<UnityEngine.Renderer>();
        sb.AppendLine("  " + t.name + " [" + comps + "] " + (r ? string.Join("|", System.Array.ConvertAll(r.sharedMaterials, m => m ? m.name + ":" + m.shader.name : "null")) : "") + " ls=" + t.localScale);
    }
}
return sb.ToString();
