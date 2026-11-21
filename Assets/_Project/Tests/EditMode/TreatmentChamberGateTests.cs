using System.IO;
using BTP.Treatment;
using NUnit.Framework;
using UnityEngine;

namespace BTP.Tests
{
    /// <summary>
    /// The chamber kiosk's start-at-minimum settings and Apply gate, the specimen recovery curve, the water stream's
    /// ballistic math and the fertiliser granules bouncing off the glass walls (all pure; no scene needed).
    /// </summary>
    public sealed class TreatmentChamberGateTests
    {
        static TreatmentProfile Tomato()
        {
            var json = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Tests/EditMode/Fixtures", "treatment_profile_tomato_early_blight.json"));
            var profile = TreatmentProfile.Parse(json, out var error);
            Assert.IsNotNull(profile, error);
            return profile;
        }

        // ------------------------------------------------------------------ start at minimum

        [Test]
        public void SettingsStartAtTheMinimumAndTheFirstOption()
        {
            var profile = Tomato();
            var settings = TreatmentSettings.FromMinimum(profile);
            Assert.AreEqual(5d, settings.Get(TreatmentProfile.Temperature, "day"));
            Assert.AreEqual(5d, settings.Get(TreatmentProfile.Temperature, "night"));
            Assert.AreEqual(0d, settings.Get(TreatmentProfile.Fertiliser, "dose"));
            Assert.AreEqual(0d, settings.Get(TreatmentProfile.Spray, "targetCoverage"));
            Assert.AreEqual(0d, settings.Get(TreatmentProfile.SoilMoisture, "rootZone"));
            Assert.AreEqual("hold", settings.GetChoice(TreatmentProfile.SoilMoisture, "irrigation"));
            Assert.IsFalse(settings.CanStep(profile, TreatmentProfile.Temperature, "day", -1), "already at the minimum");
            Assert.IsTrue(settings.CanStep(profile, TreatmentProfile.Temperature, "day", +1));

            // FromStart is unchanged (the server's start values).
            Assert.AreEqual(30d, TreatmentSettings.FromStart(profile).Get(TreatmentProfile.Temperature, "day"));
            // The soil model still starts at the profile's start moisture with nothing to add.
            var watering = RootZoneWateringModel.For(profile);
            Assert.AreEqual(81d, watering.Current);
            Assert.AreEqual(0d, watering.Amount);
        }

        [Test]
        public void RestoredSettingsStillParseOverTheMinimumBase()
        {
            var profile = Tomato();
            var stored = TreatmentSettings.FromStart(profile);
            var restored = TreatmentSettings.FromEvaluationJson(profile, stored.ToEvaluationJson(profile));
            Assert.IsNotNull(restored);
            Assert.IsTrue(restored.SameAs(stored), "every stored value replaces the minimum base");
        }

        // ------------------------------------------------------------------ Apply gate

        [Test]
        public void GateCountsControlsOnTarget()
        {
            var profile = Tomato();
            var settings = TreatmentSettings.FromMinimum(profile);
            var watering = RootZoneWateringModel.For(profile);
            // Only fertiliser (target 0, no supplement) is on target at the minimum; the soil starts too wet (81 %).
            Assert.AreEqual(1, TreatmentGate.DoneCount(profile, settings, watering));
            Assert.IsTrue(TreatmentGate.ControlDone(profile, settings, 1, watering));
            Assert.AreEqual("Set all 4 controls to the chart targets (1/4 done)", TreatmentGate.Hint(1));

            settings.Set(profile.Find(TreatmentProfile.Temperature, "day"), TreatmentProfile.Temperature, 24d);
            Assert.IsFalse(TreatmentGate.ControlDone(profile, settings, 0, watering), "night is still below its band");
            settings.Set(profile.Find(TreatmentProfile.Temperature, "night"), TreatmentProfile.Temperature, 18d);
            Assert.AreEqual(2, TreatmentGate.DoneCount(profile, settings, watering));

            settings.Set(profile.Find(TreatmentProfile.Spray, "dose"), TreatmentProfile.Spray, 4d);
            settings.Set(profile.Find(TreatmentProfile.Spray, "targetCoverage"), TreatmentProfile.Spray, 85d);
            Assert.AreEqual(3, TreatmentGate.DoneCount(profile, settings, watering));
            Assert.IsFalse(TreatmentGate.AllDone(profile, settings, watering));

            // Drain 21 points at the root zone: not done while the water runs, done once it stops on target.
            Assert.IsTrue(watering.SetAmount(-21d));
            Assert.IsTrue(watering.On());
            Assert.IsFalse(TreatmentGate.SoilDone(profile, settings, watering), "never done while the water runs");
            watering.Tick(2d);
            Assert.AreEqual(3, TreatmentGate.DoneCount(profile, settings, watering));
            watering.Tick(10d);
            Assert.IsFalse(watering.Running);
            Assert.AreEqual(60d, watering.MeasuredForServer, 1e-9);
            Assert.AreEqual(SoilMoistureRules.RootZone, watering.Irrigation);
            Assert.AreEqual(4, TreatmentGate.DoneCount(profile, settings, watering));
            Assert.IsTrue(TreatmentGate.AllDone(profile, settings, watering));

            // A control moved off its band again closes the gate.
            settings.Step(profile, TreatmentProfile.Fertiliser, "dose", +1);
            Assert.IsFalse(TreatmentGate.AllDone(profile, settings, watering));
        }

