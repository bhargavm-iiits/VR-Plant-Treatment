var sb = new System.Text.StringBuilder();
foreach (var path in new[]{"Assets/Lab_Treatment_Assets/pruning_shears.glb","Assets/Lab_Treatment_Assets/gardening_tools.glb","Assets/Lab_Treatment_Assets/work_gloves.glb"})
{
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (go == null) { sb.AppendLine(path + " not a GameObject: " + UnityEditor.AssetDatabase.GetMainAssetTypeAtPath(path)); continue; }
    var inst = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(go);
    var b = new UnityEngine.Bounds(); bool first = true;
    foreach (var r in inst.GetComponentsInChildren<UnityEngine.Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
    sb.AppendLine($"{path} size={b.size} center={b.center}");
    foreach (var f in inst.GetComponentsInChildren<UnityEngine.MeshFilter>()) sb.AppendLine($"   {f.name} tris={(f.sharedMesh? f.sharedMesh.triangles.Length/3:0)} pos={f.transform.position} size={(f.GetComponent<UnityEngine.Renderer>().bounds.size)}");
    UnityEngine.Object.DestroyImmediate(inst);
}
return sb.ToString();
