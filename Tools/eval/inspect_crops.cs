var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var sb = new System.Text.StringBuilder();
foreach (var root in scene.GetRootGameObjects())
{
    sb.AppendLine($"{root.name}: children={root.transform.childCount} renderers={root.GetComponentsInChildren<UnityEngine.Renderer>(true).Length}");
    if (root.name == "Crops")
        foreach (UnityEngine.Transform group in root.transform)
        {
            var first = group.childCount > 0 ? group.GetChild(0) : null;
            var lod = first != null ? first.GetComponent<UnityEngine.LODGroup>() : null;
            var info = first == null ? "" : $" first={first.name} pos={first.position} scale={first.localScale.x:0.00} active={first.gameObject.activeInHierarchy} lodSize={(lod ? lod.size : 0):0.00}";
            sb.AppendLine($"   {group.name}: {group.childCount}{info}");
        }
}
var tmp = UnityEngine.Object.FindFirstObjectByType<TMPro.TextMeshProUGUI>();
sb.AppendLine($"TMP default font: {(TMPro.TMP_Settings.defaultFontAsset ? TMPro.TMP_Settings.defaultFontAsset.name : "NONE")}, sample text font: {(tmp && tmp.font ? tmp.font.name : "NONE")}");
return sb.ToString();
