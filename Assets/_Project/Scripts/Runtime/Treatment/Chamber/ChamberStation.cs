using System;
using System.Collections.Generic;
using BTP.Core;
using BTP.Farm;
using BTP.Plants;
using TMPro;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The Treatment lab's specimen chamber station. For the selected case it shows the exact Farm plant that was carried
    /// over (<see cref="FarmSpecimenSnapshot"/>), or the crop's Farm model when none was, fits the glass chamber to it
    /// before revealing it (<see cref="ChamberFit"/>), places the control panel, case sign, tools, spawn point and the
    /// Interface around it, and runs the four-control treatment API (profile, Apply, Check Health) with a race guard
    /// (<see cref="ChamberCaseSession"/>): every request carries the selection's ticket and late responses are dropped.
    /// Any selection change (case selector, Farm specimen) refreshes plant, tray, chamber, sign, packet and controls
    /// together. Hall space (the ChamberLine transform): the chamber's front-right exterior corner is the origin, the
    /// chamber extends to -X and +Z, the viewer stands at -Z.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class ChamberStation : MonoBehaviour, IEnvironmentReadiness, IEnvironmentExit
    {
        public const float Eye = 1.6f;
        /// <summary>Kiosk screen centre height above the floor (constant for every chamber).</summary>
        public const float PanelHeight = 1.16f;
        /// <summary>The seated eye height every station must also serve.</summary>
        public const float SeatedEye = 1.2f;
        /// <summary>World size of one canvas unit on the kiosk screen (1280 x 760 units -> 1.024 x 0.608 m; 88-unit buttons are 70 mm).</summary>
        public const float PanelScale = 0.0008f;
        public const float PanelWidth = 1280f * PanelScale;
        public const float NozzleHeight = 0.11f;
        const float WaterRate = 9f;

        [SerializeField] TreatmentChamber chamber;
        [Tooltip("Hall origin of the chamber: its front-right exterior corner.")]
        [SerializeField] Transform chamberLine;
        [SerializeField] ChamberControlPanel panel;
        [SerializeField] Transform panelRoot;
        [SerializeField] Transform panelCanvas;
        [SerializeField] Transform signRoot;
        [SerializeField] TMP_Text signTitle;
        [SerializeField] TMP_Text signScenario;
        [SerializeField] TMP_Text signSpecimen;
        [SerializeField] GameObject fertiliserCard;
        [SerializeField] TMP_Text fertiliserCardBody;
        [SerializeField] SpawnAnchor spawnAnchor;
        [SerializeField] Transform interfaceRoot;
        [SerializeField] DiseaseVariantCatalog variants;
        [SerializeField] PlantCatalog plants;
        [SerializeField] FertiliserApplicator applicator;
        [SerializeField] WateringWand wand;
        [SerializeField] Transform sprayMarker;
        [SerializeField] TMP_Text sprayMarkerLabel;
        [SerializeField] TMP_Text rootZoneLabel;
        [Tooltip("The WATER console, hose and gooseneck outlet (Addendum 3; replaces the watering wand).")]
        [SerializeField] WaterStation water;

        /// <summary>What the chamber shows.</summary>
        public sealed class Shown
        {
            public SpecimenRef Specimen;
            public string PlantId;
            public string DiseaseId;
            public Vector3 Scale = Vector3.one;
            public float Yaw;
            public float DiseaseAmount = 1f;
            /// <summary>True when this is the exact Farm plant (snapshot or selected specimen): results are saved to it.</summary>
            public bool FromFarm;
            /// <summary>"farm" (snapshot), "catalog" (crop's Farm model) or "none".</summary>
            public string Source;
            public Bounds PlantBounds;
            public string Note;
            public string SpecimenId => Specimen != null ? Specimen.SpecimenId : "";
        }

        readonly ChamberCaseSession session = new ChamberCaseSession();
        readonly List<Material> materials = new List<Material>();
        readonly List<Vector3> plantPoints = new List<Vector3>();
        TreatmentCaseClient client;
        Transform holder;
        GameObject specimen;
        Shown shown;
        bool pending;
        bool started;
        string scenario = "standard";
        float amountFrom, amountTo, amountNow = 1f, blendTime;
        bool blending;
        double soilNow, soilTarget;
        bool waterQueued;
        float waterHold;
        int applyCount;
        GranuleSimulation.Area granuleArea;
        Vector3 pourPoint;
        Vector3 wandContact;
        float lowestLeafAboveContact;
        RootZoneWateringModel watering;
        float wet;
        bool soilDirty;
        readonly SpecimenRecovery recovery = new SpecimenRecovery();

        /// <summary>The specimen's recovery after a correct Apply (disease fades, the plant perks up, a sparkle at the end).</summary>
        public SpecimenRecovery Recovery => recovery;

        // ------------------------------------------------------------------ guided treatment hooks

        /// <summary>
        /// Guided, hands-on treatment (<see cref="GuidedTreatmentController"/>, Play mode): the front glass door stays in place
        /// for the player to open (it is never retracted), the legacy packet rig is hidden (the NPK packets and scoop replace
        /// it) and the spray bottle is offered for every case.
        /// </summary>
        public bool GuidedMode { get; set; }
        /// <summary>Replaces the kiosk's Apply while set (the guide validates the current step); returns whether it applied.</summary>
        public Func<TreatmentSettings, bool> ApplyOverride { get; set; }
        /// <summary>Asked before the WATER console's ON starts the water; false refuses (the guide explains why).</summary>
        public Func<bool> WaterOnGate { get; set; }
        /// <summary>A profile was accepted and the controls were reset for it.</summary>
        public event Action<TreatmentProfile> ProfileReady;
        /// <summary>A new specimen is shown (its profile is loading).</summary>
        public event Action SpecimenDisplayed;
        /// <summary>The specimen's per-instance materials (disease amount, wet sheen).</summary>
        public IReadOnlyList<Material> SpecimenMaterials => materials;

        /// <summary>Blends the chamber specimen's disease to <paramref name="amount"/> (a response step; the recovery owns it once engaged).</summary>
        public void BlendDiseaseTo(float amount)
        {
            if (!recovery.Engaged)
                BlendDisease(Mathf.Clamp01(amount));
        }

        public bool IsEnvironmentReady { get; private set; }
        public ChamberCaseSession Session => session;
        public ChamberControlPanel Panel => panel;
        public TreatmentChamber Chamber => chamber;
        public FertiliserApplicator Applicator => applicator;
        public WateringWand Wand => wand;
        public GameObject Specimen => specimen;
        public Shown Current => shown;
        public TreatmentCaseClient Client { get => client; set => client = value; }
        /// <summary>The path of the last profile request ("/api/treatment/cases/tomato/early_blight?scenario=standard").</summary>
        public string LastProfilePath { get; private set; }
        public string LastEvaluatePath { get; private set; }
        public double SoilRootZoneNow => watering != null ? watering.Current : soilNow;
        /// <summary>The WATER station's moisture model for the loaded profile (null before a profile loads).</summary>
        public RootZoneWateringModel Watering => watering;
        public WaterStation Water => water;
        public Vector3 PourPoint => pourPoint;
        public Vector3 WandContact => wandContact;
        /// <summary>Lowest plant point in the column above the wand's contact (height above the soil), +inf when clear.</summary>
        public float LowestLeafAboveContact => lowestLeafAboveContact;
        public GranuleSimulation.Area GranuleArea => granuleArea;
        public float CurrentDiseaseAmount => amountNow;
        public string SignTitleText => signTitle != null ? signTitle.text : "";
        public string SignScenarioText => signScenario != null ? signScenario.text : "";
        public string SignSpecimenText => signSpecimen != null ? signSpecimen.text : "";
        public Transform PanelCanvas => panelCanvas;
        public Transform SignRoot => signRoot;
        public SpawnAnchor SpawnPoint => spawnAnchor;
        public Transform ChamberLine => chamberLine;
        public IReadOnlyList<Vector3> PlantPoints => plantPoints;
        /// <summary>World points on the specimen's surface (leaves, stems, fruit): where the sprayer's mist leaves wet beads.</summary>
        public IReadOnlyList<Vector3> FoliagePoints => foliagePoints;
        readonly List<Vector3> foliagePoints = new List<Vector3>();
        /// <summary>Downward look angle from the spawn eye that frames the tray window and the canopy (verification view).</summary>
        public float ViewPitch { get; private set; } = 15f;

        internal void Bind(TreatmentChamber chamberPart, Transform line, ChamberControlPanel controls, Transform panelBase, Transform panelSurface,
            Transform sign, TMP_Text title, TMP_Text scenarioText, TMP_Text specimenText, GameObject card, TMP_Text cardBody, SpawnAnchor spawn,
            Transform ui, DiseaseVariantCatalog variantCatalog, PlantCatalog plantCatalog, FertiliserApplicator fertiliser, WateringWand watering,
            Transform marker, TMP_Text markerLabel, TMP_Text soilLabel)
        {
            chamber = chamberPart; chamberLine = line; panel = controls; panelRoot = panelBase; panelCanvas = panelSurface; signRoot = sign;
            signTitle = title; signScenario = scenarioText; signSpecimen = specimenText; fertiliserCard = card; fertiliserCardBody = cardBody;
            spawnAnchor = spawn; interfaceRoot = ui; variants = variantCatalog; plants = plantCatalog; applicator = fertiliser; wand = watering;
            sprayMarker = marker; sprayMarkerLabel = markerLabel; rootZoneLabel = soilLabel;
        }

        internal void BindWater(WaterStation station) => water = station;

        [Tooltip("The matte training sprayer shown beside the tray for spray-indicated cases (Spray page).")]
        [SerializeField] Transform sprayer;

        internal void BindSprayer(Transform prop) => sprayer = prop;

        [Tooltip("The packet and sprayer docks on the TREATMENT TOOLS bench (their tops; forward faces away from the user).")]
        [SerializeField] Transform packetShelf;
        [SerializeField] Transform sprayerShelf;

        internal void BindShelves(Transform packetDock, Transform sprayerDock)
        {
            packetShelf = packetDock;
            sprayerShelf = sprayerDock;
        }

        [Header("Addendum 6: separate stations")]
        [Tooltip("The TREATMENT TOOLS bench (packet and sprayer docks).")]
        [SerializeField] Transform toolsBench;
        [Tooltip("Hall interior in chamber-line space: x, width = X range; y, height = Z range (front wall to back wall).")]
        [SerializeField] Rect hallRect = Rect.MinMaxRect(-9.4f, -7f, 4.5f, 9.4f);
        [SerializeField] Transform kioskAnchor;
        [SerializeField] Transform waterAnchor;
        [SerializeField] Transform toolsAnchor;
        [SerializeField] Transform plantAnchor;

        internal void BindStations(Transform bench, Rect hall, Transform kiosk, Transform waterPoint, Transform toolsPoint, Transform plantPoint)
        {
            toolsBench = bench;
            hallRect = hall;
            kioskAnchor = kiosk;
            waterAnchor = waterPoint;
            toolsAnchor = toolsPoint;
            plantAnchor = plantPoint;
        }

        /// <summary>The station layout for the current chamber (line space).</summary>
        public StationLayout.Result Stations { get; private set; }
        public Transform ToolsBench => toolsBench;
        public Transform KioskRoot => panelRoot;
        public Transform PacketDock => packetShelf;
        public Transform SprayerDock => sprayerShelf;
        public Rect HallRect => hallRect;
        /// <summary>Downward look of the overview spawn (shows every station).</summary>
        public float SpawnPitch { get; private set; } = 8f;

        /// <summary>Teleport anchor (standing point facing the station) of a station; the plant anchor faces the tray window.</summary>
        public Transform AnchorOf(StationLayout.Station station) => station switch
        {
            StationLayout.Station.Kiosk => kioskAnchor,
            StationLayout.Station.Water => waterAnchor,
            _ => toolsAnchor,
        };

        public Transform PlantAnchor => plantAnchor;

        /// <summary>"Go to station" (touchscreen menu): moves the user to that station's anchor (Play mode; the rig moves).</summary>
        public void GoTo(int target)
        {
            var anchor = target switch
            {
                0 => waterAnchor,
                1 => toolsAnchor,
                2 => plantAnchor,
                3 => kioskAnchor,
                _ => spawnAnchor != null ? spawnAnchor.transform : null,
            };
            if (anchor != null && EnvironmentController.Instance != null)
                EnvironmentController.Instance.MoveTo(anchor);
        }

        Collider sprayTarget;
        bool handsOutOfView;

        public TrainingSprayTool SprayTool
        {
            get
            {
                var tool = sprayer != null ? sprayer.GetComponent<TrainingSprayTool>() : null;
                return tool != null ? tool : null;
            }
        }
        public Collider SprayTarget => sprayTarget;
        public bool HandsOutOfView => handsOutOfView;

        /// <summary>Tracking loss of <paramref name="hand"/>: spraying and pouring stop this frame and the held tool returns to its dock.</summary>
        public void HandleTrackingLost(string hand)
        {
            if (applicator != null && applicator.IsHeld && (applicator.HoldingHand == hand || applicator.HoldingHand == "unknown" || hand == "both"))
                applicator.ForceRelease("tracking lost");
            var tool = SprayTool;
            if (tool != null && tool.IsHeld && (tool.HoldingHand == hand || hand == "both"))
                tool.ForceRelease("tracking lost");
        }

        /// <summary>The holding hand's pinch drives the sprayer (a separate pinch after the pickup).</summary>
        public void HandlePinch(string hand, bool down)
        {
            var tool = SprayTool;
            if (tool != null && tool.IsHeld && tool.HoldingHand == hand)
                tool.SetPinch(down);
        }

        /// <summary>"Hands out of view" on the touchscreen header while tracking stays lost; specimen and settings are kept.</summary>
        public void ShowHandsOutOfView(bool visible)
        {
            handsOutOfView = visible;
            panel?.ShowHandsNotice(visible ? HandLossTracker.Message : null);
        }

        void OnSprayChanged()
        {
            var tool = SprayTool;
            if (tool == null || panel == null || session.Profile == null)
                return;
            // Spraying feeds the Spray settings: the amount sprayed and the coverage reached on the marked foliage.
            panel.SetValue(TreatmentProfile.Spray, "dose", Mathf.Floor(tool.SprayedUnits));
            panel.SetValue(TreatmentProfile.Spray, "targetCoverage", Mathf.Floor(tool.Coverage / 5f) * 5f);
        }

        public Transform Sprayer => sprayer;

        // ------------------------------------------------------------------ lifecycle

        void Start()
        {
            Initialise();
            // The guided, hands-on treatment runs on top of the kiosk in Play mode (step popups, tools, lamp).
            if (Application.isPlaying && GuidedTreatmentConfig.Load().guidedEnabled && GetComponent<GuidedTreatmentController>() == null)
                gameObject.AddComponent<GuidedTreatmentController>();
            var state = SelectionState.Instance;
            if (state != null)
            {
                state.CaseChanged += OnCaseChanged;
                state.SpecimenChanged += OnSpecimenChanged;
            }

            // One UI surface: the XR status panel hides here and its scan-switch question becomes the touchscreen's modal.
            var coordinator = BTP.XR.XrSessionCoordinator.Instance;
            if (coordinator != null)
            {
                coordinator.StatusPanelSuppressed = true;
                coordinator.SwitchPromptChanged += OnSwitchPrompt;
                OnSwitchPrompt();
            }

            RebuildFromSelection();
            IsEnvironmentReady = true;
        }

        void OnSwitchPrompt()
        {
            var coordinator = BTP.XR.XrSessionCoordinator.Instance;
            if (coordinator == null)
                return;
            ShowScanSwitch(coordinator.SwitchPromptPending, coordinator.SwitchPromptText, coordinator.OpenNewScan, coordinator.KeepCurrentScan);
        }

        /// <summary>
        /// The XR scan-switch question as the touchscreen's only modal ("Open New Scan" / "Keep Current Scan"); the actions
        /// are the coordinator's existing unsaved-result handling. Input behind the modal is blocked while it is open.
        /// </summary>
        public void ShowScanSwitch(bool pending, string text, System.Action openNew, System.Action keepCurrent)
        {
            if (panel == null)
                return;
            if (pending)
                panel.ShowModal("Open a different scan?", text, "Open New Scan", openNew, "Keep Current Scan", keepCurrent);
            else if (panel.ModalOpen)
                panel.HideModal();
        }

        /// <summary>Connects the panel and the API client (Start; edit-mode tools call it themselves).</summary>
        public void Initialise()
        {
            if (started)
                return;
            started = true;
            client ??= TreatmentCaseClient.FromConfiguration();
            if (panel != null)
            {
                panel.Wire();
                panel.ApplyRequested += OnApply;
                panel.CheckRequested += OnCheck;
                panel.RetryRequested += OnRetry;
                panel.ScenarioRequested += OnScenario;
                panel.ConnectionRetryRequested += OnRetry;
                panel.PageChanged += OnPage;
                panel.SettingChanged += OnSettingChanged;
            }

            if (wand != null)
                wand.TriggerChanged += OnWandTrigger;
            if (SprayTool != null)
                SprayTool.Changed += OnSprayChanged;
            if (panel != null)
            {
                panel.AmountStepRequested += OnAmountStep;
                panel.GoToRequested += GoTo;
            }

            if (water != null)
            {
                water.Wire();
                water.OnPressed += OnWaterOn;
                water.OffPressed += OnWaterOff;
                water.AmountStepRequested += OnAmountStep;
            }

            EnsureHolder();
        }

        void OnDestroy()
        {
            var coordinator = BTP.XR.XrSessionCoordinator.Instance;
            if (coordinator != null)
            {
                coordinator.SwitchPromptChanged -= OnSwitchPrompt;
                coordinator.StatusPanelSuppressed = false;
            }

            var state = SelectionState.Instance;
            if (state != null)
            {
                state.CaseChanged -= OnCaseChanged;
                state.SpecimenChanged -= OnSpecimenChanged;
            }

            if (wand != null)
                wand.TriggerChanged -= OnWandTrigger;
            ClearSpecimen();
            recovery.Dispose();
        }

        public void PrepareForExit()
        {
            if (watering != null && watering.Running)
                watering.Off();
            SaveSoil();
            blending = false;
            waterQueued = false;
            if (wand != null)
                wand.Stop();
        }

        void OnCaseChanged(string plantId, string diseaseId) => pending = true;

        void OnSpecimenChanged(SpecimenRef specimenRef) => pending = true;

        void LateUpdate()
        {
            // Selection events arrive in pairs (case, then specimen); one rebuild in the same frame covers both.
            if (pending)
            {
                pending = false;
                if (RebuildFromSelection() && EnvironmentController.Instance != null && !EnvironmentController.Instance.IsSwitching &&
                    EnvironmentController.Instance.Current == EnvironmentId.Treatment)
                    EnvironmentController.Instance.Recenter();
            }
        }

        void Update() => Tick(Time.deltaTime);

        // ------------------------------------------------------------------ selection -> specimen

        /// <summary>Shows the selected case's specimen. Returns false when nothing changed.</summary>
        bool RebuildFromSelection()
        {
            var state = SelectionState.Instance;
            CaseKey key;
            if (TreatmentServices.Instance != null)
                key = TreatmentServices.Instance.SelectedCase();
            else if (state != null && state.Current != null)
                key = new CaseKey(state.Current.PlantId, state.CurrentDiseaseId);
            else
                key = new CaseKey(PlantCatalog.DefaultPlantId, DiseaseClasses.DefaultDiseaseId(PlantCatalog.DefaultPlantId));

            var selected = state != null ? state.SelectedSpecimen : null;
            if (selected != null && !selected.IsCase(key.PlantId, key.DiseaseId))
                selected = null;
            if (shown != null && shown.PlantId == key.PlantId && shown.DiseaseId == key.DiseaseId &&
                (shown.Specimen?.SpecimenId ?? "") == (selected?.SpecimenId ?? ""))
                return false;

            FarmSpecimenSnapshot.Snapshot snapshot = null;
            if (selected != null && FarmSpecimenSnapshot.Instance != null)
                FarmSpecimenSnapshot.Instance.TryGet(selected, out snapshot);
            Show(key.PlantId, key.DiseaseId, selected, snapshot);
            return true;
        }

        /// <summary>
        /// Shows one specimen and loads its case's profile. <paramref name="snapshot"/> is the captured Farm plant (exact
        /// model, scale, yaw and disease); without it the crop's Farm model is shown at scale 1. A selected specimen
        /// without a snapshot is still the Farm plant the result belongs to. Returns the new selection ticket.
        /// </summary>
        public SelectionTicket Show(string plantId, string diseaseId, SpecimenRef selected, FarmSpecimenSnapshot.Snapshot snapshot, string requestedScenario = null)
        {
            Initialise();
            if (shown == null || shown.PlantId != plantId || shown.DiseaseId != diseaseId)
                scenario = "standard";
            if (!string.IsNullOrEmpty(requestedScenario))
                scenario = requestedScenario;

            var info = new Shown { PlantId = plantId, DiseaseId = diseaseId, Specimen = selected, FromFarm = selected != null };
            GameObject template = null;
            if (snapshot != null && snapshot.Template != null)
            {
                template = snapshot.Template;
                info.Scale = snapshot.Scale;
                info.Yaw = snapshot.Yaw;
                info.DiseaseAmount = snapshot.DiseaseAmount;
                info.Source = snapshot.Source ?? FarmSpecimenSnapshot.SourceFarm;
            }
            else
            {
                template = CatalogModel(plantId, diseaseId);
                info.Source = template != null ? "catalog" : "none";
                info.DiseaseAmount = template != null ? SpecimenGeometry.DiseaseAmount(template) : 1f;
                info.Note = template == null
                    ? "No model exists for this crop."
                    : selected != null
                        ? "The Farm plant was not captured; showing this crop's Farm model."
                        : "No Farm specimen was carried: showing this crop's Farm model. Results are not linked to a Farm plant.";
            }

            Display(info, template);
            SpecimenDisplayed?.Invoke();
            var ticket = session.Begin(info.SpecimenId, plantId, diseaseId, scenario);
            session.SpecimenShown(ticket);
            RequestProfile(ticket);
            return ticket;
        }

        GameObject CatalogModel(string plantId, string diseaseId)
        {
            if (variants != null && variants.TryGet(plantId, diseaseId, out var entry) && entry.farmPrefab != null)
                return entry.farmPrefab;
            var catalog = plants != null ? plants : SelectionState.Instance != null ? SelectionState.Instance.Catalog : null;
            if (catalog != null && catalog.TryGet(plantId, out var plant))
                return plant.FarmPrefab != null ? plant.FarmPrefab : plant.LabPrefab;
            return null;
        }

        void EnsureHolder()
        {
            if (holder != null || chamber == null)
                return;
            var existing = chamber.transform.Find("SpecimenHolder");
            holder = existing != null ? existing : new GameObject("SpecimenHolder").transform;
            holder.SetParent(chamber.transform, false);
        }

        void ClearSpecimen()
        {
            if (specimen != null)
                Kill(specimen);
            specimen = null;
            foreach (var material in materials)
                if (material != null)
                    Kill(material);
            materials.Clear();
            plantPoints.Clear();
        }

        static void Kill(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        /// <summary>
        /// Instantiates the specimen hidden, measures it at Farm scale in chamber space, fits and applies the chamber,
        /// moves the specimen onto the soil and only then reveals it, all in this call (never visible unfitted).
        /// </summary>
        void Display(Shown info, GameObject template)
        {
            EnsureHolder();
            ClearSpecimen();
            blending = false;
            waterQueued = false;
            if (applicator != null)
                applicator.ClearGranules();

            holder.gameObject.SetActive(false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;
            Bounds bounds;
            if (template != null)
            {
                specimen = Instantiate(template, holder, false);
                specimen.name = "Specimen_" + (string.IsNullOrEmpty(info.SpecimenId) ? info.PlantId : info.SpecimenId);
                specimen.SetActive(true);
                // Farm facing and scale in hall space (the chamber is never rotated or scaled).
                var chamberYaw = chamber.transform.eulerAngles.y;
                specimen.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, info.Yaw - chamberYaw, 0f));
                specimen.transform.localScale = info.Scale;
                SpecimenGeometry.Strip(specimen);
                materials.AddRange(SpecimenGeometry.CloneMaterials(specimen));
                SpecimenGeometry.SetDiseaseAmount(materials, info.DiseaseAmount);
                bounds = SpecimenGeometry.Measure(specimen, chamber.transform);
            }
            else
            {
                bounds = new Bounds(new Vector3(0f, 0.3f, 0f), new Vector3(0.3f, 0.6f, 0.3f));
            }

            info.PlantBounds = bounds;
            amountNow = info.DiseaseAmount;
            shown = info;
            var layout = template != null ? ChamberFit.Fit(bounds) : ChamberFit.Fit(new Bounds(new Vector3(0f, 0.3f, 0f), new Vector3(0.3f, 0.6f, 0.3f)));
            chamber.Apply(layout);
            chamber.transform.localPosition = new Vector3(-layout.Exterior.x * 0.5f, 0f, layout.Exterior.z * 0.5f);
            holder.localPosition = layout.SpecimenAnchor;
            holder.gameObject.SetActive(true);
            chamber.BuildRoots(ProceduralRoots.SeedFor(string.IsNullOrEmpty(info.SpecimenId) ? info.PlantId + "/" + info.DiseaseId : info.SpecimenId),
                bounds.size.y);

            if (specimen != null)
                SpecimenGeometry.VisitPoints(specimen, null, p => plantPoints.Add(p));
            foliagePoints.Clear();
            if (specimen != null)
                SpecimenGeometry.SurfacePoints(specimen, foliagePoints);
            // A new specimen is untreated: no recovery running, no override left from the previous one.
            if (Application.isPlaying)
                recovery.SetSparkle(chamber.AccentMaterial, chamber.transform);
            recovery.Bind(specimen, materials);
            recoveryShown = -2;
            panel?.SetRecoveryStatus(null, false);
            PlaceAround(layout);
            RefreshSign(null);
            if (wand != null)
            {
                wand.SetTray(chamber);
                wand.PresentHold();
            }

            watering = null;
            wet = 0f;
            panel?.SetWatering(null);
            if (water != null)
                water.ResetWater();
            RefreshWaterConsole();
        }

        // ------------------------------------------------------------------ placement

        Vector3 Hall(Vector3 local) => chamberLine != null ? chamberLine.TransformPoint(local) : local;

        /// <summary>
        /// Station layout (Addendum 6): the equipment stands as separate stations with wide walking paths around the fixed
        /// chamber (<see cref="StationLayout"/>): the touchscreen kiosk at the front-right, the WATER CONTROL console at the
        /// front-left and the TREATMENT TOOLS bench along the right-hand window wall. Each has a teleport anchor facing it;
        /// the plant anchor stands in front of the tray window (the close-up view of the specimen) and the spawn is the
        /// overview that shows all stations. Also places the spray marker, the tools, the hose and the soil readout.
        /// </summary>
        void PlaceAround(ChamberLayout layout)
        {
            var stem = chamber.SpecimenAnchor.position;
            var window = chamber.WindowCentre;
            var lineRotation = chamberLine != null ? chamberLine.rotation : Quaternion.identity;
            var floorY = chamberLine != null ? chamberLine.position.y : 0f;
            var forwardAxis = lineRotation * Vector3.forward;
            var rightAxis = lineRotation * Vector3.right;
            var exterior = new Bounds(chamber.transform.TransformPoint(chamber.ExteriorBounds.center), chamber.ExteriorBounds.size);
            var frontDepth = Vector3.Dot(exterior.min - Vector3.zero, forwardAxis);
            Vector3 World(Vector2 p) => Hall(new Vector3(p.x, 0f, p.y));
            Quaternion Yaw(float yaw) => lineRotation * Quaternion.Euler(0f, yaw, 0f);

            // The stations, from the chamber's exterior footprint (the chamber itself is never moved or resized here).
            var stations = StationLayout.Compute(layout.Exterior.x, layout.Exterior.z, hallRect, KioskAnchorLocal);
            Stations = stations;
            if (!stations.Valid)
                Debug.LogWarning("[BTP] Station layout: " + string.Join("; ", stations.Problems));

            // Plant view: in front of the tray window, a little right of the stem and always outside the chamber. A walk-in
            // tree fills the view: stand at its right side.
            var eyeRight = layout.Kind == ChamberKind.WalkIn ? layout.PlantBounds.extents.x + 0.25f : StationEyeRight;
            var eyeWorld = new Vector3(stem.x, floorY + Eye, stem.z) + rightAxis * eyeRight;
            var eyeDepth = Mathf.Min(Vector3.Dot(window, forwardAxis) - StationDistance, frontDepth - ScreenClearance);
            eyeWorld += forwardAxis * (eyeDepth - Vector3.Dot(eyeWorld, forwardAxis));
            StationDistanceUsed = Vector3.Dot(window - eyeWorld, forwardAxis);
            var toStem = stem - eyeWorld;
            toStem.y = 0f;
            if (plantAnchor != null)
                plantAnchor.SetPositionAndRotation(new Vector3(eyeWorld.x, floorY, eyeWorld.z),
                    Quaternion.LookRotation(toStem.sqrMagnitude > 1e-6f ? toStem.normalized : forwardAxis, Vector3.up));

            // Pitch of the plant view: keep the tray window's bottom in view and, when it fits, the canopy top.
            float Down(Vector3 p)
            {
                var flat = new Vector2(p.x - eyeWorld.x, p.z - eyeWorld.z).magnitude;
                return Mathf.Atan2(eyeWorld.y - p.y, Mathf.Max(flat, 0.1f)) * Mathf.Rad2Deg;
            }

            const float margin = 2.5f;
            var windowBottom = window - Vector3.up * (layout.TrayHeight * 0.5f + 0.02f);
            var plantTop = stem + Vector3.up * layout.PlantBounds.max.y;
            var low = Down(windowBottom) - (ViewHalfFov - margin);
            var high = (ViewHalfFov - margin) + Down(plantTop);
            ViewPitch = Mathf.Clamp(low <= high ? (low + high) * 0.5f : low, 4f, 40f);

            // Overview spawn (and the Interface root): everything at a glance.
            var spawnWorld = World(stations.Overview);
            var spawnRotation = Yaw(stations.OverviewYaw);
            if (spawnAnchor != null)
                spawnAnchor.transform.SetPositionAndRotation(spawnWorld, spawnRotation);
            if (interfaceRoot != null)
                interfaceRoot.SetPositionAndRotation(spawnWorld, spawnRotation);
            var focus = Vector3.zero;
            foreach (var s in stations.Stations)
                focus += World(s.Centre);
            focus += new Vector3(exterior.center.x, 0f, exterior.min.z) * 2f;
            focus /= 5f;
            var focusDistance = new Vector2(focus.x - spawnWorld.x, focus.z - spawnWorld.z).magnitude;
            SpawnPitch = Mathf.Clamp(Mathf.Atan2(Eye - 0.75f, Mathf.Max(focusDistance, 0.5f)) * Mathf.Rad2Deg, 3f, 20f);

            // Kiosk (the touchscreen on its pedestal), tilted back so it faces up towards the user at its anchor.
            if (panelRoot != null)
            {
                panelRoot.SetPositionAndRotation(World(stations.Kiosk.Centre), Yaw(stations.Kiosk.Yaw));
                if (panelCanvas != null && panelCanvas.parent == panelRoot)
                    panelCanvas.SetLocalPositionAndRotation(new Vector3(0f, PanelHeight, 0f), Quaternion.Euler(PanelTilt, 0f, 0f));
            }

            if (toolsBench != null)
                toolsBench.SetPositionAndRotation(World(stations.Tools.Centre), Yaw(stations.Tools.Yaw));
            if (water != null && water.Console != null)
                water.Console.SetPositionAndRotation(World(stations.Water.Centre), Yaw(stations.Water.Yaw));
            void Anchor(Transform anchor, StationLayout.Pose pose)
            {
                if (anchor != null)
                    anchor.SetPositionAndRotation(World(pose.Anchor), Yaw(pose.Yaw));
            }

            Anchor(kioskAnchor, stations.Kiosk);
            Anchor(waterAnchor, stations.Water);
            Anchor(toolsAnchor, stations.Tools);

            // The case sign is folded into the touchscreen header.
            if (signRoot != null)
            {
                signRoot.position = World(stations.Kiosk.Centre) + Vector3.up * 2f;
                signRoot.gameObject.SetActive(false);
            }

            PlaceSprayMarker(layout);
            PlaceTools(layout, eyeWorld);

            // The user's hinged door swings out on the left towards the WATER CONTROL console: its swing is cut short of every
            // station base and the hall walls (wide chambers open less than the full angle).
            if (chamber.Door != null)
            {
                Vector2[] Floor(IReadOnlyList<Vector2> line)
                {
                    var points = new Vector2[line.Count];
                    for (var i = 0; i < points.Length; i++)
                    {
                        var w = World(line[i]);
                        points[i] = new Vector2(w.x, w.z);
                    }

                    return points;
                }

                var bases = new List<Vector2[]>();
                foreach (var s in stations.Stations)
                    bases.Add(Floor(s.Footprint));
                chamber.Door.SetSurroundings(bases, Floor(new[]
                {
                    new Vector2(hallRect.xMin, hallRect.yMin), new Vector2(hallRect.xMax, hallRect.yMin),
                    new Vector2(hallRect.xMax, hallRect.yMax), new Vector2(hallRect.xMin, hallRect.yMax),
                }));
            }

            if (rootZoneLabel != null && rootZoneLabel.canvas != null)
            {
                var label = rootZoneLabel.canvas.transform;
                var position = window + rightAxis * (layout.TraySize.x * 0.5f + 0.09f) + Vector3.up * (layout.TrayHeight * 0.3f);
                var toward = position - eyeWorld;
                toward.y = 0f;
                label.SetPositionAndRotation(position, Quaternion.LookRotation(toward.sqrMagnitude > 1e-6f ? toward.normalized : forwardAxis, Vector3.up));
            }
        }

        /// <summary>The plant view's eye-to-tray-window distance chosen for the current specimen.</summary>
        public float StationDistanceUsed { get; private set; }

        public const float StationDistance = 0.95f, StationEyeRight = 0.25f, ScreenClearance = 0.8f;
        /// <summary>The kiosk screen tilts back this far from vertical (a lectern-like touchscreen).</summary>
        public const float PanelTilt = 45f;
        /// <summary>
        /// The kiosk anchor relative to the kiosk centre (local X right, local Z away): the head stands just in front of the
        /// screen's near edge and a little right, centred on the screen's buttons, so every poke target is within seated reach.
        /// </summary>
        public static readonly Vector2 KioskAnchorLocal = new Vector2(0.115f, -0.2f);
        /// <summary>Half the vertical field of view the station view is framed for (a 16:9 view about 62 degrees high).</summary>
        public const float ViewHalfFov = 31f;

        /// <summary>Edit mode (scene builder): fits the chamber to a default tomato-sized layout and places everything around it.</summary>
        internal void LayoutDefault()
        {
            Initialise();
            var layout = ChamberFit.Default();
            chamber.Apply(layout);
            chamber.transform.localPosition = new Vector3(-layout.Exterior.x * 0.5f, 0f, layout.Exterior.z * 0.5f);
            shown = null;
            PlaceAround(layout);
            if (sprayMarker != null)
                sprayMarker.gameObject.SetActive(false);
            if (fertiliserCard != null)
                fertiliserCard.SetActive(false);
            if (wand != null)
                wand.gameObject.SetActive(false);
        }

        void PlaceSprayMarker(ChamberLayout layout)
        {
            if (sprayMarker == null)
                return;
            var b = layout.PlantBounds;
            // The canopy front at mid-height of the measured bounds, facing the viewer.
            var local = new Vector3(b.center.x, b.center.y, b.min.z - 0.02f);
            var world = chamber.SpecimenAnchor.TransformPoint(local);
            sprayMarker.position = world;
            sprayMarker.rotation = Quaternion.identity;
            var radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.y) * 0.12f, 0.05f, 0.3f);
            sprayMarker.localScale = Vector3.one * (radius / 0.1f);
            // The marked foliage the sprayer's coverage counts on (always active, no renderer, not a physics blocker).
            if (sprayTarget == null)
            {
                var go = new GameObject("SprayTargetVolume");
                go.transform.SetParent(transform, false);
                var sphere = go.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sprayTarget = sphere;
            }

            sprayTarget.transform.position = world + Vector3.forward * 0.05f;
            ((SphereCollider)sprayTarget).radius = radius;
            SprayTool?.SetTarget(sprayTarget);
            // The mist wets the leaves of THIS specimen (its own surface points; the stripped specimen has no colliders).
            SprayTool?.SetFoliage(foliagePoints);
        }

        void PlaceTools(ChamberLayout layout, Vector3 eye)
        {
            var stem = chamber.SpecimenAnchor.position;
            var soilY = chamber.SoilWorldY;
            chamber.SoilArea(out var min, out var max);
            var forward = stem - eye;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var bandDirection3 = (-right * 0.95f - forward * 0.3f).normalized;
            var bandDirection = new Vector2(bandDirection3.x, bandDirection3.z);

            // The plant's base: how far its geometry spreads just above the soil.
            var baseRadius = 0.02f;
            foreach (var p in plantPoints)
            {
                if (p.y < soilY - 0.01f || p.y > soilY + 0.05f)
                    continue;
                baseRadius = Mathf.Max(baseRadius, new Vector2(p.x - stem.x, p.z - stem.z).magnitude);
            }

            var halfMin = Mathf.Min(max.x - min.x, max.y - min.y) * 0.5f;
            baseRadius = Mathf.Min(baseRadius, halfMin * 0.45f);
            var clearance = baseRadius + 0.015f;
            var inner = clearance + 0.01f;
            var outer = Mathf.Max(inner + 0.03f, Mathf.Min(halfMin * 0.92f, inner + 0.14f));
            chamber.GlassInnerArea(out var wallMin, out var wallMax);
            granuleArea = new GranuleSimulation.Area
            {
                SoilY = soilY, Min = min, Max = max, Stem = new Vector2(stem.x, stem.z), StemClearance = clearance, BandInner = inner,
                BandOuter = outer, BandDirection = bandDirection, HasWalls = true, WallMin = wallMin, WallMax = wallMax,
            };

            // Pour from the band's outer edge (left of the plant from the plant view).
            var pour = stem + bandDirection3 * outer;
            pour.x = Mathf.Clamp(pour.x, min.x + 0.02f, max.x - 0.02f);
            pour.z = Mathf.Clamp(pour.z, min.y + 0.02f, max.y - 0.02f);
            pour.y = soilY + 0.1f;
            pourPoint = pour;
            if (applicator != null)
                applicator.SetSoil(granuleArea, GranuleSimulation.SeedFor(shown?.SpecimenId ?? "chamber", applyCount + 1));
            PlaceDocks();

            // WATER outlet: a clear soil spot towards the console (left), the gooseneck on the left rim; the hose runs from the
            // console along the floor, in through the open front and up the tray's side to the rim clamp.
            var left = -right;
            var toward = (left * 0.9f - forward * 0.35f).normalized;
            ChooseWandContact(stem, soilY, min, max, inner, outer, toward, right);
            if (water != null)
            {
                var rim = new Vector3(min.x - TreatmentChamber.TrayWallThickness * 0.5f, soilY + 0.014f, Mathf.Clamp(wandContact.z, min.y + 0.03f, max.y - 0.03f));
                water.Fit(chamber, rim, wandContact, Vector3.left, HoseRoute(layout, rim));
            }

            if (wand != null)
                wand.PresentRootZone(wandContact, eye, NozzleHeight);
        }

        /// <summary>
        /// The hose's floor route (world points between the console's port and the rim clamp): along the floor to the
        /// chamber's front below the outlet side, over the sill on the chamber floor and back under the tray's left wall.
        /// </summary>
        List<Vector3> HoseRoute(ChamberLayout layout, Vector3 rim)
        {
            var route = new List<Vector3>();
            if (chamberLine == null)
                return route;
            var rimLine = chamberLine.InverseTransformPoint(rim);
            var x = rimLine.x - 0.05f;
            var inside = layout.FloorHeight + 0.02f;
            const float floor = 0.02f;
            route.Add(chamberLine.TransformPoint(new Vector3(x, floor, -0.3f)));
            route.Add(chamberLine.TransformPoint(new Vector3(x, floor, -0.08f)));
            route.Add(chamberLine.TransformPoint(new Vector3(x, inside, -0.08f)));
            route.Add(chamberLine.TransformPoint(new Vector3(x, inside, rimLine.z)));
            return route;
        }

        /// <summary>The packet and the sprayer stand in their docks on the TREATMENT TOOLS bench (the snap-back targets).</summary>
        void PlaceDocks()
        {
            if (applicator != null && packetShelf != null)
                // The grip point is the packet's top-left corner; the packet stands in its dock (0.48 m tall with its bands).
                applicator.Dock(packetShelf.position + Vector3.up * 0.49f, packetShelf.rotation);
            if (sprayerShelf == null)
                return;
            var tool = SprayTool;
            var pose = (sprayerShelf.position + Vector3.up * 0.012f, sprayerShelf.rotation * Quaternion.Euler(0f, -90f, 0f));
            if (tool != null)
                tool.Dock(pose.Item1, pose.Item2);
            else if (sprayer != null)
                sprayer.SetPositionAndRotation(pose.Item1, pose.Item2);
        }

        /// <summary>
        /// A soil spot beside the stem, towards the viewer's left, whose column up to the nozzle is clear of leaves, so the
        /// stream runs below the canopy and never through it.
        /// </summary>
        void ChooseWandContact(Vector3 stem, float soilY, Vector2 min, Vector2 max, float inner, float outer, Vector3 band, Vector3 right)
        {
            var best = stem + band * inner;
            var bestLowest = -1f;
            var clearHeight = NozzleHeight + 0.05f;
            foreach (var angle in new[] { 0f, -25f, 25f, -50f, 50f, -75f, 75f, -100f, 100f })
            {
                var direction = Quaternion.Euler(0f, angle, 0f) * band;
                for (var r = inner + 0.02f; r <= Mathf.Max(outer, inner + 0.03f) + 1e-4f; r += 0.02f)
                {
                    var candidate = stem + direction * r;
                    if (candidate.x < min.x + 0.02f || candidate.x > max.x - 0.02f || candidate.z < min.y + 0.02f || candidate.z > max.y - 0.02f)
                        continue;
                    var lowest = LowestPlantPointAbove(candidate, soilY, 0.04f);
                    if (lowest > bestLowest)
                    {
                        bestLowest = lowest;
                        best = candidate;
                    }

                    if (lowest >= clearHeight)
                        goto chosen;
                }
            }

            chosen:
            wandContact = new Vector3(best.x, soilY, best.z);
            lowestLeafAboveContact = LowestPlantPointAbove(wandContact, soilY, 0.04f);
        }

        /// <summary>Height above the soil of the lowest plant point within <paramref name="radius"/> of a column (+inf when none).</summary>
        public float LowestPlantPointAbove(Vector3 point, float soilY, float radius)
        {
            var lowest = float.PositiveInfinity;
            var r2 = radius * radius;
            foreach (var p in plantPoints)
            {
                var dx = p.x - point.x;
                var dz = p.z - point.z;
                if (dx * dx + dz * dz > r2 || p.y < soilY)
                    continue;
                lowest = Mathf.Min(lowest, p.y - soilY);
            }

            return lowest;
        }

        // ------------------------------------------------------------------ sign and cards

        void RefreshSign(TreatmentProfile profile)
        {
            if (shown == null)
                return;
            var plantName = profile?.Plant ?? PlantName(shown.PlantId);
            var diseaseName = profile?.Disease ?? DiseaseName(shown.PlantId, shown.DiseaseId);
            if (signTitle != null)
                signTitle.text = $"{plantName} / {diseaseName}";
            if (signScenario != null)
            {
                var line = TreatmentProfile.ScenarioLine(profile?.Scenario ?? scenario);
                signScenario.text = line ?? "";
                signScenario.gameObject.SetActive(line != null);
            }

            if (signSpecimen != null)
                signSpecimen.text = shown.FromFarm ? "Specimen " + shown.SpecimenId : "Crop model (no Farm specimen)";
            panel?.SetCase($"{plantName} / {diseaseName}", TreatmentProfile.ScenarioLine(profile?.Scenario ?? scenario),
                shown.FromFarm ? "Specimen " + shown.SpecimenId : "Crop model (no Farm specimen)");
            RefreshFertiliserCard(profile);
            if (sprayMarker != null)
            {
                var spray = profile?.Find(TreatmentProfile.Spray);
                var indicated = spray != null && spray.Indicated && specimen != null;
                sprayIndicated = indicated;
                sprayMarker.gameObject.SetActive(indicated && panel != null && panel.Page == 2);
                if (sprayMarkerLabel != null)
                    sprayMarkerLabel.text = indicated ? spray.Target : "";
            }
        }

        string PlantName(string plantId)
        {
            var catalog = plants != null ? plants : SelectionState.Instance != null ? SelectionState.Instance.Catalog : null;
            if (catalog != null && catalog.TryGet(plantId, out var plant))
                return plant.DisplayName;
            return string.IsNullOrEmpty(plantId) ? "" : char.ToUpperInvariant(plantId[0]) + plantId.Substring(1);
        }

        static string DiseaseName(string plantId, string diseaseId)
        {
            foreach (var info in DiseaseClasses.For(plantId))
                if (info.DiseaseId == diseaseId)
                    return info.DisplayName;
            return diseaseId;
        }

        void RefreshFertiliserCard(TreatmentProfile profile)
        {
            if (fertiliserCard == null)
                return;
            var factor = profile?.Find(TreatmentProfile.Fertiliser);
            var visible = factor != null && panel != null && panel.Page == 1;
            fertiliserCard.SetActive(visible);
            if (!visible || fertiliserCardBody == null)
                return;
            fertiliserCardBody.text = string.IsNullOrWhiteSpace(factor.Nutrient)
                ? "No supplement for this case.\n<size=70%>" + factor.Evidence + "</size>"
                : $"Soil test confirms low {factor.Nutrient.Trim().ToLowerInvariant()}\n<size=70%>Confirmed: {factor.Nutrient.Trim()}</size>";
        }

        void OnPage(int page)
        {
            RefreshFertiliserCard(panel != null ? panel.Profile : null);
            ShowToolFor(page);
        }

        /// <summary>The hand tool of the page: the packet on Fertiliser, the wand on Soil Moisture, none elsewhere.</summary>
        void ShowToolFor(int page)
        {
            if (applicator != null && applicator.Rig != null)
                applicator.Rig.gameObject.SetActive(applicator.HasPacket && !GuidedMode);
            if (wand != null)
                wand.gameObject.SetActive(false);
            var ready = session.Profile != null;
            if (water != null)
            {
                // The WATER CONTROL console is a station of its own and always stands; the hose and outlet connect it to
                // the tray while a profile is loaded (the chamber front is open then).
                water.gameObject.SetActive(true);
                water.SetConnected(ready);
                RefreshWaterConsole();
            }

            // The handheld sprayer only for spray-indicated cases, on the Spray page (monitoring cases keep the zero-spray path).
            if (sprayer != null)
                sprayer.gameObject.SetActive(ready && (sprayIndicated || GuidedMode));
            if (sprayMarker != null)
                sprayMarker.gameObject.SetActive(sprayIndicated && (page == 2 || (SprayTool != null && SprayTool.IsHeld)));

            // No glass or rail between the player and the plant while the hands-on stations are used.
            // Guided mode keeps the door: the player opens it for the fertiliser and the spray (glass closed for light and water).
            chamber.SetFrontOpen(ready && !GuidedMode);
            // The spray target ring is part of the Spray step.
            if (sprayMarker != null)
                sprayMarker.gameObject.SetActive(sprayIndicated && page == 2);
        }

        bool sprayIndicated;

        // ------------------------------------------------------------------ profile requests

        void RequestProfile(SelectionTicket ticket)
        {
            if (panel != null)
                panel.ShowLoading($"{PlantName(ticket.PlantId)} / {DiseaseName(ticket.PlantId, ticket.DiseaseId)}");
            if (shown != null && shown.Source == "none")
                Debug.Log($"[BTP] {ticket.PlantId} has no model; the chamber shows its message only.");
            LastProfilePath = TreatmentCaseClient.CasePath(ticket.PlantId, ticket.DiseaseId, ticket.Scenario);
            session.BeginLoad(ticket);
            panel?.SetConnection(ChamberControlPanel.Connection.Loading);
            Gate();
            if (!Application.isPlaying || client == null)
                return;
            LoadAsync(ticket);
        }

        async void LoadAsync(SelectionTicket ticket)
        {
            var result = await client.GetCase(ticket.PlantId, ticket.DiseaseId, ticket.Scenario);
            if (this == null)
                return;
            DeliverProfile(ticket, result);
        }

        /// <summary>
        /// A profile response for <paramref name="ticket"/>. A response for an earlier selection is ignored (returns false);
        /// 404 shows the observation message; other failures show Retry.
        /// </summary>
        public bool DeliverProfile(SelectionTicket ticket, TreatmentApiResult<TreatmentProfile> result)
        {
            if (!session.IsCurrent(ticket))
            {
                Debug.Log($"[BTP] Dropped a late treatment profile for {ticket} (now showing {session.Current}).");
                return false;
            }

            // The server answered (any HTTP status) or could not be reached (status 0): the header shows which.
            panel?.SetConnection(result != null && result.Status > 0 ? ChamberControlPanel.Connection.Connected : ChamberControlPanel.Connection.Offline);
            if (result == null || !result.Ok)
            {
                session.FailLoad(ticket);
                if (panel != null)
                {
                    if (result != null && result.NotFound)
                        panel.ShowObservation(PlantName(ticket.PlantId), DiseaseName(ticket.PlantId, ticket.DiseaseId));
                    else
                        panel.ShowUnavailable(result?.Error ?? "No response.");
                }

                RefreshSign(null);
                Gate();
                return true;
            }

            if (!session.AcceptProfile(ticket, result.Value))
            {
                session.FailLoad(ticket);
                panel?.ShowUnavailable($"The server returned {result.Value.CaseId} for {ticket.PlantId}/{ticket.DiseaseId}.");
                Gate();
                return true;
            }

            var profile = result.Value;
            // The kiosk starts every control at its minimum; the trainee dials each one to the chart target.
            var settings = TreatmentSettings.FromMinimum(profile);
            TreatmentSettings restored = null;
            string restoredNote = shown != null && !string.IsNullOrEmpty(shown.Note) ? shown.Note : null;
            var rootZone = profile.Find(TreatmentProfile.SoilMoisture, "rootZone");
            soilNow = soilTarget = rootZone != null ? rootZone.Start : 0d;
            applyCount = 0;
            if (applicator != null)
            {
                applicator.Configure(profile.Nutrient);
                applicator.SetSoil(granuleArea, GranuleSimulation.SeedFor(shown?.SpecimenId ?? profile.CaseId, applyCount + 1));
            }

            SprayTool?.ResetSpray();

            var record = shown != null && shown.FromFarm ? SpecimenSettingsStore.Load(shown.SpecimenId) : null;
            if (SpecimenSettingsStore.Matches(record, profile))
            {
                restored = TreatmentSettings.FromEvaluationJson(profile, record.values);
                if (restored != null)
                {
                    // Restored settings are history only (what was applied); the controls still start at their minimum.
                    session.RestoreApplied(restored);
                    applyCount = record.applyCount;
                    restoredNote = "Restored for this specimen: " + record.description;
                    if (applicator != null)
                        applicator.ShowSettled(record.granules, record.pours, GranuleSimulation.SeedFor(shown.SpecimenId, record.applyCount), granuleArea);
                    soilNow = soilTarget = SoilMoistureRules.MeasuredRootZone(rootZone != null ? rootZone.Start : 0d,
                        restored.Get(TreatmentProfile.SoilMoisture, "rootZone"), restored.GetChoice(TreatmentProfile.SoilMoisture, "irrigation"));
                }
            }

            watering = RootZoneWateringModel.For(profile);
            wet = 0f;
            if (watering != null && record != null && SpecimenSettingsStore.Matches(record, profile) && record.hasSoil)
            {
                watering.Restore(record.soilCurrent, record.soilSetpoint, record.soilAmount, record.soilDelivered, record.soilMoved);
                wet = record.soilWet;
                restoredNote = (restoredNote != null ? restoredNote + " " : "Restored for this specimen: ") +
                               $"soil {rootZone?.Format(watering.Current)}.";
            }

            panel?.ShowProfile(profile, settings, restored, restoredNote);
            panel?.SetWatering(watering);
            if (water != null)
            {
                water.ResetWater();
                water.SetWetness(wet, true);
                water.ShowDrainLabel(watering != null && watering.Amount < 0d);
                water.SetFlow(false, false);
            }

            panel?.SetMeasuredRootZone(soilNow);
            RefreshSign(profile);
            ShowToolFor(panel != null ? panel.Page : 0);
            RefreshSoilLabel();
            Gate();
            ProfileReady?.Invoke(profile);
            return true;
        }

        void OnRetry()
        {
            if (shown == null)
                return;
            var ticket = session.Begin(shown.SpecimenId, shown.PlantId, shown.DiseaseId, scenario);
            session.SpecimenShown(ticket);
            RequestProfile(ticket);
        }

        void OnScenario(string requested)
        {
            if (shown == null || string.IsNullOrEmpty(requested))
                return;
            scenario = requested;
            OnRetry();
        }

        /// <summary>Reloads the shown specimen's profile in another scenario ("severe").</summary>
        public SelectionTicket ChangeScenario(string requested)
        {
            OnScenario(requested);
            return session.Current;
        }

        void Gate()
        {
            if (panel == null)
                return;
            string busy = null;
            if (session.Loading)
                busy = "Loading";
            else if (session.Applying)
                busy = "Applying";
            else if (session.Checking)
                busy = "Checking health";
            panel.SetGate(session.CanApply, session.CanCheck, busy);
        }

        // ------------------------------------------------------------------ Apply

        void OnApply(TreatmentSettings settings) => Apply(settings);

        /// <summary>Apply: captures the settings for this specimen, saves them and plays the fertiliser and watering visuals.</summary>
        public bool Apply(TreatmentSettings settings) => ApplyOverride != null ? ApplyOverride(settings) : ApplyNow(settings);

        /// <summary>The kiosk's Apply itself (the guided treatment calls it once every step is done).</summary>
        public bool ApplyNow(TreatmentSettings settings)
        {
            var profile = session.Profile;
            if (profile == null || !session.Apply(settings))
                return false;
            var applied = session.Applied;
            applyCount++;
            var dose = applied.Get(TreatmentProfile.Fertiliser, "dose");
            var applications = applied.Get(TreatmentProfile.Fertiliser, "applications");
            var granules = 0;
            string note;
            if (applicator != null && applicator.HasPacket)
            {
                // Pours are the physical action; the amount and the number of applications are the panel's settings.
                granules = applicator.Simulation != null ? applicator.Simulation.Count : 0;
                note = SpecimenSettingsStore.Describe(dose, applications, granules, profile) +
                       $" Poured {applicator.PourCount} of {Mathf.RoundToInt((float)applications)} application(s).";
            }
            else
            {
                applicator?.ClearGranules();
                note = dose > 0d ? "No supplement packet for this case; the setting is still recorded." : null;
            }

            // Soil: the WATER station sets the moisture; Apply records it as it is now.
            if (watering != null)
                OverrideSoil(applied);

            if (shown != null && shown.FromFarm)
            {
                SpecimenSettingsStore.Save(new SpecimenSettingsRecord
                {
                    specimenId = shown.SpecimenId, plantId = profile.PlantId, diseaseId = profile.DiseaseId, scenario = profile.Scenario,
                    profileVersion = profile.ProfileVersion, values = applied.ToEvaluationJson(profile), granules = granules, dose = (float)dose,
                    applications = Mathf.RoundToInt((float)applications), applyCount = applyCount,
                    description = SpecimenSettingsStore.Describe(dose, applications, granules, profile),
                    pours = applicator != null ? applicator.PourCount : 0,
                });
                SaveSoil();
            }

            panel?.ShowApplied(applied, note);
            panel?.SetMeasuredRootZone(soilNow);
            RefreshSoilLabel();
            ShowToolFor(panel != null ? panel.Page : 0);
            Gate();
            // All four controls on the chart targets: the specimen recovers in the chamber. The kiosk faces away from the
            // chamber, so the recovery waits until the viewer looks at the plant (SpecimenRecovery.Queue).
            blending = false;
            recovery.Queue(amountNow);
            RefreshRecoveryStatus();
            return true;
        }

        Vector3 EyeWorld() => spawnAnchor != null ? spawnAnchor.transform.position + Vector3.up * Eye : chamber.transform.position + new Vector3(0f, Eye, -1.5f);

        Vector3 CanopyTop()
        {
            var b = shown != null ? shown.PlantBounds : new Bounds(Vector3.up * 0.5f, Vector3.one * 0.5f);
            return chamber.SpecimenAnchor.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
        }

        void OnSettingChanged(string factorId)
        {
            if (factorId == TreatmentProfile.SoilMoisture && panel != null)
                panel.SetMeasuredRootZone(soilNow);
        }

        void OnWandTrigger(bool pressed)
        {
            // A picked-up wand waters while its trigger is held: the soil approaches the applied (or set) value.
            if (!pressed || session.Profile == null)
                return;
            var settings = session.Applied ?? panel?.Settings;
            if (settings == null)
                return;
            var rootZone = session.Profile.Find(TreatmentProfile.SoilMoisture, "rootZone");
            soilTarget = SoilMoistureRules.MeasuredRootZone(rootZone != null ? rootZone.Start : 0d, settings.Get(TreatmentProfile.SoilMoisture, "rootZone"),
                settings.GetChoice(TreatmentProfile.SoilMoisture, "irrigation") ?? SoilMoistureRules.RootZone);
        }

        // ------------------------------------------------------------------ Check Health

        void OnCheck() => CheckHealth();

        /// <summary>Check Health: posts the applied settings; false when it is not allowed now.</summary>
        public SelectionTicket CheckHealth()
        {
            var ticket = session.BeginCheck();
            if (ticket == null)
                return null;
            LastEvaluatePath = TreatmentCaseClient.EvaluatePath(session.Profile.PlantId, session.Profile.DiseaseId, session.Profile.Scenario);
            Gate();
            if (Application.isPlaying && client != null)
                EvaluateAsync(ticket, session.Profile, EvaluationSettings());
            return ticket;
        }

        /// <summary>
        /// What Check Health sends: the applied temperature, fertiliser and spray settings with the soil moisture the WATER
        /// station actually reached (rounded to the step) and "rootZone" delivery once the outlet was used, else "hold".
        /// </summary>
        public TreatmentSettings EvaluationSettings()
        {
            var settings = (session.Applied ?? panel?.Settings)?.Clone();
            if (settings != null && watering != null)
                OverrideSoil(settings);
            return settings;
        }

        void OverrideSoil(TreatmentSettings settings)
        {
            var sub = session.Profile?.Find(TreatmentProfile.SoilMoisture, "rootZone");
            if (sub == null || watering == null)
                return;
            settings.Set(sub, TreatmentProfile.SoilMoisture, watering.MeasuredForServer);
            settings.SetChoice(TreatmentProfile.SoilMoisture, "irrigation", watering.Irrigation);
        }

        async void EvaluateAsync(SelectionTicket ticket, TreatmentProfile profile, TreatmentSettings applied)
        {
            var result = await client.Evaluate(profile, applied);
            if (this == null)
                return;
            DeliverEvaluation(ticket, result);
        }

        /// <summary>An evaluate response for <paramref name="ticket"/>; ignored (false) when the selection changed since.</summary>
        public bool DeliverEvaluation(SelectionTicket ticket, TreatmentApiResult<TreatmentEvaluation> result)
        {
            if (!session.AcceptEvaluation(ticket))
            {
                Debug.Log($"[BTP] Dropped a late treatment evaluation for {ticket} (now showing {session.Current}).");
                return false;
            }

            if (result != null && result.Conflict)
            {
                // The profile changed on the server: reload it and ask for Apply again.
                session.InvalidateProfile();
                OnRetry();
                panel?.ShowNote("The treatment profile was updated on the server and reloaded. Press Apply again.", true);
                return true;
            }

            if (result == null || !result.Ok)
            {
                panel?.ShowNote("Check Health failed: " + (result?.Error ?? "no response"), true);
                Gate();
                return true;
            }

            var evaluation = result.Value;
            panel?.ShowEvaluation(evaluation);
            if (!double.IsNaN(evaluation.RootZoneMoisture) && watering == null)
                soilNow = soilTarget = evaluation.RootZoneMoisture;
            RefreshSoilLabel();

            // The chamber copy shows the result; the Farm plant keeps it when this is the carried specimen.
            var start = shown != null ? shown.DiseaseAmount : 1f;
            var target = SpecimenTreatmentResults.ResultingDiseaseAmount(start, (float)evaluation.Score, evaluation.Passed);
            // Once Apply engaged the recovery it owns the chamber copy's look (it only ever heals); otherwise the result blends in.
            if (!recovery.Engaged)
                BlendDisease(target);
            if (shown != null && shown.FromFarm && !string.IsNullOrEmpty(shown.SpecimenId))
            {
                var saved = SpecimenTreatmentResults.Save(new SpecimenTreatmentRecord
                {
                    specimenId = shown.SpecimenId, plantId = shown.PlantId, diseaseId = shown.DiseaseId, score = (float)evaluation.Score,
                    passed = evaluation.Passed, outcome = evaluation.Outcome, outcomeLabel = evaluation.OutcomeLabel, startDiseaseAmount = start,
                    diseaseAmount = target,
                });
                panel?.ShowNote(saved ? $"Result saved to Farm specimen {shown.SpecimenId}." : "The result could not be saved (progress database closed).", !saved);
            }
            else
            {
                panel?.ShowNote("This result is not linked to a Farm plant (no specimen was carried).", false);
            }

            ReportToStory(evaluation, start);
            Gate();
            return true;
        }

        /// <summary>
        /// The after-treatment story (<see cref="TreatmentFlowPrompt"/>): a passed Check Health sends the user to the Lab's
        /// weekly changes, a failed one asks to try again. While the guided treatment runs, its completion card carries Go to
        /// Lab, so no second pop-up opens.
        /// </summary>
        void ReportToStory(TreatmentEvaluation evaluation, float start)
        {
            var plantId = shown != null ? shown.PlantId : session.Profile?.PlantId;
            var diseaseId = shown != null ? shown.DiseaseId : session.Profile?.DiseaseId;
            var guided = GetComponent<GuidedTreatmentController>();
            var popup = guided == null || !guided.isActiveAndEnabled;
            if (evaluation.Passed)
                TreatmentFlowPrompt.Applied(this, plantId, diseaseId, shown != null && shown.FromFarm ? shown.Specimen : null, start, 1f,
                    string.IsNullOrEmpty(evaluation.Outcome) ? "Passed" : evaluation.Outcome, popup);
            else if (popup)
                TreatmentFlowPrompt.Failed(this, $"Check Health: {evaluation.OutcomeLabel} (score {Mathf.RoundToInt((float)evaluation.Score * 100f)}%).");
        }

        /// <summary>
        /// Whether the viewer (the main camera: the headset in XR, the desktop camera otherwise) is looking at the specimen:
        /// its middle within <see cref="SpecimenRecovery.ViewAngle"/> of the gaze and not across the hall. True without a
        /// camera (edit-mode verification), so nothing waits there.
        /// </summary>
        bool ViewerSeesSpecimen()
        {
            if (!Application.isPlaying || specimen == null)
                return true;
            var viewer = Camera.main;
            if (viewer == null)
                return true;
            var height = shown != null ? shown.PlantBounds.size.y : 0.5f;
            var middle = specimen.transform.position + Vector3.up * (height * 0.5f);
            var eye = viewer.transform;
            var to = middle - eye.position;
            return to.sqrMagnitude < MaxRecoveryViewDistance * MaxRecoveryViewDistance && Vector3.Angle(eye.forward, to) <= SpecimenRecovery.ViewAngle;
        }

        const float MaxRecoveryViewDistance = 9f;
        /// <summary>The recovery state last sent to the kiosk: -2 none, -1 waiting, 0..100 percent, 101 recovered.</summary>
        int recoveryShown = -2;

        /// <summary>The kiosk's recovery line ("Recovering... 45%"), sent only when it changes (no per-frame strings).</summary>
        void RefreshRecoveryStatus()
        {
            var key = recovery.Done ? 101 : recovery.Active ? recovery.Percent : recovery.Waiting ? -1 : -2;
            if (key == recoveryShown)
                return;
            recoveryShown = key;
            if (panel == null)
                return;
            if (key == -2)
                panel.SetRecoveryStatus(null, false);
            else if (key == -1)
                panel.SetRecoveryStatus("Treatment applied - look at the plant in the chamber", false);
            else if (key == 101)
                panel.SetRecoveryStatus("Plant recovered", true);
            else
                panel.SetRecoveryStatus($"Recovering... {key}%", false);
        }

        void BlendDisease(float target)
        {
            amountFrom = amountNow;
            amountTo = target;
            blendTime = 0f;
            blending = !Mathf.Approximately(amountFrom, amountTo);
            if (!blending)
                SpecimenGeometry.SetDiseaseAmount(materials, amountTo);
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Disease blend, the watering sequence and the soil readout (Update; the verification calls it directly).</summary>
        public void Tick(float deltaTime)
        {
            TickWater(deltaTime);
            if (blending)
            {
                blendTime += deltaTime;
                var t = blendTime / FarmRecoveryMath.TransitionSeconds;
                amountNow = Mathf.Lerp(amountFrom, amountTo, FarmRecoveryMath.Ease(t));
                if (t >= 1f)
                {
                    amountNow = amountTo;
                    blending = false;
                }

                SpecimenGeometry.SetDiseaseAmount(materials, amountNow);
            }

            if (recovery.Waiting || recovery.Active)
            {
                recovery.Tick(deltaTime, !recovery.Waiting || ViewerSeesSpecimen());
                amountNow = recovery.Amount;
                RefreshRecoveryStatus();
            }

            if (waterQueued && (applicator == null || !applicator.IsAnimating))
            {
                if (wand != null && !wand.IsWatering)
                {
                    wand.gameObject.SetActive(true);
                    if (applicator != null && applicator.Rig != null)
                        applicator.Rig.gameObject.SetActive(false);
                    wand.StartWatering();
                }

                var step = WaterRate * deltaTime;
                soilNow = soilNow < soilTarget ? System.Math.Min(soilTarget, soilNow + step) : System.Math.Max(soilTarget, soilNow - step);
                panel?.SetMeasuredRootZone(soilNow);
                RefreshSoilLabel();
                if (System.Math.Abs(soilNow - soilTarget) < 1e-6)
                {
                    waterHold += deltaTime;
                    if (waterHold > 0.6f)
                    {
                        waterQueued = false;
                        wand?.Stop();
                        ShowToolFor(panel != null ? panel.Page : 0);
                    }
                }
            }
            else if (wand != null && wand.IsHeld && wand.IsWatering)
            {
                var step = WaterRate * deltaTime;
                soilNow = soilNow < soilTarget ? System.Math.Min(soilTarget, soilNow + step) : System.Math.Max(soilTarget, soilNow - step);
                panel?.SetMeasuredRootZone(soilNow);
                RefreshSoilLabel();
            }
        }

        /// <summary>Verification: advances the applicator, the wand and the station by <paramref name="seconds"/> in small steps.</summary>
        public void Advance(float seconds, float step = 1f / 60f)
        {
            for (var t = 0f; t < seconds - 1e-6f; t += step)
            {
                applicator?.Tick(step);
                wand?.Tick(step);
                water?.Tick(step);
                Tick(step);
            }
        }

        /// <summary>Verification: advances until the soil reading reaches <paramref name="value"/> (or the time runs out).</summary>
        public bool AdvanceUntilSoil(double value, float maxSeconds = 20f)
        {
            const float step = 1f / 60f;
            for (var t = 0f; t < maxSeconds; t += step)
            {
                if (SoilRootZoneNow >= value - 1e-6)
                    return true;
                applicator?.Tick(step);
                wand?.Tick(step);
                water?.Tick(step);
                Tick(step);
            }

            return SoilRootZoneNow >= value - 1e-6;
        }

        /// <summary>Verification: advances until the moisture is at or below <paramref name="value"/> (draining).</summary>
        public bool AdvanceUntilSoilBelow(double value, float maxSeconds = 30f)
        {
            const float step = 1f / 60f;
            for (var t = 0f; t < maxSeconds; t += step)
            {
                if (SoilRootZoneNow <= value + 1e-6)
                    return true;
                water?.Tick(step);
                Tick(step);
            }

            return SoilRootZoneNow <= value + 1e-6;
        }

        // ------------------------------------------------------------------ WATER station

        void OnAmountStep(int direction)
        {
            if (watering == null || !watering.StepAmount(direction))
                return;
            water?.ShowDrainLabel(watering.Amount < 0d);
            RefreshWater();
        }

        /// <summary>Sets "Water to add" directly (verification; snapped and clamped like the buttons).</summary>
        public bool SetWaterAmount(double amount)
        {
            if (watering == null || !watering.SetAmount(amount))
                return false;
            water?.ShowDrainLabel(watering.Amount < 0d);
            RefreshWater();
            return true;
        }

        void OnWaterOn()
        {
            if (watering == null || session.Profile == null)
                return;
            if (WaterOnGate != null && !WaterOnGate())
                return;
            watering.On();
            RefreshWater();
        }

        void OnWaterOff()
        {
            if (watering == null)
                return;
            watering.Off();
            RefreshWater();
            SaveSoil();
        }

        void RefreshWater()
        {
            if (water != null && watering != null)
            {
                // The stream's flow follows "Water to add": a small top-up trickles, a big one pours.
                var scale = System.Math.Abs(watering.Amount) / System.Math.Max(10d, (watering.Max - watering.Min) * 0.4d);
                water.SetFlowScale(Mathf.Clamp01(0.35f + 0.65f * (float)scale));
                water.SetFlow(watering.Watering, watering.Draining);
                water.SetWetness(wet);
            }

            panel?.Refresh();
            RefreshSoilLabel();
            RefreshWaterConsole();
        }

        /// <summary>The WATER CONTROL console's own readout: water to add, moisture now and setpoint, target, gauge needle.</summary>
        void RefreshWaterConsole()
        {
            if (water == null)
                return;
            var sub = session.Profile?.Find(TreatmentProfile.SoilMoisture, "rootZone");
            if (watering == null || sub == null)
            {
                water.ShowReadout("--", "Load a case on the kiosk", "", "", float.NaN, float.NaN, float.NaN, false, false);
                return;
            }

            string Format(double v) => sub.Format(v);
            var range = Mathf.Max(1e-3f, (float)(sub.Max - sub.Min));
            float Unit(double v) => Mathf.Clamp01((float)((v - sub.Min) / range));
            var running = watering.Running;
            water.ShowReadout(RootZoneWateringModel.AmountText(watering.Amount, Format),
                $"Now {Format(watering.Current)}  →  set {Format(watering.Setpoint)}",
                "Target " + TreatmentSubfactor.Compact(sub.TargetMin) + "–" + TreatmentSubfactor.Compact(sub.TargetMax) + (sub.Unit ?? ""),
                watering.StatusText(Format), Unit(watering.Current), Unit(sub.TargetMin), Unit(sub.TargetMax),
                !running && watering.Amount - watering.Step >= watering.MinAmount - 1e-9, !running && watering.Amount + watering.Step <= watering.MaxAmount + 1e-9);
        }

        void TickWater(float deltaTime)
        {
            if (watering == null || !watering.Running)
                return;
            var before = watering.Current;
            var reached = watering.Tick(deltaTime);
            var moved = (float)(watering.Current - before);
            // The soil darkens where the water lands; draining slowly lightens it.
            wet = Mathf.Clamp01(wet + (moved > 0f ? moved / 22f : moved / 45f));
            if (water != null)
            {
                water.SetWetness(wet);
                if (reached)
                    water.SetFlow(false, false);
            }

            soilDirty = true;
            panel?.Refresh();
            RefreshSoilLabel();
            RefreshWaterConsole();
            if (reached)
                SaveSoil();
        }

        /// <summary>Saves the specimen's soil state (current, setpoint, amount, delivery, wet patch) with its applied settings.</summary>
        void SaveSoil()
        {
            if (watering == null || shown == null || !shown.FromFarm || session.Profile == null)
                return;
            soilDirty = false;
            var profile = session.Profile;
            var record = SpecimenSettingsStore.Load(shown.SpecimenId);
            if (!SpecimenSettingsStore.Matches(record, profile))
                record = new SpecimenSettingsRecord
                {
                    specimenId = shown.SpecimenId, plantId = profile.PlantId, diseaseId = profile.DiseaseId, scenario = profile.Scenario,
                    profileVersion = profile.ProfileVersion,
                };
            record.hasSoil = true;
            record.soilCurrent = (float)watering.Current;
            record.soilSetpoint = (float)watering.Setpoint;
            record.soilAmount = (float)watering.Amount;
            record.soilDelivered = watering.Delivered;
            record.soilMoved = (float)watering.Moved;
            record.soilWet = wet;
            SpecimenSettingsStore.Save(record);
        }

        void RefreshSoilLabel()
        {
            if (rootZoneLabel == null)
                return;
            var profile = session.Profile;
            var rootZone = profile?.Find(TreatmentProfile.SoilMoisture, "rootZone");
            rootZoneLabel.text = rootZone != null ? "Root zone " + rootZone.Format(System.Math.Round(SoilRootZoneNow)) : "";
            if (!Application.isPlaying)
                rootZoneLabel.ForceMeshUpdate();
        }
    }
}
