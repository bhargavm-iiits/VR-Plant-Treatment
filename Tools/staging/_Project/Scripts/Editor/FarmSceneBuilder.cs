using System.Collections.Generic;
using BTP.Core;
using BTP.Farm;
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
    /// Builds Farm.unity after the farm reference: a compact field with a central dirt aisle,
    /// raised soil beds, an orchard edge, a corn block clear of the route, a timber fence,
    /// a water tower, distant mountains and the entry notice board with Enter Lab. Every plant
    /// type has one labelled, selectable specimen near the entrance.
    /// </summary>
    public static class FarmSceneBuilder
    {
        static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -9.5f);
        static readonly Vector3 SpawnHead = SpawnPosition + Vector3.up * 1.6f;
        const float BedHeight = 0.25f;

        sealed class Materials
        {
            public Material Grass, Soil, Dirt, Wood, DarkWood, Rock, Metal, Pipe, Mountain, Ring, Stake;
        }

        [MenuItem("BTP/Setup/4 - Build Farm Scene", priority = 4)]
        public static void Build()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            if (catalog == null)
                throw new System.InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first.");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnvironmentKit.DisableGlobalIllumination(ProjectPaths.Data + "/Lighting/FarmLighting.lighting");
            var materials = CreateMaterials();

            var lighting = new GameObject("Lighting").transform;
            EnvironmentKit.ConfigureLighting(lighting,
                sunColor: new Color(1f, 0.93f, 0.80f), sunIntensity: 1.35f, sunEuler: new Vector3(42f, -35f, 0f),
                skyTint: new Color(0.42f, 0.55f, 0.78f),
                ambientSky: new Color(0.58f, 0.68f, 0.84f), ambientEquator: new Color(0.52f, 0.55f, 0.47f),
                ambientGround: new Color(0.33f, 0.28f, 0.21f),
                fogColor: new Color(0.70f, 0.79f, 0.88f), fogStart: 70f, fogEnd: 380f,
                skyboxPath: ProjectPaths.EnvironmentMaterials + "/FarmSky.mat");

            var environment = new GameObject("Environment").transform;
            var navigation = new GameObject("Navigation").transform;
            BuildGroundAndPaths(environment, navigation, materials);
            BuildBeds(environment, materials);
            BuildFence(environment, materials);
            BuildWaterTower(environment, materials);
            BuildScenery(environment, materials, catalog);

            var crops = new GameObject("Crops").transform;
            PlantCrops(crops, catalog, materials);

            var specimens = new GameObject("Specimens").transform;
            BuildSpecimens(specimens, catalog, materials);

            BuildNoticeBoard(new GameObject("NoticeBoard").transform, materials);
            BuildNavigation(navigation);

            new GameObject("BootstrapGuard", typeof(BootstrapGuard));

            EditorSceneManager.SaveScene(scene, ProjectPaths.FarmScene);
            Debug.Log($"[BTP] Built {ProjectPaths.FarmScene}.");
        }

        static Materials CreateMaterials()
        {
            string M(string name) => $"{ProjectPaths.EnvironmentMaterials}/Farm_{name}.mat";
            var m = new Materials
            {
                Grass = MaterialFactory.Solid(M("Grass"), Color.white, 0.05f, ProceduralTextures.Grass),
                Soil = MaterialFactory.Solid(M("Soil"), Color.white, 0.05f, ProceduralTextures.Soil),
                Dirt = MaterialFactory.Solid(M("Dirt"), Color.white, 0.05f, ProceduralTextures.DirtPath),
                Wood = MaterialFactory.Solid(M("Wood"), Color.white, 0.1f, ProceduralTextures.Wood),
                DarkWood = MaterialFactory.Solid(M("DarkWood"), new Color(0.55f, 0.45f, 0.38f), 0.1f, ProceduralTextures.Wood),
                Rock = MaterialFactory.Solid(M("Rock"), new Color(0.56f, 0.53f, 0.49f), 0.15f),
                Metal = MaterialFactory.Solid(M("Metal"), new Color(0.35f, 0.37f, 0.39f), 0.4f),
                Pipe = MaterialFactory.Solid(M("DripLine"), new Color(0.06f, 0.06f, 0.07f), 0.3f),
                Stake = MaterialFactory.Solid(M("Stake"), new Color(0.62f, 0.50f, 0.34f), 0.1f),
                Mountain = MaterialFactory.Solid(M("Mountains"), Color.white, 0f, ProceduralTextures.Mountain),
                Ring = MaterialFactory.Glow(M("SelectionRing"), UIPalette.Cyan, 1.5f),
            };
            m.Grass.SetTextureScale("_BaseMap", new Vector2(160f, 160f));
            m.Soil.SetTextureScale("_BaseMap", new Vector2(2f, 8f));
            m.Dirt.SetTextureScale("_BaseMap", new Vector2(3f, 30f));
            m.Wood.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
            return m;
        }

        // ------------------------------------------------------------------ ground, paths, beds

        static void BuildGroundAndPaths(Transform environment, Transform navigation, Materials m)
        {
            var ground = EnvironmentKit.Primitive(PrimitiveType.Plane, "Ground", environment, Vector3.zero,
                new Vector3(60f, 1f, 60f), m.Grass, true, Quaternion.identity, false);
            ground.GetComponent<MeshRenderer>().receiveShadows = true;

            // Worked soil under the bed area (the aisle and paths sit slightly above it).
            // Layers sit centimetres apart (ground 0, field soil 0.012, paths 0.03) to avoid z-fighting at distance.
            EnvironmentKit.Box("FieldSoil", environment, new Vector3(0f, 0.002f, 6.5f), new Vector3(12f, 0.02f, 29f), m.Soil, false, 0f, false);

            // Teleportable surfaces: the central aisle and the cross path to the orchard and corn.
            var aisle = EnvironmentKit.Box("CentralAisle", navigation, new Vector3(0f, 0.01f, 5.75f), new Vector3(2.4f, 0.04f, 34.5f), m.Dirt, true, 0f, false);
            XRSceneWiring.MakeTeleportArea(aisle);
            foreach (var side in new[] { -1f, 1f })
            {
                var path = EnvironmentKit.Box(side < 0 ? "CrossPath_Orchard" : "CrossPath_Corn", navigation,
                    new Vector3(side * 4.85f, 0.01f, 0f), new Vector3(7.3f, 0.04f, 1.6f), m.Dirt, true, 0f, false);
                XRSceneWiring.MakeTeleportArea(path);
            }
        }

        static readonly (string name, float x, float zStart, float zEnd)[] Beds =
        {
            ("Bed_Demo_Left", -2.3f, -6.6f, -2.6f),
            ("Bed_Demo_Right", 2.3f, -6.6f, -2.6f),
            ("Bed_L1", -2.3f, 1.5f, 9.5f), ("Bed_L2", -2.3f, 11f, 19f),
            ("Bed_R1", 2.3f, 1.5f, 9.5f), ("Bed_R2", 2.3f, 11f, 19f),
            ("Bed_LL1", -4.6f, 1.5f, 9.5f), ("Bed_LL2", -4.6f, 11f, 19f),
            ("Bed_RR1", 4.6f, 1.5f, 9.5f), ("Bed_RR2", 4.6f, 11f, 19f),
        };

        static void BuildBeds(Transform environment, Materials m)
        {
            var group = EnvironmentKit.Group("RaisedBeds", environment).transform;
            foreach (var (name, x, zStart, zEnd) in Beds)
            {
                var length = zEnd - zStart;
                EnvironmentKit.Box(name, group, new Vector3(x, BedHeight * 0.5f, (zStart + zEnd) * 0.5f),
                    new Vector3(1.2f, BedHeight, length), m.Soil, true);
                // Drip irrigation line along the bed, as in the reference.
                var pipe = EnvironmentKit.Cylinder($"{name}_DripLine", group, new Vector3(x + 0.42f, BedHeight + 0.015f, (zStart + zEnd) * 0.5f),
                    0.012f, length, m.Pipe, false, false);
                pipe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        // ------------------------------------------------------------------ crops

        static void PlantCrops(Transform crops, PlantCatalog catalog, Materials m)
        {
            PlantDefinition Plant(string id) => catalog.TryGet(id, out var plant) ? plant : null;
            var random = new System.Random(42);

            void Row(string id, float x, float zStart, float zEnd, float spacing, float stressedShare = 0f, bool stakes = false,
                float yBase = BedHeight, float rowOffset = 0f)
            {
                var plant = Plant(id);
                if (plant == null || plant.FarmPrefab == null)
                    return;
                var group = EnvironmentKit.Group($"{plant.DisplayName} {x:0.0}/{zStart:0}", crops).transform;
                for (var z = zStart + spacing * 0.5f; z < zEnd; z += spacing)
                {
                    var position = new Vector3(x + rowOffset + ((float)random.NextDouble() - 0.5f) * 0.08f, yBase, z);
                    var instance = EnvironmentKit.Place(plant.FarmPrefab, group, position, (float)random.NextDouble() * 360f,
                        0.9f + (float)random.NextDouble() * 0.2f);
                    if (random.NextDouble() < stressedShare)
                        PlantTint.ApplyStressedFoliage(instance);
                    if (stakes)
                        EnvironmentKit.Cylinder("Stake", instance.transform.parent, position + new Vector3(0.12f, 0.55f, 0f), 0.012f, 1.1f, m.Stake, false);
                }
            }

            Row("tomato", -2.3f, 1.5f, 9.5f, 0.75f, 0.35f, true);
            Row("bell_pepper", -2.3f, 11f, 19f, 0.65f, 0.2f);
            Row("strawberry", 2.3f, 1.5f, 9.5f, 0.45f, 0.2f, rowOffset: -0.25f);
            Row("strawberry", 2.3f, 1.5f, 9.5f, 0.45f, 0.2f, rowOffset: 0.25f);
            Row("potato", 2.3f, 11f, 19f, 0.55f, 0.3f);
            Row("potato", -4.6f, 1.5f, 9.5f, 0.55f, 0.3f);
            Row("tomato", -4.6f, 11f, 19f, 0.75f, 0.35f, true);
            Row("tomato", 4.6f, 1.5f, 9.5f, 0.75f, 0.35f, true);
            Row("bell_pepper", 4.6f, 11f, 19f, 0.65f, 0.2f);

            // Corn block on the right, clear of the central route; the specimen stands at its front corner.
            for (var x = 6.4f; x <= 9.4f; x += 1.0f)
                Row("corn", x, 2.2f, 13.5f, 0.8f, 0f, false, 0f);

            // Orchard edge on the left.
            var orchard = new[]
            {
                ("apple", -8.0f, 8.0f), ("cherry", -8.0f, 14.0f), ("apple", -8.0f, 20.0f),
                ("cherry", -10.5f, -1.5f), ("apple", -10.5f, 5.0f), ("cherry", -10.5f, 11.0f), ("apple", -10.5f, 17.0f),
            };
            var trees = EnvironmentKit.Group("Orchard", crops).transform;
            foreach (var (id, x, z) in orchard)
            {
                var plant = Plant(id);
                if (plant != null && plant.FarmPrefab != null)
                    EnvironmentKit.Place(plant.FarmPrefab, trees, new Vector3(x, 0f, z), (float)random.NextDouble() * 360f,
                        0.9f + (float)random.NextDouble() * 0.2f);
            }
        }

        // ------------------------------------------------------------------ specimens

        static void BuildSpecimens(Transform parent, PlantCatalog catalog, Materials m)
        {
            // Position (base), and which side the aisle is on (+1: aisle towards +X).
            var layout = new Dictionary<string, (Vector3 position, float aisleSide)>
            {
                ["tomato"] = (new Vector3(-2.3f, BedHeight, -5.8f), 1f),
                ["bell_pepper"] = (new Vector3(-2.3f, BedHeight, -3.6f), 1f),
                ["strawberry"] = (new Vector3(2.3f, BedHeight, -5.8f), -1f),
                ["potato"] = (new Vector3(2.3f, BedHeight, -3.6f), -1f),
                ["apple"] = (new Vector3(-8.0f, 0f, -4.5f), 1f),
                ["cherry"] = (new Vector3(-8.0f, 0f, 2.2f), 1f),
                ["corn"] = (new Vector3(6.4f, 0f, 1.6f), -1f),
            };

            foreach (var plant in catalog.Plants)
            {
                if (!layout.TryGetValue(plant.PlantId, out var place))
                {
                    Debug.LogError($"[BTP] No farm specimen position for '{plant.PlantId}'.");
                    continue;
                }

                BuildSpecimen(parent, plant, place.position, place.aisleSide, m);
            }
        }

        static void BuildSpecimen(Transform parent, PlantDefinition plant, Vector3 position, float aisleSide, Materials m)
        {
            var root = new GameObject($"Specimen_{plant.PlantId}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;

            var model = plant.FarmPrefab != null
                ? EnvironmentKit.Place(plant.FarmPrefab, root.transform, Vector3.zero, 20f)
                : PlantSpawner.CreateSetupErrorMarker($"{plant.DisplayName} farm model missing", root.transform);

            var bounds = LocalBounds(model, root.transform);
            var radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.3f, 1.6f);
            var height = Mathf.Max(bounds.size.y, 0.3f);

            // Selection volume: a trunk-to-canopy box, generous for small plants.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(bounds.center.x, height * 0.5f, bounds.center.z);
            collider.size = new Vector3(Mathf.Max(bounds.size.x, 0.5f), height, Mathf.Max(bounds.size.z, 0.5f));

            var ringMesh = ProceduralMeshes.Ring($"SelectionRing_{radius:0.00}", radius, radius + 0.06f);
            var ring = EnvironmentKit.MeshObject("SelectionRing", root.transform, ringMesh, m.Ring, new Vector3(0f, 0.02f, 0f),
                Quaternion.identity, Vector3.one, false, false);
            var selectionRing = ring.AddComponent<SelectionRing>();
            selectionRing.Bind(ring.GetComponent<MeshRenderer>());

            // Label sign on the aisle side.
            var toAisle = new Vector3(aisleSide, 0f, 0f);
            var signBase = toAisle * (radius + 0.35f) + Vector3.back * 0.1f;
            var signGround = -position.y;
            var post = EnvironmentKit.Box("SignPost", root.transform, signBase + new Vector3(0f, signGround + 0.45f, 0f), new Vector3(0.05f, 0.9f, 0.05f), m.Wood, false);
            GameObjectUtility.SetStaticEditorFlags(post, 0);
            var signPosition = signBase + new Vector3(0f, signGround + 0.95f, 0f);
            var sign = UIFactory.WorldCanvas("NameSign", root.transform, new Vector2(420f, 170f), signPosition,
                UIFactory.Facing(position + signPosition, SpawnHead + new Vector3(0f, 0f, 2f), 12f));
            var signRect = (RectTransform)sign.transform;
            UIFactory.Panel(signRect, new Color(0.24f, 0.17f, 0.10f, 0.95f), false);
            UIFactory.Stretch(UIFactory.Text("Name", signRect, plant.DisplayName, 46f, Color.white, TextAlignmentOptions.Top, FontStyles.Bold).rectTransform, 14f);
            UIFactory.Stretch(UIFactory.Text("Scientific", signRect, plant.ScientificName, 26f, new Color(0.93f, 0.88f, 0.8f), TextAlignmentOptions.Bottom, FontStyles.Italic).rectTransform, 16f);

            // Name card shown while this plant is selected.
            var cardHeight = Mathf.Min(height + 0.35f, 2.1f);
            var cardPosition = toAisle * Mathf.Min(radius, 0.6f) + new Vector3(0f, cardHeight, 0f);
            var card = UIFactory.WorldCanvas("NameCard", root.transform, new Vector2(560f, 200f), cardPosition, Quaternion.identity);
            card.gameObject.AddComponent<FaceCamera>();
            var cardRect = (RectTransform)card.transform;
            var cardContent = UIFactory.Node("Content", cardRect);
            UIFactory.Stretch(cardContent);
            UIFactory.Panel(cardContent);
            var title = UIFactory.Text("Title", cardContent, plant.DisplayName, 50f, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
            UIFactory.Stretch(title.rectTransform, 18f);
            var subtitle = UIFactory.Text("Subtitle", cardContent, "Selected", 26f, UIPalette.Cyan, TextAlignmentOptions.Bottom);
            UIFactory.Stretch(subtitle.rectTransform, 20f);
            var nameCard = card.gameObject.AddComponent<PlantNameCard>();
            nameCard.Bind(cardContent.gameObject, title, subtitle);
            cardContent.gameObject.SetActive(false);

            XRSceneWiring.MakeSelectableSpecimen(root, plant.PlantId, selectionRing, nameCard);
        }

        static Bounds LocalBounds(GameObject model, Transform space)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var first = true;
            foreach (var renderer in renderers)
            {
                if (renderer.gameObject.name == "Billboard")
                    continue;
                var b = renderer.bounds;
                var min = space.InverseTransformPoint(b.min);
                var max = space.InverseTransformPoint(b.max);
                var local = new Bounds((min + max) * 0.5f, max - min);
                if (first)
                    bounds = local;
                else
                    bounds.Encapsulate(local);
                first = false;
            }

            return bounds;
        }

        // ------------------------------------------------------------------ fence, tower, scenery

        static void BuildFence(Transform environment, Materials m)
        {
            var fence = EnvironmentKit.Group("TimberFence", environment).transform;
            const float minX = -12f, maxX = 12f, minZ = -12f, maxZ = 25f;
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

        static void BuildWaterTower(Transform environment, Materials m)
        {
            var tower = EnvironmentKit.Group("WaterTower", environment).transform;
            tower.localPosition = new Vector3(15.5f, 0f, 21.5f);
            foreach (var x in new[] { -1.3f, 1.3f })
            foreach (var z in new[] { -1.3f, 1.3f })
                EnvironmentKit.Box("Leg", tower, new Vector3(x, 3.5f, z), new Vector3(0.25f, 7f, 0.25f), m.DarkWood, false);
            foreach (var y in new[] { 2.2f, 4.6f })
            {
                EnvironmentKit.Box("Brace", tower, new Vector3(0f, y, -1.3f), new Vector3(2.7f, 0.15f, 0.12f), m.DarkWood, false);
                EnvironmentKit.Box("Brace", tower, new Vector3(0f, y, 1.3f), new Vector3(2.7f, 0.15f, 0.12f), m.DarkWood, false);
                EnvironmentKit.Box("Brace", tower, new Vector3(-1.3f, y, 0f), new Vector3(0.12f, 0.15f, 2.7f), m.DarkWood, false);
                EnvironmentKit.Box("Brace", tower, new Vector3(1.3f, y, 0f), new Vector3(0.12f, 0.15f, 2.7f), m.DarkWood, false);
            }

            EnvironmentKit.Box("Platform", tower, new Vector3(0f, 7.05f, 0f), new Vector3(3.3f, 0.12f, 3.3f), m.DarkWood, false);
            EnvironmentKit.Cylinder("Tank", tower, new Vector3(0f, 8.35f, 0f), 1.6f, 2.5f, m.Wood, false);
            EnvironmentKit.MeshObject("Roof", tower, ProceduralMeshes.Cone("WaterTowerRoof", 1.85f, 1.1f), m.DarkWood,
                new Vector3(0f, 9.6f, 0f), Quaternion.identity, Vector3.one, true);
        }

        static void BuildScenery(Transform environment, Materials m, PlantCatalog catalog)
        {
            EnvironmentKit.MeshObject("Mountains", environment,
                ProceduralMeshes.MountainRing("FarmMountains", 210f, 330f, 28f, 95f, 5), m.Mountain,
                Vector3.zero, Quaternion.identity, Vector3.one);

            var rocks = EnvironmentKit.Group("Boulders", environment).transform;
            foreach (var (position, scale) in new[]
                     {
                         (new Vector3(12.8f, 0.3f, 20.5f), new Vector3(2.2f, 1.5f, 1.8f)),
                         (new Vector3(-12.9f, 0.2f, -9f), new Vector3(1.4f, 0.9f, 1.2f)),
                         (new Vector3(13f, 0.15f, -4f), new Vector3(0.9f, 0.6f, 1.1f)),
                     })
                EnvironmentKit.Primitive(PrimitiveType.Sphere, "Boulder", rocks, position, scale, m.Rock, false, Quaternion.Euler(0f, position.x * 10f, 0f), true);

            // Distant trees outside the fence: farm prefabs, so they draw as billboards.
            var background = EnvironmentKit.Group("BackgroundTrees", environment).transform;
            var random = new System.Random(7);
            var treeIds = new[] { "cherry", "apple" };
            for (var i = 0; i < 22; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var distance = 26f + (float)random.NextDouble() * 30f;
                var position = new Vector3(Mathf.Sin(angle) * distance, 0f, 6f + Mathf.Cos(angle) * distance);
                if (position.z < -15f && Mathf.Abs(position.x) < 10f)
                    continue; // keep the view behind the spawn point open
                if (catalog.TryGet(treeIds[i % 2], out var tree) && tree.FarmPrefab != null)
                    EnvironmentKit.Place(tree.FarmPrefab, background, position, (float)random.NextDouble() * 360f, 1.3f + (float)random.NextDouble() * 0.5f);
            }
        }

        // ------------------------------------------------------------------ notice board and navigation

        static void BuildNoticeBoard(Transform board, Materials m)
        {
            board.position = new Vector3(-1.75f, 0f, -8.1f);
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
            UIFactory.Place(UIFactory.Text("Intro", rect, "Pick a plant in the field, then study it up close in the lab.", 32f, UIPalette.TextSecondary).rectTransform, 50f, 125f, 1300f, 50f);
            UIFactory.Place(UIFactory.Text("Steps", rect,
                "1  Point at a labelled plant and pull the trigger, or pinch, to select it.\n" +
                "2  Press <b>Enter Lab</b>. With no plant selected the lab opens with Tomato.",
                30f, Color.white).rectTransform, 50f, 200f, 1300f, 130f);

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

            var recenter = UIFactory.Button("Recenter", rect, "Recenter view", 34f, out _);
            UIFactory.Place((RectTransform)recenter.transform, 920f, 600f, 430f, 180f);
            recenter.gameObject.AddComponent<EnvironmentButton>().Action = EnvironmentButton.ButtonAction.Recenter;
        }

        static void BuildNavigation(Transform navigation)
        {
            var spawn = new GameObject("SpawnAnchor", typeof(SpawnAnchor));
            spawn.transform.SetParent(navigation, false);
            spawn.transform.SetPositionAndRotation(SpawnPosition, Quaternion.identity);

            // Viewing points; the aisle and cross paths are teleportable everywhere else.
            XRSceneWiring.CreateTeleportAnchor(navigation, "Viewpoint_NoticeBoard", new Vector3(-1.05f, 0f, -8.75f),
                UIFactory.Facing(new Vector3(-1.75f, 0f, -8.1f), new Vector3(-1.05f, 0f, -8.75f)).eulerAngles.y);
            XRSceneWiring.CreateTeleportAnchor(navigation, "Viewpoint_Orchard", new Vector3(-6.2f, 0f, 0f), -90f);
            XRSceneWiring.CreateTeleportAnchor(navigation, "Viewpoint_Corn", new Vector3(5.2f, 0f, 0f), 90f);
            XRSceneWiring.CreateTeleportAnchor(navigation, "Viewpoint_FieldEnd", new Vector3(0f, 0f, 21f), 180f);
        }
    }
}
