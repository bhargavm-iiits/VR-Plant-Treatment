using System;
using BTP.Treatment;
using UnityEngine;

namespace BTP.Editor
{
    public static partial class TreatmentAssetBuilder
    {
        static Material Wood => Mat("Wood", new Color(0.55f, 0.38f, 0.22f), 0.15f);
        static Material Dark => Mat("ToolDark", new Color(0.12f, 0.12f, 0.13f), 0.3f);
        static Material Green => Mat("PlantGreen", new Color(0.24f, 0.55f, 0.2f), 0.25f);
        static Material Straw => Mat("Straw", new Color(0.78f, 0.66f, 0.38f), 0.05f);
        static Material SoilMat => Mat("Soil", new Color(0.3f, 0.2f, 0.12f), 0.05f);
        static Material White => Mat("PotWhite", new Color(0.92f, 0.93f, 0.94f));
        static Material Yellow => Mat("CardYellow", new Color(0.98f, 0.82f, 0.12f), 0.2f);
        static Material Cardboard => Mat("Cardboard", new Color(0.66f, 0.5f, 0.32f), 0.05f);
        static Material Steel => Mat("FixtureSteel", new Color(0.78f, 0.8f, 0.82f), 0.5f);
        static Material Mist => Clear("Mist", new Color(0.75f, 0.92f, 1f, 0.32f));

        static GameObject Make(string folder, string key, Action<Transform> build)
        {
            var root = Root(key);
            build(root.transform);
            return Save(root, folder, key);
        }

        // ------------------------------------------------------------------ tools (about 0.2 m, resting on the rack)

