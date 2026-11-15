using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    public static partial class TreatmentAssetBuilder
    {
        static void Slot(Transform parent, string name, Vector3 position, float yaw, float spacing = 0.25f)
        {
            var go = new GameObject("Slot_" + name, typeof(TreatmentSlot));
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.GetComponent<TreatmentSlot>().Configure(name, spacing);
        }

        /// <summary>
        /// Layout shared by every treatment room (viewer at the spawn facing +Z). The leaf specimen stands
        /// on the inspection holder in front of the viewer; tool rack and waste on the left, wall controls
        /// and the clean station on the right. Slot yaw = direction from the viewer, so labels face the user.
        /// </summary>
        static void RoomLayout(Transform root)
        {
            const float table = 0.8f;
            Slot(root, "spawn", new Vector3(0f, 0f, -1.35f), 0f);
            Slot(root, "interface", new Vector3(0f, 0f, -1.35f), 0f);

            // Specimen station: the grey pot with its cyan ring, clip post and the specimen slot on top.
            SpecimenPot(root, new Vector3(0f, 0f, -0.35f));
            Slot(root, "specimen", new Vector3(0f, 0.92f, -0.35f), 0f);
            Slot(root, "leaf_whole", new Vector3(0f, 0.88f, -0.5f), 0f);

            // Comparison stand: the new leaf from the treated plant appears here at the follow-up.
            Part(PrimitiveType.Cylinder, "ComparisonStand", root, new Vector3(-0.62f, 0.45f, -0.05f), new Vector3(0.24f, 0.45f, 0.24f), LabPot, default, true);
            Slot(root, "comparison", new Vector3(-0.62f, 1.08f, -0.05f), -25f);

            Slot(root, "tool_rack", new Vector3(-0.72f, BenchTop + 0.02f, -0.72f), BenchYaw);
            Workbench(root);
            Slot(root, "disposal", new Vector3(-0.95f, 0f, 0.45f), -25f, 0.5f);
            Slot(root, "sanitation", new Vector3(-1.75f, 0f, 0.2f), -48f);
            Slot(root, "bench", new Vector3(-1.5f, table + 0.01f, 1.4f), -29f, 0.28f);
            LabTable(root, "Bench", new Vector3(-1.52f, 0f, 1.44f), new Vector3(1.2f, table, 0.45f), -29f);
            Slot(root, "clean_station", new Vector3(0.62f, table + 0.01f, 0.55f), 18f, 0.4f);
            Slot(root, "clean_planting", new Vector3(0.62f, table + 0.22f, 0.55f), 18f, 0.15f);
            LabTable(root, "CleanTable", new Vector3(0.64f, 0f, 0.6f), new Vector3(0.9f, table, 0.45f), 18f);
            Slot(root, "mesh_station", new Vector3(1.45f, 0f, 0.95f), 32f);
            Slot(root, "controls", new Vector3(0.9f, 1.45f, 1.3f), 19f, 0.3f);
            Part(PrimitiveType.Cube, "ControlPanel", root, new Vector3(0.92f, 1.42f, 1.34f), new Vector3(0.75f, 0.32f, 0.03f), LabSteel, new Vector3(0f, 19f, 0f));
            Part(PrimitiveType.Cube, "ControlPost", root, new Vector3(0.92f, 0.63f, 1.36f), new Vector3(0.05f, 1.26f, 0.05f), LabSteel, new Vector3(0f, 19f, 0f));
            Slot(root, "containment", new Vector3(-2.2f, 0f, 1.6f), -37f);
            Slot(root, "neighbour", new Vector3(2.1f, 0f, -0.3f), 63f);
            Slot(root, "alt_host", new Vector3(-2.5f, 0f, 2.6f), -30f, 0.35f);
        }

        /// <summary>
        /// The indoor treatment lab shared by every room (see TreatmentAssetBuilder.LabRoom): olive-grey walls, timber
        /// ceiling on steel beams, panoramic windows, cabinets, shelving, and the trunk and grape display.
        /// </summary>
        static void RoomShell(Transform root)
        {
            LabShell(root);
            LabFurnishings(root);
        }

        // Potted crops inside the greenhouse room (TomatoLayoutUpdater moves them in an existing room prefab).
        internal static readonly Vector3 TomatoIndoor = new Vector3(2.75f, 0f, 3.35f);
        internal static readonly Vector3 PepperIndoor = new Vector3(2.9f, 0f, 2.1f);
        internal const string GreenhousePrefab = Prefabs + "/Environments/env_greenhouse.prefab";

        static void BuildEnvironments()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            var grass = MaterialFactory.Solid($"{Materials}/Grass.mat", Color.white, 0.05f, ProceduralTextures.Grass);
            grass.SetTextureScale("_BaseMap", new Vector2(30f, 30f));
            var bed = MaterialFactory.Solid($"{Materials}/FieldSoil.mat", Color.white, 0.05f, ProceduralTextures.Soil);
            bed.SetTextureScale("_BaseMap", new Vector2(2f, 10f));
            var crate = Mat("Crate", new Color(0.6f, 0.45f, 0.28f), 0.1f);

            void Plant(Transform parent, string id, Vector3 position, float yaw, float scale = 1f)
            {
                if (!catalog.TryGet(id, out var plant) || plant.FarmPrefab == null)
                    return;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(plant.FarmPrefab, parent);
                instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                instance.transform.localScale *= scale;
            }

            void Crate(Transform parent, Vector3 position, Material produce, float yaw)
            {
                Part(PrimitiveType.Cube, "Crate", parent, position + Vector3.up * 0.12f, new Vector3(0.5f, 0.24f, 0.35f), crate, new Vector3(0f, yaw, 0f), true);
                for (var i = 0; i < 8; i++)
                    Part(PrimitiveType.Sphere, "Produce", parent, position + new Vector3((i % 4 - 1.5f) * 0.1f, 0.27f, (i / 4 - 0.5f) * 0.12f),
                        Vector3.one * 0.08f, produce);
            }

            GameObject Room(string key)
            {
                var root = Root(key).transform;
                Part(PrimitiveType.Plane, "Outside", root, new Vector3(0f, -0.06f, 6f), new Vector3(6f, 1f, 6f), grass);
                RoomShell(root);
                RoomLayout(root);
                return root.gameObject;
            }

            // Greenhouse room (tomato, bell pepper, strawberry): crop beds outside, potted crops inside.
            {
                var root = Room("env.greenhouse").transform;
                for (var i = 0; i < 6; i++)
                {
                    Plant(root, new[] { "tomato", "bell_pepper", "strawberry" }[i % 3], new Vector3(-3f + i * 1.2f, 0f, 5.6f), i * 40f);
                    Plant(root, new[] { "bell_pepper", "tomato", "strawberry" }[i % 3], new Vector3(-3f + i * 1.2f, 0f, 7.2f), i * 70f);
                }
                // The ~1.3 m wide tomato stays 0.6 m clear of both walls (x 3.6, z 4.2) and 1.26 m from the pepper.
                Plant(root, "tomato", TomatoIndoor, 20f);
                Plant(root, "bell_pepper", PepperIndoor, 80f);
                Plant(root, "strawberry", new Vector3(-2.9f, 0.8f, -1.8f), 0f);
                Save(root.gameObject, "Environments", "env.greenhouse");
            }

            // Orchard room (apple, cherry, peach): fruit trees beyond the windows, fruit crates inside.
            {
                var root = Room("env.orchard").transform;
                for (var i = 0; i < 4; i++)
                {
                    Plant(root, new[] { "apple", "cherry", "peach", "apple" }[i], new Vector3(-4.5f + i * 3f, 0f, 7.5f), i * 37f);
                    Plant(root, new[] { "peach", "apple", "cherry", "peach" }[i], new Vector3(-6.5f, 0f, -1f + i * 2.6f), i * 50f);
                }
                Crate(root, new Vector3(2.7f, 0f, 3.3f), Mat("FruitRed", new Color(0.78f, 0.16f, 0.12f), 0.5f), 10f);
                Crate(root, new Vector3(2.7f, 0f, 2.6f), Mat("FruitPeach", new Color(0.95f, 0.6f, 0.35f), 0.5f), -5f);
                Save(root.gameObject, "Environments", "env.orchard");
            }

            // Vineyard room (grape): trellised vines beyond the windows, grape crates inside.
            {
                var root = Room("env.vineyard").transform;
                // Pergola rows outside the windows: each Grape_Farm module carries its posts and wires.
                foreach (var x in new[] { -3.2f, 0f, 3.2f })
                    for (var z = 0; z < 3; z++)
                        Plant(root, "grape", new Vector3(x, 0f, 6.0f + z * 3.2f), 0f);
                Crate(root, new Vector3(2.7f, 0f, 3.3f), Mat("GrapePurple", new Color(0.3f, 0.12f, 0.35f), 0.5f), 10f);
                Save(root.gameObject, "Environments", "env.vineyard");
            }

            // Field room (corn, potato): crop rows beyond the windows, sacks and cobs inside.
            {
                var root = Room("env.field").transform;
                foreach (var x in new[] { -3.2f, 0f, 3.2f })
                    Part(PrimitiveType.Cube, "Bed", root, new Vector3(x, 0.03f, 8f), new Vector3(1.4f, 0.06f, 6f), bed, default, true);
                for (var z = 0; z < 4; z++)
                {
                    Plant(root, "corn", new Vector3(-3.2f, 0.06f, 5.8f + z * 1.4f), z * 50f);
                    Plant(root, "corn", new Vector3(0f, 0.06f, 5.8f + z * 1.4f), z * 30f);
                    Plant(root, "potato", new Vector3(3.2f, 0.06f, 5.8f + z * 1.4f), z * 80f);
                }
                Crate(root, new Vector3(2.7f, 0f, 3.3f), Mat("CornYellow", new Color(0.95f, 0.8f, 0.25f), 0.3f), 10f);
                Crate(root, new Vector3(2.7f, 0f, 2.6f), Mat("TuberSkin", new Color(0.62f, 0.48f, 0.3f), 0.2f), -5f);
                Save(root.gameObject, "Environments", "env.field");
            }
        }
    }
}