using BTP.Farm;
using BTP.Plants;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BTP.Tests
{
    public class DiseaseGalleryTests
    {
        const string CatalogPath = "Assets/_Project/Data/DiseaseGalleryCatalog.asset";

        static readonly (string plant, int count)[] Expected =
        {
            ("apple", 3), ("bell_pepper", 1), ("cherry", 1), ("corn", 3), ("grape", 3),
            ("peach", 1), ("potato", 2), ("strawberry", 1), ("tomato", 9),
        };

        // ---- class list (pure data)

        [Test]
        public void ClassList_HasTheRightCountPerPlant()
        {
            foreach (var (plant, count) in Expected)
                Assert.That(DiseaseClasses.For(plant).Count, Is.EqualTo(count), plant);
            Assert.That(DiseaseClasses.All.Count, Is.EqualTo(24));
        }

        [Test]
        public void ClassList_NamesAreNotEmpty_AndIdsAreUniquePerPlant()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var info in DiseaseClasses.All)
            {
                Assert.That(info.DisplayName, Is.Not.Empty);
                Assert.That(info.DiseaseId, Is.Not.Empty);
                Assert.That(seen.Add(info.PlantId + "/" + info.DiseaseId), Is.True, info.PlantId + "/" + info.DiseaseId);
                Assert.That(info.DisplayName.ToLowerInvariant(), Does.Not.Contain("healthy"));
            }
        }

        [Test]
        public void ClassList_UnknownPlant_IsEmpty()
        {
            Assert.That(DiseaseClasses.For("banana"), Is.Empty);
            Assert.That(DiseaseClasses.For(""), Is.Empty);
            Assert.That(DiseaseClasses.For(null), Is.Empty);
        }

        // ---- catalog asset (exists after Build Plant Assets)

        [Test]
        public void CatalogAsset_HasLeafImagesForEveryDisease()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DiseaseGalleryCatalog>(CatalogPath);
            if (catalog == null)
                Assert.Ignore("DiseaseGalleryCatalog.asset not built yet: run BTP > Setup > 2 - Build Plant Assets.");

            Assert.That(catalog.TotalCount, Is.EqualTo(24));
            foreach (var (plant, count) in Expected)
            {
                var diseases = catalog.GetDiseases(plant);
                Assert.That(diseases.Count, Is.EqualTo(count), plant);
                foreach (var disease in diseases)
                {
                    Assert.That(disease.displayName, Is.Not.Empty, plant);
                    Assert.That(disease.leaf, Is.Not.Null, $"{plant}/{disease.id} has no leaf image");
                }
            }

            Assert.That(catalog.GetDiseases("banana"), Is.Empty);
        }

        [Test]
        public void EmptyCatalog_UnknownPlant_IsEmpty()
        {
            var catalog = ScriptableObject.CreateInstance<DiseaseGalleryCatalog>();
            try
            {
                Assert.That(catalog.GetDiseases("tomato"), Is.Empty);
                Assert.That(catalog.TotalCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        // ---- ring layout helpers

        [Test]
        public void Layout_AnglesAreEvenlySpaced()
        {
            for (var count = 1; count <= 9; count++)
            {
                for (var i = 1; i < count; i++)
                {
                    var step = DiseaseRingLayout.AngleDegrees(i, count) - DiseaseRingLayout.AngleDegrees(i - 1, count);
                    Assert.That(step, Is.EqualTo(360f / count).Within(1e-3f));
                }
            }

            Assert.That(DiseaseRingLayout.AngleDegrees(2, 4, 10f), Is.EqualTo(190f).Within(1e-3f));
        }

        [Test]
        public void Layout_OffsetsLieOnTheCircle()
        {
            for (var i = 0; i < 9; i++)
            {
                var offset = DiseaseRingLayout.Offset(DiseaseRingLayout.AngleDegrees(i, 9), 1.4f);
                Assert.That(offset.magnitude, Is.EqualTo(1.4f).Within(1e-3f));
                Assert.That(offset.y, Is.EqualTo(0f));
            }
        }

        [Test]
        public void Layout_RadiusIsClamped()
        {
            Assert.That(DiseaseRingLayout.RadiusFor(0.1f), Is.EqualTo(0.9f));
            Assert.That(DiseaseRingLayout.RadiusFor(10f), Is.EqualTo(2.4f));
            Assert.That(DiseaseRingLayout.RadiusFor(1.5f), Is.GreaterThan(DiseaseRingLayout.RadiusFor(0.4f)));
        }

        [Test]
        public void Layout_NearCheck_UsesGroundDistance()
        {
            var plant = new Vector3(2f, 0.25f, 3f);
            Assert.That(DiseaseRingLayout.IsWithin(new Vector3(2f, 1.6f, 6.9f), plant, 4f), Is.True);   // 3.9 m
            Assert.That(DiseaseRingLayout.IsWithin(new Vector3(2f, 1.6f, 7.1f), plant, 4f), Is.False);  // 4.1 m
            Assert.That(DiseaseRingLayout.IsWithin(new Vector3(2f, 50f, 3f), plant, 4f), Is.True);       // height ignored
        }

        [Test]
        public void Layout_EaseOutBack_OvershootsThenSettles()
        {
            Assert.That(DiseaseRingLayout.EaseOutBack(0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(DiseaseRingLayout.EaseOutBack(1f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(DiseaseRingLayout.EaseOutBack(0.7f), Is.GreaterThan(1f));
        }

        [Test]
        public void Layout_StaggerDelaysLaterCards()
        {
            Assert.That(DiseaseRingLayout.CardProgress(0.1f, 0, 0.1f, 0.5f), Is.GreaterThan(0f));
            Assert.That(DiseaseRingLayout.CardProgress(0.1f, 3, 0.1f, 0.5f), Is.EqualTo(0f));
            Assert.That(DiseaseRingLayout.TotalTime(9, 0.07f, 0.55f), Is.EqualTo(8 * 0.07f + 0.55f).Within(1e-4f));
        }
    }
}