        [Test]
        public void SoilGateWithoutTheWaterStationUsesThePanelSettings()
        {
            var profile = Tomato();
            var settings = TreatmentSettings.FromMinimum(profile);
            settings.Set(profile.Find(TreatmentProfile.SoilMoisture, "rootZone"), TreatmentProfile.SoilMoisture, 60d);
            // "Hold steady" keeps the start moisture (81 %): above the band.
            Assert.IsFalse(TreatmentGate.SoilDone(profile, settings, null));
            settings.SetChoice(TreatmentProfile.SoilMoisture, "irrigation", SoilMoistureRules.RootZone);
            Assert.IsTrue(TreatmentGate.SoilDone(profile, settings, null));
            settings.SetChoice(TreatmentProfile.SoilMoisture, "irrigation", SoilMoistureRules.Overhead);
            Assert.IsFalse(TreatmentGate.SoilDone(profile, settings, null), "overhead wets the leaves");
            Assert.AreEqual(0, TreatmentGate.DoneCount(null, settings, null));
        }

        // ------------------------------------------------------------------ recovery

        [Test]
        public void RecoveryEasesTheDiseaseToZeroOverEightSeconds()
        {
            Assert.AreEqual(0f, SpecimenRecovery.Smootherstep(0f), 1e-6f);
            Assert.AreEqual(0.5f, SpecimenRecovery.Smootherstep(0.5f), 1e-6f);
            Assert.AreEqual(1f, SpecimenRecovery.Smootherstep(1f), 1e-6f);
            Assert.AreEqual(1f, SpecimenRecovery.Smootherstep(3f), 1e-6f);
            Assert.AreEqual(0.8f, SpecimenRecovery.AmountAt(0.8f, 0f), 1e-6f);
            Assert.AreEqual(0.4f, SpecimenRecovery.AmountAt(0.8f, SpecimenRecovery.Seconds * 0.5f), 1e-6f);
            Assert.AreEqual(0f, SpecimenRecovery.AmountAt(0.8f, SpecimenRecovery.Seconds), 1e-6f);
            var previous = 1f;
            for (var t = 0f; t <= SpecimenRecovery.Seconds; t += 0.25f)
            {
                var amount = SpecimenRecovery.AmountAt(1f, t);
                Assert.LessOrEqual(amount, previous + 1e-6f, "the disease never comes back during the recovery");
                previous = amount;
            }

            // Gentle at both ends (smootherstep has zero slope there).
            Assert.Less(1f - SpecimenRecovery.AmountAt(1f, 0.1f), 0.001f);
            Assert.AreEqual(1f, SpecimenRecovery.LiftAt(0f), 1e-6f);
            Assert.AreEqual(1f + SpecimenRecovery.Lift, SpecimenRecovery.LiftAt(SpecimenRecovery.Seconds), 1e-6f);
            Assert.AreEqual(0.02f, SpecimenRecovery.Lift, 1e-6f);
        }