        static void BuildTools()
        {
            var metal = Mat("ToolMetal", new Color(0.7f, 0.72f, 0.75f), 0.6f);
            var grey = Mat("ToolGrey", new Color(0.55f, 0.58f, 0.6f), 0.3f);
            var red = Mat("ToolRed", new Color(0.75f, 0.12f, 0.1f), 0.4f);
            var glass = Clear("Glass", new Color(0.8f, 0.92f, 1f, 0.28f));
            var plastic = Clear("ClearPlastic", new Color(0.92f, 0.95f, 1f, 0.35f));

            Make("Tools", "tool.hand", t =>
            {
                if (!GlbPart(t, GlovesGlb, null, 0.18f, Vector3.zero))
                    Part(PrimitiveType.Capsule, "Glove", t, new Vector3(0f, 0.05f, 0f), new Vector3(0.08f, 0.06f, 0.05f), grey);
            });
            Make("Tools", "tool.magnifier", t =>
            {
                var ring = new GameObject("Rim", typeof(MeshFilter), typeof(MeshRenderer));
                ring.transform.SetParent(t, false);
                ring.transform.localPosition = new Vector3(0f, 0.13f, 0f);
                ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                ring.transform.localScale = Vector3.one * 0.06f;
                ring.GetComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Ring("MagnifierRim", 0.85f, 1f);
                ring.GetComponent<MeshRenderer>().sharedMaterial = Dark;
                Part(PrimitiveType.Cylinder, "Lens", t, new Vector3(0f, 0.13f, 0f), new Vector3(0.105f, 0.002f, 0.105f), glass, new Vector3(90f, 0f, 0f));
                Part(PrimitiveType.Cylinder, "Handle", t, new Vector3(0f, 0.04f, 0f), new Vector3(0.022f, 0.04f, 0.022f), Dark);
            });
            Make("Tools", "tool.shears", t =>
            {
                if (!GlbPart(t, ToolsGlb, "garden secateurs", 0.21f, Vector3.zero))
                {
                    Part(PrimitiveType.Cube, "Blade", t, new Vector3(0f, 0.15f, 0f), new Vector3(0.012f, 0.07f, 0.004f), metal, new Vector3(0f, 0f, 10f));
                    Part(PrimitiveType.Cube, "Handle", t, new Vector3(0f, 0.06f, 0f), new Vector3(0.02f, 0.1f, 0.012f), red);
                }
            });
            Make("Tools", "tool.sprayer", t =>
            {
                Part(PrimitiveType.Cylinder, "Bottle", t, new Vector3(0f, 0.07f, 0f), new Vector3(0.07f, 0.07f, 0.07f), grey);
                Part(PrimitiveType.Cube, "Head", t, new Vector3(0f, 0.16f, 0.015f), new Vector3(0.035f, 0.04f, 0.06f), Dark);
                Part(PrimitiveType.Cube, "Trigger", t, new Vector3(0f, 0.135f, 0.04f), new Vector3(0.012f, 0.035f, 0.01f), Dark);
                Label(t, "Training only", new Vector3(0f, 0.07f, -0.037f), 120f, 14f, Color.black, 0f);
            });
            Make("Tools", "tool.drip", t =>
            {
                Part(PrimitiveType.Cylinder, "Coil", t, new Vector3(0f, 0.02f, 0f), new Vector3(0.14f, 0.02f, 0.14f), Dark);
                Part(PrimitiveType.Cylinder, "Emitter", t, new Vector3(0.05f, 0.08f, 0f), new Vector3(0.012f, 0.05f, 0.012f), Dark, new Vector3(0f, 0f, 30f));
            });
            Make("Tools", "tool.wand", t =>
            {
                Part(PrimitiveType.Cylinder, "Shaft", t, new Vector3(0f, 0.12f, 0f), new Vector3(0.015f, 0.11f, 0.015f), Mat("WandGreen", new Color(0.1f, 0.45f, 0.2f)));
                Part(PrimitiveType.Cylinder, "Rose", t, new Vector3(0f, 0.235f, 0f), new Vector3(0.04f, 0.01f, 0.04f), metal);
            });
            Make("Tools", "tool.stake", t => Part(PrimitiveType.Cube, "Stake", t, new Vector3(0f, 0.12f, 0f), new Vector3(0.018f, 0.24f, 0.018f), Wood));
            Make("Tools", "tool.mulch", t => Part(PrimitiveType.Cube, "Bag", t, new Vector3(0f, 0.06f, 0f), new Vector3(0.14f, 0.12f, 0.07f), Straw));
            Make("Tools", "tool.rake", t =>
            {
                if (!GlbPart(t, ToolsGlb, "rake", 0.24f, Vector3.zero))
                    Part(PrimitiveType.Cylinder, "Shaft", t, new Vector3(0f, 0.12f, 0f), new Vector3(0.012f, 0.12f, 0.012f), Wood);
            });
            Make("Tools", "tool.tie", t => Part(PrimitiveType.Cylinder, "Spool", t, new Vector3(0f, 0.03f, 0f), new Vector3(0.06f, 0.03f, 0.06f), Green));
            Make("Tools", "tool.predator", t =>
            {
                Part(PrimitiveType.Cylinder, "Tube", t, new Vector3(0f, 0.07f, 0f), new Vector3(0.05f, 0.07f, 0.05f), Cardboard);
                Label(t, "Predatory mites", new Vector3(0f, 0.07f, -0.027f), 140f, 13f, Color.black, 0f);
            });
            Make("Tools", "tool.bag", t => Part(PrimitiveType.Cube, "FoldedBag", t, new Vector3(0f, 0.03f, 0f), new Vector3(0.16f, 0.05f, 0.11f), plastic));
            Make("Tools", "tool.spade", t =>
            {
                if (!GlbPart(t, ToolsGlb, "garden shovel", 0.26f, Vector3.zero))
                    Part(PrimitiveType.Cylinder, "Shaft", t, new Vector3(0f, 0.12f, 0f), new Vector3(0.015f, 0.12f, 0.015f), Wood);
            });
            Make("Tools", "tool.transplant", t =>
            {
                Part(PrimitiveType.Cylinder, "Pot", t, new Vector3(0f, 0.035f, 0f), new Vector3(0.07f, 0.035f, 0.07f), White);
                Part(PrimitiveType.Cylinder, "Stem", t, new Vector3(0f, 0.1f, 0f), new Vector3(0.006f, 0.04f, 0.006f), Green);
                for (var i = 0; i < 4; i++)
                    Part(PrimitiveType.Sphere, "Leaf", t, new Vector3(Mathf.Cos(i * 1.57f) * 0.025f, 0.12f + i * 0.01f, Mathf.Sin(i * 1.57f) * 0.025f),
                        new Vector3(0.04f, 0.008f, 0.025f), Green, new Vector3(0f, i * 90f, 15f));
            });
            Make("Tools", "tool.mesh", t => Part(PrimitiveType.Cylinder, "Roll", t, new Vector3(0f, 0.03f, 0f), new Vector3(0.05f, 0.09f, 0.05f),
                Mat("MeshWhite", new Color(0.9f, 0.9f, 0.88f)), new Vector3(0f, 0f, 90f)));
            Make("Tools", "tool.card", t => Part(PrimitiveType.Cube, "Card", t, new Vector3(0f, 0.08f, 0f), new Vector3(0.07f, 0.12f, 0.004f), Yellow));
        }

        static void Covered(Transform t, Vector3 position, Vector3 scale)
        {
            var state = Group("State_covered", t);
            Part(PrimitiveType.Sphere, "Mist", state, position, scale, Mist);
        }
    }
}