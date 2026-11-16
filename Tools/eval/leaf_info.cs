var sb = new System.Text.StringBuilder();
var cat = UnityEditor.AssetDatabase.LoadAssetAtPath<BTP.Plants.LeafCatalog>("Assets/_Project/Data/LeafCatalog.asset");
int n = 0;
foreach (var e in cat.Entries)
{
    if (n++ > 3 && e.plantId != "apple") continue;
    var go = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(e.prefab);
    sb.Append($"{e.plantId}/{e.diseaseId} {UnityEditor.AssetDatabase.GetAssetPath(e.prefab)}: ");
    foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
    {
        var mf = r.GetComponent<UnityEngine.MeshFilter>();
        sb.Append($"[{r.name} mesh={(mf ? mf.sharedMesh.name + " v" + mf.sharedMesh.vertexCount : "-")} size={r.bounds.size} mats={string.Join("|", System.Array.ConvertAll(r.sharedMaterials, m => m ? m.name + ":" + m.shader.name : "null"))}] ");
    }
    foreach (var c in go.GetComponentsInChildren<UnityEngine.Component>(true)) if (!(c is UnityEngine.Transform) && !(c is UnityEngine.Renderer) && !(c is UnityEngine.MeshFilter)) sb.Append(c.GetType().Name + " ");
    sb.AppendLine();
    UnityEngine.Object.DestroyImmediate(go);
}
return sb.ToString();
