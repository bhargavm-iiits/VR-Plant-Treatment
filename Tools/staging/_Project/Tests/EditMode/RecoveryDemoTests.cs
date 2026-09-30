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
