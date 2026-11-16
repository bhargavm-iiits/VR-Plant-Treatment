if (!UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Play mode required.");
UnityEngine.Application.runInBackground = true;
var display = UnityEngine.Object.FindAnyObjectByType<BTP.Lab.LabPlantDisplay>();
BTP.Core.SelectionState.Instance.SelectPlant("tomato");
UnityEngine.GameObject.Find("Reset").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
display.ShowDisease("bacterial_spot"); display.ShowDisease("early_blight");
var visual = display.CurrentModel.GetComponent<BTP.Lab.LeafRecoveryVisual>();
foreach (var t in display.CurrentModel.GetComponentsInChildren<UnityEngine.Transform>()) t.gameObject.layer = 30;
var go = new UnityEngine.GameObject("Recovery animation QA camera");
var camera = go.AddComponent<UnityEngine.Camera>(); camera.enabled = false;
camera.orthographic = true; camera.orthographicSize = 0.53f;
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor; camera.backgroundColor = new UnityEngine.Color(.06f,.075f,.09f);
camera.cullingMask = 1 << 30;
camera.transform.SetPositionAndRotation(new UnityEngine.Vector3(0,1.26f,-1f),UnityEngine.Quaternion.identity);
var rt = new UnityEngine.RenderTexture(600,600,24); camera.targetTexture = rt;
System.IO.Directory.CreateDirectory("Tools/out/previews/recovery_motion");
var samples = new System.Collections.Generic.List<object>();
var clock = System.Diagnostics.Stopwatch.StartNew();
int index = 0; int stage = 0; float previous = 0f;
UnityEngine.GameObject.Find("Week6").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () =>
{
    try
    {
        float time = (float)clock.Elapsed.TotalSeconds;
        if (stage == 0 && time >= index * .10f && index < 20)
        {
            float amount = visual.DisplayedRecovery;
            if (amount + .001f < previous) throw new System.Exception("Recovery animation moved backwards.");
            previous = amount;
            samples.Add(new { seconds = time, recovery = amount });
            camera.Render();
            var old = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
            var image = new UnityEngine.Texture2D(600,600,UnityEngine.TextureFormat.RGB24,false);
            image.ReadPixels(new UnityEngine.Rect(0,0,600,600),0,0); image.Apply(); UnityEngine.RenderTexture.active = old;
            System.IO.File.WriteAllBytes("Tools/out/previews/recovery_motion/frame_"+index.ToString("D2")+".png",image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image); index++;
        }
        if (stage == 0 && time > 2.1f)
        {
            if (visual.DisplayedRecovery < .999f) throw new System.Exception("Recovery did not reach its endpoint.");
            UnityEngine.GameObject.Find("Week2").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            if (visual.DisplayedRecovery < .999f) throw new System.Exception("Reverse selection jumped immediately.");
            stage = 1; clock.Restart();
            return;
        }
        if (stage == 1 && time > .5f && time < .9f)
        {
            if (visual.DisplayedRecovery <= visual.TargetRecovery || visual.DisplayedRecovery >= 1f)
                throw new System.Exception("Reverse animation did not interpolate.");
        }
        if (stage == 1 && time > 2.1f)
        {
            if (UnityEngine.Mathf.Abs(visual.DisplayedRecovery-visual.TargetRecovery) > .001f)
                throw new System.Exception("Reverse animation did not settle.");
            System.IO.File.WriteAllText("Tools/out/previews/recovery_motion/result.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { passed = true, samples, reverse = visual.DisplayedRecovery },Newtonsoft.Json.Formatting.Indented));
            UnityEditor.EditorApplication.update -= tick;
            camera.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
        }
    }
    catch (System.Exception error)
    {
        UnityEditor.EditorApplication.update -= tick;
        System.IO.File.WriteAllText("Tools/out/previews/recovery_motion/result.json",Newtonsoft.Json.JsonConvert.SerializeObject(new { passed = false, error = error.ToString() }));
        camera.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
    }
};
UnityEditor.EditorApplication.update += tick;
return "Started real-time forward/reverse animation check and frame capture";
