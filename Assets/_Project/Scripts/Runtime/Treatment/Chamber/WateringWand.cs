using System.Collections.Generic;
using BTP.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BTP.Treatment
{
    /// <summary>
    /// The white-and-sage watering wand in the blue-gloved hand (built by TreatmentChamberBuilder). Root-zone delivery: a
    /// thin clear stream with a slight wobble runs from the downward nozzle onto a patch of soil beside the stem, below
    /// the canopy; the soil darkens there (spreading a little) and a faint wet front shows through the tray window. Overhead
    /// delivery: the wand is held over the canopy and a fine shower falls through it. Hold: no water. The wand is an
    /// XRGrabInteractable: picked up, its trigger waters wherever the nozzle points; the panel's Apply presents it in the
    /// hand pose and waters automatically.
    /// </summary>
    public sealed class WateringWand : MonoBehaviour
    {
        const int StreamSamples = 14, StreamSides = 6, Droplets = 10, ShowerDrops = 46;

        [SerializeField] Transform rig;
        [SerializeField] Transform wand;
        [SerializeField] Transform nozzleTip;
        [SerializeField] Transform trigger;
        [SerializeField] Transform indexFinger;
        [SerializeField] GameObject glove;
        [SerializeField] MeshFilter streamFilter;
        [SerializeField] MeshRenderer streamRenderer;
        [SerializeField] MeshFilter dropletFilter;
        [SerializeField] MeshRenderer dropletRenderer;

        public enum Mode
        {
            None,
            RootZone,
            Overhead,
        }

        Mode mode;
        bool watering;
        float time;
        float wetness;
        Vector3 contact;
        Vector3 showerCentre;
        float showerTop;
        float showerBottom;
        float showerRadius;
        Mesh streamMesh;
        Mesh dropletMesh;
        Transform wetPatch;
        Transform wetFront;
        float soilY;
        float frontZWorld;
        Vector2 soilMin, soilMax;
        float wetMaxSize = 0.12f;
        float wetFrontDepth = 0.08f;
        XRGrabInteractable grab;
        bool held;
        bool waitForTriggerRelease;
        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3 wandLocalPosition;
        Quaternion wandLocalRotation;

        public bool IsWatering => watering;
        public Mode Delivery => mode;
        /// <summary>Where the stream meets the soil (root-zone delivery).</summary>
        public Vector3 Contact => contact;
        public Vector3 NozzlePosition => nozzleTip != null ? nozzleTip.position : transform.position;
        /// <summary>0..1: how far the wet patch has spread.</summary>
        public float Wetness => wetness;
        public bool IsHeld => held;

        /// <summary>Raised when the player squeezes (true) or releases (false) the trigger of the picked-up wand.</summary>
        public event System.Action<bool> TriggerChanged;

        internal void Bind(Transform rigRoot, Transform wandRoot, Transform tip, Transform triggerPart, Transform finger, GameObject gloveRoot,
            MeshFilter stream, MeshRenderer streamMeshRenderer, MeshFilter droplets, MeshRenderer dropletsRenderer)
        {
            rig = rigRoot; wand = wandRoot; nozzleTip = tip; trigger = triggerPart; indexFinger = finger; glove = gloveRoot;
            streamFilter = stream; streamRenderer = streamMeshRenderer; dropletFilter = droplets; dropletRenderer = dropletsRenderer;
        }

        bool homeCaptured;

        /// <summary>Remembers the wand's built pose in the hand once (Awake; edit-mode tools reach it through Pose).</summary>
        void CaptureHome()
        {
            if (homeCaptured || wand == null)
                return;
            homeCaptured = true;
            wandLocalPosition = wand.localPosition;
            wandLocalRotation = wand.localRotation;
        }

        void Awake()
        {
            CaptureHome();
            if (wand != null && Application.isPlaying)
            {
                // Grabbable only at runtime, so the saved lab scene keeps no grab interactables.
                grab = wand.GetComponent<XRGrabInteractable>();
                if (grab == null)
                {
                    grab = wand.gameObject.AddComponent<XRGrabInteractable>();
                    grab.throwOnDetach = false;
                    grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                }
            }

            if (grab != null)
            {
                // Controllers hold with the GRIP (the filter), leaving the trigger to water.
                ControllerGripSelectFilter.AddTo(grab);
                grab.selectEntered.AddListener(OnGrabbed);
                grab.selectExited.AddListener(OnReleased);
                grab.activated.AddListener(OnActivated);
                grab.deactivated.AddListener(OnDeactivated);
            }
        }

        void OnDestroy()
        {
            if (grab != null)
            {
                grab.selectEntered.RemoveListener(OnGrabbed);
                grab.selectExited.RemoveListener(OnReleased);
                grab.activated.RemoveListener(OnActivated);
                grab.deactivated.RemoveListener(OnDeactivated);
            }

            Kill(streamMesh);
            Kill(dropletMesh);
        }

        static void Kill(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            held = true;
            // A trigger that is already down at pickup does not water: it must be released once first.
            waitForTriggerRelease = VrControllerRig.IsActivateHeldOnController(args.interactorObject);
            if (glove != null)
                glove.SetActive(false);
        }

        void OnReleased(SelectExitEventArgs args)
        {
            held = false;
            waitForTriggerRelease = false;
            Stop();
            if (glove != null)
                glove.SetActive(true);
            ReturnHome();
        }

        void OnActivated(ActivateEventArgs args)
        {
            if (waitForTriggerRelease)
                return;
            if (mode == Mode.None)
                mode = Mode.RootZone;
            watering = true;
            TriggerChanged?.Invoke(true);
        }

        void OnDeactivated(DeactivateEventArgs args)
        {
            waitForTriggerRelease = false;
            watering = false;
            TriggerChanged?.Invoke(false);
        }

        // ------------------------------------------------------------------ placement

        /// <summary>The tray the water lands in: soil height, the inner soil rectangle, the window plane and the wet marks.</summary>
        public void SetTray(TreatmentChamber chamber)
        {
            if (chamber == null)
                return;
            soilY = chamber.SoilWorldY;
            chamber.SoilArea(out soilMin, out soilMax);
            wetPatch = chamber.WetPatch;
            wetFront = chamber.WetFront;
            frontZWorld = chamber.transform.TransformPoint(new Vector3(0f, 0f, chamber.TrayFrontZ)).z;
            var layout = chamber.Layout;
            wetMaxSize = Mathf.Clamp(Mathf.Min(layout.TraySize.x, layout.TraySize.y) * 0.4f, 0.06f, 0.3f);
            wetFrontDepth = Mathf.Max(0.02f, (layout.TrayHeight - TreatmentChamber.TrayWallThickness) * 0.65f);
            if (Application.isPlaying && streamFx == null && chamber.GlassMaterial != null)
                // A small splash share: the chamber's particles stay within ChamberGlassPhysics.ParticleBudget.
                streamFx = WaterStreamFx.Create("WandStreamFx", transform, chamber.GlassMaterial, 30);
            // Play mode: the water spreads over the soil top (shared with the WATER console; null in edit mode).
            spread = SoilWaterSpread.For(chamber);
            spread?.Fit(chamber);
            if (spread != null && wetPatch != null)
                wetPatch.gameObject.SetActive(false);
            ResetWater();
        }

        WaterStreamFx streamFx;
        SoilWaterSpread spread;

        /// <summary>The ballistic stream (Play mode, root-zone delivery); null in edit mode.</summary>
        public WaterStreamFx StreamFx => streamFx;

        /// <summary>
        /// Root-zone pose: the nozzle a little above <paramref name="soilContact"/>, the hand coming from the viewer's
        /// left, the wand turned so the viewer sees its side.
        /// </summary>
        public void PresentRootZone(Vector3 soilContact, Vector3 viewerEye, float nozzleHeight)
        {
            mode = Mode.RootZone;
            contact = soilContact;
            Pose(soilContact + Vector3.up * nozzleHeight, viewerEye);
        }

        /// <summary>Overhead pose: the nozzle above the canopy top, showering a disc of <paramref name="radius"/>.</summary>
        public void PresentOverhead(Vector3 canopyTop, float radius, Vector3 viewerEye)
        {
            mode = Mode.Overhead;
            showerCentre = new Vector3(canopyTop.x, 0f, canopyTop.z);
            showerTop = canopyTop.y + 0.12f;
            showerBottom = soilY;
            showerRadius = Mathf.Max(0.05f, radius);
            contact = new Vector3(canopyTop.x, soilY, canopyTop.z);
            Pose(canopyTop + Vector3.up * 0.16f, viewerEye);
        }

        /// <summary>Hold steady: the wand rests at its last pose and no water flows.</summary>
        public void PresentHold()
        {
            mode = Mode.None;
            Stop();
        }

        void Pose(Vector3 nozzleWorld, Vector3 viewerEye)
        {
            if (rig == null || nozzleTip == null)
                return;
            CaptureHome();
            if (!held && wand != null)
                wand.SetLocalPositionAndRotation(wandLocalPosition, wandLocalRotation);
            var forward = nozzleWorld - viewerEye;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            rig.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            var offset = rig.InverseTransformPoint(nozzleTip.position);
            rig.position = nozzleWorld - rig.rotation * offset;
            homePosition = rig.position;
            homeRotation = rig.rotation;
        }

        void ReturnHome()
        {
            if (rig == null)
                return;
            rig.SetPositionAndRotation(homePosition, homeRotation);
            if (wand != null)
                wand.SetLocalPositionAndRotation(wandLocalPosition, wandLocalRotation);
        }

        // ------------------------------------------------------------------ watering

        public void StartWatering()
        {
            if (mode == Mode.None)
                return;
            watering = true;
        }

        public void Stop()
        {
            watering = false;
            if (streamRenderer != null)
                streamRenderer.enabled = false;
            if (dropletRenderer != null)
                dropletRenderer.enabled = false;
            if (streamFx != null)
                streamFx.SetFlow(0f);
            // The film near the impact soaks in (the console sets it flowing again while its own water runs).
            if (spread != null && wetness > 0f)
                spread.SetFlowing(false);
            Squeeze(false);
        }

        /// <summary>Dry soil again (new specimen or case).</summary>
        public void ResetWater()
        {
            Stop();
            wetness = 0f;
            time = 0f;
            spread?.Clear();
            if (wetPatch != null)
                wetPatch.gameObject.SetActive(false);
            if (wetFront != null)
                wetFront.gameObject.SetActive(false);
        }

        void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the stream, droplets and wet marks (Update; the verification calls it directly).</summary>
        public void Tick(float deltaTime)
        {
            time += deltaTime;
            if (held && watering && nozzleTip != null)
            {
                // A picked-up wand waters where its nozzle points down onto the soil.
                var down = nozzleTip.position;
                contact = new Vector3(down.x, soilY, down.z);
            }

            if (!watering)
                return;
            Squeeze(true);
            if (mode == Mode.Overhead)
            {
                DrawShower();
                if (streamRenderer != null)
                    streamRenderer.enabled = false;
                if (streamFx != null)
                    streamFx.SetFlow(0f);
                return;
            }

            if (streamFx != null)
            {
                // Play mode: a ballistic stream from the nozzle with its own splash (the trigger is the flow).
                streamFx.SetSource(NozzlePosition, Vector3.down * 0.6f);
                streamFx.SetGround(soilY);
                streamFx.SetFlow(1f);
                if (streamRenderer != null)
                    streamRenderer.enabled = false;
                if (dropletRenderer != null)
                    dropletRenderer.enabled = false;
            }
            else
            {
                DrawStream();
            }
            var onSoil = contact.x >= soilMin.x && contact.x <= soilMax.x && contact.z >= soilMin.y && contact.z <= soilMax.y;
            if (!onSoil)
                return;
            wetness = Mathf.Min(1f, wetness + deltaTime / 2.5f);
            if (spread != null)
            {
                // Spreads from where the nozzle points; a moving wand leaves the soil it already wetted dark.
                spread.SetWater(contact, wetness, true);
            }
            else if (wetPatch != null)
            {
                wetPatch.gameObject.SetActive(true);
                var size = Mathf.Lerp(0.035f, wetMaxSize, wetness);
                wetPatch.position = new Vector3(contact.x, soilY + 0.0015f, contact.z);
                var parentScale = wetPatch.parent != null ? wetPatch.parent.lossyScale : Vector3.one;
                wetPatch.localScale = new Vector3(size / parentScale.x, 0.0008f, size / parentScale.z);
            }

            if (wetFront != null)
            {
                wetFront.gameObject.SetActive(true);
                var depth = Mathf.Lerp(0.005f, wetFrontDepth, wetness);
                var width = Mathf.Lerp(0.04f, wetMaxSize * 1.3f, wetness);
                wetFront.position = new Vector3(Mathf.Clamp(contact.x, soilMin.x + width * 0.5f, soilMax.x - width * 0.5f), soilY - depth * 0.5f, frontZWorld + 0.0015f);
                wetFront.localScale = new Vector3(width, depth, 0.002f);
            }
        }

        void Squeeze(bool pressed)
        {
            if (trigger != null)
                trigger.localRotation = Quaternion.Euler(pressed ? -18f : 0f, 0f, 0f);
            if (indexFinger != null)
                indexFinger.localRotation = Quaternion.Euler(pressed ? 28f : 8f, 0f, 0f);
        }

        /// <summary>The stream's centre line: a gentle curve from the nozzle to the contact point, with a slight wobble.</summary>
        public Vector3 StreamPoint(float t)
        {
            var start = NozzlePosition;
            var end = contact;
            var control = Vector3.Lerp(start, end, 0.5f) + Vector3.up * 0.01f;
            var a = Vector3.Lerp(start, control, t);
            var b = Vector3.Lerp(control, end, t);
            var p = Vector3.Lerp(a, b, t);
            var wobble = Mathf.Sin(time * 11f + t * 9f) * 0.0025f * t;
            return p + new Vector3(wobble, 0f, Mathf.Cos(time * 7f + t * 5f) * 0.0015f * t);
        }

        void DrawStream()
        {
            if (streamFilter == null)
                return;
            if (streamMesh == null)
            {
                streamMesh = new Mesh { name = "WaterStream" };
                streamMesh.MarkDynamic();
                streamFilter.sharedMesh = streamMesh;
            }

            var space = streamFilter.transform;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < StreamSamples; i++)
            {
                var t = i / (float)(StreamSamples - 1);
                var p = StreamPoint(t);
                var next = StreamPoint(Mathf.Min(1f, t + 0.05f));
                var axis = (next - p).sqrMagnitude > 1e-8f ? (next - p).normalized : Vector3.down;
                var side = Vector3.Cross(axis, Vector3.forward);
                if (side.sqrMagnitude < 1e-6f)
                    side = Vector3.Cross(axis, Vector3.right);
                side.Normalize();
                var up = Vector3.Cross(side, axis);
                var radius = Mathf.Lerp(0.0042f, 0.0028f, t);
                for (var s = 0; s < StreamSides; s++)
                {
                    var a = s * Mathf.PI * 2f / StreamSides;
                    var n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    vertices.Add(space.InverseTransformPoint(p + n * radius));
                    normals.Add(space.InverseTransformDirection(n));
                }
            }

            for (var i = 0; i < StreamSamples - 1; i++)
            for (var s = 0; s < StreamSides; s++)
            {
                var a = i * StreamSides + s;
                var b = i * StreamSides + (s + 1) % StreamSides;
                var c = a + StreamSides;
                var d = b + StreamSides;
                triangles.AddRange(new[] { a, c, d, a, d, b });
            }

            streamMesh.Clear();
            streamMesh.SetVertices(vertices);
            streamMesh.SetNormals(normals);
            streamMesh.SetTriangles(triangles, 0);
            streamMesh.RecalculateBounds();
            streamRenderer.enabled = true;

            // A few droplets splash around the contact point.
            var drops = new List<Vector3>();
            for (var i = 0; i < Droplets; i++)
            {
                var phase = Mathf.Repeat(time * 2.2f + i * 0.37f, 1f);
                var angle = i * 2.39996f;
                var r = 0.012f + phase * 0.03f;
                var height = Mathf.Sin(phase * Mathf.PI) * 0.02f;
                drops.Add(contact + new Vector3(Mathf.Cos(angle) * r, 0.002f + height, Mathf.Sin(angle) * r));
            }

            DrawDroplets(drops, 0.0022f);
        }

        void DrawShower()
        {
            var drops = new List<Vector3>();
            var height = Mathf.Max(0.05f, showerTop - showerBottom);
            for (var i = 0; i < ShowerDrops; i++)
            {
                var angle = i * 2.39996f;
                var r = showerRadius * Mathf.Sqrt((i + 0.5f) / ShowerDrops);
                var fall = Mathf.Repeat(time * 1.6f + i * 0.137f, 1f);
                drops.Add(new Vector3(showerCentre.x + Mathf.Cos(angle) * r, showerTop - fall * height, showerCentre.z + Mathf.Sin(angle) * r));
            }

            DrawDroplets(drops, 0.0026f);
        }

        void DrawDroplets(List<Vector3> positions, float size)
        {
            if (dropletFilter == null)
                return;
            if (dropletMesh == null)
            {
                dropletMesh = new Mesh { name = "WaterDroplets" };
                dropletMesh.MarkDynamic();
                dropletFilter.sharedMesh = dropletMesh;
            }

            var space = dropletFilter.transform;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] faces = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            foreach (var position in positions)
            {
                var start = vertices.Count;
                var centre = space.InverseTransformPoint(position);
                foreach (var axis in axes)
                {
                    vertices.Add(centre + new Vector3(axis.x * size, axis.y * size * 1.6f, axis.z * size));
                    normals.Add(axis);
                }

                foreach (var index in faces)
                    triangles.Add(start + index);
            }

            dropletMesh.Clear();
            dropletMesh.SetVertices(vertices);
            dropletMesh.SetNormals(normals);
            dropletMesh.SetTriangles(triangles, 0);
            dropletMesh.RecalculateBounds();
            dropletRenderer.enabled = vertices.Count > 0;
        }
    }
}
