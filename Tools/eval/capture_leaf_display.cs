var display = UnityEngine.Object.FindFirstObjectByType<BTP.Lab.LabPlantDisplay>();
var selection = BTP.Core.SelectionState.Instance;
var go = new UnityEngine.GameObject("LeafVerificationCamera");
var camera = go.AddComponent<UnityEngine.Camera>();
camera.enabled = false;
camera.fieldOfView = 72f;
camera.nearClipPlane = 0.02f;
camera.farClipPlane = 450f;
camera.transform.SetPositionAndRotation(new UnityEngine.Vector3(0f,1.6f,-1.6f), UnityEngine.Quaternion.Euler(7f,0f,0f));
var rt = new UnityEngine.RenderTexture(1800,1100,24,UnityEngine.RenderTextureFormat.ARGB32,UnityEngine.RenderTextureReadWrite.sRGB);
camera.targetTexture = rt;
System.IO.Directory.CreateDirectory("Tools/out/previews/leaves");
foreach (var example in new[] { ("tomato","early_blight",1), ("tomato","early_blight",6), ("apple","apple_scab",3), ("corn","common_rust",6), ("grape","esca",6) })
{
    selection.SelectPlant(example.Item1);
    display.ShowDisease(example.Item2);
    UnityEngine.GameObject.Find("Week" + example.Item3).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
    // Advance only the verification specimen to its endpoint for static comparison images.
    display.CurrentModel.GetComponent<BTP.Lab.LeafRecoveryVisual>().SendMessage("Advance", 2f);
    UnityEngine.Object.FindAnyObjectByType<BTP.Lab.RecoveryDemoPanel>().SendMessage("Update");
    UnityEngine.Canvas.ForceUpdateCanvases();
    foreach (var text in UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(UnityEngine.FindObjectsSortMode.None)) text.ForceMeshUpdate();
    camera.Render();
    var previous = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = rt;
    var image = new UnityEngine.Texture2D(rt.width,rt.height,UnityEngine.TextureFormat.RGB24,false);
    image.ReadPixels(new UnityEngine.Rect(0,0,rt.width,rt.height),0,0);
    image.Apply();
    UnityEngine.RenderTexture.active = previous;
    System.IO.File.WriteAllBytes("Tools/out/previews/leaves/" + example.Item1 + "_week" + example.Item3 + ".png", image.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(image);
}
camera.targetTexture = null;
rt.Release();
UnityEngine.Object.DestroyImmediate(rt);
UnityEngine.Object.DestroyImmediate(go);
selection.SelectPlant("tomato");
display.ShowDisease("early_blight");
UnityEngine.GameObject.Find("Reset").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
return "Captured 5 leaf display previews";
