// Reports the running experience's state (Play mode).
var sb = new System.Text.StringBuilder();
sb.Append($"playing={UnityEngine.Application.isPlaying} scenes=[");
for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
{
    var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    sb.Append($"{s.name}{(s.isLoaded ? "" : "(loading)")}{(s == UnityEngine.SceneManagement.SceneManager.GetActiveScene() ? "*" : "")} ");
}
sb.Append("] ");
var env = BTP.Core.EnvironmentController.Instance;
var sel = BTP.Core.SelectionState.Instance;
sb.Append($"env={(env ? env.Current?.ToString() ?? "none" : "missing")} switching={(env && env.IsSwitching)} ");
sb.Append($"selected={(sel && sel.Current ? sel.Current.PlantId : "none")} ");
var display = UnityEngine.Object.FindFirstObjectByType<BTP.Lab.LabPlantDisplay>();
if (display) sb.Append($"labModel={(display.CurrentModel ? display.CurrentModel.name : "none")} ");
var cams = UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(UnityEngine.FindObjectsSortMode.None);
var listeners = UnityEngine.Object.FindObjectsByType<UnityEngine.AudioListener>(UnityEngine.FindObjectsSortMode.None);
var eventSystems = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(UnityEngine.FindObjectsSortMode.None);
var origins = UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(UnityEngine.FindObjectsSortMode.None);
sb.Append($"cameras={cams.Length} listeners={listeners.Length} eventSystems={eventSystems.Length} xrOrigins={origins.Length} ");
if (origins.Length > 0) sb.Append($"rigPos={origins[0].transform.position} ");
var fade = UnityEngine.Object.FindFirstObjectByType<BTP.Core.FadeOverlay>();
if (fade) sb.Append($"fade={fade.Alpha:0.00} ");
foreach (var t in UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(UnityEngine.FindObjectsSortMode.None))
    if (t.name == "WeekTitle" || t.name == "Caption" || t.name == "Selection" || (t.name == "Value" && t.transform.parent.name == "Severity"))
        sb.Append($"{t.name}='{t.text}' ");
return sb.ToString();
