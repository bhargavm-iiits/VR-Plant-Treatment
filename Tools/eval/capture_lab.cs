// Preview renders of the Lab: spawns a plant on the pedestal the way LabPlantDisplay would.
var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<BTP.Plants.PlantCatalog>("Assets/_Project/Data/PlantCatalog.asset");
var display = UnityEngine.Object.FindFirstObjectByType<BTP.Lab.LabPlantDisplay>();
catalog.TryGet("tomato", out var tomato);
var model = BTP.Plants.PlantSpawner.Spawn(tomato, BTP.Plants.PlantVariant.Lab, display.ModelRoot);
// Diseased (week 1) on the left half, week 3 on the right, as the demo does.
var data = UnityEditor.AssetDatabase.LoadAssetAtPath<BTP.Lab.TomatoRecoveryData>("Assets/_Project/Data/TomatoRecoveryData.asset");
var block = new UnityEngine.MaterialPropertyBlock();
foreach (var r in model.GetComponentsInChildren<UnityEngine.Renderer>())
{
    var w1 = data.GetWeek(1); var w3 = data.GetWeek(3);
    block.SetFloat("_Severity", w3.diseaseSeverity / 100f);
    block.SetFloat("_Chlorosis", 1f - w3.chlorophyll / 100f);
    block.SetFloat("_CompareSeverity", w1.diseaseSeverity / 100f);
    block.SetFloat("_CompareChlorosis", 1f - w1.chlorophyll / 100f);
    block.SetFloat("_SplitEnabled", 1f);
    var n = r.transform.InverseTransformDirection(UnityEngine.Vector3.right).normalized;
    block.SetVector("_SplitPlane", new UnityEngine.Vector4(n.x, n.y, n.z, UnityEngine.Vector3.Dot(n, r.transform.InverseTransformPoint(display.ModelRoot.position))));
    r.SetPropertyBlock(block);
}
var views = new (string name, UnityEngine.Vector3 position, UnityEngine.Vector3 euler)[]
{
    ("lab_front", new UnityEngine.Vector3(0f, 1.6f, -1.6f), new UnityEngine.Vector3(12f, 0f, 0f)),
    ("lab_left", new UnityEngine.Vector3(0f, 1.6f, -1.6f), new UnityEngine.Vector3(10f, -50f, 0f)),
    ("lab_right", new UnityEngine.Vector3(0f, 1.6f, -1.6f), new UnityEngine.Vector3(10f, 50f, 0f)),
    ("lab_plant", new UnityEngine.Vector3(0f, 1.45f, -0.75f), new UnityEngine.Vector3(15f, 0f, 0f)),
};
var go = new UnityEngine.GameObject("PreviewCamera");
var camera = go.AddComponent<UnityEngine.Camera>();
camera.enabled = false;
camera.nearClipPlane = 0.02f;
camera.farClipPlane = 450f;
camera.fieldOfView = 90f;
var rt = new UnityEngine.RenderTexture(1280, 720, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
camera.targetTexture = rt;
foreach (var v in views)
{
    camera.transform.SetPositionAndRotation(v.position, UnityEngine.Quaternion.Euler(v.euler));
    camera.Render();
    var prev = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = rt;
    var tex = new UnityEngine.Texture2D(rt.width, rt.height, UnityEngine.TextureFormat.RGB24, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply();
    UnityEngine.RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath($"Tools/out/previews/scene_{v.name}.png"), tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex);
}
camera.targetTexture = null;
rt.Release();
UnityEngine.Object.DestroyImmediate(rt);
UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Object.DestroyImmediate(model);
return "captured";
