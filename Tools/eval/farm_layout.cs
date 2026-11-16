var sb = new System.Text.StringBuilder();
foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
    sb.Append(root.name + " ");
sb.AppendLine();
foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsInactive.Include))
{
    if (t.name.StartsWith("Specimen_") || t.name == "Orchard" || t.name.StartsWith("Greenhouse") || t.name.Contains("Fence") && t.parent != null && t.parent.name.Contains("Fence") == false || t.name == "SpawnAnchor")
        sb.AppendLine(t.name + " pos=" + t.position.ToString("F1") + " parent=" + (t.parent ? t.parent.name : "-"));
}
var orchard = UnityEngine.GameObject.Find("Orchard");
if (orchard) foreach (UnityEngine.Transform c in orchard.transform) sb.AppendLine("  tree " + c.name + " " + c.position.ToString("F1") + " s=" + c.localScale.x.ToString("F2"));
return sb.ToString();
