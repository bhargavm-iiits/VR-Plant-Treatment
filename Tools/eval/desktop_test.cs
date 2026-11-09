// Desktop demo check (Play mode): action in Tools/eval/desktop_action.txt
// "press:<plantId>" aims the cursor at that specimen and holds left click, "press:ui:<ButtonName>" at a button, "release", or "report".
var action = System.IO.File.ReadAllText("Tools/eval/desktop_action.txt").Trim();
var controls = UnityEngine.Object.FindFirstObjectByType<BTP.Core.DesktopDemoControls>();
if (controls == null) return "no DesktopDemoControls";
var cam = controls.GetComponentInChildren<UnityEngine.Camera>();
var mouse = UnityEngine.InputSystem.Mouse.current;
UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor ray = null; foreach (var r in controls.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true)) if (r.name == "Desktop Mouse Ray") ray = r;
string Report()
{
    var sel = BTP.Core.SelectionState.Instance;
    var env = BTP.Core.EnvironmentController.Instance;
    var hovered = ray != null && ray.interactablesHovered.Count > 0 ? ray.interactablesHovered[0].transform.name : "none";
    var uiHit = ray != null && ray.TryGetCurrentUIRaycastResult(out var ui) ? ui.gameObject.name : "none";
    return $"camFwd={cam.transform.forward} frame={UnityEngine.Time.frameCount} rayPos={(ray ? ray.transform.forward.ToString() : "")} mouse={mouse.position.ReadValue()} desktop={controls.IsActive} xrDevice={UnityEngine.XR.XRSettings.isDeviceActive} ray={(ray ? ray.gameObject.activeInHierarchy.ToString() : "missing")} hovered={hovered} ui={uiHit} selected={(sel && sel.Current ? sel.Current.PlantId : "none")} env={(env ? env.Current?.ToString() : "?")} camLocal={cam.transform.localPosition} paused={UnityEditor.EditorApplication.isPaused}";
}
void Queue(UnityEngine.Vector2 pos, bool down)
{
    var state = new UnityEngine.InputSystem.LowLevel.MouseState { position = pos };
    if (down) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, true);
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, state);
}
if (action == "report") return Report();
if (action == "release") { Queue(mouse.position.ReadValue(), false); return "released"; }
if (action.StartsWith("press:"))
{
    var target = action.Substring(6);
    UnityEngine.Vector3 point;
    if (target.StartsWith("ui:"))
    {
        UnityEngine.UI.Button button = null;
        foreach (var b in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(UnityEngine.FindObjectsSortMode.None))
            if (b.name == target.Substring(3)) button = b;
        if (button == null) return "no button " + target;
        point = button.transform.position;
    }
    else
    {
        BTP.Farm.SelectablePlant plant = null;
        foreach (var p in UnityEngine.Object.FindObjectsByType<BTP.Farm.SelectablePlant>(UnityEngine.FindObjectsSortMode.None))
            if (p.PlantId == target) plant = p;
        if (plant == null) return "no plant " + target;
        var col = plant.GetComponentInChildren<UnityEngine.Collider>();
point = col.bounds.center;
var b = col.bounds;
for (var i = 0; i < 125; i++)
{
    var candidate = b.min + UnityEngine.Vector3.Scale(b.size, new UnityEngine.Vector3((i % 5 + 0.5f) / 5f, (i / 5 % 5 + 0.5f) / 5f, (i / 25 + 0.5f) / 5f));
    var r = new UnityEngine.Ray(cam.transform.position, candidate - cam.transform.position);
    if (UnityEngine.Physics.Raycast(r, out var hit, 60f) && hit.collider == col) { point = hit.point; break; }
}
    }
    // Turn the rig so the target is in front of the camera, as a user would with right-drag.
    var flat = UnityEngine.Vector3.ProjectOnPlane(point - cam.transform.position, UnityEngine.Vector3.up);
    var fwd = UnityEngine.Vector3.ProjectOnPlane(cam.transform.forward, UnityEngine.Vector3.up);
    controls.transform.RotateAround(cam.transform.position, UnityEngine.Vector3.up, UnityEngine.Vector3.SignedAngle(fwd, flat, UnityEngine.Vector3.up));
    var dir = point - cam.transform.position; var pitchDeg = -UnityEngine.Mathf.Atan2(dir.y, new UnityEngine.Vector2(dir.x, dir.z).magnitude) * UnityEngine.Mathf.Rad2Deg; cam.transform.localRotation = UnityEngine.Quaternion.Euler(pitchDeg, 0f, 0f); var screen = cam.WorldToScreenPoint(point); screen.z = cam.pixelHeight;
    Queue(new UnityEngine.Vector2(screen.x, screen.y), true);
    return $"pressed at {screen} target {target}";
}
return "unknown action";





