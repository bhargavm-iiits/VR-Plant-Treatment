var sb = new System.Text.StringBuilder();
var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<BTP.Plants.PlantCatalog>("Assets/_Project/Data/PlantCatalog.asset");
foreach (var plant in catalog.Plants)
{
    foreach (var prefab in new[] { plant.FarmPrefab, plant.LabPrefab })
    {
        if (prefab == null) { sb.AppendLine(plant.PlantId + " null"); continue; }
        var go = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(prefab);
        var b = new UnityEngine.Bounds(); bool first = true; int tris = 0;
        foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        foreach (var f in go.GetComponentsInChildren<UnityEngine.MeshFilter>()) if (f.sharedMesh) tris += f.sharedMesh.triangles.Length / 3;
        sb.AppendLine($"{plant.PlantId} {prefab.name} center={b.center} size={b.size} tris={tris} children={go.transform.childCount}");
        UnityEngine.Object.DestroyImmediate(go);
    }
}
return sb.ToString();
