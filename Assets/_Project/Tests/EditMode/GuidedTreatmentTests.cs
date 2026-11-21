using System.Collections.Generic;
using System.IO;
using BTP.Treatment;
using NUnit.Framework;
using UnityEngine;

namespace BTP.Tests
{
    /// <summary>
    /// The guided hands-on treatment's pure logic: step plans from the profile, the instruction popup state machine, the
    /// trigger-consume rule, scoop/spray counting with excess, dose validation, the root-zone and spray-cone geometry,
    /// the tools' eased return to their holders and the result persistence round trip.
    /// </summary>
    public sealed class GuidedTreatmentTests
    {
        static TreatmentProfile Tomato()
        {
            var json = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Tests/EditMode/Fixtures", "treatment_profile_tomato_early_blight.json"));
            var profile = TreatmentProfile.Parse(json, out var error);
            Assert.IsNotNull(profile, error);
            return profile;
        }

        static GuidedTreatmentConfig Config()
        {
            var config = ScriptableObject.CreateInstance<GuidedTreatmentConfig>();
            config.hideFlags = HideFlags.DontSave;
            return config;
        }

        // ------------------------------------------------------------------ plans

        [Test]
        public void PlansReadTheProfileTargets()
        {
            var plans = GuidedPlanner.Build(Tomato(), Config());
            Assert.AreEqual(4, plans.Count);
            Assert.AreEqual(GuidedStepKind.Sunlight, plans[0].Kind);
            Assert.AreEqual(24.5d, plans[0].DayTarget, 1e-9, "middle of 22-27 on the 0.5 grid");
            Assert.AreEqual(18d, plans[0].NightTarget, 1e-9);
            StringAssert.Contains("24.5°C", plans[0].Instruction);
            StringAssert.Contains("press Apply", plans[0].Instruction);
            Assert.AreEqual("Step 1/4", plans[0].StepLabel);

            // Tomato early blight has no confirmed nutrient: balanced maintenance feed.
            Assert.AreEqual(GuidedStepKind.Fertiliser, plans[1].Kind);
            Assert.IsTrue(plans[1].Maintenance);
            Assert.AreEqual("NPK", plans[1].ProductId);
            Assert.AreEqual(2, plans[1].DoseTarget);
            Assert.AreEqual("Open the glass and add 2 scoops of NPK 19-19-19 to the soil around the roots.", plans[1].Instruction);

            // Spray indicated, 3-5 training units at 1 per press: exactly 3 sprays.
            Assert.IsFalse(plans[2].Maintenance);
            Assert.AreEqual(3, plans[2].DoseTarget);
            Assert.AreEqual("Open the glass and apply exactly 3 sprays to the leaves.", plans[2].Instruction);

            // Root zone 81% -> middle of 53-67 = 60: water meter -21%.
            Assert.AreEqual(-21d, plans[3].WaterAmount, 1e-9);
            Assert.AreEqual("−21%", plans[3].TargetText);
            StringAssert.StartsWith("Set the water meter to −21%, then press Apply", plans[3].Instruction);
        }

        [Test]
        public void NutrientSelectsItsProductAndOverridesWin()
        {
            var profile = Tomato();
            var fertiliser = profile.Find(TreatmentProfile.Fertiliser);
            fertiliser.Nutrient = "Potassium";
            fertiliser.Find("dose").TargetMin = 4;
            fertiliser.Find("dose").TargetMax = 6;
            var config = Config();
            var plans = GuidedPlanner.Build(profile, config);
            Assert.AreEqual("K", plans[1].ProductId);
            Assert.IsFalse(plans[1].Maintenance);
            Assert.AreEqual(3, plans[1].DoseTarget, "5 units at 2 per scoop rounds to 3 scoops");

            config.overrides.Add(new GuidedTreatmentConfig.CaseOverride { caseId = "tomato/early_blight", scoops = 4, sprays = 2, waterAmount = -15f });
            plans = GuidedPlanner.Build(profile, config);
            Assert.AreEqual(4, plans[1].DoseTarget);
            Assert.AreEqual(2, plans[2].DoseTarget);
            Assert.AreEqual(-15d, plans[3].WaterAmount, 1e-9);
        }

