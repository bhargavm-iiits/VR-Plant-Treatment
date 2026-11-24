using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BTP.Treatment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace BTP.Editor
{
    /// <summary>
    /// Batch Play-mode check of the controller grip toggle on the real rig and the real grain scoop (no headset): plays
    /// Treatment.unity (BootstrapGuard loads XRBootstrap), forces the controllers on (no controllers are tracked in the
    /// Editor), puts the scoop on a bench pose with the guided HUD chip at the hand, then checks: the right controller at the
    /// handle hovers the scoop; ONE grip press picks it up and it stays held with no button; it follows the controller;
    /// the next press releases it and it eases home; a press while pointing from 1.2 m picks it up into the hand; the left
    /// controller toggles the same way. Grip presses are injected through <see cref="ToolGripToggle.PressOverride"/>.
    /// Batch: -executeMethod BTP.Editor.ScoopGrabProbe.Run (never -nographics, no -quit); report in Tools/logs/scoop_probe.txt,
    /// exit code 1 when a check fails.
    /// </summary>
    [InitializeOnLoad]
    public static class ScoopGrabProbe
    {
        const string Key = "BTP.ScoopGrabProbe";
        const string OutPath = "Tools/logs/scoop_probe.txt";
        static int frame, stage, stageFrame, failures;
        static float stageTime;
        static readonly StringBuilder Log = new StringBuilder();

        static ScoopGrabProbe()
        {
            if (SessionState.GetInt(Key, 0) == 0)
                return;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            File.WriteAllText(OutPath, "probe started\n");
            SessionState.SetInt(Key, 1);
            failures = 0;
            SpecimenSurfaceBaker.Bake();
            EditorSceneManager.OpenScene("Assets/Scenes/Treatment.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void Line(string s)
        {
            Log.AppendLine(s);
            File.WriteAllText(OutPath, Log.ToString());
        }

        static void Finish()
        {
            SessionState.SetInt(Key, 0);
            EditorApplication.update -= Tick;
            Line(failures == 0 ? "RESULT: all checks passed" : $"RESULT: {failures} check(s) FAILED");
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (frame > 0)
                    Finish();
                return;
            }

            frame++;
            if (frame > 60000)
            {
                Line("TIMEOUT stage " + stage);
                failures++;
                Finish();
                return;
            }

            var scoop = Object.FindAnyObjectByType<FertiliserScoop>();
            var head = Camera.main != null ? Camera.main.transform : null;
            var pill = GameObject.Find("ChipWidget");
            switch (stage)
            {
                case 0:
                    if (scoop == null || scoop.Grabbable == null || head == null || frame < 400)
                        return;
                    Line($"frame {frame}: scoop at {scoop.transform.position} home {scoop.Grabbable.HomePosition}; chip widget {(pill != null ? pill.activeInHierarchy.ToString() : "missing")}");
                    foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                        if (b.GetType().Name == "XRInputModalityManager" || b.GetType().Name == "DesktopDemoControls")
                            b.enabled = false;
                    foreach (var b in Object.FindObjectsByType<Behaviour>(FindObjectsInactive.Include))
                        if (b.GetType().Name.Contains("TrackedPoseDriver"))
                            b.enabled = false;
                    foreach (var interactor in Controllers())
                        interactor.transform.parent.gameObject.SetActive(true);
                    // What Layout() does on the headset: the tools are sent to their bench homes (SetHome moves a tool that is home).
                    var bench = new Vector3(5.6f, 0.88f, 2.3f);
                    scoop.Grabbable.SetHome(bench, Quaternion.Euler(0f, 180f, 0f));
                    var i2 = 0;
                    foreach (var packet in Object.FindObjectsByType<FertiliserPacket>(FindObjectsInactive.Include))
                        packet.Grabbable.SetHome(bench + new Vector3(0f, 0f, 0.3f + 0.25f * i2++), Quaternion.identity);
                    Line($"SetHome -> scoop transform {scoop.transform.position} body {scoop.GetComponent<Rigidbody>().position} collider {scoop.GetComponent<BoxCollider>().bounds.center}");
                    // The fertiliser step's progress chip, as on the headset (the Editor run has no case profile to start the step).
                    var hud = Object.FindAnyObjectByType<GuidedHud>(FindObjectsInactive.Include);
                    if (hud != null)
                    {
                        hud.SetChip("NPK 19-19-19 · 0/2 scoops", BTP.UI.UIPalette.Cyan);
                        Line("chip forced on: " + hud.ChipText);
                    }
                    // A standing user looking down at the scoop on the bench.
                    head.position = scoop.Grabbable.Grip.position + new Vector3(0f, 0.74f, -0.42f);
                    head.rotation = Quaternion.LookRotation(scoop.Grabbable.Grip.position - head.position, Vector3.up);
                    Next();
                    return;
                case 1:
                    if (frame - stageFrame < 40)
                        return;
                    if (pill != null && pill.activeInHierarchy)
                    {
                        var delta = pill.transform.position - scoop.Grabbable.Grip.position;
                        head.position -= delta;
                        Line($"chip pill was {delta.magnitude:0.000} m from the grip: head moved so the chip sits at the scoop (as in the recording)");
                    }
                    else
                        Line("chip pill not shown");
                    Next();
                    return;
                case 2:
                    if (frame - stageFrame < 60)
                        return;
                    Place(scoop, InteractorHandedness.Right, head.rotation);
                    Next();
                    return;
                case 3:
                    if (frame - stageFrame < 20)
                        return;
                    Describe(scoop, $"right controller at the scoop grip, HUD chip at {(pill != null ? pill.transform.position.ToString() : "-")}");
                    Check(scoop.Grabbable.Grab.isHovered, "the right controller at the handle hovers the scoop (HUD chip at the hand)");
                    Press(InteractorHandedness.Right);
                    Next();
                    return;
                case 4:
                    if (frame - stageFrame < 20)
                        return;
                    State(scoop, "after ONE right grip press (no button held since)");
                    Check(scoop.Grabbable.IsHeld && scoop.Grabbable.HeldBy == "right", "one right grip press picks the scoop up; held with no button");
                    RightController().position += new Vector3(-0.3f, 0.35f, -0.2f);
                    Next();
                    return;
                case 5:
                    if (frame - stageFrame < 40)
                        return;
                    State(scoop, "controller moved 0.5 m, still no button");
                    Line($"   scoop grip to controller near point: {Vector3.Distance(scoop.Grabbable.Grip.position, NearOrigin(InteractorHandedness.Right)):0.000} m");
                    Check(scoop.Grabbable.IsHeld && Vector3.Distance(scoop.Grabbable.Grip.position, NearOrigin(InteractorHandedness.Right)) < 0.02f,
                        "it stays in the hand (ladle grip at the controller) as the controller moves");
                    Press(InteractorHandedness.Right);
                    Next();
                    return;
                case 6:
                    if (frame - stageFrame < 6)
                        return;
                    State(scoop, "just after the SECOND right grip press");
                    Check(!scoop.Grabbable.Grab.isSelected && (scoop.Grabbable.Current == TreatmentGrabbable.State.Returning || scoop.Grabbable.Current == TreatmentGrabbable.State.Settling), "the second press releases it and it heads home");
                    Next();
                    return;
                case 7:
                    if (Time.realtimeSinceStartup - stageTime < 1.5f)
                        return;
                    State(scoop, "a while later");
                    Line($"   distance to home {Vector3.Distance(scoop.transform.position, scoop.Grabbable.HomePosition):0.0000} m, angle {Quaternion.Angle(scoop.transform.rotation, scoop.Grabbable.HomeRotation):0.00} deg");
                    Line($"   distance to home {Vector3.Distance(scoop.transform.position, scoop.Grabbable.HomePosition):0.0000} m, angle {Quaternion.Angle(scoop.transform.rotation, scoop.Grabbable.HomeRotation):0.00} deg");
                    Check(scoop.Grabbable.AtHome && Vector3.Distance(scoop.transform.position, scoop.Grabbable.HomePosition) < 0.001f, "it is back on its bench spot");
                    // Pointing pick-up: the right controller 1.2 m back, aiming at the scoop.
                    var c = RightController();
                    // The far caster's stabilized origin trails a teleported controller; aim deterministically.
                    ((InteractionCasterBase)Controllers().First(i => i.handedness == InteractorHandedness.Right).farInteractionCaster).enableStabilization = false;
                    var origin = FarOrigin(InteractorHandedness.Right);
                    var local = Quaternion.Inverse(c.rotation) * origin.rotation;
                    var aim = scoop.GetComponent<Collider>().bounds.center;
                    var from = aim + new Vector3(0f, 0.7f, -1.0f);
                    c.rotation = Quaternion.LookRotation(aim - from, Vector3.up) * Quaternion.Inverse(local);
                    c.position += from - origin.position;
                    Next();
                    return;
                case 8:
                    if (frame - stageFrame < 20)
                        return;
                    var o = FarOrigin(InteractorHandedness.Right);
                    Line($"   far origin {o.position} forward {o.forward} -> scoop dir {(scoop.transform.position - o.position).normalized}");
                    var sweep = Physics.SphereCastAll(new Ray(o.position, o.forward), ToolGripToggle.PointRadius, ToolGripToggle.RayReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    Line("   sweep hits: " + string.Join(", ", sweep.OrderBy(h => h.distance).Select(h => $"{h.collider.name}@{h.distance:0.00}")));
                    var rc = Controllers().First(i => i.handedness == InteractorHandedness.Right);
                    var chosen = ToolGripToggle.Choose(ToolGripToggle.Registered, rc, NearOrigin(InteractorHandedness.Right), new Ray(o.position, o.forward));
                    Line($"   Choose -> {(chosen != null ? chosen.name : "null")}; controller hasSelection {rc.hasSelection}; ControllerFor(Right) {(BTP.Core.VrControllerRig.ControllerFor(InteractorHandedness.Right) != null)}; hovered [{string.Join(", ", rc.interactablesHovered.Select(h => (h as Object)?.name))}]");
                    Press(InteractorHandedness.Right);
                    Next();
                    return;
                case 9:
                    if (Time.frameCount >= pressFrame && Time.frameCount <= pressFrame + 4)
                        Line($"   [pf {Time.frameCount}] selected {scoop.Grabbable.Grab.isSelected} state {scoop.Grabbable.Current} toggleHolder {(scoop.Grabbable.ToggleHolder != null)} holding {BTP.Core.VrControllerRig.IsHoldingTool(Controllers().First(i => i.handedness == InteractorHandedness.Right))} region {Controllers().First(i => i.handedness == InteractorHandedness.Right).selectionRegion.Value} isSelectActive {Controllers().First(i => i.handedness == InteractorHandedness.Right).isSelectActive}");
                    if (frame - stageFrame < 30)
                        return;
                    State(scoop, "after a right grip press while POINTING at the scoop from 1.2 m");
                    Check(scoop.Grabbable.IsHeld && Vector3.Distance(scoop.Grabbable.Grip.position, NearOrigin(InteractorHandedness.Right)) < 0.02f, "pointing from 1.2 m and pressing grip brings it into the hand");
                    Line($"   scoop grip to controller near point: {Vector3.Distance(scoop.Grabbable.Grip.position, NearOrigin(InteractorHandedness.Right)):0.000} m");
                    Press(InteractorHandedness.Right);
                    Next();
                    return;
                case 10:
                    if (Time.realtimeSinceStartup - stageTime < 1.5f)
                        return;
                    State(scoop, "after the release press");
                    Check(scoop.Grabbable.AtHome, "released again: back home");
                    // Left controller toggle at the grip.
                    Place(scoop, InteractorHandedness.Left, head.rotation);
                    Next();
                    return;
                case 11:
                    if (frame - stageFrame < 20)
                        return;
                    Press(InteractorHandedness.Left);
                    Next();
                    return;
                case 12:
                    if (frame - stageFrame < 20)
                        return;
                    State(scoop, "after ONE LEFT grip press at the scoop");
                    Check(scoop.Grabbable.IsHeld && scoop.Grabbable.HeldBy == "left", "the left controller toggles it the same way");
                    Press(InteractorHandedness.Left);
                    Next();
                    return;
                case 13:
                    if (Time.realtimeSinceStartup - stageTime < 1.5f)
                        return;
                    State(scoop, "after the left release press");
                    Check(scoop.Grabbable.AtHome, "left release: back home");
                    BTP.Treatment.ToolGripToggle.PressOverride = null;
                    // ---- chamber door: A opens from in front, right B closes from inside the chamber footprint.
                    door = Object.FindAnyObjectByType<ChamberDoor>();
                    chamber = Object.FindAnyObjectByType<TreatmentChamber>();
                    if (door == null || chamber == null)
                    {
                        Check(false, "the chamber door exists");
                        stage = 99;
                        EditorApplication.ExitPlaymode();
                        return;
                    }

                    PlaceHead(head, chamber, 0.8f);
                    PressA();
                    Next();
                    return;
                case 14:
                    if (frame - stageFrame < 20)
                        return;
                    Line($"   door in range {door.UserInRange} near-for-close {door.UserNearForClose} open {door.UserOpen} prompt '{door.PromptGlyph} {door.PromptLabel}'");
                    Check(door.UserOpen, "right A in front of the door opens it");
                    Next();
                    return;
                case 15:
                    if (Time.realtimeSinceStartup - stageTime < 1.5f)
                        return;
                    // Leaning into the chamber past the door line, as when pouring a scoop onto the soil.
                    PlaceHead(head, chamber, -0.35f);
                    Next();
                    return;
                case 16:
                    if (frame - stageFrame < 20)
                        return;
                    Line($"   inside: door in range {door.UserInRange} near-for-close {door.UserNearForClose} prompt '{door.PromptGlyph} {door.PromptLabel}'");
                    Check(!door.UserInRange && door.UserNearForClose, "inside the footprint the old open rule fails, the close rule holds (the old dead end)");
                    var handled = BTP.Core.VrControllerRig.HandleBack(false, null);
                    Check(handled && !door.UserOpen, "right B (the rig's Back) closes the open door from there");
                    Next();
                    return;
                case 17:
                    if (Time.realtimeSinceStartup - stageTime < 1.5f)
                        return;
                    Check(!door.Moving && !door.UserOpen, "the door swung shut");
                    Check(!BTP.Core.VrControllerRig.HandleBack(false, null), "with the door shut, B falls through to its old Back");
                    // ---- spray: the mist wets the specimen's leaves.
                    station = Object.FindAnyObjectByType<ChamberStation>();
                    if (station.Specimen == null)
                        station.Show("tomato", "early_blight", null, null);
                    Next();
                    return;
                case 18:
                    if (frame - stageFrame < 30)
                        return;
                    var tool = station.SprayTool;
                    if (tool == null || station.Specimen == null || station.FoliagePoints.Count == 0)
                    {
                        Check(false, $"sprayer and specimen present (tool {tool != null}, specimen {station.Specimen != null}, foliage points {station.FoliagePoints.Count})");
                        stage = 99;
                        EditorApplication.ExitPlaymode();
                        return;
                    }

                    tool.gameObject.SetActive(true);
                    GuidedInputGate.Gate.Modal = false;
                    var centre = Vector3.zero;
                    foreach (var point in station.FoliagePoints)
                        centre += point;
                    centre /= station.FoliagePoints.Count;
                    sprayAim = centre;
                    var nozzle = tool.Nozzle;
                    var sprayFrom = centre + new Vector3(0f, 0.1f, -0.45f);
                    var nozzleLocal = Quaternion.Inverse(tool.transform.rotation) * nozzle.rotation;
                    tool.transform.rotation = Quaternion.LookRotation(centre - sprayFrom, Vector3.up) * Quaternion.Inverse(nozzleLocal);
                    tool.transform.position += sprayFrom - nozzle.position;
                    tool.Grab(null, GuidedInputGate.Key);
                    tool.SetPinch(true);
                    maxMist = 0;
                    Line($"   spraying at the specimen centre {centre} from {nozzle.position} (foliage points {station.FoliagePoints.Count}, burst {tool.BurstSeconds:0.00} s)");
                    Next();
                    return;
                case 19:
                    var sprayer = station.SprayTool;
                    if (sprayer.MistParticleSystem != null)
                        maxMist = Mathf.Max(maxMist, sprayer.MistParticleSystem.particleCount);
                    if (Time.realtimeSinceStartup - stageTime < 0.8f)
                        return;
                    sprayer.SetPinch(false);
                    var beads = sprayer.BeadCount;
                    var onLeaf = BeadsOnLeaves(sprayer, station);
                    Line($"   mist droplets at most {maxMist}; wet beads {beads}; beads within 1 cm of a leaf point {onLeaf:P0}");
                    Check(maxMist >= 150, "a dense mist while spraying");
                    Check(beads >= 40 && onLeaf >= 0.95f, "the droplets bead up on the specimen's leaves");
                    Next();
                    return;
                case 20:
                    if (Time.realtimeSinceStartup - stageTime < 7f)
                        return;
                    Line($"   wet beads after 7 s: {station.SprayTool.BeadCount}");
                    Check(station.SprayTool.BeadCount == 0, "the beads fade away after a few seconds");
                    stage = 99;
                    EditorApplication.ExitPlaymode();
                    return;
            }
        }

        static ChamberDoor door;
        static TreatmentChamber chamber;
        static ChamberStation station;
        static Vector3 sprayAim;
        static int maxMist;
        static int aFrame = -1;

        static void PressA()
        {
            aFrame = Time.frameCount + 1;
            ChamberDoor.PrimaryPressOverride = () => Time.frameCount == aFrame;
            Line($"-> right A press at player frame {aFrame}");
        }

        /// <summary>The head at the door's middle, <paramref name="outwards"/> metres in front of it (negative: inside), looking at it.</summary>
        static void PlaceHead(Transform head, TreatmentChamber chamber, float outwards)
        {
            var mid = (chamber.DoorHingeWorld + chamber.DoorEdgeWorld) * 0.5f;
            var outward = chamber.DoorOutwardWorld;
            outward.y = 0f;
            outward.Normalize();
            var eye = mid + outward * outwards;
            eye.y = chamber.transform.position.y + 1.6f;
            head.SetPositionAndRotation(eye, Quaternion.LookRotation(-outward, Vector3.up));
        }

        static readonly ParticleSystem.Particle[] Sampled = new ParticleSystem.Particle[400];

        /// <summary>Share of the wet beads that sit within 1 cm of one of the specimen's surface points.</summary>
        static float BeadsOnLeaves(TrainingSprayTool sprayer, ChamberStation station)
        {
            var system = sprayer.transform.Find("SprayLeafBeads")?.GetComponent<ParticleSystem>();
            if (system == null)
                return 0f;
            var n = system.GetParticles(Sampled);
            if (n == 0)
                return 0f;
            var near = 0;
            for (var i = 0; i < n; i++)
            {
                var best = float.MaxValue;
                foreach (var point in station.FoliagePoints)
                    best = Mathf.Min(best, (point - Sampled[i].position).sqrMagnitude);
                if (best <= 0.0001f)
                    near++;
            }

            return near / (float)n;
        }

        static void Check(bool ok, string what)
        {
            if (!ok)
                failures++;
            Line((ok ? "  PASS " : "  FAIL ") + what);
        }

        static int pressFrame = -1;
        static InteractorHandedness pressHand;

        static void Press(InteractorHandedness hand)
        {
            pressHand = hand;
            pressFrame = Time.frameCount + 1;
            BTP.Treatment.ToolGripToggle.PressOverride = h => h == pressHand && Time.frameCount == pressFrame;
            Line($"-> {hand} GRIP press at player frame {pressFrame}");
        }

        static Transform RightController() => Controllers().First(i => i.handedness == InteractorHandedness.Right).transform.parent;

        static Vector3 NearOrigin(InteractorHandedness hand) =>
            ((SphereInteractionCaster)Controllers().First(i => i.handedness == hand).nearInteractionCaster).effectiveCastOrigin.position;

        static Transform FarOrigin(InteractorHandedness hand) =>
            ((InteractionCasterBase)Controllers().First(i => i.handedness == hand).farInteractionCaster).effectiveCastOrigin;

        static void State(FertiliserScoop scoop, string label)
        {
            var item = scoop.Grabbable;
            var grab = item.Grab;
            var by = grab.interactorsSelecting.Count > 0 ? ((Component)grab.interactorsSelecting[0]).transform.parent.name : "none";
            Line($"==== {label} (frame {frame}, player frame {Time.frameCount})");
            Line($"   state {item.Current} heldBy {item.HeldBy ?? "-"} selected {grab.isSelected} by {by} toggleHolder {(item.ToggleHolder != null ? item.ToggleHolder.transform.parent.name : "none")} scoop at {scoop.transform.position}");
            foreach (var poke in Object.FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include))
                Line($"   poke {poke.transform.parent.name}/{poke.name} enabled {poke.enabled}");
        }

        static void Next()
        {
            stage++;
            stageFrame = frame;
            stageTime = Time.realtimeSinceStartup;
        }

        static IEnumerable<NearFarInteractor> Controllers() =>
            Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include)
                .Where(i => i.transform.parent != null && i.transform.parent.name.Contains("Controller"));

        static void Place(FertiliserScoop scoop, InteractorHandedness hand, Quaternion rotation)
        {
            foreach (var interactor in Controllers())
            {
                var controller = interactor.transform.parent;
                foreach (var b in controller.GetComponents<Behaviour>())
                    if (b.GetType().Name.Contains("TrackedPoseDriver"))
                        b.enabled = false;
                var caster = interactor.nearInteractionCaster as Component;
                var origin = (interactor.nearInteractionCaster as SphereInteractionCaster)?.effectiveCastOrigin;
                var target = interactor.handedness == hand ? scoop.Grabbable.Grip.position : scoop.transform.position + Vector3.up * 3f;
                controller.rotation = rotation;
                var offset = origin != null ? origin.position - controller.position : Vector3.zero;
                controller.position = target - offset;
                Line($"placed {controller.name}: caster origin {(origin != null ? origin.name : "null")} now at {(origin != null ? origin.position : Vector3.zero)} target {target}");
            }
        }

        static void Describe(FertiliserScoop scoop, string label)
        {
            Line("==== " + label + $" (frame {frame})");
            var grab = scoop.Grabbable.Grab;
            Line($"scoop '{scoop.name}' path {Path(scoop.transform)} scene {scoop.gameObject.scene.name} layer {scoop.gameObject.layer} active {scoop.gameObject.activeInHierarchy} pos {scoop.transform.position} lossyScale {scoop.transform.lossyScale}");
            var rb = scoop.GetComponent<Rigidbody>();
            Line($"BODY position {rb.position} kinematic {rb.isKinematic} interpolation {rb.interpolation}; transform {scoop.transform.position}; collider bounds centre {scoop.GetComponent<BoxCollider>().bounds.center}; state {scoop.Grabbable.Current}");
            Line($"grab enabled {grab.enabled} isActiveAndEnabled {grab.isActiveAndEnabled} layers {grab.interactionLayers.value} manager {(grab.interactionManager != null ? grab.interactionManager.GetHashCode() + " " + grab.interactionManager.gameObject.scene.name : "null")} hovered {grab.isHovered} selected {grab.isSelected} distanceMode {grab.distanceCalculationMode}");
            foreach (var c in grab.colliders)
                Line(c == null ? "  collider <destroyed>" : $"  collider {c.GetType().Name} '{c.name}' enabled {c.enabled} active {c.gameObject.activeInHierarchy} trigger {c.isTrigger} layer {c.gameObject.layer} bounds {c.bounds.center} {c.bounds.size} mapped {(grab.interactionManager != null && grab.interactionManager.TryGetInteractableForCollider(c, out var mappedTo) ? (mappedTo as Object)?.name : "NO")}");
            foreach (var c in scoop.GetComponentsInChildren<Collider>(true))
                if (!grab.colliders.Contains(c))
                    Line($"  NOT in grab.colliders: {c.GetType().Name} '{c.name}' enabled {c.enabled}");
            var managers = Object.FindObjectsByType<XRInteractionManager>(FindObjectsInactive.Include);
            Line("managers: " + string.Join(", ", managers.Select(m => m.GetHashCode() + "@" + m.gameObject.scene.name)));
            Line("physics scenes: scoop " + scoop.gameObject.scene.GetPhysicsScene().GetHashCode());
            foreach (var interactor in Controllers())
            {
                var caster = interactor.nearInteractionCaster as SphereInteractionCaster;
                var origin = caster != null ? caster.effectiveCastOrigin : null;
                Line($"-- {Path(interactor.transform)} hand {interactor.handedness} enabled {interactor.isActiveAndEnabled} layers {interactor.interactionLayers.value} manager {(interactor.interactionManager != null ? interactor.interactionManager.GetHashCode().ToString() : "null")} near {interactor.enableNearCasting} far {interactor.enableFarCasting} scene {interactor.gameObject.scene.name} physicsScene {interactor.gameObject.scene.GetPhysicsScene().GetHashCode()}");
                Line($"   selectInput mode {interactor.selectInput.inputSourceMode} selectTrigger {interactor.selectActionTrigger} region {interactor.selectionRegion.Value} hovered [{string.Join(", ", interactor.interactablesHovered.Select(h => (h as Object)?.name))}] selected [{string.Join(", ", interactor.interactablesSelected.Select(h => (h as Object)?.name))}]");
                if (caster != null && origin != null)
                {
                    Line($"   caster enabled {caster.isActiveAndEnabled} mask {caster.physicsLayerMask.value} radius {caster.castRadius} trig {caster.physicsTriggerInteraction} origin '{Path(origin)}' at {origin.position}  dist to grip {Vector3.Distance(origin.position, scoop.Grabbable.Grip.position):0.000}");
                    var hits = Physics.OverlapSphere(origin.position, caster.castRadius * caster.transform.lossyScale.x, caster.physicsLayerMask, caster.physicsTriggerInteraction);
                    Line("   OverlapSphere: " + string.Join(", ", hits.Select(h => $"{h.name}(L{h.gameObject.layer},{h.GetType().Name})")));
                    var list = new List<Collider>();
                    var ok = caster.TryGetColliderTargets(interactor.interactionManager, list);
                    Line($"   caster.TryGetColliderTargets {ok}: " + string.Join(", ", list.Select(h => h != null ? h.name : "null")));
                }

                var group = interactor.containingGroup as XRInteractionGroup;
                Line($"   group {(group != null ? group.groupName : "none")} active {(group != null && group.activeInteractor != null ? Path(((Component)group.activeInteractor).transform) : "none")}");
                foreach (var poke in interactor.transform.parent.GetComponentsInChildren<XRPokeInteractor>(true))
                    Line($"   poke '{Path(poke.transform)}' enabled {poke.isActiveAndEnabled} hovered [{string.Join(", ", poke.interactablesHovered.Select(h => (h as Object)?.name))}] pokingUI {UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster.IsPokeInteractingWithUI(poke)} attach {poke.GetAttachTransform(null).position}");
                Line($"   far ray UI hit {(interactor.TryGetCurrentUIRaycastResult(out var ui) ? ui.gameObject.name : "none")}");
                var targets = new List<IXRInteractable>();
                interactor.GetValidTargets(targets);
                Line("   valid targets: " + string.Join(", ", targets.Select(t => (t as Object)?.name)));
            }
        }

        static string Path(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent)
                s = p.name + "/" + s;
            return s;
        }
    }
}
