var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_Project/Prefabs/Leaves/grape_leaf_blight.prefab");
var instance = UnityEngine.Object.Instantiate(prefab);
instance.transform.position = new UnityEngine.Vector3(1000,0,0);
foreach (var t in instance.GetComponentsInChildren<UnityEngine.Transform>()) t.gameObject.layer = 30;
var renderer = instance.GetComponentInChildren<UnityEngine.Renderer>();
var bounds = renderer.bounds;
var go = new UnityEngine.GameObject("Grape face audit");
var cam = go.AddComponent<UnityEngine.Camera>(); cam.enabled = false; cam.orthographic = true;
cam.cullingMask = 1 << 30; cam.orthographicSize = 0.62f;
cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.backgroundColor = new UnityEngine.Color(.06f,.075f,.09f);
var rt = new UnityEngine.RenderTexture(600,600,24); cam.targetTexture = rt;
foreach (var side in new [] { -1, 1 })
{
 cam.transform.position = bounds.center + new UnityEngine.Vector3(0,0,side*3);
 cam.transform.LookAt(bounds.center); cam.Render();
 var previous = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
 var image = new UnityEngine.Texture2D(600,600,UnityEngine.TextureFormat.RGB24,false);
 image.ReadPixels(new UnityEngine.Rect(0,0,600,600),0,0); image.Apply(); UnityEngine.RenderTexture.active = previous;
 System.IO.File.WriteAllBytes("Tools/out/previews/grape_face_" + side + ".png",image.EncodeToPNG());
 UnityEngine.Object.DestroyImmediate(image);
}
cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(instance);
return "Captured both Grape Leaf Blight faces";
