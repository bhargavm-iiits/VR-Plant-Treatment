using System.Collections.Generic;
using BTP.Core;
using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.Editor
{
    /// <summary>
    /// Adds the specimen chamber hall to Treatment.unity (called by <see cref="TreatmentSceneBuilder.BuildScene"/>): a
    /// lab hall sized from <see cref="ChamberFit.ReservedExterior"/> with at least 1.5 m clearance on every side and a
    /// viewer zone in front (floor with teleport area, olive walls, panoramic windows onto the vineyard, timber ceiling
    /// with pendants and lights), the resizable <see cref="TreatmentChamber"/>, the four-control panel on its stand, the
    /// case sign with the Fertiliser card, the spray-target ring, the gloved hand with the fertiliser packet, the watering
    /// wand and the <see cref="ChamberStation"/> that runs them. The case rooms and the manual panels stay in the scene
    /// but are hidden by the chamber layout.
    /// </summary>
    public static class TreatmentChamberBuilder
    {
        public const string Materials = ProjectPaths.Root + "/Materials/Treatment/Chamber";
        /// <summary>World position of the chamber line (the chamber's front-right exterior corner).</summary>
        public static readonly Vector3 LineOrigin = new Vector3(2.0f, 0f, 1.0f);
        public const float Clearance = 1.5f;
        public const float PanelWidthMm = 1280f, PanelHeightMm = 664f;
        static readonly Color Ink = new Color(0.04f, 0.1f, 0.13f);
        static readonly Color Charcoal = new Color(0.12f, 0.135f, 0.15f, 0.97f);

        /// <summary>The hall interior in chamber-line space: X from Left to Right, Z from Front to Back.</summary>
        public static float HallLeft => -ChamberFit.ReservedExterior.x - Clearance;
        /// <summary>Addendum 6: room for the kiosk and the TREATMENT TOOLS bench on the right, whatever the chamber size.</summary>
        public static float HallRight => 4.5f;
        /// <summary>Addendum 6: deep enough in front for the WATER CONTROL console, walking paths and the overview spawn.</summary>
        public static float HallFront => -7.0f;
        public static float HallBack => ChamberFit.ReservedExterior.z + Clearance;
        public static float HallHeight => ChamberFit.ReservedExterior.y + 0.7f;

        internal static ChamberStation Build(Transform treatment, TreatmentController controller, Transform ui, SpawnAnchor spawn, ManualTreatmentView view)
        {
            var root = new GameObject("ChamberHall").transform;
            root.SetParent(treatment, false);
            var line = new GameObject("ChamberLine").transform;
            line.SetParent(root, false);
            line.localPosition = LineOrigin;

            BuildHall(line);
            var chamber = BuildChamber(line);
            var panel = BuildPanel(root, out var panelRoot, out var panelCanvas);
            BuildScreenFrame(panel, ui);
            var sign = BuildSign(root, out var title, out var scenario, out var specimen, out var card, out var cardBody);
            var marker = BuildSprayMarker(root, out var markerLabel);
            var soilLabel = BuildSoilLabel(root);
            var applicator = BuildApplicator(root);
            var water = BuildWaterStation(root);
            var bench = BuildToolsBench(root, out var packetDock, out var sprayerDock);
            var anchors = BuildStationAnchors(root);

            var station = new GameObject("ChamberStation").AddComponent<ChamberStation>();
            station.transform.SetParent(root, false);
            station.Bind(chamber, line, panel, panelRoot, panelCanvas, sign, title, scenario, specimen, card, cardBody, spawn, ui,
                AssetDatabase.LoadAssetAtPath<DiseaseVariantCatalog>(ProjectPaths.VariantCatalog), AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog),
                applicator, null, marker, markerLabel, soilLabel);
            station.BindWater(water);
            station.BindSprayer(BuildSprayerProp(root));
            station.BindShelves(packetDock, sprayerDock);
            station.BindStations(bench, Rect.MinMaxRect(HallLeft, HallFront, HallRight, HallBack), anchors[0], anchors[1], anchors[2], anchors[3]);
            station.gameObject.AddComponent<HandTrackingMonitor>().Bind(station);
            BuildProbes(line);

            controller.BindChamberLayout(true);
            EditorUtility.SetDirty(controller);
            if (view != null)
            {
                view.panelsSuppressed = true;
                foreach (var manual in view.manualPanels)
                    if (manual != null)
                        manual.SetActive(false);
                EditorUtility.SetDirty(view);
            }

            // The saved scene shows a sensible tomato-sized chamber with everything placed around it.
            station.LayoutDefault();
            return station;
        }

        // ------------------------------------------------------------------ materials

        static Material Lit(string name, Color color, float metallic, float smoothness, bool transparent = false, bool doubleSided = false) =>
            MaterialFactory.Lit($"{Materials}/{name}.mat", color, metallic, smoothness, transparent: transparent, doubleSided: doubleSided);

        static Material Solid(string name, Color color, float smoothness = 0.1f, Texture texture = null) =>
            MaterialFactory.Solid($"{Materials}/{name}.mat", color, smoothness, texture);

        /// <summary>Glass without sky reflections, so the chamber and tray windows stay clear from the viewer's angle.</summary>
        static Material NoReflections(Material material)
        {
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.SetFloat("_SpecularHighlights", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Glow(string name, Color color, float intensity) => MaterialFactory.Glow($"{Materials}/{name}.mat", color, intensity);

        static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material,
            Quaternion? rotation = null, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation ?? Quaternion.identity);
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A cylinder from <paramref name="a"/> to <paramref name="b"/> (local space of <paramref name="parent"/>).</summary>
        static GameObject Limb(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material material, PrimitiveType type = PrimitiveType.Capsule)
        {
            var axis = b - a;
            var length = axis.magnitude;
            var scale = type == PrimitiveType.Capsule
                ? new Vector3(radius * 2f, Mathf.Max(length, radius * 2f) * 0.5f, radius * 2f)
                : new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            return Part(type, name, parent, (a + b) * 0.5f, scale, material, Quaternion.FromToRotation(Vector3.up, axis.normalized));
        }

        // ------------------------------------------------------------------ hall

        static void BuildHall(Transform line)
        {
            var hall = new GameObject("Hall").transform;
            hall.SetParent(line, false);
            var left = HallLeft;
            var right = HallRight;
            var front = HallFront;
            var back = HallBack;
            var height = HallHeight;
            var width = right - left;
            var depth = back - front;
            var cx = (left + right) * 0.5f;
            var cz = (front + back) * 0.5f;

            // The lab room's warm stone tiles, olive walls and timber (TreatmentAssetBuilder.LabRoom colours).
            // CC0 terrazzo floor and coated-pine ceiling (Assets/_Project/ThirdParty, see CREDITS.md).
            // The reference lab: large cream floor tiles, sage-olive walls, a low timber ceiling with steel beams and black
            // pendants, white counters, wooden shelves and wide windows onto the Farm rows.
            var floor = Solid("HallFloorCream", new Color(1f, 0.95f, 0.86f), 0.35f, ProceduralTextures.FloorTiles);
            floor.SetTextureScale("_BaseMap", new Vector2(width / 1.3f, depth / 1.3f));
            var wall = Solid("HallWallSage", new Color(0.5f, 0.55f, 0.4f), 0.1f);
            var timber = PbrPine("HallPine", new Vector2(6f, 6f));
            var steel = Solid("HallSteel", new Color(0.13f, 0.14f, 0.15f), 0.35f);
            var frame = Solid("HallWindowFrame", new Color(0.16f, 0.17f, 0.18f), 0.4f);
            var glass = Lit("HallWindowGlass", new Color(0.85f, 0.93f, 1f, 0.05f), 0f, 0.2f, true, true);
            var counter = Solid("HallCounter", new Color(0.9f, 0.91f, 0.9f), 0.4f);
            var cabinet = Solid("HallCabinet", new Color(0.55f, 0.57f, 0.58f), 0.25f);

            var floorObject = Part(PrimitiveType.Cube, "HallFloor", hall, new Vector3(cx, -0.02f, cz), new Vector3(width, 0.04f, depth), floor, null, true);
            floorObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            XRSceneWiring.MakeTeleportArea(floorObject);

            // Solid walls on the left and behind the viewer; low walls with panoramic windows at the back and right.
            Part(PrimitiveType.Cube, "WallLeft", hall, new Vector3(left - 0.05f, height * 0.5f, cz), new Vector3(0.1f, height, depth + 0.2f), wall, null, true);
            Part(PrimitiveType.Cube, "WallFront", hall, new Vector3(cx, height * 0.5f, front - 0.05f), new Vector3(width + 0.2f, height, 0.1f), wall, null, true);
            const float sill = 0.95f;
            var windowTop = 2.35f;
            foreach (var (name, centre, length, yaw) in new[]
                     {
                         ("Back", new Vector3(cx, 0f, back + 0.05f), width + 0.2f, 0f),
                         ("Right", new Vector3(right + 0.05f, 0f, cz), depth + 0.2f, 90f),
                     })
            {
                var rotation = Quaternion.Euler(0f, yaw, 0f);
                var along = rotation * Vector3.right;
                Part(PrimitiveType.Cube, "WallLow" + name, hall, centre + Vector3.up * (sill * 0.5f), new Vector3(length, sill, 0.1f), wall, rotation, true);
                Part(PrimitiveType.Cube, "WallHeader" + name, hall, centre + Vector3.up * ((windowTop + height) * 0.5f), new Vector3(length, height - windowTop, 0.1f), wall, rotation);
                Part(PrimitiveType.Cube, "Window" + name, hall, centre + Vector3.up * ((sill + windowTop) * 0.5f), new Vector3(length, windowTop - sill, 0.02f), glass, rotation);
                Part(PrimitiveType.Cube, "FrameBottom" + name, hall, centre + Vector3.up * sill, new Vector3(length, 0.05f, 0.12f), frame, rotation);
                Part(PrimitiveType.Cube, "FrameTop" + name, hall, centre + Vector3.up * windowTop, new Vector3(length, 0.05f, 0.12f), frame, rotation);
                var mullions = Mathf.Max(2, Mathf.RoundToInt(length / 2.6f));
                for (var i = 1; i < mullions; i++)
                    Part(PrimitiveType.Cube, "Mullion" + name, hall, centre + along * (-length * 0.5f + length * i / mullions) + Vector3.up * ((sill + windowTop) * 0.5f),
                        new Vector3(0.06f, windowTop - sill, 0.1f), frame, rotation);
            }

            // A low timber ceiling (3.2 m, like the reference room) with joists and steel beams, open above the reserved
            // chamber footprint into a raised bay (so a walk-in tree still fits), whose soffits are timber too.
            const float low = RoomCeiling;
            var holeMin = new Vector2(-ChamberFit.ReservedExterior.x - 0.1f, -0.1f);
            var holeMax = new Vector2(0.1f, ChamberFit.ReservedExterior.z + 0.1f);
            void Slab(string name, float x0, float x1, float z0, float z1)
            {
                if (x1 - x0 < 0.01f || z1 - z0 < 0.01f)
                    return;
                Part(PrimitiveType.Cube, name, hall, new Vector3((x0 + x1) * 0.5f, low, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.06f, z1 - z0), timber);
            }

            Slab("CeilingFront", left - 0.1f, right + 0.1f, front - 0.1f, holeMin.y);
            Slab("CeilingBack", left - 0.1f, right + 0.1f, holeMax.y, back + 0.1f);
            Slab("CeilingLeft", left - 0.1f, holeMin.x, holeMin.y, holeMax.y);
            Slab("CeilingRight", holeMax.x, right + 0.1f, holeMin.y, holeMax.y);
            Part(PrimitiveType.Cube, "CeilingBay", hall, new Vector3((holeMin.x + holeMax.x) * 0.5f, height, (holeMin.y + holeMax.y) * 0.5f),
                new Vector3(holeMax.x - holeMin.x + 0.2f, 0.06f, holeMax.y - holeMin.y + 0.2f), timber);
            var bayHeight = height - low;
            Part(PrimitiveType.Cube, "BaySoffitFront", hall, new Vector3((holeMin.x + holeMax.x) * 0.5f, low + bayHeight * 0.5f, holeMin.y),
                new Vector3(holeMax.x - holeMin.x, bayHeight, 0.06f), wall);
            Part(PrimitiveType.Cube, "BaySoffitBack", hall, new Vector3((holeMin.x + holeMax.x) * 0.5f, low + bayHeight * 0.5f, holeMax.y),
                new Vector3(holeMax.x - holeMin.x, bayHeight, 0.06f), wall);
            Part(PrimitiveType.Cube, "BaySoffitLeft", hall, new Vector3(holeMin.x, low + bayHeight * 0.5f, (holeMin.y + holeMax.y) * 0.5f),
                new Vector3(0.06f, bayHeight, holeMax.y - holeMin.y), wall);
            Part(PrimitiveType.Cube, "BaySoffitRight", hall, new Vector3(holeMax.x, low + bayHeight * 0.5f, (holeMin.y + holeMax.y) * 0.5f),
                new Vector3(0.06f, bayHeight, holeMax.y - holeMin.y), wall);
            bool InHole(float x, float z) => x > holeMin.x && x < holeMax.x && z > holeMin.y && z < holeMax.y;
            for (var x = left + 0.6f; x < right; x += 1.2f)
            {
                // Joists run front to back under the low ceiling, interrupted by the bay.
                Part(PrimitiveType.Cube, "Joist", hall, new Vector3(x, low - 0.1f, (front + holeMin.y) * 0.5f), new Vector3(0.12f, 0.16f, holeMin.y - front), timber);
                Part(PrimitiveType.Cube, "Joist", hall, new Vector3(x, low - 0.1f, (holeMax.y + back) * 0.5f), new Vector3(0.12f, 0.16f, back - holeMax.y), timber);
                if (x < holeMin.x || x > holeMax.x)
                    Part(PrimitiveType.Cube, "Joist", hall, new Vector3(x, low - 0.1f, (holeMin.y + holeMax.y) * 0.5f), new Vector3(0.12f, 0.16f, holeMax.y - holeMin.y), timber);
            }

            foreach (var z in new[] { front + 0.9f, holeMin.y - 0.2f, holeMax.y + 0.4f })
                Part(PrimitiveType.Cube, "SteelBeam", hall, new Vector3(cx, low - 0.26f, z), new Vector3(width, 0.26f, 0.16f), steel);
            var shade = Solid("HallPendantShade", new Color(0.07f, 0.07f, 0.08f), 0.5f);
            var bulb = Glow("HallPendantBulb", new Color(1f, 0.86f, 0.62f), 2.2f);
            // Black pendants on short cords from the low ceiling (longer ones in the bay), shades at about 2.7 m.
            const float shadeY = 2.72f;
            for (var x = left + 1.2f; x < right - 0.4f; x += 2.2f)
            for (var z = front + 1.2f; z < back - 0.4f; z += 2.4f)
            {
                var ceilingY = InHole(x, z) ? height - 0.06f : low - 0.03f;
                var cord = (ceilingY - shadeY) * 0.5f;
                Part(PrimitiveType.Cylinder, "PendantCord", hall, new Vector3(x, shadeY + cord, z), new Vector3(0.008f, cord, 0.008f), shade);
                Part(PrimitiveType.Cylinder, "PendantShade", hall, new Vector3(x, shadeY, z), new Vector3(0.1f, 0.07f, 0.1f), shade);
                Part(PrimitiveType.Cylinder, "PendantBulb", hall, new Vector3(x, shadeY - 0.072f, z), new Vector3(0.08f, 0.004f, 0.08f), bulb);
            }

            foreach (var position in new[]
                     {
                         new Vector3(-1.2f, height - 1.2f, -0.8f), new Vector3(1.6f, height - 1.2f, 1.0f), new Vector3(-4.2f, height - 1.0f, 2.2f),
                         new Vector3(-2.0f, height - 1.0f, 5.2f), new Vector3(-6.0f, height - 1.0f, 5.6f),
                         new Vector3(-2.0f, RoomCeiling - 0.4f, -4.2f), new Vector3(2.6f, RoomCeiling - 0.4f, -2.6f), new Vector3(2.6f, RoomCeiling - 0.4f, 1.8f),
                     })
            {
                var light = new GameObject("HallLight", typeof(Light)).GetComponent<Light>();
                light.transform.SetParent(hall, false);
                light.transform.localPosition = position;
                light.type = LightType.Point;
                light.range = 9f;
                light.intensity = 1.35f;
                light.color = new Color(1f, 0.9f, 0.74f);
                light.shadows = LightShadows.None;
            }

            // White lab counters along the left and front walls (outside the reserved chamber and route zone).
            Part(PrimitiveType.Cube, "CounterLeftTop", hall, new Vector3(left + 0.35f, 0.92f, cz), new Vector3(0.7f, 0.04f, depth - 1.6f), counter, null, true);
            Part(PrimitiveType.Cube, "CounterLeftBody", hall, new Vector3(left + 0.33f, 0.45f, cz), new Vector3(0.62f, 0.9f, depth - 1.7f), cabinet);
            Part(PrimitiveType.Cube, "CounterFrontTop", hall, new Vector3(cx - 1.0f, 0.92f, front + 0.35f), new Vector3(width - 4.0f, 0.04f, 0.7f), counter, null, true);
            Part(PrimitiveType.Cube, "CounterFrontBody", hall, new Vector3(cx - 1.0f, 0.45f, front + 0.33f), new Vector3(width - 4.1f, 0.9f, 0.62f), cabinet);
            // White counters under the back windows, seen behind the plant.
            Part(PrimitiveType.Cube, "CounterBackTop", hall, new Vector3(cx, 0.92f, back - 0.35f), new Vector3(width - 1.6f, 0.04f, 0.7f), counter, null, true);
            Part(PrimitiveType.Cube, "CounterBackBody", hall, new Vector3(cx, 0.45f, back - 0.33f), new Vector3(width - 1.7f, 0.9f, 0.62f), cabinet);

            // Wooden wall shelves with white bins on the left wall, as in the reference.
            var bin = Solid("HallBinWhite", new Color(0.93f, 0.94f, 0.94f), 0.3f);
            var shelfZ = front + 2.4f;
            for (var level = 0; level < 4; level++)
            {
                var y = 1.0f + level * 0.42f;
                Part(PrimitiveType.Cube, "Shelf", hall, new Vector3(left + 0.2f, y, shelfZ), new Vector3(0.36f, 0.035f, 1.6f), timber);
                for (var b = 0; b < 3; b++)
                    if ((level + b) % 2 == 0)
                        Part(PrimitiveType.Cube, "Bin", hall, new Vector3(left + 0.2f, y + 0.1f, shelfZ - 0.55f + b * 0.55f), new Vector3(0.28f, 0.17f, 0.36f), bin);
            }

            foreach (var dz in new[] { -0.8f, 0.8f })
                Part(PrimitiveType.Cube, "ShelfUpright", hall, new Vector3(left + 0.2f, 1.6f, shelfZ + dz), new Vector3(0.04f, 1.4f, 0.04f), steel);
            BuildFarmView(hall, right, back);
        }

        public const float RoomCeiling = 3.2f;

        /// <summary>
        /// The Farm seen through the windows (reference: green crop rows and fruit trees): staked tomato rows and orchard
        /// trees from the project's own Farm models (their LODs and billboards keep them cheap), outside the back and right windows.
        /// </summary>
        static void BuildFarmView(Transform hall, float right, float back)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            if (catalog == null)
                return;
            var view = new GameObject("FarmView").transform;
            view.SetParent(hall, false);
            var grass = Solid("FarmViewGrass", new Color(0.42f, 0.62f, 0.26f), 0.05f, ProceduralTextures.Grass);
            grass.SetTextureScale("_BaseMap", new Vector2(12f, 12f));
            Part(PrimitiveType.Cube, "FarmViewGround", view, new Vector3(-2f, -0.04f, back + 9f), new Vector3(40f, 0.04f, 17f), grass);
            Part(PrimitiveType.Cube, "FarmViewGroundRight", view, new Vector3(right + 9f, -0.04f, 2f), new Vector3(17f, 0.04f, 24f), grass);
            var random = new System.Random(7);
            void Row(string plantId, Vector3 start, Vector3 step, int count, float scale)
            {
                if (!catalog.TryGet(plantId, out var plant) || plant.FarmPrefab == null)
                    return;
                for (var i = 0; i < count; i++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(plant.FarmPrefab, view);
                    instance.transform.localPosition = start + step * i;
                    instance.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                    instance.transform.localScale = Vector3.one * scale * (0.92f + (float)random.NextDouble() * 0.12f);
                    foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                        Object.DestroyImmediate(collider);
                    // Scenery only: no shadows, and a small LOD size so the far LOD / billboard is used through the windows
                    // (keeps the window view cheap on Quest).
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }

                    foreach (var lod in instance.GetComponentsInChildren<LODGroup>(true))
                        lod.size *= 0.12f;
                }
            }

            // Behind the back windows: two tomato rows, then a row of apple trees.
            Row("tomato", new Vector3(-10f, 0f, back + 1.8f), new Vector3(1.4f, 0f, 0f), 15, 1f);
            Row("tomato", new Vector3(-9.3f, 0f, back + 3.4f), new Vector3(1.4f, 0f, 0f), 14, 1f);
            Row("apple", new Vector3(-12f, 0f, back + 7f), new Vector3(4.8f, 0f, 0f), 6, 1f);
            // Beyond the right windows: a tomato row and orange trees.
            Row("tomato", new Vector3(right + 1.8f, 0f, -2.5f), new Vector3(0f, 0f, 1.4f), 9, 1f);
            Row("orange", new Vector3(right + 5.5f, 0f, -3f), new Vector3(0f, 0f, 4.8f), 3, 1f);
        }

        // ------------------------------------------------------------------ chamber

        static TreatmentChamber BuildChamber(Transform line)
        {
            var go = new GameObject("TreatmentChamber");
            go.transform.SetParent(line, false);
            var chamber = go.AddComponent<TreatmentChamber>();
            var soilTexture = ProceduralTextures.Soil;
            // CC0 Ground015 soil (rough, dark) for the topsoil and the soil cap.
            var topsoil = Pbr("ChamberTopsoil", ThirdParty + "/ambientCG/Ground015/Ground015_1K-JPG", new Color(0.55f, 0.45f, 0.4f), 0f, 0.12f, new Vector2(1.5f, 1.5f),
                occlusion: true);
            var subsoil = Solid("ChamberSubsoil", new Color(0.4f, 0.29f, 0.21f), 0.05f, soilTexture);
            subsoil.SetTextureScale("_BaseMap", new Vector2(3f, 2f));
            var deep = Solid("ChamberDeepSoil", new Color(0.5f, 0.38f, 0.28f), 0.05f, soilTexture);
            deep.SetTextureScale("_BaseMap", new Vector2(3f, 2f));
            chamber.Bind(
                Pbr("ChamberFrameMetal012", ThirdParty + "/ambientCG/Metal012/Metal012_1K-JPG", new Color(0.86f, 0.87f, 0.88f), 0.9f, 0.6f, new Vector2(2f, 2f),
                    doubleSided: true),
                // Clear glass with subtle reflections (from the hall's HDRI reflection probe), low alpha, no haze.
                Lit("ChamberGlass", new Color(0.92f, 0.96f, 1f, 0.07f), 0f, 0.78f, true, true),
                Glow("ChamberAccent", UIPalette.Cyan, 2.6f),
                Glow("ChamberLightPanel", new Color(1f, 0.97f, 0.9f), 1.3f),
                Lit("ChamberTray", new Color(0.17f, 0.18f, 0.19f), 0.1f, 0.45f),
                NoReflections(Lit("ChamberTrayWindow", new Color(0.9f, 0.96f, 1f, 0.04f), 0f, 0.5f, true, true)),
                topsoil, subsoil, deep,
                Lit("ChamberRoots", new Color(0.95f, 0.78f, 0.48f), 0f, 0.3f, false, true),
                Solid("ChamberFloorPlate", new Color(0.86f, 0.87f, 0.88f), 0.5f),
                Solid("ChamberBench", new Color(0.92f, 0.93f, 0.93f), 0.45f),
                Solid("ChamberPedestal", new Color(0.82f, 0.84f, 0.86f), 0.4f),
                MaterialFactory.Unlit($"{Materials}/ChamberWetSoil.mat", new Color(0.04f, 0.025f, 0.015f, 0.78f), true));
            chamber.BindDetail(Lit("ChamberSealRubber", new Color(0.05f, 0.05f, 0.055f), 0f, 0.25f), ShadowMaterial(),
                MaterialFactory.Unlit($"{Materials}/ChamberRootHaze.mat", new Color(0.16f, 0.1f, 0.06f, 0.42f), true));
            chamber.EnsureParts();
            chamber.Apply(ChamberFit.Default());
            return chamber;
        }

        // ------------------------------------------------------------------ control panel

        static TMP_Text Text(RectTransform parent, string name, string text, float size, Color color, float x, float y, float w, float h,
            FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, float minSize = 0f)
        {
            var label = TreatmentSceneBuilder.Text(parent, name, text, size, color, x, y, w, h, style, alignment);
            if (minSize > 0f)
            {
                label.enableAutoSizing = true;
                label.fontSizeMin = minSize;
                label.fontSizeMax = size;
            }

            return label;
        }

        static Button Button(RectTransform parent, string name, string label, float size, float x, float y, float w, float h, out SelectedStateVisual state,
            bool primary = false)
        {
            var button = primary ? UIFactory.Button(name, parent, label, size, out state, UIPalette.Cyan, Ink) : UIFactory.Button(name, parent, label, size, out state);
            var text = button.GetComponentInChildren<TMP_Text>();
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Min(14f, size);
            text.fontSizeMax = size;
            text.fontStyle = FontStyles.Bold;
            UIFactory.Place((RectTransform)button.transform, x, y, w, h);
            return button;
        }

        /// <summary>Smallest body-text size on the kiosk (canvas units; 32 x 0.8 mm = 25.6 mm em, about 18 mm cap height).</summary>
        public const float KioskBody = 32f;

        /// <summary>Kiosk text: fixed size or auto-sized down to <paramref name="minSize"/>, never below body size; long text ends in an ellipsis.</summary>
        static TMP_Text KText(RectTransform parent, string name, string text, float size, Color color, float x, float y, float w, float h,
            FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, float minSize = 0f)
        {
            var label = TreatmentSceneBuilder.Text(parent, name, text, size, color, x, y, w, h, style, alignment);
            var min = Mathf.Max(minSize > 0f ? minSize : size, KioskBody);
            if (min < size)
            {
                label.enableAutoSizing = true;
                label.fontSizeMin = min;
                label.fontSizeMax = size;
            }

            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        /// <summary>A kiosk button (88+ units = 70+ mm): its label never shrinks below <paramref name="minSize"/>; two lines allowed.</summary>
        static Button KButton(RectTransform parent, string name, string label, float size, float x, float y, float w, float h, out SelectedStateVisual state,
            bool primary = false, float minSize = 28f)
        {
            var button = Button(parent, name, label, size, x, y, w, h, out state, primary);
            var text = button.GetComponentInChildren<TMP_Text>();
            text.fontSizeMin = Mathf.Min(size, minSize);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }

        /// <summary>
        /// Addendum 6: the treatment kiosk. A landscape touchscreen (1280 x 760 units at 0.8 mm = 1.02 x 0.61 m, buttons
        /// 70 mm and up, body text 25.6 mm) tilted back 45 degrees on a pedestal cabinet with a base plate, a cyan accent
        /// and a "TREATMENT CONTROLS" topper readable across the hall. Page area (below the 96-unit header strip): the left
        /// column holds the page (title band, two [-] value [+] rows or the Soil Moisture page), the right column the 2 x 2
        /// tabs, the readout, the status line, Apply / Check Health and the result.
        /// </summary>
        static ChamberControlPanel BuildPanel(Transform root, out Transform panelRoot, out Transform canvasTransform)
        {
            var stand = new GameObject("ControlPanelStand").transform;
            stand.SetParent(root, false);
            panelRoot = stand;
            var metal = Lit("PanelStandMetal", new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.5f);
            var dark = Solid("PanelStandBase", new Color(0.18f, 0.19f, 0.2f), 0.3f);
            var cabinet = Lit("KioskCabinet", new Color(0.2f, 0.21f, 0.23f), 0.4f, 0.45f);
            var accent = Glow("ChamberAccent", UIPalette.Cyan, 2.6f);
            Part(PrimitiveType.Cube, "BasePlate", stand, new Vector3(0f, 0.015f, 0.02f), new Vector3(1.0f, 0.03f, 0.72f), dark);
            Part(PrimitiveType.Cube, "BaseAccent", stand, new Vector3(0f, 0.033f, -0.345f), new Vector3(0.98f, 0.012f, 0.02f), accent);
            Part(PrimitiveType.Cube, "Cabinet", stand, new Vector3(0f, 0.355f, 0.16f), new Vector3(0.5f, 0.65f, 0.3f), cabinet);
            Part(PrimitiveType.Cube, "CabinetAccent", stand, new Vector3(0f, 0.62f, 0.008f), new Vector3(0.46f, 0.014f, 0.006f), accent);
            Part(PrimitiveType.Cube, "Neck", stand, new Vector3(0f, 0.9f, 0.17f), new Vector3(0.16f, 0.54f, 0.12f), metal);
            BlobShadow(stand, new Vector3(0f, 0.002f, 0.02f), new Vector2(1.3f, 1.0f));

            // Topper sign behind the screen's top edge: the station's name, readable across the hall.
            foreach (var x in new[] { -0.42f, 0.42f })
                Part(PrimitiveType.Cube, "TopperPost", stand, new Vector3(x, 1.47f, 0.31f), new Vector3(0.03f, 0.22f, 0.03f), metal);
            Part(PrimitiveType.Cube, "TopperBoard", stand, new Vector3(0f, 1.68f, 0.32f), new Vector3(1.12f, 0.22f, 0.04f), cabinet);
            Part(PrimitiveType.Cube, "TopperAccent", stand, new Vector3(0f, 1.565f, 0.298f), new Vector3(1.1f, 0.012f, 0.01f), accent);
            StationLabel(stand, "TopperLabel", "TREATMENT CONTROLS", new Vector3(0f, 1.68f, 0.298f), new Vector2(1080f, 200f), 86f);

            var w = PanelWidthMm;
            var canvas = UIFactory.WorldCanvas("ControlPanel", stand, new Vector2(w, PanelHeightMm), new Vector3(0f, ChamberStation.PanelHeight, 0f),
                Quaternion.Euler(ChamberStation.PanelTilt, 0f, 0f));
            canvasTransform = canvas.transform;
            canvas.transform.localScale = Vector3.one * ChamberStation.PanelScale;
            // Matte charcoal bezel with a slight bevel (a smaller front lip on a deeper back shell), behind the screen.
            var bezel = Part(PrimitiveType.Cube, "Bezel", canvas.transform, new Vector3(0f, -ScreenStrip * 0.5f, 26f),
                new Vector3(w + 44f, PanelHeightMm + ScreenStrip + 44f, 34f), dark);
            bezel.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            Part(PrimitiveType.Cube, "BezelLip", canvas.transform, new Vector3(0f, -ScreenStrip * 0.5f, 6f),
                new Vector3(w + 22f, PanelHeightMm + ScreenStrip + 22f, 10f), Solid("ScreenBezelLip", new Color(0.1f, 0.105f, 0.11f), 0.2f));
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect, Charcoal, false);
            var edge = UIFactory.RoundedImage("CyanEdge", rect, UIPalette.Cyan, ProceduralTextures.RoundedOutline);
            UIFactory.Stretch(edge.rectTransform, -4f);
            edge.raycastTarget = false;

            // Left column: bright cyan title band with the page title (dark text), as in the references.
            var band = UIFactory.RoundedImage("HeaderBand", rect, UIPalette.Cyan, ProceduralTextures.RoundedFill);
            band.raycastTarget = false;
            UIFactory.Place(band.rectTransform, 16f, 6f, 768f, 96f);
            var header = KText(rect, "Header", "Soil Moisture", 62f, Ink, 40f, 10f, 530f, 88f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 44f);
            var scenario = KButton(rect, "Scenario", "Standard", 32f, 582f, 10f, 194f, 88f, out _);
            var scenarioLabel = scenario.GetComponentInChildren<TMP_Text>();
            scenario.gameObject.SetActive(false);

            // Generic two-setting page (Temperature, Fertiliser, Spray), with a faint band behind each row so a label and
            // its buttons and value read as one row from any angle.
            var controls = UIFactory.Node("Controls", rect);
            UIFactory.Stretch(controls);
            var info = KText(controls, "PageInfo", "", KioskBody, UIPalette.TextSecondary, 30f, 108f, 740f, 84f);
            var rows = new List<ChamberControlPanel.Row>();
            for (var r = 0; r < 2; r++)
            {
                var y = 200f + r * 150f;
                var node = UIFactory.Node("Row" + (r + 1), controls);
                UIFactory.Stretch(node);
                var stripe = UIFactory.Rectangle("RowBand", node, new Color(1f, 1f, 1f, 0.045f));
                UIFactory.Place(stripe.rectTransform, 16f, y - 4f, 768f, 140f);
                var label = KText(node, "Label", "", 38f, Color.white, 30f, y, 330f, 88f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 32f);
                var minus = KButton(node, "Minus", "−", 54f, 368f, y, 88f, 88f, out _);
                var valueBox = UIFactory.RoundedImage("ValueBox", node, new Color(0.07f, 0.08f, 0.09f, 1f), ProceduralTextures.RoundedFill);
                valueBox.raycastTarget = false;
                UIFactory.Place(valueBox.rectTransform, 464f, y, 220f, 88f);
                var value = KText(node, "Value", "", 46f, Color.white, 468f, y, 212f, 88f, FontStyles.Bold, TextAlignmentOptions.Center, 34f);
                var plus = KButton(node, "Plus", "+", 54f, 692f, y, 88f, 88f, out _);
                var target = KText(node, "Target", "", KioskBody, UIPalette.TextSecondary, 368f, y + 92f, 412f, 44f, FontStyles.Italic, TextAlignmentOptions.Center);
                var feedback = KText(node, "Feedback", "", KioskBody, UIPalette.HealthGood, 30f, y + 92f, 334f, 44f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                rows.Add(new ChamberControlPanel.Row
                {
                    root = node.gameObject, label = label, minus = minus, minusLabel = minus.GetComponentInChildren<TMP_Text>(), value = value, plus = plus,
                    plusLabel = plus.GetComponentInChildren<TMP_Text>(), target = target, feedback = feedback,
                });
            }

            var note = KText(controls, "PageNote", "", KioskBody, new Color(0.86f, 0.9f, 0.93f), 30f, 498f, 740f, 44f, FontStyles.Normal, TextAlignmentOptions.Center);
            var status = KText(controls, "PageStatus", "", 46f, UIPalette.HealthGood, 30f, 546f, 740f, 64f, FontStyles.Bold | FontStyles.Italic,
                TextAlignmentOptions.Center, 34f);

            var soil = BuildSoilPage(rect, 800f);

            var message = KText(rect, "Message", "", 36f, UIPalette.TextSecondary, 40f, 112f, 720f, 380f, FontStyles.Normal, TextAlignmentOptions.Center, 32f);
            var retry = KButton(rect, "Retry", "Retry", 36f, 250f, 510f, 300f, 96f, out _, true);
            retry.gameObject.SetActive(false);

            // Right column: 2 x 2 tabs, the readout and status, Apply / Check Health and the result.
            const float rx = 816f, rw = 448f;
            var tabs = new List<Button>();
            var tabStates = new List<SelectedStateVisual>();
            for (var i = 0; i < 4; i++)
            {
                var tab = KButton(rect, "Tab" + (i + 1), $"{i + 1} {ChamberControlPanel.TabTitles[i]}", 32f, rx + (i % 2) * 228f, 8f + (i / 2) * 100f, 220f, 92f,
                    out var state, false, KioskBody);
                tabs.Add(tab);
                tabStates.Add(state);
            }

            var divider = UIFactory.Rectangle("Divider", rect, new Color(1f, 1f, 1f, 0.18f));
            UIFactory.Place(divider.rectTransform, rx, 206f, rw, 2f);
            var readout = KText(rect, "Readout", "", KioskBody, UIPalette.TextSecondary, rx, 212f, rw, 84f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            var statusLine = KText(rect, "StatusLine", "", KioskBody, UIPalette.TextSecondary, rx, 298f, rw, 84f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            var apply = KButton(rect, "Apply", "Apply", 38f, rx, 390f, 220f, 96f, out _, true);
            var check = KButton(rect, "CheckHealth", "Check Health", 34f, rx + 228f, 390f, 220f, 96f, out _);
            var headline = KText(rect, "ResultHeadline", "", 36f, UIPalette.HealthGood, rx, 494f, rw, 84f, FontStyles.Bold, TextAlignmentOptions.TopLeft, KioskBody);
            var resultFeedback = KText(rect, "ResultFeedback", "", KioskBody, Color.white, rx, 580f, rw, 84f, FontStyles.Normal, TextAlignmentOptions.TopLeft);

            var panel = canvas.gameObject.AddComponent<ChamberControlPanel>();
            panel.Bind(header, scenario, scenarioLabel, tabs, tabStates, controls.gameObject, info, rows, note, status, message, retry, readout, statusLine,
                apply, check, headline, resultFeedback);
            panel.BindSoil(soil);
            return panel;
        }

        /// <summary>A station's name on a world-space sign (no raycaster), facing -Z of <paramref name="parent"/> (towards the user).</summary>
        static TMP_Text StationLabel(Transform parent, string name, string text, Vector3 position, Vector2 sizeMm, float fontMm, Color? color = null)
        {
            var canvas = UIFactory.WorldCanvas(name, parent, sizeMm, position, Quaternion.identity);
            Object.DestroyImmediate(canvas.GetComponent<TrackedDeviceGraphicRaycaster>());
            var label = UIFactory.Text("Text", canvas.transform, text, fontMm, color ?? Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = fontMm * 0.8f;
            label.fontSizeMax = fontMm;
            UIFactory.Stretch(label.rectTransform);
            return label;
        }

        public const float ScreenStrip = 96f;

        /// <summary>
        /// Batch entry: bakes the static hall (lightmaps with modest settings, the hall lights baked, the light probes and the
        /// reflection probe) and saves Treatment.unity. Run after BuildScene (rebuilding the scene drops the bake).
        /// </summary>
        [MenuItem("BTP/Treatment/6 - Bake Chamber Hall Lighting", priority = 45)]
        public static void BakeLighting()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ProjectPaths.TreatmentScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var hall = GameObject.Find("ChamberHall/ChamberLine/Hall");
            if (hall == null)
                throw new System.InvalidOperationException("Treatment.unity has no chamber hall; run BTP/Treatment/3 first.");
            var statics = 0;
            foreach (var renderer in hall.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<LODGroup>() != null)
                    continue; // the Farm-view plants are scenery, lit by probes
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.renderQueue >= 3000)
                    continue; // glass and glows stay dynamic
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
                statics++;
            }

            foreach (var light in hall.GetComponentsInChildren<Light>(true))
                light.lightmapBakeType = LightmapBakeType.Baked;

            var settings = Lightmapping.lightingSettings ?? new LightingSettings { name = "TreatmentLighting" };
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution = 4f;
            settings.lightmapMaxSize = 1024;
            settings.directSampleCount = 16;
            settings.indirectSampleCount = 64;
            settings.environmentSampleCount = 64;
            settings.maxBounces = 1;
            settings.ao = true;
            settings.aoMaxDistance = 0.6f;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            Lightmapping.lightingSettings = settings;
            var started = System.DateTime.Now;
            var ok = Lightmapping.Bake();
            var seconds = (System.DateTime.Now - started).TotalSeconds;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            var count = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0;
            var size = count > 0 && LightmapSettings.lightmaps[0].lightmapColor != null ? LightmapSettings.lightmaps[0].lightmapColor.width : 0;
            Debug.Log($"[BTP] Chamber hall bake {(ok ? "done" : "FAILED")} in {seconds:0} s: {statics} static renderers, {count} lightmap(s) of {size} px.");
        }
        const string ThirdParty = ProjectPaths.Root + "/ThirdParty";
        const string SprayerModel = "Assets/Lab_Treatment_Assets/spray-bottle/model/SprayBottle_Quest.fbx";
        const string Hdri = ThirdParty + "/PolyHaven/aloe_farm_shade_house/aloe_farm_shade_house_2k.hdr";

        /// <summary>Import settings for a downloaded map: normal maps as normals, 1K max, Android ASTC at 1024.</summary>
        static Texture2D ImportMap(string path, bool normal)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[BTP] Missing third-party texture {path}; see ThirdParty/CREDITS.md.");
                return null;
            }

            var dirty = false;
            if (normal && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                dirty = true;
            }

            if (importer.maxTextureSize != 1024)
            {
                importer.maxTextureSize = 1024;
                dirty = true;
            }

            var android = importer.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.maxTextureSize != 1024 || android.format != TextureImporterFormat.ASTC_6x6)
            {
                android.overridden = true;
                android.maxTextureSize = 1024;
                android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                dirty = true;
            }

            if (dirty)
                importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A URP Lit material from an ambientCG set (prefix + "_Color.jpg", "_NormalGL.jpg", optional "_AmbientOcclusion.jpg").</summary>
        static Material Pbr(string name, string prefix, Color tint, float metallic, float smoothness, Vector2 tiling, bool occlusion = false,
            bool doubleSided = false)
        {
            var albedo = ImportMap(prefix + "_Color.jpg", false);
            var normal = ImportMap(prefix + "_NormalGL.jpg", true);
            var material = MaterialFactory.Lit($"{Materials}/{name}.mat", tint, metallic, smoothness, albedo, normal, doubleSided: doubleSided);
            material.SetTextureScale("_BaseMap", tiling);
            if (occlusion)
            {
                var ao = ImportMap(prefix + "_AmbientOcclusion.jpg", false);
                material.SetTexture("_OcclusionMap", ao);
                if (ao != null)
                    material.EnableKeyword("_OCCLUSIONMAP");
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        static Material PbrPine(string name, Vector2 tiling)
        {
            var albedo = ImportMap(ThirdParty + "/PolyHaven/coated_pine/coated_pine_diff_1k.jpg", false);
            var normal = ImportMap(ThirdParty + "/PolyHaven/coated_pine/coated_pine_nor_gl_1k.jpg", true);
            var material = MaterialFactory.Lit($"{Materials}/{name}.mat", Color.white, 0f, 0.35f, albedo, normal);
            material.SetTextureScale("_BaseMap", tiling);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Reflection probe using the CC0 aloe_farm_shade_house HDRI (daylight reflections on glass and steel; the windows still
        /// show the scene's own scenery) and a light-probe grid around the chamber zone for the dynamic specimen and chamber.
        /// </summary>
        static void BuildProbes(Transform line)
        {
            var importer = AssetImporter.GetAtPath(Hdri) as TextureImporter;
            Cubemap cubemap = null;
            if (importer != null)
            {
                if (importer.textureShape != TextureImporterShape.TextureCube)
                {
                    importer.textureShape = TextureImporterShape.TextureCube;
                    importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                    importer.maxTextureSize = 1024;
                    importer.SaveAndReimport();
                }

                cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(Hdri);
            }

            var probe = new GameObject("HallReflectionProbe", typeof(ReflectionProbe)).GetComponent<ReflectionProbe>();
            probe.transform.SetParent(line, false);
            probe.transform.localPosition = new Vector3((HallLeft + HallRight) * 0.5f, 1.6f, (HallFront + HallBack) * 0.5f);
            probe.size = new Vector3(HallRight - HallLeft, HallHeight, HallBack - HallFront);
            probe.boxProjection = true;
            probe.intensity = 0.4f;
            if (cubemap != null)
            {
                probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
                probe.customBakedTexture = cubemap;
            }
            else
            {
                probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            }

            var probes = new GameObject("ChamberLightProbes", typeof(LightProbeGroup)).GetComponent<LightProbeGroup>();
            probes.transform.SetParent(line, false);
            var positions = new List<Vector3>();
            for (var x = HallLeft + 0.5f; x <= HallRight - 0.4f; x += 1.5f)
            for (var z = HallFront + 0.5f; z <= HallBack - 0.4f; z += 1.5f)
            foreach (var y in new[] { 0.3f, 1.2f, 2.4f, 4.0f })
                positions.Add(new Vector3(x, y, z));
            probes.probePositions = positions.ToArray();
        }

        /// <summary>The matte training sprayer (the project's own spray bottle model) shown beside the tray for spray cases.</summary>
        static Transform BuildSprayerProp(Transform root)
        {
            var go = new GameObject("TrainingSprayerProp");
            go.transform.SetParent(root, false);
            var matte = Lit("SprayerMatteWhite", new Color(0.9f, 0.91f, 0.92f), 0f, 0.2f);
            var trigger = Lit("SprayerMatteSage", new Color(0.42f, 0.55f, 0.43f), 0f, 0.25f);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SprayerModel);
            if (model != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
                instance.name = "Model";
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materials = new Material[renderer.sharedMaterials.Length];
                    for (var i = 0; i < materials.Length; i++)
                        materials[i] = renderer.name.StartsWith("Cube") || renderer.name == "Cylinder.002" ? trigger : matte;
                    renderer.sharedMaterials = materials;
                }

                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.015f, instance.GetComponentsInChildren<Renderer>(true)) });
            }

            // Nozzle tip at the front of the trigger head; the mist and the coverage rays leave along its +Z.
            var nozzle = new GameObject("Nozzle").transform;
            nozzle.SetParent(go.transform, false);
            nozzle.SetLocalPositionAndRotation(new Vector3(0.046f, 0.258f, 0f), Quaternion.Euler(0f, 90f, 0f));
            var mist = new GameObject("Mist", typeof(MeshFilter), typeof(MeshRenderer));
            mist.transform.SetParent(root, false);
            var mistRenderer = mist.GetComponent<MeshRenderer>();
            mistRenderer.sharedMaterial = Lit("SprayMistDroplets", new Color(0.9f, 0.96f, 1f, 0.55f), 0f, 0.8f, true, true);
            mistRenderer.shadowCastingMode = ShadowCastingMode.Off;
            mistRenderer.enabled = false;
            go.AddComponent<TrainingSprayTool>().Bind(nozzle, mist.GetComponent<MeshFilter>(), mistRenderer);
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>
        /// Addendum 4/6: turns the control panel into the lab's single touchscreen. A header strip carries the case (plant /
        /// disease, scenario, specimen), the connection dot and label (Offline - Retry is pressable) and the Menu button;
        /// the treatment pages move below it. The menu holds Choose a Case (the case selector, moved here from its own
        /// floating panel), Return to Lab, Recenter (the overview) and "Go to" the other stations. One modal (the XR scan
        /// switch) covers everything; while it is open the Body's CanvasGroup takes no input. The old floating navigation
        /// panel is removed.
        /// </summary>
        static void BuildScreenFrame(ChamberControlPanel panel, Transform ui)
        {
            var rect = (RectTransform)panel.transform;
            var w = PanelWidthMm;
            var height = PanelHeightMm + ScreenStrip;
            var keep = new HashSet<string> { "Background", "CyanEdge", "Border", "Bezel", "BezelLip" };
            var moved = new List<Transform>();
            foreach (Transform child in rect)
                if (!keep.Contains(child.name))
                    moved.Add(child);
            rect.sizeDelta = new Vector2(w, height);
            // The cyan edge and the background stretch with the canvas; make the background fully opaque (matte screen).
            var background = rect.Find("Background")?.GetComponent<Image>();
            if (background != null)
                background.color = new Color(0.1f, 0.115f, 0.13f, 1f);

            var body = UIFactory.Node("Body", rect);
            UIFactory.Stretch(body);
            var group = body.gameObject.AddComponent<CanvasGroup>();
            var pages = UIFactory.Node("Pages", body);
            UIFactory.Place(pages, 0f, ScreenStrip, w, PanelHeightMm);
            foreach (var child in moved)
                child.SetParent(pages, false);

            // Header strip: case, connection, menu.
            var strip = UIFactory.Node("Header", body);
            UIFactory.Place(strip, 0f, 0f, w, ScreenStrip);
            var caseTitle = KText(strip, "Case", "Rice / Brown spot", 42f, Color.white, 24f, 2f, 690f, 52f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 34f);
            caseTitle.textWrappingMode = TextWrappingModes.NoWrap;
            var caseScenario = KText(strip, "Scenario", "", KioskBody, UIPalette.Cyan, 24f, 52f, 250f, 42f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var caseSpecimen = KText(strip, "Specimen", "", KioskBody, UIPalette.TextSecondary, 284f, 52f, 450f, 42f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            caseSpecimen.textWrappingMode = TextWrappingModes.NoWrap;
            var dot = UIFactory.RoundedImage("ConnectionDot", strip, UIPalette.Warning, ProceduralTextures.RoundedFill);
            dot.raycastTarget = false;
            dot.pixelsPerUnitMultiplier = 6f;
            UIFactory.Place(dot.rectTransform, 744f, 37f, 22f, 22f);
            var connection = UIFactory.Button("Connection", strip, "Loading", 32f, out _, new Color(1f, 1f, 1f, 0.06f), Color.white);
            UIFactory.Place((RectTransform)connection.transform, 776f, 4f, 248f, 88f);
            var connectionText = connection.GetComponentInChildren<TMP_Text>();
            connectionText.enableAutoSizing = true;
            connectionText.fontSizeMin = 28f;
            connectionText.fontSizeMax = 32f;
            var menuButton = KButton(strip, "Menu", "Menu", 36f, w - 240f, 4f, 224f, 88f, out _);

            // Choose a Case page: the existing case selector, without its own canvas.
            var casePage = UIFactory.Node("CasePage", body);
            UIFactory.Place(casePage, 0f, ScreenStrip, w, PanelHeightMm);
            var caseBack = KButton(casePage, "Back", "Back", 36f, w - 236f, 8f, 220f, 88f, out _, true);
            KText(casePage, "Title", "Choose a Case", 46f, Color.white, 24f, 8f, 600f, 88f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var selector = FindChild(ui, "CaseSelectorPanel");
            if (selector != null)
            {
                foreach (var raycaster in selector.GetComponents<UnityEngine.EventSystems.BaseRaycaster>())
                    Object.DestroyImmediate(raycaster);
                Object.DestroyImmediate(selector.GetComponent<CanvasEventCamera>());
                Object.DestroyImmediate(selector.GetComponent<CanvasScaler>());
                Object.DestroyImmediate(selector.GetComponent<Canvas>());
                var selectorRect = (RectTransform)selector;
                selectorRect.SetParent(casePage, false);
                var scale = (PanelHeightMm - 112f) / Mathf.Max(1f, selectorRect.sizeDelta.y);
                selectorRect.anchorMin = selectorRect.anchorMax = new Vector2(0f, 1f);
                selectorRect.pivot = new Vector2(0f, 1f);
                selectorRect.localRotation = Quaternion.identity;
                selectorRect.localScale = Vector3.one * scale;
                selectorRect.anchoredPosition = new Vector2((w - selectorRect.sizeDelta.x * scale) * 0.5f, -104f);
            }

            casePage.gameObject.SetActive(false);

            // The menu (overflow), two columns: Choose a Case, Return to Lab, Recenter, Close | Go to the other stations.
            var menu = UIFactory.Node("MenuSheet", body);
            UIFactory.Place(menu, w - 684f, ScreenStrip - 4f, 668f, 432f);
            UIFactory.Panel(menu, new Color(0.15f, 0.17f, 0.19f, 1f));
            var menuCases = KButton(menu, "ChooseCase", "Choose a Case", 32f, 16f, 16f, 310f, 92f, out _);
            var returnLab = KButton(menu, "ReturnToLab", "Return to Lab", 32f, 16f, 116f, 310f, 92f, out _);
            returnLab.gameObject.AddComponent<EnvironmentButton>().Action = EnvironmentButton.ButtonAction.ReturnToLab;
            var recenter = KButton(menu, "Recenter", "Recenter (overview)", 32f, 16f, 216f, 310f, 92f, out _);
            recenter.gameObject.AddComponent<EnvironmentButton>().Action = EnvironmentButton.ButtonAction.Recenter;
            var menuClose = KButton(menu, "CloseMenu", "Close", 32f, 16f, 316f, 310f, 92f, out _);
            var goButtons = new List<Button>
            {
                KButton(menu, "GoWater", "Go to WATER CONTROL", 30f, 342f, 16f, 310f, 92f, out _),
                KButton(menu, "GoTools", "Go to TREATMENT TOOLS", 30f, 342f, 116f, 310f, 92f, out _),
                KButton(menu, "GoPlant", "Go to the plant chamber", 30f, 342f, 216f, 310f, 92f, out _),
            };
            menu.gameObject.SetActive(false);

            // The single modal, above the body.
            var modal = UIFactory.Node("Modal", rect);
            UIFactory.Stretch(modal);
            var shade = modal.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.62f);
            var modalGroup = modal.gameObject.AddComponent<CanvasGroup>();
            var box = UIFactory.Node("Box", modal);
            UIFactory.Place(box, 140f, 130f, w - 280f, 500f);
            UIFactory.Panel(box, new Color(0.12f, 0.135f, 0.15f, 1f));
            var modalTitle = KText(box, "Title", "Open a different scan?", 44f, Color.white, 30f, 22f, w - 340f, 60f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 34f);
            var modalBody = KText(box, "Body", "", KioskBody, UIPalette.TextSecondary, 30f, 92f, w - 340f, 270f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            var buttonWidth = (w - 280f - 90f) * 0.5f;
            var primary = KButton(box, "OpenNewScan", "Open New Scan", 34f, 30f, 380f, buttonWidth, 96f, out _, true);
            var secondary = KButton(box, "KeepCurrentScan", "Keep Current Scan", 34f, 60f + buttonWidth, 380f, buttonWidth, 96f, out _);
            modal.gameObject.SetActive(false);

            panel.BindScreen(new ChamberControlPanel.Screen
            {
                body = group, caseTitle = caseTitle, caseScenario = caseScenario, caseSpecimen = caseSpecimen, connectionDot = dot,
                connectionLabel = connectionText, connectionButton = connection, menuButton = menuButton, menu = menu.gameObject, menuCases = menuCases,
                menuClose = menuClose, casePage = casePage.gameObject, casePageClose = caseBack, pageRoot = pages.gameObject, modal = modalGroup,
                modalTitle = modalTitle, modalBody = modalBody, modalPrimary = primary, modalSecondary = secondary, goButtons = goButtons,
            });

            // Only the touchscreen takes input: the floating navigation panel goes, hidden panels lose their raycasters.
            var navigation = FindChild(ui, "NavigationPanel");
            if (navigation != null)
                Object.DestroyImmediate(navigation.gameObject);
            foreach (var raycaster in ui.GetComponentsInChildren<UnityEngine.EventSystems.BaseRaycaster>(true))
                raycaster.enabled = false;
            foreach (var canvas in ui.GetComponentsInChildren<Canvas>(true))
            {
                var hidden = canvas.gameObject.GetComponent<CanvasGroup>();
                if (hidden == null)
                    hidden = canvas.gameObject.AddComponent<CanvasGroup>();
                hidden.blocksRaycasts = false;
                hidden.interactable = false;
            }
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        /// <summary>A soft dark ellipse on the floor (a cheap contact shadow; no SSAO on Quest).</summary>
        static GameObject BlobShadow(Transform parent, Vector3 position, Vector2 size)
        {
            var go = EnvironmentKit.MeshObject("ContactShadow", parent, ProceduralMeshes.GroundQuad("ContactShadowQuad", Vector2.one, new Rect(0f, 0f, 1f, 1f)),
                ShadowMaterial(), position, Quaternion.identity, new Vector3(size.x, 1f, size.y), false, false);
            return go;
        }

        static Material shadowMaterial;

        static Material ShadowMaterial()
        {
            if (shadowMaterial != null)
                return shadowMaterial;
            const string texturePath = Materials + "/ContactShadow.png";
            if (!System.IO.File.Exists(texturePath))
            {
                const int size = 128;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f) / size * 2f - 1f;
                    var dy = (y + 0.5f) / size * 2f - 1f;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(1f - d);
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, a * a * 0.85f));
                }

                ProjectPaths.EnsureFolder(Materials);
                System.IO.File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            shadowMaterial = MaterialFactory.Unlit($"{Materials}/ContactShadow.mat", Color.white, true);
            shadowMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            EditorUtility.SetDirty(shadowMaterial);
            return shadowMaterial;
        }

        /// <summary>
        /// The Soil Moisture page (left column of the kiosk): "Water to add [-] [20%] [+]", then Current moisture, Setpoint and
        /// Target range, each label and its value box inside one faint row band (Addendum 6: from an oblique angle a value
        /// can no longer seem to belong to the next row), the cyan italic status line and the leaf-wetness line.
        /// </summary>
        static ChamberControlPanel.SoilPage BuildSoilPage(RectTransform rect, float w)
        {
            var node = UIFactory.Node("SoilPage", rect);
            UIFactory.Stretch(node);
            var boxColor = new Color(0.07f, 0.08f, 0.09f, 1f);
            var boxEdge = new Color(1f, 1f, 1f, 0.55f);

            void Band(string name, float y, float h)
            {
                var band = UIFactory.Rectangle(name + "Band", node, new Color(1f, 1f, 1f, 0.045f));
                UIFactory.Place(band.rectTransform, 16f, y - 3f, w - 32f, h + 6f);
            }

            TMP_Text Box(string name, float x, float y, float width, float h)
            {
                var box = UIFactory.RoundedImage(name + "Box", node, boxColor, ProceduralTextures.RoundedFill);
                box.raycastTarget = false;
                UIFactory.Place(box.rectTransform, x, y, width, h);
                var outline = UIFactory.RoundedImage("Outline", box.transform, boxEdge, ProceduralTextures.RoundedOutline);
                UIFactory.Stretch(outline.rectTransform);
                outline.raycastTarget = false;
                return KText(node, name, "", 44f, Color.white, x + 4f, y, width - 8f, h, FontStyles.Bold, TextAlignmentOptions.Center, 34f);
            }

            void Divider(float y)
            {
                var line = UIFactory.Rectangle("Divider", node, new Color(1f, 1f, 1f, 0.2f));
                UIFactory.Place(line.rectTransform, 30f, y, w - 60f, 2f);
            }

            const float valueX = 464f, valueWidth = 316f, rowH = 80f;
            Band("Amount", 108f, 88f);
            KText(node, "AmountLabel", "Water to add", 40f, Color.white, 30f, 108f, 330f, 88f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 34f);
            var minus = KButton(node, "AmountMinus", "−", 54f, 368f, 108f, 88f, 88f, out _);
            var amount = Box("Amount", 464f, 108f, 220f, 88f);
            var plus = KButton(node, "AmountPlus", "+", 54f, 692f, 108f, 88f, 88f, out _);
            minus.gameObject.AddComponent<HoldRepeatButton>();
            plus.gameObject.AddComponent<HoldRepeatButton>();
            Divider(202f);
            Band("Current", 208f, rowH);
            KText(node, "CurrentLabel", "Current moisture", 40f, Color.white, 30f, 208f, 420f, rowH, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 34f);
            var current = Box("Current", valueX, 208f, valueWidth, rowH);
            Divider(294f);
            Band("Setpoint", 300f, rowH);
            KText(node, "SetpointLabel", "Setpoint", 40f, Color.white, 30f, 300f, 420f, rowH, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 34f);
            var setpoint = Box("Setpoint", valueX, 300f, valueWidth, rowH);
            Divider(386f);
            Band("Target", 392f, rowH);
            KText(node, "TargetLabel", "Target range", 40f, Color.white, 30f, 392f, 420f, rowH, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 34f);
            var target = Box("Target", valueX, 392f, valueWidth, rowH);
            Divider(478f);
            var status = KText(node, "Status", "", 50f, UIPalette.Cyan, 30f, 484f, w - 60f, 64f, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center, 34f);
            var leaf = KText(node, "LeafWetness", "", 38f, new Color(0.8f, 0.85f, 0.9f), 30f, 556f, 370f, 48f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            var feedback = KText(node, "Feedback", "", 38f, UIPalette.HealthGood, 410f, 556f, w - 440f, 48f, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            node.gameObject.SetActive(false);
            return new ChamberControlPanel.SoilPage
            {
                root = node.gameObject, amountMinus = minus, amountPlus = plus, amount = amount, current = current, setpoint = setpoint, target = target,
                status = status, leafWetness = leaf, feedback = feedback,
            };
        }

        // ------------------------------------------------------------------ sign, card, marker, soil label

        static Transform BuildSign(Transform root, out TMP_Text title, out TMP_Text scenario, out TMP_Text specimen, out GameObject card, out TMP_Text cardBody)
        {
            var sign = new GameObject("CaseSign").transform;
            sign.SetParent(root, false);
            var size = new Vector2(760f, 250f);
            var canvas = UIFactory.WorldCanvas("Sign", sign, size, Vector3.zero, Quaternion.identity);
            canvas.transform.localScale = Vector3.one * 0.00072f;
            Object.DestroyImmediate(canvas.GetComponent<TrackedDeviceGraphicRaycaster>());
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect, new Color(0.15f, 0.16f, 0.17f, 0.97f), false);
            var outline = UIFactory.RoundedImage("Border", rect, new Color(0.8f, 0.82f, 0.84f, 1f), ProceduralTextures.RoundedOutline);
            UIFactory.Stretch(outline.rectTransform);
            outline.raycastTarget = false;
            var accent = UIFactory.RoundedImage("Accent", rect, UIPalette.Cyan, ProceduralTextures.RoundedFill);
            accent.raycastTarget = false;
            UIFactory.Place(accent.rectTransform, 30f, 40f, 12f, 150f);
            title = Text(rect, "Title", "Tomato / Early blight", 66f, Color.white, 64f, 22f, 670f, 92f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 34f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            scenario = Text(rect, "Scenario", "", 52f, UIPalette.Cyan, 64f, 112f, 670f, 70f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 28f);
            specimen = Text(rect, "Specimen", "", 26f, UIPalette.TextSecondary, 64f, 188f, 670f, 44f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, 16f);

            // Pole from the sign's bottom to the floor.
            var pole = Part(PrimitiveType.Cylinder, "SignPole", sign, Vector3.zero, Vector3.one, Lit("SignPole", new Color(0.78f, 0.8f, 0.82f), 0.5f, 0.5f));
            var bottom = 2.12f - size.y * UIFactory.MillimetreScale * 0.5f;
            pole.transform.localPosition = new Vector3(0.24f, -2.12f + bottom * 0.5f, 0.04f);
            pole.transform.localScale = new Vector3(0.055f, bottom * 0.5f, 0.055f);

            // The compact Fertiliser card under the sign (shown with the Fertiliser page).
            var cardSize = new Vector2(600f, 170f);
            var cardCanvas = UIFactory.WorldCanvas("FertiliserCard", sign, cardSize, new Vector3(0f, 0.2f, 0f), Quaternion.identity);
            cardCanvas.transform.localScale = Vector3.one * 0.0006f;
            Object.DestroyImmediate(cardCanvas.GetComponent<TrackedDeviceGraphicRaycaster>());
            var cardRect = (RectTransform)cardCanvas.transform;
            UIFactory.Panel(cardRect, Charcoal, false);
            var cardEdge = UIFactory.RoundedImage("CyanEdge", cardRect, UIPalette.Cyan, ProceduralTextures.RoundedOutline);
            UIFactory.Stretch(cardEdge.rectTransform, -2f);
            cardEdge.raycastTarget = false;
            Text(cardRect, "Heading", "Fertiliser", 44f, Color.white, 30f, 14f, 540f, 56f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            cardBody = Text(cardRect, "Body", "", 30f, Color.white, 30f, 72f, 545f, 88f, FontStyles.Italic, TextAlignmentOptions.TopLeft, 16f);
            card = cardCanvas.gameObject;
            card.SetActive(false);
            return sign;
        }

        static Transform BuildSprayMarker(Transform root, out TMP_Text label)
        {
            var marker = new GameObject("SprayTargetMarker").transform;
            marker.SetParent(root, false);
            var mesh = ProceduralMeshes.Ring("SprayTargetRing", 0.088f, 0.1f);
            EnvironmentKit.MeshObject("Ring", marker, mesh, Glow("SprayTargetRing", UIPalette.Cyan, 2.4f), Vector3.zero, Quaternion.Euler(-90f, 0f, 0f),
                Vector3.one, false, false);
            var canvas = UIFactory.WorldCanvas("Label", marker, new Vector2(260f, 44f), new Vector3(0f, 0.125f, 0f), Quaternion.identity);
            Object.DestroyImmediate(canvas.GetComponent<TrackedDeviceGraphicRaycaster>());
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect, new Color(0.07f, 0.1f, 0.13f, 0.75f), false);
            label = UIFactory.Text("Text", rect, "", 18f, UIPalette.Cyan, TextAlignmentOptions.Center, FontStyles.Bold);
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 18f;
            UIFactory.Stretch(label.rectTransform, 4f);
            marker.gameObject.SetActive(false);
            return marker;
        }

        static TMP_Text BuildSoilLabel(Transform root)
        {
            var canvas = UIFactory.WorldCanvas("RootZoneReadout", root, new Vector2(230f, 56f), Vector3.zero, Quaternion.identity);
            Object.DestroyImmediate(canvas.GetComponent<TrackedDeviceGraphicRaycaster>());
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect, new Color(0.07f, 0.1f, 0.13f, 0.88f));
            var text = UIFactory.Text("Text", rect, "", 26f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 26f;
            UIFactory.Stretch(text.rectTransform, 5f);
            return text;
        }

        // ------------------------------------------------------------------ fertiliser packet (a tracked-hand tool) and granules

        static FertiliserApplicator BuildApplicator(Transform root)
        {
            var go = new GameObject("FertiliserApplicator");
            go.transform.SetParent(root, false);
            var applicator = go.AddComponent<FertiliserApplicator>();
            var rig = new GameObject("HandRig").transform;
            rig.SetParent(go.transform, false);
            var wrist = new GameObject("Wrist").transform;
            wrist.SetParent(rig, false);

            var cream = Solid("PacketCream", new Color(0.93f, 0.89f, 0.78f), 0.2f);
            var sage = Solid("PacketSage", new Color(0.49f, 0.62f, 0.4f), 0.2f);
            var seam = Solid("PacketSeam", new Color(0.3f, 0.42f, 0.26f), 0.3f);
            var torn = Solid("PacketTorn", new Color(0.97f, 0.94f, 0.86f), 0.1f);
            var dark = Solid("PacketOpening", new Color(0.18f, 0.16f, 0.12f), 0.1f);

            var packet = new GameObject("Packet").transform;
            packet.SetParent(wrist, false);
            // A large training packet (about 0.33 x 0.45 m), so its label reads from the play position.
            packet.localPosition = new Vector3(0.165f, -0.255f, 0f);
            packet.localScale = Vector3.one * 1.5f;
            const float pw = 0.22f, ph = 0.30f, pt = 0.035f;
            Part(PrimitiveType.Cube, "Body", packet, Vector3.zero, new Vector3(pw, ph, pt), cream);
            Part(PrimitiveType.Cube, "TopBand", packet, new Vector3(0f, ph * 0.5f - 0.0375f, 0f), new Vector3(pw + 0.002f, 0.075f, pt + 0.002f), sage);
            Part(PrimitiveType.Cube, "BottomBand", packet, new Vector3(0f, -ph * 0.5f + 0.0275f, 0f), new Vector3(pw + 0.002f, 0.055f, pt + 0.002f), sage);
            Part(PrimitiveType.Cube, "Zip", packet, new Vector3(0f, ph * 0.5f - 0.062f, 0f), new Vector3(pw + 0.003f, 0.008f, pt + 0.004f), seam);
            Part(PrimitiveType.Cube, "TornCorner", packet, new Vector3(pw * 0.5f - 0.012f, -ph * 0.5f + 0.012f, 0f), new Vector3(0.034f, 0.034f, pt + 0.003f), torn,
                Quaternion.Euler(0f, 0f, 45f));
            Part(PrimitiveType.Cube, "Opening", packet, new Vector3(pw * 0.5f - 0.004f, -ph * 0.5f + 0.004f, 0f), new Vector3(0.026f, 0.01f, pt * 0.6f), dark,
                Quaternion.Euler(0f, 0f, 45f));
            var corner = new GameObject("PourCorner").transform;
            corner.SetParent(packet, false);
            corner.localPosition = new Vector3(pw * 0.5f + 0.004f, -ph * 0.5f - 0.004f, 0f);

            // The data-driven label on the packet's front (towards the viewer).
            var canvas = UIFactory.WorldCanvas("Label", packet, new Vector2(180f, 150f), new Vector3(0.018f, -0.004f, -pt * 0.5f - 0.0015f), Quaternion.identity);
            Object.DestroyImmediate(canvas.GetComponent<TrackedDeviceGraphicRaycaster>());
            var labelRect = (RectTransform)canvas.transform;
            var green = new Color(0.12f, 0.36f, 0.16f);
            var nutrient = UIFactory.Text("Nutrient", labelRect, "POTASSIUM", 56f, green, TextAlignmentOptions.Center, FontStyles.Bold);
            nutrient.textWrappingMode = TextWrappingModes.NoWrap;
            nutrient.enableAutoSizing = true;
            nutrient.fontSizeMin = 12f;
            nutrient.fontSizeMax = 56f;
            UIFactory.Place(nutrient.rectTransform, 4f, 22f, 172f, 66f);
            var product = UIFactory.Text("Product", labelRect, "TRAINING FERTILISER", 20f, green, TextAlignmentOptions.Center, FontStyles.Bold);
            product.textWrappingMode = TextWrappingModes.NoWrap;
            product.enableAutoSizing = true;
            product.fontSizeMin = 8f;
            product.fontSizeMax = 20f;
            UIFactory.Place(product.rectTransform, 4f, 92f, 172f, 30f);

            var granules = new GameObject("Granules", typeof(MeshFilter), typeof(MeshRenderer));
            granules.transform.SetParent(go.transform, false);
            var granuleRenderer = granules.GetComponent<MeshRenderer>();
            granuleRenderer.sharedMaterial = Lit("FertiliserGranule", new Color(0.95f, 0.91f, 0.78f), 0f, 0.35f, false, true);
            granuleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            granuleRenderer.enabled = false;

            applicator.Bind(rig, packet, packet.gameObject, corner, nutrient, product, granules.GetComponent<MeshFilter>(), granuleRenderer);
            rig.gameObject.SetActive(false);
            return applicator;
        }

        // ------------------------------------------------------------------ WATER CONTROL station (Addendum 6)

        /// <summary>
        /// The WATER CONTROL console, a floor station of its own (forward = away from the user): a plinth with a cyan
        /// accent, a cabinet body with vent slots and a sloped deck carrying the "WATER CONTROL" title, big ON / OFF keys
        /// over physical caps (ON glows cyan while water runs), "Water to add" [-] [+20%] [+], a moisture gauge whose
        /// needle follows the root-zone moisture with cyan ticks at the target band, and the level / target / status
        /// lines. A hose port on its right side feeds the floor hose to the gooseneck outlet on the tray rim.
        /// </summary>
        static WaterStation BuildWaterStation(Transform root)
        {
            var go = new GameObject("WaterStation");
            go.transform.SetParent(root, false);
            var station = go.AddComponent<WaterStation>();
            var console = new GameObject("Console").transform;
            console.SetParent(go.transform, false);

            var bodyMaterial = Lit("ConsoleBody", new Color(0.62f, 0.64f, 0.66f), 0.5f, 0.5f);
            var faceMaterial = Lit("ConsoleFace", new Color(0.12f, 0.13f, 0.14f), 0.1f, 0.45f);
            var capIdle = Lit("ConsoleCapIdle", new Color(0.2f, 0.22f, 0.24f), 0.1f, 0.5f);
            var capOff = Lit("ConsoleCapOff", new Color(0.36f, 0.38f, 0.41f), 0.1f, 0.5f);
            var capGlow = Glow("ConsoleCapGlow", UIPalette.Cyan, 2.6f);
            var ledIdle = Solid("ConsoleLedIdle", new Color(0.1f, 0.12f, 0.13f), 0.4f);
            var ledGlow = Glow("ConsoleLedGlow", UIPalette.Cyan, 2.6f);
            var standMaterial = Lit("ConsoleStand", new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.5f);
            var plinth = Solid("PanelStandBase", new Color(0.18f, 0.19f, 0.2f), 0.3f);
            var accent = Glow("ChamberAccent", UIPalette.Cyan, 2.6f);
            var dialMaterial = Lit("ConsoleDial", new Color(0.93f, 0.94f, 0.95f), 0f, 0.4f);
            var needleMaterial = Solid("ConsoleNeedle", new Color(0.08f, 0.08f, 0.09f), 0.3f);

            Part(PrimitiveType.Cube, "Plinth", console, new Vector3(0f, 0.04f, 0f), new Vector3(1.0f, 0.08f, 0.7f), plinth);
            Part(PrimitiveType.Cube, "PlinthAccent", console, new Vector3(0f, 0.085f, -0.3f), new Vector3(0.96f, 0.014f, 0.02f), accent);
            Part(PrimitiveType.Cube, "Body", console, new Vector3(0f, 0.44f, 0.03f), new Vector3(0.92f, 0.72f, 0.56f), bodyMaterial);
            for (var i = 0; i < 4; i++)
                Part(PrimitiveType.Cube, "Vent", console, new Vector3(0f, 0.26f + i * 0.05f, -0.252f), new Vector3(0.5f, 0.014f, 0.006f), faceMaterial);
            Part(PrimitiveType.Cube, "BackRise", console, new Vector3(0f, 0.93f, 0.29f), new Vector3(0.92f, 0.26f, 0.04f), bodyMaterial);
            var deck = new GameObject("Deck").transform;
            deck.SetParent(console, false);
            deck.localPosition = new Vector3(0f, 0.92f, -0.01f);
            deck.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            Part(PrimitiveType.Cube, "Face", deck, new Vector3(0f, -0.02f, 0f), new Vector3(0.94f, 0.04f, 0.56f), faceMaterial);
            BlobShadow(console, new Vector3(0f, 0.002f, 0f), new Vector2(1.3f, 1.0f));

            // Canvas 900 x 520 mm on the deck; canvas x -> deck x, canvas y (down) -> deck -z (towards the user).
            Vector3 Deck(float cx, float cy, float y) => new Vector3((cx - 450f) * 0.001f, y, (260f - cy) * 0.001f);
            var onCap = Part(PrimitiveType.Cube, "OnCap", deck, Deck(125f, 213f, 0.015f), new Vector3(0.17f, 0.03f, 0.15f), capGlow);
            var offCap = Part(PrimitiveType.Cube, "OffCap", deck, Deck(335f, 213f, 0.015f), new Vector3(0.17f, 0.03f, 0.15f), capOff);
            var led = Part(PrimitiveType.Sphere, "DrainLed", deck, Deck(210f, 316f, 0.006f), new Vector3(0.018f, 0.018f, 0.018f), ledIdle);
            // Moisture gauge: white dial, black needle, cyan ticks at the target band (rotations set at runtime).
            Part(PrimitiveType.Cylinder, "Dial", deck, Deck(690f, 240f, 0.004f), new Vector3(0.26f, 0.004f, 0.26f), dialMaterial);
            Part(PrimitiveType.Cylinder, "DialRim", deck, Deck(690f, 240f, 0.002f), new Vector3(0.275f, 0.004f, 0.275f), standMaterial);
            Transform Pointer(string name, Vector3 size, float reach, Material material)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(deck, false);
                pivot.localPosition = Deck(690f, 240f, 0.012f);
                Part(PrimitiveType.Cube, "Bar", pivot, new Vector3(0f, 0f, reach), size, material);
                return pivot;
            }

            var needle = Pointer("GaugeNeedle", new Vector3(0.01f, 0.004f, 0.11f), 0.05f, needleMaterial);
            var tickMin = Pointer("GaugeTickMin", new Vector3(0.012f, 0.005f, 0.028f), 0.112f, ledGlow);
            var tickMax = Pointer("GaugeTickMax", new Vector3(0.012f, 0.005f, 0.028f), 0.112f, ledGlow);
            Part(PrimitiveType.Cylinder, "NeedleHub", deck, Deck(690f, 240f, 0.014f), new Vector3(0.024f, 0.004f, 0.024f), needleMaterial);

            // The face's labels and the buttons (uGUI: XR poke, controller ray and the desktop mouse ray).
            var canvas = UIFactory.WorldCanvas("Face", deck, new Vector2(900f, 520f), new Vector3(0f, 0.034f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var rect = (RectTransform)canvas.transform;
            Text(rect, "Label", "WATER CONTROL", 80f, Color.white, 20f, 6f, 860f, 92f, FontStyles.Bold, TextAlignmentOptions.Center, 64f);
            var rule = UIFactory.Rectangle("TitleRule", rect, UIPalette.Cyan);
            UIFactory.Place(rule.rectTransform, 20f, 102f, 860f, 6f);
            Button Key(string name, string label, float x, Color color)
            {
                var image = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
                image.transform.SetParent(rect, false);
                image.color = new Color(1f, 1f, 1f, 0f);
                UIFactory.Place(image.rectTransform, x, 128f, 190f, 170f);
                var button = image.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var text = Text((RectTransform)image.transform, "Text", label, 56f, color, 0f, 0f, 190f, 170f, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
                text.rectTransform.localPosition += new Vector3(0f, 0f, -16f);
                return button;
            }

            var on = Key("OnButton", "ON", 30f, Color.white);
            var off = Key("OffButton", "OFF", 240f, Color.white);
            var drain = Text(rect, "DrainLabel", "DRAIN", 30f, UIPalette.Cyan, 230f, 300f, 140f, 36f, FontStyles.Bold, TextAlignmentOptions.Left);
            drain.gameObject.SetActive(false);
            Text(rect, "AmountLabel", "Water to add", 38f, Color.white, 30f, 336f, 420f, 48f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var minus = UIFactory.Button("AmountMinus", rect, "−", 60f, out _);
            UIFactory.Place((RectTransform)minus.transform, 30f, 392f, 110f, 110f);
            var amount = Text(rect, "Amount", "--", 54f, Color.white, 146f, 392f, 198f, 110f, FontStyles.Bold, TextAlignmentOptions.Center, 34f);
            var plus = UIFactory.Button("AmountPlus", rect, "+", 60f, out _);
            UIFactory.Place((RectTransform)plus.transform, 350f, 392f, 110f, 110f);
            minus.gameObject.AddComponent<HoldRepeatButton>();
            plus.gameObject.AddComponent<HoldRepeatButton>();
            var level = Text(rect, "Level", "", 36f, Color.white, 480f, 380f, 410f, 46f, FontStyles.Bold, TextAlignmentOptions.Center, 28f);
            var target = Text(rect, "Target", "", 32f, UIPalette.TextSecondary, 480f, 428f, 410f, 42f, FontStyles.Normal, TextAlignmentOptions.Center, 26f);
            var status = Text(rect, "Status", "", 32f, UIPalette.Cyan, 480f, 472f, 410f, 42f, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center, 24f);

            var port = new GameObject("HosePort").transform;
            port.SetParent(console, false);
            port.localPosition = new Vector3(0.5f, 0.14f, 0.12f);
            port.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Limb("PortNipple", console, new Vector3(0.46f, 0.14f, 0.12f), new Vector3(0.5f, 0.14f, 0.12f), 0.016f, standMaterial, PrimitiveType.Cylinder);

            var hoseMaterial = Lit("WaterHose", new Color(0.1f, 0.1f, 0.11f), 0f, 0.35f, false, true);
            var outletMaterial = Lit("WaterOutlet", new Color(0.55f, 0.57f, 0.6f), 0.6f, 0.5f, false, true);
            var water = Lit("WaterStream", new Color(0.82f, 0.92f, 1f, 0.5f), 0f, 0.95f, true, true);
            MeshFilter MakeMesh(string name, Material material)
            {
                var mesh = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                mesh.transform.SetParent(go.transform, false);
                var renderer = mesh.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                return mesh.GetComponent<MeshFilter>();
            }

            var hose = MakeMesh("Hose", hoseMaterial);
            var outlet = MakeMesh("Outlet", outletMaterial);
            var stream = MakeMesh("Stream", water);
            var droplets = MakeMesh("Splash", water);
            stream.GetComponent<MeshRenderer>().enabled = false;
            droplets.GetComponent<MeshRenderer>().enabled = false;

            station.Bind(console, on, off, onCap.transform, offCap.transform, onCap.GetComponent<MeshRenderer>(), offCap.GetComponent<MeshRenderer>(), capIdle,
                capGlow, capOff, led.GetComponent<MeshRenderer>(), ledIdle, ledGlow, drain, port, hose, outlet, stream, stream.GetComponent<MeshRenderer>(),
                droplets, droplets.GetComponent<MeshRenderer>(), null);
            station.BindConsole(minus, plus, amount, level, target, status, needle, tickMin, tickMax);
            return station;
        }

        // ------------------------------------------------------------------ TREATMENT TOOLS bench (Addendum 6)

        /// <summary>
        /// The TREATMENT TOOLS bench (forward = towards the wall it stands against): a cabinet with drawers and a white top,
        /// a charcoal pegboard back with the "TREATMENT TOOLS" header board, and two labelled docks at the front edge of
        /// the top, within seated reach: FERTILISER (the packet stands in it) and SPRAYER. The docks are the tools'
        /// snap-back targets.
        /// </summary>
        static Transform BuildToolsBench(Transform root, out Transform packetDock, out Transform sprayerDock)
        {
            var bench = new GameObject("ToolsBench").transform;
            bench.SetParent(root, false);
            var plinth = Solid("PanelStandBase", new Color(0.18f, 0.19f, 0.2f), 0.3f);
            var cabinet = Lit("BenchCabinet", new Color(0.32f, 0.34f, 0.36f), 0.4f, 0.4f);
            var top = Solid("HallCounter", new Color(0.9f, 0.91f, 0.9f), 0.4f);
            var board = Lit("KioskCabinet", new Color(0.2f, 0.21f, 0.23f), 0.4f, 0.45f);
            var accent = Glow("ChamberAccent", UIPalette.Cyan, 2.6f);
            var handle = Lit("PanelStandMetal", new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.5f);
            var sage = Solid("PacketSage", new Color(0.49f, 0.62f, 0.4f), 0.2f);
            var dockTop = Lit("DockTop", new Color(0.2f, 0.21f, 0.22f), 0.1f, 0.3f);

            Part(PrimitiveType.Cube, "Plinth", bench, new Vector3(0f, 0.04f, 0.01f), new Vector3(1.96f, 0.08f, 0.6f), plinth);
            Part(PrimitiveType.Cube, "Cabinet", bench, new Vector3(0f, 0.43f, 0.01f), new Vector3(1.96f, 0.7f, 0.6f), cabinet);
            for (var i = 0; i < 3; i++)
            {
                var x = -0.65f + i * 0.65f;
                Part(PrimitiveType.Cube, "DrawerLine", bench, new Vector3(x, 0.55f, -0.292f), new Vector3(0.58f, 0.008f, 0.006f), plinth);
                Part(PrimitiveType.Cube, "DrawerHandle", bench, new Vector3(x, 0.65f, -0.298f), new Vector3(0.22f, 0.018f, 0.018f), handle);
            }

            Part(PrimitiveType.Cube, "Top", bench, new Vector3(0f, 0.8f, 0f), new Vector3(2.0f, 0.04f, 0.65f), top, null, true);
            Part(PrimitiveType.Cube, "TopAccent", bench, new Vector3(0f, 0.776f, -0.327f), new Vector3(2.0f, 0.014f, 0.012f), accent);
            Part(PrimitiveType.Cube, "Pegboard", bench, new Vector3(0f, 1.245f, 0.3f), new Vector3(2.0f, 0.85f, 0.03f), board);
            Part(PrimitiveType.Cube, "HeaderBoard", bench, new Vector3(0f, 1.82f, 0.3f), new Vector3(2.04f, 0.26f, 0.05f), board);
            Part(PrimitiveType.Cube, "HeaderAccent", bench, new Vector3(0f, 1.682f, 0.27f), new Vector3(2.0f, 0.016f, 0.016f), accent);
            StationLabel(bench, "HeaderLabel", "TREATMENT TOOLS", new Vector3(0f, 1.82f, 0.272f), new Vector2(1900f, 230f), 120f);
            BlobShadow(bench, new Vector3(0f, 0.002f, 0f), new Vector2(2.4f, 0.95f));

            Transform Dock(string name, string label, float x, Vector3 cradleSize, Material cradle)
            {
                Part(PrimitiveType.Cube, name + "Cradle", bench, new Vector3(x, 0.828f, -0.18f), cradleSize, cradle);
                Part(PrimitiveType.Cube, name + "Plate", bench, new Vector3(x, 1.43f, 0.283f), new Vector3(0.62f, 0.13f, 0.006f), plinth);
                StationLabel(bench, name + "Label", label, new Vector3(x, 1.43f, 0.279f), new Vector2(600f, 120f), 72f, UIPalette.Cyan);
                var dock = new GameObject(name).transform;
                dock.SetParent(bench, false);
                dock.localPosition = new Vector3(x, 0.82f, -0.18f);
                return dock;
            }

            packetDock = Dock("PacketDock", "FERTILISER", -0.42f, new Vector3(0.4f, 0.016f, 0.16f), sage);
            sprayerDock = Dock("SprayerDock", "SPRAYER", 0.42f, new Vector3(0.2f, 0.016f, 0.2f), dockTop);
            return bench;
        }

        /// <summary>Teleport anchors (cyan pads) for the stations and the plant view; the station places them at runtime.</summary>
        static Transform[] BuildStationAnchors(Transform root)
        {
            var node = new GameObject("StationAnchors").transform;
            node.SetParent(root, false);
            var anchors = new Transform[4];
            var names = new[] { "Anchor_Kiosk", "Anchor_WaterControl", "Anchor_TreatmentTools", "Anchor_PlantChamber" };
            for (var i = 0; i < names.Length; i++)
                anchors[i] = XRSceneWiring.CreateTeleportAnchor(node, names[i], Vector3.zero, 0f).transform;
            return anchors;
        }
    }
}
