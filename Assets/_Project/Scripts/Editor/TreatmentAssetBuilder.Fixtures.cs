using System;
using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    public static partial class TreatmentAssetBuilder
    {
        static readonly Color OnGreen = new Color(0.3f, 1f, 0.5f);

        static void BuildFixtures()
        {
            var bin = Mat("BinGreen", new Color(0.24f, 0.33f, 0.24f), 0.3f);
            var panelGrey = Mat("FixtureGrey", new Color(0.55f, 0.57f, 0.6f));
            var glass = Clear("Glass", new Color(0.8f, 0.92f, 1f, 0.28f));
            var plastic = Clear("BagPlastic", new Color(0.95f, 0.97f, 1f, 0.3f));
            var insectMesh = Clear("InsectMesh", new Color(0.85f, 0.88f, 0.9f, 0.35f));
            var light = Glow("IndicatorGreen", new Color(0.2f, 0.95f, 0.45f), 2f);

            Make("Fixtures", "fx.disposal_bin", t =>
            {
                Part(PrimitiveType.Cube, "Bin", t, new Vector3(0f, 0.22f, 0f), new Vector3(0.4f, 0.44f, 0.34f), bin);
                Part(PrimitiveType.Cube, "Lid", t, new Vector3(0f, 0.455f, 0f), new Vector3(0.42f, 0.03f, 0.36f), Mat("BinLid", new Color(0.42f, 0.36f, 0.3f)));
                Label(t, "PLANT WASTE", new Vector3(0f, 0.25f, -0.172f), 340f, 44f, Color.white);
                AddBox(t.gameObject, new Vector3(0f, 0.24f, 0f), new Vector3(0.42f, 0.48f, 0.36f));
            });
            Make("Fixtures", "fx.sanitation", t =>
            {
                Part(PrimitiveType.Cube, "Stand", t, new Vector3(0f, 0.4f, 0f), new Vector3(0.4f, 0.8f, 0.32f), Steel);
                Part(PrimitiveType.Cube, "Basin", t, new Vector3(0f, 0.82f, 0f), new Vector3(0.36f, 0.06f, 0.28f), White);
                Part(PrimitiveType.Cube, "Container", t, new Vector3(0.1f, 0.95f, 0.06f), new Vector3(0.12f, 0.2f, 0.1f), White);
                Label(t, "Tool hygiene", new Vector3(0f, 0.6f, -0.162f), 300f, 34f, Color.black);
                AddBox(t.gameObject, new Vector3(0f, 0.55f, 0f), new Vector3(0.42f, 1.1f, 0.34f));
            });
            Make("Fixtures", "fx.valve", t =>
            {
                Part(PrimitiveType.Cube, "Box", t, Vector3.zero, new Vector3(0.16f, 0.16f, 0.06f), panelGrey);
                Part(PrimitiveType.Cylinder, "Handle", t, new Vector3(0f, 0f, -0.04f), new Vector3(0.08f, 0.01f, 0.08f), Mat("ValveBlue", new Color(0.15f, 0.35f, 0.8f)), new Vector3(90f, 0f, 0f));
                Label(t, "Drip irrigation", new Vector3(0f, 0.13f, -0.031f), 220f, 22f, Color.white);
                var on = Group("State_on", t);
                Part(PrimitiveType.Sphere, "Light", on, new Vector3(0.06f, 0.06f, -0.035f), Vector3.one * 0.02f, light);
                Label(on, "ON", new Vector3(0f, -0.12f, -0.031f), 120f, 26f, OnGreen);
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.18f, 0.2f, 0.08f));
            });
            Make("Fixtures", "fx.vent", t =>
            {
                Part(PrimitiveType.Cube, "Switch", t, Vector3.zero, new Vector3(0.14f, 0.18f, 0.05f), panelGrey);
                Part(PrimitiveType.Cube, "Rocker", t, new Vector3(0f, 0f, -0.03f), new Vector3(0.05f, 0.08f, 0.02f), Dark);
                Label(t, "Roof Vent", new Vector3(0f, 0.14f, -0.026f), 200f, 24f, Color.white);
                var on = Group("State_on", t);
                Part(PrimitiveType.Sphere, "Light", on, new Vector3(0.05f, 0.07f, -0.03f), Vector3.one * 0.02f, light);
                Label(on, "OPEN", new Vector3(0f, -0.13f, -0.026f), 140f, 26f, OnGreen);
                Part(PrimitiveType.Cube, "OpenPane", on, new Vector3(0f, 2.2f, 0.8f), new Vector3(1.2f, 0.02f, 0.6f), glass, new Vector3(-30f, 0f, 0f));
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.16f, 0.2f, 0.07f));
            });
            Make("Fixtures", "fx.fan", t =>
            {
                Part(PrimitiveType.Cylinder, "Pole", t, new Vector3(0f, -0.72f, 0f), new Vector3(0.03f, 0.72f, 0.03f), Dark);
                Part(PrimitiveType.Cylinder, "Base", t, new Vector3(0f, -1.44f, 0f), new Vector3(0.3f, 0.02f, 0.3f), Dark);
                Part(PrimitiveType.Cylinder, "Guard", t, Vector3.zero, new Vector3(0.36f, 0.03f, 0.36f), Clear("FanGuard", new Color(0.2f, 0.2f, 0.2f, 0.45f)), new Vector3(90f, 0f, 0f));
                var blades = Group("Blades", t);
                for (var i = 0; i < 3; i++)
                    Part(PrimitiveType.Cube, "Blade", blades, Quaternion.Euler(0f, 0f, i * 120f) * new Vector3(0f, 0.08f, 0f), new Vector3(0.06f, 0.15f, 0.005f), Dark, new Vector3(0f, 0f, i * 120f));
                Label(t, "Air circulation", new Vector3(0f, 0.25f, 0f), 260f, 26f, Color.white);
                var on = Group("State_on", t);
                var motion = Part(PrimitiveType.Cylinder, "Motion", on, new Vector3(0f, 0f, -0.02f), new Vector3(0.32f, 0.001f, 0.32f), Mist, new Vector3(90f, 0f, 0f));
                motion.AddComponent<Turntable>();
                Label(on, "ON", new Vector3(0f, -0.25f, 0f), 120f, 26f, OnGreen);
                AddBox(t.gameObject, new Vector3(0f, -0.3f, 0f), new Vector3(0.38f, 1.2f, 0.12f));
            });
            Make("Fixtures", "fx.display", t =>
            {
                Part(PrimitiveType.Cube, "Screen", t, Vector3.zero, new Vector3(0.3f, 0.18f, 0.02f), Dark);
                Label(t, "Humidity risk\nHIGH", new Vector3(0f, 0f, -0.012f), 280f, 30f, UIPalette.Warning);
                Label(Group("State_inspected", t), "Checked", new Vector3(0f, -0.13f, -0.012f), 200f, 22f, UIPalette.Cyan);
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.32f, 0.2f, 0.04f));
            });
            Make("Fixtures", "fx.risk_card", t =>
            {
                Part(PrimitiveType.Cube, "Card", t, new Vector3(0f, 0.1f, 0f), new Vector3(0.22f, 0.16f, 0.005f), White, new Vector3(-20f, 0f, 0f));
                Label(t, "Case notes card", new Vector3(0f, 0.1f, -0.012f), 200f, 22f, Color.black, 0f);
                Label(Group("State_inspected", t), "Read", new Vector3(0f, 0.22f, 0f), 160f, 22f, UIPalette.Cyan);
                AddBox(t.gameObject, new Vector3(0f, 0.1f, 0f), new Vector3(0.24f, 0.2f, 0.06f));
            });
            Make("Fixtures", "fx.board", t =>
            {
                Part(PrimitiveType.Cube, "Board", t, new Vector3(0f, 0.18f, 0f), new Vector3(0.36f, 0.26f, 0.015f), Mat("BoardCork", new Color(0.7f, 0.55f, 0.38f)));
                Label(t, "Planning board", new Vector3(0f, 0.25f, -0.01f), 300f, 28f, Color.black);
                Label(Group("State_inspected", t), "Read", new Vector3(0f, 0.36f, 0f), 160f, 22f, UIPalette.Cyan);
                AddBox(t.gameObject, new Vector3(0f, 0.18f, 0f), new Vector3(0.38f, 0.28f, 0.04f));
            });
            Make("Fixtures", "fx.lab_result", t =>
            {
                Part(PrimitiveType.Cube, "Tablet", t, new Vector3(0f, 0.05f, 0f), new Vector3(0.2f, 0.14f, 0.012f), Dark, new Vector3(-25f, 0f, 0f));
                Label(t, "Diagnostic result\n(simulated)", new Vector3(0f, 0.05f, -0.02f), 180f, 18f, Color.white, 0f);
                Label(Group("State_inspected", t), "CONFIRMED", new Vector3(0f, 0.16f, 0f), 200f, 26f, OnGreen);
                AddBox(t.gameObject, new Vector3(0f, 0.05f, 0f), new Vector3(0.22f, 0.16f, 0.06f));
            });
            Make("Fixtures", "fx.neighbour", t =>
            {
                Part(PrimitiveType.Cylinder, "Pot", t, new Vector3(0f, 0.12f, 0f), new Vector3(0.3f, 0.12f, 0.3f), White);
                Part(PrimitiveType.Cylinder, "Soil", t, new Vector3(0f, 0.245f, 0f), new Vector3(0.28f, 0.005f, 0.28f), SoilMat);
                for (var i = 0; i < 7; i++)
                    Part(PrimitiveType.Sphere, "Foliage", t, new Vector3(Mathf.Cos(i) * 0.1f, 0.4f + (i % 3) * 0.1f, Mathf.Sin(i) * 0.1f), Vector3.one * 0.16f, Green);
                Label(t, "Neighbouring host", new Vector3(0f, 0.12f, -0.152f), 240f, 22f, Color.black);
                AddBox(t.gameObject, new Vector3(0f, 0.35f, 0f), new Vector3(0.4f, 0.7f, 0.4f));
                Part(PrimitiveType.Sphere, "Mist", Group("State_covered", t), new Vector3(0f, 0.5f, 0f), new Vector3(0.42f, 0.36f, 0.42f), Mist);
                Label(Group("State_inspected", t), "No lesions found", new Vector3(0f, 0.85f, 0f), 260f, 22f, UIPalette.Cyan);
            });
            Make("Fixtures", "fx.containment", t =>
            {
                Part(PrimitiveType.Cube, "Floor", t, new Vector3(0f, 0.02f, 0f), new Vector3(0.9f, 0.04f, 0.9f), Steel);
                foreach (var (p, s) in new[]
                         {
                             (new Vector3(0f, 0.5f, 0.45f), new Vector3(0.9f, 1f, 0.01f)), (new Vector3(-0.45f, 0.5f, 0f), new Vector3(0.01f, 1f, 0.9f)),
                             (new Vector3(0.45f, 0.6f, 0f), new Vector3(0.03f, 1.2f, 0.9f)), (new Vector3(0f, 1f, 0f), new Vector3(0.9f, 0.01f, 0.9f))
                         })
                    Part(PrimitiveType.Cube, "Wall", t, p, s, glass);
                Part(PrimitiveType.Cube, "Sign", t, new Vector3(0f, 0.12f, -0.46f), new Vector3(0.7f, 0.16f, 0.01f), Yellow);
                Label(t, "Infected plant \u2022 contained", new Vector3(0f, 0.12f, -0.468f), 640f, 40f, Color.black);
            });
            Make("Fixtures", "fx.clean_pot", t =>
            {
                Part(PrimitiveType.Cylinder, "Pot", t, new Vector3(0f, 0.1f, 0f), new Vector3(0.26f, 0.1f, 0.26f), White);
                Part(PrimitiveType.Cylinder, "Soil", t, new Vector3(0f, 0.205f, 0f), new Vector3(0.24f, 0.005f, 0.24f), SoilMat);
                Label(t, "Clean planting position", new Vector3(0f, 0.1f, -0.132f), 280f, 22f, Color.black);
                AddBox(t.gameObject, new Vector3(0f, 0.12f, 0f), new Vector3(0.3f, 0.26f, 0.3f));
            });
            Make("Fixtures", "fx.mesh_frame", t =>
            {
                foreach (var x in new[] { -0.3f, 0.3f })
                foreach (var z in new[] { -0.3f, 0.3f })
                    Part(PrimitiveType.Cube, "Post", t, new Vector3(x, 0.35f, z), new Vector3(0.02f, 0.7f, 0.02f), Steel);
                var placed = Group("State_placed", t);
                Part(PrimitiveType.Cube, "Mesh", placed, new Vector3(0f, 0.35f, 0f), new Vector3(0.62f, 0.7f, 0.62f), insectMesh);
                Part(PrimitiveType.Cube, "Card", placed, new Vector3(0f, 0.6f, -0.32f), new Vector3(0.06f, 0.1f, 0.003f), Yellow);
                AddBox(t.gameObject, new Vector3(0f, 0.35f, 0f), new Vector3(0.64f, 0.72f, 0.64f));
            });
            Make("Fixtures", "fx.bag_overlay", t => Part(PrimitiveType.Sphere, "Bag", t, Vector3.zero, Vector3.one, plastic));
        }

        static void BuildSpecimens()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            if (catalog == null)
                throw new InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first.");
            // The life-size Farm models are the visual base; treatment targets are separate objects.
            foreach (var plant in catalog.Plants)
                Bind("specimen." + plant.PlantId, plant.FarmPrefab);
        }
    }
}