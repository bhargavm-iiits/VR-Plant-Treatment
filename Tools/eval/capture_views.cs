// Renders preview images of the open scene from fixed viewpoints (temporary camera, not saved).
var views = new (string name, UnityEngine.Vector3 position, UnityEngine.Vector3 euler, float fov)[]
{
    ("spawn", new UnityEngine.Vector3(0f, 1.6f, -9.5f), new UnityEngine.Vector3(8f, 0f, 0f), 90f),
    ("spawn_left", new UnityEngine.Vector3(0f, 1.6f, -9.5f), new UnityEngine.Vector3(10f, -45f, 0f), 90f),
    ("aerial", new UnityEngine.Vector3(0f, 26f, -26f), new UnityEngine.Vector3(40f, 0f, 0f), 70f),
    ("specimens", new UnityEngine.Vector3(0f, 1.6f, -8f), new UnityEngine.Vector3(18f, 0f, 0f), 90f),
};
var go = new UnityEngine.GameObject("PreviewCamera");
var camera = go.AddComponent<UnityEngine.Camera>();
camera.enabled = false;
camera.nearClipPlane = 0.05f;
camera.farClipPlane = 450f;
var rt = new UnityEngine.RenderTexture(1280, 720, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
camera.targetTexture = rt;
var saved = new System.Collections.Generic.List<string>();
foreach (var v in views)
{
    camera.transform.SetPositionAndRotation(v.position, UnityEngine.Quaternion.Euler(v.euler));
    camera.fieldOfView = v.fov;
    camera.Render();
    var prev = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = rt;
    var tex = new UnityEngine.Texture2D(rt.width, rt.height, UnityEngine.TextureFormat.RGB24, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply();
    UnityEngine.RenderTexture.active = prev;
    var path = System.IO.Path.GetFullPath($"Tools/out/previews/scene_{v.name}.png");
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex);
    saved.Add(path);
}
camera.targetTexture = null;
rt.Release();
UnityEngine.Object.DestroyImmediate(rt);
UnityEngine.Object.DestroyImmediate(go);
return string.Join("\n", saved);