        [Test]
        public void DoseValidationExplainsWrongValues()
        {
            var plans = GuidedPlanner.Build(Tomato(), Config());
            Assert.IsTrue(GuidedPlanner.ValidateTemperature(plans[0], 24.5, 18, true, out var problem), problem);
            Assert.IsTrue(GuidedPlanner.ValidateTemperature(plans[0], 22, 20, true, out _), "band edges are on target");
            Assert.IsFalse(GuidedPlanner.ValidateTemperature(plans[0], 30, 18, true, out problem));
            StringAssert.Contains("Day temperature is 30°C — too high", problem);
            Assert.IsFalse(GuidedPlanner.ValidateTemperature(plans[0], 24.5, 5, true, out problem));
            StringAssert.Contains("Night temperature is 5°C — too low", problem);

            Assert.IsTrue(GuidedPlanner.ValidateWater(plans[3], 81, -21, out problem), problem);
            Assert.IsTrue(GuidedPlanner.ValidateWater(plans[3], 81, -14, out _), "67% is the band's top");
            Assert.IsFalse(GuidedPlanner.ValidateWater(plans[3], 81, -5, out problem));
            StringAssert.Contains("76%", problem);
        }

        [Test]
        public void WaterTargetFollowsTheSoilsActualMoisture()
        {
            var profile = Tomato();
            var plans = GuidedPlanner.Build(profile, Config());
            // A revisited specimen whose soil is already at 64%: only -4% brings it to the band's middle (60%).
            GuidedPlanner.RetargetWater(plans[3], profile.Find(TreatmentProfile.SoilMoisture, "rootZone"), 64d, Config());
            Assert.AreEqual(-4d, plans[3].WaterAmount, 1e-9);
            Assert.AreEqual("−4%", plans[3].TargetText);
            Assert.IsTrue(GuidedPlanner.ValidateWater(plans[3], 64d, plans[3].WaterAmount, out var problem), problem);
            StringAssert.Contains("−4%", plans[3].Instruction);
        }

        [Test]
        public void ComposedSettingsPassTheKioskGate()
        {
            var profile = Tomato();
            var config = Config();
            var plans = GuidedPlanner.Build(profile, config);
            var board = TreatmentSettings.FromMinimum(profile);
            board.Set(profile.Find(TreatmentProfile.Temperature, "day"), TreatmentProfile.Temperature, 24.5);
            board.Set(profile.Find(TreatmentProfile.Temperature, "night"), TreatmentProfile.Temperature, 18);
            var settings = GuidedPlanner.ComposeSettings(profile, board, plans, 2, 3, config);
            for (var i = 0; i < 3; i++)
                Assert.IsTrue(TreatmentGate.ControlDone(profile, settings, i, null), $"control {i} on target");
            Assert.AreEqual(3d, settings.Get(TreatmentProfile.Spray, "dose"), 1e-9, "the counted sprays");
            Assert.AreEqual(0d, settings.Get(TreatmentProfile.Fertiliser, "dose"), 1e-9, "maintenance feed is not a supplement");
        }

        // ------------------------------------------------------------------ popup state machine

        [Test]
        public void InstructionPopupShowsOnceUntilOkAndOnlySuccessAdvances()
        {
            var sequencer = new GuidedSequencer();
            var shown = new List<GuidedStepKind>();
            sequencer.InstructionShown += plan => shown.Add(plan.Kind);
            sequencer.Start(GuidedPlanner.Build(Tomato(), Config()));

            Assert.AreEqual(GuidedSequencer.Phase.Instruction, sequencer.Current, "shown at step start");
            Assert.IsTrue(sequencer.InstructionVisible);
            Assert.IsFalse(sequencer.CanWork(GuidedStepKind.Sunlight), "tools wait for OK");
            Assert.IsFalse(sequencer.CompleteStep(GuidedStepKind.Sunlight), "no success while the popup is up");

            Assert.IsTrue(sequencer.Acknowledge(), "OK hides it");
            Assert.IsFalse(sequencer.InstructionVisible);
            Assert.IsTrue(sequencer.CanWork(GuidedStepKind.Sunlight));
            Assert.IsFalse(sequencer.Acknowledge(), "a second OK does nothing");

            // Working the step (and failing) never reopens the instruction.
            Assert.IsFalse(sequencer.RequestInstruction());
            Assert.IsFalse(sequencer.CompleteStep(GuidedStepKind.Spray), "the wrong step cannot complete");
            Assert.IsFalse(sequencer.InstructionVisible);
            Assert.AreEqual(1, sequencer.InstructionsShown);

            // Success: the next instruction shows.
            Assert.IsTrue(sequencer.BeginResponse(GuidedStepKind.Sunlight));
            Assert.IsFalse(sequencer.CanWork(GuidedStepKind.Sunlight), "attempts are ignored during the response");
            Assert.IsTrue(sequencer.CompleteStep(GuidedStepKind.Sunlight, "24.5/18"));
            Assert.IsTrue(sequencer.InstructionVisible);
            Assert.AreEqual(GuidedStepKind.Fertiliser, sequencer.Step.Kind);
            Assert.AreEqual(2, sequencer.InstructionsShown);

            foreach (var kind in new[] { GuidedStepKind.Fertiliser, GuidedStepKind.Spray, GuidedStepKind.Water })
            {
                Assert.IsTrue(sequencer.Acknowledge());
                Assert.IsTrue(sequencer.CompleteStep(kind));
            }

            Assert.IsTrue(sequencer.Completed);
            Assert.IsFalse(sequencer.InstructionVisible);
            CollectionAssert.AreEqual(new[] { GuidedStepKind.Sunlight, GuidedStepKind.Fertiliser, GuidedStepKind.Spray, GuidedStepKind.Water }, shown);
            Assert.AreEqual("24.5/18", sequencer.Summaries[0]);
        }

