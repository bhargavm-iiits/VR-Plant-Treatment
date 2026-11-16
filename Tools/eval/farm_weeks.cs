// Farm six-week timeline check (Play mode). Action in Tools/eval/farm_weeks_action.txt:
// "goto:<plantId>:<dx>:<dz>" puts the desktop rig at the specimen + (dx, dz) metres;
// "shot:<name>" saves the desktop camera view; "view:<name>:x:y:z:pitch:yaw" saves a view from a temporary camera;
// "report" describes the ring, console and crop states.
var action = System.IO.File.ReadAllText("Tools/eval/farm_weeks_action.txt").Trim();
var controls = UnityEngine.Object.FindAnyObjectByType<BTP.Core.DesktopDemoControls>();
var cam = controls != null ? controls.GetComponentInChildren<UnityEngine.Camera>() : UnityEngine.Camera.main;
var outDir = System.IO.Path.GetFullPath("Tools/out/previews/farm_weeks");
System.IO.Directory.CreateDirectory(outDir);
string Save(UnityEngine.Camera camera, string name)
{
    var rt = new UnityEngine.RenderTexture(1600, 900, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
    var previous = camera.targetTexture;
    camera.targetTexture = rt;
    camera.Render();
    camera.targetTexture = previous;
    var active = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = rt;
    var tex = new UnityEngine.Texture2D(rt.width, rt.height, UnityEngine.TextureFormat.RGB24, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply();
    UnityEngine.RenderTexture.active = active;
    var path = System.IO.Path.Combine(outDir, name + ".png");
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex);
    rt.Release();
    UnityEngine.Object.DestroyImmediate(rt);
    return path;
}
if (action.StartsWith("goto:"))
{
    var parts = action.Split(':');
    foreach (var p in UnityEngine.Object.FindObjectsByType<BTP.Farm.SelectablePlant>())
    {
        if (p.PlantId != parts[1]) continue;
        var target = p.transform.position + new UnityEngine.Vector3(float.Parse(parts[2]), 0f, float.Parse(parts[3]));
        target.y = 0f;
        controls.transform.position = target;
        var flat = UnityEngine.Vector3.ProjectOnPlane(p.transform.position - cam.transform.position, UnityEngine.Vector3.up);
        var fwd = UnityEngine.Vector3.ProjectOnPlane(cam.transform.forward, UnityEngine.Vector3.up);
        controls.transform.RotateAround(cam.transform.position, UnityEngine.Vector3.up, UnityEngine.Vector3.SignedAngle(fwd, flat, UnityEngine.Vector3.up));
        return $"rig at {controls.transform.position} distance {UnityEngine.Vector3.ProjectOnPlane(p.transform.position - cam.transform.position, UnityEngine.Vector3.up).magnitude:0.00}";
    }
    return "no specimen " + parts[1];
}
if (action.StartsWith("uipress:") || action == "uirelease")
{
    var mouse = UnityEngine.InputSystem.Mouse.current;
    if (action == "uirelease")
    {
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = mouse.position.ReadValue() });
        return "released";
    }
    UnityEngine.UI.Button button = null;
    foreach (var bt in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>())
        if (bt.name == action.Substring(8)) button = bt;
    if (button == null) return "no button";
    var rt = (UnityEngine.RectTransform)button.transform;
    var point = rt.TransformPoint(rt.rect.center);
    var dir = point - cam.transform.position;
    var flat = UnityEngine.Vector3.ProjectOnPlane(dir, UnityEngine.Vector3.up);
    var fwd = UnityEngine.Vector3.ProjectOnPlane(cam.transform.forward, UnityEngine.Vector3.up);
    controls.transform.RotateAround(cam.transform.position, UnityEngine.Vector3.up, UnityEngine.Vector3.SignedAngle(fwd, flat, UnityEngine.Vector3.up));
    cam.transform.localRotation = UnityEngine.Quaternion.Euler(-UnityEngine.Mathf.Atan2(dir.y, flat.magnitude) * UnityEngine.Mathf.Rad2Deg, 0f, 0f);
    var screen = cam.WorldToScreenPoint(point);
    var state = new UnityEngine.InputSystem.LowLevel.MouseState { position = new UnityEngine.Vector2(screen.x, screen.y) }
        .WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, true);
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, state);
    return $"pressing {button.name} at {screen} of {cam.pixelWidth}x{cam.pixelHeight}";
}
if (action == "ray")
{
    UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor ray = null;
    foreach (var r in controls.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true)) if (r.name == "Desktop Mouse Ray") ray = r;
    var ui = ray != null && ray.TryGetCurrentUIRaycastResult(out var hit) ? hit.gameObject.name + "/" + hit.gameObject.transform.parent.name : "none";
    return $"camFwd={cam.transform.forward} rayFwd={(ray ? ray.transform.forward : UnityEngine.Vector3.zero)} ui={ui} mouse={UnityEngine.InputSystem.Mouse.current.position.ReadValue()} screen={UnityEngine.Screen.width}x{UnityEngine.Screen.height}";
}
if (action.StartsWith("shot:"))
{
    // Look at the plant/console area a little below the horizon so ring and console are both in frame.
    var parts = action.Split(':');
    if (parts.Length > 2) cam.transform.localRotation = UnityEngine.Quaternion.Euler(float.Parse(parts[2]), 0f, 0f);
    return Save(cam, parts[1]);
}
if (action.StartsWith("view:"))
{
    var p = action.Split(':');
    var go = new UnityEngine.GameObject("FarmWeeksCamera");
    var c = go.AddComponent<UnityEngine.Camera>();
    c.enabled = false;
    c.fieldOfView = 60f;
    c.farClipPlane = 450f;
    go.transform.SetPositionAndRotation(new UnityEngine.Vector3(float.Parse(p[2]), float.Parse(p[3]), float.Parse(p[4])),
        UnityEngine.Quaternion.Euler(float.Parse(p[5]), float.Parse(p[6]), 0f));
    var path = Save(c, p[1]);
    UnityEngine.Object.Destroy(go);
    return path;
}
var sb = new System.Text.StringBuilder();
var gallery = UnityEngine.Object.FindAnyObjectByType<BTP.Farm.DiseaseGalleryController>();
var driver = UnityEngine.Object.FindAnyObjectByType<BTP.Farm.FarmCropRecovery>();
sb.Append($"frame={UnityEngine.Time.frameCount} ringOpen={(gallery && gallery.IsOpen)} ringPlant={(gallery ? gallery.OpenPlantId : "-")} ");
if (gallery && gallery.Console) sb.Append($"console={gallery.Console.IsVisible}:{gallery.Console.PlantId} ");
foreach (var id in new[] { "tomato", "apple", "potato", "bell_pepper", "strawberry" })
    sb.Append($"{id}=W{BTP.Farm.FarmRecoveryState.GetWeek(id)}/{(driver ? driver.CurrentAmount(id) : -1):0.00} ");
foreach (var t in UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>())
    if (t.name == "Status" || (t.name == "Title" && t.transform.parent.name == "TimelineConsole")) sb.Append($"{t.name}='{t.text}' ");
var sel = BTP.Core.SelectionState.Instance;
sb.Append($"selected={(sel && sel.Current ? sel.Current.PlantId : "none")} lastGuard={BTP.Farm.FarmConsolePressGuard.LastPressFrame}");
// Renderers on the tomato crop that use a runtime copy.
var runtime = 0; var asset = 0;
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>())
    foreach (var m in r.sharedMaterials)
        if (m != null && m.name.StartsWith("Tomato_")) { if (m.name.EndsWith("(runtime)")) runtime++; else asset++; }
sb.Append($" tomatoRenderers runtime={runtime} asset={asset}");
return sb.ToString();
