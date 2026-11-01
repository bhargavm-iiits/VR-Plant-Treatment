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
        static readonly string[] SevenPlantIds = { "apple", "bell_pepper", "cherry", "corn", "potato", "strawberry", "tomato" };

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
                Object.DestroyImmediate(obj);
            created.Clear();
        }

        PlantDefinition MakePlant(string id, bool withModels = true)
        {
            var plant = ScriptableObject.CreateInstance<PlantDefinition>();
            plant.Configure(id, id, "", "", "", "", id == PlantCatalog.DefaultPlantId);
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
        public void SevenPlantCatalog_IsValid()
        {
            Assert.That(MakeCatalog(SevenPlantIds).Validate(), Is.Empty);
        }

        [Test]
        public void Validate_RejectsGrapeAndPeach()
        {
            var catalog = MakeCatalog("apple", "bell_pepper", "cherry", "corn", "potato", "tomato", "grape");
            Assert.That(catalog.Validate(), Has.Some.Contains("must not be selectable"));
        }

        [Test]
        public void Validate_ReportsMissingModels()
        {
            var catalog = ScriptableObject.CreateInstance<PlantCatalog>();
            created.Add(catalog);
            var plants = new List<PlantDefinition>();
            foreach (var id in SevenPlantIds)
                plants.Add(MakePlant(id, withModels: id != "cherry"));
            catalog.SetPlants(plants);

            var problems = catalog.Validate();
            Assert.That(problems, Has.Some.Contains("cherry has no farm model"));
            Assert.That(problems, Has.Some.Contains("cherry has no lab model"));
        }

        [Test]
        public void Selection_StartsEmpty_AndDefaultsToTomato()
        {
            var selection = new PlantSelection(MakeCatalog(SevenPlantIds));
            Assert.That(selection.Current, Is.Null);
            Assert.That(selection.EnsureSelection().PlantId, Is.EqualTo("tomato"));
        }

        [Test]
        public void Selection_KeepsExplicitChoice()
        {
            var selection = new PlantSelection(MakeCatalog(SevenPlantIds));
            selection.Select("cherry");
            Assert.That(selection.EnsureSelection().PlantId, Is.EqualTo("cherry"));
        }

        [Test]
        public void Selection_RaisesChangedOncePerChange()
        {
            var selection = new PlantSelection(MakeCatalog(SevenPlantIds));
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
            var selection = new PlantSelection(MakeCatalog(SevenPlantIds));
            LogAssert.Expect(LogType.Error, "[BTP] Cannot select unknown plant 'peach'.");
            Assert.That(selection.Select("peach"), Is.False);
            Assert.That(selection.Current, Is.Null);
        }
    }
}