        // ------------------------------------------------------------------ trigger consume

        [Test]
        public void OkPressIsConsumedUntilThatTriggerIsReleased()
        {
            var gate = new TriggerConsumeGate { Modal = true };
            gate.Observe("right", true);
            gate.Observe("left", false);
            Assert.IsFalse(gate.Allows("right"), "nothing while the popup is up");
            Assert.IsFalse(gate.Allows("left"));

            // OK pressed with the right trigger (still held as the popup hides).
            gate.ConsumeHeld();
            gate.Modal = false;
            Assert.IsFalse(gate.Allows("right"), "the dismissing press does not spray/apply");
            Assert.IsTrue(gate.Allows("left"), "the other hand is free");
            gate.Observe("right", true);
            Assert.IsFalse(gate.Allows("right"), "still the same press");
            gate.Observe("right", false);
            Assert.IsTrue(gate.Allows("right"), "released: the next press works");
            gate.Observe("right", true);
            Assert.IsTrue(gate.Allows("right"));

            // A mouse OK is consumed explicitly.
            gate.Consume("mouse");
            Assert.IsFalse(gate.Allows("mouse"));
            gate.Observe("mouse", false);
            Assert.IsTrue(gate.Allows("mouse"));
        }

        // ------------------------------------------------------------------ counting

        [Test]
        public void SpraysCountOnlyHitsAndExcessNeedsAReset()
        {
            var counter = new DoseCounter(3);
            Assert.AreEqual(DoseCounter.Result.Missed, counter.Register(false), "a missed spray");
            Assert.AreEqual(0, counter.Count);
            Assert.AreEqual(DoseCounter.Result.Counted, counter.Register(true));
            Assert.AreEqual(DoseCounter.Result.Counted, counter.Register(true));
            Assert.AreEqual("2/3 sprays", counter.Progress(null, "spray"));
            Assert.AreEqual(DoseCounter.Result.Reached, counter.Register(true));
            Assert.IsFalse(counter.Tick(0.5f), "confirming");
            Assert.AreEqual(DoseCounter.Result.Excess, counter.Register(true), "one too many inside the window");
            Assert.IsTrue(counter.Excess);
            Assert.IsFalse(counter.Tick(5f), "an excess never completes");

            counter.Reset();
            Assert.AreEqual(0, counter.Count);
            for (var i = 0; i < 3; i++)
                counter.Register(true);
            Assert.IsFalse(counter.Tick(1f));
            Assert.IsTrue(counter.Tick(0.3f), "held at the target");
            Assert.IsTrue(counter.Complete);
            Assert.AreEqual(DoseCounter.Result.Ignored, counter.Register(true));
            Assert.AreEqual("NPK 19-19-19 · 3/3 scoops", new DoseCounterProbe(3).Progress());
        }

        sealed class DoseCounterProbe
        {
            readonly DoseCounter counter;
            public DoseCounterProbe(int n)
            {
                counter = new DoseCounter(n);
                for (var i = 0; i < n; i++)
                    counter.Register(true);
            }

            public string Progress() => counter.Progress("NPK 19-19-19", "scoop");
        }

