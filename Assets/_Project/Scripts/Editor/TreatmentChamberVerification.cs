using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BTP.Core;
using BTP.Farm;
using BTP.Plants;
using BTP.Treatment;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// BTP/Treatment/5 - Verify Specimen Chamber. Opens the real Farm, captures real plants (tomato, corn, apple, rice, a
    /// small strawberry and potato plant, two tomatoes of one row and a tomato of another row) with
    /// <see cref="FarmSpecimenSnapshot.Capture"/>, then feeds each to the Treatment scene's <see cref="ChamberStation"/> in
    /// edit mode and checks Farm scale, measured size, chamber class, clearances, the hall, the panel and the route to it,
    /// the profile path, the fertiliser packet and granules, the watering wand, the race guard and the per-specimen
    /// progress. Renders a PNG per case from the spawn view and writes a text report. With the backend running
    /// (CropScanXR.config.json apiBaseUrl), the profiles and evaluations come from the live API; otherwise from the
    /// tomato fixture only. Batch: -executeMethod BTP.Editor.TreatmentChamberVerification.Run [-chamberOut dir]
    /// (never with -nographics: the PNGs are real renders). The scenes are not saved.
    /// </summary>
    public static class TreatmentChamberVerification
    {
        const float ScaleTolerance = 1e-3f;
        const string FixtureDir = "Assets/_Project/Tests/EditMode/Fixtures";

        sealed class Case
        {
            public string Name;
            public string PlantId;
            public string DiseaseId;
            public int Index;
            public ChamberKind Expected;
            public FarmSpecimenSnapshot.Snapshot Snapshot;
            public Vector3 FarmScale;
            public Vector3 FarmSize;
            public float FarmYaw;
            public float FarmAmount;
        }

        static readonly StringBuilder Report = new StringBuilder();
        static int failures;
        static int passes;
        static string outDir;
        static TreatmentCaseClient live;
        static bool liveUp;

        static void Line(string text)
        {
            Report.AppendLine(text);
            Debug.Log("[BTP][Verify] " + text);
        }

        static bool Check(bool ok, string what)
        {
            if (ok)
                passes++;
            else
                failures++;
            Line((ok ? "  PASS " : "  FAIL ") + what);
            return ok;
        }

        static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

        [MenuItem("BTP/Treatment/5 - Verify Specimen Chamber", priority = 44)]
        public static void RunFromMenu() => Run();

        public static void Run()
        {
            Report.Clear();
            failures = passes = 0;
            outDir = ArgValue("-chamberOut") ?? Path.Combine(Path.GetTempPath(), "BTP_ChamberVerification");
            Directory.CreateDirectory(outDir);
            Line($"Treatment chamber verification {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            var dbPath = Path.Combine(outDir, "verify_progress.db");
            if (File.Exists(dbPath))
                File.Delete(dbPath);
            var progress = new SqliteProgressRepository(dbPath);
            SpecimenTreatmentResults.StoreOverride = progress;
            try
            {
                Execute();
            }
            catch (Exception exception)
            {
                failures++;
                Line("EXCEPTION " + exception);
            }
            finally
            {
                SpecimenTreatmentResults.StoreOverride = null;
                progress.Dispose();
                Line($"RESULT: {passes} checks passed, {failures} failed.");
                File.WriteAllText(Path.Combine(outDir, "chamber_verification.txt"), Report.ToString());
                Debug.Log($"[BTP][Verify] Report written to {Path.Combine(outDir, "chamber_verification.txt")}");
            }
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        /// <summary>Station layout for every chamber size up to the reserved walk-in (no specimen needed: the pure layout).</summary>
        static void VerifyLayoutExtremes()
        {
            var hall = Rect.MinMaxRect(TreatmentChamberBuilder.HallLeft, TreatmentChamberBuilder.HallFront, TreatmentChamberBuilder.HallRight, TreatmentChamberBuilder.HallBack);
            var reserve = ChamberFit.ReservedExterior;
            foreach (var (label, w, d) in new[] { ("bench 0.7", 0.7f, 0.7f), ("tall 2.0", 2.0f, 2.0f), ("walk-in 4.2", 4.2f, 4.0f), ("reserve", reserve.x, reserve.z) })
            {
                var r = StationLayout.Compute(w, d, hall, ChamberStation.KioskAnchorLocal);
                Check(r.Valid, $"layout for a {label} m chamber: {(r.Valid ? "valid" : string.Join("; ", r.Problems))}; overview needs +-{F(StationLayout.OverviewHalfSpan(r))} deg");
            }
        }

        static void Execute()
        {
            AnchorLines.Clear();
            ReachTable.Clear();
            live = TreatmentCaseClient.FromConfiguration();
            var health = live.GetCaseBlocking("tomato", "early_blight", "standard");
            liveUp = health.Ok;
            Line($"Live API {live.BaseUrl}: {(liveUp ? "reachable" : "NOT reachable (" + health.Error + "), using fixtures")}");

            var farm = EditorSceneManager.OpenScene(ProjectPaths.FarmScene, OpenSceneMode.Single);
            var treatment = EditorSceneManager.OpenScene(ProjectPaths.TreatmentScene, OpenSceneMode.Additive);
            var host = new GameObject("VerificationSnapshots");
            SceneManager.MoveGameObjectToScene(host, treatment);

            var cases = new List<Case>
            {
                new Case { Name = "tomato_a", PlantId = "tomato", DiseaseId = "early_blight", Index = 0, Expected = ChamberKind.Tall },
                new Case { Name = "tomato_b", PlantId = "tomato", DiseaseId = "early_blight", Index = 2, Expected = ChamberKind.Tall },
                new Case { Name = "tomato_late_blight", PlantId = "tomato", DiseaseId = "late_blight", Index = 1, Expected = ChamberKind.Tall },
                new Case { Name = "corn", PlantId = "corn", DiseaseId = "common_rust", Index = 0, Expected = ChamberKind.Tall },
                new Case { Name = "apple", PlantId = "apple", DiseaseId = "apple_scab", Index = 0, Expected = ChamberKind.WalkIn },
                new Case { Name = "strawberry", PlantId = "strawberry", DiseaseId = "leaf_scorch", Index = 0, Expected = ChamberKind.Bench },
                new Case { Name = "potato", PlantId = "potato", DiseaseId = "late_blight", Index = 0, Expected = ChamberKind.Bench },
                new Case { Name = "rice", PlantId = "rice", DiseaseId = "brown_spot", Index = 0, Expected = ChamberKind.Tall },
                new Case { Name = "wheat", PlantId = "wheat", DiseaseId = "rust", Index = 0, Expected = ChamberKind.Tall },
            };

            var plantings = new List<SelectablePlant>();
            foreach (var root in farm.GetRootGameObjects())
                plantings.AddRange(root.GetComponentsInChildren<SelectablePlant>(true));
            Line($"Farm: {plantings.Count} plantings in {farm.name}.");
            foreach (var c in cases)
            {
                var planting = plantings.FirstOrDefault(p => p.PlantId == c.PlantId && p.DiseaseId == c.DiseaseId);
                if (!Check(planting != null, $"Farm row {c.PlantId}/{c.DiseaseId} exists"))
                    continue;
                var plant = planting.PlantAt(c.Index);
                var model = SpecimenGeometry.ModelOf(plant);
                if (!Check(model != null, $"{c.Name}: Farm plant #{c.Index + 1} has a model"))
                    continue;
                c.FarmScale = model.transform.lossyScale;
                c.FarmYaw = model.transform.eulerAngles.y;
                c.FarmSize = SpecimenGeometry.Measure(model, null).size;
                c.FarmAmount = SpecimenGeometry.DiseaseAmount(model);
                var snapshotHost = new GameObject("Snapshot_" + c.Name);
                snapshotHost.transform.SetParent(host.transform, false);
                c.Snapshot = snapshotHost.AddComponent<FarmSpecimenSnapshot>().Capture(planting, c.Index);
                Check(c.Snapshot != null && c.Snapshot.Template != null, $"{c.Name}: captured {c.Snapshot?.Specimen?.SpecimenId}");
            }

            MeasureReserve(treatment);

            EditorSceneManager.CloseScene(farm, true);
            SceneManager.SetActiveScene(treatment);
            var station = Object.FindObjectsByType<ChamberStation>(FindObjectsInactive.Include).FirstOrDefault();
            if (!Check(station != null, "Treatment scene has a ChamberStation"))
                return;
            station.Client = live;
            station.Initialise();

            var shown = new Dictionary<string, Case>();
            foreach (var c in cases.Where(c => c.Snapshot != null))
            {
                VerifyBaseCase(station, c);
                shown[c.Name] = c;
            }

            VerifySameCrop(station, shown);
            VerifyFertiliser(station, shown);
            VerifyRaceAndProgress(station, shown);
            VerifyWaterStation(station, shown);
            VerifyTouchscreen(station, shown);
            VerifyHands(station, shown);
            VerifyLiveApi();
            VerifyLayoutExtremes();
            File.WriteAllLines(Path.Combine(outDir, "chamber_anchor_after.txt"), AnchorLines);
            Line("Seated reach table (case | station | targets | farthest | m):");
            foreach (var row in ReachTable)
                Line("  " + row);
            VerifyBootstrapHands();
        }

        // ------------------------------------------------------------------ reserve

        static void MeasureReserve(Scene scene)
        {
            var variants = AssetDatabase.LoadAssetAtPath<DiseaseVariantCatalog>(ProjectPaths.VariantCatalog);
            var space = new GameObject("ReserveSpace");
            SceneManager.MoveGameObjectToScene(space, scene);
            var largest = Vector3.zero;
            var largestName = "";
            foreach (var entry in variants.Entries)
            {
                if (entry.farmPrefab == null)
                    continue;
                var instance = (GameObject)Object.Instantiate(entry.farmPrefab, space.transform, false);
                // Largest Farm scale (FarmDiseaseRows): trees 0.92-1.02, pergola modules 1, everything else 0.92-1.06.
                var tree = entry.plantId is "apple" or "cherry" or "peach" or "mango" or "orange";
                var maxScale = tree ? 1.02f : entry.plantId == "grape" ? 1f : 1.06f;
                for (var yaw = 0f; yaw < 180f; yaw += 15f)
                {
                    instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
                    instance.transform.localScale = Vector3.one * maxScale;
                    var layout = ChamberFit.Fit(SpecimenGeometry.Measure(instance, space.transform));
                    var e = layout.Exterior;
                    if (e.x > largest.x || e.y > largest.y || e.z > largest.z)
                        largestName = $"{entry.plantId}/{entry.diseaseId} yaw {yaw}";
                    largest = Vector3.Max(largest, e);
                }

                Object.DestroyImmediate(instance);
            }

            Object.DestroyImmediate(space);
            var reserve = ChamberFit.ReservedExterior;
            Line($"Largest exterior over {variants.Entries.Count} Farm variants at max Farm scale, yaw 0-165: {V(largest)} ({largestName}); reserved {V(reserve)}");
            Check(largest.x <= reserve.x && largest.y <= reserve.y && largest.z <= reserve.z, "ReservedExterior covers every Farm plant");
        }

        // ------------------------------------------------------------------ helpers

        static TreatmentApiResult<TreatmentProfile> Profile(string plantId, string diseaseId, string scenario = "standard")
        {
            if (liveUp)
                return live.GetCaseBlocking(plantId, diseaseId, scenario);
            var fixture = Path.Combine(FixtureDir, $"treatment_profile_{plantId}_{diseaseId}.json");
            if (!File.Exists(fixture))
                return new TreatmentApiResult<TreatmentProfile> { Status = 0, Error = "API offline and no fixture." };
            return new TreatmentApiResult<TreatmentProfile> { Status = 200, Value = TreatmentProfile.Parse(File.ReadAllText(fixture), out _) };
        }

        static SelectionTicket ShowCase(ChamberStation station, Case c, string scenario = null)
        {
            var ticket = station.Show(c.PlantId, c.DiseaseId, c.Snapshot.Specimen, c.Snapshot, scenario);
            station.DeliverProfile(ticket, Profile(c.PlantId, c.DiseaseId, ticket.Scenario));
            return ticket;
        }

        static void VerifyBaseCase(ChamberStation station, Case c)
        {
            Line($"Case {c.Name}: {c.PlantId}/{c.DiseaseId} plant #{c.Index + 1}");
            var ticket = ShowCase(station, c);
            var chamber = station.Chamber;
            var layout = chamber.Layout;
            var specimen = station.Specimen;
            var scale = specimen.transform.lossyScale;
            var size = SpecimenGeometry.Measure(specimen, null).size;
            Line($"  specimen {station.Current.SpecimenId}; Farm scale {V(c.FarmScale)} yaw {F(c.FarmYaw)} disease {F(c.FarmAmount)}");
            Line($"  chamber {layout.Kind}: interior {V(layout.Interior)} exterior {V(layout.Exterior)} floor {F(layout.FloorHeight)} soil {F(layout.SoilSurface)} " +
                 $"tray {F(layout.TraySize.x)} x {F(layout.TraySize.y)} x {F(layout.TrayHeight)}");
            Check((scale - c.FarmScale).magnitude < ScaleTolerance, $"clone world scale {V(scale)} == Farm scale");
            Check((size - c.FarmSize).magnitude < 2e-3f, $"measured size {V(size)} == Farm LOD0 size {V(c.FarmSize)}");
            Check(Mathf.Abs(Mathf.DeltaAngle(specimen.transform.eulerAngles.y, c.FarmYaw)) < 0.05f, "clone yaw == Farm yaw");
            Check(Mathf.Abs(station.CurrentDiseaseAmount - c.FarmAmount) < 1e-4f, $"disease amount {F(station.CurrentDiseaseAmount)} == Farm");
            Check(layout.Kind == c.Expected, $"chamber kind {layout.Kind} (expected {c.Expected})");

            // Side and top clearance; below, the plant stands on the soil (checked separately).
            var box = layout.InteriorBounds;
            box.min -= new Vector3(0f, layout.SideClearance, 0f);
            var inside = SpecimenGeometry.AllInside(specimen, chamber.transform, box, layout.SideClearance, out var gap, out var points);
            Check(inside, $"all {points} LOD0 points inside the interior with >= {F(layout.SideClearance)} m side clearance (worst gap {F(gap)} m)");
            var lowest = SpecimenGeometry.Measure(specimen, chamber.transform).min.y;
            Check(lowest >= layout.FloorHeight + layout.PedestalHeight - 1e-3f, $"lowest point {F(lowest)} m is above the tray bottom (in the soil or above it)");
            var plantTop = SpecimenGeometry.Measure(specimen, chamber.transform).max.y;
            var topGap = layout.FloorHeight + layout.Interior.y - plantTop;
            Check(topGap >= layout.TopClearance - 1e-3f, $"clearance above canopy {F(topGap)} m >= {F(layout.TopClearance)}");
            var stemY = specimen.transform.position.y;
            Check(Mathf.Abs(stemY - chamber.SoilWorldY) < 1e-4f, $"stem base on the soil surface (y {F(stemY)})");
            var reserve = ChamberFit.ReservedExterior;
            Check(layout.Exterior.x <= reserve.x && layout.Exterior.y <= reserve.y && layout.Exterior.z <= reserve.z, "exterior <= ReservedExterior");
            var leftGap = -layout.Exterior.x - TreatmentChamberBuilder.HallLeft;
            var backGap = TreatmentChamberBuilder.HallBack - layout.Exterior.z;
            var roofGap = TreatmentChamberBuilder.HallHeight - layout.Exterior.y;
            Check(leftGap >= TreatmentChamberBuilder.Clearance - 1e-3f && backGap >= TreatmentChamberBuilder.Clearance - 1e-3f && roofGap >= 1f,
                $"inside the hall: left {F(leftGap)} m, back {F(backGap)} m, ceiling {F(roofGap)} m");

            var line = station.ChamberLine;
            var panel = station.PanelCanvas.position;
            var panelLocal = line.InverseTransformPoint(panel);
            Check(!new Bounds(chamber.transform.TransformPoint(chamber.ExteriorBounds.center), chamber.ExteriorBounds.size).Contains(panel),
                $"kiosk screen outside the chamber (line-space {V(panelLocal)})");
            Check(Mathf.Abs(panel.y - ChamberStation.PanelHeight) < 1e-3f, $"kiosk screen centre height {F(panel.y)} m");
            var eye = station.AnchorOf(StationLayout.Station.Kiosk).position + Vector3.up * ChamberStation.Eye;
            var flat = new Vector2(panel.x - eye.x, panel.z - eye.z).magnitude;
            Line($"  kiosk anchor to screen centre {F(flat)} m (horizontal), eye-to-screen {F((panel - eye).magnitude)} m");
            Physics.SyncTransforms();
            var blocked = Physics.Raycast(eye, (panel - eye).normalized, out var hitInfo, (panel - eye).magnitude - 0.02f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Check(!blocked, $"nothing solid between the kiosk anchor eye and the screen{(blocked ? " - hit " + hitInfo.collider.name : "")}");
            VerifyStations(station, c.Name);
            var spec = c.Name switch
            {
                "tomato_a" => "tomato/early_blight/0", "wheat" => "wheat/rust/0", "strawberry" => "strawberry/leaf_scorch/0", "apple" => "apple/apple_scab/0", _ => null,
            };
            if (spec != null)
                ChamberAnchorDump(station, spec);
            ViewChecks(station);
            Check(station.LastProfilePath == TreatmentCaseClient.CasePath(c.PlantId, c.DiseaseId, "standard") &&
                  station.LastProfilePath.StartsWith($"/api/treatment/cases/{c.PlantId}/{c.DiseaseId}"), $"profile path {station.LastProfilePath}");
            Check(station.Panel.Current == ChamberControlPanel.State.Ready || station.Panel.Current == ChamberControlPanel.State.Observation,
                $"panel state {station.Panel.Current} ({station.Panel.MessageText})");
            Check(station.SignTitleText.Length > 0 && station.SignSpecimenText.Contains(c.Snapshot.Specimen.SpecimenId), $"sign '{station.SignTitleText}' / '{station.SignSpecimenText}'");
            Check(station.Session.IsCurrent(ticket), "ticket current");
            Render(station, c.Name);
            Render(station, c.Name + "_overview", overview: true);
        }

        static void VerifySameCrop(ChamberStation station, Dictionary<string, Case> shown)
        {
            if (!shown.TryGetValue("tomato_a", out var a) || !shown.TryGetValue("tomato_b", out var b))
                return;
            Line("Two specimens of the same crop (tomato/early_blight #1 and #3)");
            Check(a.Snapshot.Specimen.SpecimenId != b.Snapshot.Specimen.SpecimenId, $"different specimen IDs {a.Snapshot.Specimen.SpecimenId} / {b.Snapshot.Specimen.SpecimenId}");
            Check((a.FarmScale - b.FarmScale).magnitude > 1e-3f || Mathf.Abs(Mathf.DeltaAngle(a.FarmYaw, b.FarmYaw)) > 1f, "they differ in Farm scale or yaw");
            foreach (var c in new[] { a, b, a })
            {
                ShowCase(station, c);
                var s = station.Specimen.transform;
                Check(station.Current.SpecimenId == c.Snapshot.Specimen.SpecimenId && (s.lossyScale - c.FarmScale).magnitude < ScaleTolerance &&
                      Mathf.Abs(Mathf.DeltaAngle(s.eulerAngles.y, c.FarmYaw)) < 0.05f && Mathf.Abs(station.CurrentDiseaseAmount - c.FarmAmount) < 1e-4f &&
                      station.LastProfilePath.StartsWith("/api/treatment/cases/tomato/early_blight"),
                    $"switch to {c.Snapshot.Specimen.SpecimenId}: scale {V(s.lossyScale)} yaw {F(s.eulerAngles.y)} amount {F(station.CurrentDiseaseAmount)} path {station.LastProfilePath}");
            }
        }

        // ------------------------------------------------------------------ fertiliser

        static void VerifyFertiliser(ChamberStation station, Dictionary<string, Case> shown)
        {
            if (shown.TryGetValue("rice", out var rice))
            {
                Line("Fertiliser: Rice / Brown spot");
                ShowCase(station, rice);
                var panel = station.Panel;
                panel.ShowPage(1);
                Check(station.Applicator.HasPacket && station.Applicator.Label == "POTASSIUM", $"packet label '{station.Applicator.Label}'");
                Check(panel.PageInfoText.Contains("Confirmed: Potassium") && panel.PageInfoText.Contains("soil test"), $"panel info '{Flat(panel.PageInfoText)}'");
                Check(station.Applicator.Rig.gameObject.activeSelf && !station.Applicator.IsHeld, "packet standing in its dock (no hand model)");
                panel.SetValue("fertiliser", "dose", 3);
                panel.SetValue("fertiliser", "applications", 2);
                Check(panel.ApplyInteractable, "Apply enabled (specimen and profile match)");
                Check(!panel.CheckInteractable, "Check Health disabled before Apply");
                // Two real pours (Addendum 5: tilt-to-pour while held), one with each hand, then Apply.
                PourOnce(station, 1.2f, "right", "rice_fertiliser_pouring");
                PourOnce(station, 1.2f, "left", null);
                station.Apply(panel.Settings);
                Check(panel.CheckInteractable, "Check Health enabled after Apply");
                station.Advance(3.5f);
                var sim = station.Applicator.Simulation;
                Check(sim != null && sim.Count >= 2 * FertiliserApplicator.MinPourGranules && sim.AllSettled && station.Applicator.PourCount == 2,
                    $"{sim?.Count} granules poured in {station.Applicator.PourCount} pours and settled");
                CheckGranules(station, sim);
                var before = sim.Granules.Select(g => g.Position).ToList();
                station.Advance(5f);
                var moved = sim.Granules.Select((g, i) => (g.Position - before[i]).magnitude).Max();
                Check(moved < 1e-6f && station.Applicator.VisibleGranules == sim.Count, $"granules remain after 5 more simulated seconds (max move {moved})");
                Check(station.Specimen != null && Mathf.Abs(station.CurrentDiseaseAmount - rice.FarmAmount) < 1e-4f, "Apply did not change the disease amount");
                Render(station, "rice_fertiliser_settled");
                if (liveUp)
                {
                    var profile = station.Session.Profile;
                    foreach (var (dose, apps, expect) in new[] { (3d, 2d, 1d), (4d, 2d, 1d), (0d, 0d, -1d) })
                    {
                        var s = TreatmentSettings.FromStart(profile);
                        s.Set(profile.Find("fertiliser", "dose"), "fertiliser", dose);
                        s.Set(profile.Find("fertiliser", "applications"), "fertiliser", apps);
                        var r = live.EvaluateBlocking(profile, s);
                        var score = r.Ok ? r.Value.FactorScore("fertiliser") : double.NaN;
                        Check(r.Ok && (expect < 0 ? score < 1d : Math.Abs(score - expect) < 1e-6),
                            $"live evaluate rice {dose}/{apps}: fertiliser score {F(score)} ({(expect < 0 ? "below target" : "on target")}) {r.Error}");
                    }

                    var check = station.CheckHealth();
                    var result = live.EvaluateBlocking(station.Session.Profile, station.Session.Applied);
                    station.DeliverEvaluation(check, result);
                    Line($"  Check Health via the station: {panel.ResultText}");
                }
            }

            if (shown.TryGetValue("tomato_a", out var tomato))
            {
                Line("Fertiliser: Tomato / Early blight");
                ShowCase(station, tomato);
                var panel = station.Panel;
                panel.ShowPage(1);
                Check(!station.Applicator.HasPacket && !station.Applicator.Rig.gameObject.activeSelf, "no packet for a case without a deficiency");
                Check(panel.PageInfoText.Contains("No supplement for this case."), $"panel info '{Flat(panel.PageInfoText)}'");
                var granules = 0;
                if (station.Apply(panel.Settings))
                    granules = station.Applicator.VisibleGranules + (station.Applicator.Simulation?.Count ?? 0);
                station.Advance(3f);
                Check(granules == 0 && station.Applicator.Simulation == null, "Apply with 0/0 spawns zero granules");
                if (liveUp)
                {
                    var r = live.EvaluateBlocking(station.Session.Profile, station.Session.Applied);
                    Check(r.Ok && Math.Abs(r.Value.FactorScore("fertiliser") - 1d) < 1e-6, $"live evaluate tomato 0/0: fertiliser score {F(r.Ok ? r.Value.FactorScore("fertiliser") : double.NaN)}");
                }

                panel.SetValue("fertiliser", "dose", 2);
                panel.SetValue("fertiliser", "applications", 1);
                station.Apply(panel.Settings);
                Check(station.Applicator.Simulation == null && panel.StatusLineText.Contains("No supplement packet for this case"),
                    $"dose > 0 without a packet: note shown, no granules ('{Flat(panel.StatusLineText)}')");
                panel.SetValue("fertiliser", "dose", 0);
                panel.SetValue("fertiliser", "applications", 0);
                station.Apply(panel.Settings);
                Render(station, "tomato_fertiliser");
            }
        }

        static string Flat(string text) => (text ?? "").Replace("\n", " | ");

        static void CheckGranules(ChamberStation station, GranuleSimulation sim)
        {
            if (sim == null)
                return;
            var area = station.GranuleArea;
            var onSoil = 0;
            var inTray = 0;
            var besideStem = 0;
            var maxDistance = 0f;
            foreach (var g in sim.Granules)
            {
                if (Mathf.Abs(g.Position.y - (area.SoilY + GranuleSimulation.Radius)) < 1e-3f)
                    onSoil++;
                if (g.Position.x >= area.Min.x && g.Position.x <= area.Max.x && g.Position.z >= area.Min.y && g.Position.z <= area.Max.y)
                    inTray++;
                var d = new Vector2(g.Position.x - area.Stem.x, g.Position.z - area.Stem.y).magnitude;
                maxDistance = Mathf.Max(maxDistance, d);
                if (d >= area.StemClearance - 1e-4f)
                    besideStem++;
            }

            Check(onSoil == sim.Count, $"{onSoil}/{sim.Count} granules on the soil surface (y {F(area.SoilY)})");
            Check(inTray == sim.Count, $"{inTray}/{sim.Count} granules inside the tray");
            Check(besideStem == sim.Count, $"{besideStem}/{sim.Count} granules clear of the stem (>= {F(area.StemClearance)} m; farthest {F(maxDistance)} m)");
        }

        // ------------------------------------------------------------------ race guard and progress

        static void VerifyRaceAndProgress(ChamberStation station, Dictionary<string, Case> shown)
        {
            if (!shown.TryGetValue("rice", out var rice) || !shown.TryGetValue("tomato_a", out var a) || !shown.TryGetValue("tomato_b", out var b))
                return;
            Line("Direct switch rice -> tomato #1 -> tomato #3, late responses, per-specimen progress");
            var riceTicket = station.Show(rice.PlantId, rice.DiseaseId, rice.Snapshot.Specimen, rice.Snapshot);
            var riceProfile = Profile(rice.PlantId, rice.DiseaseId);
            var tomatoTicket = ShowCase(station, a);
            Check(!station.DeliverProfile(riceTicket, riceProfile), "a late rice profile after switching to tomato is ignored");
            Check(station.Session.Profile != null && station.Session.Profile.PlantId == "tomato" && station.Panel.Profile.PlantId == "tomato" &&
                  station.SignTitleText.StartsWith("Tomato") && !station.Applicator.HasPacket, $"panel, sign and packet stay on tomato ('{station.SignTitleText}')");

            // tomato #1: apply non-start settings (saved for this specimen only).
            var panel = station.Panel;
            panel.SetValue("temperature", "day", 25);
            panel.SetValue("soilMoisture", "rootZone", 60);
            panel.SetChoice("soilMoisture", "irrigation", "rootZone");
            station.Apply(panel.Settings);
            station.Advance(4f);
            var check = station.CheckHealth();
            ShowCase(station, b);
            var stale = new TreatmentApiResult<TreatmentEvaluation>
            {
                Status = 200,
                Value = TreatmentEvaluation.Parse(File.ReadAllText(Path.Combine(FixtureDir, "treatment_evaluation_tomato_early_blight.json")), out _),
            };
            Check(check != null && !station.DeliverEvaluation(check, stale), "a late evaluation for tomato #1 is ignored after switching to tomato #3");
            var startDay = station.Session.Profile.Find("temperature", "day").Start;
            Check(Math.Abs(panel.Settings.Get("temperature", "day") - startDay) < 1e-9 && station.Session.Applied == null,
                $"tomato #3 starts from its own (empty) record: day {F(panel.Settings.Get("temperature", "day"))}");
            Check(station.Current.SpecimenId == b.Snapshot.Specimen.SpecimenId, "tomato #3 shows its own specimen");
            Render(station, "switch_tomato_b");
            ShowCase(station, a);
            // (The soil delivery now comes from the WATER station: untouched soil is recorded as "hold".)
            Check(Math.Abs(panel.Settings.Get("temperature", "day") - 25d) < 1e-9 && station.Session.Applied != null &&
                  station.Watering != null && Math.Abs(station.Watering.Current - 81d) < 1e-9, "returning to tomato #1 restores its applied settings and soil");
            Check(panel.StatusLineText.Contains("Restored for this specimen"), $"restored note: '{Flat(panel.StatusLineText)}'");
            var record = SpecimenSettingsStore.Load(a.Snapshot.Specimen.SpecimenId);
            Check(record != null && SpecimenSettingsStore.Load(b.Snapshot.Specimen.SpecimenId) == null, $"saved under {SpecimenSettingsStore.Key(a.Snapshot.Specimen.SpecimenId)} only");
            ShowCase(station, rice);
            Check(station.Applicator.HasPacket && station.SignTitleText.StartsWith("Rice"), "back to rice: packet and sign refreshed together");
            var riceRecord = SpecimenSettingsStore.Load(rice.Snapshot.Specimen.SpecimenId);
            Check(riceRecord != null && riceRecord.granules > 0 && riceRecord.pours == 2 && station.Applicator.VisibleGranules == riceRecord.granules,
                $"rice restores its settled granules ({station.Applicator.VisibleGranules})");
            var result = SpecimenTreatmentResults.Load(rice.Snapshot.Specimen.SpecimenId);
            if (liveUp)
                Check(result != null, $"rice Check Health result saved: {(result != null ? SpecimenTreatmentResults.Describe(result) : "none")}");
        }

        // ------------------------------------------------------------------ soil moisture

        // ------------------------------------------------------------------ station view

        /// <summary>The plant view (Addendum 6: the plant chamber's anchor in front of the tray window; the close-up renders).</summary>
        static Camera ViewCamera(ChamberStation station, int width = RenderWidth, int height = RenderHeight) =>
            PoseCamera(station.PlantAnchor.position + Vector3.up * ChamberStation.Eye, Quaternion.Euler(station.ViewPitch, station.PlantAnchor.eulerAngles.y, 0f), width, height);

        /// <summary>The overview spawn (all stations at a glance).</summary>
        static Camera OverviewCamera(ChamberStation station, int width = RenderWidth, int height = RenderHeight)
        {
            var spawn = station.SpawnPoint.transform;
            return PoseCamera(spawn.position + Vector3.up * ChamberStation.Eye, Quaternion.Euler(station.SpawnPitch, spawn.eulerAngles.y, 0f), width, height);
        }

        static Camera PoseCamera(Vector3 position, Quaternion rotation, int width, int height)
        {
            var go = new GameObject("VerificationCamera", typeof(Camera));
            var camera = go.GetComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = FieldOfView;
            camera.aspect = width / (float)height;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 600f;
            return camera;
        }

        static bool InView(Camera camera, RectTransform rect, out string where)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in corners)
            {
                var p = camera.WorldToViewportPoint(corner);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
                if (p.z <= 0f)
                {
                    where = "behind the eye";
                    return false;
                }
            }

            where = $"viewport x {F(min.x)}-{F(max.x)}, y {F(min.y)}-{F(max.y)}";
            return min.x >= 0f && max.x <= 1f && min.y >= 0f && max.y <= 1f;
        }

        /// <summary>First-person station framing: panel and sign fully inside the view, the stem and the tray window visible, the front open.</summary>
        static void ViewChecks(ChamberStation station)
        {
            var camera = ViewCamera(station);
            try
            {
                var panel = (RectTransform)station.PanelCanvas;
                InView(camera, panel, out var panelWhere);
                Line($"  touchscreen in the spawn view: {panelWhere} (within reach; turn the head to it)");
                var stem = camera.WorldToViewportPoint(station.Chamber.SpecimenAnchor.position);
                var window = camera.WorldToViewportPoint(station.Chamber.WindowCentre);
                // A walk-in tree is framed from its right side (the touchscreen must not hide it), so its stem sits further left.
                var minX = station.Chamber.Layout.Kind == ChamberKind.WalkIn ? 0.05f : 0.2f;
                Check(stem.x > minX && stem.x < 0.65f && window.y > 0.02f && window.y < 0.6f, $"stem at viewport x {F(stem.x)}, tray window at y {F(window.y)}");
                var eyeToTray = (station.Chamber.WindowCentre - camera.transform.position);
                eyeToTray.y = 0f;
                Line($"  eye-to-tray-window {F(eyeToTray.magnitude)} m (horizontal); pitch {F(station.ViewPitch)} deg");
                if (station.Session.Profile != null)
                    Check(station.Chamber.FrontOpen, "chamber front (door glass and hatch rail) retracted: nothing between the player and the plant");
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        // ------------------------------------------------------------------ WATER station (Addendum 3)

        static string Pct(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "%";

        static void VerifyWaterStation(ChamberStation station, Dictionary<string, Case> shown)
        {
            if (!shown.TryGetValue("rice", out var rice))
                return;
            Line("WATER station: Rice / Brown spot, severe scenario");
            var ticket = station.Show(rice.PlantId, rice.DiseaseId, rice.Snapshot.Specimen, rice.Snapshot, "severe");
            station.DeliverProfile(ticket, Profile(rice.PlantId, rice.DiseaseId, "severe"));
            var panel = station.Panel;
            var model = station.Watering;
            var water = station.Water;
            if (!Check(model != null && water != null && panel.Current == ChamberControlPanel.State.Ready, "severe profile loaded with a WATER station"))
                return;
            Check(Math.Abs(model.Start - 52d) < 1e-9 && Math.Abs(model.TargetMin - 64d) < 1e-9 && Math.Abs(model.TargetMax - 80d) < 1e-9 && Math.Abs(model.Step - 1d) < 1e-9,
                $"moisture from the profile: start {Pct(model.Start)}, band {model.TargetMin}-{model.TargetMax}, step {model.Step}");
            var renderers = station.Specimen.GetComponentsInChildren<Renderer>(true);
            var materialsBefore = renderers.SelectMany(r => r.sharedMaterials).ToList();
            var amountsBefore = materialsBefore.Select(m => m != null && m.HasProperty("_DiseaseAmount") ? m.GetFloat("_DiseaseAmount") : -1f).ToList();

            panel.ShowPage(3);
            Check(water.Hand == null && !Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Any(t => t.name is "Palm" or "Glove" or "Sleeve" or "Hand"),
                "no hand, glove or sleeve visuals in the lab (tracked hands drive input only)");
            // [+] twenty times, as the player does (the panel's own button event).
            for (var i = 0; i < 20; i++)
                station.SetWaterAmount(model.Amount + model.Step);
            Check(Math.Abs(model.Amount - 20d) < 1e-9 && Math.Abs(model.Setpoint - 72d) < 1e-9, $"water to add {model.Amount}: setpoint {Pct(model.Setpoint)} shown before ON");
            water.PressOn();
            Check(model.Running && water.Flowing && water.OnGlowing, "ON: water runs, the stream is on and ON glows cyan");
            station.AdvanceUntilSoil(62d);
            Check(Math.Abs(model.Current - 62d) < 0.1d, $"moisture rising: {F(model.Current)}%");
            var texts = panel.VisiblePageTexts();
            var expected = new List<string>
            {
                "Soil Moisture", "Water to add", "20%", "Current moisture", "62%", "Setpoint", "72%", "Target range", "64\u201380%", "Watering", "Leaf wetness 22/100",
            };
            Check(texts.SequenceEqual(expected), $"panel copy: [{string.Join(" | ", texts)}]");
            Check(station.SignTitleText == "Rice / Brown spot" && station.SignScenarioText == "Severe scenario", $"sign '{station.SignTitleText}' / '{station.SignScenarioText}'");
            var area = station.GranuleArea;
            var contact = water.Contact;
            var stemDistance = new Vector2(contact.x - area.Stem.x, contact.z - area.Stem.y).magnitude;
            Check(Mathf.Abs(contact.y - area.SoilY) < 1e-4f && contact.x > area.Min.x && contact.x < area.Max.x && contact.z > area.Min.y && contact.z < area.Max.y,
                "stream lands on the soil inside the tray");
            Check(stemDistance >= area.StemClearance, $"stream lands beside the stems ({F(stemDistance)} m from the stem)");
            var outletHeight = water.OutletTip.y - area.SoilY;
            var lowest = station.LowestPlantPointAbove(contact, area.SoilY, 0.04f);
            Check(lowest > outletHeight + 0.02f, $"no leaves above the stream: outlet {F(outletHeight)} m, lowest plant point in the column {F(lowest)} m " +
                                                 $"({station.PlantPoints.Count} measured points)");
            Check(water.Wetness > 0f, $"soil darkening where the water lands (wetness {F(water.Wetness)})");
            water.HoldOnCap(true);
            Render(station, "treat4_water_on_62");
            Render(station, "treat4_water_on_62_1600", 1600, 900);
            water.HoldOnCap(false);

            water.PressOff();
            var paused = model.Current;
            Check(!model.Running && !water.Flowing && !water.OnGlowing, "OFF stops at once: stream off, ON dark");
            station.Advance(2f);
            Check(Math.Abs(model.Current - paused) < 1e-9 && panel.PageStatusText == "Paused at 62%", $"paused: {F(model.Current)}%, '{panel.PageStatusText}'");
            water.PressOn();
            Check(model.Running && Math.Abs(model.Setpoint - 72d) < 1e-9, "ON resumes towards 72%");
            station.AdvanceUntilSoil(72d);
            station.Advance(0.5f);
            Check(Math.Abs(model.Current - 72d) < 1e-9 && !model.Running && !water.Flowing, $"auto-stop exactly at {F(model.Current)}%, stream off");
            Check(panel.PageStatusText == "Setpoint reached - on target" && panel.PageStatusColor == BTP.UI.UIPalette.HealthGood, $"status '{panel.PageStatusText}' (green)");
            water.PressOn();
            station.Advance(1f);
            Check(Math.Abs(model.Current - 72d) < 1e-9 && !water.Flowing && panel.PageStatusText == "Choose water to add", $"ON again adds nothing: {F(model.Current)}%, '{panel.PageStatusText}'");
            var materialsAfter = renderers.SelectMany(r => r.sharedMaterials).ToList();
            var amountsAfter = materialsAfter.Select(m => m != null && m.HasProperty("_DiseaseAmount") ? m.GetFloat("_DiseaseAmount") : -1f).ToList();
            Check(materialsBefore.SequenceEqual(materialsAfter) && amountsBefore.SequenceEqual(amountsAfter), "leaf materials unchanged by watering");

            var settings = station.EvaluationSettings();
            Check(Math.Abs(settings.Get("soilMoisture", "rootZone") - 72d) < 1e-9 && settings.GetChoice("soilMoisture", "irrigation") == "rootZone",
                "Check Health sends rootZone 72 with rootZone delivery");
            if (liveUp)
            {
                var r = live.EvaluateBlocking(station.Session.Profile, settings);
                Check(r.Ok && Math.Abs(r.Value.FactorScore("soilMoisture") - 1d) < 1e-6 && Math.Abs(r.Value.LeafWetness - 22d) < 1e-6,
                    $"live: rice severe 72 + rootZone -> soil score {F(r.Ok ? r.Value.FactorScore("soilMoisture") : double.NaN)}, leaf wetness {F(r.Ok ? r.Value.LeafWetness : double.NaN)}");
                station.Apply(panel.Settings);
                var check = station.CheckHealth();
                station.DeliverEvaluation(check, live.EvaluateBlocking(station.Session.Profile, station.EvaluationSettings()));
                Check(Math.Abs(panel.LeafWetness - 22d) < 1e-9, $"after Check Health the server leaf wetness {F(panel.LeafWetness)} is shown: {panel.ResultText}");
            }

            // Changing the amount while paused recomputes the setpoint from the current moisture.
            station.SetWaterAmount(8);
            water.PressOn();
            station.Advance(1f);
            water.PressOff();
            var now = model.Current;
            station.SetWaterAmount(20);
            Check(Math.Abs(model.Setpoint - Math.Min(100d, now + 20d)) < 1e-9, $"paused at {F(now)}%: +20 -> setpoint {Pct(model.Setpoint)} (from the current moisture)");
            station.SetWaterAmount(-4);
            Check(model.Amount < 0d && panel.VisiblePageTexts().Contains("Drain 4%"), "a negative amount reads as a drain (Drain 4%)");
            Render(station, "rice_water_paused");

            // Drain: tomato early blight starts too wet (81 against 53-67).
            if (shown.TryGetValue("tomato_a", out var tomato))
            {
                Line("WATER station: drain Tomato / Early blight 81 -> 60");
                ShowCase(station, tomato);
                model = station.Watering;
                panel.ShowPage(3);
                Check(Math.Abs(model.Current - 81d) < 1e-9 || model.Delivered, $"tomato starts at {Pct(model.Current)} (band {model.TargetMin}-{model.TargetMax})");
                station.SetWaterAmount(60d - model.Current);
                Check(Math.Abs(model.Setpoint - 60d) < 1e-9 && panel.VisiblePageTexts().Contains("Drain 21%"), $"amount reads '{RootZoneWateringModel.AmountText(model.Amount, Pct)}', setpoint {Pct(model.Setpoint)}");
                water.PressOn();
                Check(model.Draining && !water.Flowing && water.DrainShown, "draining: no stream, drain LED cyan");
                Check(panel.PageStatusText == "Draining", $"status '{panel.PageStatusText}'");
                station.Advance(1.5f);
                Render(station, "tomato_draining");
                station.AdvanceUntilSoilBelow(60d);
                station.Advance(0.5f);
                Check(Math.Abs(model.Current - 60d) < 1e-9 && model.InTarget && !model.Running, $"drained to {F(model.Current)}% inside {model.TargetMin}-{model.TargetMax}");
                if (liveUp)
                {
                    var r = live.EvaluateBlocking(station.Session.Profile, station.EvaluationSettings());
                    Check(r.Ok && Math.Abs(r.Value.FactorScore("soilMoisture") - 1d) < 1e-6,
                        $"live: tomato drained 60 + rootZone -> soil score {F(r.Ok ? r.Value.FactorScore("soilMoisture") : double.NaN)}");
                }
            }

            // Untouched soil already in band holds: corn common rust.
            if (shown.TryGetValue("corn", out var corn) && liveUp)
            {
                ShowCase(station, corn);
                var s2 = station.EvaluationSettings();
                var r = live.EvaluateBlocking(station.Session.Profile, s2);
                Check(r.Ok && s2.GetChoice("soilMoisture", "irrigation") == "hold" && Math.Abs(r.Value.FactorScore("soilMoisture") - 1d) < 1e-6,
                    $"live: untouched corn {Pct(station.Watering.Current)} in band {station.Watering.TargetMin}-{station.Watering.TargetMax} + hold -> soil score " +
                    $"{F(r.Ok ? r.Value.FactorScore("soilMoisture") : double.NaN)}");
            }

            // Switching specimens moves the tray, window, outlet, sign and moisture values together; late responses are dropped.
            if (shown.TryGetValue("tomato_b", out var tomatoB) && shown.TryGetValue("apple", out var apple))
            {
                Line("Switch tomato #3 -> apple: everything updates together");
                var tomatoTicket = ShowCase(station, tomatoB);
                var tomatoProfile = Profile(tomatoB.PlantId, tomatoB.DiseaseId);
                var appleTicket = station.Show(apple.PlantId, apple.DiseaseId, apple.Snapshot.Specimen, apple.Snapshot);
                Check(!station.DeliverProfile(tomatoTicket, tomatoProfile), "a late tomato profile is ignored after switching to apple");
                station.DeliverProfile(appleTicket, Profile(apple.PlantId, apple.DiseaseId));
                var sub = station.Session.Profile.Find("soilMoisture", "rootZone");
                model = station.Watering;
                station.Chamber.SoilArea(out var min, out var max);
                var c = station.Water.Contact;
                Check(model != null && Math.Abs(model.Start - sub.Start) < 1e-9 && Math.Abs(model.TargetMin - sub.TargetMin) < 1e-9 && Math.Abs(model.Step - sub.Step) < 1e-9,
                    $"apple moisture: start {Pct(model.Start)}, band {model.TargetMin}-{model.TargetMax}, step {model.Step}");
                Check(c.x > min.x && c.x < max.x && c.z > min.y && c.z < max.y && Mathf.Abs(c.y - station.Chamber.SoilWorldY) < 1e-4f, "outlet refitted to the apple tray");
                Check(station.SignTitleText.StartsWith("Apple") && station.Current.SpecimenId == apple.Snapshot.Specimen.SpecimenId, $"sign '{station.SignTitleText}', specimen {station.Current.SpecimenId}");
                panel.ShowPage(3);
                Render(station, "switch_apple_water");
                ShowCase(station, rice, "severe");
                model = station.Watering;
                Check(model.Delivered && Math.Abs(model.Current - now) < 1e-3, $"back to rice: its soil state restored ({F(model.Current)}%, delivered {model.Delivered})");
            }

            // The polished Fertiliser view (large readable packet).
            ShowCase(station, rice, "standard");
            panel.ShowPage(1);
            panel.SetValue("fertiliser", "dose", 3);
            panel.SetValue("fertiliser", "applications", 2);
            // The packet alone at a held grip pose, tilted over the soil and pouring (no hand model).
            var grip = PourGrip(station, 50f);
            station.Applicator.Grab(grip, "right");
            for (var i = 0; i < 40; i++)
            {
                station.Applicator.Tick(1f / 60f);
                station.Tick(1f / 60f);
            }

            var camera = ViewCamera(station);
            try
            {
                var label = station.Applicator.Rig.GetComponentsInChildren<Canvas>(true).Select(x => (RectTransform)x.transform).FirstOrDefault(x => x.name == "Label");
                if (label != null)
                {
                    InView(camera, label, out var where);
                    Line($"  POTASSIUM label at the held pose: {where}");
                }
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }

            Render(station, "fertiliser_packet_polished");
            station.Applicator.Release();
            Object.DestroyImmediate(grip.gameObject);
        }

        // ------------------------------------------------------------------ Addendum 5: tracked hands (no hand visuals)

        /// <summary>A grip pose that holds the packet with its torn corner <paramref name="tilt"/> degrees below horizontal over the pour point.</summary>
        static Transform PourGrip(ChamberStation station, float tilt, bool overSoil = true)
        {
            var applicator = station.Applicator;
            var rig = applicator.Rig;
            var grip = new GameObject("TestGrip").transform;
            // The packet faces the viewer; a roll about its facing axis lowers the torn (right-hand) corner.
            var facing = Quaternion.LookRotation(station.PlantAnchor.forward, Vector3.up);
            var saved = (rig.position, rig.rotation);
            rig.SetPositionAndRotation(Vector3.zero, facing);
            float Tilted(float roll)
            {
                rig.rotation = facing * Quaternion.Euler(0f, 0f, roll);
                var centre = rig.Find("Wrist/Packet") != null ? rig.Find("Wrist/Packet").position : rig.position;
                var v = applicator.PourCorner.position - centre;
                return Mathf.Asin(Mathf.Clamp(-v.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            }

            // Find the roll that gives the requested tilt (monotonic over this range).
            var best = 0f;
            var bestError = float.MaxValue;
            for (var roll = 60f; roll >= -140f; roll -= 0.5f)
            {
                var error = Mathf.Abs(Tilted(roll) - tilt);
                if (error < bestError)
                {
                    bestError = error;
                    best = roll;
                }
            }

            rig.rotation = facing * Quaternion.Euler(0f, 0f, best);
            var cornerOffset = applicator.PourCorner.position - rig.position;
            var target = overSoil ? station.PourPoint : station.PourPoint - Vector3.up * 0.0f + station.PlantAnchor.right * -1.5f;
            grip.SetPositionAndRotation(target - cornerOffset, rig.rotation);
            rig.SetPositionAndRotation(saved.position, saved.rotation);
            return grip;
        }

        static void PourOnce(ChamberStation station, float seconds, string hand, string render)
        {
            var grip = PourGrip(station, 55f);
            station.Applicator.Grab(grip, hand);
            for (var t = 0f; t < seconds; t += 1f / 60f)
            {
                station.Applicator.Tick(1f / 60f);
                station.Tick(1f / 60f);
                if (render != null && t >= seconds * 0.5f)
                {
                    Render(station, render);
                    render = null;
                }
            }

            station.Applicator.Release();
            Object.DestroyImmediate(grip.gameObject);
        }

        static void VerifyHands(ChamberStation station, Dictionary<string, Case> shown)
        {
            Line("Addendum 5: tracked hands - press de-dup, hidden input, grab/attach, tilt-to-pour, spray, tracking loss, seated reach");
            var panel = station.Panel;

            // One gesture = one press: a poke and a ray activation of the same button in one gesture count once.
            if (shown.TryGetValue("rice", out var rice))
            {
                ShowCase(station, rice, "severe");
                panel.ShowPage(3);
                var water = station.Water;
                var presses = 0;
                void Count() => presses++;
                water.OnPressed += Count;
                water.OnButton.onClick.Invoke(); // poke
                water.OnButton.onClick.Invoke(); // the pinch ray on the same button in the same gesture
                water.OnPressed -= Count;
                Check(presses == 1, $"WATER ON pressed by poke and ray in one gesture acts once ({presses}); log: {HandTestLog.Last}");
                water.PressOff();
                var tabPresses = 0;
                void Page(int p) => tabPresses++;
                panel.PageChanged += Page;
                var tab = station.PanelCanvas.GetComponentsInChildren<Button>(true).First(b => b.name == "Tab1");
                tab.onClick.Invoke();
                tab.onClick.Invoke();
                panel.PageChanged -= Page;
                Check(tabPresses == 1, $"touchscreen tab pressed twice in one gesture changes the page once ({tabPresses})");
            }

            // Hidden panels ignore input.
            var hiddenOk = true;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas.transform.IsChildOf(station.PanelCanvas) || canvas.renderMode != RenderMode.WorldSpace)
                    continue;
                var raycaster = canvas.GetComponent<UnityEngine.EventSystems.BaseRaycaster>();
                var group = canvas.GetComponent<CanvasGroup>();
                var interactive = canvas.gameObject.activeInHierarchy && raycaster != null && raycaster.enabled && (group == null || group.blocksRaycasts) &&
                                  canvas.GetComponentsInChildren<Button>(false).Length > 0;
                if (interactive && !canvas.transform.IsChildOf(station.Water.transform))
                {
                    hiddenOk = false;
                    Line($"  other interactive canvas: {canvas.name}");
                }
            }

            Check(hiddenOk, "no other treatment canvas takes input (the touchscreen and the WATER console are the only button surfaces)");
            panel.ShowCasePage(false);
            Check(!station.PanelCanvas.GetComponentsInChildren<Button>(false).Any(b => b.transform.IsChildOf(station.PanelCanvas.Find("Body/CasePage"))),
                "the closed Choose a Case page has no active buttons");

            if (rice == null)
                return;
            // Grab and attach to the grip pose with either hand; release returns to the dock.
            ShowCase(station, rice, "standard");
            var applicator = station.Applicator;
            foreach (var hand in new[] { "left", "right" })
            {
                var grip = new GameObject("TestGrip").transform;
                grip.SetPositionAndRotation(applicator.DockPosition + new Vector3(0.1f, 0.15f, 0.05f), Quaternion.Euler(10f, 20f, 5f));
                applicator.Grab(grip, hand);
                applicator.Tick(1f / 60f);
                var attached = (applicator.Rig.position - grip.position).magnitude < 1e-5f && Quaternion.Angle(applicator.Rig.rotation, grip.rotation) < 0.01f;
                applicator.Release();
                applicator.Tick(FertiliserApplicator.ReturnDelay + 0.05f);
                var docked = (applicator.Rig.position - applicator.DockPosition).magnitude < 1e-5f;
                Check(attached && docked && applicator.HoldingHand == null, $"{hand} hand: packet follows the grip pose, release returns it to the dock");
                Object.DestroyImmediate(grip.gameObject);
            }

            // Tilt-to-pour: on only past the pour angle and over the soil.
            foreach (var (tilt, overSoil, expect) in new[] { (5f, true, false), (35f, true, true), (70f, true, true), (55f, false, false) })
            {
                var grip = PourGrip(station, tilt, overSoil);
                applicator.Grab(grip, "right");
                var before = applicator.Simulation != null ? applicator.Simulation.Count : 0;
                for (var i = 0; i < 30; i++)
                    applicator.Tick(1f / 60f);
                var after = applicator.Simulation != null ? applicator.Simulation.Count : 0;
                var pouring = applicator.IsPouring;
                Check(pouring == expect && (after > before) == expect,
                    $"packet tilted {F(applicator.Tilt)} deg {(overSoil ? "over the soil" : "away from the tray")}: pouring {pouring}, +{after - before} granules");
                applicator.Release();
                Object.DestroyImmediate(grip.gameObject);
            }

            // Tracking loss mid-pour: stops the same frame, the packet returns to its dock immediately, settings and specimen kept.
            {
                panel.SetValue("temperature", "day", 24);
                var settingsBefore = panel.Settings.Clone();
                var specimenBefore = station.Current.SpecimenId;
                var grip = PourGrip(station, 60f);
                applicator.Grab(grip, "right");
                for (var i = 0; i < 10; i++)
                    applicator.Tick(1f / 60f);
                var wasPouring = applicator.IsPouring;
                station.HandleTrackingLost("right");
                Check(wasPouring && !applicator.IsPouring && !applicator.IsHeld && (applicator.Rig.position - applicator.DockPosition).magnitude < 1e-5f,
                    "tracking lost mid-pour: pouring stops the same frame, the packet is back in its dock");
                var tracker = new HandLossTracker();
                tracker.Update(0.016f, false, true);
                tracker.Update(0.5f, false, false);
                var early = tracker.ShowMessage;
                tracker.Update(0.6f, false, false);
                station.ShowHandsOutOfView(tracker.ShowMessage);
                Check(!early && tracker.ShowMessage && panel.HandsNoticeText == HandLossTracker.Message,
                    $"after {HandLossTracker.MessageDelay} s without hands the touchscreen shows '{panel.HandsNoticeText}'");
                tracker.Update(0.016f, true, false);
                station.ShowHandsOutOfView(tracker.ShowMessage);
                Check(panel.HandsNoticeText == null && panel.Settings.SameAs(settingsBefore) && station.Current.SpecimenId == specimenBefore,
                    "hands back: notice cleared, specimen and settings unchanged");
                Object.DestroyImmediate(grip.gameObject);
            }

            // Spray (wheat is spray-indicated? use the first spray-indicated case shown): separate pinch, target-only coverage, loss.
            Case sprayCase = null;
            foreach (var c in shown.Values)
            {
                ShowCase(station, c);
                if (station.Session.Profile?.Find("spray")?.Indicated == true && station.SprayTool != null)
                {
                    sprayCase = c;
                    break;
                }
            }

            if (Check(sprayCase != null, $"spray-indicated case for the sprayer: {sprayCase?.Name}"))
            {
                var tool = station.SprayTool;
                panel.ShowPage(2);
                Check(tool.gameObject.activeSelf && !tool.IsHeld, "training sprayer in its dock");
                var target = station.SprayTarget.transform.position;
                var grip = new GameObject("TestGrip").transform;
                // Aim the nozzle at the marked target from 0.35 m in front of it.
                var nozzleLocal = tool.transform.InverseTransformPoint(tool.Nozzle.position);
                var aim = Quaternion.LookRotation((target - (target - station.PlantAnchor.forward * 0.35f)).normalized, Vector3.up);
                var nozzleRotationLocal = Quaternion.Inverse(tool.transform.rotation) * tool.Nozzle.rotation;
                var toolRotation = aim * Quaternion.Inverse(nozzleRotationLocal);
                var nozzleWorld = target - station.PlantAnchor.forward * 0.35f;
                grip.SetPositionAndRotation(nozzleWorld - toolRotation * nozzleLocal, toolRotation);
                Physics.SyncTransforms();
                tool.SetPinch(true); // pinch already closed while grabbing
                tool.Grab(grip, "left");
                tool.Tick(0.5f);
                Check(!tool.IsSpraying && tool.SprayedUnits == 0f, "a pinch held at pickup does not spray (a separate pinch is required)");
                tool.SetPinch(false);
                tool.SetPinch(true);
                for (var i = 0; i < 60; i++)
                    tool.Tick(1f / 60f);
                var onTarget = tool.Coverage;
                Check(tool.IsSpraying && onTarget > 10f && tool.LastHitFraction > 0.5f,
                    $"separate pinch sprays: coverage {F(onTarget)}% ({F(tool.LastHitFraction * 100f)}% of rays on the marked foliage), {F(tool.SprayedUnits)} units");
                station.Tick(0f);
                Render(station, "a5_spraying_target");
                grip.rotation = grip.rotation * Quaternion.Euler(0f, 70f, 0f);
                tool.Tick(0f);
                Physics.SyncTransforms();
                for (var i = 0; i < 60; i++)
                    tool.Tick(1f / 60f);
                Check(Math.Abs(tool.Coverage - onTarget) < 1e-4f && tool.LastHitFraction == 0f, $"spray aimed away from the target adds no coverage ({F(tool.Coverage)}%)");
                tool.SetPinch(false);
                Check(!tool.IsSpraying, "releasing the pinch stops the spray");
                var sprayDose = panel.Settings.Get("spray", "dose");
                Check(Math.Abs(panel.Settings.Get("spray", "targetCoverage") - Math.Floor(tool.Coverage / 5f) * 5f) < 1e-6 && Math.Abs(sprayDose - Math.Floor(tool.SprayedUnits)) < 1e-6,
                    $"spray feeds the Spray settings: {sprayDose} units, {panel.Settings.Get("spray", "targetCoverage")}% coverage");
                tool.SetPinch(true);
                tool.Tick(1f / 60f);
                station.HandleTrackingLost("left");
                Check(!tool.IsSpraying && !tool.IsHeld && (tool.transform.position - tool.DockPosition).magnitude < 1e-5f,
                    "tracking lost mid-spray: spray stops the same frame, the sprayer is back in its dock");
                Object.DestroyImmediate(grip.gameObject);
            }

            // A monitoring case keeps the no-spray path: 0/0 with no pickup.
            foreach (var c in shown.Values)
            {
                ShowCase(station, c);
                if (station.Session.Profile?.Find("spray")?.Indicated == false)
                {
                    Check(!station.SprayTool.gameObject.activeSelf && panel.Settings.Get("spray", "dose") == 0d,
                        $"{c.Name}: monitoring case, no sprayer, spray 0/0 without a pickup");
                    break;
                }
            }

            // Seated reach per station (Addendum 6): at each station's anchor every poke/grab target is within 0.65 m of the
            // seated shoulder (the bench, tall and walk-in cases are also checked in the base cases).
            foreach (var name in new[] { "rice", "strawberry", "wheat", "apple" })
            {
                if (!shown.TryGetValue(name, out var c))
                    continue;
                ShowCase(station, c);
                foreach (var id in new[] { StationLayout.Station.Kiosk, StationLayout.Station.Water, StationLayout.Station.Tools })
                    SeatedReachAt(station, name + " (hands)", id);
            }
        }

        // ------------------------------------------------------------------ Addendum 4: the single touchscreen

        static void VerifyTouchscreen(ChamberStation station, Dictionary<string, Case> shown)
        {
            Line("Addendum 4: single touchscreen, clear face, outage, modal, switching while loading");
            foreach (var name in new[] { "strawberry", "wheat", "apple" })
            {
                if (!shown.TryGetValue(name, out var c))
                    continue;
                ShowCase(station, c);
                TouchscreenChecks(station, c);
                KioskTextCheck(station, name);
                RowAlignmentChecks(station, name);
                Render(station, "a4_" + name + "_spawn");
                Render(station, "a6_" + name + "_overview", overview: true);
            }

            if (shown.TryGetValue("wheat", out var wheat) && shown.TryGetValue("strawberry", out var strawberry))
            {
                // Switch while the wheat profile is still loading: its late answer is ignored, the strawberry stays.
                var wheatTicket = station.Show(wheat.PlantId, wheat.DiseaseId, wheat.Snapshot.Specimen, wheat.Snapshot);
                Check(station.Panel.Current == ChamberControlPanel.State.Loading && station.Panel.ConnectionState == ChamberControlPanel.Connection.Loading,
                    "wheat profile loading: header shows Loading");
                var strawberryTicket = station.Show(strawberry.PlantId, strawberry.DiseaseId, strawberry.Snapshot.Specimen, strawberry.Snapshot);
                Check(!station.DeliverProfile(wheatTicket, Profile(wheat.PlantId, wheat.DiseaseId)), "late wheat profile ignored after switching to strawberry");
                station.DeliverProfile(strawberryTicket, Profile(strawberry.PlantId, strawberry.DiseaseId));
                Check(station.Current.PlantId == "strawberry" && station.Specimen.name.Contains("strawberry") && station.Panel.CaseTitleText.StartsWith("Strawberry"),
                    $"strawberry stays: specimen {station.Specimen.name}, header '{station.Panel.CaseTitleText}'");

                // The XR scan-switch modal (the coordinator's own Open/Keep actions are passed in by the station at runtime).
                ShowCase(station, wheat);
                var panel = station.Panel;
                var opened = 0;
                var kept = 0;
                foreach (var primary in new[] { true, false })
                {
                    station.ShowScanSwitch(true, "Scan 42 (Tomato / Early blight) was sent to this headset. The assessment of scan 41 is not saved yet.",
                        () => opened++, () => kept++);
                    Check(panel.ModalOpen && panel.ModalTitleText == "Open a different scan?" && !panel.Body.interactable && !panel.Body.blocksRaycasts,
                        "modal open: input behind it blocked (CanvasGroup interactable and blocksRaycasts off)");
                    if (primary)
                        Render(station, "a4_scan_switch_modal");
                    panel.PressModal(primary);
                    Check(!panel.ModalOpen && panel.Body.interactable && panel.Body.blocksRaycasts, $"{(primary ? "Open New Scan" : "Keep Current Scan")}: modal closed, input back");
                }

                Check(opened == 1 && kept == 1, $"each modal button calls its action once (open {opened}, keep {kept})");

                // API outage: a dead port, then recovery with Retry.
                var good = station.Client;
                station.Client = new TreatmentCaseClient("http://127.0.0.1:8999");
                var outageTicket = station.ChangeScenario("standard");
                station.DeliverProfile(outageTicket, station.Client.GetCaseBlocking(wheat.PlantId, wheat.DiseaseId, "standard"));
                Check(panel.ConnectionState == ChamberControlPanel.Connection.Offline && panel.ConnectionText == "Offline - Retry",
                    $"outage: header '{panel.ConnectionText}'");
                Check(panel.Current == ChamberControlPanel.State.Unavailable && !panel.ApplyInteractable && !panel.CheckInteractable,
                    $"outage: inline error with Retry, controls disabled ({Flat(panel.MessageText)})");
                Check(station.Specimen != null && station.Specimen.activeInHierarchy, "outage: the plant stays visible");
                Render(station, "a4_wheat_outage");
                station.Client = good;
                var retryTicket = station.ChangeScenario("standard");
                station.DeliverProfile(retryTicket, Profile(wheat.PlantId, wheat.DiseaseId));
                Check(liveUp ? panel.ConnectionState == ChamberControlPanel.Connection.Connected && panel.Current == ChamberControlPanel.State.Ready : true,
                    $"Retry with the server back: header '{panel.ConnectionText}', panel {panel.Current}");
                foreach (var page in new[] { 0, 1, 2, 3 })
                {
                    panel.ShowPage(page);
                    Render(station, "a4_wheat_tab" + (page + 1));
                    Render(station, "a6_wheat_kiosk_tab" + (page + 1), at: StationLayout.Station.Kiosk);
                }

                Render(station, "a6_wheat_water_station", at: StationLayout.Station.Water, target: station.Water.OnCapTop);
                Render(station, "a6_wheat_tools_station", at: StationLayout.Station.Tools, target: station.SprayerDock.position + Vector3.up * 0.4f);
                panel.ShowCasePage(true);
                Check(panel.CasePageOpen, "Choose a Case is a page of the touchscreen");
                Render(station, "a4_wheat_choose_case");
                panel.ShowCasePage(false);
            }

            if (liveUp)
            {
                var health = HealthStatus(live.BaseUrl);
                Check(health == 200, $"desktop backend answers {live.BaseUrl}/healthz ({health})");
            }
        }

        static int HealthStatus(string baseUrl)
        {
            using (var request = UnityEngine.Networking.UnityWebRequest.Get(baseUrl + "/healthz"))
            {
                var operation = request.SendWebRequest();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!operation.isDone && DateTime.UtcNow < deadline)
                    System.Threading.Thread.Sleep(5);
                return (int)request.responseCode;
            }
        }

        /// <summary>
        /// Reach, height and tilt of the touchscreen; it never touches the chamber; a 9 x 7 grid on its face is visible from the
        /// spawn, recentered and seated eyes (no glass, post, rail, door, plant or tool in the way, tested against temporary
        /// mesh colliders on the real geometry); and it covers at most 5% of the specimen's screen area.
        /// </summary>
        static void TouchscreenChecks(ChamberStation station, Case c)
        {
            var chamber = station.Chamber;
            var kioskAnchor = station.AnchorOf(StationLayout.Station.Kiosk);
            var screen = (RectTransform)station.PanelCanvas;
            var centre = screen.position;
            var flat = new Vector2(centre.x - kioskAnchor.position.x, centre.z - kioskAnchor.position.z).magnitude;
            var tilt = Vector3.Angle(Vector3.ProjectOnPlane(-screen.forward, Vector3.up), -screen.forward);
            var width = screen.rect.width * screen.lossyScale.x;
            Check(flat <= 0.35f && Mathf.Abs(centre.y - ChamberStation.PanelHeight) < 1e-3f && Mathf.Abs(tilt - ChamberStation.PanelTilt) < 0.5f && width > 0.9f,
                $"{c.Name}: kiosk screen {F(width)} x {F(screen.rect.height * screen.lossyScale.y)} m, {F(flat)} m ahead of its anchor, centre {F(centre.y)} m, tilted {F(tilt)} deg");
            var exterior = new Bounds(chamber.transform.TransformPoint(chamber.ExteriorBounds.center), chamber.ExteriorBounds.size);
            var corners = new Vector3[4];
            screen.GetWorldCorners(corners);
            var screenBox = new Bounds(corners[0], Vector3.zero);
            foreach (var corner in corners)
                screenBox.Encapsulate(corner);
            screenBox.Expand(new Vector3(0.03f, 0.03f, 0.03f));
            Check(!screenBox.Intersects(exterior), $"{c.Name}: the touchscreen never intersects the chamber exterior");

            // Temporary colliders on everything that could block the face.
            var temporary = new List<Collider>();
            var blockers = new List<Renderer>();
            blockers.AddRange(chamber.GetComponentsInChildren<Renderer>(false));
            if (station.Water != null)
                blockers.AddRange(station.Water.GetComponentsInChildren<Renderer>(false));
            if (station.Applicator != null)
                blockers.AddRange(station.Applicator.GetComponentsInChildren<Renderer>(false));
            if (station.Sprayer != null)
                blockers.AddRange(station.Sprayer.GetComponentsInChildren<Renderer>(false));
            foreach (var renderer in blockers)
            {
                if (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>()?.sharedMesh != null && renderer.GetComponent<Collider>() == null)
                {
                    var collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    temporary.Add(collider);
                }
                else if (renderer is SkinnedMeshRenderer skinned && skinned.GetComponent<Collider>() == null)
                {
                    var collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = skinned.sharedMesh;
                    temporary.Add(collider);
                }
            }

            Physics.SyncTransforms();
            try
            {
                foreach (var (label, eyeHeight, from) in new[]
                         {
                             ("kiosk anchor standing", ChamberStation.Eye, kioskAnchor), ("kiosk anchor seated", ChamberStation.SeatedEye, kioskAnchor),
                             ("overview spawn", ChamberStation.Eye, station.SpawnPoint.transform),
                         })
                {
                    var eye = from.position + Vector3.up * eyeHeight;
                    var blocked = 0;
                    string first = null;
                    for (var i = 0; i < 9; i++)
                    for (var j = 0; j < 7; j++)
                    {
                        var u = (i + 0.5f) / 9f;
                        var v = (j + 0.5f) / 7f;
                        var point = Vector3.Lerp(Vector3.Lerp(corners[0], corners[3], u), Vector3.Lerp(corners[1], corners[2], u), v);
                        var direction = point - eye;
                        if (Physics.Raycast(eye, direction.normalized, out var hit, direction.magnitude - 0.005f, Physics.AllLayers, QueryTriggerInteraction.Collide))
                        {
                            blocked++;
                            first ??= hit.collider.name;
                        }
                    }

                    Check(blocked == 0, $"{c.Name}: clear face from the {label} eye ({F(eyeHeight)} m): {63 - blocked}/63 points visible{(first != null ? ", first blocker " + first : "")}");
                }
            }
            finally
            {
                foreach (var collider in temporary)
                    Object.DestroyImmediate(collider);
            }

            // The specimen's screen rectangle is not hidden by the kiosk by more than 5% in the overview.
            var camera = OverviewCamera(station);
            try
            {
                var bounds = SpecimenGeometry.Measure(station.Specimen, null);
                Rect Project(IEnumerable<Vector3> points)
                {
                    var min = new Vector2(float.MaxValue, float.MaxValue);
                    var max = new Vector2(float.MinValue, float.MinValue);
                    foreach (var p in points)
                    {
                        var v = (Vector2)camera.WorldToViewportPoint(p);
                        min = Vector2.Min(min, v);
                        max = Vector2.Max(max, v);
                    }

                    return Rect.MinMaxRect(Mathf.Clamp01(min.x), Mathf.Clamp01(min.y), Mathf.Clamp01(max.x), Mathf.Clamp01(max.y));
                }

                var boxCorners = new List<Vector3>();
                for (var k = 0; k < 8; k++)
                    boxCorners.Add(new Vector3((k & 1) == 0 ? bounds.min.x : bounds.max.x, (k & 2) == 0 ? bounds.min.y : bounds.max.y, (k & 4) == 0 ? bounds.min.z : bounds.max.z));
                var plantRect = Project(boxCorners);
                var screenRect = Project(corners);
                var overlap = Rect.MinMaxRect(Mathf.Max(plantRect.xMin, screenRect.xMin), Mathf.Max(plantRect.yMin, screenRect.yMin),
                    Mathf.Min(plantRect.xMax, screenRect.xMax), Mathf.Min(plantRect.yMax, screenRect.yMax));
                var covered = overlap.width > 0f && overlap.height > 0f ? overlap.width * overlap.height / Mathf.Max(plantRect.width * plantRect.height, 1e-6f) : 0f;
                Check(covered <= 0.05f, $"{c.Name}: touchscreen covers {covered * 100f:0.0}% of the specimen on screen");
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }

            Perf(station, c.Name);
        }

        /// <summary>A rough Editor performance snapshot of the spawn view: visible renderers, triangles, draw calls, textures.</summary>
        static void Perf(ChamberStation station, string name)
        {
            var camera = OverviewCamera(station);
            try
            {
                var planes = GeometryUtility.CalculateFrustumPlanes(camera);
                long triangles = 0;
                var draws = 0;
                var visible = 0;
                var textures = new HashSet<Texture>();
                var materials = new HashSet<Material>();
                foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                {
                    if (!renderer.enabled || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                        continue;
                    if (renderer is ParticleSystemRenderer)
                        continue;
                    var lodGroup = renderer.GetComponentInParent<LODGroup>();
                    if (lodGroup != null && lodGroup.lodCount > 1 && !lodGroup.GetLODs()[0].renderers.Contains(renderer))
                        continue; // only the LOD in use near the viewer
                    visible++;
                    Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh != null)
                        for (var i = 0; i < mesh.subMeshCount; i++)
                            triangles += mesh.GetIndexCount(i) / 3;
                    draws += Mathf.Max(1, renderer.sharedMaterials.Length);
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null)
                            continue;
                        materials.Add(material);
                        foreach (var property in material.GetTexturePropertyNameIDs())
                        {
                            var texture = material.GetTexture(property);
                            if (texture != null)
                                textures.Add(texture);
                        }
                    }
                }

                long textureBytes = 0;
                foreach (var texture in textures)
                    textureBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                var lightmaps = LightmapSettings.lightmaps;
                var lightmapText = lightmaps != null && lightmaps.Length > 0 && lightmaps[0].lightmapColor != null
                    ? $"{lightmaps.Length} x {lightmaps[0].lightmapColor.width}px"
                    : "none";
                Line($"  PERF {name}: {visible} visible renderers, {triangles:N0} triangles, ~{draws} draw calls (before batching), {materials.Count} materials " +
                     $"(SetPass upper bound), {textures.Count} textures {textureBytes / (1024f * 1024f):0.0} MB (Editor memory), lightmaps {lightmapText}");
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        // ------------------------------------------------------------------ Addendum 6: separate stations

        static Vector2[] RectCorners(Rect r) => new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };

        /// <summary>The chamber's real exterior footprint in line space (from its transform and bounds, not from the layout).</summary>
        static Rect ChamberRect(ChamberStation station)
        {
            var chamber = station.Chamber;
            var line = station.ChamberLine;
            var a = line.InverseTransformPoint(chamber.transform.TransformPoint(chamber.ExteriorBounds.min));
            var b = line.InverseTransformPoint(chamber.transform.TransformPoint(chamber.ExteriorBounds.max));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }

        static Transform StationRoot(ChamberStation station, StationLayout.Station id) => id switch
        {
            StationLayout.Station.Kiosk => station.KioskRoot,
            StationLayout.Station.Water => station.Water.Console,
            _ => station.ToolsBench,
        };

        static Bounds RendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(false);
            var bounds = new Bounds(root.position, Vector3.zero);
            foreach (var r in renderers)
                if (r.enabled && !(r is ParticleSystemRenderer) && r.name != "ContactShadow")
                    bounds.Encapsulate(r.bounds);
            return bounds;
        }

        /// <summary>
        /// Station spacing, chamber and wall clearance, approach zones, teleport anchors, per-station seated reach, the
        /// overview, label readability and the kiosk's body text, for the specimen now shown.
        /// </summary>
        static void VerifyStations(ChamberStation station, string name)
        {
            var r = station.Stations;
            if (!Check(r != null && r.Valid, $"{name}: station layout valid{(r != null && !r.Valid ? " - " + string.Join("; ", r.Problems) : "")}"))
                return;
            var line = station.ChamberLine;
            var chamberRect = ChamberRect(station);
            var chamberCorners = RectCorners(chamberRect);
            Check(Mathf.Abs(chamberRect.width - r.Chamber.width) < 1e-3f && Mathf.Abs(chamberRect.height - r.Chamber.height) < 1e-3f &&
                  Mathf.Abs(chamberRect.xMax) < 1e-3f && Mathf.Abs(chamberRect.yMin) < 1e-3f,
                $"{name}: layout uses the real chamber footprint X {F(chamberRect.xMin)}..{F(chamberRect.xMax)}, Z {F(chamberRect.yMin)}..{F(chamberRect.yMax)}");
            var stations = r.Stations.ToList();
            var minGap = float.MaxValue;
            var gaps = new List<string>();
            foreach (var s in stations)
            {
                var g = StationLayout.Distance(s.Footprint, chamberCorners);
                gaps.Add($"{s.Id}-chamber {F(g)}");
                minGap = Mathf.Min(minGap, g);
            }

            for (var i = 0; i < stations.Count; i++)
            for (var j = i + 1; j < stations.Count; j++)
            {
                var g = StationLayout.Distance(stations[i].Footprint, stations[j].Footprint);
                gaps.Add($"{stations[i].Id}-{stations[j].Id} {F(g)}");
                minGap = Mathf.Min(minGap, g);
            }

            Check(minGap >= StationLayout.MinGap - 1e-3f, $"{name}: clear gaps (m) {string.Join(", ", gaps)}; min {F(minGap)} >= {StationLayout.MinGap}");
            foreach (var s in stations)
                Line($"  {s.Id}: centre ({F(s.Centre.x)}, {F(s.Centre.y)}) yaw {F(s.Yaw)} base {F(s.Size.x)} x {F(s.Size.y)} m, anchor ({F(s.Anchor.x)}, {F(s.Anchor.y)})");
            Line($"  overview spawn ({F(r.Overview.x)}, {F(r.Overview.y)}) yaw {F(r.OverviewYaw)}, needs +-{F(StationLayout.OverviewHalfSpan(r))} deg horizontally");

            // The real geometry agrees: each station's renderers stay in the hall and clear of the chamber.
            var hall = station.HallRect;
            foreach (var s in stations)
            {
                var root = StationRoot(station, s.Id);
                var b = RendererBounds(root);
                var min = line.InverseTransformPoint(b.min);
                var max = line.InverseTransformPoint(b.max);
                var box = Rect.MinMaxRect(min.x, min.z, max.x, max.z);
                var inside = box.xMin >= hall.xMin - 1e-3f && box.xMax <= hall.xMax + 1e-3f && box.yMin >= hall.yMin - 1e-3f && box.yMax <= hall.yMax + 1e-3f;
                var clear = StationLayout.Distance(RectCorners(box), chamberCorners);
                Check(inside && clear >= 1.0f, $"{name}: {s.Id} geometry ({F(box.width)} x {F(box.height)} m, {F(b.size.y)} m tall) inside the hall, " +
                                                $"{F(clear)} m from the chamber (bounding box)");
                var anchor = station.AnchorOf(s.Id);
                var toStation = root.position - anchor.position;
                toStation.y = 0f;
                // The user stands at the station's front: never inside its solid body (cabinet / console body); at the kiosk the
                // head is over the base plate's front lip, just in front of the tilted screen's near edge.
                var solid = root.GetComponentsInChildren<Renderer>(false).Where(x => x.name is "Cabinet" or "Body").Select(x => x.bounds).ToList();
                var inSolid = solid.Any(x => anchor.position.x > x.min.x && anchor.position.x < x.max.x && anchor.position.z > x.min.z && anchor.position.z < x.max.z);
                var free = StationLayout.Distance(new[] { s.Anchor }, chamberCorners);
                Check(anchor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor>() != null &&
                      Vector3.Dot(anchor.forward, toStation.normalized) > 0.8f && solid.Count > 0 && !inSolid && free >= 1.0f,
                    $"{name}: {s.Id} teleport anchor at its front, facing it, outside its body ({F(toStation.magnitude)} m to its centre, {F(free)} m from the chamber)");
            }

            var plant = station.PlantAnchor;
            Check(plant != null && plant.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor>() != null &&
                  !chamberRect.Contains(new Vector2(line.InverseTransformPoint(plant.position).x, line.InverseTransformPoint(plant.position).z)),
                $"{name}: plant-chamber teleport anchor in front of the tray window, outside the chamber");

            OverviewChecks(station, name);
            foreach (var id in new[] { StationLayout.Station.Kiosk, StationLayout.Station.Water, StationLayout.Station.Tools })
                SeatedReachAt(station, name, id);
            LabelChecks(station, name);
            HoseChecks(station, name);
        }

        /// <summary>The overview spawn shows every station (base to top) and the chamber front in a 62-degree 16:9 view.</summary>
        static void OverviewChecks(ChamberStation station, string name)
        {
            var camera = OverviewCamera(station);
            try
            {
                var outside = new List<string>();
                var worstX = 0f;
                foreach (var s in station.Stations.Stations)
                {
                    var b = RendererBounds(StationRoot(station, s.Id));
                    foreach (var p in s.Footprint)
                    foreach (var y in new[] { 0f, b.max.y })
                    {
                        var v = camera.WorldToViewportPoint(station.ChamberLine.TransformPoint(new Vector3(p.x, y, p.y)));
                        worstX = Mathf.Max(worstX, Mathf.Abs(v.x - 0.5f));
                        if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f)
                            outside.Add($"{s.Id} ({F(v.x)}, {F(v.y)})");
                    }
                }

                var chamber = ChamberRect(station);
                foreach (var x in new[] { chamber.xMin, chamber.xMax })
                {
                    var v = camera.WorldToViewportPoint(station.ChamberLine.TransformPoint(new Vector3(x, 0f, chamber.yMin)));
                    if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f)
                        outside.Add($"chamber front ({F(v.x)}, {F(v.y)})");
                }

                Check(outside.Count == 0, $"{name}: overview spawn shows every station and the chamber front (widest at viewport x {F(0.5f + worstX)}, " +
                                          $"pitch {F(station.SpawnPitch)}){(outside.Count > 0 ? " - outside: " + string.Join(", ", outside.Distinct()) : "")}");
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        static Vector3 Centre(RectTransform rect) => rect.TransformPoint(rect.rect.center);

        /// <summary>Seated reach at one station's anchor: every poke and grab target within 0.65 m of the seated shoulder.</summary>
        static void SeatedReachAt(ChamberStation station, string name, StationLayout.Station id)
        {
            var anchor = station.AnchorOf(id);
            var eye = anchor.position + Vector3.up * ChamberStation.SeatedEye;
            Vector3 Shoulder(Vector3 target)
            {
                var side = Vector3.Dot(target - eye, anchor.right) >= 0f ? 1f : -1f;
                return eye - Vector3.up * 0.2f + anchor.right * (0.18f * side);
            }

            var targets = new Dictionary<string, Vector3>();
            var panel = station.Panel;
            if (id == StationLayout.Station.Kiosk)
            {
                var page = panel.Page;
                var casePage = station.PanelCanvas.Find("Body/CasePage");
                void Collect(string prefix)
                {
                    foreach (var button in station.PanelCanvas.GetComponentsInChildren<Button>(false))
                        if (!button.transform.IsChildOf(casePage) && !button.name.StartsWith("Stage"))
                            targets[prefix + button.name] = Centre((RectTransform)button.transform);
                }

                if (panel.Current == ChamberControlPanel.State.Ready)
                    foreach (var p in new[] { 0, 1, 2, 3 })
                    {
                        panel.ShowPage(p);
                        Collect(ChamberControlPanel.TabTitles[p] + " ");
                    }
                else
                    Collect("");
                panel.ShowMenu(true);
                Collect("menu ");
                panel.ShowMenu(false);
                panel.ShowPage(page);
            }
            else if (id == StationLayout.Station.Water)
            {
                var water = station.Water;
                targets["ON"] = water.OnCapTop;
                targets["ON button"] = Centre((RectTransform)water.OnButton.transform);
                targets["OFF button"] = Centre((RectTransform)water.OffButton.transform);
                targets["Water to add -"] = Centre((RectTransform)water.AmountMinus.transform);
                targets["Water to add +"] = Centre((RectTransform)water.AmountPlus.transform);
            }
            else
            {
                targets["packet grip"] = station.Applicator.HasPacket ? station.Applicator.DockPosition : station.PacketDock.position + Vector3.up * 0.49f;
                targets["sprayer grip"] = station.SprayerDock.position + Vector3.up * 0.13f;
            }

            var worst = 0f;
            var worstName = "";
            foreach (var t in targets)
            {
                var d = (t.Value - Shoulder(t.Value)).magnitude;
                if (d > worst)
                {
                    worst = d;
                    worstName = t.Key;
                }
            }

            ReachTable.Add($"{name} | {id} | {targets.Count} | {worstName} | {F(worst)}");
            Check(worst <= 0.65f, $"{name}: seated reach at the {id} anchor - {targets.Count} poke/grab targets, farthest {worstName} {F(worst)} m from the seated shoulder");
        }

        static readonly List<string> ReachTable = new List<string>();

        static float CapAngle(TMP_Text text, Vector3 eye)
        {
            text.ForceMeshUpdate();
            var font = text.font;
            var ratio = font != null && font.faceInfo.pointSize > 0f ? font.faceInfo.capLine / font.faceInfo.pointSize : 0.7f;
            var cap = text.fontSize * ratio * text.transform.lossyScale.y;
            var centre = Centre(text.rectTransform);
            var up = text.transform.up;
            var a = centre - up * (cap * 0.5f) - eye;
            var b = centre + up * (cap * 0.5f) - eye;
            return Vector3.Angle(a, b);
        }

        /// <summary>Station names: cap height at 2.5 m (along the sign's normal) and from the overview eye.</summary>
        static void LabelChecks(ChamberStation station, string name)
        {
            var labels = new List<(string, TMP_Text)>
            {
                ("kiosk TREATMENT CONTROLS", station.KioskRoot.Find("TopperLabel/Text")?.GetComponent<TMP_Text>()),
                ("bench TREATMENT TOOLS", station.ToolsBench.Find("HeaderLabel/Text")?.GetComponent<TMP_Text>()),
                ("console WATER CONTROL", station.Water.Console.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Label")),
            };
            var overviewEye = station.SpawnPoint.transform.position + Vector3.up * ChamberStation.Eye;
            foreach (var (label, text) in labels)
            {
                if (!Check(text != null, $"{name}: {label} label exists"))
                    continue;
                var normalEye = Centre(text.rectTransform) - text.transform.forward * 2.5f;
                var at25 = CapAngle(text, normalEye);
                var fromOverview = CapAngle(text, overviewEye);
                var distance = (Centre(text.rectTransform) - overviewEye).magnitude;
                Check(at25 >= 1.2f, $"{name}: {label} '{text.text}' cap height {F(at25)} deg at 2.5 m (>= 1.2); {F(fromOverview)} deg from the overview ({F(distance)} m)");
            }
        }

        /// <summary>
        /// Kiosk body text from the kiosk anchor's standing eye: every visible text on each page (header, page, tabs, footer,
        /// buttons) subtends at least 1.2 degrees of cap height. The Choose a Case page (the shared case selector) is reported only.
        /// </summary>
        static void KioskTextCheck(ChamberStation station, string name)
        {
            var panel = station.Panel;
            var eye = station.AnchorOf(StationLayout.Station.Kiosk).position + Vector3.up * ChamberStation.Eye;
            var casePage = station.PanelCanvas.Find("Body/CasePage");
            var menu = station.PanelCanvas.Find("Body/MenuSheet");
            var modal = station.PanelCanvas.Find("Modal");
            var page = panel.Page;
            foreach (var p in new[] { 0, 1, 2, 3 })
            {
                panel.ShowPage(p);
                Canvas.ForceUpdateCanvases();
                var worst = float.MaxValue;
                var worstName = "";
                var count = 0;
                foreach (var text in station.PanelCanvas.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (string.IsNullOrWhiteSpace(text.text) || text.transform.IsChildOf(casePage) || text.transform.IsChildOf(menu) || text.transform.IsChildOf(modal))
                        continue;
                    var angle = CapAngle(text, eye);
                    count++;
                    if (angle < worst)
                    {
                        worst = angle;
                        worstName = $"{text.name} ({F(text.fontSize)} u, '{Flat(text.text).Substring(0, Mathf.Min(24, Flat(text.text).Length))}')";
                    }
                }

                Check(worst >= 1.2f, $"{name}: kiosk {ChamberControlPanel.TabTitles[p]} page - {count} texts, smallest cap height {F(worst)} deg " +
                                     $"from the standing eye at the kiosk anchor ({worstName})");
            }

            panel.ShowPage(page);
            panel.ShowCasePage(true);
            Canvas.ForceUpdateCanvases();
            var caseWorst = station.PanelCanvas.GetComponentsInChildren<TMP_Text>(false).Where(t => t.transform.IsChildOf(casePage) && !string.IsNullOrWhiteSpace(t.text))
                .Select(t => CapAngle(t, eye)).DefaultIfEmpty(0f).Min();
            Line($"  {name}: Choose a Case page (shared selector, scaled to fit) smallest cap height {F(caseWorst)} deg (reported, not gated)");
            panel.ShowCasePage(false);
        }

        /// <summary>Soil Moisture and setting rows: each label is vertically centred on its own value box (canvas space).</summary>
        static void RowAlignmentChecks(ChamberStation station, string name)
        {
            var canvas = station.PanelCanvas;
            float Y(Transform t) => canvas.InverseTransformPoint(Centre((RectTransform)t)).y;
            var soil = canvas.GetComponentsInChildren<Transform>(true).First(t => t.name == "SoilPage");
            var worst = 0f;
            var pairs = new List<string>();
            foreach (var (label, box) in new[] { ("AmountLabel", "AmountBox"), ("CurrentLabel", "CurrentBox"), ("SetpointLabel", "SetpointBox"), ("TargetLabel", "TargetBox") })
            {
                var l = soil.Find(label);
                var b = soil.Find(box);
                var d = Mathf.Abs(Y(l) - Y(b));
                worst = Mathf.Max(worst, d);
                pairs.Add($"{label} {F(Y(l))}/{F(Y(b))}");
            }

            foreach (var row in canvas.GetComponentsInChildren<Transform>(true).Where(t => t.name is "Row1" or "Row2"))
                worst = Mathf.Max(worst, Mathf.Abs(Y(row.Find("Label")) - Y(row.Find("ValueBox"))));
            Check(worst < 0.5f, $"{name}: every label sits on its own value row (max offset {F(worst)} canvas units; soil rows {string.Join(", ", pairs)})");
        }

        /// <summary>The hose runs from the console along the floor and climbs only at the chamber to the rim clamp; the outlet stays on the tray.</summary>
        static void HoseChecks(ChamberStation station, string name)
        {
            var water = station.Water;
            var points = water.HosePoints;
            if (!Check(points != null && points.Count > 4, $"{name}: hose laid ({points?.Count} points)"))
                return;
            var line = station.ChamberLine;
            var chamber = ChamberRect(station);
            var floorRun = 0f;
            var airborne = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                var a = line.InverseTransformPoint(points[i - 1]);
                var b = line.InverseTransformPoint(points[i]);
                var length = (b - a).magnitude;
                var inFront = a.z < chamber.yMin - 0.15f && b.z < chamber.yMin - 0.15f;
                if (Mathf.Max(a.y, b.y) <= 0.06f)
                    floorRun += length;
                else if (inFront && Mathf.Min(a.y, b.y) > 0.25f)
                    airborne += length;
            }

            station.Chamber.SoilArea(out var min, out var max);
            var tip = water.OutletTip;
            Check(floorRun > 0.5f && airborne < 0.05f && tip.x > min.x && tip.x < max.x && tip.z > min.y && tip.z < max.y,
                $"{name}: hose {F(floorRun)} m along the floor, none hanging in the walkway; gooseneck outlet over the tray");
        }

        /// <summary>The plant, chamber and tray (the fixed anchor) are where they were before Addendum 6.</summary>
        static void ChamberAnchorDump(ChamberStation station, string spec)
        {
            var c = station.Chamber;
            var t = c.transform;
            string Vx(Vector3 v) => v.x.ToString("0.00000", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.00000", CultureInfo.InvariantCulture) + "," +
                                   v.z.ToString("0.00000", CultureInfo.InvariantCulture);
            var ext = new Bounds(t.TransformPoint(c.ExteriorBounds.center), c.ExteriorBounds.size);
            var text = spec + " kind=" + c.Layout.Kind + " line=" + Vx(station.ChamberLine.position) + " chamberPos=" + Vx(t.position) + " chamberRot=" + Vx(t.eulerAngles) +
                       " chamberScale=" + Vx(t.lossyScale) + " exterior=" + Vx(c.Layout.Exterior) + " extCenter=" + Vx(ext.center) + " extSize=" + Vx(ext.size) +
                       " specimenPos=" + Vx(station.Specimen.transform.position) + " specimenRot=" + Vx(station.Specimen.transform.eulerAngles) +
                       " specimenScale=" + Vx(station.Specimen.transform.lossyScale) + " soilY=" + c.SoilWorldY.ToString("0.00000", CultureInfo.InvariantCulture) +
                       " window=" + Vx(c.WindowCentre);
            AnchorLines.Add(text);
            var before = ArgValue("-anchorBefore");
            if (before == null || !File.Exists(before))
                return;
            var match = File.ReadAllLines(before).FirstOrDefault(l => l.StartsWith(spec + " "));
            Check(match != null && match.Trim() == text.Trim(), $"{spec}: chamber transform and exterior bounds identical to before Addendum 6" +
                                                                (match != null && match.Trim() != text.Trim() ? $"\n    before {match}\n    after  {text}" : ""));
        }

        static readonly List<string> AnchorLines = new List<string>();

        /// <summary>XRBootstrap: the tracked hands drive input only; no hand mesh can render (Addendum 5, re-checked for Addendum 6).</summary>
        static void VerifyBootstrapHands()
        {
            var scene = EditorSceneManager.OpenScene(ProjectPaths.BootstrapScene, OpenSceneMode.Additive);
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
                var visualizers = all.Where(t => t.name == "Hand Visualizer").ToList();
                var handRoots = all.Where(t => t.name.Contains("Hand") && (t.name.EndsWith("QuestVisual") || t.name.EndsWith("Quest Visual") ||
                                                                          t.name.EndsWith("AndroidXRVisual") || t.name.EndsWith("Android XR Visual"))).ToList();
                var meshes = all.SelectMany(t => t.GetComponents<SkinnedMeshRenderer>()).Where(m => m.name.Contains("Hand")).ToList();
                Check(visualizers.Count > 0 && visualizers.All(t => !t.gameObject.activeSelf), $"XRBootstrap: {visualizers.Count} Hand Visualizer(s), all inactive");
                Check(handRoots.All(t => !t.gameObject.activeInHierarchy) && meshes.All(m => !m.gameObject.activeInHierarchy),
                    $"XRBootstrap: {handRoots.Count} hand-visual roots and {meshes.Count} hand skinned meshes, none active in the saved rig");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ------------------------------------------------------------------ live API

        static void VerifyLiveApi()
        {
            if (!liveUp)
            {
                Line("Live API checks skipped: the backend is not reachable.");
                return;
            }

            Line("Live API: profile + evaluate with start values and with target values");
            foreach (var (plant, disease) in new[] { ("tomato", "early_blight"), ("corn", "common_rust"), ("apple", "apple_scab"), ("rice", "brown_spot") })
            {
                var p = live.GetCaseBlocking(plant, disease, "standard");
                if (!Check(p.Ok, $"{plant}/{disease}: profile {p.Status} {p.Error}"))
                    continue;
                var profile = p.Value;
                Line($"  {profile.Plant} / {profile.Disease} v{profile.ProfileVersion} ({profile.Scenario}); nutrient {profile.Nutrient ?? "none"}; leaf wetness start {F(profile.LeafWetnessStart)}");
                var start = live.EvaluateBlocking(profile, TreatmentSettings.FromStart(profile));
                Check(start.Ok, $"  start values: score {F(start.Ok ? start.Value.Score : double.NaN)} passed {start.Value?.Passed} '{start.Value?.Feedback}'");
                var target = TreatmentSettings.FromStart(profile);
                foreach (var factor in profile.Factors)
                foreach (var sub in factor.Subfactors)
                {
                    if (sub.IsChoice)
                        target.SetChoice(factor.Id, sub.Id, sub.Targets.Contains("rootZone") ? "rootZone" : sub.Targets.FirstOrDefault() ?? sub.StartChoice);
                    else
                        target.Set(sub, factor.Id, (sub.TargetMin + sub.TargetMax) * 0.5);
                }

                var best = live.EvaluateBlocking(profile, target);
                Check(best.Ok && best.Value.Passed, $"  target values: score {F(best.Ok ? best.Value.Score : double.NaN)} passed {best.Value?.Passed} outcome {best.Value?.OutcomeLabel}");
            }

            var missing = live.GetCaseBlocking("tomato", "no_such_disease", "standard");
            Check(missing.NotFound, $"unknown case -> 404 ({missing.Status})");
            var tomato = live.GetCaseBlocking("tomato", "early_blight", "standard").Value;
            if (tomato != null)
            {
                var stale = new TreatmentProfile();
                stale.Factors.AddRange(tomato.Factors);
                var json = TreatmentSettings.FromStart(tomato).ToEvaluationJson(tomato).Replace("\"profileVersion\":1", "\"profileVersion\":99");
                Check(json.Contains("\"applications\":0,") || json.Contains("\"applications\":0}"), "applications sent as an integer");
                Line("  evaluation body: " + TreatmentSettings.FromStart(tomato).ToEvaluationJson(tomato));
            }
        }

        // ------------------------------------------------------------------ rendering

        const int RenderWidth = 1672, RenderHeight = 941;
        const float FieldOfView = 62f;

        /// <summary>The standing eye at a station's anchor, looking at <paramref name="target"/>.</summary>
        static Camera StationCamera(ChamberStation station, StationLayout.Station id, Vector3 target, int width, int height)
        {
            var eye = station.AnchorOf(id).position + Vector3.up * ChamberStation.Eye;
            return PoseCamera(eye, Quaternion.LookRotation(target - eye, Vector3.up), width, height);
        }

        static void Render(ChamberStation station, string name, int width = RenderWidth, int height = RenderHeight, bool overview = false,
            StationLayout.Station? at = null, Vector3? target = null)
        {
            var camera = at.HasValue ? StationCamera(station, at.Value, target ?? station.PanelCanvas.position, width, height)
                : overview ? OverviewCamera(station, width, height) : ViewCamera(station, width, height);
            var go = camera.gameObject;
            camera.clearFlags = CameraClearFlags.Skybox;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = texture;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                if (canvas.renderMode == RenderMode.WorldSpace)
                    canvas.worldCamera = camera;
            Canvas.ForceUpdateCanvases();
            foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude))
                text.ForceMeshUpdate();
            camera.Render();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            var path = Path.Combine(outDir, $"chamber_{name}.png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(go);
            Line($"  PNG {path}");
        }
    }
}
