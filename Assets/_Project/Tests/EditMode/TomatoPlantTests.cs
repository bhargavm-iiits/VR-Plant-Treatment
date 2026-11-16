using System.Linq;
using BTP.Plants;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BTP.Tests
{
    /// <summary>
    /// The Blender tomato plants (TomatoPlantBuilder): one Farm prefab per tomato disease, and the scenes' tomato
    /// (Tomato_Farm / Tomato_Lab, rebuilt in place) showing the default disease, early blight, at real size.
    /// Skipped until the tomatoes have been exported and applied.
    /// </summary>
    public class TomatoPlantTests
    {
        const string Prefabs = "Assets/_Project/Prefabs/Plants";
        const string FarmPrefab = Prefabs + "/Tomato_Farm.prefab";
        const string LabPrefab = Prefabs + "/Tomato_Lab.prefab";

        static GameObject LoadVariant(string diseaseId)
        {
            var path = $"{Prefabs}/Tomato/Tomato_{diseaseId}_Farm.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Assert.Ignore($"{path} not built yet (run BTP/Setup/Replace Tomato With Blender Tomato Plants (Wind)).");
            return prefab;
        }

        static Material LeafMaterial(GameObject prefab) =>
            prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .FirstOrDefault(m => m != null && m.name.StartsWith("TomatoLeaf_"));

        [Test]
        public void TomatoDiseaseIds_MatchDiseaseClasses()
        {
            var ids = DiseaseClasses.For("tomato").Select(d => d.DiseaseId).OrderBy(d => d);
            Assert.That(LeafWindTests.TomatoDiseases.OrderBy(d => d), Is.EqualTo(ids));
            Assert.That(DiseaseClasses.DefaultDiseaseId("tomato"), Is.EqualTo("early_blight"));
        }

        [Test]
        public void EveryTomatoDisease_HasAFarmPrefab_WithItsOwnLeaf_ThreeLods()
        {
            foreach (var id in LeafWindTests.TomatoDiseases)
            {
                var prefab = LoadVariant(id);
                Assert.That(LeafMaterial(prefab)?.name, Is.EqualTo($"TomatoLeaf_{id}"), id);
                var lods = prefab.GetComponent<LODGroup>();
                Assert.That(lods, Is.Not.Null, $"{id} LODGroup");
                var levels = lods.GetLODs();
                Assert.That(levels.Length, Is.EqualTo(3), $"{id}: mesh, far mesh, billboard");
                Assert.That(levels[2].renderers.Single().name, Is.EqualTo("Billboard"), id);
            }
        }

        [Test]
        public void TomatoFarm_ShowsEarlyBlight_AtRealSize()
        {
            var variant = LoadVariant("early_blight");
            var farm = AssetDatabase.LoadAssetAtPath<GameObject>(FarmPrefab);
            Assert.That(farm, Is.Not.Null, FarmPrefab);
            var leaf = LeafMaterial(farm);
            Assert.That(leaf, Is.Not.Null, "Tomato_Farm.prefab has no TomatoLeaf_ material (tomato not applied?)");
            Assert.That(leaf.name, Is.EqualTo("TomatoLeaf_early_blight"));
            Assert.That(leaf, Is.SameAs(LeafMaterial(variant)));
            Assert.That(leaf.IsKeywordEnabled("_LEAF_WIND"), Is.True);

            // Native size (no rescale): about 1.75 m tall, standing on the prefab origin.
            var near = farm.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "Model");
            var bounds = near.sharedMesh.bounds;
            var height = bounds.size.y * near.transform.localScale.y;
            Assert.That(height, Is.InRange(1.6f, 1.9f), "near mesh height (m)");
            var bottom = near.transform.localPosition.y + (bounds.min.y * near.transform.localScale.y);
            Assert.That(bottom, Is.EqualTo(0f).Within(0.02f), "plant base on the ground");

            var lab = AssetDatabase.LoadAssetAtPath<GameObject>(LabPrefab);
            Assert.That(lab, Is.Not.Null, LabPrefab);
            Assert.That(LeafMaterial(lab), Is.SameAs(leaf), "Tomato_Lab.prefab shows the same plant");
        }
    }
}
