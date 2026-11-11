var sb = new System.Text.StringBuilder();
var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<BTP.Plants.PlantCatalog>("Assets/_Project/Data/PlantCatalog.asset");
if (catalog == null) return "no catalog";
var problems = catalog.Validate();
sb.AppendLine($"catalog plants={catalog.Plants.Count} problems={problems.Count} {string.Join(" | ", problems)}");
foreach (var plant in catalog.Plants)
{
    string Describe(UnityEngine.GameObject prefab)
    {
        if (prefab == null) return "MISSING";
        var renderers = prefab.GetComponentsInChildren<UnityEngine.Renderer>(true);
        var tris = 0;
        foreach (var f in prefab.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
            if (f.sharedMesh != null && f.gameObject.name != "Billboard") tris += f.sharedMesh.triangles.Length / 3;
        var b = new UnityEngine.Bounds();
        var first = true;
        foreach (var f in prefab.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
        {
            if (f.sharedMesh == null || f.gameObject.name == "Billboard") continue;
            var m = f.transform.localToWorldMatrix;
            var mb = f.sharedMesh.bounds;
            var c = m.MultiplyPoint3x4(mb.center);
            var e = new UnityEngine.Vector3(
                UnityEngine.Mathf.Abs(m.m00) * mb.extents.x + UnityEngine.Mathf.Abs(m.m01) * mb.extents.y + UnityEngine.Mathf.Abs(m.m02) * mb.extents.z,
                UnityEngine.Mathf.Abs(m.m10) * mb.extents.x + UnityEngine.Mathf.Abs(m.m11) * mb.extents.y + UnityEngine.Mathf.Abs(m.m12) * mb.extents.z,
                UnityEngine.Mathf.Abs(m.m20) * mb.extents.x + UnityEngine.Mathf.Abs(m.m21) * mb.extents.y + UnityEngine.Mathf.Abs(m.m22) * mb.extents.z);
            var local = new UnityEngine.Bounds(c, e * 2f);
            if (first) { b = local; first = false; } else b.Encapsulate(local);
        }
        var mats = string.Join("/", renderers.SelectMany(r => r.sharedMaterials).Where(x => x != null).Select(x => x.shader.name.Replace("Universal Render Pipeline/", "")).Distinct());
        var lod = prefab.GetComponent<UnityEngine.LODGroup>();
        return $"{prefab.name} tris={tris} size=({b.size.x:0.00},{b.size.y:0.00},{b.size.z:0.00}) minY={b.min.y:0.00} lods={(lod ? lod.lodCount : 0)} shaders={mats}";
    }
    sb.AppendLine($"{plant.PlantId,-12} farm: {Describe(plant.FarmPrefab)}");
    sb.AppendLine($"{"",-12} lab:  {Describe(plant.LabPrefab)}");
}
return sb.ToString();
