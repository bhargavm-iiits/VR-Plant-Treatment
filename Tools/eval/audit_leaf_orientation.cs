var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<BTP.Plants.LeafCatalog>("Assets/_Project/Data/LeafCatalog.asset");
var cameraObject = new UnityEngine.GameObject("Leaf audit camera");
var camera = cameraObject.AddComponent<UnityEngine.Camera>();
camera.enabled = false;
camera.orthographic = true;
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(0.06f, 0.075f, 0.09f);
camera.cullingMask = 1 << 30;
var rt = new UnityEngine.RenderTexture(360,360,24,UnityEngine.RenderTextureFormat.ARGB32,UnityEngine.RenderTextureReadWrite.sRGB);
camera.targetTexture = rt;
System.IO.Directory.CreateDirectory("Tools/out/previews/leaf_audit");
foreach (var entry in catalog.Entries)
{
    var instance = UnityEngine.Object.Instantiate(entry.prefab);
    instance.transform.position = new UnityEngine.Vector3(1000,0,0);
    foreach (var t in instance.GetComponentsInChildren<UnityEngine.Transform>()) t.gameObject.layer = 30;
    var renderers = instance.GetComponentsInChildren<UnityEngine.Renderer>();
    var bounds = renderers[0].bounds;
    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
    camera.orthographicSize = UnityEngine.Mathf.Max(bounds.size.x,bounds.size.y) * 0.6f;
    camera.transform.SetPositionAndRotation(bounds.center + UnityEngine.Vector3.back * 3f,UnityEngine.Quaternion.identity);
    camera.Render();
    var previous = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = rt;
    var image = new UnityEngine.Texture2D(360,360,UnityEngine.TextureFormat.RGB24,false);
    image.ReadPixels(new UnityEngine.Rect(0,0,360,360),0,0);
    image.Apply();
    UnityEngine.RenderTexture.active = previous;
    System.IO.File.WriteAllBytes("Tools/out/previews/leaf_audit/" + entry.plantId + "_" + entry.diseaseId + ".png",image.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(image);
    UnityEngine.Object.DestroyImmediate(instance);
}
camera.targetTexture = null;
rt.Release();
UnityEngine.Object.DestroyImmediate(rt);
UnityEngine.Object.DestroyImmediate(cameraObject);
return "Captured 24 leaf orientations";
