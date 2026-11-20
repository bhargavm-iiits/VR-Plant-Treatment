using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// The WATER console beside the soil tray (built by TreatmentChamberBuilder; replaces the Addendum 2 watering wand): a
    /// small angled charcoal console with large ON and OFF buttons, a short dark hose to a grey gooseneck outlet clamped on
    /// the tray rim, a thin water column with a splash where it meets the soil, and the wet marks on the soil. The buttons
    /// are world-space uGUI buttons over physical caps, so XR poke, controller rays and the desktop mouse ray all press
    /// them; a pressed cap dips 6 mm and ON glows cyan while the water runs. <see cref="ChamberStation"/> owns the
    /// moisture model (<see cref="RootZoneWateringModel"/>) and tells this view what to show.
    /// </summary>
    public sealed class WaterStation : MonoBehaviour
    {
        const float CapTravel = 0.006f, PressSeconds = 0.18f;
        const int StreamSamples = 10, StreamSides = 8, Droplets = 12;

        [SerializeField] Transform console;
        [SerializeField] Button onButton;
        [SerializeField] Button offButton;
        [SerializeField] Transform onCap;
        [SerializeField] Transform offCap;
        [SerializeField] MeshRenderer onCapRenderer;
        [SerializeField] MeshRenderer offCapRenderer;
        [SerializeField] Material capIdle;
        [SerializeField] Material capOnGlow;
        [SerializeField] Material capOffLit;
        [SerializeField] MeshRenderer drainLed;
        [SerializeField] Material ledIdle;
        [SerializeField] Material ledGlow;
        [SerializeField] TMP_Text drainLabel;
        [SerializeField] Transform hosePort;
        [SerializeField] MeshFilter hose;
        [SerializeField] MeshFilter outlet;
        [SerializeField] MeshFilter stream;
        [SerializeField] MeshRenderer streamRenderer;
        [SerializeField] MeshFilter droplets;
        [SerializeField] MeshRenderer dropletRenderer;
        [SerializeField] GameObject hand;

        Mesh hoseMesh, outletMesh, streamMesh, dropletMesh;
        Vector3 onHome, offHome;
        bool homes;
        float onPress, offPress;
        bool flowing, draining;
        float time;
        Vector3 tip, contact;
        Transform wetPatch, wetFront;
        float soilY, frontZ, wetMax = 0.12f, wetDepth = 0.08f;
        Vector2 soilMin, soilMax;
        float wetness;
        /// <summary>The wetting front behind the clear tray wall lags the surface wetness (water takes time to soak down).</summary>
        float frontWetness;
        WaterStreamFx streamFx;
        float flowScale = 1f;
        BTP.UI.PhysicalButton onKey, offKey;
        Material onIdle, onLit, offIdle, offLit;
        MaterialPropertyBlock wetBlock;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        const float FrontSoakRate = 0.09f;

        public event Action OnPressed;
        public event Action OffPressed;
        /// <summary>The console's own "Water to add" [-] (-1) / [+] (+1) (Addendum 6: the console works on its own).</summary>
        public event Action<int> AmountStepRequested;

        [Header("Addendum 6: WATER CONTROL console readout")]
        [SerializeField] Button amountMinus;
        [SerializeField] Button amountPlus;
        [SerializeField] TMP_Text amountText;
        [SerializeField] TMP_Text levelText;
        [SerializeField] TMP_Text targetText;
        [SerializeField] TMP_Text statusText;
        [SerializeField] Transform gaugeNeedle;
        [SerializeField] Transform gaugeTickMin;
        [SerializeField] Transform gaugeTickMax;

        /// <summary>Gauge sweep: 0% at -GaugeSweep degrees, 100% at +GaugeSweep (about the dial's normal).</summary>
        public const float GaugeSweep = 120f;

        internal void BindConsole(Button minus, Button plus, TMP_Text amount, TMP_Text level, TMP_Text target, TMP_Text status, Transform needle,
            Transform tickMin, Transform tickMax)
        {
            amountMinus = minus; amountPlus = plus; amountText = amount; levelText = level; targetText = target; statusText = status;
            gaugeNeedle = needle; gaugeTickMin = tickMin; gaugeTickMax = tickMax;
        }

        public Button AmountMinus => amountMinus;
        public Button AmountPlus => amountPlus;
        public string AmountReadout => amountText != null ? amountText.text : "";
        public string LevelReadout => levelText != null ? levelText.text : "";
        public string StatusReadout => statusText != null ? statusText.text : "";
        /// <summary>The gauge needle's angle (degrees about the dial's normal; -GaugeSweep = 0%, +GaugeSweep = 100%).</summary>
        public float NeedleAngle { get; private set; } = -GaugeSweep;
        public bool Connected { get; private set; } = true;

        /// <summary>The console readout and the gauge (values 0..1 of the moisture range; NaN hides the needle).</summary>
        public void ShowReadout(string amount, string level, string target, string status, float moisture, float bandMin, float bandMax, bool canMinus,
            bool canPlus)
        {
            if (amountText != null)
                amountText.text = amount ?? "";
            if (levelText != null)
                levelText.text = level ?? "";
            if (targetText != null)
                targetText.text = target ?? "";
            if (statusText != null)
                statusText.text = status ?? "";
            if (amountMinus != null)
                amountMinus.interactable = canMinus;
            if (amountPlus != null)
                amountPlus.interactable = canPlus;
            float Angle(float unit) => Mathf.Lerp(-GaugeSweep, GaugeSweep, Mathf.Clamp01(unit));
            if (gaugeNeedle != null)
            {
                gaugeNeedle.gameObject.SetActive(!float.IsNaN(moisture));
                NeedleAngle = float.IsNaN(moisture) ? -GaugeSweep : Angle(moisture);
                gaugeNeedle.localRotation = Quaternion.Euler(0f, NeedleAngle, 0f);
            }

            foreach (var (tick, value) in new[] { (gaugeTickMin, bandMin), (gaugeTickMax, bandMax) })
            {
                if (tick == null)
                    continue;
                tick.gameObject.SetActive(!float.IsNaN(value));
                if (!float.IsNaN(value))
                    tick.localRotation = Quaternion.Euler(0f, Angle(value), 0f);
            }
        }

        /// <summary>Shows the hose and the gooseneck outlet only while the console is connected to a loaded case's tray.</summary>
        public void SetConnected(bool connected)
        {
            Connected = connected;
            foreach (var filter in new[] { hose, outlet })
                if (filter != null && filter.TryGetComponent<MeshRenderer>(out var r))
                    r.enabled = connected;
            if (!connected)
                SetFlow(false, false);
        }

        public Transform Console => console;
        public Vector3 OutletTip => tip;
        /// <summary>Where the stream meets the soil.</summary>
        public Vector3 Contact => contact;
        public bool Flowing => flowing;
        public bool DrainShown => draining;
        public float Wetness => wetness;
        public bool OnGlowing => onCapRenderer != null && (onCapRenderer.sharedMaterial == capOnGlow || (onLit != null && onCapRenderer.sharedMaterial == onLit));
        /// <summary>The falling stream (Play mode; null in edit mode, where the mesh column is drawn instead).</summary>
        public WaterStreamFx StreamFx => streamFx;
        /// <summary>Wetness of the front seen through the tray window (lags <see cref="Wetness"/>).</summary>
        public float FrontWetness => frontWetness;
        public Vector3 OnCapTop => onCap != null ? onCap.position + onCap.up * (onCap.lossyScale.y * 0.5f) : transform.position;
        public GameObject Hand => hand;
        public Button OnButton => onButton;
        public Button OffButton => offButton;

        internal void Bind(Transform consoleRoot, Button on, Button off, Transform onCapPart, Transform offCapPart, MeshRenderer onRenderer,
            MeshRenderer offRenderer, Material idle, Material onGlow, Material offLit, MeshRenderer led, Material ledOff, Material ledOn, TMP_Text drain,
            Transform port, MeshFilter hoseFilter, MeshFilter outletFilter, MeshFilter streamFilter, MeshRenderer streamMeshRenderer,
            MeshFilter dropletFilter, MeshRenderer dropletMeshRenderer, GameObject handRig)
        {
            console = consoleRoot; onButton = on; offButton = off; onCap = onCapPart; offCap = offCapPart; onCapRenderer = onRenderer;
            offCapRenderer = offRenderer; capIdle = idle; capOnGlow = onGlow; capOffLit = offLit; drainLed = led; ledIdle = ledOff; ledGlow = ledOn;
            drainLabel = drain; hosePort = port; hose = hoseFilter; outlet = outletFilter; stream = streamFilter; streamRenderer = streamMeshRenderer;
            droplets = dropletFilter; dropletRenderer = dropletMeshRenderer; hand = handRig;
        }

        void Awake() => Wire();

        bool wired;

        /// <summary>Connects the buttons (Awake; edit-mode tools call it themselves).</summary>
        public void Wire()
        {
            if (wired)
                return;
            wired = true;
            CaptureHomes();
            if (onButton != null)
            {
                onButton.onClick.RemoveAllListeners();
                onButton.onClick.AddListener(() => GuardedPress(onButton, PressOn));
            }

            if (offButton != null)
            {
                offButton.onClick.RemoveAllListeners();
                offButton.onClick.AddListener(() => GuardedPress(offButton, PressOff));
            }

            void Step(Button button, int direction)
            {
                if (button == null)
                    return;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => GuardedPress(button, () => AmountStepRequested?.Invoke(direction)));
            }

            Step(amountMinus, -1);
            Step(amountPlus, +1);
            if (Application.isPlaying)
                MakeKeysPhysical();
        }

        /// <summary>
        /// Play mode: ON and OFF become bevelled keycaps (green and red runtime copies of the cap materials) that travel 5 mm
        /// under the press (<see cref="BTP.UI.PhysicalButton"/> drives the travel; this view only picks the lit state), and
        /// the "Water to add" keys get the raised physical look.
        /// </summary>
        void MakeKeysPhysical()
        {
            foreach (var capPart in new[] { onCap, offCap })
                if (capPart != null && capPart.TryGetComponent<MeshFilter>(out var filter))
                    filter.sharedMesh = BTP.UI.PhysicalButton.KeycapMesh;
            var idleSource = capIdle != null ? capIdle : capOffLit;
            var glowSource = capOnGlow != null ? capOnGlow : idleSource;
            onIdle = ChamberGlassPhysics.Tinted(idleSource, new Color(0.1f, 0.32f, 0.17f), "ON idle", 0.6f);
            onLit = ChamberGlassPhysics.Tinted(glowSource, new Color(0.2f, 0.95f, 0.45f), "ON lit");
            offIdle = ChamberGlassPhysics.Tinted(idleSource, new Color(0.36f, 0.09f, 0.08f), "OFF idle", 0.6f);
            offLit = ChamberGlassPhysics.Tinted(glowSource, new Color(0.98f, 0.25f, 0.2f), "OFF lit");
            if (onButton != null && onCap != null)
                onKey = BTP.UI.PhysicalButton.AttachCap(onButton, onCap);
            if (offButton != null && offCap != null)
                offKey = BTP.UI.PhysicalButton.AttachCap(offButton, offCap);
            BTP.UI.PhysicalButton.Attach(amountMinus);
            BTP.UI.PhysicalButton.Attach(amountPlus);
            SetFlow(flowing, draining);
        }

        void CaptureHomes()
        {
            if (homes)
                return;
            homes = true;
            if (onCap != null)
                onHome = onCap.localPosition;
            if (offCap != null)
                offHome = offCap.localPosition;
        }

        void OnDestroy()
        {
            foreach (var mesh in new[] { hoseMesh, outletMesh, streamMesh, dropletMesh })
            {
                if (mesh == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(mesh);
                else
                    DestroyImmediate(mesh);
            }

            foreach (var material in new[] { onIdle, onLit, offIdle, offLit })
                if (material != null)
                    Destroy(material);
        }

        static void GuardedPress(Button button, Action press)
        {
            var accepted = BTP.UI.PressGuard.Shared.TryPress(button, Time.unscaledTime);
            HandTestLog.Log("water:" + button.name, null, "ui", accepted ? "pressed" : "duplicate ignored");
            if (accepted)
                press();
        }

        /// <summary>ON (button, poke, ray, mouse or tests): the cap dips and the station is asked to start or resume.</summary>
        public void PressOn()
        {
            onPress = PressSeconds;
            if (onKey != null)
                onKey.Pulse();
            OnPressed?.Invoke();
        }

        /// <summary>OFF: the cap dips and the station is asked to stop.</summary>
        public void PressOff()
        {
            offPress = PressSeconds;
            if (offKey != null)
                offKey.Pulse();
            OffPressed?.Invoke();
        }

        /// <summary>0..1 flow of the stream (from "Water to add"); applied the next time the water starts.</summary>
        public void SetFlowScale(float value) => flowScale = Mathf.Clamp(value, 0.2f, 1f);

        // ------------------------------------------------------------------ placement

        /// <summary>
        /// Fits the outlet to a tray: the gooseneck rises from <paramref name="rimBase"/> on the rim and arcs over to a spout
        /// above <paramref name="soilContact"/>; the hose runs from the console's port down and over to the rim clamp.
        /// </summary>
        public void Fit(TreatmentChamber chamber, Vector3 rimBase, Vector3 soilContact, Vector3 outward) => Fit(chamber, rimBase, soilContact, outward, null);

        /// <summary>
        /// As the four-argument Fit, with the hose laid along <paramref name="route"/> (world points between the console port
        /// and the rim clamp): out of the port, down to the floor, along the route with rounded bends and up to the clamp.
        /// </summary>
        public void Fit(TreatmentChamber chamber, Vector3 rimBase, Vector3 soilContact, Vector3 outward, IReadOnlyList<Vector3> route)
        {
            CaptureHomes();
            soilY = chamber.SoilWorldY;
            chamber.SoilArea(out soilMin, out soilMax);
            wetPatch = chamber.WetPatch;
            wetFront = chamber.WetFront;
            frontZ = chamber.transform.TransformPoint(new Vector3(0f, 0f, chamber.TrayFrontZ)).z;
            var layout = chamber.Layout;
            wetMax = Mathf.Clamp(Mathf.Min(layout.TraySize.x, layout.TraySize.y) * 0.42f, 0.06f, 0.3f);
            wetDepth = Mathf.Max(0.02f, (layout.TrayHeight - TreatmentChamber.TrayWallThickness) * 0.7f);
            contact = new Vector3(soilContact.x, soilY, soilContact.z);
            tip = contact + Vector3.up * 0.075f;

            // Gooseneck: up from the rim clamp, over and down to the spout.
            var riserTop = rimBase + Vector3.up * 0.13f;
            var path = new List<Vector3> { rimBase - Vector3.up * 0.02f, rimBase + Vector3.up * 0.06f };
            path.AddRange(TubeMesh.Bezier(rimBase + Vector3.up * 0.065f, riserTop + Vector3.up * 0.04f, tip + Vector3.up * 0.1f, tip + Vector3.up * 0.025f, 12));
            path.Add(tip);
            outletMesh = Fill(outlet, outletMesh, "GooseneckOutlet", path, 0.012f, 0.0095f);

            // Hose along the floor route (Addendum 6: the console is a separate station).
            if (hosePort != null && route != null && route.Count > 0)
            {
                var clamp = rimBase + outward * 0.03f - Vector3.up * 0.03f;
                var start = hosePort.position;
                var floorStart = new Vector3(start.x, route[0].y, start.z) + hosePort.forward * 0.14f;
                var points = new List<Vector3> { start, start + hosePort.forward * 0.06f, floorStart };
                points.AddRange(route);
                var last = route[route.Count - 1];
                points.Add(new Vector3(last.x, clamp.y - 0.06f, last.z));
                points.Add(clamp);
                HosePoints = Rounded(points, 0.08f);
                hoseMesh = Fill(hose, hoseMesh, "WaterHose", HosePoints, 0.013f, 0.013f);
            }
            else if (hosePort != null)
            {
                var clamp = rimBase + outward * 0.03f - Vector3.up * 0.03f;
                var start = hosePort.position;
                var sag = Mathf.Min(start.y, clamp.y) - 0.08f;
                var a = start - hosePort.forward * 0.02f;
                var hosePath = TubeMesh.Bezier(a, new Vector3(a.x, sag, a.z) + (clamp - a) * 0.25f, new Vector3(clamp.x, sag, clamp.z) + outward * 0.1f, clamp, 16);
                hoseMesh = Fill(hose, hoseMesh, "WaterHose", hosePath, 0.011f, 0.011f);
            }

            if (Application.isPlaying)
                EnsureStream(chamber);
            ResetWater();
        }

        /// <summary>Play mode: the ballistic stream with its splash and ripples, and the water spreading over the soil top.</summary>
        void EnsureStream(TreatmentChamber chamber)
        {
            var glass = chamber != null ? chamber.GlassMaterial : null;
            if (glass == null)
                return;
            if (streamFx == null)
                // 110 splash particles: with the sprayer's mist (120), the wand (30) and the recovery sparkle (40) the
                // chamber stays within ChamberGlassPhysics.ParticleBudget (300).
                streamFx = WaterStreamFx.Create("WaterStreamFx", transform, glass, 110);
            streamFx.SetSource(tip, Vector3.down * (0.35f + 0.5f * flowScale));
            streamFx.SetGround(soilY);
            // The water spreading over the soil top (wet patch, satin sheen, shallow film) replaces the flat wet disc.
            spread = SoilWaterSpread.For(chamber);
            spread?.Fit(chamber);
        }

        SoilWaterSpread spread;

        /// <summary>The soil-top spread (Play mode; null in edit mode, where the flat wet disc is drawn instead).</summary>
        public SoilWaterSpread Spread => spread;

        /// <summary>The hose centreline after the last Fit (verification).</summary>
        public List<Vector3> HosePoints { get; private set; } = new List<Vector3>();

        /// <summary>A polyline with each inner corner replaced by a short quadratic bend of up to <paramref name="radius"/>.</summary>
        static List<Vector3> Rounded(IReadOnlyList<Vector3> points, float radius)
        {
            var result = new List<Vector3> { points[0] };
            for (var i = 1; i < points.Count - 1; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                var c = points[i + 1];
                var inLength = (b - a).magnitude;
                var outLength = (c - b).magnitude;
                if (inLength < 1e-4f || outLength < 1e-4f)
                    continue;
                var r = Mathf.Min(radius, inLength * 0.45f, outLength * 0.45f);
                var p0 = b + (a - b) / inLength * r;
                var p2 = b + (c - b) / outLength * r;
                for (var k = 0; k <= 4; k++)
                {
                    var t = k / 4f;
                    result.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * b + t * t * p2);
                }
            }

            result.Add(points[points.Count - 1]);
            for (var i = result.Count - 1; i > 0; i--)
                if ((result[i] - result[i - 1]).sqrMagnitude < 1e-8f)
                    result.RemoveAt(i);
            return result;
        }

        Mesh Fill(MeshFilter filter, Mesh mesh, string name, IReadOnlyList<Vector3> points, float r0, float r1)
        {
            if (filter == null)
                return mesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                filter.sharedMesh = mesh;
            }

            TubeMesh.Fill(mesh, filter.transform, points, r0, r1, 12);
            return mesh;
        }

        /// <summary>Dry soil, no water (new specimen or case).</summary>
        public void ResetWater()
        {
            wetness = 0f;
            frontWetness = 0f;
            spread?.Clear();
            SetFlow(false, false);
            if (wetPatch != null)
                wetPatch.gameObject.SetActive(false);
            if (wetFront != null)
                wetFront.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ state

        /// <summary>Water running into the soil (stream on, ON glowing) or draining (no stream, drain LED on).</summary>
        public void SetFlow(bool water, bool drain)
        {
            flowing = water;
            draining = drain;
            var running = water || drain;
            if (onCapRenderer != null)
                onCapRenderer.sharedMaterial = onLit != null ? (running ? onLit : onIdle) : running ? capOnGlow : capIdle;
            if (offCapRenderer != null)
                offCapRenderer.sharedMaterial = offLit != null ? (running ? offIdle : offLit) : running ? capIdle : capOffLit;
            if (drainLed != null)
                drainLed.sharedMaterial = drain ? ledGlow : ledIdle;
            spread?.SetFlowing(water);
            if (streamFx != null)
            {
                // Play mode: the ballistic stream replaces the mesh column and the splash cubes.
                if (streamRenderer != null)
                    streamRenderer.enabled = false;
                if (dropletRenderer != null)
                    dropletRenderer.enabled = false;
                streamFx.SetSource(tip, Vector3.down * (0.35f + 0.5f * flowScale));
                streamFx.SetGround(soilY);
                streamFx.SetFlow(water ? flowScale : 0f);
                return;
            }

            if (streamRenderer != null)
                streamRenderer.enabled = water;
            if (dropletRenderer != null)
                dropletRenderer.enabled = water;
            if (water)
                DrawStream();
        }

        /// <summary>The console's DRAIN sub-label for a negative amount.</summary>
        public void ShowDrainLabel(bool visible)
        {
            if (drainLabel != null)
                drainLabel.gameObject.SetActive(visible);
        }

        /// <summary>0..1 soil wetness at the outlet: the wet patch and the wet front behind the window grow with it.</summary>
        public void SetWetness(float value) => SetWetness(value, false);

        /// <summary>
        /// 0..1 soil wetness at the outlet. The wet patch spreads from the impact and gets darker (and, in Play mode, glossier
        /// through a wet sheen) as it soaks; the wetting front behind the clear tray wall follows more slowly (<see cref="Tick"/>)
        /// unless <paramref name="instant"/> (a restored specimen shows its soil as it was left).
        /// </summary>
        public void SetWetness(float value, bool instant)
        {
            wetness = Mathf.Clamp01(value);
            if (instant)
                frontWetness = wetness;
            var visible = wetness > 0.01f;
            if (spread != null)
            {
                // Play mode: the water spreads over the soil top from the impact (darker, satin, a shallow film while it flows).
                if (wetPatch != null)
                    wetPatch.gameObject.SetActive(false);
                spread.SetWater(contact, wetness, flowing, instant);
            }
            else if (wetPatch != null)
            {
                wetPatch.gameObject.SetActive(visible);
                if (visible)
                {
                    var size = Mathf.Lerp(0.03f, wetMax, Mathf.Sqrt(wetness));
                    wetPatch.position = new Vector3(contact.x, soilY + 0.0015f, contact.z);
                    var parent = wetPatch.parent != null ? wetPatch.parent.lossyScale : Vector3.one;
                    wetPatch.localScale = new Vector3(size / parent.x, 0.0008f, size / parent.z);
                    if (Application.isPlaying && wetPatch.TryGetComponent<MeshRenderer>(out var patchRenderer) && patchRenderer.sharedMaterial != null &&
                        patchRenderer.sharedMaterial.HasProperty(BaseColorId))
                    {
                        // Darker as it soaks: the wet soil's alpha rises (property block; the material asset is shared).
                        wetBlock ??= new MaterialPropertyBlock();
                        var color = patchRenderer.sharedMaterial.GetColor(BaseColorId);
                        color.a = Mathf.Lerp(0.38f, Mathf.Max(color.a, 0.88f), Mathf.Sqrt(wetness));
                        patchRenderer.GetPropertyBlock(wetBlock);
                        wetBlock.SetColor(BaseColorId, color);
                        patchRenderer.SetPropertyBlock(wetBlock);
                    }
                }
            }

            UpdateFront();
        }

        /// <summary>The wetting front seen through the tray window, from the lagging <see cref="frontWetness"/>.</summary>
        void UpdateFront()
        {
            if (wetFront == null)
                return;
            var visible = frontWetness > 0.01f;
            wetFront.gameObject.SetActive(visible);
            if (!visible)
                return;
            var depth = Mathf.Lerp(0.006f, wetDepth, frontWetness);
            // With the soil-top spread the front is as wide as the patch reaching the window (it lags as it soaks down).
            var width = spread != null
                ? Mathf.Clamp(spread.TargetRadiusNow * 1.6f * Mathf.Lerp(0.5f, 1f, frontWetness), 0.04f, soilMax.x - soilMin.x)
                : Mathf.Lerp(0.04f, wetMax * 1.3f, frontWetness);
            wetFront.position = new Vector3(Mathf.Clamp(contact.x, soilMin.x + width * 0.5f, soilMax.x - width * 0.5f), soilY - depth * 0.5f, frontZ + 0.0015f);
            wetFront.localScale = new Vector3(width, depth, 0.002f);
        }

        public void ShowHand(bool visible)
        {
            if (hand != null)
                hand.SetActive(visible);
        }

        void Update() => Tick(Time.deltaTime);

        /// <summary>Button travel, the water column's wobble and the splash (Update; the verification calls it directly).</summary>
        public void Tick(float deltaTime)
        {
            time += deltaTime;
            onPress = Mathf.Max(0f, onPress - deltaTime);
            offPress = Mathf.Max(0f, offPress - deltaTime);
            // In Play mode the physical keys own the caps' travel.
            if (onCap != null && onKey == null)
                onCap.localPosition = onHome - Vector3.up * (onPress > 0f ? CapTravel / Mathf.Max(onCap.parent != null ? onCap.parent.lossyScale.y : 1f, 1e-4f) : 0f);
            if (offCap != null && offKey == null)
                offCap.localPosition = offHome - Vector3.up * (offPress > 0f ? CapTravel / Mathf.Max(offCap.parent != null ? offCap.parent.lossyScale.y : 1f, 1e-4f) : 0f);
            if (flowing && streamFx == null)
                DrawStream();
            if (!Mathf.Approximately(frontWetness, wetness))
            {
                // The front soaks down (or dries) behind the surface.
                frontWetness = Mathf.MoveTowards(frontWetness, wetness, deltaTime * FrontSoakRate);
                UpdateFront();
            }
        }

        /// <summary>Holds a cap down (the verification shot: the fingertip on ON).</summary>
        public void HoldOnCap(bool down)
        {
            CaptureHomes();
            if (onKey != null)
            {
                onKey.HoldDown(down);
                return;
            }

            if (onCap != null)
                onCap.localPosition = onHome - Vector3.up * (down ? CapTravel / Mathf.Max(onCap.parent != null ? onCap.parent.lossyScale.y : 1f, 1e-4f) : 0f);
        }

        void DrawStream()
        {
            if (stream == null)
                return;
            var points = new List<Vector3>();
            for (var i = 0; i < StreamSamples; i++)
            {
                var t = i / (float)(StreamSamples - 1);
                var p = Vector3.Lerp(tip, contact, t);
                p += new Vector3(Mathf.Sin(time * 13f + t * 8f) * 0.0012f * t, 0f, Mathf.Cos(time * 9f + t * 6f) * 0.0008f * t);
                points.Add(p);
            }

            streamMesh = Fill(stream, streamMesh, "WaterColumn", points, 0.0035f, 0.0028f);
            if (droplets == null)
                return;
            if (dropletMesh == null)
            {
                dropletMesh = new Mesh { name = "WaterSplash" };
                droplets.sharedMesh = dropletMesh;
            }

            var space = droplets.transform;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] faces = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            for (var i = 0; i < Droplets; i++)
            {
                var phase = Mathf.Repeat(time * 2.4f + i * 0.31f, 1f);
                var angle = i * 2.39996f;
                var r = 0.008f + phase * 0.025f;
                var centre = space.InverseTransformPoint(contact + new Vector3(Mathf.Cos(angle) * r, 0.002f + Mathf.Sin(phase * Mathf.PI) * 0.015f, Mathf.Sin(angle) * r));
                var start = vertices.Count;
                foreach (var axis in axes)
                {
                    vertices.Add(centre + axis * 0.0022f);
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
        }
    }
}
