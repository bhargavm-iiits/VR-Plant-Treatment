// Performs one journey action in Play mode. The action name is read from Tools/eval/play_action.txt.
var action = System.IO.File.ReadAllText("Tools/eval/play_action.txt").Trim();
UnityEngine.UI.Button FindButton(string name)
{
    foreach (var b in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(UnityEngine.FindObjectsSortMode.None))
        if (b.name == name) return b;
    return null;
}
if (action.StartsWith("select:"))
{
    var id = action.Substring(7);
    foreach (var plant in UnityEngine.Object.FindObjectsByType<BTP.Farm.SelectablePlant>(UnityEngine.FindObjectsSortMode.None))
    {
        if (plant.PlantId != id) continue;
        var interactable = plant.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        interactable.selectEntered.Invoke(new UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs());
        return $"selected specimen {id}";
    }
    return $"no specimen {id}";
}
var button = FindButton(action);
if (button == null) return $"button '{action}' not found";
if (!button.interactable) return $"button '{action}' is not interactable";
button.onClick.Invoke();
return $"clicked {action}";
