if (!UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Play mode required.");
var display = UnityEngine.Object.FindAnyObjectByType<BTP.Lab.LabPlantDisplay>();
var state = BTP.Core.SelectionState.Instance;
var data = UnityEditor.AssetDatabase.LoadAssetAtPath<BTP.Lab.TomatoRecoveryData>("Assets/_Project/Data/TomatoRecoveryData.asset");
var ui = UnityEngine.Object.FindAnyObjectByType<BTP.Lab.RecoveryDemoPanel>().transform;
var uiPosition = ui.position;
var uiRotation = ui.rotation;
var camera = UnityEngine.Camera.main;
var input = typeof(BTP.Lab.LeafMouseRotation).GetMethod("ProcessPointer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
int checks = 0;
foreach (var plant in state.Catalog.Plants)
{
    state.SelectPlant(plant.PlantId);
    foreach (var disease in BTP.Plants.DiseaseClasses.For(plant.PlantId))
    {
        display.ShowDisease(disease.DiseaseId);
        var model = display.CurrentModel;
        var turn = model.GetComponent<BTP.Lab.LeafMouseRotation>();
        var pivot = model.transform.Find("Leaf Spin Pivot");
        if (turn == null || pivot == null) throw new System.Exception("Rotation missing: " + model.name);
        var centre = pivot.position;
        var wrapper = model.transform.rotation;
        var pointer = (UnityEngine.Vector2)camera.WorldToScreenPoint(centre);
        var original = pivot.localRotation;
        input.Invoke(turn, new object[] { camera, pointer, true, true, false });
        if (!turn.IsDragging) throw new System.Exception("Cannot grab leaf: " + model.name);
        pointer += UnityEngine.Vector2.right * (180f / .35f);
        input.Invoke(turn, new object[] { camera, pointer, false, true, false });
        if (UnityEngine.Quaternion.Angle(original, pivot.localRotation) < 179.9f) throw new System.Exception("Leaf did not turn 180 degrees: " + model.name);
        if (UnityEngine.Vector3.Distance(centre, pivot.position) > .0001f || UnityEngine.Quaternion.Angle(wrapper,model.transform.rotation) > .001f)
            throw new System.Exception("Specimen frame moved: " + model.name);
        var visual = model.GetComponent<BTP.Lab.LeafRecoveryVisual>();
        visual.Show(data.GetWeek(5), data.GetWeek(1), data.GetWeek(6), display.ModelRoot);
        visual.SendMessage("Advance", 2f);
        pointer += new UnityEngine.Vector2(100f, 40f);
        input.Invoke(turn, new object[] { camera, pointer, false, true, false });
        // Refresh after a turn with the week animation already finished.
        visual.SendMessage("LateUpdate");
        foreach (var renderer in model.GetComponentsInChildren<UnityEngine.Renderer>())
        {
            var block = new UnityEngine.MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            var plane = block.GetVector("_SplitPlane");
            var normal = new UnityEngine.Vector3(plane.x,plane.y,plane.z);
            var expected = renderer.transform.localToWorldMatrix.transpose.MultiplyVector(display.ModelRoot.right).normalized;
            if (UnityEngine.Vector3.Dot(normal,expected) < .999f) throw new System.Exception("Comparison plane stopped tracking rotation: " + model.name);
        }
        visual.Show(data.GetWeek(6), data.GetWeek(1), data.GetWeek(6), display.ModelRoot);
        visual.SendMessage("Advance", 2f);
        if (visual.DisplayedRecovery != 1 || visual.DisplayedBaselineRecovery != 1) throw new System.Exception("Week 6 recovery changed: " + model.name);
        var released = pivot.localRotation;
        input.Invoke(turn, new object[] { camera, pointer, false, false, false });
        input.Invoke(turn, new object[] { camera, pointer + UnityEngine.Vector2.right * 100f, false, false, false });
        if (turn.IsDragging || UnityEngine.Quaternion.Angle(released,pivot.localRotation) > .001f) throw new System.Exception("Leaf continued turning after release: " + model.name);
        checks++;
    }
}
if (UnityEngine.Vector3.Distance(uiPosition, ui.position) > .0001f || UnityEngine.Quaternion.Angle(uiRotation,ui.rotation) > .001f)
    throw new System.Exception("Leaf rotation moved the controls.");
state.SelectPlant("apple");
var active = display.CurrentModel.transform.Find("Leaf Spin Pivot");
var start = active.localRotation;
double started = UnityEditor.EditorApplication.timeSinceStartup;
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () =>
{
    if (UnityEditor.EditorApplication.timeSinceStartup - started < 1.2) return;
    UnityEditor.EditorApplication.update -= tick;
    float turned = UnityEngine.Quaternion.Angle(start,active.localRotation);
    var result = turned < .001f ? "PASS" : "FAIL";
    var path = System.IO.Path.GetFullPath("Tools/out/previews/leaf_rotation/result.txt");
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
    System.IO.File.WriteAllText(path,result + ": 24 conditions; mouse drag turns and tilts, release stops, fixed specimen centre/controls, comparison plane tracks rotation, whole-leaf Week 6 recovery. Idle movement: " + turned + " degrees.");
};
UnityEditor.EditorApplication.update += tick;
return "PASS: " + checks + " drag/release checks. Live idle measurement pending in Tools/out/previews/leaf_rotation/result.txt";
