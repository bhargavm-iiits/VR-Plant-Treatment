using System.Collections.Generic;
using System.IO;
using BTP.Core;
using BTP.Treatment;
using NUnit.Framework;
using UnityEngine;

namespace BTP.Tests
{
    /// <summary>
    /// The specimen chamber's pure parts: chamber fitting, the JSON reader, profile and evaluation parsing on real backend
    /// responses, settings stepping and the strict evaluation body, the race guard, the fertiliser packet label, the
    /// granules, the soil-moisture previews and the per-specimen records.
    /// </summary>
    public sealed class TreatmentChamberTests
    {
        static string Fixture(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Tests/EditMode/Fixtures", name + ".json"));

        static TreatmentProfile Tomato()
        {
            var profile = TreatmentProfile.Parse(Fixture("treatment_profile_tomato_early_blight"), out var error);
            Assert.IsNotNull(profile, error);
            return profile;
        }

        static Bounds Plant(float w, float h, float d, Vector2 offset = default) =>
            new Bounds(new Vector3(offset.x, h * 0.5f, offset.y), new Vector3(w, h, d));

        // ------------------------------------------------------------------ ChamberFit

        [TestCase(1.3f, 1.75f, 1.3f, ChamberKind.Tall)]   // tomato
        [TestCase(0.81f, 2.2f, 0.92f, ChamberKind.Tall)]  // corn
        [TestCase(4.05f, 4.0f, 3.5f, ChamberKind.WalkIn)] // apple
        [TestCase(0.34f, 0.3f, 0.33f, ChamberKind.Bench)] // strawberry
        [TestCase(0.37f, 0.5f, 0.35f, ChamberKind.Bench)] // potato
        [TestCase(0.85f, 1.07f, 0.88f, ChamberKind.Tall)] // rice
        public void FitClassifiesFarmPlants(float w, float h, float d, ChamberKind kind) => Assert.AreEqual(kind, ChamberFit.Fit(Plant(w, h, d)).Kind);

        [TestCase(1.3f, 1.75f, 1.3f)]
        [TestCase(0.81f, 2.2f, 0.92f)]
        [TestCase(4.05f, 4.0f, 3.5f)]
        [TestCase(0.34f, 0.3f, 0.33f)]
        public void FitKeepsClearanceOnEverySideAndAbove(float w, float h, float d)
        {
            var plant = Plant(w, h, d, new Vector2(0.12f, -0.07f));
            var layout = ChamberFit.Fit(plant);
            var interior = layout.InteriorBounds;
            var anchor = layout.SpecimenAnchor;
            var placed = new Bounds(plant.center + anchor, plant.size);
            Assert.GreaterOrEqual(placed.min.x - interior.min.x, layout.SideClearance - 1e-4f);
            Assert.GreaterOrEqual(interior.max.x - placed.max.x, layout.SideClearance - 1e-4f);
            Assert.GreaterOrEqual(placed.min.z - interior.min.z, layout.SideClearance - 1e-4f);
            Assert.GreaterOrEqual(interior.max.z - placed.max.z, layout.SideClearance - 1e-4f);
            Assert.GreaterOrEqual(interior.max.y - placed.max.y, layout.TopClearance - 1e-4f);
            Assert.AreEqual(layout.FloorHeight + layout.SoilSurface, anchor.y, 1e-5f, "stem base on the soil surface");
            // The tray is centred under the stem and inside the interior.
            Assert.LessOrEqual(Mathf.Abs(layout.PlantPivot.x) + layout.TraySize.x * 0.5f, layout.Interior.x * 0.5f + 1e-4f);
            Assert.LessOrEqual(Mathf.Abs(layout.PlantPivot.y) + layout.TraySize.y * 0.5f, layout.Interior.z * 0.5f + 1e-4f);
            Assert.AreEqual(layout.SoilSurface, layout.PedestalHeight + layout.TrayHeight, 1e-4f);
            Assert.Greater(layout.HatchTop, layout.SoilSurface);
            Assert.Less(layout.HatchTop, layout.Interior.y);
        }

        [Test]
        public void FitCentresThePlantAndOffsetsThePivot()
        {
            var layout = ChamberFit.Fit(Plant(1f, 1.5f, 1f, new Vector2(0.2f, 0.1f)));
            Assert.AreEqual(-0.2f, layout.PlantPivot.x, 1e-5f);
            Assert.AreEqual(-0.1f, layout.PlantPivot.y, 1e-5f);
        }

        [Test]
        public void FrameAndHandleAreConstantAndWalkInFitsAPerson()
        {
            Assert.AreEqual(0.045f, ChamberFit.FrameThickness);
            Assert.AreEqual(0.30f, ChamberFit.HandleLength);
            var small = ChamberFit.Fit(Plant(0.3f, 0.3f, 0.3f));
            var big = ChamberFit.Fit(Plant(4.9f, 3.8f, 4.6f));
            Assert.AreEqual(small.Exterior.x - small.Interior.x - 2f * ChamberFit.BenchOverhang, 2f * ChamberFit.FrameThickness, 1e-4f);
            Assert.AreEqual(big.Exterior.x - big.Interior.x, 2f * ChamberFit.FrameThickness, 1e-4f);
            Assert.GreaterOrEqual(big.Interior.y, ChamberFit.WalkInMinHeight);
            Assert.AreEqual(ChamberFit.BenchTop + ChamberFit.BenchPlinth, small.FloorHeight, 1e-5f);
            Assert.AreEqual(ChamberFit.FloorPlinth, big.FloorHeight, 1e-5f);
        }

        [Test]
        public void ReserveCoversTheLargestFarmTree()
        {
            // Mango, the widest Farm plant, at its largest Farm scale and turned 45 degrees (diagonal footprint).
            var exterior = ChamberFit.Fit(Plant(5.9f * 1.02f, 3.8f * 1.02f, 5.9f * 1.02f)).Exterior;
            var reserve = ChamberFit.ReservedExterior;
            Assert.LessOrEqual(exterior.x, reserve.x);
            Assert.LessOrEqual(exterior.y, reserve.y);
            Assert.LessOrEqual(exterior.z, reserve.z);
        }

        // ------------------------------------------------------------------ JSON and profile

        [Test]
        public void SimpleJsonReadsMixedValues()
        {
            Assert.IsTrue(SimpleJson.TryParse("{\"a\":[1,2.5,-3e2],\"b\":\"x\\u00b0\\\"\",\"c\":null,\"d\":true,\"e\":{}}", out var value, out var error), error);
            var root = SimpleJson.Object(value);
            Assert.AreEqual(-300d, (double)SimpleJson.Array(root["a"])[2]);
            Assert.AreEqual("x°\"", root["b"]);
            Assert.IsTrue(SimpleJson.IsNull(root, "c"));
            Assert.AreEqual(true, root["d"]);
            Assert.IsFalse(SimpleJson.TryParse("{\"a\":1,}", out _, out _));
            Assert.IsFalse(SimpleJson.TryParse("[1 2]", out _, out _));
            Assert.AreEqual("25", SimpleJson.Number(25.0));
            Assert.AreEqual("22.5", SimpleJson.Number(22.5));
            Assert.AreEqual("\"a\\nb\"", SimpleJson.Quote("a\nb"));
        }

        [Test]
        public void ProfileParsesTheRealResponseInOrder()
        {
            var profile = Tomato();
            Assert.AreEqual("tomato", profile.PlantId);
            Assert.AreEqual(1, profile.ProfileVersion);
            Assert.AreEqual("standard", profile.Scenario);
            Assert.AreEqual(70d, profile.LeafWetnessStart);
            CollectionAssert.AreEqual(new[] { "temperature", "fertiliser", "spray", "soilMoisture" }, profile.Factors.ConvertAll(f => f.Id));
            var day = profile.Find("temperature", "day");
            Assert.AreEqual(30d, day.Start);
            Assert.AreEqual(22d, profile.Find("temperature", "night").Start);
            Assert.AreEqual(0.5d, day.Step);
            Assert.AreEqual("30.0°C", day.Format(day.Start));
            Assert.AreEqual("Target 22–27°C", day.TargetText);
            Assert.AreEqual("Step 0.5°C", day.StepText);
            var irrigation = profile.Find("soilMoisture", "irrigation");
            Assert.IsTrue(irrigation.IsChoice);
            Assert.AreEqual("hold", irrigation.StartChoice);
            Assert.AreEqual(3, irrigation.Options.Count);
            CollectionAssert.AreEqual(new[] { "rootZone" }, irrigation.Targets);
            Assert.AreEqual("Root-zone", irrigation.ShortLabel("rootZone"));
            Assert.AreEqual("Overhead watering", irrigation.OptionLabel("overhead"));
            Assert.IsNull(profile.Nutrient);
            Assert.IsTrue(profile.Find("spray").Indicated);
            Assert.AreEqual("3 training units", profile.Find("fertiliser", "dose").Format(3));
            Assert.AreEqual("1 application", profile.Find("fertiliser", "applications").Format(1));
            Assert.AreEqual("2 applications", profile.Find("fertiliser", "applications").Format(2));
        }

        [Test]
        public void ProfileRejectsAWrongShape()
        {
            var json = Fixture("treatment_profile_tomato_early_blight").Replace("\"id\": \"spray\"", "\"id\": \"mist\"");
            Assert.IsNull(TreatmentProfile.Parse(json, out var error));
            StringAssert.Contains("mist", error);
        }

        static TreatmentProfile WithNutrient(string nutrient) =>
            TreatmentProfile.Parse(Fixture("treatment_profile_tomato_early_blight").Replace("\"nutrient\": null", $"\"nutrient\": \"{nutrient}\""), out _);

        [Test]
        public void PacketLabelComesFromTheProfileNutrient()
        {
            Assert.AreEqual("NITROGEN", TreatmentProfile.PacketLabel(WithNutrient("Nitrogen").Nutrient));
            Assert.AreEqual("PHOSPHORUS", TreatmentProfile.PacketLabel(WithNutrient("Phosphorus").Nutrient));
            Assert.AreEqual("POTASSIUM", TreatmentProfile.PacketLabel(WithNutrient("Potassium").Nutrient));
            Assert.IsNull(TreatmentProfile.PacketLabel(Tomato().Nutrient), "no confirmed deficiency: no packet");
            Assert.AreEqual("Severe scenario", TreatmentProfile.ScenarioLine("severe"));
            Assert.IsNull(TreatmentProfile.ScenarioLine("standard"));
        }

        // ------------------------------------------------------------------ settings

        [Test]
        public void SettingsStepOnTheGridAndClamp()
        {
            var profile = Tomato();
            var settings = TreatmentSettings.FromStart(profile);
            Assert.IsTrue(settings.Step(profile, "temperature", "day", -1));
            Assert.AreEqual(29.5d, settings.Get("temperature", "day"), 1e-9);
            settings.Set(profile.Find("temperature", "day"), "temperature", 22.26);
            Assert.AreEqual(22.5d, settings.Get("temperature", "day"), 1e-9, "snapped to min + k * step");
            settings.Set(profile.Find("temperature", "day"), "temperature", 99);
            Assert.AreEqual(40d, settings.Get("temperature", "day"));
            Assert.IsFalse(settings.CanStep(profile, "temperature", "day", +1));
            Assert.IsFalse(settings.Step(profile, "temperature", "day", +1));
            Assert.IsFalse(settings.CanStep(profile, "fertiliser", "dose", -1));
            settings.Set(profile.Find("spray", "targetCoverage"), "spray", 83);
            Assert.AreEqual(85d, settings.Get("spray", "targetCoverage"));
            Assert.IsTrue(settings.Cycle(profile, "soilMoisture", "irrigation", +1));
            Assert.AreEqual("rootZone", settings.GetChoice("soilMoisture", "irrigation"));
            settings.Cycle(profile, "soilMoisture", "irrigation", -1);
            settings.Cycle(profile, "soilMoisture", "irrigation", -1);
            Assert.AreEqual("overhead", settings.GetChoice("soilMoisture", "irrigation"), "wraps around");
            var copy = settings.Clone();
            Assert.IsTrue(copy.SameAs(settings));
            copy.Step(profile, "fertiliser", "applications", +1);
            Assert.IsFalse(copy.SameAs(settings));
        }

        [Test]
        public void EvaluationBodyMatchesTheStrictSchema()
        {
            var profile = Tomato();
            var settings = TreatmentSettings.FromStart(profile);
            settings.Set(profile.Find("fertiliser", "applications"), "fertiliser", 2);
            settings.Set(profile.Find("temperature", "day"), "temperature", 24.5);
            var json = settings.ToEvaluationJson(profile);
            Assert.AreEqual("{\"profileVersion\":1,\"values\":{\"temperature\":{\"day\":24.5,\"night\":22},\"fertiliser\":{\"dose\":0,\"applications\":2}," +
                            "\"spray\":{\"dose\":0,\"targetCoverage\":0},\"soilMoisture\":{\"rootZone\":81,\"irrigation\":\"hold\"}}}", json);
            var back = TreatmentSettings.FromEvaluationJson(profile, json);
            Assert.IsTrue(back.SameAs(settings));
        }

        [Test]
        public void EvaluationParsesTheRealResponse()
        {
            var result = TreatmentEvaluation.Parse(Fixture("treatment_evaluation_tomato_early_blight"), out var error);
            Assert.IsNotNull(result, error);
            Assert.AreEqual(0.531d, result.Score, 1e-9);
            Assert.IsFalse(result.Passed);
            Assert.AreEqual("Not yet on target", result.Headline);
            Assert.AreEqual(50d, result.LeafWetness);
            Assert.AreEqual(81d, result.RootZoneMoisture);
            Assert.AreEqual(1d, result.FactorScore("fertiliser"));
            Assert.AreEqual(8, result.Rows.Count);
            Assert.AreEqual("hold", result.Find("soilMoisture", "irrigation").Choice);
            Assert.IsFalse(result.Find("temperature", "day").OnTarget);
            StringAssert.Contains("above", result.Find("temperature", "day").Feedback);
        }

        [Test]
        public void CasePathsEscapeSegmentsAndCarryTheScenario()
        {
            Assert.AreEqual("/api/treatment/cases/rice/brown_spot?scenario=severe", TreatmentCaseClient.CasePath("rice", "brown_spot", "severe"));
            Assert.AreEqual("/api/treatment/cases/a%20b/c%2Fd/evaluate?scenario=standard", TreatmentCaseClient.EvaluatePath("a b", "c/d", null));
            Assert.IsNull(TreatmentCaseClient.RefuseUrl("http://127.0.0.1:8000"));
            Assert.IsNull(TreatmentCaseClient.RefuseUrl("https://api.example.org"));
            Assert.IsNotNull(TreatmentCaseClient.RefuseUrl("http://api.example.org"));
        }

        // ------------------------------------------------------------------ race guard

        [Test]
        public void SessionDropsLateResponsesAndGatesTheButtons()
        {
            var session = new ChamberCaseSession();
            var rice = session.Begin("rice/brown_spot#01", "rice", "brown_spot", "standard");
            session.SpecimenShown(rice);
            session.BeginLoad(rice);
            Assert.IsFalse(session.CanApply, "loading");
            var tomato = session.Begin("tomato/early_blight#01", "tomato", "early_blight", "standard");
            session.SpecimenShown(tomato);
            Assert.IsFalse(session.AcceptProfile(rice, Tomato()), "late response for the earlier selection");
            Assert.IsFalse(session.SpecimenShown(rice));
            Assert.IsFalse(session.CanApply, "no profile yet");
            Assert.IsTrue(session.AcceptProfile(tomato, Tomato()));
            Assert.IsTrue(session.CanApply);
            Assert.IsFalse(session.CanCheck, "Check Health only after Apply");
            var settings = TreatmentSettings.FromStart(session.Profile);
            Assert.IsTrue(session.Apply(settings));
            settings.Step(session.Profile, "temperature", "day", -1);
            Assert.IsFalse(session.Applied.SameAs(settings), "Apply captured a copy");
            var check = session.BeginCheck();
            Assert.IsNotNull(check);
            Assert.IsFalse(session.CanApply, "checking");
            var other = session.Begin("tomato/early_blight#03", "tomato", "early_blight", "standard");
            Assert.IsFalse(session.AcceptEvaluation(check), "late evaluation after a switch");
            session.SpecimenShown(other);
            Assert.IsTrue(session.AcceptProfile(other, Tomato()));
            Assert.IsNull(session.Applied, "the new specimen starts without applied settings");
        }

        [Test]
        public void SessionRefusesAProfileOfAnotherCase()
        {
            var session = new ChamberCaseSession();
            var ticket = session.Begin("corn/common_rust#01", "corn", "common_rust", "standard");
            session.SpecimenShown(ticket);
            Assert.IsFalse(session.AcceptProfile(ticket, Tomato()));
            Assert.IsFalse(session.CanApply);
        }

        // ------------------------------------------------------------------ granules and soil

        static GranuleSimulation.Area Area() => new GranuleSimulation.Area
        {
            SoilY = 0.7f, Min = new Vector2(-0.3f, -0.2f), Max = new Vector2(0.3f, 0.2f), Stem = Vector2.zero, StemClearance = 0.05f, BandInner = 0.06f,
            BandOuter = 0.18f, BandDirection = new Vector2(-1f, -0.5f),
        };

        [Test]
        public void GranulesSettleBesideTheStemInsideTheTray()
        {
            Assert.AreEqual(48, GranuleSimulation.CountFor(3, 2));
            Assert.AreEqual(0, GranuleSimulation.CountFor(0, 2));
            Assert.AreEqual(0, GranuleSimulation.CountFor(3, 0));
            Assert.AreEqual(GranuleSimulation.MaxCount, GranuleSimulation.CountFor(10, 3));
            var area = Area();
            var sim = new GranuleSimulation(48, 7, new Vector3(-0.1f, 0.8f, -0.06f), area);
            Assert.IsTrue(sim.RunToEnd());
            var settled = new List<Vector3>();
            foreach (var g in sim.Granules)
            {
                Assert.AreEqual(area.SoilY + GranuleSimulation.Radius, g.Position.y, 1e-5f);
                Assert.That(g.Position.x, Is.InRange(area.Min.x, area.Max.x));
                Assert.That(g.Position.z, Is.InRange(area.Min.y, area.Max.y));
                Assert.GreaterOrEqual(new Vector2(g.Position.x, g.Position.z).magnitude, area.StemClearance - 1e-5f);
                settled.Add(g.Position);
            }

            sim.Step(10f);
            for (var i = 0; i < settled.Count; i++)
                Assert.AreEqual(settled[i], sim.Granules[i].Position, "settled granules stay");
            var again = new GranuleSimulation(48, 7, new Vector3(-0.1f, 0.8f, -0.06f), area);
            again.RunToEnd();
            Assert.AreEqual(sim.Granules[17].Position, again.Granules[17].Position, "deterministic");
        }

        [Test]
        public void SoilMoisturePreviewFollowsTheServerRules()
        {
            Assert.AreEqual(22d, SoilMoistureRules.PreviewLeafWetness(42, "rootZone"));
            Assert.AreEqual(22d, SoilMoistureRules.PreviewLeafWetness(42, "hold"));
            Assert.AreEqual(72d, SoilMoistureRules.PreviewLeafWetness(42, "overhead"));
            Assert.AreEqual(100d, SoilMoistureRules.PreviewLeafWetness(90, "overhead"));
            Assert.AreEqual(0d, SoilMoistureRules.PreviewLeafWetness(10, "rootZone"));
            Assert.AreEqual(52d, SoilMoistureRules.MeasuredRootZone(52, 72, "hold"));
            Assert.AreEqual(72d, SoilMoistureRules.MeasuredRootZone(52, 72, "rootZone"));
            Assert.AreEqual("Leaf wetness 22/100", SoilMoistureRules.LeafWetnessText(22));
            var profile = Tomato();
            var rootZone = profile.Find("soilMoisture", "rootZone");
            var irrigation = profile.Find("soilMoisture", "irrigation");
            Assert.AreEqual(SoilMoistureRules.Verdict.OnTarget, SoilMoistureRules.Status(rootZone, irrigation, 60, "rootZone"));
            Assert.AreEqual(SoilMoistureRules.Verdict.Above, SoilMoistureRules.Status(rootZone, irrigation, 81, "rootZone"));
            Assert.AreEqual(SoilMoistureRules.Verdict.Below, SoilMoistureRules.Status(rootZone, irrigation, 40, "rootZone"));
            Assert.AreEqual(SoilMoistureRules.Verdict.OverheadWetsLeaves, SoilMoistureRules.Status(rootZone, irrigation, 60, "overhead"));
            Assert.AreEqual(SoilMoistureRules.Verdict.WrongDelivery, SoilMoistureRules.Status(rootZone, irrigation, 60, "hold"));
        }

        // ------------------------------------------------------------------ records and selection

        [Test]
        public void ResultingDiseaseAmountAndRecordParsing()
        {
            Assert.AreEqual(0.15f, SpecimenTreatmentResults.ResultingDiseaseAmount(1f, 1f, true), 1e-5f);
            Assert.AreEqual(1f - 0.45f * 0.5f, SpecimenTreatmentResults.ResultingDiseaseAmount(1f, 0.5f, false), 1e-5f);
            Assert.AreEqual(0f, SpecimenTreatmentResults.ResultingDiseaseAmount(0f, 0.2f, false));
            var record = new SpecimenTreatmentRecord { specimenId = "tomato/early_blight#03", score = 0.9f, passed = true, outcomeLabel = "Managed" };
            var parsed = SpecimenTreatmentResults.Parse(JsonUtility.ToJson(record));
            Assert.AreEqual("tomato/early_blight#03", parsed.specimenId);
            Assert.AreEqual("Treatment: Managed (score 90%)", SpecimenTreatmentResults.Describe(parsed));
            Assert.IsNull(SpecimenTreatmentResults.Parse("not json"));
            Assert.AreEqual("specimen_result/tomato/early_blight#03", SpecimenTreatmentResults.Key(record.specimenId));
            Assert.AreEqual("specimen_settings/x", SpecimenSettingsStore.Key("x"));
            var settingsRecord = new SpecimenSettingsRecord { specimenId = "x", plantId = "tomato", diseaseId = "early_blight", scenario = "standard", profileVersion = 1 };
            Assert.IsTrue(SpecimenSettingsStore.Matches(SpecimenSettingsStore.Parse(JsonUtility.ToJson(settingsRecord)), Tomato()));
        }

        [Test]
        public void SpecimenIdsSeparatePlantsOfOneRow()
        {
            var a = new SpecimenRef(null, "tomato", "early_blight", 0);
            var b = new SpecimenRef(null, "tomato", "early_blight", 2);
            Assert.AreEqual("tomato/early_blight#01", a.SpecimenId);
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(b.IsCase("tomato", "early_blight"));
        }

        // ------------------------------------------------------------------ WATER station moisture model (Addendum 3)

        static RootZoneWateringModel RiceSevere() => new RootZoneWateringModel(52, 0, 100, 1, 64, 80);

        static void RunUntil(RootZoneWateringModel model, double value)
        {
            for (var i = 0; i < 10000 && model.Running && System.Math.Abs(model.Current - value) > 1e-6; i++)
                model.Tick(1d / 120d);
        }

        [Test]
        public void WateringAddsTheAmountPausesResumesAndStopsAtTheSetpoint()
        {
            var model = RiceSevere();
            Assert.AreEqual(RootZoneWateringModel.State.ChooseAmount, model.Status);
            Assert.IsFalse(model.On(), "nothing to add yet");
            Assert.IsFalse(model.Delivered);
            Assert.IsTrue(model.SetAmount(20));
            Assert.AreEqual(72d, model.Setpoint, "52 + 20");
            Assert.IsTrue(model.On());
            Assert.IsTrue(model.Watering);
            Assert.IsFalse(model.On(), "ON while running does not add again");
            Assert.IsFalse(model.StepAmount(+1), "the amount is locked while the water runs");
            model.Tick(2.5);
            Assert.AreEqual(62d, model.Current, 1e-9, "4 %/s for 2.5 s");
            model.Off();
            Assert.IsFalse(model.Running);
            Assert.AreEqual(62d, model.Current, 1e-9, "OFF keeps the water already added");
            Assert.AreEqual(RootZoneWateringModel.State.Paused, model.Status);
            Assert.AreEqual("Paused at 62%", model.StatusText(v => v.ToString("0") + "%"));
            model.Tick(5);
            Assert.AreEqual(62d, model.Current, 1e-9, "no water while paused");
            Assert.IsTrue(model.On(), "ON resumes");
            Assert.AreEqual(72d, model.Setpoint, "towards the same setpoint");
            var reached = false;
            for (var i = 0; i < 1000 && !reached; i++)
                reached = model.Tick(0.1);
            Assert.IsTrue(reached);
            Assert.AreEqual(72d, model.Current, 1e-12, "stops exactly at the setpoint");
            Assert.IsFalse(model.Running);
            Assert.AreEqual(0d, model.Amount);
            Assert.AreEqual(RootZoneWateringModel.State.ReachedOnTarget, model.Status);
            Assert.IsFalse(model.On(), "ON again adds nothing");
            Assert.AreEqual(RootZoneWateringModel.State.ChooseAmount, model.Status);
            Assert.AreEqual(72d, model.Current);
            Assert.AreEqual(72d, model.MeasuredForServer);
            Assert.AreEqual("rootZone", model.Irrigation);
            Assert.AreEqual(20d, model.Moved, 1e-9);
        }

        [Test]
        public void ChangingTheAmountWhilePausedStartsFromTheCurrentMoisture()
        {
            var model = RiceSevere();
            model.SetAmount(20);
            model.On();
            model.Tick(2.5);
            model.Off();
            Assert.IsTrue(model.SetAmount(20));
            Assert.AreEqual(82d, model.Setpoint, "62 + 20");
            Assert.IsTrue(model.StepAmount(+1));
            Assert.AreEqual(83d, model.Setpoint);
            model.SetAmount(100);
            Assert.AreEqual(38d, model.Amount, "kept inside the profile range");
            Assert.AreEqual(100d, model.Setpoint);
            model.SetAmount(-200);
            Assert.AreEqual(-62d, model.Amount);
            Assert.AreEqual(0d, model.Setpoint);
        }

        [Test]
        public void NegativeAmountsDrainATooWetSoil()
        {
            // Tomato early blight starts too wet: 81 against 53-67.
            var model = new RootZoneWateringModel(81, 0, 100, 1, 53, 67);
            Assert.IsTrue(model.SetAmount(-21));
            Assert.AreEqual(60d, model.Setpoint);
            Assert.AreEqual("Drain 21%", RootZoneWateringModel.AmountText(model.Amount, v => v.ToString("0") + "%"));
            Assert.IsTrue(model.On());
            Assert.IsTrue(model.Draining);
            Assert.AreEqual("Draining", model.StatusText(v => v + "%"));
            RunUntil(model, 60);
            Assert.AreEqual(60d, model.Current, 1e-9);
            Assert.IsTrue(model.InTarget);
            Assert.AreEqual(RootZoneWateringModel.State.ReachedOnTarget, model.Status);
            Assert.AreEqual("rootZone", model.Irrigation);
        }

        [Test]
        public void UntouchedSoilHoldsTheStartValue()
        {
            var model = new RootZoneWateringModel(59, 0, 100, 1, 53, 67);
            Assert.AreEqual("hold", model.Irrigation);
            Assert.AreEqual(59d, model.MeasuredForServer);
            Assert.IsTrue(model.InTarget);
            model.SetAmount(5);
            Assert.AreEqual("hold", model.Irrigation, "choosing an amount is not delivering it");
            var outside = new RootZoneWateringModel(52, 0, 100, 1, 64, 80);
            outside.SetAmount(5);
            outside.On();
            RunUntil(outside, 57);
            Assert.AreEqual(RootZoneWateringModel.State.ReachedOutsideTarget, outside.Status);
            var restored = RiceSevere();
            restored.Restore(62, 72, 20, true, 10);
            Assert.AreEqual(RootZoneWateringModel.State.Paused, restored.Status);
            Assert.IsTrue(restored.On());
            RunUntil(restored, 72);
            Assert.AreEqual(72d, restored.Current, 1e-9);
            var profileModel = RootZoneWateringModel.For(Tomato());
            Assert.AreEqual(81d, profileModel.Start);
            Assert.AreEqual(53d, profileModel.TargetMin);
        }

        // ------------------------------------------------------------------ Addendum 5: tracked hands

        [Test]
        public void PressGuardActsOncePerGesture()
        {
            var guard = new BTP.UI.PressGuard(0.25f);
            var on = new object();
            var off = new object();
            Assert.IsTrue(guard.TryPress(on, 10f), "poke");
            Assert.IsFalse(guard.TryPress(on, 10.05f), "the ray on the same button in the same gesture");
            Assert.IsTrue(guard.TryPress(off, 10.06f), "another button is independent");
            Assert.IsFalse(guard.TryPress(on, 10.249f));
            Assert.IsTrue(guard.TryPress(on, 10.3f), "a new gesture after the window");
            guard.Forget(on);
            Assert.IsTrue(guard.TryPress(on, 10.31f), "press-and-hold repeat is let through explicitly");
            Assert.IsFalse(guard.TryPress(null, 11f));
        }

        [Test]
        public void HandLossStopsToolsAtOnceAndShowsTheNoticeAfterASecond()
        {
            var tracker = new HandLossTracker();
            var lost = new List<int>();
            tracker.HandLost += lost.Add;
            tracker.Update(0.016f, false, false);
            Assert.IsFalse(tracker.ShowMessage, "controllers or desktop: hands never seen, nothing reported");
            tracker.Update(0.016f, true, true);
            tracker.Update(0.016f, true, false);
            CollectionAssert.AreEqual(new[] { 1 }, lost, "the right hand lost on that very update");
            tracker.Update(0.016f, false, false);
            CollectionAssert.AreEqual(new[] { 1, 0 }, lost);
            tracker.Update(0.9f, false, false);
            Assert.IsFalse(tracker.ShowMessage, "not before a second");
            tracker.Update(0.2f, false, false);
            Assert.IsTrue(tracker.ShowMessage);
            Assert.AreEqual("Hands out of view", HandLossTracker.Message);
            tracker.Update(0.016f, false, true);
            Assert.IsFalse(tracker.ShowMessage, "cleared when a hand is back");
        }

        [Test]
        public void ContinuousPourEmitsGranulesThatSettleInTheBand()
        {
            var area = Area();
            var sim = new GranuleSimulation(5, area);
            Assert.AreEqual(0, sim.Count);
            for (var i = 0; i < 30; i++)
            {
                sim.Emit(new Vector3(-0.12f, 0.8f, -0.05f), 2);
                sim.Step(1f / 60f);
            }

            Assert.AreEqual(60, sim.Count);
            Assert.IsTrue(sim.RunToEnd());
            foreach (var g in sim.Granules)
            {
                Assert.AreEqual(area.SoilY + GranuleSimulation.Radius, g.Position.y, 1e-5f);
                Assert.GreaterOrEqual(new Vector2(g.Position.x, g.Position.z).magnitude, area.StemClearance - 1e-5f);
            }
        }

        [Test]
        public void ProceduralRootsAreDeterministicAndInsideTheTray()
        {
            var settings = new ProceduralRoots.Settings { HalfWidth = 0.3f, Depth = 0.2f, Front = -0.2f, Layer = 0.018f, Vigour = 0.4f, Seed = 11 };
            var a = ProceduralRoots.Build(settings);
            var b = ProceduralRoots.Build(settings);
            try
            {
                Assert.Greater(a.vertexCount, 100);
                Assert.AreEqual(a.vertices[50], b.vertices[50]);
                Assert.LessOrEqual(a.bounds.max.y, 0.01f);
                Assert.GreaterOrEqual(a.bounds.min.y, -0.22f);
                Assert.LessOrEqual(a.bounds.max.x, 0.32f);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }
    }
}
