using BTP.Lab;
using NUnit.Framework;
using UnityEngine;

namespace BTP.Tests
{
    public class RecoveryDemoTests
    {
        TomatoRecoveryData data;

        [SetUp]
        public void SetUp() => data = ScriptableObject.CreateInstance<TomatoRecoveryData>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(data);

        [TestCase(1, 25, 30, 85)]
        [TestCase(2, 40, 45, 70)]
        [TestCase(3, 65, 70, 25)]
        [TestCase(4, 75, 80, 18)]
        [TestCase(5, 88, 90, 8)]
        [TestCase(6, 96, 96, 2)]
        public void WeeksMatchTheBrief(int week, int leafHealth, int chlorophyll, int severity)
        {
            var row = data.GetWeek(week);
            Assert.That(row.week, Is.EqualTo(week));
            Assert.That(row.leafHealth, Is.EqualTo(leafHealth));
            Assert.That(row.chlorophyll, Is.EqualTo(chlorophyll));
            Assert.That(row.diseaseSeverity, Is.EqualTo(severity));
        }

        [Test]
        public void TheSameWeeklyTableServesEveryPlant()
        {
            // One shared, deterministic example table: Tomato's values are the brief's, and no plant has its own.
            Assert.That(data.WeekCount, Is.EqualTo(6));
            Assert.That(data.GetWeek(1).diseaseSeverity, Is.EqualTo(85));
            Assert.That(data.GetWeek(6).leafHealth, Is.EqualTo(96));
            foreach (var plant in new[] { "apple", "bell_pepper", "cherry", "corn", "grape", "peach", "potato", "strawberry", "tomato" })
                Assert.That(BTP.Plants.DiseaseClasses.For(plant).Count, Is.GreaterThan(0), plant);
        }

        [Test]
        public void EveryPlantHasADefaultExampleDisease()
        {
            var expected = new System.Collections.Generic.Dictionary<string, string>
            {
                ["tomato"] = "Early Blight", ["apple"] = "Apple Scab", ["bell_pepper"] = "Bacterial Spot",
                ["cherry"] = "Powdery Mildew", ["corn"] = "Common Rust", ["grape"] = "Black Rot",
                ["peach"] = "Bacterial Spot", ["potato"] = "Early Blight", ["strawberry"] = "Leaf Scorch",
            };
            foreach (var pair in expected)
            {
                var list = BTP.Plants.DiseaseClasses.For(pair.Key);
                Assert.That(list[BTP.Plants.DiseaseClasses.DefaultIndex(pair.Key)].DisplayName, Is.EqualTo(pair.Value), pair.Key);
            }

            Assert.That(BTP.Plants.DiseaseClasses.DefaultIndex("banana"), Is.EqualTo(0));
        }

        [Test]
        public void DiseaseCycling_WrapsAround()
        {
            Assert.That(BTP.Plants.DiseaseClasses.Step(0, 3, 1), Is.EqualTo(1));
            Assert.That(BTP.Plants.DiseaseClasses.Step(2, 3, 1), Is.EqualTo(0));
            Assert.That(BTP.Plants.DiseaseClasses.Step(0, 3, -1), Is.EqualTo(2));
            Assert.That(BTP.Plants.DiseaseClasses.Step(0, 1, 1), Is.EqualTo(0));
            Assert.That(BTP.Plants.DiseaseClasses.Step(5, 0, 1), Is.EqualTo(0));
        }

        [Test]
        public void OnlyWeekThreeIsMarkedAsTreatment()
        {
            Assert.That(data.WeekCount, Is.EqualTo(6));
            for (var week = 1; week <= data.WeekCount; week++)
            {
                var expected = week == TomatoRecoveryData.TreatmentWeek ? "Demo treatment applied" : null;
                Assert.That(string.IsNullOrEmpty(data.GetWeek(week).caption) ? null : data.GetWeek(week).caption, Is.EqualTo(expected));
            }
        }

        [Test]
        public void Timeline_StepsAndClampsWithinSixWeeks()
        {
            var timeline = new RecoveryTimeline(data);
            Assert.That(timeline.CurrentWeek, Is.EqualTo(1));
            Assert.That(timeline.CanGoBack, Is.False);

            timeline.Previous();
            Assert.That(timeline.CurrentWeek, Is.EqualTo(1));

            timeline.Next();
            timeline.Next();
            Assert.That(timeline.CurrentWeek, Is.EqualTo(3));

            timeline.SetWeek(99);
            Assert.That(timeline.CurrentWeek, Is.EqualTo(6));
            Assert.That(timeline.CanGoForward, Is.False);
        }

        [Test]
        public void Timeline_ResetReturnsToWeekOne()
        {
            var timeline = new RecoveryTimeline(data);
            var changes = 0;
            timeline.WeekChanged += _ => changes++;

            timeline.SetWeek(5);
            timeline.Reset();
            Assert.That(timeline.CurrentWeek, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(2));
        }
    }
}
