using BTP.Farm;
using BTP.Lab;
using BTP.Plants;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BTP.Tests
{
    public class FarmTimelineTests
    {
        const string CatalogPath = "Assets/_Project/Data/PlantCatalog.asset";
        const string FarmRecoveryShader = "BTP/Farm Recovery";

        TomatoRecoveryData data;

        [SetUp]
        public void SetUp()
        {
            FarmRecoveryState.ClearAll();
            data = ScriptableObject.CreateInstance<TomatoRecoveryData>();
        }

        [TearDown]
        public void TearDown()
        {
            FarmRecoveryState.ClearAll();
            Object.DestroyImmediate(data);
        }

        // ---- per-crop week state

        [Test]
        public void State_EveryCropStartsAtWeekOne()
        {
            Assert.That(FarmRecoveryState.GetWeek("tomato"), Is.EqualTo(1));
            Assert.That(FarmRecoveryState.GetWeek("apple"), Is.EqualTo(1));
            Assert.That(FarmRecoveryState.GetWeek(null), Is.EqualTo(1));
        }

        [Test]
        public void State_CropsAreIndependent()
        {
            FarmRecoveryState.SetWeek("tomato", 6);
            FarmRecoveryState.SetWeek("apple", 3);
            Assert.That(FarmRecoveryState.GetWeek("tomato"), Is.EqualTo(6));
            Assert.That(FarmRecoveryState.GetWeek("apple"), Is.EqualTo(3));
            Assert.That(FarmRecoveryState.GetWeek("corn"), Is.EqualTo(1));
        }

        [Test]
        public void State_ResetReturnsOnlyThatCropToWeekOne()
        {
            FarmRecoveryState.SetWeek("tomato", 5);
            FarmRecoveryState.SetWeek("grape", 4);
            FarmRecoveryState.Reset("tomato");
            Assert.That(FarmRecoveryState.GetWeek("tomato"), Is.EqualTo(1));
            Assert.That(FarmRecoveryState.GetWeek("grape"), Is.EqualTo(4));
        }

        [Test]
        public void State_PersistsAndClampsAndRaisesOncePerChange()
        {
            var changes = 0;
            void Count(string id, int week) => changes++;
            FarmRecoveryState.WeekChanged += Count;
            try
            {
                FarmRecoveryState.SetWeek("peach", 4);
                FarmRecoveryState.SetWeek("peach", 4);
                Assert.That(FarmRecoveryState.SetWeek("peach", 99), Is.EqualTo(6));
                Assert.That(FarmRecoveryState.Step("peach", 1), Is.EqualTo(6));
                Assert.That(FarmRecoveryState.Step("peach", -10), Is.EqualTo(1));
                Assert.That(changes, Is.EqualTo(3));
            }
            finally
            {
                FarmRecoveryState.WeekChanged -= Count;
            }

            // The week is kept in the session state, so a console opened later reads it back.
            FarmRecoveryState.SetWeek("peach", 3);
            Assert.That(FarmRecoveryState.GetWeek("peach"), Is.EqualTo(3));
        }

        // ---- severity mapping from the shared table

        [Test]
        public void Severity_WeekOneIsFullyDiseased_WeekSixIsHealthy()
        {
            Assert.That(FarmRecoveryMath.DiseaseAmount(data, 1), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(FarmRecoveryMath.DiseaseAmount(data, 6), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(FarmRecoveryMath.DiseaseAmount(data, 3), Is.EqualTo(25f / 85f).Within(1e-5f));
        }

        [Test]
        public void Severity_FallsEveryWeek()
        {
            for (var week = 2; week <= data.WeekCount; week++)
                Assert.That(FarmRecoveryMath.DiseaseAmount(data, week), Is.LessThan(FarmRecoveryMath.DiseaseAmount(data, week - 1)), $"week {week}");
        }

        [Test]
        public void StatusLine_ReadsTheTable()
        {
            Assert.That(FarmRecoveryMath.StatusLine(data.GetWeek(3), 6),
                Is.EqualTo("Week 3 of 6 · Leaf 65% · Chlorophyll 70% · Severity 25%"));
        }

        [Test]
        public void Ease_IsClampedAndMonotonic()
        {
            Assert.That(FarmRecoveryMath.Ease(-1f), Is.EqualTo(0f));
            Assert.That(FarmRecoveryMath.Ease(2f), Is.EqualTo(1f));
            Assert.That(FarmRecoveryMath.Ease(0.6f), Is.GreaterThan(FarmRecoveryMath.Ease(0.4f)));
        }

        // ---- console layout

        [Test]
        public void Console_StandsBetweenPlantAndViewer_OutsideTheRing()
        {
            var plant = new Vector3(2f, 0.25f, 3f);
            var viewer = new Vector3(2f, 1.6f, 0f); // 3 m in front of the plant
            var position = FarmRecoveryMath.ConsolePosition(plant, viewer, 1.0f, 1.03f);
            Assert.That(position.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(position.z, Is.EqualTo(3f - 1.3f).Within(1e-4f)); // ring radius + 0.3
            Assert.That(position.y, Is.EqualTo(1.03f).Within(1e-4f));
        }

        [Test]
        public void Console_KeepsClearOfACloseViewer()
        {
            Assert.That(FarmRecoveryMath.ConsoleDistance(1.4f, 1.5f), Is.EqualTo(0.8f).Within(1e-4f)); // viewer - 0.7
            Assert.That(FarmRecoveryMath.ConsoleDistance(1.4f, 0.6f), Is.EqualTo(0.5f).Within(1e-4f)); // never inside 0.5
            Assert.That(FarmRecoveryMath.ConsoleDistance(0.9f, 4f), Is.EqualTo(1.2f).Within(1e-4f));
        }

        // ---- assets from Build Plant Assets

        [Test]
        public void EveryCrop_HasHealthyAndDiseasedFarmAndBillboardAlbedos()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(CatalogPath);
            if (catalog == null)
                Assert.Ignore("PlantCatalog.asset not built yet: run BTP > Setup > 2 - Build Plant Assets.");

            Assert.That(catalog.Plants.Count, Is.EqualTo(9));
            // The nine PlantVillage plants and the ten field and tropical crops: every crop with Farm rows.
            foreach (var plant in catalog.AllPlants)
            {
                Assert.That(plant.FarmPrefab, Is.Not.Null, plant.PlantId);
                var billboardChecked = false;
                var nearChecked = false;
                foreach (var renderer in plant.FarmPrefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.shader.name != FarmRecoveryShader)
                            continue;
                        var healthy = material.GetTexture("_BaseMap");
                        var diseased = material.GetTexture("_DiseasedMap");
                        Assert.That(healthy, Is.Not.Null, $"{plant.PlantId} {material.name} healthy");
                        Assert.That(diseased, Is.Not.Null, $"{plant.PlantId} {material.name} diseased");
                        // The field and tropical crops' stems and fruit use this shader for its wind only: one atlas, no disease.
                        if (!material.name.Contains("Parts_"))
                            Assert.That(healthy, Is.Not.SameAs(diseased), $"{plant.PlantId} {material.name}");
                        Assert.That(material.GetTexture("_RecoveryMask"), Is.Not.Null, $"{plant.PlantId} {material.name} mask");
                        // The distant LOD: a baked billboard, or the apple tree's far mesh.
                        if (renderer.name == "Billboard" || renderer.name == "ModelFar")
                            billboardChecked = true;
                        else
                            nearChecked = true;
                    }
                }

                Assert.That(billboardChecked, Is.True, $"{plant.PlantId} billboard uses {FarmRecoveryShader}");
                Assert.That(nearChecked, Is.True, $"{plant.PlantId} near mesh uses {FarmRecoveryShader}");
            }
        }
    }
}
