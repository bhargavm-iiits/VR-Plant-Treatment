using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTP.Plants;
using BTP.Treatment;
using NUnit.Framework;
using SQLite;

namespace BTP.Tests
{
    /// <summary>
    /// Treatment content and lesson-engine tests against the real SQL source, loaded into an
    /// in-memory SQLite database (the same queries the Treatment lab runs).
    /// </summary>
    public sealed class TreatmentTests
    {
        const string SqlPath = "Assets/_Project/Data/Treatment/treatment_content.sql";

        static string sql;
        SqliteTreatmentRepository repository;

        static SQLiteConnection OpenContent(string script)
        {
            var db = new SQLiteConnection(":memory:");
            db.ExecuteScript(script);
            return db;
        }

        [OneTimeSetUp]
        public void LoadSql() => sql = File.ReadAllText(SqlPath);

        [SetUp]
        public void Open()
        {
            repository = new SqliteTreatmentRepository(() => OpenContent(sql));
            repository.InitializeAsync().Wait();
            Assert.IsTrue(repository.IsReady, repository.Error);
        }

        [TearDown]
        public void Close() => repository.Dispose();

        TreatmentCase Load(string plant, string disease)
        {
            var c = repository.GetCase(new CaseKey(plant, disease), out var error);
            Assert.IsNotNull(c, error);
            return c;
        }

        List<string> Validate(TreatmentCase c) => TreatmentCaseValidator.Validate(c, k => repository.GetTool(k) != null, null);

        /// <summary>Plays a lesson the way a learner would: correct tool on an uncounted target of the current step.</summary>
        static void Play(TreatmentSession session, int routeChoice = 0)
        {
            for (var guard = 0; guard < 100 && session.CurrentStep != null; guard++)
            {
                var step = session.CurrentStep;
                ActionResult result;
                if (step.Action == TreatmentActions.Choose)
                {
                    var correct = step.Options.Where(o => o.Correct).ToList();
                    result = session.TryPerform(ActionContext.Choose(correct[System.Math.Min(routeChoice, correct.Count - 1)].OptionId));
                }
                else if (step.Action == TreatmentActions.Advance)
                {
                    result = session.TryPerform(ActionContext.Advance());
                }
                else
                {
                    session.Progress.StepTargets.TryGetValue(step.StepId, out var counted);
                    var target = session.Case.SceneTargets().First(t => t.Tag == step.TargetTag && session.IsTargetVisible(t) &&
                                                                       (counted == null || !counted.Contains(t.TargetId)));
                    result = session.TryPerform(ActionContext.OnTarget(step.ToolKey, target.TargetId, target.Tag));
                }

                Assert.IsTrue(result.Accepted, $"{session.Case.Key} step {step.StepId} was refused: {result.Message}");
            }

            Assert.IsTrue(session.Progress.Completed, $"{session.Case.Key} did not finish.");
        }

        [Test]
        public void EveryCanonicalPairResolvesToItsOwnValidRecord()
        {
            Assert.AreEqual(24, DiseaseClasses.All.Count);
            Assert.AreEqual(58, DiseaseClasses.AllCases.Count);
            Assert.AreEqual(58, repository.ListCases().Count);
            foreach (var info in DiseaseClasses.AllCases)
            {
                var c = Load(info.PlantId, info.DiseaseId);
                Assert.AreEqual(info.PlantId, c.Key.PlantId);
                Assert.AreEqual(info.DiseaseId, c.Key.DiseaseId);
                CollectionAssert.IsEmpty(Validate(c), $"{c.Key}: {string.Join("; ", Validate(c))}");
                Assert.IsNotEmpty(c.Evidence);
                Assert.IsTrue(c.Methods.All(m => !string.IsNullOrEmpty(m.Limitations)));
                Assert.IsTrue(c.Steps.Any(s => s.Action == TreatmentActions.Observe));
                Assert.AreNotEqual(TreatmentOutcome.None, c.Outcome);
            }
        }

        [Test]
        public void SharedDiseaseIdsDoNotCollideAcrossPlants()
        {
            var apple = Load("apple", "black_rot");
            var grape = Load("grape", "black_rot");
            Assert.AreEqual("orchard", apple.EnvironmentKey);
            Assert.AreEqual("vineyard", grape.EnvironmentKey);
            Assert.AreNotEqual(apple.Scenario, grape.Scenario);
            Assert.AreNotEqual(Load("tomato", "early_blight").EnvironmentKey, Load("potato", "early_blight").EnvironmentKey);
        }

