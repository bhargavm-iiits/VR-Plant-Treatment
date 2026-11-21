using System.Linq;
using BTP.Treatment;
using NUnit.Framework;
using UnityEngine;

namespace BTP.Tests
{
    /// <summary>Addendum 6: the separate treatment stations around the fixed chamber (pure layout).</summary>
    public sealed class StationLayoutTests
    {
        static readonly Rect Hall = Rect.MinMaxRect(-9.4f, -7f, 4.5f, 9.4f);

        [TestCase(0.71f, 0.70f, TestName = "Bench chamber (strawberry)")]
        [TestCase(1.0f, 1.12f, TestName = "Tall chamber (wheat)")]
        [TestCase(1.71f, 1.71f, TestName = "Tall chamber (tomato)")]
        [TestCase(4.14f, 3.96f, TestName = "Walk-in chamber (apple)")]
        [TestCase(7.9f, 7.9f, TestName = "Reserved walk-in exterior")]
        public void StationsAreSeparateClearOfTheChamberAndInsideTheHall(float width, float depth)
        {
            var r = StationLayout.Compute(width, depth, Hall, ChamberStation.KioskAnchorLocal);
            Assert.IsTrue(r.Valid, string.Join("; ", r.Problems));
            var chamber = new[] { new Vector2(-width, 0f), new Vector2(0f, 0f), new Vector2(0f, depth), new Vector2(-width, depth) };
            var stations = r.Stations.ToList();
            foreach (var s in stations)
            {
                Assert.GreaterOrEqual(StationLayout.Distance(s.Footprint, chamber), StationLayout.MinGap - 1e-4f, $"{s.Id} to the chamber");
                Assert.IsFalse(StationLayout.Overlaps(s.ApproachZone, chamber), $"{s.Id} approach zone");
                foreach (var p in s.Footprint.Concat(s.ApproachZone))
                    Assert.IsTrue(Hall.Contains(p), $"{s.Id} {p} inside the hall");
            }

            for (var i = 0; i < stations.Count; i++)
            for (var j = i + 1; j < stations.Count; j++)
                Assert.GreaterOrEqual(StationLayout.Distance(stations[i].Footprint, stations[j].Footprint), StationLayout.MinGap - 1e-4f,
                    $"{stations[i].Id}-{stations[j].Id}");
        }

        [Test]
        public void OverviewSeesEveryStationForRealSpecimens()
        {
            foreach (var (w, d) in new[] { (0.71f, 0.7f), (1.0f, 1.12f), (1.71f, 1.71f), (4.14f, 3.96f) })
            {
                var r = StationLayout.Compute(w, d, Hall, ChamberStation.KioskAnchorLocal);
                Assert.LessOrEqual(StationLayout.OverviewHalfSpan(r), StationLayout.OverviewHalfAngle + 0.5f, $"{w} m chamber");
                Assert.Less(r.Overview.y, -2.5f, "the overview stands in front of the stations");
            }
        }

        [Test]
        public void AnchorsFaceTheirStations()
        {
            var r = StationLayout.Compute(1.7f, 1.7f, Hall, ChamberStation.KioskAnchorLocal);
            foreach (var s in r.Stations)
            {
                var toStation = (s.Centre - s.Anchor).normalized;
                Assert.Greater(Vector2.Dot(toStation, s.Forward), 0.8f, s.Id.ToString());
            }
        }

        [Test]
        public void PolygonDistanceAndOverlap()
        {
            var a = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            var b = a.Select(p => p + new Vector2(3f, 0f)).ToArray();
            var c = a.Select(p => p + new Vector2(0.5f, 0.5f)).ToArray();
            Assert.AreEqual(2f, StationLayout.Distance(a, b), 1e-5f);
            Assert.AreEqual(0f, StationLayout.Distance(a, c), 1e-5f);
            Assert.IsTrue(StationLayout.Overlaps(a, c));
            Assert.IsFalse(StationLayout.Overlaps(a, b));
            Assert.IsTrue(StationLayout.Contains(a, new Vector2(0.5f, 0.5f)));
            Assert.IsFalse(StationLayout.Contains(a, new Vector2(1.5f, 0.5f)));
        }
    }
}
