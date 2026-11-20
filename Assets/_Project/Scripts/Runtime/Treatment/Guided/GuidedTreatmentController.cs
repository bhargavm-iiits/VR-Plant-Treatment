using System.Collections.Generic;
using BTP.Core;
using BTP.UI;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The guided, hands-on chamber treatment (added to the <see cref="ChamberStation"/> in Play mode; no scene change).
    /// For the loaded case it builds the four steps from the profile (<see cref="GuidedPlanner"/>) and runs them one at a
    /// time (<see cref="GuidedSequencer"/>): each step opens with ONE instruction card in the player's view (exact target,
    /// short instruction, large OK). OK only acknowledges and hands control back (its press is consumed:
    /// <see cref="GuidedInputGate"/>); the player then works the step by hand and a small chip counts the progress. Only a
    /// successful step advances (tick animation, then the next card); mistakes get a short toast, never the card again.
    /// 1 Sunlight: the kiosk's Temperature row + Apply (glass closed) → room lights fade, the grow lamp focuses warm light
    ///   on the plant, the leaves lift and the colour improves one step, lights return.
    /// 2 NPK: open the door, dip the grain scoop into the prescribed packet, pour it onto the soil round the roots; only
    ///   scoops whose granules settle in the root zone count; too many → "reset step".
    /// 3 Spray: open the door, grip the bottle, one trigger press = one spray, counted only when the mist cone reaches the
    ///   selected plant; feedback mist, wet sheen, chip tick, haptic; too many → "reset step".
    /// 4 Water: set the WATER CONTROL meter to the target and press ON with the glass closed → the soil spread animates.
    /// Then the specimen recovers (<see cref="SpecimenRecovery"/>), the result is saved against the Farm specimen
    /// (<see cref="SpecimenTreatmentResults"/>, <see cref="GuidedTreatmentResults"/>) and "Return to Farm" is offered.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class GuidedTreatmentController : MonoBehaviour
    {
        const float OkGuardSeconds = 0.3f, SprayRange = 0.9f, SprayCone = 12f, MinSprayShare = 0.25f;

        sealed class ScoopBatch
        {
            public GranuleSimulation Simulation;
            /// <summary>Instanced granules of this pour (null on the legacy mesh path).</summary>
            public GranulePour Pour;
            public GameObject Visual;
            public Mesh Mesh;
            public bool InChamber, DoorOpen, Countable, Closed, Judged;
            public float SettledFor;
        }

        ChamberStation station;
        TreatmentChamber chamber;
        ChamberControlPanel panel;
        GuidedTreatmentConfig config;
        GuidedProps props;
        GuidedHud hud;
        SunLampStation lamp;
        SunlightResponse sunlight;
        DesktopToolHand desktopHand;
        readonly List<FertiliserPacket> packets = new List<FertiliserPacket>();
        FertiliserScoop scoop;
        TrainingSprayTool sprayTool;
        TreatmentGrabbable sprayGrab;
        Material granuleMaterial;
        Transform granuleRoot;
        GranuleRenderer granuleRenderer;
        GameObject patch;
        Material patchMaterial;
        readonly List<ScoopBatch> batches = new List<ScoopBatch>();
        ScoopBatch pouring;

        readonly GuidedSequencer sequencer = new GuidedSequencer();
        TreatmentProfile profile;
        List<GuidedStepPlan> plans = new List<GuidedStepPlan>();
        DoseCounter scoops = new DoseCounter(1), sprays = new DoseCounter(1);
        float okTime = -10f;
        bool waterRunning;
        double dayApplied, nightApplied, waterAmountApplied, rootZoneAfter;
        float diseaseBefore = 1f;
        bool completed, saved;
        string savedWhere;
        int recoveryShown = -1;
        float sheen;
        readonly Dictionary<Material, float> sheenBase = new Dictionary<Material, float>();
        float floorY;
        bool built;

        public GuidedSequencer Sequencer => sequencer;
        public GuidedHud Hud => hud;
        public IReadOnlyList<GuidedStepPlan> Plans => plans;
        public DoseCounter Scoops => scoops;
        public DoseCounter Sprays => sprays;
        public SunLampStation Lamp => lamp;
        public SunlightResponse Sunlight => sunlight;
        public FertiliserScoop Scoop => scoop;
        public IReadOnlyList<FertiliserPacket> Packets => packets;
        public TreatmentGrabbable SprayGrab => sprayGrab;
        public TrainingSprayTool SprayTool => sprayTool;
        public DesktopToolHand DesktopHand => desktopHand;
        public bool Completed => completed;
        public bool Saved => saved;
        public string SavedWhere => savedWhere;
        public int PendingBatches => batches.FindAll(b => !b.Judged).Count;
        public float RootZoneRadius { get; private set; }
        public ChamberStation Station => station;
        /// <summary>The instanced granule renderer (null without the granule material).</summary>
        public GranuleRenderer Granules => granuleRenderer;

        void Start()
        {
            station = GetComponent<ChamberStation>();
            if (station == null || station.Chamber == null)
            {
                enabled = false;
                return;
            }

            chamber = station.Chamber;
            panel = station.Panel;
            config = GuidedTreatmentConfig.Load();
            station.GuidedMode = true;
            station.ApplyOverride = OnKioskApply;
            station.WaterOnGate = OnWaterOnRequested;
            if (panel != null)
                panel.GuidedApply = true;
            TrainingSprayTool.UseGate = GuidedInputGate.Allows;
            sequencer.InstructionShown += ShowInstruction;
            sequencer.AllCompleted += OnAllCompleted;
            station.ProfileReady += OnProfileReady;
            station.SpecimenDisplayed += OnSpecimenDisplayed;
            Build();
            if (station.Session.Profile != null)
                OnProfileReady(station.Session.Profile);
        }

        void OnDestroy()
        {
            if (station != null)
            {
                station.ProfileReady -= OnProfileReady;
                station.SpecimenDisplayed -= OnSpecimenDisplayed;
                station.ApplyOverride = null;
                station.WaterOnGate = null;
            }

            if (TrainingSprayTool.UseGate == (System.Func<string, bool>)GuidedInputGate.Allows)
                TrainingSprayTool.UseGate = null;
            GuidedInputGate.Gate.Clear();
            props?.Dispose();
        }

        // ------------------------------------------------------------------ building

        void Build()
        {
            if (built)
                return;
            built = true;
            floorY = station.ChamberLine != null ? station.ChamberLine.position.y : 0f;
            var top = station.ToolsBench != null ? station.ToolsBench.Find("Top") : null;
            var lit = top != null ? top.GetComponent<Renderer>()?.sharedMaterial : null;
            props = new GuidedProps(lit, chamber.AccentMaterial, chamber.GlassMaterial);
            var granules = station.Applicator != null ? station.Applicator.transform.Find("Granules") : null;
            granuleMaterial = granules != null ? granules.GetComponent<Renderer>()?.sharedMaterial : null;
            if (granuleMaterial == null)
                granuleMaterial = props.Lit("Granule", new Color(0.92f, 0.9f, 0.84f), 0.2f);

            hud = GuidedHud.Create(transform);
            lamp = SunLampStation.Build(transform, props);
            lamp.Pressed += OnLampPressed;
            sunlight = new GameObject("SunlightResponse").AddComponent<SunlightResponse>();
            sunlight.transform.SetParent(transform, false);
            sunlight.Bind(lamp, props);

            desktopHand = gameObject.AddComponent<DesktopToolHand>();
            desktopHand.SurfaceOverride = SoilUnderCursor;
            var benchRoot = new GameObject("GuidedTools").transform;
            benchRoot.SetParent(transform, false);
            if (config.granuleMaterial != null)
            {
                granuleRenderer = GranuleRenderer.Create(transform, config.granuleMaterial);
                granuleRenderer.LodDistance = config.granuleLodDistance;
                if (!granuleRenderer.Supported)
                {
                    Destroy(granuleRenderer.gameObject);
                    granuleRenderer = null;
                }
            }

            foreach (var product in config.products)
            {
                var packet = FertiliserPacket.Build(benchRoot, product, config, props, granuleMaterial, floorY, granuleRenderer);
                packets.Add(packet);
                desktopHand.Register(packet.Grabbable);
            }

            scoop = FertiliserScoop.Build(benchRoot, config, props, granuleMaterial, floorY, granuleRenderer);
            scoop.SetPackets(packets);
            scoop.FillRequested = OnFillRequested;
            scoop.PourStarted += OnPourStarted;
            scoop.Emit += OnPourEmit;
            scoop.PourEnded += () => { if (pouring != null) pouring.Closed = true; pouring = null; };
            desktopHand.Register(scoop.Grabbable);

            granuleRoot = new GameObject("GuidedGranules").transform;
            granuleRoot.SetParent(null, false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(granuleRoot.gameObject, gameObject.scene);
            patch = GuidedProps.Part(PrimitiveType.Cylinder, "FertilisedSoil", transform, Vector3.zero, Vector3.one, null);
            patchMaterial = props.Tint("FertilisedSoil", new Color(0.12f, 0.08f, 0.04f, 0f), 2990);
            patch.GetComponent<MeshRenderer>().sharedMaterial = patchMaterial;
            patch.SetActive(false);
            AttachBenchProps();
            Debug.Log("[BTP] Guided treatment ready: sunlight station, " + packets.Count + " fertiliser packets, grain scoop, spray bottle, " +
                      benchProps.Count + " bench props.");
        }

        readonly List<TreatmentGrabbable> benchProps = new List<TreatmentGrabbable>();

        /// <summary>
        /// The small props on the benches nearest the chamber (config paths) become grabbable too: pick up, inspect, put back.
        /// Props still statically batched (BTP/Treatment/Make Bench Props Grabbable not run) cannot move and are skipped.
        /// </summary>
        void AttachBenchProps()
        {
            var skipped = 0;
            foreach (var path in config.benchProps)
            {
                var parts = path.Split('/');
                foreach (var root in gameObject.scene.GetRootGameObjects())
                {
                    if (root.name != parts[0])
                        continue;
                    var level = new List<Transform> { root.transform };
                    for (var i = 1; i < parts.Length; i++)
                    {
                        var next = new List<Transform>();
                        foreach (var t in level)
                            foreach (Transform child in t)
                                if (child.name == parts[i])
                                    next.Add(child);
                        level = next;
                    }

                    foreach (var prop in level)
                    {
                        var batched = false;
                        foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
                            batched |= r.isPartOfStaticBatch;
                        if (batched || prop.gameObject.isStatic)
                        {
                            skipped++;
                            continue;
                        }

                        var item = TreatmentGrabbable.Attach(prop.gameObject, prop.name, null, floorY);
                        benchProps.Add(item);
                        desktopHand.Register(item);
                    }
                }
            }

            if (skipped > 0)
                Debug.LogWarning($"[BTP] {skipped} bench props are statically batched and stay fixed (run BTP/Treatment/Make Bench Props Grabbable).");
        }

        void HookSprayer()
        {
            var tool = station.SprayTool;
            if (tool == null || tool == sprayTool)
                return;
            sprayTool = tool;
            sprayTool.ExternalHome = true;
            sprayTool.BurstSeconds = config.sprayBurstSeconds;
            sprayTool.SprayPressed += OnSprayPressed;
            sprayGrab = TreatmentGrabbable.Attach(sprayTool.gameObject, "Spray bottle", null, floorY);
            sprayGrab.Grabbed += item => { if (item.HeldBy == GuidedInputGate.Mouse) sprayTool.Grab(null, GuidedInputGate.Key); };
            sprayGrab.Released += _ => sprayTool.Release();
            sprayGrab.Used += (item, source, down) => { if (source == GuidedInputGate.Key) sprayTool.SetPinch(down); };
            desktopHand.Register(sprayGrab, sprayTool.Nozzle);
        }

        /// <summary>Places the sunlight station beside the chamber and the packets and the scoop on the TREATMENT TOOLS bench.</summary>
        void Layout()
        {
            var lineRotation = station.ChamberLine != null ? station.ChamberLine.rotation : Quaternion.identity;
            var exterior = chamber.ExteriorBounds;
            var centre = chamber.transform.TransformPoint(exterior.center);
            var right = lineRotation * Vector3.right;
            // Beside the chamber's right wall, a little behind the plant, so the lamp leans over the glass without blocking the view.
            var lampPosition = centre + right * (exterior.extents.x + 0.46f) + lineRotation * Vector3.forward * (exterior.extents.z * 0.45f);
            lampPosition.y = floorY;
            if (station.KioskRoot != null && Vector3.Distance(Flat(lampPosition), Flat(station.KioskRoot.position)) < 1.1f)
                lampPosition += lineRotation * Vector3.forward * 0.6f;
            lamp.transform.SetPositionAndRotation(lampPosition, lineRotation);
            lamp.AimAt(PlantCentre());

            var bench = station.ToolsBench;
            if (bench == null)
                return;
            var topRenderer = bench.Find("Top") != null ? bench.Find("Top").GetComponent<Renderer>() : null;
            var topY = topRenderer != null ? topRenderer.bounds.max.y : bench.position.y + 0.82f;
            Vector3 OnBench(float along, float depth) => new Vector3(0f, topY - bench.position.y, 0f) + bench.position + bench.rotation * new Vector3(along, 0f, depth);
            // Side by side along the bench, a hand's width apart (the open sacks are wider than the old packets).
            var spacing = 0.2f;
            foreach (var packet in packets)
                spacing = Mathf.Max(spacing, packet.Width + 0.025f);
            for (var i = 0; i < packets.Count; i++)
                packets[i].Grabbable.SetHome(OnBench(-0.16f - spacing * i, 0.06f), bench.rotation * Quaternion.Euler(0f, -90f, 0f));
            scoop.Grabbable.SetHome(OnBench(0.05f, -0.1f) + Vector3.up * 0.022f, bench.rotation * Quaternion.Euler(0f, 180f, 0f));
            foreach (var item in AllTools())
                item.SetFloor(floorY);
            if (sprayGrab != null && station.SprayerDock != null)
                sprayGrab.SetHome(station.SprayerDock.position + Vector3.up * 0.012f, station.SprayerDock.rotation * Quaternion.Euler(0f, -90f, 0f));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        IEnumerable<TreatmentGrabbable> AllTools()
        {
            foreach (var packet in packets)
                yield return packet.Grabbable;
            if (scoop != null)
                yield return scoop.Grabbable;
            if (sprayGrab != null)
                yield return sprayGrab;
        }

        // ------------------------------------------------------------------ case lifecycle

        void OnSpecimenDisplayed()
        {
            sequencer.Stop();
            profile = null;
            completed = false;
            hud.HideCard();
            hud.SetChip(null, UIPalette.Cyan);
            hud.HideToast();
            GuidedInputGate.Gate.Modal = false;
            sunlight.Finish();
            ClearBatches();
            foreach (var item in AllTools())
                item.ReturnHome(true);
            scoop.Empty();
        }

        void OnProfileReady(TreatmentProfile loaded)
        {
            profile = loaded;
            HookSprayer();
            Layout();
            ClearBatches();
            foreach (var item in AllTools())
                item.ReturnHome(true);
            scoop.Empty();
            plans = GuidedPlanner.Build(profile, config);
            // A revisited specimen keeps its watered soil: aim the meter from the moisture it has now.
            var custom = config.OverrideFor(profile.CaseId);
            if (station.Watering != null && (custom == null || float.IsNaN(custom.waterAmount)))
                GuidedPlanner.RetargetWater(plans[(int)GuidedStepKind.Water], profile.Find(TreatmentProfile.SoilMoisture, "rootZone"), station.Watering.Current, config);
            scoops = new DoseCounter(plans[(int)GuidedStepKind.Fertiliser].DoseTarget);
            sprays = new DoseCounter(plans[(int)GuidedStepKind.Spray].DoseTarget);
            sprayTool?.ResetSpray();
            completed = false;
            saved = false;
            savedWhere = null;
            waterRunning = false;
            recoveryShown = -1;
            diseaseBefore = station.Current != null ? station.Current.DiseaseAmount : 1f;
            dayApplied = nightApplied = waterAmountApplied = rootZoneAfter = 0d;
            PlacePatch();
            CaptureSheen();
            lamp.SetStatus("Set the temperature on the kiosk, then Apply");
            Debug.Log($"[BTP] Guided treatment for {profile.CaseId} ({station.Current?.SpecimenId}): " +
                      string.Join(" | ", plans.ConvertAll(p => p.StepLabel + " " + p.TargetText)));
            sequencer.Start(plans);
        }

        void ShowInstruction(GuidedStepPlan plan)
        {
            GuidedInputGate.Gate.Modal = true;
            hud.SetChip(null, UIPalette.Cyan);
            hud.HideToast();
            hud.ShowCard(plan.StepLabel, plan.Title, plan.TargetText, plan.Instruction, new GuidedHud.CardButton("OK", OnOk));
            if (plan.Kind == GuidedStepKind.Sunlight)
                lamp.SetStatus("Target " + plan.TargetText);
        }

        /// <summary>OK on the instruction card: hides it and hands control back; the dismissing press is consumed.</summary>
        void OnOk(string source)
        {
            if (!sequencer.Acknowledge())
                return;
            var gate = GuidedInputGate.Gate;
            gate.ConsumeHeld();
            if (source != GuidedInputGate.Script)
                gate.Consume(source);
            gate.Modal = false;
            okTime = Time.unscaledTime;
            hud.HideCard();
            RefreshChip();
        }

        /// <summary>Verification: OK on the current card as <paramref name="source"/>.</summary>
        public bool PressOk(string source = GuidedInputGate.Script) => hud.Press(0, source);

        void RefreshChip()
        {
            var plan = sequencer.Step;
            if (plan == null || sequencer.Current != GuidedSequencer.Phase.Working)
            {
                hud.SetChip(null, UIPalette.Cyan);
                return;
            }

            switch (plan.Kind)
            {
                case GuidedStepKind.Sunlight:
                    hud.SetChip($"Temperature  {plan.TargetText}  ·  Apply on the kiosk", UIPalette.Warning);
                    break;
                case GuidedStepKind.Fertiliser:
                    hud.SetChip(scoops.Progress(plan.ProductLabel, "scoop"), scoops.Excess ? UIPalette.HealthBad : scoops.Count == scoops.Target ? UIPalette.HealthGood : UIPalette.Cyan);
                    break;
                case GuidedStepKind.Spray:
                    hud.SetChip(sprays.Progress("Spray", "spray"), sprays.Excess ? UIPalette.HealthBad : sprays.Count == sprays.Target ? UIPalette.HealthGood : UIPalette.Cyan);
                    break;
                case GuidedStepKind.Water:
                    hud.SetChip($"Water meter {plan.TargetText}  ·  glass closed  ·  ON", UIPalette.Cyan);
                    break;
            }
        }

        void Toast(string text, Color tint, string action = null, System.Action<string> onAction = null) =>
            hud.Toast(text, tint, action, onAction);

        void Warn(string text) => Toast(text, UIPalette.Warning);

        /// <summary>A short "not now" hint when a tool is used outside its step (silent while an instruction card is up).</summary>
        void NotNow(string what)
        {
            if (sequencer.InstructionVisible || sequencer.Current == GuidedSequencer.Phase.Responding)
                return;
            var step = sequencer.Step;
            if (completed || step == null)
                Toast("Treatment complete — press Return to Farm.", UIPalette.HealthGood);
            else
                Warn($"{what} is not this step. Now: {step.Title} — {step.TargetText}.");
        }

        bool DoorOpen => chamber.FrontOpen || (chamber.Door != null && (chamber.Door.UserOpen || chamber.Door.Moving)) || chamber.DoorAngle > 1f;

        /// <summary>A step succeeded: its response finishes with a tick on the card, then the next instruction.</summary>
        void Succeed(GuidedStepKind kind, string title, string line)
        {
            if (sequencer.Current == GuidedSequencer.Phase.Working)
                sequencer.BeginResponse(kind);
            hud.SetChip(null, UIPalette.Cyan);
            hud.HideToast();
            Debug.Log($"[BTP] Guided step done: {kind} ({line}).");
            hud.PlaySuccess(title, line, () => sequencer.CompleteStep(kind, line));
        }

        // ------------------------------------------------------------------ 1 sunlight and temperature

        void OnLampPressed()
        {
            if (sequencer.InstructionVisible || profile == null)
                return;
            var plan = plans.Count > 0 ? plans[0] : null;
            if (sequencer.CanWork(GuidedStepKind.Sunlight))
                Toast($"Target {plan.TargetText}: set it on the kiosk Temperature row, then press Apply.", UIPalette.Cyan);
            else if (sequencer.DoneCount > 0)
                Toast("Sunlight & temperature is done for this plant.", UIPalette.HealthGood);
            else
                NotNow("Sunlight");
        }

        bool OnKioskApply(TreatmentSettings settings)
        {
            if (profile == null || completed || sequencer.InstructionVisible || Time.unscaledTime - okTime < OkGuardSeconds)
                return false;
            if (!sequencer.CanWork(GuidedStepKind.Sunlight))
            {
                NotNow("Apply on the kiosk (temperature)");
                return false;
            }

            if (DoorOpen)
            {
                Warn("Close the glass door first — the light treatment runs with the glass closed.");
                return false;
            }

            var plan = sequencer.Step;
            var hasNight = profile.Find(TreatmentProfile.Temperature, "night") != null;
            var day = settings.Get(TreatmentProfile.Temperature, "day");
            var night = settings.Get(TreatmentProfile.Temperature, "night");
            if (!GuidedPlanner.ValidateTemperature(plan, day, night, hasNight, out var problem))
            {
                Warn(problem);
                panel?.ShowNote(problem, true);
                return false;
            }

            dayApplied = day;
            nightApplied = night;
            sequencer.BeginResponse(GuidedStepKind.Sunlight);
            hud.SetChip(null, UIPalette.Cyan);
            panel?.ShowNote("Temperature applied — watch the plant under the treatment light.", false);
            var line = hasNight
                ? $"Day {GuidedStepPlan.Number(day)}{plan.TemperatureUnit} · Night {GuidedStepPlan.Number(night)}{plan.TemperatureUnit}"
                : $"{GuidedStepPlan.Number(day)}{plan.TemperatureUnit}";
            var specimen = station.Specimen;
            sunlight.Play(specimen != null ? specimen.transform : null, PlantCentre(), config.sunlightSeconds,
                _ => station.BlendDiseaseTo(station.CurrentDiseaseAmount - config.sunlightColourStep),
                () => Succeed(GuidedStepKind.Sunlight, "Temperature set", line));
            lamp.SetStatus("Light treatment applied");
            return true;
        }

        // ------------------------------------------------------------------ 2 fertiliser

        bool OnFillRequested(FertiliserPacket packet)
        {
            if (sequencer.InstructionVisible)
                return false;
            if (!sequencer.CanWork(GuidedStepKind.Fertiliser))
            {
                NotNow("Fertiliser");
                return false;
            }

            var plan = sequencer.Step;
            if (packet.ProductId != plan.ProductId)
            {
                Warn($"Wrong packet: this plant needs {plan.ProductLabel}, not {packet.ProductLabel}.");
                return false;
            }

            return true;
        }

        void OnPourStarted(Vector3 lip)
        {
            var area = station.GranuleArea;
            var inChamber = lip.x >= area.WallMin.x && lip.x <= area.WallMax.x && lip.z >= area.WallMin.y && lip.z <= area.WallMax.y &&
                            lip.y > area.SoilY && lip.y < area.SoilY + 0.9f;
            var batch = new ScoopBatch
            {
                InChamber = inChamber, DoorOpen = DoorOpen, Countable = sequencer.CanWork(GuidedStepKind.Fertiliser),
            };
            if (inChamber)
            {
                batch.Simulation = new GranuleSimulation(GranuleSimulation.SeedFor(station.Current?.SpecimenId ?? "scoop", batches.Count + 1), area);
            }
            else
            {
                // Spilled: the granules fall onto whatever is below the lip (bench, floor) and are swept away after a while.
                var below = Physics.Raycast(lip, Vector3.down, out var hit, 3f, ~(1 << 2), QueryTriggerInteraction.Ignore) ? hit.point.y : floorY;
                batch.Simulation = new GranuleSimulation(batches.Count + 7, new GranuleSimulation.Area
                {
                    SoilY = below, Min = new Vector2(lip.x - 3f, lip.z - 3f), Max = new Vector2(lip.x + 3f, lip.z + 3f),
                    Stem = new Vector2(lip.x + 100f, lip.z + 100f), BandDirection = Vector2.left,
                });
            }

            var product = config.FindProduct(scoop.ProductId) ?? config.ProductForNutrient(null);
            batches.Add(batch);
            pouring = batch;
            if (granuleRenderer != null)
            {
                batch.Pour = new GranulePour(batch.Simulation, GranuleLook.For(product), (uint)(batches.Count * 7919 + 17), config.visualGranulesPerGranule);
                granuleRenderer.Register(batch.Pour);
                return;
            }

            batch.Visual = new GameObject("ScoopGranules", typeof(MeshFilter), typeof(MeshRenderer));
            batch.Visual.transform.SetParent(granuleRoot, false);
            batch.Mesh = props.Own(new Mesh { name = "ScoopGranules" });
            batch.Mesh.MarkDynamic();
            batch.Visual.GetComponent<MeshFilter>().sharedMesh = batch.Mesh;
            var renderer = batch.Visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = FertiliserPacket.GranuleTint(props, granuleMaterial, product);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnPourEmit(Vector3 lip, Vector3 velocity, int count)
        {
            if (pouring == null)
                return;
            var random = new System.Random(batches.Count * 131 + count);
            for (var i = 0; i < count; i++)
            {
                // A shaken scoop scatters its granules over the soil round the roots.
                var jitter = new Vector3(((float)random.NextDouble() - 0.5f) * 0.32f, -(float)random.NextDouble() * 0.06f, ((float)random.NextDouble() - 0.5f) * 0.32f);
                pouring.Simulation.Launch(lip + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 0.012f, velocity + jitter);
            }
        }

        void TickBatches(float deltaTime)
        {
            for (var i = batches.Count - 1; i >= 0; i--)
            {
                var batch = batches[i];
                if (!batch.Simulation.AllSettled || !batch.Closed)
                {
                    batch.Simulation.Step(deltaTime);
                    if (batch.Pour != null)
                        continue;
                    var points = new List<Vector3>(batch.Simulation.Count);
                    foreach (var g in batch.Simulation.Granules)
                        if (g.Released)
                            points.Add(g.Position);
                    GuidedProps.GranuleMesh(batch.Mesh, points, GranuleSimulation.Radius * 1.7f);
                    continue;
                }

                if (!batch.Judged)
                {
                    batch.Judged = true;
                    Judge(batch);
                }

                if (batch.InChamber)
                    continue;
                batch.SettledFor += deltaTime;
                if (batch.SettledFor > 4f)
                {
                    if (batch.Visual != null)
                        Destroy(batch.Visual);
                    if (batch.Pour != null)
                        granuleRenderer?.Unregister(batch.Pour);
                    batches.RemoveAt(i);
                }
            }
        }

        void Judge(ScoopBatch batch)
        {
            var area = station.GranuleArea;
            var points = new List<Vector3>();
            foreach (var g in batch.Simulation.Granules)
                points.Add(g.Position);
            var share = batch.InChamber ? GuidedGeometry.RootZoneShare(points, area.Stem, RootZoneRadius, area.Min, area.Max) : 0f;
            if (!batch.Countable)
            {
                Debug.Log($"[BTP] Scoop poured outside the fertiliser step (share {share:P0}); not counted.");
                return;
            }

            var hit = batch.InChamber && batch.DoorOpen && share >= config.scoopLandingShare;
            var result = scoops.Register(hit);
            Debug.Log($"[BTP] Scoop judged: inChamber={batch.InChamber} door={batch.DoorOpen} rootZone={share:P0} -> {result} ({scoops.Count}/{scoops.Target}).");
            switch (result)
            {
                case DoseCounter.Result.Missed:
                    if (!batch.InChamber)
                        Warn("Spilled outside the plant's tray — pour over the soil inside the chamber.");
                    else if (!batch.DoorOpen)
                        Warn("Open the glass door first.");
                    else
                        Warn($"Missed the roots ({share:P0} landed there) — pour onto the soil around the stem.");
                    break;
                case DoseCounter.Result.Counted:
                case DoseCounter.Result.Reached:
                    scoop.Grabbable.Haptic(0.35f, 0.08f);
                    DarkenPatch();
                    Toast($"Scoop {scoops.Count}/{scoops.Target} on the roots", UIPalette.HealthGood);
                    break;
                case DoseCounter.Result.Excess:
                    ShowScoopReset();
                    break;
                case DoseCounter.Result.Ignored when scoops.Excess:
                    ShowScoopReset();
                    break;
            }

            RefreshChip();
        }

        /// <summary>The excess warning with its "Reset step" action; it stays up until the step is reset.</summary>
        void ShowScoopReset() =>
            hud.Toast($"Too many scoops ({scoops.Count}/{scoops.Target}) — reset the step and add exactly {scoops.Target}.", UIPalette.HealthBad,
                "Reset step", _ => ResetFertiliserStep(), 0f);

        void ShowSprayReset() =>
            hud.Toast($"Too many sprays ({sprays.Count}/{sprays.Target}) — reset step to retry.", UIPalette.HealthBad, "Reset step", _ => ResetSprayStep(), 0f);

        /// <summary>"Reset step": no scoops counted, the poured granules and the soil tint cleared.</summary>
        public void ResetFertiliserStep()
        {
            scoops.Reset();
            ClearBatches();
            scoop.Empty();
            PlacePatch();
            RefreshChip();
            Toast($"Fertiliser step reset — add exactly {GuidedStepPlan.Plural(scoops.Target, "scoop")}.", UIPalette.Cyan);
        }

        void ClearBatches()
        {
            foreach (var batch in batches)
            {
                if (batch.Visual != null)
                    Destroy(batch.Visual);
                if (batch.Pour != null)
                    granuleRenderer?.Unregister(batch.Pour);
            }

            batches.Clear();
            // The sacks are full again for a new attempt or case.
            foreach (var packet in packets)
                if (packet != null)
                    packet.Refill();
            pouring = null;
        }

        void PlacePatch()
        {
            var area = station.GranuleArea;
            RootZoneRadius = Mathf.Max(area.StemClearance + config.rootZoneMargin, area.BandOuter + 0.01f);
            if (patch == null)
                return;
            patch.transform.position = new Vector3(area.Stem.x, area.SoilY + 0.0015f, area.Stem.y);
            patch.transform.localScale = new Vector3(RootZoneRadius * 2f, 0.0005f, RootZoneRadius * 2f);
            patchMaterial.SetColor("_BaseColor", new Color(0.12f, 0.08f, 0.04f, 0f));
            patch.SetActive(false);
        }

        void DarkenPatch()
        {
            patch.SetActive(true);
            var alpha = Mathf.Min(0.55f, 0.24f * scoops.Count);
            patchMaterial.SetColor("_BaseColor", new Color(0.12f, 0.08f, 0.04f, alpha));
        }

        /// <summary>The cursor ray over the tray soil (desktop): the point on the soil plane, else null.</summary>
        Vector3? SoilUnderCursor(Ray ray)
        {
            if (profile == null)
                return null;
            var area = station.GranuleArea;
            var height = area.SoilY + 0.1f;
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
                return null;
            var t = (height - ray.origin.y) / ray.direction.y;
            if (t <= 0f)
                return null;
            var p = ray.origin + ray.direction * t;
            return p.x >= area.WallMin.x && p.x <= area.WallMax.x && p.z >= area.WallMin.y && p.z <= area.WallMax.y ? p - Vector3.up * 0.06f : (Vector3?)null;
        }

        // ------------------------------------------------------------------ 3 spray

        void OnSprayPressed(string hand)
        {
            if (!sequencer.CanWork(GuidedStepKind.Spray))
            {
                NotNow("Spraying");
                return;
            }

            var nozzle = sprayTool.Nozzle;
            var share = nozzle != null ? GuidedGeometry.ConeHitShare(nozzle.position, nozzle.forward, SprayCone, SprayRange, PlantBounds()) : 0f;
            var open = DoorOpen;
            var result = sprays.Register(open && share >= MinSprayShare);
            Debug.Log($"[BTP] Spray press ({hand}): door={open} reach={share:P0} -> {result} ({sprays.Count}/{sprays.Target}).");
            switch (result)
            {
                case DoseCounter.Result.Missed:
                    Warn(open ? "Missed — aim the nozzle at the leaves of the selected plant." : "The mist hit the glass — open the door first.");
                    break;
                case DoseCounter.Result.Counted:
                case DoseCounter.Result.Reached:
                    sprayGrab?.Haptic(0.45f, 0.08f);
                    sheen = 1f;
                    break;
                case DoseCounter.Result.Excess:
                case DoseCounter.Result.Ignored when sprays.Excess:
                    ShowSprayReset();
                    break;
            }

            RefreshChip();
        }

        public void ResetSprayStep()
        {
            sprays.Reset();
            sheen = 0f;
            RefreshChip();
            Toast($"Spray step reset — apply exactly {GuidedStepPlan.Plural(sprays.Target, "spray")}.", UIPalette.Cyan);
        }

        void CaptureSheen()
        {
            sheenBase.Clear();
            foreach (var material in station.SpecimenMaterials)
                if (material != null && material.HasProperty("_Smoothness"))
                    sheenBase[material] = material.GetFloat("_Smoothness");
        }

        void TickSheen(float deltaTime)
        {
            if (sheen <= 0f && sheenBase.Count == 0)
                return;
            sheen = Mathf.Max(0f, sheen - deltaTime / 2.5f);
            // Wet leaves keep a little sheen once the spray step has started counting.
            var level = Mathf.Max(sheen, sprays.Count > 0 ? 0.25f : 0f);
            foreach (var pair in sheenBase)
                if (pair.Key != null)
                    pair.Key.SetFloat("_Smoothness", Mathf.Lerp(pair.Value, 0.85f, level));
        }

        // ------------------------------------------------------------------ 4 water

        bool OnWaterOnRequested()
        {
            if (profile == null)
                return true;
            if (completed)
            {
                Toast("Treatment complete — press Return to Farm.", UIPalette.HealthGood);
                return false;
            }

            if (sequencer.InstructionVisible)
                return false;
            if (!sequencer.CanWork(GuidedStepKind.Water))
            {
                NotNow("Watering");
                return false;
            }

            if (DoorOpen)
            {
                Warn("Close the glass door first, then press ON.");
                return false;
            }

            var watering = station.Watering;
            var plan = sequencer.Step;
            if (watering == null)
                return true;
            if (!GuidedPlanner.ValidateWater(plan, watering.Current, watering.Amount, out var problem))
            {
                Warn(problem);
                return false;
            }

            waterRunning = true;
            waterAmountApplied = watering.Amount;
            return true;
        }

        void TickWater()
        {
            if (!waterRunning)
                return;
            var watering = station.Watering;
            if (watering == null || watering.Running)
                return;
            waterRunning = false;
            if (!sequencer.CanWork(GuidedStepKind.Water))
                return;
            if (watering.InTarget)
            {
                rootZoneAfter = watering.Current;
                var plan = sequencer.Step;
                Succeed(GuidedStepKind.Water, "Soil moisture set",
                    $"Water {GuidedStepPlan.Signed(waterAmountApplied, plan.WaterUnit)} · root zone {GuidedStepPlan.Number(rootZoneAfter)}{plan.WaterUnit}");
            }
            else
            {
                Warn("The water stopped before the root zone reached its target — set the meter again and press ON.");
            }
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            var dt = Time.deltaTime;
            GuidedInputGate.ObserveDevices(desktopHand != null && desktopHand.UseDown);
            GuidedInputGate.Gate.Modal = sequencer.InstructionVisible;
            TickBatches(dt);
            TickSheen(dt);
            TickWater();
            if (sequencer.CanWork(GuidedStepKind.Fertiliser) && PendingBatches == 0 && !scoop.Pouring && scoops.Tick(dt))
            {
                var plan = sequencer.Step;
                Succeed(GuidedStepKind.Fertiliser, "Fertiliser added", $"{GuidedStepPlan.Plural(scoops.Count, "scoop")} of {plan.ProductLabel} on the roots");
            }

            if (sequencer.CanWork(GuidedStepKind.Spray) && sprays.Tick(dt))
                Succeed(GuidedStepKind.Spray, "Leaves sprayed", $"{GuidedStepPlan.Plural(sprays.Count, "spray")} on the leaves");
            if (completed)
                RefreshCompletion();
        }

        Bounds PlantBounds()
        {
            var specimen = station.Specimen;
            var bounds = new Bounds(PlantCentre(), Vector3.one * 0.2f);
            if (specimen == null)
                return bounds;
            var first = true;
            foreach (var r in specimen.GetComponentsInChildren<Renderer>())
            {
                if (first)
                    bounds = r.bounds;
                else
                    bounds.Encapsulate(r.bounds);
                first = false;
            }

            return bounds;
        }

        Vector3 PlantCentre()
        {
            var shown = station.Current;
            var b = shown != null ? shown.PlantBounds : new Bounds(Vector3.up * 0.3f, Vector3.one * 0.4f);
            return chamber.SpecimenAnchor.TransformPoint(b.center);
        }

        // ------------------------------------------------------------------ completion

        void OnAllCompleted()
        {
            completed = true;
            hud.SetChip(null, UIPalette.Cyan);
            var board = panel != null ? panel.Settings : null;
            var settings = GuidedPlanner.ComposeSettings(profile, board, plans, scoops.Count, sprays.Count, config);
            var day = profile.Find(TreatmentProfile.Temperature, "day");
            var night = profile.Find(TreatmentProfile.Temperature, "night");
            if (day != null)
                settings.Set(day, TreatmentProfile.Temperature, dayApplied);
            if (night != null)
                settings.Set(night, TreatmentProfile.Temperature, nightApplied);
            var applied = station.ApplyNow(settings);
            Debug.Log($"[BTP] Guided treatment complete for {station.Current?.SpecimenId}: kiosk apply {(applied ? "accepted" : "refused")}; recovery queued.");
            Save();
            ShowCompletion();
        }

        GuidedTreatmentRecord BuildRecord()
        {
            var shown = station.Current;
            var sun = plans[(int)GuidedStepKind.Sunlight];
            var water = plans[(int)GuidedStepKind.Water];
            return new GuidedTreatmentRecord
            {
                specimenId = shown?.SpecimenId, plantId = profile.PlantId, diseaseId = profile.DiseaseId, plantName = profile.Plant, diseaseName = profile.Disease,
                dayTemperature = (float)dayApplied, nightTemperature = (float)nightApplied,
                hasNight = profile.Find(TreatmentProfile.Temperature, "night") != null, temperatureUnit = sun.TemperatureUnit,
                product = plans[(int)GuidedStepKind.Fertiliser].ProductLabel, scoops = scoops.Count, sprays = sprays.Count,
                waterAmount = (float)waterAmountApplied, rootZoneAfter = (float)rootZoneAfter, waterUnit = water.WaterUnit, diseaseBefore = diseaseBefore,
            };
        }

        /// <summary>Saves the result against the Farm specimen (idempotent; done at completion and again before leaving).</summary>
        public bool Save()
        {
            var shown = station.Current;
            if (profile == null || !completed || shown == null || !shown.FromFarm || string.IsNullOrEmpty(shown.SpecimenId))
                return false;
            if (saved)
                return true;
            SpecimenTreatmentResults.Save(new SpecimenTreatmentRecord
            {
                specimenId = shown.SpecimenId, plantId = shown.PlantId, diseaseId = shown.DiseaseId, score = 1f, passed = true, outcome = profile.Outcome,
                outcomeLabel = string.IsNullOrEmpty(profile.OutcomeLabel) ? "Recovered" : profile.OutcomeLabel, startDiseaseAmount = diseaseBefore, diseaseAmount = 0f,
            });
            savedWhere = GuidedTreatmentResults.Save(BuildRecord());
            saved = true;
            Debug.Log($"[BTP] Guided treatment saved for Farm specimen {shown.SpecimenId} ({savedWhere}).");
            return true;
        }

        void ShowCompletion()
        {
            var shown = station.Current;
            var linked = shown != null && shown.FromFarm;
            var lines = string.Join("\n", BuildRecord().Lines());
            var note = linked ? "" : "\n<color=#F9BF24>No Farm plant was carried: this result is not saved to the Farm.</color>";
            hud.ShowCard("4/4", "Treatment complete", "Plant recovering…", lines + note,
                new GuidedHud.CardButton("Return to Farm", _ => ReturnToFarm()));
            recoveryShown = -1;
        }

        void RefreshCompletion()
        {
            if (!hud.CardVisible || hud.TickPlaying)
                return;
            var recovery = station.Recovery;
            var key = recovery.Done ? 101 : recovery.Active ? recovery.Percent : recovery.Waiting ? -2 : -3;
            if (key == recoveryShown)
                return;
            recoveryShown = key;
            var target = key == 101 ? "Plant recovered" : key >= 0 ? $"Recovering… {key}%" : key == -2 ? "Look at the plant" : "Plant recovering…";
            var shown = station.Current;
            var lines = string.Join("\n", BuildRecord().Lines());
            var note = shown != null && shown.FromFarm ? "" : "\n<color=#F9BF24>No Farm plant was carried: this result is not saved to the Farm.</color>";
            hud.ShowCard("4/4", "Treatment complete", target, lines + note, new GuidedHud.CardButton("Return to Farm", _ => ReturnToFarm()));
        }

        /// <summary>Return to Farm: the result is saved first, then the Farm loads.</summary>
        public void ReturnToFarm()
        {
            Save();
            foreach (var item in AllTools())
                item.ReturnHome(true);
            hud.HideCard();
            hud.SetChip(null, UIPalette.Cyan);
            GuidedInputGate.Gate.Clear();
            var environment = EnvironmentController.Instance;
            if (environment != null)
                environment.ReturnToFarm();
        }
    }
}
