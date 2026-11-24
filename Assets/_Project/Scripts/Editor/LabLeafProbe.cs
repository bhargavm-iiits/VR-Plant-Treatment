using System.IO;
using System.Linq;
using System.Text;
using BTP.Lab;
using BTP.Treatment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Editor
{
    /// <summary>
    /// Batch Play-mode check of picking up the Visiting Lab leaf with the grip toggle, as the Treatment tools are (no
    /// headset): plays Lab.unity, forces the controllers on and points the right one at the leaf from the spawn view. The
    /// grip press goes through the real <see cref="ToolGripToggle"/> (<see cref="ToolGripToggle.PressOverride"/> stands in
    /// for the Input System grip action): the leaf must come into the hand and stay held with no button down, follow the
    /// hand and turn 180 degrees with it, and a second press must put it back on the stand in its original pose.
    /// Batch: -executeMethod BTP.Editor.LabLeafProbe.Run (no -quit, no -nographics); report in Tools/logs/lab_leaf_probe.txt,
    /// exit code 1 when a check fails.
    /// </summary>
    [InitializeOnLoad]
    public static class LabLeafProbe
    {
        const string Key = "BTP.LabLeafProbe";
        const string OutPath = "Tools/logs/lab_leaf_probe.txt";
        static int ticks, stage, stageFrame, pressFrame, failures;
        static float stageTime;
        static readonly StringBuilder Log = new StringBuilder();
        static Vector3 homePosition;
        static Quaternion homeRotation, handStart, leafStart;

        static LabLeafProbe()
        {
            if (SessionState.GetInt(Key, 0) != 0)
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            File.WriteAllText(OutPath, "lab probe started\n");
            SessionState.SetInt(Key, 1);
            EditorSceneManager.OpenScene("Assets/Scenes/Lab.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void Line(string s)
        {
            Log.AppendLine(s);
            File.WriteAllText(OutPath, Log.ToString());
        }

        static void Check(bool ok, string what)
        {
            if (!ok)
                failures++;
            Line((ok ? "  PASS " : "  FAIL ") + what);
        }

        static void Next()
        {
            stage++;
            stageFrame = Time.frameCount;
            stageTime = Time.realtimeSinceStartup;
        }

        static void Press() => pressFrame = Time.frameCount + 1;

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (ticks > 0)
                {
                    SessionState.SetInt(Key, 0);
                    EditorApplication.update -= Tick;
                    ToolGripToggle.PressOverride = null;
                    Line(failures == 0 ? "RESULT: all checks passed" : $"RESULT: {failures} check(s) FAILED");
                    EditorApplication.Exit(failures == 0 ? 0 : 1);
                }
                return;
            }

            if (ticks++ == 0)
                stageTime = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - stageTime > 120f)
            {
                Line($"TIMEOUT at stage {stage}");
                failures++;
                EditorApplication.ExitPlaymode();
                return;
            }

            var leaf = Object.FindAnyObjectByType<LeafMouseRotation>();
            var controllers = Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include)
                .Where(i => i.transform.parent != null && i.transform.parent.name.Contains("Controller")).ToList();
            var right = controllers.FirstOrDefault(c => c.handedness == InteractorHandedness.Right);
            var ctl = right != null ? right.transform.parent : null;
            var frames = Time.frameCount - stageFrame;
            switch (stage)
            {
                case 0:
                    if (leaf == null || leaf.Grabbable == null || right == null || Time.realtimeSinceStartup - stageTime < 8f)
                        return;
                    foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                        if (b.GetType().Name == "XRInputModalityManager" || b.GetType().Name == "DesktopDemoControls")
                            b.enabled = false;
                    foreach (var b in Object.FindObjectsByType<Behaviour>(FindObjectsInactive.Include))
                        if (b.GetType().Name.Contains("TrackedPoseDriver"))
                            b.enabled = false;
                    foreach (var c in controllers)
                        c.transform.parent.gameObject.SetActive(true);
                    var centre = Centre(leaf);
                    var head = Camera.main.transform;
                    var spawn = Object.FindAnyObjectByType<BTP.Core.SpawnAnchor>();
                    var eye = spawn != null ? spawn.transform.position + Vector3.up * 1.6f : centre + new Vector3(0f, 0.5f, -1.2f);
                    head.SetPositionAndRotation(eye, Quaternion.LookRotation(centre - eye, Vector3.up));
                    var from = eye + head.right * 0.2f - Vector3.up * 0.35f;
                    var local = Quaternion.Inverse(ctl.rotation) * right.transform.rotation;
                    ctl.rotation = Quaternion.LookRotation(centre - from, Vector3.up) * Quaternion.Inverse(local);
                    ctl.position += from - right.transform.position;
                    homePosition = leaf.Pivot.position;
                    homeRotation = leaf.Pivot.rotation;
                    ToolGripToggle.PressOverride = hand => hand == InteractorHandedness.Right && Time.frameCount == pressFrame;
                    Line($"leaf '{leaf.name}' centre {centre}; eye {eye}; controller ray from {right.transform.position} dir {right.transform.forward}");
                    Check(ToolGripToggle.Active, "the grip toggle is running in the Lab");
                    Check(ToolGripToggle.Registered.Contains(leaf.Grabbable), "the leaf is one of its hand-held items");
                    Next();
                    return;
                case 1:
                    if (frames < 30)
                        return;
                    Line($"XRI hovered by the right controller: [{string.Join(", ", right.interactablesHovered.Select(h => (h as Object)?.name))}]; " +
                         $"cue visible {leaf.Cue != null && leaf.Cue.Visible} ('{leaf.Cue?.Label}')");
                    Check(right.interactablesHovered.Contains(leaf.Grabbable.Grab), "the controller ray hovers the leaf");
                    Check(leaf.Cue != null && leaf.Cue.Visible && leaf.Cue.Label == LeafGrabCue.HoverText, "hover shows the ring and 'Press GRIP to pick up the leaf'");
                    Press();
                    Next();
                    return;
                case 2:
                    if (Time.realtimeSinceStartup - stageTime < 1.5f)
                        return;
                    var attach = right.GetAttachTransform(leaf.Grabbable.Grab);
                    var gap = Vector3.Distance(leaf.Grip.position, attach.position);
                    Line($"held {leaf.Grabbable.IsHeld} by {leaf.Grabbable.HeldBy}; selected {leaf.Grabbable.Grab.isSelected}; toggle holder {(leaf.Grabbable.ToggleHolder != null)}; " +
                         $"stem {gap:0.000} m from the hand's attach point");
                    Check(leaf.Grabbable.IsHeld && leaf.Grabbable.Grab.isSelected && ReferenceEquals(leaf.Grabbable.ToggleHolder, right),
                        "one grip press picks the leaf up into the right hand (grip toggle)");
                    Check(leaf.IsHeld, "it stays held with no button down");
                    Check(gap < 0.05f, "the leaf comes to the hand, held by its stem");
                    handStart = ctl.rotation;
                    leafStart = leaf.Pivot.rotation;
                    Next();
                    return;
                case 3:
                    // Roll the controller 180 degrees (turn the wrist) over 60 frames, then let the hold settle.
                    if (frames <= 60)
                    {
                        ctl.rotation = Quaternion.AngleAxis(3f, ctl.forward) * ctl.rotation;
                        return;
                    }

                    if (frames < 120)
                        return;
                    var expected = ctl.rotation * Quaternion.Inverse(handStart) * leafStart;
                    var leafTurn = Quaternion.Angle(leafStart, leaf.Pivot.rotation);
                    Line($"hand turned {Quaternion.Angle(handStart, ctl.rotation):0.0} deg, leaf turned {leafTurn:0.0} deg " +
                         $"(off the hand by {Quaternion.Angle(expected, leaf.Pivot.rotation):0.00} deg)");
                    Check(leafTurn > 175f && Quaternion.Angle(expected, leaf.Pivot.rotation) < 3f, "turning the wrist turns the held leaf front to back with the hand");
                    Check(Vector3.Dot(leaf.Pivot.forward, leafStart * Vector3.forward) < -0.9f, "its back now faces where its front did");
                    Press();
                    Next();
                    return;
                case 4:
                    if (!leaf.Grabbable.AtHome)
                    {
                        if (Time.realtimeSinceStartup - stageTime > 5f)
                        {
                            Check(false, "a second grip press puts the leaf back on the stand");
                            stage = 99;
                            EditorApplication.ExitPlaymode();
                        }

                        return;
                    }

                    Check(!leaf.Grabbable.Grab.isSelected && leaf.Grabbable.ToggleHolder == null, "a second grip press lets go (grip toggle)");
                    Check(Vector3.Distance(leaf.Pivot.position, homePosition) < 1e-3f && Quaternion.Angle(leaf.Pivot.rotation, homeRotation) < 0.1f,
                        "and the leaf eases back to its stand in its original pose");
                    Check(!leaf.IsHeld && !BTP.Core.VrControllerRig.IsHoldingTool(right), "the controller is free again");
                    stage = 99;
                    EditorApplication.ExitPlaymode();
                    return;
            }
        }

        static Vector3 Centre(LeafMouseRotation leaf) =>
            leaf.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }).center;
    }
}