        [Test]
        public void RecoveryRunsOnceAndResetsOnANewSpecimen()
        {
            var recovery = new SpecimenRecovery();
            Assert.IsFalse(recovery.Begin(1f), "no specimen, nothing to recover");
            var plant = new GameObject("RecoveryTestPlant");
            try
            {
                recovery.Bind(plant);
                Assert.IsTrue(recovery.Begin(0.9f));
                Assert.IsFalse(recovery.Begin(0.9f), "already running");
                Assert.IsFalse(recovery.Tick(SpecimenRecovery.Seconds * 0.5f));
                Assert.AreEqual(0.45f, recovery.Amount, 1e-5f);
                Assert.AreEqual(1f + SpecimenRecovery.Lift * 0.5f, plant.transform.localScale.y, 1e-5f);
                Assert.IsTrue(recovery.Tick(SpecimenRecovery.Seconds));
                Assert.IsTrue(recovery.Done);
                Assert.AreEqual(0f, recovery.Amount);
                Assert.IsFalse(recovery.Begin(1f), "a finished recovery never starts again for this specimen");
                recovery.Bind(plant);
                Assert.IsFalse(recovery.Engaged, "a new specimen (case change) is untreated");
                Assert.AreEqual(1f, plant.transform.localScale.y, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(plant);
            }
        }

        [Test]
        public void QueuedRecoveryWaitsForTheViewerThenRunsOnce()
        {
            var recovery = new SpecimenRecovery();
            var plant = new GameObject("RecoveryQueuePlant");
            try
            {
                recovery.Bind(plant);
                Assert.IsTrue(recovery.Queue(0.8f));
                Assert.IsTrue(recovery.Waiting && recovery.Engaged && !recovery.Active);
                Assert.IsFalse(recovery.Queue(0.8f), "already queued");
                Assert.IsFalse(recovery.Begin(0.8f), "already queued");
                recovery.Tick(3f, false);
                Assert.IsTrue(recovery.Waiting, "nobody is looking yet");
                Assert.AreEqual(0.8f, recovery.Amount, 1e-6f);
                Assert.AreEqual(0, recovery.Percent);
                recovery.Tick(0.1f, true);
                Assert.IsTrue(recovery.Active, "starts as soon as the viewer looks");
                recovery.Tick(SpecimenRecovery.Seconds * 0.5f, false);
                Assert.IsTrue(recovery.Active, "once running it no longer depends on the gaze");
                Assert.AreEqual(0.4f, recovery.Amount, 1e-5f);
                Assert.AreEqual(50, recovery.Percent);
                Assert.IsTrue(recovery.Tick(SpecimenRecovery.Seconds, false));
                Assert.IsTrue(recovery.Done);
                Assert.AreEqual(100, recovery.Percent);
                Assert.IsFalse(recovery.Queue(1f), "a finished recovery is never queued again for this specimen");
            }
            finally
            {
                Object.DestroyImmediate(plant);
            }
        }

        [Test]
        public void QueuedRecoveryStartsAnywayAfterTheMaximumWait()
        {
            Assert.IsTrue(SpecimenRecovery.ShouldStart(true, 0f));
            Assert.IsFalse(SpecimenRecovery.ShouldStart(false, SpecimenRecovery.MaxWait - 0.01f));
            Assert.IsTrue(SpecimenRecovery.ShouldStart(false, SpecimenRecovery.MaxWait));
            var recovery = new SpecimenRecovery();
            var plant = new GameObject("RecoveryWaitPlant");
            try
            {
                recovery.Bind(plant);
                recovery.Queue(1f);
                for (var t = 0f; t < SpecimenRecovery.MaxWait + 0.5f; t += 0.5f)
                    recovery.Tick(0.5f, false);
                Assert.IsTrue(recovery.Active, "never waits forever");
            }
            finally
            {
                Object.DestroyImmediate(plant);
            }
        }

        [Test]
        public void RecoveryWritesTheSpecimensOwnMaterialsAndRestoresTheirTint()
        {
            var shader = Shader.Find("BTP/Farm Recovery");
            if (shader == null)
                Assert.Ignore("BTP/Farm Recovery shader not available");
            var material = new Material(shader);
            material.SetFloat(SpecimenGeometry.DiseaseAmountProperty, 1f);
            var baseColor = new Color(0.9f, 0.8f, 0.7f, 1f);
            material.SetColor("_BaseColor", baseColor);
            var plant = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plant.GetComponent<MeshRenderer>().sharedMaterial = material;
            var recovery = new SpecimenRecovery();
            try
            {
                recovery.Bind(plant, new[] { material });
                Assert.AreEqual(1, recovery.Materials.Count);
                recovery.Begin(1f);
                recovery.Tick(SpecimenRecovery.Seconds * 0.5f);
                Assert.AreEqual(0.5f, material.GetFloat(SpecimenGeometry.DiseaseAmountProperty), 1e-4f);
                Assert.Greater(material.GetColor("_BaseColor").g, baseColor.g, "the leaves glow greener mid-way");
                recovery.Tick(SpecimenRecovery.Seconds);
                Assert.AreEqual(0f, material.GetFloat(SpecimenGeometry.DiseaseAmountProperty), 1e-6f, "the material itself stays healed");
                Assert.AreEqual(baseColor, material.GetColor("_BaseColor"), "the glow fades back to the plain tint");
                Assert.IsFalse(plant.GetComponent<MeshRenderer>().HasPropertyBlock(), "no property block to be cleared by others");
            }
            finally
            {
                Object.DestroyImmediate(plant);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void GlowTintIsPlainAtBothEndsAndGreenestHalfWay()
        {
            Assert.AreEqual(Color.white, SpecimenRecovery.GlowTint(0f));
            var end = SpecimenRecovery.GlowTint(1f);
            Assert.AreEqual(1f, end.g, 1e-5f);
            Assert.AreEqual(1f, end.r, 1e-5f);
            var mid = SpecimenRecovery.GlowTint(0.5f);
            Assert.AreEqual(1f + SpecimenRecovery.GlowGreen, mid.g, 1e-5f);
            Assert.Less(mid.r, 1f);
            Assert.Greater(SpecimenRecovery.GlowTint(0.5f).g, SpecimenRecovery.GlowTint(0.25f).g);
        }

        // ------------------------------------------------------------------ soil-top spread

        [Test]
        public void SpreadRadiusGrowsWithTheWaterDelivered()
        {
            const float max = 0.25f;
            Assert.AreEqual(0f, SoilWaterSpread.TargetRadius(0f, max), "dry soil: no patch");
            var first = SoilWaterSpread.TargetRadius(0.006f, max);
            Assert.That(first, Is.InRange(SoilWaterSpread.MinRadius, 0.05f), "the first water makes a small spot");
            Assert.AreEqual(max, SoilWaterSpread.TargetRadius(1f, max), 1e-6f);
            Assert.AreEqual(max, SoilWaterSpread.TargetRadius(3f, max), 1e-6f, "clamped");
            var previous = 0f;
            for (var w = 0.01f; w <= 1f; w += 0.01f)
            {
                var r = SoilWaterSpread.TargetRadius(w, max);
                Assert.Greater(r, previous - 1e-6f, "more water never shrinks the patch");
                previous = r;
            }

            // Area (r^2) grows about linearly with the water: a quarter of the water reaches half way.
            var quarter = SoilWaterSpread.TargetRadius(0.25f, max);
            Assert.AreEqual(Mathf.Lerp(SoilWaterSpread.MinRadius, max, 0.5f), quarter, 1e-5f);
        }

        [Test]
        public void SpreadCreepsOutwardAtAFiniteSpeedAndTheFilmSoaksIn()
        {
            var r = SoilWaterSpread.StepRadius(0f, 0.2f, 1f);
            Assert.AreEqual(SoilWaterSpread.SpreadSpeed, r, 1e-6f, "the edge creeps, it does not jump");
            Assert.AreEqual(0.2f, SoilWaterSpread.StepRadius(0.19f, 0.2f, 10f), 1e-6f, "and stops at the target");
            Assert.AreEqual(0.2f - SoilWaterSpread.DrySpeed, SoilWaterSpread.StepRadius(0.2f, 0f, 1f), 1e-6f, "drying shrinks it slowly");
            Assert.AreEqual(0.1f, SoilWaterSpread.StepRadius(0.1f, 0.2f, -1f), 1e-6f, "no negative time");

            var film = 0f;
            for (var t = 0f; t < 1f; t += 0.1f)
                film = SoilWaterSpread.StepFilm(film, true, 0.1f);
            Assert.AreEqual(1f, film, 1e-5f, "the film builds within a second of flowing water");
            film = SoilWaterSpread.StepFilm(film, false, 1f);
            Assert.AreEqual(1f - SoilWaterSpread.FilmSoak, film, 1e-5f, "after OFF it soaks in gradually");
            Assert.AreEqual(0f, SoilWaterSpread.StepFilm(film, false, 10f), 1e-6f);
            Assert.LessOrEqual(SoilWaterSpread.PuddleRadius(0.3f), 0.06f);
            Assert.AreEqual(0.01f, SoilWaterSpread.PuddleRadius(0.02f), 1e-6f);
        }

        [Test]
        public void SpreadCoverageIsSoftAndIrregularAtTheEdge()
        {
            Assert.AreEqual(0f, SoilWaterSpread.Coverage(0f, 0f, 0f), "no patch, no coverage");
            Assert.AreEqual(1f, SoilWaterSpread.Coverage(0f, 0.1f, 0f), 1e-6f);
            Assert.AreEqual(0.5f, SoilWaterSpread.Coverage(0.1f, 0.1f, 0f), 1e-5f, "half at the nominal edge");
            Assert.AreEqual(0f, SoilWaterSpread.Coverage(0.1f + SoilWaterSpread.Feather, 0.1f, 0f), 1e-6f);
            Assert.Greater(SoilWaterSpread.Coverage(0.1f, 0.1f, 1f), SoilWaterSpread.Coverage(0.1f, 0.1f, -1f), "the wobble moves the edge");
            var lo = float.MaxValue;
            var hi = float.MinValue;
            for (var a = 0f; a < Mathf.PI * 2f; a += 0.05f)
            {
                var w = SoilWaterSpread.Wobble(a, 0.3f, 1.1f, 2.2f);
                lo = Mathf.Min(lo, w);
                hi = Mathf.Max(hi, w);
            }

            Assert.GreaterOrEqual(lo, -1f);
            Assert.LessOrEqual(hi, 1f);
            Assert.Greater(hi - lo, 0.5f, "the edge is visibly irregular");
        }

        // ------------------------------------------------------------------ water stream

        [Test]
        public void StreamFollowsTheBallisticPathToTheGround()
        {
            var t = WaterStreamFx.TimeToGround(1f, 0f, 0f);
            Assert.AreEqual(Mathf.Sqrt(2f / WaterStreamFx.Gravity), t, 1e-5f);
            var landing = WaterStreamFx.Ballistic(new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f), t);
            Assert.AreEqual(0f, landing.y, 1e-4f);
            Assert.AreEqual(t, landing.x, 1e-5f);

            // Thrown downwards it lands sooner; thrown upwards later; already below the ground: at once.
            var down = WaterStreamFx.TimeToGround(0.5f, -1f, 0f);
            Assert.AreEqual(0f, WaterStreamFx.Ballistic(new Vector3(0f, 0.5f, 0f), Vector3.down, down).y, 1e-4f);
            Assert.Less(down, WaterStreamFx.TimeToGround(0.5f, 0f, 0f));
            Assert.Greater(WaterStreamFx.TimeToGround(0.5f, 1f, 0f), WaterStreamFx.TimeToGround(0.5f, 0f, 0f));
            Assert.AreEqual(0f, WaterStreamFx.TimeToGround(0f, 0f, 0.1f));

            // A constant flow thins as it accelerates (r ~ 1/sqrt(v)) and never swells.
            Assert.AreEqual(0.002f, WaterStreamFx.RadiusAt(0.004f, 1f, 4f), 1e-6f);
            Assert.AreEqual(0.004f, WaterStreamFx.RadiusAt(0.004f, 1f, 0.5f), 1e-6f);
            Assert.Less(WaterStreamFx.RadiusAt(0.004f, 1f, 3f), WaterStreamFx.RadiusAt(0.004f, 1f, 2f));
        }

        // ------------------------------------------------------------------ granules vs glass

        [Test]
        public void GranuleBouncesOffAGlassWallWithLowRestitution()
        {
            var min = new Vector2(-0.5f, -0.5f);
            var max = new Vector2(0.5f, 0.5f);
            var r = GranuleSimulation.Radius;
            var p = new Vector3(-0.52f, 0.5f, 0f);
            var v = new Vector3(-2f, 0.5f, 1f);
            Assert.IsTrue(GranuleSimulation.CollideWalls(ref p, ref v, min, max, r));
            Assert.AreEqual(min.x + r, p.x, 1e-6f, "back on the wall's inner plane");
            Assert.AreEqual(2f * GranuleSimulation.WallRestitution, v.x, 1e-6f, "reflected with restitution 0.2");
            Assert.AreEqual(0.5f * (1f - GranuleSimulation.WallFriction), v.y, 1e-6f, "friction along the wall");
            Assert.AreEqual(1f * (1f - GranuleSimulation.WallFriction), v.z, 1e-6f);

            // The back wall (+Z) reflects Z; a granule inside is untouched.
            p = new Vector3(0f, 0.3f, 0.6f);
            v = new Vector3(0f, 0f, 3f);
            Assert.IsTrue(GranuleSimulation.CollideWalls(ref p, ref v, min, max, r));
            Assert.AreEqual(max.y - r, p.z, 1e-6f);
            Assert.AreEqual(-3f * GranuleSimulation.WallRestitution, v.z, 1e-6f);
            p = Vector3.zero;
            v = Vector3.one;
            Assert.IsFalse(GranuleSimulation.CollideWalls(ref p, ref v, min, max, r));
            Assert.AreEqual(Vector3.one, v);
        }

        [Test]
        public void GranulesThrownAtTheGlassStayInsideTheChamber()
        {
            var area = new GranuleSimulation.Area
            {
                SoilY = 0.4f, Min = new Vector2(-0.12f, -0.1f), Max = new Vector2(0.12f, 0.1f), Stem = Vector2.zero, StemClearance = 0.03f,
                BandInner = 0.04f, BandOuter = 0.09f, BandDirection = Vector2.left, HasWalls = true,
                WallMin = new Vector2(-0.2f, -0.18f), WallMax = new Vector2(0.2f, 0.18f),
            };
            var sim = new GranuleSimulation(5, area);
            sim.Launch(new Vector3(0f, 0.9f, 0f), new Vector3(6f, 0.5f, 0f));
            sim.Launch(new Vector3(0f, 0.9f, 0f), new Vector3(-5f, 1f, 4f));
            sim.Launch(new Vector3(0.1f, 0.7f, 0.1f), new Vector3(0f, 0f, 9f));
            var r = GranuleSimulation.Radius;
            var touched = false;
            for (var step = 0; step < 600 && !sim.AllSettled; step++)
            {
                sim.Step(1f / 60f);
                foreach (var g in sim.Granules)
                {
                    Assert.GreaterOrEqual(g.Position.x, area.WallMin.x + r - 1e-5f);
                    Assert.LessOrEqual(g.Position.x, area.WallMax.x - r + 1e-5f);
                    Assert.GreaterOrEqual(g.Position.z, area.WallMin.y + r - 1e-5f);
                    Assert.LessOrEqual(g.Position.z, area.WallMax.y - r + 1e-5f);
                    touched |= Mathf.Abs(g.Position.x) >= area.WallMax.x - r - 1e-4f || Mathf.Abs(g.Position.z) >= area.WallMax.y - r - 1e-4f;
                }
            }

            Assert.IsTrue(touched, "the throws reach the glass");
            Assert.IsTrue(sim.AllSettled);
            foreach (var g in sim.Granules)
            {
                Assert.GreaterOrEqual(g.Position.x, area.Min.x - 1e-5f, "settled on the soil");
                Assert.LessOrEqual(g.Position.x, area.Max.x + 1e-5f);
            }

            // Without walls (the existing behaviour) nothing changes: the same throw leaves the wall box in flight.
            var open = area;
            open.HasWalls = false;
            var free = new GranuleSimulation(5, open);
            free.Launch(new Vector3(0f, 0.9f, 0f), new Vector3(6f, 0.5f, 0f));
            free.Step(0.1f);
            Assert.Greater(free.Granules[0].Position.x, area.WallMax.x);
        }
    }
}
