using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTP.Lab;
using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using BTP.XR;
using NUnit.Framework;
using SQLite;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Tests
{
    /// <summary>
    /// The ten Lab-only crops (34 conditions): each case resolves to one Lab leaf, one Treatment specimen with anchors
    /// and one complete SQLite case (guided and manual validators), while the Farm and CropScan XR keep the original 24.
    /// </summary>
    public sealed class LabOnlyCropTests
    {
        const string SqlPath = "Assets/_Project/Data/Treatment/treatment_content.sql";
        const string AssetCatalogPath = "Assets/_Project/Data/Treatment/TreatmentAssetCatalog.asset";
        const string LabScene = "Assets/Scenes/Lab.unity";
        const string TreatmentScene = "Assets/Scenes/Treatment.unity";

        SqliteTreatmentRepository repository;

        [SetUp]
        public void Open()
        {
            var sql = File.ReadAllText(SqlPath);
            repository = new SqliteTreatmentRepository(() =>
            {
                var db = new SQLiteConnection(":memory:");
                db.ExecuteScript(sql);
                return db;
            });
            repository.InitializeAsync().Wait();
            Assert.IsTrue(repository.IsReady, repository.Error);
        }

        [TearDown]
        public void Close() => repository.Dispose();

        // ------------------------------------------------------------------ identity

        [Test]
        public void ThirtyFourLabOnlyConditionsOnTenCrops_WithUniqueLowercaseKeys()
        {
            Assert.AreEqual(24, DiseaseClasses.All.Count, "the Farm classes are unchanged");
            Assert.AreEqual(34, DiseaseClasses.LabOnly.Count);
            Assert.AreEqual(58, DiseaseClasses.AllCases.Count);
            Assert.AreEqual(10, DiseaseClasses.LabOnlyPlantIds.Length);
            var keys = DiseaseClasses.AllCases.Select(i => i.PlantId + "/" + i.DiseaseId).ToList();
            CollectionAssert.AllItemsAreUnique(keys);
            foreach (var key in keys)
                StringAssert.IsMatch("^[a-z_]+/[a-z_]+$", key);
            CollectionAssert.AreEqual(DiseaseClasses.All.Select(i => i.PlantId + "/" + i.DiseaseId), keys.Take(24), "the original 24 keep their order");
            foreach (var plant in DiseaseClasses.LabOnlyPlantIds)
            {
                Assert.IsTrue(DiseaseClasses.IsLabOnlyPlant(plant));
                var list = DiseaseClasses.For(plant);
                Assert.IsNotEmpty(list, plant);
                Assert.IsTrue(list.Any(i => i.DiseaseId == DiseaseClasses.DefaultDiseaseId(plant)), plant + " default condition");
                Assert.IsFalse(list.Any(i => DiseaseClasses.IsPlantVillageCase(i.PlantId, i.DiseaseId)), plant + " must not be a PlantVillage case");
            }

            Assert.AreEqual(9, DiseaseClasses.For("soybean").Count, "all nine soybean conditions are listed");
            Assert.AreEqual("early_blight", DiseaseClasses.DefaultDiseaseId("tomato"), "Tomato / Early blight stays the default");
        }

        [Test]
        public void ConnectedXrMapStaysOnTheTwentyFourFarmClasses()
        {
            var map = XrCaseMap.LoadDefault(out var problems);
            CollectionAssert.IsEmpty(problems);
            Assert.IsTrue(map.Rows.All(r => DiseaseClasses.IsPlantVillageCase(r.plantId, r.diseaseId)));
            Assert.IsFalse(map.Rows.Any(r => DiseaseClasses.IsLabOnlyPlant(r.plantId)));
        }

        // ------------------------------------------------------------------ catalogs

        [Test]
        public void PlantCatalog_ListsTheTenFieldAndTropicalCropsWithLeafPreviewsAndFarmModels()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>("Assets/_Project/Data/PlantCatalog.asset");
            Assert.IsNotNull(catalog);
            CollectionAssert.IsEmpty(catalog.Validate());
            Assert.AreEqual(9, catalog.Plants.Count, "the Farm list is unchanged");
            Assert.AreEqual(10, catalog.LabOnlyPlants.Count);
            Assert.AreEqual(19, catalog.AllPlants.Count);
            CollectionAssert.AreEqual(DiseaseClasses.LabOnlyPlantIds, catalog.LabOnlyPlants.Select(p => p.PlantId));
            foreach (var plant in catalog.LabOnlyPlants)
            {
                Assert.IsNotNull(plant.FarmPrefab, plant.PlantId + " Farm model (BTP/Setup/27 - Build Farm Crops)");
                Assert.IsNotNull(plant.LabPrefab, plant.PlantId);
                Assert.IsTrue(plant.HasRecoveryDemo, plant.PlantId);
                Assert.IsFalse(string.IsNullOrEmpty(plant.ScientificName), plant.PlantId);
            }
        }

        [Test]
        public void EveryLabOnlyCase_HasOneLabLeafAndOneTreatmentSpecimenWithAnchors()
        {
            var leaves = AssetDatabase.LoadAssetAtPath<LeafCatalog>("Assets/_Project/Data/LeafCatalog.asset");
            var assets = AssetDatabase.LoadAssetAtPath<TreatmentAssetCatalog>(AssetCatalogPath);
            Assert.IsNotNull(leaves);
            Assert.IsNotNull(assets);
            var prefabs = new HashSet<GameObject>();
            foreach (var info in DiseaseClasses.LabOnly)
            {
                var key = info.PlantId + "/" + info.DiseaseId;
                Assert.IsTrue(leaves.TryGet(info.PlantId, info.DiseaseId, out var leaf), key);
                Assert.IsTrue(prefabs.Add(leaf), key + " must use its own leaf");
                var specimen = assets.Resolve<GameObject>($"leafspecimen.{info.PlantId}.{info.DiseaseId}");
                Assert.IsNotNull(specimen, key + " specimen binding");
                var anchors = specimen.GetComponent<LeafAnchors>();
                Assert.IsNotNull(anchors, key);
                // Each lesson target gets its own point: three lesion and two healthy targets per leaf.
                Assert.GreaterOrEqual(anchors.Lesions.Count, 3, key + " lesion anchors");
                Assert.GreaterOrEqual(anchors.Healthy.Count, 2, key + " healthy anchors");
                // Anchors sit on the actual mesh: inside the specimen's leaf bounds.
                var bounds = anchors.LeafBounds;
                bounds.Expand(0.02f);
                foreach (var point in anchors.Lesions.Concat(anchors.Healthy))
                    Assert.IsTrue(bounds.Contains(point.position), key + " anchor off the leaf");
                Assert.That(anchors.LeafBounds.size.y, Is.EqualTo(0.7f).Within(0.02f), key + " specimen height");
                Assert.IsNotNull(assets.Resolve<GameObject>($"target.leaf.{info.PlantId}.{info.DiseaseId}"), key + " leaf target");
                Assert.IsNotNull(assets.Resolve<Material>($"leafmat.{info.PlantId}.{info.DiseaseId}"), key + " leaf material");
                Assert.IsNotNull(assets.Resolve<Material>($"leafmat.healthy.{info.PlantId}"), key + " healthy material");
            }
        }

        // ------------------------------------------------------------------ SQLite content

        [Test]
        public void EveryLabOnlyCase_IsACompleteValidSqliteCase_GuidedAndManual()
        {
            var assets = AssetDatabase.LoadAssetAtPath<TreatmentAssetCatalog>(AssetCatalogPath);
            Assert.IsNotNull(assets);
            Assert.AreEqual(58, repository.ListCases().Count);
            foreach (var info in DiseaseClasses.LabOnly)
            {
                var c = repository.GetCase(new CaseKey(info.PlantId, info.DiseaseId), out var error);
                Assert.IsNotNull(c, error);
                Assert.AreEqual($"leafspecimen.{info.PlantId}.{info.DiseaseId}", c.SpecimenAsset);
                var guided = TreatmentCaseValidator.Validate(c, k => repository.GetTool(k) != null, assets.Has);
                CollectionAssert.IsEmpty(guided, $"{c.Key}: {string.Join("; ", guided)}");
                var manual = TreatmentCaseValidator.ValidateManual(c);
                CollectionAssert.IsEmpty(manual, $"{c.Key}: {string.Join("; ", manual)}");
                Assert.IsTrue(c.Evidence.All(e => !string.IsNullOrEmpty(e.Url) && e.Url.StartsWith("https://")), c.Key.ToString());
            }
        }

        [Test]
        public void IncurableConditions_AreTaughtAsReplacementNotCure()
        {
            foreach (var (plant, disease) in new[] { ("orange", "huanglongbing"), ("cotton", "leaf_curl_virus"), ("chilli", "leaf_curl"),
                         ("rice", "tungro"), ("soybean", "mosaic_virus"), ("sugarcane", "mosaic"), ("sugarcane", "yellow_leaf"),
                         ("sugarcane", "red_rot"), ("cotton", "fusarium_wilt"), ("soybean", "sudden_death_syndrome") })
            {
                var c = repository.GetCase(new CaseKey(plant, disease), out var error);
                Assert.IsNotNull(c, error);
                Assert.AreEqual(TreatmentOutcome.Replaced, c.Outcome, c.Key.ToString());
                Assert.AreEqual(0f, c.Manual.MaxHealthGain, c.Key + " treatment must not improve an incurable leaf");
            }
        }

        // ------------------------------------------------------------------ selectors

        [Test]
        public void Pager_PutsEachItemOnItsPage()
        {
            Assert.AreEqual(0, PlantButtonPager.PageOf(0, 10));
            Assert.AreEqual(0, PlantButtonPager.PageOf(9, 10));
            Assert.AreEqual(1, PlantButtonPager.PageOf(10, 10));
            Assert.AreEqual(2, PlantButtonPager.PageOf(18, 8));
            Assert.AreEqual(0, PlantButtonPager.PageOf(-1, 8));
        }

        [Test]
        public void LabAndTreatmentSelectors_ListAll19Crops()
        {
            WithScene(LabScene, scene =>
            {
                var panel = Find(scene, "PlantPanel");
                foreach (var id in DiseaseClasses.LabOnlyPlantIds.Concat(new[] { "tomato", "apple" }))
                    Assert.IsNotNull(panel.Find("Plant_" + id), "Lab button " + id);
                Assert.AreEqual(19, panel.GetComponentsInChildren<PlantButton>(true).Length);
                Assert.IsNotNull(panel.GetComponentInChildren<PlantButtonPager>(true));
            });
            WithScene(TreatmentScene, scene =>
            {
                var selector = Find(scene, "CaseSelectorPanel").GetComponent<TreatmentCaseSelector>();
                var serialized = new SerializedObject(selector);
                Assert.AreEqual(19, serialized.FindProperty("plantIds").arraySize);
                Assert.GreaterOrEqual(serialized.FindProperty("diseaseButtons").arraySize, 9, "Soybean's ninth condition needs a ninth button");
                Assert.IsNotNull(selector.GetComponentInChildren<PlantButtonPager>(true));
            });
        }

        static void WithScene(string path, System.Action<Scene> check)
        {
            var scene = SceneManager.GetSceneByPath(path);
            var opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try { check(scene); }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static Transform Find(Scene scene, string name) =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).First(t => t.name == name);
    }
}