        [Test]
        public void ScoopCountsOnlyWhenGranulesLandInTheRootZone()
        {
            var stem = new Vector2(0f, 0f);
            var min = new Vector2(-0.2f, -0.2f);
            var max = new Vector2(0.2f, 0.2f);
            var onRoots = new[] { new Vector3(0.05f, 0f, 0.02f), new Vector3(-0.06f, 0f, 0.03f), new Vector3(0.0f, 0f, -0.08f) };
            Assert.AreEqual(1f, GuidedGeometry.RootZoneShare(onRoots, stem, 0.1f, min, max), 1e-6f);
            var spilled = new[] { new Vector3(0.05f, 0f, 0.02f), new Vector3(0.18f, 0f, 0.18f), new Vector3(0.5f, 0f, 0f), new Vector3(0.15f, 0f, 0f) };
            Assert.AreEqual(0.25f, GuidedGeometry.RootZoneShare(spilled, stem, 0.1f, min, max), 1e-6f, "outside the radius or the tray");
            Assert.AreEqual(0f, GuidedGeometry.RootZoneShare(new Vector3[0], stem, 0.1f, min, max));
            // Another plant's tray (the wrong plant) is outside this root zone.
            Assert.IsFalse(GuidedGeometry.InRootZone(new Vector3(2f, 0f, 0f), stem, 0.1f, min, max));
        }

        [Test]
        public void SprayConeMustReachThePlant()
        {
            var plant = new Bounds(new Vector3(0f, 1f, 1f), new Vector3(0.4f, 0.6f, 0.4f));
            Assert.Greater(GuidedGeometry.ConeHitShare(new Vector3(0f, 1f, 0.3f), Vector3.forward, 10f, 0.75f, plant), 0.9f, "aimed at the leaves");
            Assert.AreEqual(0f, GuidedGeometry.ConeHitShare(new Vector3(0f, 1f, 0.3f), Vector3.back, 10f, 0.75f, plant), "aimed away");
            Assert.AreEqual(0f, GuidedGeometry.ConeHitShare(new Vector3(0f, 1f, -1.5f), Vector3.forward, 10f, 0.75f, plant), "out of range");
            var otherPlant = new Bounds(new Vector3(3f, 1f, 1f), Vector3.one * 0.4f);
            Assert.AreEqual(0f, GuidedGeometry.ConeHitShare(new Vector3(3f, 1f, 0.3f), Vector3.forward, 10f, 0.75f, plant), "the wrong plant");
            Assert.Greater(GuidedGeometry.ConeHitShare(new Vector3(3f, 1f, 0.3f), Vector3.forward, 10f, 0.75f, otherPlant), 0.9f);
        }

        // ------------------------------------------------------------------ home return

        [Test]
        public void ReleasedToolsEaseIntoTheirHolder()
        {
            var home = new Vector3(1f, 0.8f, 2f);
            var homeRotation = Quaternion.Euler(0f, 90f, 0f);
            Assert.IsTrue(GuidedGeometry.ReleaseSettlesHome(home + new Vector3(0.2f, 0.1f, 0f), home, 0.25f));
            Assert.IsFalse(GuidedGeometry.ReleaseSettlesHome(home + new Vector3(0.3f, 0f, 0f), home, 0.25f), "too far: it drops");

            var from = home + Vector3.up * 0.2f;
            var fromRotation = Quaternion.identity;
            GuidedGeometry.SettlePose(from, fromRotation, home, homeRotation, 0f, 0.25f, out var p0, out var r0);
            Assert.Less(Vector3.Distance(p0, from), 1e-5f);
            Assert.Less(Quaternion.Angle(r0, fromRotation), 0.01f);
            GuidedGeometry.SettlePose(from, fromRotation, home, homeRotation, 0.125f, 0.25f, out var mid, out var midRotation);
            Assert.AreEqual(0.5f, Vector3.Distance(mid, from) / 0.2f, 1e-3f, "smoothstep is half way at half time");
            Assert.AreEqual(45f, Quaternion.Angle(midRotation, homeRotation), 0.5f);
            GuidedGeometry.SettlePose(from, fromRotation, home, homeRotation, 0.3f, 0.25f, out var end, out var endRotation);
            Assert.Less(Vector3.Distance(end, home), 1e-5f);
            Assert.Less(Quaternion.Angle(endRotation, homeRotation), 0.01f);
            Assert.Less(GuidedGeometry.Ease(0.1f), 0.1f, "eases in");
            Assert.Greater(GuidedGeometry.Ease(0.9f), 0.9f, "eases out");

            Assert.IsFalse(GuidedGeometry.ShouldAutoReturn(3f, 8f, home + Vector3.right, 0f, home, 6f));
            Assert.IsTrue(GuidedGeometry.ShouldAutoReturn(8f, 8f, home + Vector3.right, 0f, home, 6f), "idle");
            Assert.IsTrue(GuidedGeometry.ShouldAutoReturn(0f, 8f, new Vector3(1f, -2f, 2f), 0f, home, 6f), "fell through the floor");
            Assert.IsTrue(GuidedGeometry.ShouldAutoReturn(0f, 8f, home + Vector3.right * 7f, 0f, home, 6f), "out of reach");
        }

