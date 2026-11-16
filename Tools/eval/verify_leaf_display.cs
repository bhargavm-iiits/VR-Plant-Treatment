if (!UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Run this integration check in Play mode.");
var display = UnityEngine.Object.FindFirstObjectByType<BTP.Lab.LabPlantDisplay>();
var state = BTP.Core.SelectionState.Instance;
var report = new System.Text.StringBuilder();
int checkedConditions = 0;
var orientationData = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Tools/leaf_surface_manifest.json"));
foreach (var plant in state.Catalog.Plants)
{
    state.SelectPlant(plant.PlantId);
    foreach (var disease in BTP.Plants.DiseaseClasses.For(plant.PlantId))
    {
        if (!display.ShowDisease(disease.DiseaseId)) throw new System.Exception("Leaf unavailable: " + disease.DiseaseId);
        if (display.CurrentModel.name != plant.PlantId + "/" + disease.DiseaseId) throw new System.Exception("Wrong leaf displayed");
        foreach (var entry in orientationData["entries"])
            if ((string)entry["name"] == plant.PlantId + "_" + disease.DiseaseId)
            {
                var angles = entry["euler"];
                var expected = UnityEngine.Quaternion.Euler((float)angles[0],(float)angles[1],(float)angles[2]);
                if (UnityEngine.Quaternion.Angle(expected,display.CurrentModel.transform.Find("Leaf").localRotation) > .1f)
                    throw new System.Exception("Leaf orientation correction missing: " + disease.DiseaseId);
            }
        var renderer = display.CurrentModel.GetComponentInChildren<UnityEngine.Renderer>();
        if (renderer.sharedMaterial.shader.name != "BTP/Leaf Recovery") throw new System.Exception("Wrong material");
        var bounds = renderer.bounds;
        if (bounds.size.y > 0.9f || bounds.size.x > 0.92f || bounds.size.y < 0.08f)
            throw new System.Exception("Leaf outside display bounds: " + display.CurrentModel.name + " " + bounds.size);
        var buttons = UnityEngine.Object.FindFirstObjectByType<BTP.Lab.LeafDiseaseSelector>().GetComponentsInChildren<UnityEngine.UI.Button>();
        if (buttons.Length != BTP.Plants.DiseaseClasses.For(plant.PlantId).Count) throw new System.Exception("Wrong disease choices");
        checkedConditions++;
    }
    report.AppendLine(plant.PlantId + ": correct models and disease buttons");
}
state.SelectPlant("tomato");
display.ShowDisease("early_blight");
UnityEngine.GameObject.Find("Week6").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
var target = display.CurrentModel.GetComponentInChildren<UnityEngine.Renderer>();
var block = new UnityEngine.MaterialPropertyBlock();
target.GetPropertyBlock(block);
var animated = display.CurrentModel.GetComponent<BTP.Lab.LeafRecoveryVisual>();
if (animated.TargetRecovery != 1f || !animated.IsTransitioning) throw new System.Exception("Week 6 animation not started");
UnityEngine.GameObject.Find("Reset").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
target.GetPropertyBlock(block);
if (animated.TargetRecovery != 0f) throw new System.Exception("Reset did not target the original disease texture");
var diseaseSelector = UnityEngine.Object.FindFirstObjectByType<BTP.Lab.LeafDiseaseSelector>();
diseaseSelector.transform.Find("Disease_0").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
if (display.CurrentDiseaseId != "bacterial_spot") throw new System.Exception("Top disease button did not swap leaf");
UnityEngine.GameObject.Find("NextDisease").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
if (display.CurrentDiseaseId != "early_blight") throw new System.Exception("Side disease navigation did not swap leaf");
return "PASS " + checkedConditions + " leaf conditions; Week 6, Reset, direct disease and side navigation.\n" + report;
