// Run against the freshly recompiled runtime in a normal farm-to-lab session.
var display = UnityEngine.Object.FindAnyObjectByType<BTP.Lab.LabPlantDisplay>();
var facing = display.GetComponent<BTP.Lab.LeafViewerFacing>();
if (facing == null) throw new System.Exception("Viewer-facing display not wired into the Lab scene.");
var state = BTP.Core.SelectionState.Instance;
int checks = 0;
var sizes = new System.Collections.Generic.Dictionary<string,float>();
foreach (var eye in new[] { new UnityEngine.Vector3(0,1.6f,-1.8f), new UnityEngine.Vector3(0,1.6f,1.8f), new UnityEngine.Vector3(1.8f,1.6f,0), new UnityEngine.Vector3(-1.8f,1.6f,0) })
{
    facing.FaceViewer(eye);
    var towards = eye-display.transform.position; towards.y = 0;
    if (UnityEngine.Vector3.Dot(-display.ModelRoot.forward,towards.normalized) < .999f) throw new System.Exception("Leaf facing away from viewer.");
    foreach (var plant in state.Catalog.Plants)
    {
        state.SelectPlant(plant.PlantId);
        foreach (var disease in BTP.Plants.DiseaseClasses.For(plant.PlantId))
        {
            display.ShowDisease(disease.DiseaseId);
            var visual = display.CurrentModel.GetComponent<BTP.Lab.LeafRecoveryVisual>();
            UnityEngine.GameObject.Find("Week6").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            visual.SendMessage("Advance",2f);
            UnityEngine.Object.FindAnyObjectByType<BTP.Lab.RecoveryDemoPanel>().SendMessage("Update");
            if (visual.DisplayedRecovery != 1f || visual.DisplayedBaselineRecovery != 1f) throw new System.Exception("Week 6 did not recover the whole leaf.");
            string key = plant.PlantId+"/"+disease.DiseaseId;
            float size = display.CurrentModel.transform.localScale.x;
            if (sizes.TryGetValue(key,out var original) && UnityEngine.Mathf.Abs(size-original) > .001f) throw new System.Exception("Leaf size changed with viewpoint.");
            sizes[key] = size;
            if (display.transform.Find("ComparisonMarker").gameObject.activeSelf) throw new System.Exception("Week 6 comparison divider should be hidden.");
            checks++;
        }
    }
}
state.SelectPlant("apple"); display.ShowDisease("apple_scab");
facing.FaceViewer(new UnityEngine.Vector3(0,1.6f,-1.8f));
return "PASS: "+checks+" leaf/view checks. Front, rear, left and right views; stable sizing and fully healthy Week 6 for all 24 conditions.";
