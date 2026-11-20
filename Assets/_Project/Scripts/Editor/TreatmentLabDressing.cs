using System.Collections.Generic;
using System.Linq;
using BTP.Treatment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Furnishes the Treatment lab hall (Treatment.unity) with believable plant-pathology treatment equipment, as pure
    /// decoration under one "Lab Dressing" root that is rebuilt on every run (nothing else in the scene is touched):
    /// a diagnostics bench on the left counter (microscope, petri dishes, scale, pH meter, glassware, incubator, whiteboard
    /// treatment chart, tool pegboard), a spray-equipment wall on the front counter (bench-top fume hood, sink and tap,
    /// knapsack sprayers on hooks, watering can, hose reel), a PPE and first-aid corner, a colour-coded chemical store
    /// (labelled fungicides, bactericides, fertilisers and bio-controls, jerry cans, safety cabinet, sacks on a pallet),
    /// a seedling nursery along the back window and free-standing islands (spray mixing, sample prep, propagation, growth
    /// cabinet, quarantine). Plants are the project's crop models as young potted plants (diseased where the zone holds
    /// infected stock) and props, shelving and surfaces use CC0 Poly Haven / ambientCG assets where available (see
    /// TreatmentLabDressing.Real.cs and Assets/Lab_Treatment_Assets/Lab/ATTRIBUTION.md), with modelled fallbacks.
    /// All of it is static, shares a small material palette and keeps clear of the chamber, the stations, their
    /// approach zones and anchors, the spawn and the door swing; floor-standing zones are re-checked at runtime by
    /// <see cref="LabDressingClearance"/> when the chamber is fitted to a larger plant.
    /// World space: the hall interior spans X -7.4..6.5, Z -6.0..10.4 (wall counters 0.94 m high along the left, front and back walls).
    /// </summary>
    public static partial class TreatmentLabDressing
    {
        public const string RootName = "Lab Dressing";

        // Hall interior and its existing counters (world space, see TreatmentChamberBuilder.BuildHall).
        const float WallLeft = -7.4f, WallRight = 6.5f, WallFront = -6.0f, WallBack = 10.4f;
        const float CounterTop = 0.94f, CounterDepth = 0.7f;
        const float BenchHeight = 0.9f;

        static readonly List<LabDressingClearance.Zone> Zones = new List<LabDressingClearance.Zone>();

        [MenuItem("BTP/Setup/Furnish Treatment Lab", priority = 60)]
        public static void Furnish() => Furnish(true);

        /// <summary>Builds (or rebuilds) the dressing in Treatment.unity and saves it; closes the scene again if it opened it and <paramref name="closeIfOpened"/>.</summary>
        public static string Furnish(bool closeIfOpened)
        {
            if (EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Exit Play mode before furnishing the Treatment lab.");

            var scene = SceneManager.GetSceneByPath(ProjectPaths.TreatmentScene);
            var opened = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ProjectPaths.TreatmentScene, OpenSceneMode.Additive);
                opened = true;
            }

            foreach (var old in scene.GetRootGameObjects().Where(r => r.name == RootName).ToList())
                Object.DestroyImmediate(old);

            var line = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "ChamberLine");
            var station = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ChamberStation>(true)).FirstOrDefault();
            var chamber = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TreatmentChamber>(true)).FirstOrDefault();
            if (line == null || station == null || chamber == null)
                throw new System.InvalidOperationException("Treatment.unity has no chamber hall; run BTP/Treatment/3 - Build Treatment Scene first.");

            MeshCache.Clear();
            Zones.Clear();
            PrepareLabAssets();
            pal = new Palette();
            healthyIndex = sickIndex = 0;

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            GameObjectUtility.SetStaticEditorFlags(root, PropFlags);
            var t = root.transform;

            DiagnosticsBench(t);
            SprayWall(t);
            PpeCorner(t);
            ChemicalStore(t);
            Nursery(t);
            MixingIsland(t);
            TrolleyAndBins(t);
            PrepIsland(t);
            PropagationIsland(t);
            GrowthCabinet(t);
            Quarantine(t);

            var report = Validate(root, line, station, chamber);
            var clearance = root.AddComponent<LabDressingClearance>();
            clearance.Bind(station, chamber, line, new List<LabDressingClearance.Zone>(Zones));

            var triangles = Triangles(root);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var summary = $"[BTP] Lab dressing: {root.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, {triangles} triangles, " +
                          $"{Zones.Count} floor zones. {report}";
            Debug.Log(summary);
            if (opened && closeIfOpened)
                EditorSceneManager.CloseScene(scene, true);
            return summary;
        }

        /// <summary>Triangles drawn by the dressing at full detail (TextMeshPro quads and lower LOD levels excluded).</summary>
        public static long Triangles(GameObject root)
        {
            var lowerLods = new HashSet<Renderer>();
            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                for (var i = 1; i < lods.Length; i++)
                    lowerLods.UnionWith(lods[i].renderers.Where(r => r != null));
            }

            long total = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<TMPro.TMP_Text>() != null || lowerLods.Contains(filter.GetComponent<Renderer>()))
                    continue;
                for (var s = 0; s < filter.sharedMesh.subMeshCount; s++)
                    total += filter.sharedMesh.GetIndexCount(s) / 3;
            }

            return total;
        }

        /// <summary>
        /// Removes any floor zone that already conflicts with the scene's current chamber layout (none should) and reports
        /// the zones that a chamber grown to its reserved maximum would hide at runtime.
        /// </summary>
        static string Validate(GameObject root, Transform line, ChamberStation station, TreatmentChamber chamber)
        {
            var exterior = chamber.HasLayout ? chamber.Layout.Exterior : ChamberFit.Default().Exterior;
            var current = StationLayout.Compute(exterior.x, exterior.z, station.HallRect, ChamberStation.KioskAnchorLocal);
            var largest = StationLayout.Compute(ChamberFit.ReservedExterior.x, ChamberFit.ReservedExterior.z, station.HallRect, ChamberStation.KioskAnchorLocal);
            var probe = root.AddComponent<LabDressingClearance>();
            probe.Bind(station, chamber, line, null);
            var hinge = Vector2.zero;
            var radius = 0f;
            if (chamber.HasLayout)
            {
                var h = line.InverseTransformPoint(chamber.DoorHingeWorld);
                var e = line.InverseTransformPoint(chamber.DoorEdgeWorld);
                hinge = new Vector2(h.x, h.z);
                radius = new Vector2(e.x - h.x, e.z - h.z).magnitude + LabDressingClearance.DoorMargin;
            }

            var removed = new List<string>();
            var wouldHide = new List<string>();
            foreach (var zone in Zones.ToList())
            {
                var footprint = probe.ToLine(zone);
                if (LabDressingClearance.Blocks(footprint, current, hinge, radius))
                {
                    removed.Add(zone.Root.name);
                    Debug.LogWarning($"[BTP] Lab dressing: '{zone.Root.name}' is in the way of the current chamber layout and was left out.");
                    Object.DestroyImmediate(zone.Root);
                    Zones.Remove(zone);
                }
                else if (LabDressingClearance.Blocks(footprint, largest))
                    wouldHide.Add(zone.Root.name);
            }

            Object.DestroyImmediate(probe);
            var spawn = line.TransformPoint(new Vector3(current.Overview.x, 0f, current.Overview.y));
            return $"Chamber {exterior.x:0.00} x {exterior.z:0.00} m, spawn at ({spawn.x:0.00}, {spawn.z:0.00}); " +
                   $"left out now: {(removed.Count == 0 ? "none" : string.Join(", ", removed))}; " +
                   $"hidden at runtime if the chamber grows to {ChamberFit.ReservedExterior.x:0.0} m: {(wouldHide.Count == 0 ? "none" : string.Join(", ", wouldHide))}.";
        }

        /// <summary>Registers a floor-standing unit (world-space footprint) for the runtime clearance check.</summary>
        static void Zone(Transform unit, float minX, float minZ, float maxX, float maxZ) =>
            Zones.Add(new LabDressingClearance.Zone { Root = unit.gameObject, Min = new Vector2(minX, minZ), Max = new Vector2(maxX, maxZ) });

        // ------------------------------------------------------------------ zone A: diagnostics bench (left wall counter)

        /// <summary>
        /// The left wall counter as a diagnostics bench. Local frame: X runs along the counter (= world Z), +Z is the wall,
        /// the top is y = 0 and the user stands on the -Z side.
        /// </summary>
        static void DiagnosticsBench(Transform root)
        {
            var bench = Group("DiagnosticsBench", root, new Vector3(WallLeft + CounterDepth * 0.5f, CounterTop, 0f), -90f);
            const float wallZ = CounterDepth * 0.5f;

            if (Real("Microscope", bench, new Vector3(-1.95f, 0f, 0.04f), 180f, 0.42f) == null)
                Microscope(Group("Microscope", bench, new Vector3(-1.95f, 0f, 0.02f)));
            Decor("fx_lab_result", bench, new Vector3(-1.5f, 0.02f, -0.18f), 0f);
            Real("BunsenBurner", bench, new Vector3(-1.48f, 0f, 0.17f), 0f, 0.16f);

            // Petri dishes: a stack and three open plates with cultures.
            var petri = Group("PetriDishes", bench, new Vector3(-1.15f, 0f, 0f));
            for (var i = 0; i < 5; i++)
                Lathe("Dish", petri, "Petri", PetriProfile, pal.Glass, new Vector3(-0.06f, i * 0.013f, 0.1f));
            var cultures = new[] { pal.Paper, pal.Organic, pal.LeafSick };
            for (var i = 0; i < 3; i++)
            {
                var p = new Vector3(0.02f + (i % 2) * 0.1f, 0f, -0.05f + i * 0.08f);
                Lathe("Dish", petri, "Petri", PetriProfile, pal.Glass, p);
                Cyl("Culture", petri, p + Vector3.up * 0.002f, 0.04f, 0.004f, cultures[i], 10);
            }

            DigitalScale(Group("DigitalScale", bench, new Vector3(-0.7f, 0f, 0.02f)), "12.50 g");
            PhMeter(Group("PhMeter", bench, new Vector3(-0.32f, 0f, -0.02f)));

            // Glassware: beakers with coloured solutions, measuring cylinders, a conical flask.
            var glass = Group("Glassware", bench, new Vector3(0.12f, 0f, 0f));
            var liquids = new[] { pal.LiquidBlue, pal.LiquidAmber, pal.Fertiliser };
            for (var i = 0; i < 3; i++)
            {
                var p = new Vector3(i * 0.1f, 0f, -0.08f + (i % 2) * 0.12f);
                Lathe("Beaker", glass, "Beaker", BeakerProfile, pal.Glass, p, 1f, 12, false);
                Cyl("Solution", glass, p + Vector3.up * 0.004f, 0.036f, 0.05f + i * 0.015f, liquids[i], 10);
            }

            for (var i = 0; i < 2; i++)
            {
                var p = new Vector3(0.36f + i * 0.07f, 0f, 0.1f);
                Cyl("CylinderFoot", glass, p, 0.032f, 0.012f, pal.Glass, 6);
                Lathe("MeasuringCylinder", glass, "MeasuringCylinder", CylinderGlassProfile, pal.Glass, p, 1f, 10, false);
                Cyl("Solution", glass, p + Vector3.up * 0.014f, 0.016f, 0.12f + i * 0.05f, liquids[i], 8);
            }

            Lathe("ConicalFlask", glass, "Flask", FlaskProfile, pal.Glass, new Vector3(0.42f, 0f, -0.1f), 1f, 12, false);
            Cyl("Solution", glass, new Vector3(0.42f, 0.004f, -0.1f), 0.045f, 0.025f, pal.LiquidBlue, 10);

            // Pipette stand with four pipettes.
            var pipettes = Group("PipetteStand", bench, new Vector3(0.82f, 0f, 0.08f));
            Box("Base", pipettes, new Vector3(0f, 0.01f, 0f), new Vector3(0.16f, 0.02f, 0.1f), pal.White, 0.004f);
            Box("Upright", pipettes, new Vector3(0f, 0.15f, 0.035f), new Vector3(0.02f, 0.28f, 0.02f), pal.White, 0.004f);
            Box("Rack", pipettes, new Vector3(0f, 0.22f, 0.01f), new Vector3(0.16f, 0.02f, 0.06f), pal.White, 0.004f);
            for (var i = 0; i < 4; i++)
            {
                Rod("Pipette", pipettes, new Vector3(-0.06f + i * 0.04f, 0.03f, 0.005f), new Vector3(-0.06f + i * 0.04f, 0.3f, 0.005f), 0.004f, pal.Glass, 6);
                Cyl("Bulb", pipettes, new Vector3(-0.06f + i * 0.04f, 0.3f, 0.005f), 0.008f, 0.03f, i % 2 == 0 ? pal.Bactericide : pal.Fungicide, 6);
            }

            // Labelled sample jars (leaf samples in for diagnosis).
            var jars = Group("SampleJars", bench, new Vector3(1.32f, 0f, 0f));
            Box("Tray", jars, new Vector3(0f, 0.008f, 0f), new Vector3(0.36f, 0.016f, 0.24f), pal.White, 0.004f);
            for (var i = 0; i < 6; i++)
            {
                var p = new Vector3(-0.11f + (i % 3) * 0.11f, 0.016f, -0.055f + (i / 3) * 0.11f);
                Lathe("Jar", jars, "Jar", JarProfile, pal.Glass, p);
                Cyl("Sample", jars, p + Vector3.up * 0.004f, 0.025f, 0.035f, i % 3 == 1 ? pal.LeafSick : pal.Leaf, 8);
                Cyl("Lid", jars, p + Vector3.up * 0.09f, 0.028f, 0.012f, pal.Fungicide, 10);
            }

            StickOn(jars, "SAMPLES IN", new Vector3(0f, 0.008f, -0.121f), new Vector2(0.2f, 0.014f), 0.012f);

            // Whiteboard with the treatment chart on the wall above the bench.
            Whiteboard(Group("Whiteboard", bench, new Vector3(1.05f, 0f, wallZ)));

            // Two supplied spray bottles and the benchtop incubator.
            SprayBottle(bench, new Vector3(2.3f, 0f, 0.05f), 20f);
            SprayBottle(bench, new Vector3(2.5f, 0f, 0.12f), -35f);
            Incubator(Group("Incubator", bench, new Vector3(3.3f, 0f, 0.04f)));

            // Amber reagent bottles under a caution sign.
            var reagents = Group("ReagentBottles", bench, new Vector3(4.3f, 0f, 0.05f));
            for (var i = 0; i < 5; i++)
            {
                var p = new Vector3(-0.2f + i * 0.09f, 0f, (i % 2) * 0.07f);
                Lathe("Reagent", reagents, "Reagent", ReagentProfile, pal.AmberGlass, p);
                Cyl("Cap", reagents, p + Vector3.up * 0.155f, 0.015f, 0.02f, pal.Rubber, 8);
            }

            Box("ShelfEdge", reagents, new Vector3(0f, 0.015f, -0.2f), new Vector3(0.3f, 0.03f, 0.004f), pal.White, 0f);
            StickOn(reagents, "REAGENTS", new Vector3(0f, 0.015f, -0.204f), new Vector2(0.2f, 0.02f), 0.014f);
            Sign("CautionSign", bench, "CAUTION\nPLANT PATHOGENS\nWASH HANDS AFTER HANDLING SAMPLES", new Vector3(4.3f, 0.95f, wallZ), new Vector2(0.62f, 0.34f),
                pal.Safety, Ink, 0.06f);

            // Diseased samples waiting for diagnosis: three potted plants on a drip tray.
            var waiting = Group("SamplePlants", bench, new Vector3(5.5f, 0f, 0f));
            Box("DripTray", waiting, new Vector3(0f, 0.01f, 0f), new Vector3(0.62f, 0.02f, 0.3f), pal.Rubber, 0.005f);
            for (var i = 0; i < 3; i++)
                PottedSeedling(waiting, new Vector3(-0.2f + i * 0.2f, 0.02f, 0f), true, 1.5f);
            StickOn(waiting, "AWAITING DIAGNOSIS", new Vector3(0f, 0.012f, -0.152f), new Vector2(0.3f, 0.016f), 0.012f);

            // Pegboard of hand tools (supplied gardening tools) on the wall, pruning shears below it.
            Pegboard(Group("ToolPegboard", bench, new Vector3(7.3f, 0f, wallZ)));
            PruningShears(bench, new Vector3(7.15f, 0f, -0.08f), 25f);
        }

        static void Microscope(Transform m)
        {
            Box("Base", m, new Vector3(0f, 0.02f, 0.02f), new Vector3(0.18f, 0.04f, 0.22f), pal.White, 0.01f);
            Box("Arm", m, new Vector3(0f, 0.17f, 0.09f), new Vector3(0.05f, 0.27f, 0.06f), pal.White, 0.01f, new Vector3(-12f, 0f, 0f));
            Box("Stage", m, new Vector3(0f, 0.13f, -0.01f), new Vector3(0.14f, 0.012f, 0.12f), pal.Rubber, 0.003f);
            Box("Slide", m, new Vector3(0f, 0.138f, -0.02f), new Vector3(0.075f, 0.002f, 0.026f), pal.Glass, 0f);
            Cyl("Lamp", m, new Vector3(0f, 0.04f, -0.01f), 0.025f, 0.03f, pal.Rubber, 10);
            Cyl("Turret", m, new Vector3(0f, 0.2f, 0.0f), 0.035f, 0.025f, pal.Stainless, 10);
            for (var i = 0; i < 3; i++)
                Rod("Objective", m, new Vector3(Mathf.Sin(i * 2.1f) * 0.02f, 0.2f, Mathf.Cos(i * 2.1f) * 0.02f),
                    new Vector3(Mathf.Sin(i * 2.1f) * 0.025f, 0.165f, Mathf.Cos(i * 2.1f) * 0.025f - 0.005f), 0.008f, pal.Stainless, 8);
            Rod("Tube", m, new Vector3(0f, 0.225f, 0.0f), new Vector3(0f, 0.31f, -0.06f), 0.022f, pal.White, 10);
            Rod("EyepieceL", m, new Vector3(-0.022f, 0.3f, -0.05f), new Vector3(-0.03f, 0.35f, -0.1f), 0.011f, pal.Rubber, 8);
            Rod("EyepieceR", m, new Vector3(0.022f, 0.3f, -0.05f), new Vector3(0.03f, 0.35f, -0.1f), 0.011f, pal.Rubber, 8);
            Rod("FocusKnob", m, new Vector3(-0.05f, 0.09f, 0.08f), new Vector3(0.05f, 0.09f, 0.08f), 0.02f, pal.Rubber, 10);
        }

        static void DigitalScale(Transform s, string reading)
        {
            Box("Body", s, new Vector3(0f, 0.025f, 0f), new Vector3(0.22f, 0.05f, 0.26f), pal.White, 0.01f);
            Cyl("Pan", s, new Vector3(0f, 0.05f, 0.03f), 0.08f, 0.006f, pal.Stainless, 16);
            Box("Display", s, new Vector3(0f, 0.04f, -0.105f), new Vector3(0.12f, 0.03f, 0.05f), pal.Rubber, 0.003f, new Vector3(-25f, 0f, 0f));
            Text(s, reading, new Vector3(0f, 0.046f, -0.117f), new Vector2(0.1f, 0.022f), new Color(0.55f, 1f, 0.7f), 0.018f, euler: new Vector3(65f, 0f, 0f));
        }

        static void PhMeter(Transform p)
        {
            Box("Meter", p, new Vector3(0f, 0.018f, 0f), new Vector3(0.08f, 0.036f, 0.16f), pal.Safety, 0.008f, new Vector3(0f, 15f, 0f));
            Box("Screen", p, new Vector3(0.01f, 0.037f, 0.02f), new Vector3(0.05f, 0.003f, 0.05f), pal.Rubber, 0f, new Vector3(0f, 15f, 0f));
            Text(p, "pH 6.8", new Vector3(0.01f, 0.04f, 0.02f), new Vector2(0.045f, 0.02f), new Color(0.55f, 1f, 0.7f), 0.012f, euler: new Vector3(90f, 15f, 0f));
            Lathe("Beaker", p, "Beaker", BeakerProfile, pal.Glass, new Vector3(0.12f, 0f, 0.06f), 0.8f, 12, false);
            Cyl("Buffer", p, new Vector3(0.12f, 0.003f, 0.06f), 0.029f, 0.05f, pal.LiquidBlue, 10);
            Rod("Probe", p, new Vector3(0.12f, 0.01f, 0.06f), new Vector3(0.125f, 0.16f, 0.065f), 0.006f, pal.Rubber, 6);
            Rod("Cable", p, new Vector3(0.125f, 0.16f, 0.065f), new Vector3(0.02f, 0.03f, 0.08f), 0.003f, pal.Rubber, 4);
        }

        static void Incubator(Transform i)
        {
            Box("Body", i, new Vector3(0f, 0.31f, 0f), new Vector3(0.55f, 0.62f, 0.5f), pal.White, 0.015f, shadows: true);
            Box("Door", i, new Vector3(0f, 0.31f, -0.255f), new Vector3(0.5f, 0.56f, 0.02f), pal.White, 0.008f);
            Box("Window", i, new Vector3(0f, 0.34f, -0.266f), new Vector3(0.36f, 0.34f, 0.004f), pal.Glass, 0f);
            Box("ShelfInside", i, new Vector3(0f, 0.3f, -0.1f), new Vector3(0.44f, 0.01f, 0.3f), pal.Stainless, 0f);
            for (var k = 0; k < 3; k++)
                Lathe("Plate", i, "Petri", PetriProfile, pal.Glass, new Vector3(-0.12f + k * 0.12f, 0.305f, -0.1f));
            Rod("Handle", i, new Vector3(0.21f, 0.2f, -0.28f), new Vector3(0.21f, 0.42f, -0.28f), 0.008f, pal.Stainless, 6);
            Box("Panel", i, new Vector3(0f, 0.585f, -0.266f), new Vector3(0.3f, 0.05f, 0.004f), pal.Rubber, 0f);
            Text(i, "INCUBATOR  25.0 °C", new Vector3(0f, 0.585f, -0.27f), new Vector2(0.28f, 0.04f), new Color(0.55f, 1f, 0.7f), 0.022f);
        }

        /// <summary>Wall whiteboard (local origin on the wall surface, board facing -Z) with the week's treatment chart.</summary>
        static void Whiteboard(Transform w)
        {
            const float width = 1.7f, height = 0.95f, bottom = 0.42f;
            var centre = new Vector3(0f, bottom + height * 0.5f, -0.015f);
            Box("Frame", w, centre, new Vector3(width + 0.04f, height + 0.04f, 0.02f), pal.Stainless, 0.004f);
            Box("Board", w, centre + new Vector3(0f, 0f, -0.011f), new Vector3(width, height, 0.002f), pal.White, 0f);
            Box("MarkerTray", w, new Vector3(0f, bottom - 0.02f, -0.04f), new Vector3(0.9f, 0.015f, 0.05f), pal.Stainless, 0f);
            for (var i = 0; i < 3; i++)
                Rod("Marker", w, new Vector3(-0.2f + i * 0.1f, bottom - 0.005f, -0.04f), new Vector3(-0.08f + i * 0.1f, bottom - 0.005f, -0.04f), 0.007f,
                    i == 0 ? pal.SignBlue : i == 1 ? pal.Bactericide : pal.Fertiliser, 6);
            Text(w, "<b>TREATMENT CHART</b>  week 46", centre + new Vector3(0f, height * 0.41f, -0.014f), new Vector2(width * 0.92f, 0.07f), new Color(0.05f, 0.25f, 0.55f), 0.055f);
            const string chart =
                "<b>Disease<pos=33%>Treatment<pos=70%>Dose<pos=86%>Every</b>\n" +
                "Early blight (tomato)<pos=33%>Mancozeb 75 WP<pos=70%>2.5 g/L<pos=86%>7 days\n" +
                "Late blight (potato)<pos=33%>Copper oxychloride 50 WP<pos=70%>3 g/L<pos=86%>10 days\n" +
                "Bacterial spot (pepper)<pos=33%>Streptocycline + copper<pos=70%>0.1 g/L<pos=86%>10 days\n" +
                "Powdery mildew (squash)<pos=33%>Wettable sulphur 80 WP<pos=70%>2 g/L<pos=86%>10 days\n" +
                "Leaf curl virus (chilli)<pos=33%>Neem oil (vector control)<pos=70%>5 mL/L<pos=86%>7 days\n" +
                "<color=#B3241F>Always: PPE on - 24 h re-entry - log every spray</color>";
            var text = Text(w, chart, centre + new Vector3(0f, -0.05f, -0.014f), new Vector2(width * 0.92f, height * 0.72f), new Color(0.1f, 0.1f, 0.12f), 0.042f,
                TMPro.FontStyles.Normal, TMPro.TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        }

        /// <summary>Wall pegboard (origin on the wall surface) holding hand tools from the supplied gardening set.</summary>
        static void Pegboard(Transform p)
        {
            const float width = 1.7f, height = 0.85f, bottom = 0.3f;
            Box("Board", p, new Vector3(0f, bottom + height * 0.5f, -0.012f), new Vector3(width, height, 0.02f), pal.Painted, 0.004f);
            Text(p, "HAND TOOLS - CLEAN AFTER USE", new Vector3(0f, bottom + height - 0.05f, -0.024f), new Vector2(width * 0.9f, 0.06f), Color.white, 0.04f);
            var tools = new[] { ("garden secateurs", 0.22f), ("garden shovel", 0.32f), ("landing fork", 0.3f), ("knife", 0.25f), ("sickle", 0.38f), ("spatula__", 0.3f) };
            for (var i = 0; i < tools.Length; i++)
            {
                var x = -width * 0.5f + 0.17f + i * (width - 0.34f) / (tools.Length - 1);
                Rod("Hook", p, new Vector3(x, bottom + height - 0.15f, -0.02f), new Vector3(x, bottom + height - 0.15f, -0.07f), 0.004f, pal.Stainless, 6);
                var tool = GlbMesh(p, GardeningToolsGlb, tools[i].Item1, tools[i].Item2, Pose.Hung, Vector3.zero, 0f);
                if (tool == null)
                    continue;
                // Hang it from the hook: its top just under the hook, against the board.
                tool.localPosition = new Vector3(x, bottom + height - 0.13f - tools[i].Item2, -0.045f);
            }
        }

        // ------------------------------------------------------------------ zone B: spray equipment wall (front counter)

        /// <summary>
        /// The front wall counter behind the spawn. Local frame: +Z is the wall, X = -world X, the top is y = 0.
        /// </summary>
        static void SprayWall(Transform root)
        {
            var wall = Group("SprayEquipmentWall", root, new Vector3(0f, CounterTop, WallFront + CounterDepth * 0.5f), 180f);
            const float wallZ = CounterDepth * 0.5f;
            float X(float worldX) => -worldX;

            FumeHood(Group("FumeHood", wall, new Vector3(X(-5.0f), 0f, 0.02f)));
            Sink(Group("Sink", wall, new Vector3(X(-3.25f), 0f, 0.0f)));
            Sign("EyewashSign", wall, "EYE WASH\nat sink", new Vector3(X(-3.25f), 0.9f, wallZ), new Vector2(0.34f, 0.2f), pal.SignGreen, Color.white, 0.05f);

            // Knapsack sprayers on wall hooks, colour-coded by what they spray.
            KnapsackSprayer(Group("KnapsackFungicide", wall, new Vector3(X(-2.15f), 0.3f, wallZ - 0.13f)), pal.Fungicide, "FUNGICIDE ONLY");
            KnapsackSprayer(Group("KnapsackHerbicide", wall, new Vector3(X(-1.45f), 0.3f, wallZ - 0.13f)), pal.Safety, "BIO / NEEM ONLY");
            Sign("SprayerSign", wall, "SPRAY EQUIPMENT\nRinse tank x3 after use", new Vector3(X(-1.8f), 1.42f, wallZ), new Vector2(0.9f, 0.22f), pal.SignBlue,
                Color.white, 0.06f);

            var can = GlbMesh(wall, GardeningToolsGlb, "watering can", 0.5f, Pose.Authored, new Vector3(X(-0.45f), 0f, 0.02f), 200f);
            if (can == null)
                Lathe("WateringCan", wall, "Jug", JugProfile, pal.Fertiliser, new Vector3(X(-0.45f), 0f, 0f), 1.6f);

            // Measuring jugs and a labelled concentrate ready for the mixing bench.
            var mix = Group("MeasuringJugs", wall, new Vector3(X(0.55f), 0f, 0f));
            Lathe("Jug", mix, "Jug", JugProfile, pal.White, new Vector3(-0.1f, 0f, 0.05f), 1f, 12, false);
            Lathe("Jug", mix, "Jug", JugProfile, pal.White, new Vector3(0.08f, 0f, 0.1f), 0.8f, 12, false);
            ChemicalBottle(mix, new Vector3(0.22f, 0f, -0.05f), pal.Organic, "Neem Oil\n1500 ppm");

            HoseReel(Group("HoseReel", wall, new Vector3(X(2.35f), 0.42f, wallZ)));
        }

        static void FumeHood(Transform h)
        {
            const float w = 1.5f, d = 0.62f, ht = 1.15f;
            Box("Worktop", h, new Vector3(0f, 0.015f, 0f), new Vector3(w, 0.03f, d), pal.Stainless, 0.006f, shadows: true);
            Box("SideL", h, new Vector3(-w * 0.5f + 0.04f, ht * 0.5f, 0f), new Vector3(0.08f, ht, d), pal.White, 0.01f, shadows: true);
            Box("SideR", h, new Vector3(w * 0.5f - 0.04f, ht * 0.5f, 0f), new Vector3(0.08f, ht, d), pal.White, 0.01f, shadows: true);
            Box("Back", h, new Vector3(0f, ht * 0.5f, d * 0.5f - 0.02f), new Vector3(w, ht, 0.04f), pal.White, 0.01f);
            Box("Canopy", h, new Vector3(0f, ht - 0.14f, 0f), new Vector3(w, 0.28f, d), pal.White, 0.012f, shadows: true);
            Box("Sash", h, new Vector3(0f, 0.62f, -d * 0.5f + 0.03f), new Vector3(w - 0.16f, 0.5f, 0.012f), pal.Glass, 0f);
            Box("SashHandle", h, new Vector3(0f, 0.38f, -d * 0.5f + 0.015f), new Vector3(w - 0.3f, 0.025f, 0.03f), pal.Stainless, 0.006f);
            Box("Display", h, new Vector3(0.5f, ht - 0.14f, -d * 0.5f - 0.002f), new Vector3(0.24f, 0.1f, 0.004f), pal.Rubber, 0f);
            Text(h, "AIRFLOW 0.5 m/s", new Vector3(0.5f, ht - 0.14f, -d * 0.5f - 0.006f), new Vector2(0.22f, 0.07f), new Color(0.55f, 1f, 0.7f), 0.025f);
            Text(h, "FUME HOOD - mix concentrates here", new Vector3(-0.2f, ht - 0.14f, -d * 0.5f - 0.004f), new Vector2(0.8f, 0.08f), Ink, 0.045f);
            // Duct up to the ceiling (3.2 m) from the 0.94 m counter.
            Cyl("Duct", h, new Vector3(0f, ht, 0.05f), 0.13f, 3.17f - CounterTop - ht, pal.Stainless, 12);
            // Inside: a reagent bottle and a beaker.
            Lathe("Reagent", h, "Reagent", ReagentProfile, pal.AmberGlass, new Vector3(-0.3f, 0.03f, 0.1f));
            Lathe("Beaker", h, "Beaker", BeakerProfile, pal.Glass, new Vector3(0.1f, 0.03f, 0.05f), 1.2f, 12, false);
        }

        static void Sink(Transform s)
        {
            const float w = 0.62f, d = 0.46f, rim = 0.1f;
            Box("Bottom", s, new Vector3(0f, 0.005f, 0f), new Vector3(w, 0.01f, d), pal.Stainless, 0f);
            Box("WallFront", s, new Vector3(0f, rim * 0.5f, -d * 0.5f + 0.01f), new Vector3(w, rim, 0.02f), pal.Stainless, 0.004f);
            Box("WallBack", s, new Vector3(0f, rim * 0.5f, d * 0.5f - 0.01f), new Vector3(w, rim, 0.02f), pal.Stainless, 0.004f);
            Box("WallL", s, new Vector3(-w * 0.5f + 0.01f, rim * 0.5f, 0f), new Vector3(0.02f, rim, d), pal.Stainless, 0.004f);
            Box("WallR", s, new Vector3(w * 0.5f - 0.01f, rim * 0.5f, 0f), new Vector3(0.02f, rim, d), pal.Stainless, 0.004f);
            Cyl("Drain", s, new Vector3(0f, 0.01f, 0f), 0.03f, 0.002f, pal.Rubber, 10);
            // Gooseneck tap with two lever handles.
            Cyl("TapBase", s, new Vector3(0f, 0f, d * 0.5f + 0.06f), 0.025f, 0.05f, pal.Stainless, 10);
            Rod("TapRiser", s, new Vector3(0f, 0.04f, d * 0.5f + 0.06f), new Vector3(0f, 0.32f, d * 0.5f + 0.06f), 0.013f, pal.Stainless, 8);
            Rod("TapSpout", s, new Vector3(0f, 0.32f, d * 0.5f + 0.06f), new Vector3(0f, 0.32f, d * 0.5f - 0.18f), 0.012f, pal.Stainless, 8);
            Rod("TapNozzle", s, new Vector3(0f, 0.33f, d * 0.5f - 0.18f), new Vector3(0f, 0.27f, d * 0.5f - 0.18f), 0.013f, pal.Stainless, 8);
            foreach (var x in new[] { -0.09f, 0.09f })
            {
                Cyl("Valve", s, new Vector3(x, 0f, d * 0.5f + 0.06f), 0.018f, 0.04f, pal.Stainless, 8);
                Rod("Lever", s, new Vector3(x, 0.04f, d * 0.5f + 0.06f), new Vector3(x * 1.7f, 0.05f, d * 0.5f + 0.02f), 0.006f, pal.Stainless, 6);
            }

            // Yellow eyewash head beside the tap.
            Rod("EyewashArm", s, new Vector3(w * 0.5f + 0.06f, 0f, d * 0.5f - 0.05f), new Vector3(w * 0.5f + 0.06f, 0.14f, d * 0.5f - 0.05f), 0.012f, pal.Safety, 8);
            Lathe("EyewashHead", s, "Dome", DomeProfile, pal.Safety, new Vector3(w * 0.5f + 0.06f, 0.14f, d * 0.5f - 0.05f), 0.8f, 10);
        }

        /// <summary>A knapsack sprayer hanging on a wall hook: tank, lid, pump lever, hose and lance, label (origin = tank bottom).</summary>
        static void KnapsackSprayer(Transform k, Material tank, string label)
        {
            Box("Tank", k, new Vector3(0f, 0.27f, 0f), new Vector3(0.36f, 0.54f, 0.2f), tank, 0.04f);
            Cyl("Lid", k, new Vector3(0.05f, 0.54f, -0.01f), 0.065f, 0.035f, pal.Rubber, 12);
            Box("StrapL", k, new Vector3(-0.1f, 0.3f, 0.105f), new Vector3(0.04f, 0.5f, 0.012f), pal.Rubber, 0f);
            Box("StrapR", k, new Vector3(0.1f, 0.3f, 0.105f), new Vector3(0.04f, 0.5f, 0.012f), pal.Rubber, 0f);
            Rod("PumpLever", k, new Vector3(-0.2f, 0.05f, 0f), new Vector3(-0.2f, 0.5f, 0.0f), 0.01f, pal.Rubber, 6);
            Rod("Hose", k, new Vector3(0.18f, 0.06f, -0.02f), new Vector3(0.24f, 0.05f, -0.06f), 0.01f, pal.Rubber, 6);
            Rod("Lance", k, new Vector3(0.24f, 0.05f, -0.06f), new Vector3(0.24f, 0.72f, -0.06f), 0.008f, pal.Stainless, 6);
            Rod("Nozzle", k, new Vector3(0.24f, 0.72f, -0.06f), new Vector3(0.24f, 0.76f, -0.08f), 0.01f, pal.Rubber, 6);
            Rod("Hook", k, new Vector3(0f, 0.6f, 0.13f), new Vector3(0f, 0.6f, 0.04f), 0.008f, pal.Stainless, 6);
            StickOn(k, label, new Vector3(0f, 0.3f, -0.102f), new Vector2(0.26f, 0.05f), 0.025f);
        }

        /// <summary>Wall-mounted hose reel (origin on the wall surface): the scanned hose on its bracket, or a modelled reel.</summary>
        static void HoseReel(Transform h)
        {
            if (Real("HoseReel", h, new Vector3(0f, -0.1f, 0f), 0f, 0.62f, anchor: Anchor.Wall, name: "Hose") != null)
                return;
            Box("Bracket", h, new Vector3(0f, 0.25f, -0.02f), new Vector3(0.36f, 0.5f, 0.04f), pal.Painted, 0.006f);
            for (var i = 0; i < 3; i++)
                Rod("Coil", h, new Vector3(0f, 0.25f, -0.06f - i * 0.045f), new Vector3(0f, 0.25f, -0.1f - i * 0.045f), 0.2f - i * 0.004f, pal.Fertiliser, 16);
            Rod("Hub", h, new Vector3(0f, 0.25f, -0.04f), new Vector3(0f, 0.25f, -0.21f), 0.06f, pal.Rubber, 10);
            Rod("HoseEnd", h, new Vector3(0.18f, 0.12f, -0.12f), new Vector3(0.2f, -0.2f, -0.14f), 0.012f, pal.Fertiliser, 6);
            Rod("Gun", h, new Vector3(0.2f, -0.2f, -0.14f), new Vector3(0.22f, -0.32f, -0.12f), 0.018f, pal.Safety, 6);
        }

        // ------------------------------------------------------------------ zone C: PPE and first aid (front wall, right of the counter)

        static void PpeCorner(Transform root)
        {
            // Local frame on the front wall surface: X = -world X, -Z into the room.
            var corner = Group("PpeStation", root, new Vector3(0f, 0f, WallFront), 180f);
            float X(float worldX) => -worldX;

            var board = Group("PpeBoard", corner, new Vector3(X(4.8f), 0f, 0f));
            Box("Backboard", board, new Vector3(0f, 1.6f, -0.012f), new Vector3(1.5f, 1.3f, 0.02f), pal.White, 0.006f);
            Sign("PpeSign", board, "PPE REQUIRED\nin the treatment lab", new Vector3(0f, 2.12f, -0.022f), new Vector2(1.1f, 0.2f), pal.SignBlue, Color.white, 0.07f);
            // Three aprons on hooks (left), goggles and respirators on a shelf, glove boxes and work gloves below (right).
            var apronColours = new[] { pal.White, pal.Fertiliser, pal.Safety };
            for (var i = 0; i < 3; i++)
            {
                var x = -0.55f + i * 0.2f;
                Rod("Hook", board, new Vector3(x, 1.95f, -0.02f), new Vector3(x, 1.95f, -0.08f), 0.006f, pal.Stainless, 6);
                Box("Apron", board, new Vector3(x, 1.52f, -0.05f), new Vector3(0.34f, 0.82f, 0.012f), apronColours[i], 0f, new Vector3(4f, 0f, i * 2f - 2f));
                Box("Neck", board, new Vector3(x, 1.94f, -0.05f), new Vector3(0.16f, 0.04f, 0.01f), apronColours[i], 0f);
            }

            Box("ShelfTop", board, new Vector3(0.38f, 1.62f, -0.11f), new Vector3(0.6f, 0.02f, 0.2f), pal.Stainless, 0.004f);
            Box("ShelfLow", board, new Vector3(0.38f, 1.2f, -0.11f), new Vector3(0.6f, 0.02f, 0.2f), pal.Stainless, 0.004f);
            for (var i = 0; i < 3; i++)
            {
                var g = new Vector3(0.18f + i * 0.1f, 1.63f, -0.12f);
                Box("GoggleFrame", board, g + new Vector3(0f, 0.025f, 0f), new Vector3(0.085f, 0.04f, 0.03f), pal.Glass, 0f);
                Box("GoggleStrap", board, g + new Vector3(0f, 0.025f, 0.02f), new Vector3(0.09f, 0.012f, 0.01f), pal.Rubber, 0f);
            }

            for (var i = 0; i < 2; i++)
            {
                var m = new Vector3(0.5f + i * 0.1f, 1.63f, -0.12f);
                Lathe("Respirator", board, "Dome", DomeProfile, pal.White, m, 0.75f, 10);
                Cyl("Filter", board, m + new Vector3(0f, 0.01f, -0.035f), 0.018f, 0.03f, pal.Bactericide, 8);
            }

            for (var i = 0; i < 2; i++)
            {
                var b = new Vector3(0.22f + i * 0.17f, 1.21f, -0.12f);
                Box("GloveBox", board, b + new Vector3(0f, 0.05f, 0f), new Vector3(0.14f, 0.1f, 0.12f), pal.White, 0.004f);
                Text(board, i == 0 ? "NITRILE\nGLOVES M" : "NITRILE\nGLOVES L", b + new Vector3(0f, 0.05f, -0.062f), new Vector2(0.12f, 0.07f), pal.SignBlue.color, 0.02f);
            }

            var gloves = GlbMesh(board, WorkGlovesGlb, "Object_4", 0.2f, Pose.Flat, new Vector3(0.56f, 1.21f, -0.12f), 15f);
            if (gloves == null)
                Box("Gloves", board, new Vector3(0.58f, 1.24f, -0.12f), new Vector3(0.12f, 0.05f, 0.2f), pal.Fertiliser, 0.01f);

            // Tool hygiene station (project fixture) between the counter and the board.
            Decor("fx_sanitation", corner, new Vector3(X(3.78f), 0f, -0.2f), 0f);

            // First-aid box and an emergency eyewash on its pedestal in the corner.
            var aid = Group("FirstAid", corner, new Vector3(X(6.15f), 1.5f, 0f));
            Box("Box", aid, new Vector3(0f, 0f, -0.06f), new Vector3(0.36f, 0.36f, 0.12f), pal.SignGreen, 0.01f);
            Box("CrossV", aid, new Vector3(0f, 0f, -0.122f), new Vector3(0.06f, 0.2f, 0.004f), pal.White, 0f);
            Box("CrossH", aid, new Vector3(0f, 0f, -0.122f), new Vector3(0.2f, 0.06f, 0.004f), pal.White, 0f);
            Text(aid, "FIRST AID", new Vector3(0f, -0.14f, -0.126f), new Vector2(0.3f, 0.05f), Color.white, 0.035f);

            var eyewash = Group("EmergencyEyewash", corner, new Vector3(X(5.75f), 0f, -0.38f));
            Cyl("Foot", eyewash, Vector3.zero, 0.14f, 0.02f, pal.Safety, 12);
            Cyl("Pedestal", eyewash, Vector3.up * 0.02f, 0.035f, 0.84f, pal.Safety, 10);
            Lathe("Bowl", eyewash, "Bowl", BowlProfile, pal.Safety, Vector3.up * 0.86f, 1f, 14, false);
            Rod("PushFlag", eyewash, new Vector3(0.12f, 0.9f, -0.12f), new Vector3(0.12f, 0.9f, -0.22f), 0.012f, pal.Safety, 6);
            Sign("EyewashSign", corner, "EMERGENCY\nEYEWASH", new Vector3(X(5.75f), 2.05f, 0f), new Vector2(0.42f, 0.28f), pal.SignGreen, Color.white, 0.06f);
            Zone(eyewash, 5.55f, -6.0f, 5.95f, -5.4f);
        }

        // ------------------------------------------------------------------ zone D: chemical store (back-right)

        static readonly string[] JerryCans = { "Propiconazole\n25 EC", "Kasugamycin\n3 SL", "NPK\n19-19-19", "Neem Oil\nconcentrate" };

        static void ChemicalStore(Transform root)
        {
            var store = Group("ChemicalStore", root, Vector3.zero);

            // Front shelving: colour-coded and labelled stock, facing the room (-Z).
            var front = Group("ShelvingFront", store, new Vector3(5.2f, 0f, 6.0f));
            Shelving(front, out var levels);
            var codes = new[] { pal.Fungicide, pal.Bactericide, pal.Fertiliser, pal.Organic };
            // Floor level: 20 L jerry cans.
            for (var i = 0; i < 4; i++)
                JerryCan(front, new Vector3(-0.82f + i * 0.54f, levels[0], -0.02f), codes[i], JerryCans[i]);
            // Level 1: fungicide powder bags (blue).
            ShelfHeader(front, levels[1], "FUNGICIDES", pal.Fungicide);
            var fungicides = new[] { "Mancozeb\n75 WP", "Copper\nOxychloride\n50 WP", "Carbendazim\n50 WP", "Sulphur\n80 WP" };
            for (var i = 0; i < fungicides.Length; i++)
                PowderBag(front, new Vector3(-0.8f + i * 0.53f, levels[1], -0.04f), pal.Fungicide, fungicides[i]);
            // Level 2: bactericide bottles (red).
            ShelfHeader(front, levels[2], "BACTERICIDES", pal.Bactericide);
            var bactericides = new[] { "Strepto-\ncycline", "Kasugamycin\n3 SL", "Copper\nHydroxide\n77 WP", "Validamycin\n3 L" };
            for (var i = 0; i < bactericides.Length; i++)
            {
                var x = -0.8f + i * 0.53f;
                ChemicalBottle(front, new Vector3(x - 0.06f, levels[2], -0.05f), pal.Bactericide, bactericides[i]);
                ChemicalBottle(front, new Vector3(x + 0.07f, levels[2], 0.06f), pal.Bactericide, null);
            }

            // Level 3: fertilisers and bio-controls (green / amber).
            ShelfHeader(front, levels[3], "FERTILISERS & BIO-CONTROL", pal.Fertiliser);
            var bio = new[] { ("NPK\n19-19-19", pal.Fertiliser), ("Calcium\nNitrate", pal.Fertiliser), ("Neem Oil\n1500 ppm", pal.Organic), ("Trichoderma\nviride", pal.Organic) };
            for (var i = 0; i < bio.Length; i++)
            {
                var x = -0.8f + i * 0.53f;
                ChemicalBottle(front, new Vector3(x - 0.06f, levels[3], -0.05f), bio[i].Item2, bio[i].Item1);
                ChemicalBottle(front, new Vector3(x + 0.07f, levels[3], 0.06f), bio[i].Item2, null);
            }

            Carton(front, new Vector3(-0.6f, 2.13f, 0f), new Vector3(0.5f, 0.26f, 0.4f));
            Carton(front, new Vector3(0.65f, 2.11f, 0.02f), new Vector3(0.42f, 0.22f, 0.36f), 180f);
            Sign("StoreSign", front, "CHEMICAL STORE\nAuthorised staff - PPE required", new Vector3(0f, 2.18f, -0.26f), new Vector2(1.5f, 0.26f), pal.Safety, Ink, 0.08f);
            Zone(front, 4.0f, 5.7f, 6.4f, 6.3f);

            // Rear shelving: back stock (cartons, spare bottles), mostly seen end-on.
            var rear = Group("ShelvingRear", store, new Vector3(5.2f, 0f, 7.7f));
            Shelving(rear, out var rearLevels);
            var random = new System.Random(5);
            for (var level = 0; level < 4; level++)
            for (var i = 0; i < 4; i++)
            {
                var x = -0.85f + i * 0.56f;
                if ((level + i) % 2 == 0)
                    Carton(rear, new Vector3(x, rearLevels[level] + 0.15f, 0f), new Vector3(0.42f, 0.3f, 0.36f), level * 90f);
                else
                    for (var k = 0; k < 3; k++)
                        ChemicalBottle(rear, new Vector3(x - 0.13f + k * 0.13f, rearLevels[level], -0.04f + (float)random.NextDouble() * 0.08f), codes[(level + k) % 4], null);
            }

            Zone(rear, 4.0f, 7.4f, 6.4f, 8.0f);

            // Long garden tools standing in a caddy at the aisle end.
            var caddy = Group("ToolCaddy", store, new Vector3(3.65f, 0f, 7.7f));
            Lathe("Caddy", caddy, "Tub", TubProfile, pal.Rubber, Vector3.zero, 0.75f, 12, false);
            var longTools = new[] { ("rake", 1.45f, 0f), ("hoe1", 1.35f, 90f), ("garden pitchfork", 1.25f, 200f), ("bayonet shovel__", 1.2f, 300f) };
            for (var i = 0; i < longTools.Length; i++)
            {
                var a = longTools[i].Item3 * Mathf.Deg2Rad;
                var tool = GlbMesh(caddy, GardeningToolsGlb, longTools[i].Item1, longTools[i].Item2, Pose.Hung, new Vector3(Mathf.Sin(a) * 0.06f, 0.03f, Mathf.Cos(a) * 0.06f),
                    longTools[i].Item3);
                if (tool != null)
                    tool.localRotation = Quaternion.Euler(0f, longTools[i].Item3, 0f) * Quaternion.Euler(6f, 0f, 0f);
            }

            Zone(caddy, 3.45f, 7.5f, 3.85f, 7.9f);

            // Yellow chemical safety cabinet in the back corner.
            var cabinet = Group("SafetyCabinet", store, new Vector3(6.1f, 0f, WallBack - 0.27f));
            Box("Body", cabinet, new Vector3(0f, 0.55f, 0f), new Vector3(0.7f, 1.1f, 0.5f), pal.Safety, 0.012f, shadows: true);
            Box("DoorSeam", cabinet, new Vector3(0f, 0.55f, -0.251f), new Vector3(0.006f, 1.0f, 0.004f), pal.Rubber, 0f);
            Rod("HandleL", cabinet, new Vector3(-0.05f, 0.5f, -0.27f), new Vector3(-0.05f, 0.66f, -0.27f), 0.008f, pal.Rubber, 6);
            Rod("HandleR", cabinet, new Vector3(0.05f, 0.5f, -0.27f), new Vector3(0.05f, 0.66f, -0.27f), 0.008f, pal.Rubber, 6);
            Sign("CabinetSign", cabinet, "FLAMMABLE\nKEEP CLOSED", new Vector3(0f, 0.86f, -0.252f), new Vector2(0.42f, 0.16f), pal.Bactericide, Color.white, 0.05f);
            Solid(cabinet, new Vector3(0f, 0.55f, 0f), new Vector3(0.7f, 1.1f, 0.5f));
            Zone(cabinet, 5.75f, WallBack - 0.52f, 6.45f, WallBack - 0.02f);

            // Pallet of growing media sacks.
            var pallet = Group("SackPallet", store, new Vector3(4.0f, 0f, 9.0f));
            Pallet(pallet);
            var sacks = new[] { ("COMPOST\n25 kg", pal.SackGreen), ("COCO PEAT\n5 kg", pal.SackBrown), ("VERMICULITE\n100 L", pal.SackWhite), ("COMPOST\n25 kg", pal.SackGreen), ("PERLITE\n100 L", pal.SackWhite) };
            for (var i = 0; i < sacks.Length; i++)
            {
                var layer = i < 3 ? 0 : 1;
                var x = layer == 0 ? -0.38f + i * 0.38f : -0.19f + (i - 3) * 0.38f;
                var y = 0.14f + layer * 0.16f + 0.08f;
                var sackYaw = (i * 7f) % 9f - 4f;
                var sack = Real("SackFlat", pallet, new Vector3(x, y - 0.08f, 0f), sackYaw, size: new Vector3(0.36f, 0.16f, 0.62f), materials: new[] { sacks[i].Item2 },
                    shadows: true, name: "Sack");
                if (sack == null)
                    Box("Sack", pallet, new Vector3(x, y, 0f), new Vector3(0.36f, 0.16f, 0.62f), sacks[i].Item2, 0.04f, new Vector3(0f, sackYaw, 0f), true);
                Text(pallet, sacks[i].Item1, new Vector3(x, y + 0.083f, -0.05f), new Vector2(0.26f, 0.18f), sacks[i].Item2 == pal.SackWhite ? Ink : Color.white, 0.04f,
                    euler: new Vector3(90f, sackYaw, 0f));
            }

            Solid(pallet, new Vector3(0f, 0.25f, 0f), new Vector3(1.2f, 0.5f, 1.0f));
            Zone(pallet, 3.4f, 8.5f, 4.6f, 9.5f);
        }

        /// <summary>
        /// A 2.3 x 0.5 x 2.0 m shelving bay facing -Z (two scanned steel-frame units side by side, or a modelled painted steel
        /// bay); returns the heights of the four lower shelf tops (the fifth, at 2.0 m, carries cartons).
        /// </summary>
        static void Shelving(Transform s, out float[] levels)
        {
            const float w = 2.3f, d = 0.5f, h = 2.0f;
            var left = Real("SteelShelves", s, new Vector3(-w * 0.25f, 0f, 0f), 0f, size: new Vector3(w * 0.5f, h, d), shadows: true, name: "ShelfUnit");
            if (left != null)
            {
                Real("SteelShelves", s, new Vector3(w * 0.25f, 0f, 0f), 0f, size: new Vector3(w * 0.5f, h, d), shadows: true, name: "ShelfUnit");
                // Shelf tops of the steel-frame unit scaled to 2.0 m (measured on its mesh: 0.06, 0.30, 0.53, 0.77 of its height).
                levels = new[] { 0.121f, 0.598f, 1.065f, 1.541f };
                Solid(s, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
                return;
            }

            levels = new[] { 0.1f, 0.6f, 1.1f, 1.6f };
            foreach (var x in new[] { -w * 0.5f + 0.02f, w * 0.5f - 0.02f })
            foreach (var z in new[] { -d * 0.5f + 0.02f, d * 0.5f - 0.02f })
                Box("Post", s, new Vector3(x, h * 0.5f, z), new Vector3(0.04f, h, 0.04f), pal.Painted, 0.004f, shadows: true);
            foreach (var y in levels.Concat(new[] { h - 0.02f }))
                Box("Shelf", s, new Vector3(0f, y - 0.015f, 0f), new Vector3(w, 0.03f, d), pal.Painted, 0.006f, shadows: true);
            Box("BackPanel", s, new Vector3(0f, h * 0.5f, d * 0.5f - 0.01f), new Vector3(w - 0.06f, h - 0.05f, 0.01f), pal.Painted, 0f);
            Solid(s, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        }

        static void ShelfHeader(Transform s, float shelfY, string text, Material code)
        {
            Box("ShelfStrip", s, new Vector3(-0.55f, shelfY - 0.022f, -0.252f), new Vector3(1.0f, 0.045f, 0.004f), code, 0f);
            Text(s, text, new Vector3(-0.55f, shelfY - 0.022f, -0.256f), new Vector2(0.96f, 0.042f), Color.white, 0.034f);
        }

        /// <summary>A 1 L HDPE chemical bottle, colour-coded, with an optional front label (bottom on the point).</summary>
        static void ChemicalBottle(Transform parent, Vector3 basePoint, Material code, string label)
        {
            Lathe("Bottle", parent, "Bottle", BottleProfile, code, basePoint);
            Cyl("Cap", parent, basePoint + Vector3.up * 0.232f, 0.02f, 0.025f, pal.White, 8);
            if (label != null)
                StickOn(parent, label, basePoint + new Vector3(0f, 0.095f, -0.047f), new Vector2(0.075f, 0.1f), 0.016f);
        }

        /// <summary>A 20 L jerry can (bottom on the point), colour-coded and labelled.</summary>
        static void JerryCan(Transform parent, Vector3 basePoint, Material code, string label)
        {
            var can = Group("JerryCan", parent, basePoint);
            var model = Real(code == pal.Fungicide || code == pal.Fertiliser ? "JerryCanBlue" : "JerryCanRed", can, Vector3.zero, 0f, 0.4f, name: "Can");
            if (model != null)
            {
                var b = Extent(model);
                StickOn(can, label, new Vector3(b.center.x, 0.2f, b.min.z - 0.003f), new Vector2(Mathf.Min(0.2f, b.size.x * 0.8f), 0.14f), 0.028f);
                return;
            }

            Box("Body", can, new Vector3(0f, 0.19f, 0f), new Vector3(0.3f, 0.38f, 0.2f), code, 0.025f);
            Box("Handle", can, new Vector3(-0.04f, 0.41f, 0f), new Vector3(0.16f, 0.04f, 0.04f), code, 0.01f);
            Cyl("Spout", can, new Vector3(0.1f, 0.37f, 0f), 0.028f, 0.05f, code, 10);
            Cyl("Cap", can, new Vector3(0.1f, 0.42f, 0f), 0.032f, 0.025f, pal.Rubber, 10);
            StickOn(can, label, new Vector3(0f, 0.2f, -0.102f), new Vector2(0.22f, 0.15f), 0.03f);
        }

        /// <summary>A standing powder bag (wettable powder), colour-coded and labelled.</summary>
        static void PowderBag(Transform parent, Vector3 basePoint, Material code, string label)
        {
            var bag = Group("PowderBag", parent, basePoint);
            Box("Bag", bag, new Vector3(0f, 0.15f, 0f), new Vector3(0.24f, 0.3f, 0.09f), code, 0.02f);
            Box("Seal", bag, new Vector3(0f, 0.305f, 0f), new Vector3(0.24f, 0.012f, 0.02f), code, 0f);
            StickOn(bag, label, new Vector3(0f, 0.15f, -0.047f), new Vector2(0.19f, 0.16f), 0.028f);
            Box("Bag2", bag, new Vector3(0.05f, 0.15f, 0.12f), new Vector3(0.24f, 0.3f, 0.09f), code, 0.02f);
        }

        static void Pallet(Transform p)
        {
            const float w = 1.2f, d = 1.0f;
            for (var i = 0; i < 3; i++)
                Box("Block", p, new Vector3(0f, 0.05f, -d * 0.5f + 0.05f + i * (d - 0.1f) * 0.5f), new Vector3(w, 0.1f, 0.1f), pal.Wood, 0.006f, shadows: true);
            for (var i = 0; i < 6; i++)
                Box("Slat", p, new Vector3(-w * 0.5f + 0.07f + i * (w - 0.14f) / 5f, 0.12f, 0f), new Vector3(0.14f, 0.04f, d), pal.Wood, 0.004f, shadows: true);
        }

        // ------------------------------------------------------------------ zone E: nursery (back wall counter, under the window)

        static void Nursery(Transform root)
        {
            // Local frame: X = world X, +Z is the back wall, the counter top is y = 0.
            var nursery = Group("SeedlingNursery", root, new Vector3(0f, CounterTop, WallBack - CounterDepth * 0.5f));
            for (var i = 0; i < 10; i++)
            {
                var x = -6.1f + (i / 2) * 0.6f;
                var z = i % 2 == 0 ? -0.15f : 0.15f;
                SeedTray(nursery, new Vector3(x, 0f, z), Vector3.zero, i == 3);
            }

            StickOn(nursery, "NURSERY  sown wk 44 - water daily", new Vector3(-4.9f, 0.016f, -0.33f), new Vector2(0.5f, 0.03f), 0.022f);
            for (var i = 0; i < 6; i++)
                PottedSeedling(nursery, new Vector3(-2.75f + (i % 3) * 0.2f, 0f, i < 3 ? -0.12f : 0.12f), false, 1.4f);

            // Repotting station: compost tub with scoop, a stack of clean pots, trowel and pruning shears.
            var repot = Group("Repotting", nursery, new Vector3(3.0f, 0f, 0f));
            if (Real("CompostBag", repot, new Vector3(-0.3f, 0f, 0.02f), 15f, 0.3f, name: "CompostSack") == null)
            {
                Lathe("CompostTub", repot, "Tub", TubProfile, pal.Rubber, new Vector3(-0.3f, 0f, 0.02f), 0.8f, 14, false);
                Cyl("Compost", repot, new Vector3(-0.3f, 0.02f, 0.02f), 0.18f, 0.17f, pal.Soil, 12);
            }
            for (var i = 0; i < 4; i++)
                Lathe("EmptyPot", repot, "Pot", PotProfile, pal.Terracotta, new Vector3(0.1f, i * 0.03f, 0.08f), 1.2f, 12, false);
            GlbMesh(repot, GardeningToolsGlb, "garden shovel", 0.3f, Pose.Flat, new Vector3(0.35f, 0f, -0.12f), 30f);
            Decor("fx_clean_pot", repot, new Vector3(0.45f, 0f, 0.12f), 0f);
        }

        // ------------------------------------------------------------------ zone F: free-standing islands

        /// <summary>A stainless lab island of bench height with painted drawer pedestals and a kick plinth (origin on the floor, centre).</summary>
        static void Island(Transform u, float length, float depth, bool drawersBothSides = true)
        {
            Box("Top", u, new Vector3(0f, BenchHeight - 0.02f, 0f), new Vector3(length, 0.04f, depth), pal.Stainless, 0.008f);
            Box("Body", u, new Vector3(0f, (BenchHeight - 0.04f) * 0.5f + 0.05f, 0f), new Vector3(length - 0.06f, BenchHeight - 0.14f, depth - 0.08f), pal.Painted, 0.01f, shadows: true);
            Box("Plinth", u, new Vector3(0f, 0.04f, 0f), new Vector3(length - 0.14f, 0.08f, depth - 0.16f), pal.Rubber, 0f);
            var count = Mathf.Max(2, Mathf.RoundToInt(length / 0.6f));
            foreach (var side in drawersBothSides ? new[] { -1f, 1f } : new[] { -1f })
                for (var i = 0; i < count; i++)
                {
                    var x = -length * 0.5f + 0.03f + (length - 0.06f) * (i + 0.5f) / count;
                    var z = side * (depth * 0.5f - 0.039f);
                    for (var row = 0; row < 3; row++)
                    {
                        var y = 0.2f + row * 0.22f;
                        Box("DrawerSeam", u, new Vector3(x, y + 0.1f, z), new Vector3((length - 0.06f) / count - 0.02f, 0.004f, 0.004f), pal.Rubber, 0f);
                        Rod("Pull", u, new Vector3(x - 0.06f, y, z + side * 0.012f), new Vector3(x + 0.06f, y, z + side * 0.012f), 0.006f, pal.Stainless, 6);
                    }
                }

            Solid(u, new Vector3(0f, BenchHeight * 0.5f, 0f), new Vector3(length, BenchHeight, depth));
        }

        static void MixingIsland(Transform root)
        {
            var u = Group("SprayMixingBench", root, new Vector3(-4.2f, 0f, -3.0f), 180f);
            Island(u, 2.4f, 0.8f);
            const float top = BenchHeight;
            Box("Upstand", u, new Vector3(0f, top + 0.25f, 0.37f), new Vector3(2.4f, 0.5f, 0.03f), pal.Painted, 0.006f);
            Sign("MixSign", u, "SPRAY MIXING - gloves, goggles and mask ON", new Vector3(0f, top + 0.4f, 0.355f), new Vector2(1.7f, 0.15f), pal.SignBlue, Color.white, 0.06f);
            Lathe("Jug", u, "Jug", JugProfile, pal.White, new Vector3(-0.8f, top, -0.05f), 1f, 12, false);
            Cyl("Mix", u, new Vector3(-0.8f, top + 0.006f, -0.05f), 0.06f, 0.09f, pal.LiquidBlue, 10);
            Rod("StirPaddle", u, new Vector3(-0.78f, top + 0.02f, -0.05f), new Vector3(-0.72f, top + 0.32f, 0.0f), 0.008f, pal.White, 6);
            ChemicalBottle(u, new Vector3(-0.55f, top, 0.12f), pal.Fungicide, "Propiconazole\n25 EC");
            PowderBag(u, new Vector3(-0.3f, top, 0.15f), pal.Fungicide, "Mancozeb\n75 WP");
            DigitalScale(Group("Scale", u, new Vector3(0.05f, top, 0f)), "25.0 g");
            SprayBottle(u, new Vector3(0.4f, top, -0.1f), 140f);
            GlbMesh(u, WorkGlovesGlb, "Object_4", 0.24f, Pose.Flat, new Vector3(0.66f, top, -0.12f), -30f);
            KnapsackSprayer(Group("KnapsackOnBench", u, new Vector3(0.98f, top, 0.1f)), pal.Fungicide, "MIX: 2.5 g/L");
            // 20 L mixing drum on the floor at the end of the bench.
            var drum = Group("MixingDrum", u, new Vector3(1.48f, 0f, -0.05f));
            Cyl("Drum", drum, Vector3.zero, 0.17f, 0.46f, pal.Fungicide, 14, shadows: true);
            Cyl("Lid", drum, Vector3.up * 0.46f, 0.175f, 0.02f, pal.Rubber, 14);
            StickOn(drum, "MIXED\nFUNGICIDE", new Vector3(0f, 0.28f, -0.172f), new Vector2(0.18f, 0.1f), 0.024f);
            Zone(u, -5.9f, -3.45f, -2.95f, -2.55f);
        }

        static void TrolleyAndBins(Transform root)
        {
            var u = Group("TrolleyAndBins", root, new Vector3(-6.4f, 0f, -0.75f), -90f);
            // Local frame: X along the left counter (world -Z ... +Z reversed), -Z towards the room.
            var trolley = Group("Trolley", u, new Vector3(-0.65f, 0f, 0f));
            const float w = 0.9f, d = 0.5f;
            foreach (var x in new[] { -w * 0.5f + 0.02f, w * 0.5f - 0.02f })
            foreach (var z in new[] { -d * 0.5f + 0.02f, d * 0.5f - 0.02f })
            {
                Cyl("Post", trolley, new Vector3(x, 0.1f, z), 0.012f, 0.8f, pal.Stainless, 8);
                Cyl("Caster", trolley, new Vector3(x, 0.0f, z), 0.04f, 0.08f, pal.Rubber, 8, new Vector3(0f, 0f, 0f));
            }

            Box("ShelfLow", trolley, new Vector3(0f, 0.25f, 0f), new Vector3(w, 0.025f, d), pal.Stainless, 0.006f, shadows: true);
            Box("ShelfTop", trolley, new Vector3(0f, 0.88f, 0f), new Vector3(w, 0.025f, d), pal.Stainless, 0.006f, shadows: true);
            Rod("Handle", trolley, new Vector3(-w * 0.5f - 0.02f, 0.9f, -d * 0.4f), new Vector3(-w * 0.5f - 0.02f, 0.9f, d * 0.4f), 0.012f, pal.Stainless, 8);
            for (var i = 0; i < 3; i++)
                if (Real("SampleCrate", trolley, new Vector3(-0.28f + i * 0.28f, 0.8925f, 0f), 90f, size: new Vector3(0.34f, 0.15f, 0.26f)) == null)
                    Box("SampleCrate", trolley, new Vector3(-0.28f + i * 0.28f, 0.89f + 0.06f, 0f), new Vector3(0.26f, 0.12f, 0.36f), i == 1 ? pal.Fertiliser : pal.White, 0.01f);
            for (var i = 0; i < 2; i++)
                PottedSeedling(trolley, new Vector3(-0.2f + i * 0.4f, 0.265f, 0f), true, 1.8f);
            Solid(trolley, new Vector3(0f, 0.5f, 0f), new Vector3(w, 1.0f, d));

            Decor("fx_disposal_bin", u, new Vector3(0.25f, 0f, 0.0f), 0f);
            Decor("fx_disposal_bin", u, new Vector3(0.75f, 0f, 0.0f), 0f);
            Zone(u, -6.7f, -1.9f, -6.1f, 0.25f);
        }

        static void PrepIsland(Transform root)
        {
            var u = Group("SamplePrepIsland", root, new Vector3(-3.6f, 0f, 3.0f));
            const float top = BenchHeight;
            Island(u, 2.6f, 0.9f);
            // Sink and tap at the right end.
            var sink = Group("Sink", u, new Vector3(0.85f, top, 0.05f));
            Sink(sink);
            // Glassware and a pH calibration set at the left end.
            for (var i = 0; i < 4; i++)
            {
                var p = new Vector3(-1.05f + i * 0.11f, top, -0.2f + (i % 2) * 0.1f);
                Lathe("Beaker", u, "Beaker", BeakerProfile, pal.Glass, p, 1.1f, 12, false);
                Cyl("Solution", u, p + Vector3.up * 0.005f, 0.04f, 0.04f + i * 0.012f, i % 2 == 0 ? pal.LiquidBlue : pal.LiquidAmber, 10);
            }

            Lathe("ConicalFlask", u, "Flask", FlaskProfile, pal.Glass, new Vector3(-0.55f, top, 0.15f), 1.3f, 12, false);
            Cyl("Solution", u, new Vector3(-0.55f, top + 0.005f, 0.15f), 0.058f, 0.04f, pal.Fertiliser, 10);
            for (var i = 0; i < 3; i++)
            {
                var p = new Vector3(-0.25f + i * 0.09f, top, 0.18f);
                Lathe("Reagent", u, "Reagent", ReagentProfile, i == 1 ? pal.AmberGlass : pal.White, p);
                Cyl("Cap", u, p + Vector3.up * 0.155f, 0.015f, 0.02f, i == 0 ? pal.Bactericide : i == 1 ? pal.Rubber : pal.SignBlue, 8);
            }

            StickOn(u, "pH 4.0 / 7.0 / 10.0 buffers", new Vector3(-0.16f, top + 0.012f, 0.1f), new Vector2(0.3f, 0.02f), 0.014f);
            PhMeter(Group("PhMeter", u, new Vector3(-0.3f, top, -0.2f)));
            // Leaf samples on a cutting mat.
            var mat = Group("CuttingMat", u, new Vector3(0.2f, top, -0.12f));
            Box("Mat", mat, new Vector3(0f, 0.002f, 0f), new Vector3(0.45f, 0.004f, 0.3f), pal.Fertiliser, 0f);
            for (var i = 0; i < 4; i++)
                Box("Leaf", mat, new Vector3(-0.14f + i * 0.09f, 0.006f, (i % 2) * 0.06f - 0.03f), new Vector3(0.05f, 0.003f, 0.09f), i == 2 ? pal.Leaf : pal.LeafSick, 0f,
                    new Vector3(0f, i * 35f - 40f, 0f));
            Rod("Scalpel", mat, new Vector3(0.1f, 0.008f, 0.1f), new Vector3(0.22f, 0.008f, 0.06f), 0.004f, pal.Stainless, 4);
            Zone(u, -4.95f, 2.5f, -2.25f, 3.5f);
        }

        static void PropagationIsland(Transform root)
        {
            var u = Group("PropagationIsland", root, new Vector3(-3.6f, 0f, 5.8f));
            const float top = BenchHeight;
            Island(u, 2.6f, 0.9f);
            for (var i = 0; i < 4; i++)
                SeedTray(u, new Vector3(-0.95f + (i / 2) * 0.6f, top, i % 2 == 0 ? -0.17f : 0.17f), Vector3.zero, false);

            // Insect-proof mesh frame (project fixture) over a tray of seedlings.
            SeedTray(u, new Vector3(0.35f, top, 0f), new Vector3(0f, 90f, 0f), false);
            Decor("fx_mesh_frame", u, new Vector3(0.35f, top, 0f), 0f);
            for (var i = 0; i < 4; i++)
                PottedSeedling(u, new Vector3(0.9f + (i % 2) * 0.19f, top, -0.13f + (i / 2) * 0.26f), false, 1.5f);
            StickOn(u, "PROPAGATION - healthy stock only", new Vector3(0f, top - 0.02f, -0.452f), new Vector2(0.7f, 0.035f), 0.026f);
            Zone(u, -4.95f, 5.3f, -2.25f, 6.3f);
        }

        static void GrowthCabinet(Transform root)
        {
            var u = Group("GrowthCabinet", root, new Vector3(-4.8f, 0f, 8.15f));
            const float w = 1.0f, d = 0.7f, h = 2.0f;
            Box("SideL", u, new Vector3(-w * 0.5f + 0.03f, h * 0.5f, 0f), new Vector3(0.06f, h, d), pal.White, 0.01f, shadows: true);
            Box("SideR", u, new Vector3(w * 0.5f - 0.03f, h * 0.5f, 0f), new Vector3(0.06f, h, d), pal.White, 0.01f, shadows: true);
            Box("Top", u, new Vector3(0f, h - 0.12f, 0f), new Vector3(w, 0.24f, d), pal.White, 0.012f, shadows: true);
            Box("Base", u, new Vector3(0f, 0.06f, 0f), new Vector3(w, 0.12f, d), pal.White, 0.01f, shadows: true);
            Box("Back", u, new Vector3(0f, h * 0.5f, d * 0.5f - 0.02f), new Vector3(w, h, 0.04f), pal.White, 0f);
            Box("Door", u, new Vector3(0f, 0.94f, -d * 0.5f + 0.015f), new Vector3(w - 0.1f, 1.62f, 0.012f), pal.Glass, 0f);
            Rod("Handle", u, new Vector3(w * 0.5f - 0.09f, 0.75f, -d * 0.5f - 0.01f), new Vector3(w * 0.5f - 0.09f, 1.15f, -d * 0.5f - 0.01f), 0.01f, pal.Stainless, 6);
            for (var i = 0; i < 4; i++)
            {
                var y = 0.14f + i * 0.4f;
                Box("Shelf", u, new Vector3(0f, y, 0f), new Vector3(w - 0.12f, 0.015f, d - 0.08f), pal.Stainless, 0f);
                SeedTray(u, new Vector3(-0.15f, y + 0.008f, 0f), new Vector3(0f, 90f, 0f), false);
                SeedTray(u, new Vector3(0.17f, y + 0.008f, 0f), new Vector3(0f, 90f, 0f), false);
                // Grow LED bar under the shelf above.
                Box("GrowLed", u, new Vector3(0f, y + 0.37f, 0f), new Vector3(w - 0.2f, 0.012f, 0.05f), pal.Led, 0f);
            }

            Box("Display", u, new Vector3(0f, h - 0.12f, -d * 0.5f - 0.002f), new Vector3(0.5f, 0.12f, 0.004f), pal.Rubber, 0f);
            Text(u, "GROWTH CHAMBER  24 °C  70 % RH", new Vector3(0f, h - 0.12f, -d * 0.5f - 0.006f), new Vector2(0.46f, 0.09f), new Color(0.55f, 1f, 0.7f), 0.03f);
            Solid(u, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
            Zone(u, -5.3f, 7.8f, -4.3f, 8.5f);
        }

        static void Quarantine(Transform root)
        {
            var u = Group("Quarantine", root, new Vector3(-2.0f, 0f, 7.6f));
            Decor("fx_containment", u, Vector3.zero, 0f);
            PottedSeedling(u, new Vector3(0f, 0f, 0f), true, 3f);
            var stand = Group("SignStand", u, new Vector3(0.75f, 0f, -0.2f));
            Cyl("Foot", stand, Vector3.zero, 0.16f, 0.02f, pal.Rubber, 12);
            Cyl("Post", stand, Vector3.up * 0.02f, 0.015f, 1.3f, pal.Stainless, 8);
            Sign("QuarantineSign", stand, "QUARANTINE\nInfected plants - do not touch", new Vector3(0f, 1.42f, 0f), new Vector2(0.5f, 0.3f), pal.Bactericide, Color.white,
                0.06f);
            Decor("fx_disposal_bin", u, new Vector3(-0.75f, 0f, 0f), 0f);
            Zone(u, -3.0f, 7.1f, -1.05f, 8.1f);
        }
    }
}
