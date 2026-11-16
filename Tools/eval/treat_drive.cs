// Drives the Treatment lab in Play mode. Actions (from Tools/eval/treat_action.txt, separated by ';'):
//   state | tool:<key>|tool:hand | target:<targetId> | tag:<tag> (first visible unmarked-or-marked target of tag) |
//   choose:<n> (option index of the current step) | advance | case:<plant>/<disease> | reset | autoplay[:route-option-index]
var path = "Tools/eval/treat_action.txt";
var script = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : "state";
var controller = UnityEngine.Object.FindAnyObjectByType<BTP.Treatment.TreatmentController>();
if (controller == null) return "no TreatmentController";
var feedbackText = UnityEngine.GameObject.Find("Feedback")?.GetComponent<TMPro.TMP_Text>();
var sb = new System.Text.StringBuilder();
string Feedback() => feedbackText != null ? feedbackText.text : "?";
string State()
{
    var s = controller.Session;
    if (s == null) return "session=null (unavailable) bullets=" + UnityEngine.GameObject.Find("Bullets")?.GetComponent<TMPro.TMP_Text>()?.text;
    var step = s.CurrentStep;
    var visible = 0; foreach (var t in controller.Targets) if (t.gameObject.activeInHierarchy) visible++;
    return $"case={s.Case.Key} step={(step == null ? "DONE" : step.StepId + ":" + step.Action + ":" + step.ToolKey + ":" + step.TargetTag)} {s.CompletedSteps}/{s.TotalSteps} route={s.Progress.Route} held={controller.HeldTool} carried={s.CarriedTargets.Count} outcome={s.Progress.Outcome} completed={s.Progress.Completed} visibleTargets={visible}";
}
BTP.Treatment.TreatmentTarget Find(string tag)
{
    var s = controller.Session;
    BTP.Treatment.TreatmentTarget fallback = null;
    foreach (var t in controller.Targets)
    {
        if (t.Tag != tag || !t.gameObject.activeInHierarchy) continue;
        var step = s.CurrentStep;
        System.Collections.Generic.HashSet<string> counted = null;
        if (step != null) s.Progress.StepTargets.TryGetValue(step.StepId, out counted);
        if (counted == null || !counted.Contains(t.TargetId)) return t;
        fallback = fallback ?? t;
    }
    return fallback;
}
void Tool(string key)
{
    foreach (var p in UnityEngine.Object.FindObjectsByType<BTP.Treatment.TreatmentToolPickup>(UnityEngine.FindObjectsInactive.Exclude))
        if ((key == "hand" && p.ToolKey == null) || p.ToolKey == key) { controller.OnToolSelected(p, null); return; }
    sb.AppendLine("  (no rack tool " + key + ")");
}
foreach (var raw in script.Split(';'))
{
    var a = raw.Trim(); if (a.Length == 0) continue;
    if (a == "state") { }
    else if (a.StartsWith("tool:")) Tool(a.Substring(5));
    else if (a.StartsWith("target:")) { foreach (var t in controller.Targets) if (t.TargetId == a.Substring(7)) controller.OnTargetSelected(t, null); }
    else if (a.StartsWith("tag:")) { var t = Find(a.Substring(4)); if (t != null) controller.OnTargetSelected(t, null); else sb.AppendLine("  (no target tag " + a.Substring(4) + ")"); }
    else if (a.StartsWith("choose:")) { var st = controller.Session.CurrentStep; controller.Choose(st.Options[int.Parse(a.Substring(7))].OptionId); }
    else if (a == "advance") controller.Advance();
    else if (a.StartsWith("case:")) { var k = a.Substring(5).Split('/'); BTP.Core.SelectionState.Instance.SelectCase(k[0], k[1]); }
    else if (a == "reset") controller.ResetCase();
    else if (a.StartsWith("autoplay"))
    {
        var routeIndex = a.Contains(":") ? int.Parse(a.Substring(9)) : 0;
        for (var guard = 0; guard < 80 && controller.Session != null && controller.Session.CurrentStep != null; guard++)
        {
            var st = controller.Session.CurrentStep;
            if (st.Action == "choose")
            {
                var idx = 0; var correct = 0;
                for (var i = 0; i < st.Options.Count; i++) if (st.Options[i].Correct) { if (correct == routeIndex || (correct == 0 && i == st.Options.Count - 1)) { idx = i; } if (correct == 0) idx = i; correct++; }
                var chosen = -1; var c2 = 0;
                for (var i = 0; i < st.Options.Count; i++) if (st.Options[i].Correct) { if (c2 == routeIndex) chosen = i; c2++; }
                if (chosen < 0) for (var i = 0; i < st.Options.Count; i++) if (st.Options[i].Correct) { chosen = i; break; }
                controller.Choose(st.Options[chosen].OptionId);
            }
            else if (st.Action == "advance") controller.Advance();
            else
            {
                if (st.RequiresCleanTool && controller.Session.IsToolContaminated(st.ToolKey)) sb.AppendLine("  !! tool contaminated before " + st.StepId);
                if (controller.HeldTool != st.ToolKey) { if (st.ToolKey == null) { if (controller.HeldTool != null) Tool(controller.HeldTool); } else Tool(st.ToolKey); }
                var t = Find(st.TargetTag);
                if (t == null) { sb.AppendLine("  !! no target for " + st.StepId + " tag " + st.TargetTag); break; }
                controller.OnTargetSelected(t, null);
            }
            sb.AppendLine("  " + st.StepId + " -> " + Feedback());
        }
    }
    else { sb.AppendLine("unknown action " + a); continue; }
    sb.AppendLine($"[{a}] feedback=\"{Feedback()}\"");
}
sb.AppendLine(State());
return sb.ToString();