        [Test]
        public void EveryLessonCanBeCompletedOnEveryValidRoute()
        {
            foreach (var info in DiseaseClasses.AllCases)
            {
                var c = Load(info.PlantId, info.DiseaseId);
                var routes = c.Steps.SelectMany(s => s.Options).Where(o => o.Correct && o.Route != null).Select(o => o.Route).Distinct().Count();
                for (var route = 0; route < System.Math.Max(1, routes); route++)
                {
                    var session = new TreatmentSession(c, new CaseProgress(c.Key, c.ContentVersion));
                    Play(session, route);
                    Assert.AreEqual(c.Outcome, session.Progress.Outcome);
                    Assert.AreEqual(0, session.CarriedTargets.Count, $"{c.Key} finished while still carrying removed material.");
                }
            }
        }

        [Test]
        public void WrongToolTargetAndPrematureActionsAreExplainedWithoutProgress()
        {
            var c = Load("tomato", "early_blight");
            var session = new TreatmentSession(c, new CaseProgress(c.Key, c.ContentVersion));
            var lesion = c.Targets.First(t => t.Tag == "lesion_spot");
            var healthy = c.Targets.First(t => t.Tag == "healthy_tissue");

            var wrongTool = session.TryPerform(ActionContext.OnTarget("shears", lesion.TargetId, lesion.Tag));
            var wrongTarget = session.TryPerform(ActionContext.OnTarget("magnifier", healthy.TargetId, healthy.Tag));
            var premature = session.TryPerform(ActionContext.OnTarget(null, "fx_disposal", "disposal_bin"));
            var wrongChoice = session.TryPerform(ActionContext.Advance());

            foreach (var result in new[] { wrongTool, wrongTarget, premature, wrongChoice })
            {
                Assert.IsFalse(result.Accepted);
                Assert.IsNotEmpty(result.Message);
            }

            Assert.AreEqual("Use the magnifier for this task.", wrongTool.Message);
            Assert.AreEqual(0, session.CompletedSteps);
            Assert.IsEmpty(session.Progress.TargetStates);
            Assert.IsEmpty(session.Progress.StepTargets.SelectMany(p => p.Value));
        }

        [Test]
        public void ContaminatedToolsMustBeCleanedAndDisposalNeedsRemovedMaterial()
        {
            var c = Load("tomato", "target_spot");
            var session = new TreatmentSession(c, new CaseProgress(c.Key, c.ContentVersion));
            while (session.CurrentStep.StepId != "clean_tools")
            {
                var before = session.CurrentStep;
                Play1(session);
                Assert.AreNotSame(before, session.CurrentStep);
            }

            Assert.IsTrue(session.IsToolContaminated("shears"));
            Play1(session);
            Assert.IsFalse(session.IsToolContaminated("shears"));
        }

        static void Play1(TreatmentSession session)
        {
            var step = session.CurrentStep;
            while (session.CurrentStep == step)
            {
                if (step.Action == TreatmentActions.Choose)
                    session.TryPerform(ActionContext.Choose(step.Options.First(o => o.Correct).OptionId));
                else if (step.Action == TreatmentActions.Advance)
                    session.TryPerform(ActionContext.Advance());
                else
                {
                    session.Progress.StepTargets.TryGetValue(step.StepId, out var counted);
                    var target = session.Case.SceneTargets().First(t => t.Tag == step.TargetTag && session.IsTargetVisible(t) &&
                                                                       (counted == null || !counted.Contains(t.TargetId)));
                    Assert.IsTrue(session.TryPerform(ActionContext.OnTarget(step.ToolKey, target.TargetId, target.Tag)).Accepted);
                }
            }
        }

        [Test]
        public void MildCommonRustNeverRequiresSprayingAndCornIsNeverStripped()
        {
            var rust = Load("corn", "common_rust");
            var session = new TreatmentSession(rust, new CaseProgress(rust.Key, rust.ContentVersion));
            Play(session);
            Assert.IsFalse(session.ApplicableSteps.Any(s => s.Action == TreatmentActions.Spray));

            foreach (var disease in new[] { "gray_leaf_spot", "common_rust", "northern_leaf_blight" })
            {
                var corn = Load("corn", disease);
                Assert.IsFalse(corn.Steps.Any(s => s.Action == TreatmentActions.Cut || s.Action == TreatmentActions.Collect),
                    $"corn/{disease} rewards removing foliage.");
            }
        }

