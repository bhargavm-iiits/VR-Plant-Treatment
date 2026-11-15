using System.Collections.Generic;
using BTP.Core;
using BTP.Farm;
using BTP.Lab;
using BTP.Plants;
using BTP.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Editor
{
    /// <summary>
    /// Builds the Farm (Envirornment.unity): one row per plant disease, 58 in all (FarmDiseaseRows), in seven bands
    /// along a central dirt aisle with a labelled head path per band, a timber fence, and the entry notice board with
    /// Enter Lab. Selecting any plant in a row selects that plant and its disease. GreenhouseParkBuilder adds the
    /// greenhouse frame over the fenced field and the bush belts, road and hills around it, and sets the hazy
    /// daylight. The whole field floor is a teleport area; the aisle and head paths are teleport areas too, with
    /// viewing points at each band. A fresh build is complete: it also adds the treatment status the Treatment
    /// step (BTP/Treatment/4) would add, so no later step needs to be replayed.
    /// </summary>
    public static class FarmSceneBuilder
    {
        static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -6.5f);
        static readonly Vector3 SpawnHead = SpawnPosition + Vector3.up * 1.6f;
        static readonly Vector3 BoardPosition = new Vector3(-2.6f, 0f, -4.4f);
        // Viewing point 0.75 m in front of the board, close enough to poke its buttons.
        static readonly Vector3 BoardViewpoint = BoardPosition + new Vector3(SpawnPosition.x - BoardPosition.x, 0f, SpawnPosition.z - BoardPosition.z).normalized * 0.75f;
        // The timber fence around the field (35 x 94.5 m; it was 35 x 46 m before the soybean, cereal, cotton,
        // chilli, sugarcane, shrub and tropical orchard bands, which lie beyond Z = 37);
        // GreenhouseParkBuilder fits its greenhouse frame just outside it.
        internal const float FenceMinX = -17.5f, FenceMaxX = 17.5f, FenceMinZ = -9f, FenceMaxZ = 85.5f;
        /// <summary>Length of the field when its ground textures were tiled; the longer field keeps that texel size.</summary>
        const float TiledLength = 46f;

        internal sealed class Materials
        {
            public Material Soil, Dirt, Wood, DarkWood, Metal, Pipe, Ring, Stake;
            /// <summary>Soil and dirt tiled for the full length of the field (the field floor and the central aisle).</summary>
            public Material FieldSoil, Aisle;
        }

        [MenuItem("BTP/Setup/4 - Build Farm Scene", priority = 4)]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Exit Play mode before rebuilding the Farm.");
            // Load assets after NewScene: opening a scene unloads unused assets loaded before it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            if (catalog == null)
                throw new System.InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first.");
            var variants = AssetDatabase.LoadAssetAtPath<DiseaseVariantCatalog>(ProjectPaths.VariantCatalog);
            if (variants == null)
                throw new System.InvalidOperationException("Run BTP/Setup/26 - Build Farm Disease Variants first.");
            EnvironmentKit.DisableGlobalIllumination(ProjectPaths.Data + "/Lighting/FarmLighting.lighting");
            var materials = CreateMaterials();

            var lighting = new GameObject("Lighting").transform;
            GreenhouseParkBuilder.ConfigureLighting(lighting);

            var environment = new GameObject("Environment").transform;
            var navigation = new GameObject("Navigation").transform;
            BuildGroundAndPaths(environment, navigation, materials);
            BuildFence(environment, materials);
            // The ground, the greenhouse frame over the field, and the bush belts, road and hills outside it.
            GreenhouseParkBuilder.Build(environment);

            var gallery = BuildDiseaseGallery();
            var crops = new GameObject("Crops").transform;
            var beds = EnvironmentKit.Group("RaisedBeds", environment).transform;
            var rowMaterials = new FarmDiseaseRows.Materials
            {
                Soil = materials.Soil, Dirt = materials.Dirt, Wood = materials.Wood, Pipe = materials.Pipe, Ring = materials.Ring, Stake = materials.Stake,
            };
            var rows = FarmDiseaseRows.Build(crops, beds, variants, AssetDatabase.LoadAssetAtPath<DiseaseGalleryCatalog>(ProjectPaths.DiseaseCatalog),
                rowMaterials, gallery);

            var recovery = AssetDatabase.LoadAssetAtPath<TomatoRecoveryData>(ProjectPaths.RecoveryData);
            if (recovery == null)
                throw new System.InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first (it builds the recovery data).");
            BuildCropRecovery(catalog, variants, recovery);
            gallery.BindConsole(BuildTimelineConsole(recovery));

            BuildNoticeBoard(new GameObject("NoticeBoard").transform, materials);
            // Pipes along every row, the tank and the one-button console right of the entrance (after the rows).
            FarmIrrigationBuilder.Build(materials.Soil, materials.Wood, materials.DarkWood);
            BuildNavigation(navigation);

            new GameObject("BootstrapGuard", typeof(BootstrapGuard));

            // What BTP/Treatment/4 adds to the Farm: each row's saved treatment status under its label.
            var labels = 0;
            foreach (var row in rows)
                labels += TreatmentSceneBuilder.AddFarmStatus(row) ? 1 : 0;

            EditorSceneManager.SaveScene(scene, ProjectPaths.FarmScene);
            // The greenhouse cover, frame shell and door frames are generated meshes rebuilt in place for the new
            // footprint; save them with the scene, or the scene would reference the old, smaller ones on disk.
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Built {ProjectPaths.FarmScene}: {rows.Count} disease rows, {labels} treatment status labels.");
        }

        static Materials CreateMaterials()
        {
            string M(string name) => $"{ProjectPaths.EnvironmentMaterials}/Farm_{name}.mat";
            var m = new Materials
            {
                Soil = MaterialFactory.Solid(M("Soil"), Color.white, 0.05f, ProceduralTextures.Soil),
                Dirt = MaterialFactory.Solid(M("Dirt"), Color.white, 0.05f, ProceduralTextures.DirtPath),
                Wood = MaterialFactory.Solid(M("Wood"), Color.white, 0.1f, ProceduralTextures.Wood),
                DarkWood = MaterialFactory.Solid(M("DarkWood"), new Color(0.55f, 0.45f, 0.38f), 0.1f, ProceduralTextures.Wood),
                Metal = MaterialFactory.Solid(M("Metal"), new Color(0.35f, 0.37f, 0.39f), 0.4f),
                Pipe = MaterialFactory.Solid(M("DripLine"), new Color(0.06f, 0.06f, 0.07f), 0.3f),
                Stake = MaterialFactory.Solid(M("Stake"), new Color(0.62f, 0.50f, 0.34f), 0.1f),
                Ring = MaterialFactory.Glow(M("SelectionRing"), UIPalette.Cyan, 1.5f),
            };
            m.Soil.SetTextureScale("_BaseMap", new Vector2(2f, 8f));
            m.Dirt.SetTextureScale("_BaseMap", new Vector2(3f, 30f));
            m.Wood.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
            var stretch = (FenceMaxZ - FenceMinZ) / TiledLength;
            m.FieldSoil = MaterialFactory.Solid(M("FieldSoil"), Color.white, 0.05f, ProceduralTextures.Soil);
            m.FieldSoil.SetTextureScale("_BaseMap", new Vector2(2f, 8f * stretch));
            m.Aisle = MaterialFactory.Solid(M("DirtAisle"), Color.white, 0.05f, ProceduralTextures.DirtPath);
            m.Aisle.SetTextureScale("_BaseMap", new Vector2(3f, 30f * stretch));
            return m;
        }

        // ------------------------------------------------------------------ ground and paths

        static void BuildGroundAndPaths(Transform environment, Transform navigation, Materials m)
        {
            // The grass ground itself is laid by GreenhouseParkBuilder.Build.
            // Worked soil over the whole fenced field (the paths sit slightly above it). Layers sit centimetres apart
            // (ground 0, field soil 0.012, paths 0.03) to avoid z-fighting at distance. The soil is a teleport area,
            // so every walkway between beds and trees can be reached; beds, plants and their selection volumes stop
            // the teleport ray, so nobody lands on a bed.
            const float inset = 0.4f;
            var field = EnvironmentKit.Box("FieldSoil", navigation,
                new Vector3((FenceMinX + FenceMaxX) * 0.5f, 0.002f, (FenceMinZ + FenceMaxZ) * 0.5f),
                new Vector3(FenceMaxX - FenceMinX - inset * 2f, 0.02f, FenceMaxZ - FenceMinZ - inset * 2f), m.FieldSoil, true, 0f, false);
            XRSceneWiring.MakeTeleportArea(field);

            // The central aisle from the entrance to the far end, and the head path of each band of rows.
            var aisle = EnvironmentKit.Box("CentralAisle", navigation, new Vector3(0f, 0.01f, (FenceMinZ + FenceMaxZ) * 0.5f),
                new Vector3(FarmDiseaseRows.AisleHalfWidth * 2f, 0.04f, FenceMaxZ - FenceMinZ - inset * 2f), m.Aisle, true, 0f, false);
            XRSceneWiring.MakeTeleportArea(aisle);
            foreach (var (name, z) in FarmDiseaseRows.HeadPaths)
            {
                // Turned 90 degrees, so the dirt texture runs along the path as it does along the aisle.
                var path = EnvironmentKit.Box(name, navigation, new Vector3(0f, 0.012f, z + FarmDiseaseRows.PathWidth * 0.5f),
                    new Vector3(FarmDiseaseRows.PathWidth, 0.04f, FenceMaxX - FenceMinX - 2f), m.Dirt, true, 90f, false);
                XRSceneWiring.MakeTeleportArea(path);
            }
        }

        // ------------------------------------------------------------------ gallery

        /// <summary>The disease gallery: one controller with a pooled ring of cards (see DiseaseGalleryController).</summary>
        static DiseaseGalleryController BuildDiseaseGallery()
        {
            var diseases = AssetDatabase.LoadAssetAtPath<DiseaseGalleryCatalog>(ProjectPaths.DiseaseCatalog);
            if (diseases == null)
                throw new System.InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first (it builds the disease gallery data).");

            var border = MaterialFactory.Unlit($"{ProjectPaths.UIMaterials}/DiseaseCard_Border.mat", UIPalette.Cyan);
            var panel = MaterialFactory.Unlit($"{ProjectPaths.UIMaterials}/DiseaseCard_Panel.mat", new Color(0.07f, 0.09f, 0.11f));
            var image = MaterialFactory.Unlit($"{ProjectPaths.UIMaterials}/DiseaseCard_Leaf.mat", Color.white);
            var controller = new GameObject("DiseaseGallery", typeof(DiseaseGalleryController)).GetComponent<DiseaseGalleryController>();
            controller.Bind(diseases, border, panel, image);
            return controller;
        }

        // ------------------------------------------------------------------ six-week timeline

        /// <summary>
        /// One driver for every crop's BTP/Farm Recovery materials (near meshes and billboards, from the plant's Farm
        /// prefab and all of its disease variants, so every disease row of a crop follows that crop's week).
        /// At run time it swaps them for per-crop copies on every renderer in the scene.
        /// </summary>
        static void BuildCropRecovery(PlantCatalog catalog, DiseaseVariantCatalog variants, TomatoRecoveryData recovery)
        {
            var crops = new List<FarmCropRecovery.Crop>();
            // Every crop with Farm rows: the nine PlantVillage plants and the ten field and tropical crops.
            foreach (var plant in catalog.AllPlants)
            {
                if (plant == null || plant.FarmPrefab == null)
                    continue;
                var crop = new FarmCropRecovery.Crop { plantId = plant.PlantId, materials = FarmRecoveryMaterials(plant.FarmPrefab) };
                foreach (var entry in variants.Entries)
                {
                    if (entry.plantId != plant.PlantId || entry.farmPrefab == null)
                        continue;
                    foreach (var material in FarmRecoveryMaterials(entry.farmPrefab))
                    {
                        if (!crop.materials.Contains(material))
                            crop.materials.Add(material);
                    }
                }

                if (crop.materials.Count == 0)
                    Debug.LogError($"[BTP] {plant.DisplayName} farm prefab has no Farm Recovery materials; run Build Plant Assets.");
                crops.Add(crop);
            }

            var driver = new GameObject("FarmCropRecovery", typeof(FarmCropRecovery)).GetComponent<FarmCropRecovery>();
            driver.Bind(recovery, crops);
        }

        /// <summary>A Farm prefab's BTP/Farm Recovery materials (every LOD), the ones the timeline drives.</summary>
        internal static List<Material> FarmRecoveryMaterials(GameObject farmPrefab)
        {
            var materials = new List<Material>();
            foreach (var renderer in farmPrefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null && material.shader != null && material.shader.name == MaterialFactory.FarmRecoveryShader &&
                        !materials.Contains(material))
                        materials.Add(material);
                }
            }

            return materials;
        }

        /// <summary>
        /// The six-week console that opens with a plant's disease ring: a 1000 x 340 mm world-space canvas with the
        /// same controls as the Lab bar, a status line, three small metric bars and the prototype disclaimer.
        /// </summary>
        static FarmTimelineConsole BuildTimelineConsole(TomatoRecoveryData recovery)
        {
            var host = new GameObject("FarmTimeline");
            var canvas = UIFactory.WorldCanvas("TimelineConsole", host.transform, new Vector2(1000f, 340f), Vector3.zero, Quaternion.identity);
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect);
            canvas.gameObject.AddComponent<FarmConsolePressGuard>();

            var title = UIFactory.Text("Title", rect, "Six-week timeline", 26f, UIPalette.TextSecondary, TextAlignmentOptions.MidlineLeft);
            UIFactory.Place(title.rectTransform, 22f, 6f, 600f, 40f);
            // Centred over the Week 3 button, on one line.
            var treatmentX = 100f + (TomatoRecoveryData.TreatmentWeek - 1) * 113f + 52f - 160f;
            var marker = UIFactory.Text("TreatmentMarker", rect, TomatoRecoveryData.TreatmentCaption, 24f, UIPalette.Cyan, TextAlignmentOptions.Bottom);
            marker.textWrappingMode = TextWrappingModes.NoWrap;
            UIFactory.Place(marker.rectTransform, treatmentX, 48f, 320f, 38f);

            Button ConsoleButton(string name, string text, float size, float x, float width, out SelectedStateVisual visual)
            {
                var button = UIFactory.Button(name, rect, text, size, out visual);
                UIFactory.Place((RectTransform)button.transform, x, 90f, width, 96f);
                button.gameObject.AddComponent<FarmConsolePressGuard>();
                return button;
            }

            var previous = ConsoleButton("Previous", "<", 52f, 18f, 70f, out _);
            var weeks = new List<Button>();
            var visuals = new List<SelectedStateVisual>();
            for (var i = 0; i < recovery.WeekCount; i++)
            {
                weeks.Add(ConsoleButton($"Week{i + 1}", $"Week {i + 1}", 26f, 100f + i * 113f, 104f, out var visual));
                visuals.Add(visual);
            }

            var next = ConsoleButton("Next", ">", 52f, 782f, 70f, out _);
            var reset = ConsoleButton("Reset", "Reset", 28f, 866f, 116f, out _);

            var status = UIFactory.Text("Status", rect, "Week 1 of 6", 26f, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UIFactory.Place(status.rectTransform, 22f, 194f, 960f, 38f);
            var leaf = MiniMetric("LeafHealth", rect, "Leaf", true, 22f, 236f, 310f);
            var chlorophyll = MiniMetric("Chlorophyll", rect, "Chlorophyll", true, 345f, 236f, 310f);
            var severity = MiniMetric("Severity", rect, "Severity", false, 668f, 236f, 310f);

            var disclaimer = UIFactory.Text("Disclaimer", rect, TomatoRecoveryData.Disclaimer, 22f, UIPalette.Warning,
                TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            UIFactory.Place(disclaimer.rectTransform, 22f, 292f, 960f, 36f);

            var console = host.AddComponent<FarmTimelineConsole>();
            console.Bind(recovery, canvas.gameObject, title, status, previous, next, reset, weeks, visuals, leaf, chlorophyll, severity);
            canvas.gameObject.SetActive(false);
            return console;
        }

        /// <summary>A compact metric row for the Farm console: label and bar on one 44 mm line (the values are in the status line).</summary>
        static MetricBar MiniMetric(string name, Transform parent, string label, bool higherIsBetter, float x, float y, float width)
        {
            var row = UIFactory.Place(UIFactory.Node(name, parent), x, y, width, 44f);
            UIFactory.Place(UIFactory.Text("Name", row, label, 22f, UIPalette.TextPrimary, TextAlignmentOptions.MidlineLeft).rectTransform,
                0f, 0f, 140f, 44f);
            var track = UIFactory.RoundedImage("Track", row, UIPalette.TrackBackground, ProceduralTextures.RoundedFill);
            track.raycastTarget = false;
            track.pixelsPerUnitMultiplier = 4f;
            UIFactory.Place(track.rectTransform, 146f, 14f, width - 166f, 16f);
            var fill = UIFactory.RoundedImage("Fill", track.transform, UIPalette.HealthGood, ProceduralTextures.RoundedFill);
            fill.raycastTarget = false;
            fill.pixelsPerUnitMultiplier = 4f;
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0.5f, 1f);
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            var bar = row.gameObject.AddComponent<MetricBar>();
            bar.Bind(fillRect, fill, null, higherIsBetter);
            return bar;
        }

        // ------------------------------------------------------------------ fence

        static void BuildFence(Transform environment, Materials m)
        {
            var fence = EnvironmentKit.Group("TimberFence", environment).transform;
            const float minX = FenceMinX, maxX = FenceMaxX, minZ = FenceMinZ, maxZ = FenceMaxZ;
            void Side(Vector3 from, Vector3 to)
            {
                var length = Vector3.Distance(from, to);
                var posts = Mathf.CeilToInt(length / 2.5f);
                for (var i = 0; i <= posts; i++)
                {
                    var p = Vector3.Lerp(from, to, (float)i / posts);
                    EnvironmentKit.Box("Post", fence, p + Vector3.up * 0.55f, new Vector3(0.12f, 1.1f, 0.12f), m.Wood, false);
                }

                var yaw = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
                foreach (var h in new[] { 0.45f, 0.9f })
                    EnvironmentKit.Box("Rail", fence, (from + to) * 0.5f + Vector3.up * h, new Vector3(0.06f, 0.1f, length), m.DarkWood, false, yaw);
            }

            Side(new Vector3(minX, 0f, minZ), new Vector3(maxX, 0f, minZ));
            Side(new Vector3(maxX, 0f, minZ), new Vector3(maxX, 0f, maxZ));
            Side(new Vector3(maxX, 0f, maxZ), new Vector3(minX, 0f, maxZ));
            Side(new Vector3(minX, 0f, maxZ), new Vector3(minX, 0f, minZ));
        }

        // ------------------------------------------------------------------ notice board and navigation

        static void BuildNoticeBoard(Transform board, Materials m)
        {
            board.position = BoardPosition;
            board.rotation = UIFactory.Facing(board.position, SpawnHead);
            EnvironmentKit.Box("PostLeft", board, new Vector3(-0.78f, 1.1f, 0.04f), new Vector3(0.1f, 2.2f, 0.1f), m.DarkWood, true);
            EnvironmentKit.Box("PostRight", board, new Vector3(0.78f, 1.1f, 0.04f), new Vector3(0.1f, 2.2f, 0.1f), m.DarkWood, true);
            EnvironmentKit.Box("Roof", board, new Vector3(0f, 2.28f, -0.02f), new Vector3(1.9f, 0.08f, 0.55f), m.DarkWood, false);
            EnvironmentKit.Box("Board", board, new Vector3(0f, 1.35f, 0.06f), new Vector3(1.5f, 0.95f, 0.06f), m.Wood, true);
            foreach (var go in board.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(go.gameObject, 0);

            var canvas = UIFactory.WorldCanvas("BoardUI", board, new Vector2(1400f, 860f), new Vector3(0f, 1.35f, 0.025f), Quaternion.identity);
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect);
            UIFactory.Place(UIFactory.Text("Title", rect, "Plant Disease Lab", 64f, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold).rectTransform, 50f, 40f, 1300f, 80f);
            UIFactory.Place(UIFactory.Text("Intro", rect, "Every row shows one disease. Pick a row, then study its leaf in the lab.", 32f, UIPalette.TextSecondary).rectTransform, 50f, 125f, 1300f, 50f);
            UIFactory.Place(UIFactory.Text("Steps", rect,
                "1  Seven bands of rows, from tomatoes here to the mango and orange orchard at the far end. Each row's sign names its disease.\n" +
                "2  Point at any plant in a row and pull the trigger, or pinch, to select it and its disease.\n" +
                "3  Press <b>Enter Lab</b> to study that disease's leaf. Up close, selecting also shows sample leaves.",
                30f, Color.white).rectTransform, 50f, 200f, 1300f, 165f);

            var readout = UIFactory.Text("Selection", rect, "", 36f, UIPalette.Cyan);
            UIFactory.Place(readout.rectTransform, 50f, 370f, 1300f, 60f);
            readout.gameObject.AddComponent<SelectionReadout>().Bind(readout);

            var enter = UIFactory.Button("EnterLab", rect, "Enter Lab", 56f, out _, UIPalette.Cyan, new Color(0.02f, 0.11f, 0.14f));
            UIFactory.Place((RectTransform)enter.transform, 50f, 500f, 820f, 280f);
            var colors = enter.colors;
            colors.highlightedColor = new Color(0.55f, 0.95f, 1f);
            colors.pressedColor = UIPalette.ButtonPressed;
            enter.colors = colors;
            enter.gameObject.AddComponent<EnvironmentButton>().Action = EnvironmentButton.ButtonAction.EnterLab;

            UIFactory.Place(UIFactory.Text("Credit", rect, DiseaseClasses.Credit, 22f, UIPalette.TextSecondary).rectTransform, 50f, 800f, 1300f, 36f);

            var recenter = UIFactory.Button("Recenter", rect, "Recenter view", 34f, out _);
            UIFactory.Place((RectTransform)recenter.transform, 920f, 600f, 430f, 180f);
            recenter.gameObject.AddComponent<EnvironmentButton>().Action = EnvironmentButton.ButtonAction.Recenter;
        }

        static void BuildNavigation(Transform navigation)
        {
            var spawn = new GameObject("SpawnAnchor", typeof(SpawnAnchor));
            spawn.transform.SetParent(navigation, false);
            spawn.transform.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
            EnvironmentKit.PreviewCamera(spawn.transform);

            // Viewing points on the head paths; the whole field floor is teleportable everywhere else.
            XRSceneWiring.CreateTeleportAnchor(navigation, "Viewpoint_NoticeBoard", BoardViewpoint,
                UIFactory.Facing(BoardPosition, BoardViewpoint).eulerAngles.y);
            foreach (var (name, position, yaw) in FarmDiseaseRows.Viewpoints)
                XRSceneWiring.CreateTeleportAnchor(navigation, name, position, yaw);
        }
    }
}
