using System.Collections.Generic;
using BTP.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Treatment
{
    /// <summary>
    /// The fertiliser packet as a tracked-hand tool (Addendum 5; built by TreatmentChamberBuilder, no hand model). It stands
    /// in its dock and is only present when the profile confirms a nutrient (its label is that nutrient, upper-cased). Picked
    /// up with a tracked grasp (an XRGrabInteractable added at runtime; either hand) it follows the grip pose. Granules flow
    /// only while the packet is held with its torn corner tilted below horizontal over the soil; the rate grows with the tilt.
    /// Each continuous pour over the soil of at least <see cref="MinPourGranules"/> granules counts as one application
    /// (<see cref="PourCount"/>); the supplement amount and the number of applications stay the panel's two settings. Release
    /// (or tracking loss) stops the pour at once and returns the packet to its dock.
    /// </summary>
    public sealed class FertiliserApplicator : MonoBehaviour
    {
        public const float PourAngle = 20f, MaxPourAngle = 80f, MinRate = 12f, MaxRate = 70f, ReturnDelay = 0.4f;
        public const int MinPourGranules = 6;

        [SerializeField] Transform rig;
        [Tooltip("The packet body (its centre defines the tilt of the torn corner).")]
        [SerializeField] Transform wrist;
        [SerializeField] GameObject packet;
        [SerializeField] Transform pourCorner;
        [SerializeField] TMP_Text nutrientLabel;
        [SerializeField] TMP_Text productLabel;
        [SerializeField] MeshFilter granuleFilter;
        [SerializeField] MeshRenderer granuleRenderer;

        GranuleSimulation simulation;
        Mesh granuleMesh;
        /// <summary>Instanced granules when a <see cref="GranuleRenderer"/> is running (the guided treatment's).</summary>
        GranulePour pour;
        string label;
        Vector3 dockPosition;
        Quaternion dockRotation = Quaternion.identity;
        bool held;
        Transform grip;
        string holdingHand;
        bool pouring;
        int sessionGranules;
        float emitCarry;
        float returnTimer = -1f;
        int seed = 1;
        GranuleSimulation.Area area;
        bool hasArea;
        XRGrabInteractable grab;

        public string Label => label;
        public bool HasPacket => label != null;
        public GranuleSimulation Simulation => simulation;
        public bool IsHeld => held;
        public bool IsPouring => pouring;
        /// <summary>True while pouring (kept for callers of the earlier scripted animation).</summary>
        public bool IsAnimating => pouring;
        public string HoldingHand => holdingHand;
        /// <summary>Continuous pours over the soil of at least <see cref="MinPourGranules"/> granules.</summary>
        public int PourCount { get; private set; }
        public Transform Rig => rig;
        public Transform PourCorner => pourCorner;
        public Vector3 DockPosition => dockPosition;
        /// <summary>How far the torn corner points below horizontal (degrees; negative when it points up).</summary>
        public float Tilt { get; private set; }

        internal void Bind(Transform rigRoot, Transform wristPivot, GameObject packetRoot, Transform corner, TMP_Text nutrient, TMP_Text product,
            MeshFilter granules, MeshRenderer granulesRenderer)
        {
            rig = rigRoot; wrist = wristPivot; packet = packetRoot; pourCorner = corner; nutrientLabel = nutrient; productLabel = product;
            granuleFilter = granules; granuleRenderer = granulesRenderer;
        }

        void Awake()
        {
            if (!Application.isPlaying || rig == null)
                return;
            // Grabbable only at runtime (the saved lab scene keeps no grab interactables): a tracked grasp with either hand.
            var body = rig.GetComponent<Rigidbody>();
            if (body == null)
                body = rig.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            if (rig.GetComponent<Collider>() == null)
            {
                var box = rig.gameObject.AddComponent<BoxCollider>();
                var bounds = LocalBounds();
                box.center = bounds.center;
                box.size = bounds.size;
            }

            grab = rig.gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;
            grab.attachTransform = rig;
            // A ray grab brings the packet into the hand (the controllers' default is to hold it at the end of the ray).
            grab.farAttachMode = InteractableFarAttachMode.Near;
            // Controllers hold with the GRIP or the TRIGGER (the trigger does nothing else with the packet: it pours by
            // tilting); tracked hands are unaffected.
            ControllerGripSelectFilter.AddTo(grab, false);
            grab.selectEntered.AddListener(args => Grab(null, Hand(args.interactorObject)));
            grab.selectExited.AddListener(_ => Release());
        }

        static string Hand(IXRInteractor interactor) =>
            interactor is IXRSelectInteractor select && select.handedness == InteractorHandedness.Left ? "left" : interactor is IXRSelectInteractor right &&
            right.handedness == InteractorHandedness.Right ? "right" : "unknown";

        Bounds LocalBounds()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 0.05f);
            var first = true;
            foreach (var renderer in rig.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var b = filter.sharedMesh.bounds;
                for (var c = 0; c < 8; c++)
                {
                    var p = rig.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z)));
                    if (first)
                        bounds = new Bounds(p, Vector3.zero);
                    else
                        bounds.Encapsulate(p);
                    first = false;
                }
            }

            return bounds;
        }

        void OnDestroy()
        {
            if (pour != null)
                GranuleRenderer.Active?.Unregister(pour);
            if (granuleMesh == null)
                return;
            if (Application.isPlaying)
                Destroy(granuleMesh);
            else
                DestroyImmediate(granuleMesh);
        }

        void Update() => Tick(Time.deltaTime);

        /// <summary>Shows the packet for <paramref name="nutrient"/> (none when it is null), back in its dock, with no granules.</summary>
        public void Configure(string nutrient)
        {
            label = TreatmentProfile.PacketLabel(nutrient);
            if (nutrientLabel != null)
                nutrientLabel.text = label ?? "";
            if (productLabel != null)
                productLabel.text = "TRAINING FERTILISER";
            ForceRelease(null);
            if (rig != null)
                rig.gameObject.SetActive(label != null);
            ClearGranules();
            if (pour != null)
            {
                GranuleRenderer.Active?.Unregister(pour);
                pour = null;
            }

            PourCount = 0;
            if (nutrientLabel != null && !Application.isPlaying)
                nutrientLabel.ForceMeshUpdate();
        }

        /// <summary>Where the packet stands when it is not held (reachable seated, beside the WATER console).</summary>
        public void Dock(Vector3 position, Quaternion rotation)
        {
            dockPosition = position;
            dockRotation = rotation;
            if (!held && rig != null)
                rig.SetPositionAndRotation(position, rotation);
        }

        /// <summary>The soil the granules land on (tray, stem, band) and the seed of this specimen's pours.</summary>
        public void SetSoil(GranuleSimulation.Area soil, int pourSeed)
        {
            area = soil;
            hasArea = true;
            seed = pourSeed;
        }

        /// <summary>
        /// A tracked grasp took the packet. With <paramref name="gripPose"/> the packet follows that transform (tests and the
        /// verification); without it the XRGrabInteractable moves it with the grip pose.
        /// </summary>
        public void Grab(Transform gripPose, string hand)
        {
            if (!HasPacket)
                return;
            held = true;
            grip = gripPose;
            holdingHand = hand;
            returnTimer = -1f;
            HandTestLog.Log("packet-grab", hand, "hand-grasp", "held");
        }

        /// <summary>Release: the pour stops at once; the packet returns to its dock after a short delay.</summary>
        public void Release()
        {
            if (!held)
                return;
            StopPour();
            held = false;
            grip = null;
            HandTestLog.Log("packet-release", holdingHand, "hand-grasp", "returning to dock");
            holdingHand = null;
            returnTimer = ReturnDelay;
        }

        /// <summary>Tracking loss (or a case change): stop pouring this frame, drop the packet and put it back in its dock now.</summary>
        public void ForceRelease(string reason)
        {
            var wasHeld = held;
            StopPour();
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            held = false;
            grip = null;
            if (wasHeld && reason != null)
                HandTestLog.Log("packet-force-release", holdingHand, "hand-grasp", reason);
            holdingHand = null;
            returnTimer = -1f;
            if (rig != null)
                rig.SetPositionAndRotation(dockPosition, dockRotation);
        }

        /// <summary>Follows the grip, pours while tilted over the soil, moves the granules (Update; tests call it directly).</summary>
        public void Tick(float deltaTime)
        {
            if (held && grip != null && rig != null)
                rig.SetPositionAndRotation(grip.position, grip.rotation);
            if (!held && returnTimer >= 0f)
            {
                returnTimer -= deltaTime;
                if (returnTimer < 0f && rig != null)
                    rig.SetPositionAndRotation(dockPosition, dockRotation);
            }

            UpdatePour(deltaTime);
            if (simulation != null && !simulation.AllSettled)
            {
                simulation.Step(deltaTime);
                RebuildMesh();
            }
        }

        bool OverSoil(Vector3 corner) =>
            hasArea && corner.x >= area.Min.x && corner.x <= area.Max.x && corner.z >= area.Min.y && corner.z <= area.Max.y && corner.y > area.SoilY &&
            corner.y - area.SoilY < 0.6f;

        void UpdatePour(float deltaTime)
        {
            if (pourCorner == null || wrist == null)
                return;
            var centre = (packet != null ? packet.transform : wrist).position;
            var toCorner = pourCorner.position - centre;
            Tilt = toCorner.sqrMagnitude > 1e-8f ? Mathf.Asin(Mathf.Clamp(-toCorner.normalized.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
            var total = simulation != null ? simulation.Count : 0;
            var should = held && HasPacket && Tilt >= PourAngle && OverSoil(pourCorner.position) && total < GranuleSimulation.MaxCount;
            if (should && !pouring)
            {
                pouring = true;
                sessionGranules = 0;
                emitCarry = 0f;
                HandTestLog.Log("packet-pour", holdingHand, "hand-grasp", $"started tilt={Tilt:0}");
            }
            else if (!should && pouring)
            {
                StopPour();
            }

            if (!pouring)
                return;
            simulation ??= new GranuleSimulation(seed, area);
            emitCarry += Mathf.Lerp(MinRate, MaxRate, Mathf.InverseLerp(PourAngle, MaxPourAngle, Tilt)) * deltaTime;
            var count = Mathf.Min(Mathf.FloorToInt(emitCarry), GranuleSimulation.MaxCount - simulation.Count);
            if (count <= 0)
                return;
            emitCarry -= count;
            simulation.Emit(pourCorner.position, count);
            sessionGranules += count;
        }

        void StopPour()
        {
            if (!pouring)
                return;
            pouring = false;
            if (sessionGranules >= MinPourGranules)
                PourCount++;
            HandTestLog.Log("packet-pour", holdingHand, "hand-grasp", $"stopped granules={sessionGranules} applications={PourCount}");
        }

        /// <summary>Shows a saved application at once: the same granules, already settled, the packet in its dock.</summary>
        public int ShowSettled(int granules, int pours, int pourSeed, GranuleSimulation.Area soil)
        {
            ClearGranules();
            SetSoil(soil, pourSeed);
            var count = HasPacket ? Mathf.Clamp(granules, 0, GranuleSimulation.MaxCount) : 0;
            if (count == 0)
                return 0;
            simulation = new GranuleSimulation(count, pourSeed, new Vector3((soil.Min.x + soil.Max.x) * 0.5f, soil.SoilY + 0.1f, (soil.Min.y + soil.Max.y) * 0.5f), soil);
            simulation.RunToEnd();
            PourCount = pours;
            RebuildMesh();
            return count;
        }

        /// <summary>Removes the settled granules (case or specimen change).</summary>
        public void ClearGranules()
        {
            pouring = false;
            simulation = null;
            if (pour != null)
                pour.Simulation = null;
            if (granuleMesh != null)
                granuleMesh.Clear();
            if (granuleRenderer != null)
                granuleRenderer.enabled = false;
        }

        /// <summary>One combined mesh of small octahedra (one draw call), rebuilt while granules move.</summary>
        void RebuildMesh()
        {
            var instanced = GranuleRenderer.Active;
            if (simulation != null && instanced != null && instanced.Supported)
            {
                // Real-size instanced granules (the product's look) instead of the octahedra mesh.
                if (pour == null)
                {
                    var config = GuidedTreatmentConfig.Load();
                    pour = new GranulePour(null, GranuleLook.For(config.ProductForNutrient(label)), (uint)seed, config.visualGranulesPerGranule);
                }

                pour.Simulation = simulation;
                instanced.Register(pour);
                if (granuleRenderer != null)
                    granuleRenderer.enabled = false;
                return;
            }

            if (granuleFilter == null || simulation == null)
                return;
            if (granuleMesh == null)
            {
                granuleMesh = new Mesh { name = "FertiliserGranules" };
                granuleMesh.MarkDynamic();
                granuleFilter.sharedMesh = granuleMesh;
            }

            var vertices = new List<Vector3>(simulation.Count * 6);
            var normals = new List<Vector3>(simulation.Count * 6);
            var triangles = new List<int>(simulation.Count * 24);
            var origin = granuleFilter.transform;
            var r = GranuleSimulation.Radius * 1.25f;
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] faces = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            foreach (var g in simulation.Granules)
            {
                if (!g.Released)
                    continue;
                var centre = origin.InverseTransformPoint(g.Position);
                var start = vertices.Count;
                foreach (var axis in axes)
                {
                    vertices.Add(centre + axis * r);
                    normals.Add(axis);
                }

                foreach (var index in faces)
                    triangles.Add(start + index);
            }

            granuleMesh.Clear();
            granuleMesh.SetVertices(vertices);
            granuleMesh.SetNormals(normals);
            granuleMesh.SetTriangles(triangles, 0);
            granuleMesh.RecalculateBounds();
            if (granuleRenderer != null)
                granuleRenderer.enabled = vertices.Count > 0;
        }

        /// <summary>Number of granules visible now.</summary>
        public int VisibleGranules
        {
            get
            {
                if (simulation == null)
                    return 0;
                var count = 0;
                foreach (var g in simulation.Granules)
                    if (g.Released)
                        count++;
                return count;
            }
        }
    }
}