        [Test]
        public void VirusAndSevereEscaEndInADistinctReplacement()
        {
            foreach (var (plant, disease) in new[] { ("tomato", "yellow_leaf_curl_virus"), ("tomato", "mosaic_virus"), ("grape", "esca") })
            {
                var c = Load(plant, disease);
                Assert.AreEqual(TreatmentOutcome.Replaced, c.Outcome);
                var session = new TreatmentSession(c, new CaseProgress(c.Key, c.ContentVersion));
                Play(session);
                var original = c.Targets.First(t => t.Tag == "infected_plant");
                var replacement = c.Targets.First(t => t.Tag == "replacement");
                Assert.AreEqual("removed_plant", session.Progress.GetTargetState(original.TargetId));
                Assert.IsTrue(session.IsTargetVisible(replacement));
                Assert.IsFalse(c.Steps.Any(s => s.TargetTag == "infected_plant" && s.Action == TreatmentActions.Spray));
            }
        }

        [Test]
        public void LateBlightIsTaughtAsAWaterMould()
        {
            foreach (var plant in new[] { "tomato", "potato" })
            {
                var c = Load(plant, "late_blight");
                StringAssert.Contains("Oomycete", c.Category);
                Assert.IsTrue(c.Steps.SelectMany(s => s.Options).Any(o => !o.Correct &&
                    (o.Feedback.ToLowerInvariant().Contains("ordinary fungicides") || o.Feedback.ToLowerInvariant().Contains("generic fungal"))));
            }
        }

        [Test]
        public void MissingRecordOrInvalidRowsBlockOnlyThatLesson()
        {
            var broken = sql + "\nDELETE FROM cases WHERE plant_id = 'peach';\nDELETE FROM evidence WHERE plant_id = 'cherry';";
            using (var repo = new SqliteTreatmentRepository(() => OpenContent(broken)))
            {
                repo.InitializeAsync().Wait();
                Assert.IsNull(repo.GetCase(new CaseKey("peach", "bacterial_spot"), out var error));
                StringAssert.Contains("No treatment record", error);
                var cherry = repo.GetCase(new CaseKey("cherry", "powdery_mildew"), out _);
                Assert.IsNotEmpty(TreatmentCaseValidator.Validate(cherry, k => repo.GetTool(k) != null, null));
                var tomato = repo.GetCase(new CaseKey("tomato", "early_blight"), out _);
                CollectionAssert.IsEmpty(TreatmentCaseValidator.Validate(tomato, k => repo.GetTool(k) != null, null));
            }
        }

        [Test]
        public void EditingADatabaseInstructionChangesTheLesson()
        {
            var edited = sql + "\nUPDATE steps SET instruction = 'Edited in SQL only' WHERE plant_id = 'tomato' AND disease_id = 'early_blight' AND step_id = 'inspect';";
            using (var repo = new SqliteTreatmentRepository(() => OpenContent(edited)))
            {
                repo.InitializeAsync().Wait();
                var c = repo.GetCase(new CaseKey("tomato", "early_blight"), out _);
                Assert.AreEqual("Edited in SQL only", c.FindStep("inspect").Instruction);
            }
        }

        [Test]
        public void ProgressRoundTripsAndResetAffectsOnlyOneCase()
        {
            using (var progress = new SqliteProgressRepository(":memory:"))
            {
                Assert.IsTrue(progress.IsReady, progress.Error);
                var a = Load("tomato", "early_blight");
                var b = Load("tomato", "leaf_mold");
                var sa = new TreatmentSession(a, progress.Load(a.Key, a.ContentVersion));
                Play(sa);
                var sb = new TreatmentSession(b, progress.Load(b.Key, b.ContentVersion));
                Play1(sb);
                Assert.IsTrue(progress.Save(sa.Progress));
                Assert.IsTrue(progress.Save(sb.Progress));

                var restored = progress.Load(a.Key, a.ContentVersion);
                Assert.IsTrue(restored.Completed);
                Assert.AreEqual(TreatmentOutcome.Managed, restored.Outcome);
                CollectionAssert.AreEquivalent(sa.Progress.CompletedSteps, restored.CompletedSteps);
                CollectionAssert.AreEquivalent(sa.Progress.Observations, restored.Observations);

                Assert.IsTrue(progress.Reset(a.Key));
                Assert.IsFalse(progress.Load(a.Key, a.ContentVersion).HasStarted);
                Assert.IsTrue(progress.Load(b.Key, b.ContentVersion).HasStarted);

                progress.SetSetting("last_case", b.Key.ToString());
                Assert.AreEqual("tomato/leaf_mold", progress.GetSetting("last_case"));
            }
        }

        [Test]
        public void CaseKeyParsesOnlyPlantSlashDisease()
        {
            Assert.IsTrue(CaseKey.TryParse("grape/black_rot", out var key));
            Assert.AreEqual(new CaseKey("grape", "black_rot"), key);
            Assert.IsFalse(CaseKey.TryParse("black_rot", out _));
            Assert.IsFalse(CaseKey.TryParse("a/b/c", out _));
            Assert.IsFalse(CaseKey.TryParse("/x", out _));
        }
    }
}