        // ------------------------------------------------------------------ persistence

        [Test]
        public void ResultsRoundTripThroughTheDatabaseAndTheFile()
        {
            var folder = Path.Combine(Path.GetTempPath(), "btp_guided_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var progress = new SqliteProgressRepository(Path.Combine(folder, "progress.db"));
            try
            {
                Assert.IsTrue(progress.IsReady, progress.Error);
                SpecimenTreatmentResults.StoreOverride = progress;
                GuidedTreatmentResults.ClearMemory();
                var record = new GuidedTreatmentRecord
                {
                    specimenId = "tomato/early_blight#02", plantId = "tomato", diseaseId = "early_blight", plantName = "Tomato", diseaseName = "Early blight",
                    dayTemperature = 24.5f, nightTemperature = 18f, hasNight = true, product = "NPK 19-19-19", scoops = 2, sprays = 3, waterAmount = -21f,
                    rootZoneAfter = 60f,
                };
                Assert.AreEqual("database", GuidedTreatmentResults.Save(record));
                Assert.IsTrue(SpecimenTreatmentResults.Save(new SpecimenTreatmentRecord
                    { specimenId = record.specimenId, plantId = "tomato", diseaseId = "early_blight", passed = true, score = 1f, diseaseAmount = 0f }));

                GuidedTreatmentResults.ClearMemory();
                var loaded = GuidedTreatmentResults.Load(record.specimenId);
                Assert.IsNotNull(loaded, "read back from the database");
                Assert.AreEqual(2, loaded.scoops);
                Assert.AreEqual(3, loaded.sprays);
                Assert.AreEqual("Temperature: 24.5°C day / 18°C night", loaded.Lines()[0]);
                Assert.AreEqual("Fertiliser: 2 scoops of NPK 19-19-19", loaded.Lines()[1]);
                Assert.AreEqual("Water: −21% (root zone 60%)", loaded.Lines()[3]);
                CollectionAssert.AreEqual(new[] { record.specimenId }, GuidedTreatmentResults.Pending());
                Assert.AreEqual(0f, SpecimenTreatmentResults.Load(record.specimenId).diseaseAmount, "the Farm plant shows healthy");
                Assert.IsNull(GuidedTreatmentResults.Load("tomato/early_blight#03"), "other plants keep their own state");

                GuidedTreatmentResults.Acknowledge(record.specimenId);
                GuidedTreatmentResults.ClearMemory();
                Assert.AreEqual(0, GuidedTreatmentResults.Pending().Count, "the recap is shown once");

                // Without a database: a JSON file in persistentDataPath (here the temp folder).
                SpecimenTreatmentResults.StoreOverride = null;
                GuidedTreatmentResults.FileOverride = Path.Combine(folder, "guided.json");
                var other = new GuidedTreatmentRecord { specimenId = "rice/brown_spot#01", product = "K 0-0-50 (SOP)", scoops = 3 };
                if (TreatmentServices.Instance == null)
                    Assert.AreEqual("file", GuidedTreatmentResults.Save(other));
                else
                    GuidedTreatmentResults.Save(other);
                GuidedTreatmentResults.ClearMemory();
                if (TreatmentServices.Instance == null)
                {
                    Assert.AreEqual(3, GuidedTreatmentResults.Load("rice/brown_spot#01").scoops);
                    CollectionAssert.Contains(GuidedTreatmentResults.Pending(), "rice/brown_spot#01");
                }
            }
            finally
            {
                SpecimenTreatmentResults.StoreOverride = null;
                GuidedTreatmentResults.FileOverride = null;
                GuidedTreatmentResults.ClearMemory();
                progress.Dispose();
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
