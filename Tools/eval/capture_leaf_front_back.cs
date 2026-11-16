// Capture the final saved lab after a clean script reload.
var display = UnityEngine.Object.FindAnyObjectByType<BTP.Lab.LabPlantDisplay>();
var facing = display.GetComponent<BTP.Lab.LeafViewerFacing>();
var selection = BTP.Core.SelectionState.Instance;
selection.SelectPlant("apple"); display.ShowDisease("apple_scab");
var go = new UnityEngine.GameObject("Front/rear leaf verification camera");
var cam = go.AddComponent<UnityEngine.Camera>(); cam.enabled = false;
cam.fieldOfView = 72f; cam.nearClipPlane = .02f;
var rt = new UnityEngine.RenderTexture(1800,1100,24,UnityEngine.RenderTextureFormat.ARGB32,UnityEngine.RenderTextureReadWrite.sRGB);
cam.targetTexture = rt;
System.IO.Directory.CreateDirectory("Tools/out/previews/leaf_front_back");
foreach (var view in new[] { ("front",new UnityEngine.Vector3(0,1.6f,-1.8f),0f), ("rear",new UnityEngine.Vector3(0,1.6f,1.8f),180f) })
foreach (int week in new[] { 1,6 })
{
    facing.FaceViewer(view.Item2);
    UnityEngine.GameObject.Find("Week"+week).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
    display.CurrentModel.GetComponent<BTP.Lab.LeafRecoveryVisual>().SendMessage("Advance",2f);
    UnityEngine.Object.FindAnyObjectByType<BTP.Lab.RecoveryDemoPanel>().SendMessage("Update");
    cam.transform.SetPositionAndRotation(view.Item2,UnityEngine.Quaternion.Euler(7,view.Item3,0));
    UnityEngine.Canvas.ForceUpdateCanvases();
    foreach (var text in UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(UnityEngine.FindObjectsSortMode.None)) text.ForceMeshUpdate();
    cam.Render(); var previous = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
    var image = new UnityEngine.Texture2D(1800,1100,UnityEngine.TextureFormat.RGB24,false);
    image.ReadPixels(new UnityEngine.Rect(0,0,1800,1100),0,0); image.Apply(); UnityEngine.RenderTexture.active = previous;
    System.IO.File.WriteAllBytes("Tools/out/previews/leaf_front_back/"+view.Item1+"_week"+week+".png",image.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(image);
}
cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
facing.FaceViewer(UnityEngine.Camera.main.transform.position);
UnityEngine.GameObject.Find("Reset").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
return "Captured front and rear views at Weeks 1 and 6";
