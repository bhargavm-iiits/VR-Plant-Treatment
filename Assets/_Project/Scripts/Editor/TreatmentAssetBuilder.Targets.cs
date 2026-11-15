using BTP.UI;
using UnityEngine;

namespace BTP.Editor
{
    public static partial class TreatmentAssetBuilder
    {
        static void Seedling(Transform t, Material leaves)
        {
            Part(PrimitiveType.Cylinder, "Stem", t, new Vector3(0f, 0.08f, 0f), new Vector3(0.01f, 0.08f, 0.01f), Green);
            for (var i = 0; i < 6; i++)
                Part(PrimitiveType.Sphere, "Leaf", t, new Vector3(Mathf.Cos(i * 1.05f) * 0.05f, 0.1f + i * 0.015f, Mathf.Sin(i * 1.05f) * 0.05f),
                    new Vector3(0.08f, 0.012f, 0.04f), leaves, new Vector3(0f, i * 60f, 12f));
            AddBox(t.gameObject, new Vector3(0f, 0.1f, 0f), new Vector3(0.18f, 0.2f, 0.18f));
        }

        static void BuildTargets()
        {
            var brown = Mat("DeadBrown", new Color(0.33f, 0.22f, 0.12f), 0.1f);
            var black = Mat("MummyBlack", new Color(0.08f, 0.06f, 0.06f), 0.2f);
            var orange = Mat("RustOrange", new Color(0.86f, 0.45f, 0.1f), 0.2f);
            var water = Glow("WaterDrop", new Color(0.35f, 0.7f, 1f), 1.4f);
            var cyan = Glow("CyanSoft", new Color(0.13f, 0.83f, 0.93f, 0.6f), 1.2f);
            var mite = Mat("MiteWhite", new Color(0.95f, 0.95f, 0.9f), 0.2f);
            var proxy = Clear("PlantProxy", new Color(0.13f, 0.83f, 0.93f, 0.12f));
            var fruit = Mat("FruitRed", new Color(0.78f, 0.16f, 0.12f), 0.5f);
            var grapes = Mat("GrapePurple", new Color(0.3f, 0.12f, 0.35f), 0.5f);
            var sick = Mat("SickYellow", new Color(0.72f, 0.7f, 0.25f), 0.2f);
            var tuber = Mat("TuberSkin", new Color(0.62f, 0.48f, 0.3f), 0.2f);
            var rot = Mat("TuberRot", new Color(0.45f, 0.2f, 0.1f), 0.1f);
            var juniper = Mat("Juniper", new Color(0.12f, 0.3f, 0.16f), 0.1f);

            // Leaf targets: the Lab's 3D leaf model of each case (its UVs match the diseased and healthy
            // leaf textures), 0.15 m tall. The renderer named "Blade" takes the case's material key.
            var leaves = UnityEditor.AssetDatabase.LoadAssetAtPath<BTP.Plants.LeafCatalog>(LeafAssetPipeline.CatalogPath);
            foreach (var info in BTP.Plants.DiseaseClasses.AllCases)
            {
                if (leaves == null || !leaves.TryGet(info.PlantId, info.DiseaseId, out var leafPrefab))
                    throw new System.InvalidOperationException($"Leaf model missing for {info.PlantId}/{info.DiseaseId}.");
                var mesh = leafPrefab.GetComponentInChildren<MeshFilter>().sharedMesh;
                Make("Targets", $"target.leaf.{info.PlantId}.{info.DiseaseId}", t =>
                {
                    var blade = new GameObject("Blade", typeof(MeshFilter), typeof(MeshRenderer));
                    blade.transform.SetParent(t, false);
                    var bounds = mesh.bounds;
                    var scale = 0.15f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    blade.transform.localScale = Vector3.one * scale;
                    blade.transform.localPosition = -bounds.center * scale;
                    blade.GetComponent<MeshFilter>().sharedMesh = mesh;
                    blade.GetComponent<MeshRenderer>().sharedMaterial = Green;
                    blade.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    AddBox(t.gameObject, Vector3.zero, new Vector3(bounds.size.x * scale, bounds.size.y * scale, 0.04f));
                    Covered(t, Vector3.zero, new Vector3(bounds.size.x * scale * 1.1f, 0.16f, 0.025f));
                    var observed = Group("State_observed", t);
                    Part(PrimitiveType.Sphere, "Check", observed, new Vector3(0.06f, 0.07f, 0f), Vector3.one * 0.014f, cyan);
                });
            }
            Make("Targets", "target.stem_marker", t =>
            {
                var capsule = t.gameObject.AddComponent<CapsuleCollider>();
                capsule.radius = 0.035f;
                capsule.height = 0.4f;
            });
            Make("Targets", "target.root_zone", t =>
            {
                Part(PrimitiveType.Cylinder, "Soil", t, new Vector3(0f, 0.005f, 0f), new Vector3(0.2f, 0.005f, 0.2f), SoilMat);
                AddBox(t.gameObject, new Vector3(0f, 0.03f, 0f), new Vector3(0.22f, 0.06f, 0.22f));
                var state = Group("State_irrigated", t);
                Part(PrimitiveType.Cylinder, "DripLine", state, new Vector3(-0.12f, 0.015f, 0f), new Vector3(0.012f, 0.12f, 0.012f), Dark, new Vector3(0f, 0f, 90f));
                Part(PrimitiveType.Sphere, "Drop", state, new Vector3(0f, 0.02f, 0f), Vector3.one * 0.012f, water);
            });
            Make("Targets", "target.soil_surface", t =>
            {
                Part(PrimitiveType.Cylinder, "BareSoil", t, new Vector3(0f, 0.004f, 0f), new Vector3(0.26f, 0.004f, 0.26f), SoilMat);
                AddBox(t.gameObject, new Vector3(0f, 0.02f, 0f), new Vector3(0.26f, 0.04f, 0.26f));
                Part(PrimitiveType.Cylinder, "Mulch", Group("State_placed", t), new Vector3(0f, 0.012f, 0f), new Vector3(0.3f, 0.008f, 0.3f), Straw);
            });
            Make("Targets", "target.support_point", t =>
            {
                Part(PrimitiveType.Cylinder, "Peg", t, new Vector3(0f, 0.03f, 0f), new Vector3(0.02f, 0.03f, 0.02f), cyan);
                AddBox(t.gameObject, new Vector3(0f, 0.05f, 0f), new Vector3(0.08f, 0.1f, 0.08f));
                Part(PrimitiveType.Cube, "Stake", Group("State_placed", t), new Vector3(0f, 0.5f, 0f), new Vector3(0.02f, 1f, 0.02f), Wood);
            });
            Make("Targets", "target.mummy", t =>
            {
                Part(PrimitiveType.Sphere, "Mummy", t, Vector3.zero, new Vector3(0.05f, 0.045f, 0.05f), black);
                AddBox(t.gameObject, Vector3.zero, Vector3.one * 0.08f);
            });
            Make("Targets", "target.leaf_litter", t =>
            {
                for (var i = 0; i < 4; i++)
                    Part(PrimitiveType.Cube, "Leaf", t, new Vector3((i - 1.5f) * 0.05f, 0.005f, (i % 2) * 0.04f), new Vector3(0.07f, 0.004f, 0.045f), brown, new Vector3(0f, i * 40f, 0f));
                AddBox(t.gameObject, new Vector3(0f, 0.03f, 0.02f), new Vector3(0.26f, 0.06f, 0.14f));
            });
            Make("Targets", "target.branch", t =>
            {
                Part(PrimitiveType.Cylinder, "Branch", t, Vector3.zero, new Vector3(0.025f, 0.2f, 0.025f), Wood, new Vector3(0f, 0f, 70f));
                Part(PrimitiveType.Cylinder, "Canker", t, Vector3.zero, new Vector3(0.03f, 0.03f, 0.03f), black, new Vector3(0f, 0f, 70f));
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.4f, 0.12f, 0.06f));
            });
            Make("Targets", "target.gall", t =>
            {
                Part(PrimitiveType.Sphere, "Gall", t, Vector3.zero, Vector3.one * 0.05f, brown);
                for (var i = 0; i < 5; i++)
                    Part(PrimitiveType.Cylinder, "Horn", t, Quaternion.Euler(0f, i * 72f, 40f) * Vector3.up * 0.03f, new Vector3(0.006f, 0.02f, 0.006f), orange,
                        new Vector3(0f, i * 72f, 40f));
                AddBox(t.gameObject, Vector3.zero, Vector3.one * 0.1f);
            });
            Make("Targets", "target.plant_volume", t =>
            {
                Part(PrimitiveType.Cylinder, "Proxy", t, new Vector3(0f, 0.15f, 0f), new Vector3(1f, 0.15f, 1f), proxy);
                var capsule = t.gameObject.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.15f, 0f);
                capsule.radius = 0.5f;
                capsule.height = 0.3f;
            });
            Make("Targets", "target.canopy", t =>
            {
                for (var i = 0; i < 4; i++)
                    Part(PrimitiveType.Sphere, "Shoot", t, new Vector3((i - 1.5f) * 0.05f, (i % 2) * 0.05f, 0f), new Vector3(0.09f, 0.07f, 0.06f), Green);
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.25f, 0.15f, 0.1f));
                Covered(t, Vector3.zero, new Vector3(0.28f, 0.18f, 0.12f));
                Part(PrimitiveType.Cylinder, "Wire", Group("State_placed", t), new Vector3(0f, 0.05f, 0f), new Vector3(0.004f, 0.18f, 0.004f), Dark, new Vector3(0f, 0f, 90f));
            });
            Make("Targets", "target.weed", t =>
            {
                for (var i = 0; i < 3; i++)
                    Part(PrimitiveType.Capsule, "Blade", t, new Vector3((i - 1) * 0.02f, 0.04f, 0f), new Vector3(0.012f, 0.04f, 0.012f), Green, new Vector3(0f, 0f, (i - 1) * 25f));
                AddBox(t.gameObject, new Vector3(0f, 0.04f, 0f), new Vector3(0.1f, 0.09f, 0.08f));
            });
            Make("Targets", "target.volunteer", t =>
            {
                Part(PrimitiveType.Cylinder, "Stem", t, new Vector3(0f, 0.06f, 0f), new Vector3(0.012f, 0.06f, 0.012f), Green);
                for (var i = 0; i < 4; i++)
                    Part(PrimitiveType.Sphere, "Leaf", t, new Vector3(Mathf.Cos(i * 1.57f) * 0.04f, 0.1f, Mathf.Sin(i * 1.57f) * 0.04f), new Vector3(0.06f, 0.012f, 0.04f), Green, new Vector3(0f, i * 90f, 0f));
                AddBox(t.gameObject, new Vector3(0f, 0.07f, 0f), new Vector3(0.14f, 0.14f, 0.14f));
            });
            Make("Targets", "target.trunk_cutaway", t =>
            {
                Part(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0f, 0.05f, 0f), new Vector3(0.1f, 0.12f, 0.1f), Wood, new Vector3(0f, 0f, 90f));
                Part(PrimitiveType.Cylinder, "Decay", t, new Vector3(-0.121f, 0.05f, 0f), new Vector3(0.07f, 0.002f, 0.07f), black, new Vector3(0f, 0f, 90f));
                AddBox(t.gameObject, new Vector3(0f, 0.05f, 0f), new Vector3(0.26f, 0.12f, 0.12f));
            });
            Make("Targets", "target.tuber", t =>
            {
                Part(PrimitiveType.Sphere, "Tuber", t, new Vector3(0f, 0.04f, 0f), new Vector3(0.1f, 0.07f, 0.07f), tuber);
                Part(PrimitiveType.Cylinder, "CutFace", t, new Vector3(0f, 0.04f, -0.034f), new Vector3(0.08f, 0.002f, 0.055f), rot, new Vector3(90f, 0f, 0f));
                AddBox(t.gameObject, new Vector3(0f, 0.04f, 0f), new Vector3(0.12f, 0.09f, 0.09f));
            });
            Make("Targets", "target.mites", t =>
            {
                Part(PrimitiveType.Cylinder, "Stipple", t, Vector3.zero, new Vector3(0.09f, 0.002f, 0.09f), Mat("Stipple", new Color(0.85f, 0.85f, 0.6f)), new Vector3(90f, 0f, 0f));
                for (var i = 0; i < 9; i++)
                    Part(PrimitiveType.Sphere, "Mite", t, new Vector3(Mathf.Cos(i * 0.7f) * 0.03f * (i % 3 + 1) / 3f, Mathf.Sin(i * 0.7f) * 0.03f, -0.004f), Vector3.one * 0.005f, mite);
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.1f, 0.1f, 0.03f));
                Part(PrimitiveType.Cube, "Sachet", Group("State_released", t), new Vector3(0f, -0.06f, 0f), new Vector3(0.04f, 0.05f, 0.005f), Cardboard);
                Covered(t, Vector3.zero, new Vector3(0.11f, 0.11f, 0.03f));
            });
            Make("Targets", "target.whiteflies", t =>
            {
                for (var i = 0; i < 12; i++)
                    Part(PrimitiveType.Sphere, "Fly", t, new Vector3(Mathf.Sin(i * 2.1f) * 0.06f, Mathf.Cos(i * 1.3f) * 0.04f, Mathf.Sin(i * 0.9f) * 0.03f), new Vector3(0.008f, 0.004f, 0.006f), mite);
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.15f, 0.1f, 0.08f));
            });
            Make("Targets", "target.seedling", t => Seedling(t, Green));
            Make("Targets", "target.seedling_spotted", t =>
            {
                Part(PrimitiveType.Cylinder, "Pot", t, new Vector3(0f, 0.035f, 0f), new Vector3(0.08f, 0.035f, 0.08f), Mat("PotBlack", new Color(0.1f, 0.1f, 0.1f)));
                Seedling(t, sick);
            });
            Make("Targets", "target.ties", t =>
            {
                Part(PrimitiveType.Cylinder, "Support", t, new Vector3(0f, 0.08f, 0f), new Vector3(0.012f, 0.08f, 0.012f), Wood);
                for (var i = 0; i < 3; i++)
                    Part(PrimitiveType.Cylinder, "Tie", t, new Vector3(0f, 0.04f + i * 0.05f, 0f), new Vector3(0.03f, 0.003f, 0.03f), Green);
                AddBox(t.gameObject, new Vector3(0f, 0.08f, 0f), new Vector3(0.06f, 0.18f, 0.06f));
            });
            Make("Targets", "target.old_foliage", t =>
            {
                for (var i = 0; i < 4; i++)
                    Part(PrimitiveType.Cube, "OldLeaf", t, new Vector3((i - 1.5f) * 0.03f, 0.02f, 0f), new Vector3(0.05f, 0.004f, 0.04f),
                        Mat("OldFoliage", new Color(0.42f, 0.22f, 0.25f)), new Vector3(20f, i * 45f, 0f));
                AddBox(t.gameObject, new Vector3(0f, 0.02f, 0f), new Vector3(0.16f, 0.06f, 0.08f));
            });
            Make("Targets", "target.cull_pile", t =>
            {
                for (var i = 0; i < 7; i++)
                    Part(PrimitiveType.Sphere, "Cull", t, new Vector3(Mathf.Cos(i) * 0.06f, 0.03f + (i % 3) * 0.02f, Mathf.Sin(i) * 0.06f), new Vector3(0.07f, 0.05f, 0.05f), i % 2 == 0 ? tuber : rot);
                AddBox(t.gameObject, new Vector3(0f, 0.04f, 0f), new Vector3(0.2f, 0.1f, 0.2f));
            });
            Make("Targets", "target.cluster", t =>
            {
                for (var i = 0; i < 12; i++)
                {
                    var row = i < 5 ? 0 : i < 9 ? 1 : i < 11 ? 2 : 3;
                    Part(PrimitiveType.Sphere, "Berry", t, new Vector3(Mathf.Cos(i * 1.3f) * 0.02f * (3 - row), -row * 0.025f, Mathf.Sin(i * 1.3f) * 0.015f), Vector3.one * 0.02f, grapes);
                }
                AddBox(t.gameObject, new Vector3(0f, -0.04f, 0f), new Vector3(0.09f, 0.12f, 0.06f));
                Covered(t, new Vector3(0f, -0.04f, 0f), new Vector3(0.1f, 0.13f, 0.07f));
            });
            Make("Targets", "target.fruit", t =>
            {
                Part(PrimitiveType.Sphere, "Fruit", t, Vector3.zero, Vector3.one * 0.065f, fruit);
                Part(PrimitiveType.Sphere, "Spot", t, new Vector3(0f, 0.01f, -0.03f), new Vector3(0.012f, 0.012f, 0.006f), black);
                AddBox(t.gameObject, Vector3.zero, Vector3.one * 0.08f);
                Covered(t, Vector3.zero, Vector3.one * 0.075f);
            });
            Make("Targets", "target.juniper", t =>
            {
                Part(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0f, 0.2f, 0f), new Vector3(0.06f, 0.2f, 0.06f), Wood);
                var cone = new GameObject("Crown", typeof(MeshFilter), typeof(MeshRenderer));
                cone.transform.SetParent(t, false);
                cone.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                cone.GetComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Cone("JuniperCrown", 0.45f, 1.5f);
                cone.GetComponent<MeshRenderer>().sharedMaterial = juniper;
                AddBox(t.gameObject, new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 1.8f, 0.9f));
            });
        }
    }
}