using System.Collections.Generic;
using System.Linq;
using BTP.Farm;
using BTP.Plants;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BTP.Tests
{
    /// <summary>
    /// The Farm disease rows: one Farm prefab per PlantVillage disease class (DiseaseVariantCatalog, built by
    /// BTP/Setup/26 - Build Farm Disease Variants) and, in the Farm scene, exactly one row per disease whose plants
    /// all select that plant and that disease.
    /// </summary>
    public class FarmDiseaseRowTests
    {
        const string VariantCatalogPath = "Assets/_Project/Data/DiseaseVariantCatalog.asset";
        const string FarmScenePath = "Assets/Scenes/Envirornment.unity";
        const string FarmRecoveryShader = "BTP/Farm Recovery";

        static DiseaseVariantCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DiseaseVariantCatalog>(VariantCatalogPath);
            if (catalog == null)
                Assert.Ignore($"{VariantCatalogPath} not built yet (run BTP/Setup/26 - Build Farm Disease Variants).");
            return catalog;
        }

        static List<Material> Materials(GameObject prefab) =>
            prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();

        [Test]
        public void EveryDiseaseClass_ResolvesToAFarmPrefab_WithLodsAndTimelineLeaves()
        {
            var catalog = LoadCatalog();
            Assert.That(catalog.Entries.Count, Is.EqualTo(58));
            Assert.That(catalog.Entries.Select(e => e.plantId + "/" + e.diseaseId).Distinct().Count(), Is.EqualTo(58), "one entry per case");

            foreach (var info in DiseaseClasses.AllCases)
            {
                var name = $"{info.PlantId}/{info.DiseaseId}";
                Assert.That(catalog.TryGet(info.PlantId, info.DiseaseId, out var entry), Is.True, name);
                Assert.That(entry.farmPrefab, Is.Not.Null, name);
                var lods = entry.farmPrefab.GetComponent<LODGroup>();
                Assert.That(lods, Is.Not.Null, $"{name} LODGroup");
                Assert.That(lods.GetLODs().Length, Is.GreaterThanOrEqualTo(2), $"{name}: near mesh and a lighter far level");

                // The disease's leaf material is on the prefab and is driven by the six-week timeline.
                Assert.That(entry.leafMaterial, Is.Not.Null, name);
                Assert.That(entry.leafMaterial.shader.name, Is.EqualTo(FarmRecoveryShader), name);
                Assert.That(entry.leafMaterial.GetTexture("_DiseasedMap"), Is.Not.Null, $"{name} diseased albedo");
                Assert.That(entry.leafMaterial.GetTexture("_BaseMap"), Is.Not.Null, $"{name} healthy albedo");
                Assert.That(Materials(entry.farmPrefab), Has.Member(entry.leafMaterial), $"{name} prefab shows its leaf material");
            }
        }

        [Test]
        public void MultiDiseasePlants_ShowOnlyTheirOwnDisease()
        {
            var catalog = LoadCatalog();
            foreach (var plantId in new[] { "apple", "grape", "corn", "potato", "tomato", "chilli", "cotton", "mango", "rice", "soybean", "sugarcane", "wheat" })
            {
                var entries = catalog.Entries.Where(e => e.plantId == plantId).ToList();
                Assert.That(entries.Count, Is.EqualTo(DiseaseClasses.For(plantId).Count), plantId);
                Assert.That(entries.Select(e => e.leafMaterial).Distinct().Count(), Is.EqualTo(entries.Count), $"{plantId}: one leaf material per disease");
                Assert.That(entries.Select(e => e.leafMaterial.GetTexture("_DiseasedMap")).Distinct().Count(), Is.EqualTo(entries.Count),
                    $"{plantId}: one diseased albedo per disease");
                Assert.That(entries.Select(e => e.farmPrefab).Distinct().Count(), Is.EqualTo(entries.Count), $"{plantId}: one prefab per disease");
                foreach (var entry in entries)
                {
                    var materials = Materials(entry.farmPrefab);
                    foreach (var other in entries.Where(o => o != entry))
                        Assert.That(materials, Has.No.Member(other.leafMaterial), $"{plantId}/{entry.diseaseId} also shows {other.diseaseId}");
                }
            }

            // Tomato rows use the Blender disease plants, each with its own supplied leaf.
            foreach (var entry in catalog.Entries.Where(e => e.plantId == "tomato"))
                Assert.That(entry.leafMaterial.name, Is.EqualTo($"TomatoLeaf_{entry.diseaseId}"));
        }

        [Test]
        public void FarmScene_HasOneRowPerDisease_WhosePlantsSelectThatDisease()
        {
            var catalog = LoadCatalog();
            var scene = SceneManager.GetSceneByPath(FarmScenePath);
            var opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(FarmScenePath, OpenSceneMode.Additive);
            try
            {
                var rows = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SelectablePlant>(true)).Where(s => s.IsRow).ToList();
                var groups = rows.GroupBy(r => r.PlantId + "/" + r.DiseaseId).ToList();
                Assert.That(groups.Count, Is.EqualTo(58), "one row per case");
                Assert.That(groups.All(g => g.Count() == 1), Is.True, "no disease has two rows");
                foreach (var info in DiseaseClasses.AllCases)
                    Assert.That(groups.Any(g => g.Key == info.PlantId + "/" + info.DiseaseId), Is.True, $"row for {info.PlantId}/{info.DiseaseId}");

                var footprints = new List<(SelectablePlant row, Bounds bounds)>();
                foreach (var row in rows)
                {
                    var name = row.PlantId + "/" + row.DiseaseId;
                    Assert.That(row.GetComponent<XRSimpleInteractable>(), Is.Not.Null, name);
                    Assert.That(row.transform.Find("NameSign"), Is.Not.Null, $"{name} label");
                    Assert.That(row.Plants.Length, Is.GreaterThanOrEqualTo(2), $"{name} plants");
                    Assert.That(catalog.TryGet(row.PlantId, row.DiseaseId, out var entry), Is.True, name);
                    foreach (var plant in row.Plants)
                    {
                        Assert.That(plant, Is.Not.Null, name);
                        var collider = plant.GetComponent<Collider>();
                        Assert.That(collider, Is.Not.Null, $"{name} {plant.name} collider");
                        // Pointing at this plant reaches this row's selectable, with the row's plant and disease.
                        var selectable = collider.GetComponentInParent<SelectablePlant>();
                        Assert.That(selectable, Is.SameAs(row), $"{name} {plant.name}");
                        Assert.That(selectable.PlantId, Is.EqualTo(row.PlantId));
                        Assert.That(selectable.DiseaseId, Is.EqualTo(row.DiseaseId));
                        var model = plant.GetComponentsInChildren<LODGroup>(true).Select(l => PrefabUtility.GetCorrespondingObjectFromSource(l.gameObject)).FirstOrDefault();
                        Assert.That(model, Is.SameAs(entry.farmPrefab), $"{name} {plant.name} shows the disease's Farm prefab");
                        Assert.That(plant.position.y, Is.InRange(-0.01f, 0.3f), $"{name} {plant.name} stands on the ground or its bed");
                        footprints.Add((row, collider.bounds));
                    }
                }

                // No plant's selection volume overlaps a plant of another row (ground plane).
                for (var i = 0; i < footprints.Count; i++)
                {
                    for (var j = i + 1; j < footprints.Count; j++)
                    {
                        if (footprints[i].row == footprints[j].row)
                            continue;
                        Bounds a = footprints[i].bounds, b = footprints[j].bounds;
                        var overlap = a.min.x < b.max.x && b.min.x < a.max.x && a.min.z < b.max.z && b.min.z < a.max.z;
                        Assert.That(overlap, Is.False, $"{footprints[i].row.name} overlaps {footprints[j].row.name}");
                    }
                }
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RowSelection_MatchesOnlyItsOwnDisease_SpecimenMatchesAnyDiseaseOfItsPlant()
        {
            var row = new GameObject("Row", typeof(XRSimpleInteractable), typeof(SelectablePlant)).GetComponent<SelectablePlant>();
            var specimen = new GameObject("Specimen", typeof(XRSimpleInteractable), typeof(SelectablePlant)).GetComponent<SelectablePlant>();
            try
            {
                row.Bind("corn", null, null);
                row.BindRow("common_rust", new[] { row.transform, new GameObject("Plant").transform });
                Assert.That(row.IsRow, Is.True);
                Assert.That(row.Matches("corn", "common_rust"), Is.True);
                Assert.That(row.Matches("corn", "gray_leaf_spot"), Is.False);
                Assert.That(row.Matches("potato", "common_rust"), Is.False);
                Assert.That(row.Matches(null, null), Is.False);

                specimen.Bind("corn", null, null);
                Assert.That(specimen.IsRow, Is.False);
                Assert.That(specimen.Matches("corn", "gray_leaf_spot"), Is.True);
                Assert.That(specimen.Matches("potato", "early_blight"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(row.Plants[1].gameObject);
                Object.DestroyImmediate(row.gameObject);
                Object.DestroyImmediate(specimen.gameObject);
            }
        }

        [Test]
        public void RowLabels_ShowWithinTheirViewDistance_OnTheGround()
        {
            Assert.That(BTP.UI.CanvasDistanceCull.IsNear(new Vector3(0f, 1.6f, 0f), new Vector3(0f, 1.0f, 11.9f), 12f), Is.True);
            Assert.That(BTP.UI.CanvasDistanceCull.IsNear(new Vector3(0f, 1.6f, 0f), new Vector3(9f, 1.0f, 9f), 12f), Is.False);
            // Height does not count: a label below a standing viewer is as near as its ground distance.
            Assert.That(BTP.UI.CanvasDistanceCull.IsNear(new Vector3(0f, 30f, 0f), new Vector3(0f, 1.0f, 5f), 12f), Is.True);
        }
    }
}
