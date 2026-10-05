using System.Collections.Generic;
using BTP.Core;
using BTP.Plants;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BTP.Tests
{
    public class PlantCatalogTests
    {
        static readonly string[] NinePlantIds = { "apple", "bell_pepper", "cherry", "corn", "grape", "peach", "potato", "strawberry", "tomato" };

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
                Object.DestroyImmediate(obj);
            created.Clear();
        }

        PlantDefinition MakePlant(string id, bool withModels = true, bool withRecoveryDemo = true)
        {
            var plant = ScriptableObject.CreateInstance<PlantDefinition>();
            plant.Configure(id, id, "", "", "", "", withRecoveryDemo);
            if (withModels)
            {
                var model = new GameObject(id);
                created.Add(model);
                plant.SetPrefabs(model, model);
            }

            created.Add(plant);
            return plant;
        }

        PlantCatalog MakeCatalog(params string[] ids)
        {
            var catalog = ScriptableObject.CreateInstance<PlantCatalog>();
            var plants = new List<PlantDefinition>();
            foreach (var id in ids)
                plants.Add(MakePlant(id));
            catalog.SetPlants(plants);
            created.Add(catalog);
            return catalog;
        }

        [Test]
        public void NinePlantCatalog_IsValid()
        {
            var catalog = MakeCatalog(NinePlantIds);
            Assert.That(catalog.Plants.Count, Is.EqualTo(9));
            Assert.That(catalog.Plants.Count, Is.EqualTo(PlantCatalog.ExpectedPlantCount));
            Assert.That(catalog.Validate(), Is.Empty);
        }

        [Test]
        public void Validate_AcceptsGrapeAndPeach()
        {
            var catalog = MakeCatalog(NinePlantIds);
            Assert.That(catalog.TryGet("grape", out _), Is.True);
            Assert.That(catalog.TryGet("peach", out _), Is.True);
            Assert.That(catalog.Validate(), Has.None.Contains("must not be selectable"));
        }

        [Test]
        public void Validate_RejectsWrongPlantCount()
        {
            var catalog = MakeCatalog("apple", "bell_pepper", "cherry", "corn", "potato", "strawberry", "tomato");
            Assert.That(catalog.Validate(), Has.Some.Contains("expected 9"));
        }

        [Test]
        public void AllNinePlantsHaveARecoveryDemo()
        {
            var catalog = MakeCatalog(NinePlantIds);
            Assert.That(catalog.Plants.Count, Is.EqualTo(9));
            foreach (var plant in catalog.Plants)
                Assert.That(plant.HasRecoveryDemo, Is.True, plant.PlantId);
            Assert.That(catalog.Validate(), Is.Empty);
        }

        [Test]
        public void Validate_ReportsAPlantWithoutARecoveryDemo()
        {
            var catalog = ScriptableObject.CreateInstance<PlantCatalog>();
            created.Add(catalog);
            var plants = new List<PlantDefinition>();
            foreach (var id in NinePlantIds)
                plants.Add(MakePlant(id, withRecoveryDemo: id != "grape"));
            catalog.SetPlants(plants);

            Assert.That(catalog.Validate(), Has.Some.Contains("grape has no recovery demo"));
        }

        [Test]
        public void Validate_ReportsMissingModels()
        {
            var catalog = ScriptableObject.CreateInstance<PlantCatalog>();
            created.Add(catalog);
            var plants = new List<PlantDefinition>();
            foreach (var id in NinePlantIds)
                plants.Add(MakePlant(id, withModels: id != "cherry"));
            catalog.SetPlants(plants);

            var problems = catalog.Validate();
            Assert.That(problems, Has.Some.Contains("cherry has no farm model"));
            Assert.That(problems, Has.Some.Contains("cherry has no lab model"));
        }

        [Test]
        public void Selection_StartsEmpty_AndDefaultsToTomato()
        {
            var selection = new PlantSelection(MakeCatalog(NinePlantIds));
            Assert.That(selection.Current, Is.Null);
            Assert.That(selection.EnsureSelection().PlantId, Is.EqualTo("tomato"));
        }

        [Test]
        public void Selection_KeepsExplicitChoice()
        {
            var selection = new PlantSelection(MakeCatalog(NinePlantIds));
            selection.Select("cherry");
            Assert.That(selection.EnsureSelection().PlantId, Is.EqualTo("cherry"));
        }

        [Test]
        public void Selection_RaisesChangedOncePerChange()
        {
            var selection = new PlantSelection(MakeCatalog(NinePlantIds));
            var raised = 0;
            selection.Changed += _ => raised++;
            selection.Select("corn");
            selection.Select("corn");
            selection.Select("potato");
            Assert.That(raised, Is.EqualTo(2));
        }

        [Test]
        public void Selection_RejectsPlantsWithoutModels()
        {
            var selection = new PlantSelection(MakeCatalog(NinePlantIds));
            LogAssert.Expect(LogType.Error, "[BTP] Cannot select unknown plant 'banana'.");
            Assert.That(selection.Select("banana"), Is.False);
            Assert.That(selection.Current, Is.Null);
        }
    }
}
