using System;
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
    /// Manual treatment mode tests against the real SQL source (loaded into in-memory SQLite) and a real
    /// progress database (":memory:" or a temporary file): profiles, − / + buttons, validation and rules,
    /// outcome-aware recovery, the timer, the session state machine, the v1 → v2 progress migration and
    /// session save / restore.
    /// </summary>
    public sealed class ManualTreatmentTests
    {
        const string SqlPath = "Assets/_Project/Data/Treatment/treatment_content.sql";
        static readonly string[] ReplacedCases = { "tomato/late_blight", "potato/late_blight", "tomato/yellow_leaf_curl_virus", "tomato/mosaic_virus", "grape/esca" };

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

        TreatmentCase Load(string key)
        {
            Assert.IsTrue(CaseKey.TryParse(key, out var k));
            return Load(k.PlantId, k.DiseaseId);
        }

        static Dictionary<string, ControlSelection> Values(ManualTreatmentProfile profile, Func<TreatmentControlDef, float> pick) =>
            profile.Controls.ToDictionary(c => c.ControlId, c => new ControlSelection { Value = pick(c), Touched = true });

        static float Middle(TreatmentControlDef c) => c.ValueAt(c.StepIndex((c.RecommendedMin + c.RecommendedMax) * 0.5f));

        /// <summary>Presses + / − until the control reaches <paramref name="value"/>.</summary>
        static void SetTo(TreatmentSessionManager session, TreatmentControlDef control, float value)
        {
            for (var guard = 0; guard < 100; guard++)
            {
                var current = session.Buttons.ValueOf(control.ControlId);
                if (session.Buttons.IsTouched(control.ControlId) && Math.Abs(current - value) < 1e-4f)
                    return;
                var result = value >= current ? session.Increase(control.ControlId) : session.Decrease(control.ControlId);
                Assert.IsTrue(result.Accepted, result.Message);
            }

            Assert.Fail($"Could not set {control.ControlId} to {value}.");
        }

        static TreatmentSessionManager NewSession(TreatmentCase c, long learner = 1, Func<DateTime> clock = null) =>
            new TreatmentSessionManager(c, TreatmentSessionManager.NewRecord(c, learner, DateTime.UtcNow), clock);

        /// <summary>Plays a whole session: diagnosis, set every control, apply, monitor, check, final, finish.</summary>
        static void PlaySession(TreatmentSessionManager session, Func<TreatmentControlDef, float> pick)
        {
            Assert.IsTrue(session.Start().Accepted);
            Assert.IsTrue(session.ConfirmDiagnosis().Accepted);
            foreach (var control in session.Profile.Controls)
                SetTo(session, control, pick(control));
            Assert.IsTrue(session.Apply().Accepted);
            Assert.IsTrue(session.ContinueToMonitoring().Accepted);
            Assert.IsTrue(session.CheckHealth(session.Target).Accepted);
            Assert.IsTrue(session.ProceedToFinal().Accepted);
            Assert.IsTrue(session.Finish().Accepted);
            Assert.AreEqual(ManualStage.Completed, session.Stage);
        }

        // ------------------------------------------------------------------ content

        [Test]
        public void All24CasesLoadAValidManualProfile()
        {
            // 24 Farm classes plus 34 Lab-only conditions, all with a manual profile.
            Assert.AreEqual(24, DiseaseClasses.All.Count);
            Assert.AreEqual(58, DiseaseClasses.AllCases.Count);
            foreach (var info in DiseaseClasses.AllCases)
            {
                var c = Load(info.PlantId, info.DiseaseId);
                Assert.IsNotNull(c.Manual, $"{c.Key}: {c.ManualError}");
                Assert.AreEqual(c.Key, c.Manual.Key);
                var problems = TreatmentCaseValidator.ValidateManual(c);
                CollectionAssert.IsEmpty(problems, $"{c.Key}: {string.Join("; ", problems)}");
                Assert.LessOrEqual(c.Manual.Controls.Count, 5, $"{c.Key} has more controls than the adjustment panel shows.");
                // The step lesson is unaffected by the manual data.
                CollectionAssert.IsEmpty(TreatmentCaseValidator.Validate(c, k => repository.GetTool(k) != null, null));
                foreach (var control in c.Manual.Controls)
                    Assert.IsTrue(c.Methods.Any(m => m.MethodId == control.MethodId), $"{c.Key}/{control.ControlId} is not derived from a method row.");
            }
        }

        [Test]
        public void CropToDiseaseListingComesFromTheDatabase()
        {
            var data = new TreatmentDataManager(repository);
            var crops = data.ListCrops();
            Assert.AreEqual(19, crops.Count);
            Assert.AreEqual(58, crops.Sum(c => c.Diseases.Count));
            Assert.AreEqual(9, crops.Single(c => c.PlantId == "soybean").Diseases.Count);
            var tomato = crops.Single(c => c.PlantId == "tomato");
            Assert.AreEqual(9, tomato.Diseases.Count);
            CollectionAssert.AreEquivalent(DiseaseClasses.For("grape").Select(i => i.DiseaseId), data.DiseasesOf("grape").Select(s => s.Key.DiseaseId));
            Assert.IsEmpty(data.DiseasesOf("banana"));
            Assert.IsNotNull(data.LoadManualCase(new CaseKey("apple", "black_rot"), out var problems), string.Join("; ", problems));
            Assert.IsNull(data.LoadManualCase(new CaseKey("banana", "black_rot"), out problems));
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void MissingOrInvalidManualDataMakesOnlyThatCaseUnavailable()
        {
            var broken = sql + "\nDELETE FROM case_profile WHERE plant_id = 'peach';" +
                         "\nUPDATE treatment_controls SET step_value = 0 WHERE plant_id = 'cherry';" +
                         "\nUPDATE treatment_controls SET start_value = rec_min WHERE plant_id = 'corn' AND disease_id = 'common_rust';" +
                         "\nUPDATE treatment_controls SET rec_max = 12 WHERE plant_id = 'apple' AND disease_id = 'apple_scab';" +
                         "\nUPDATE case_profile SET max_health_gain = 0.2 WHERE plant_id = 'grape' AND disease_id = 'esca';";
            using (var repo = new SqliteTreatmentRepository(() => OpenContent(broken)))
            {
                repo.InitializeAsync().Wait();
                var data = new TreatmentDataManager(repo);
                foreach (var key in new[] { "peach/bacterial_spot", "cherry/powdery_mildew", "corn/common_rust", "apple/apple_scab", "grape/esca" })
                {
                    CaseKey.TryParse(key, out var k);
                    Assert.IsNull(data.LoadManualCase(k, out var problems), key);
                    Assert.IsNotEmpty(problems, key);
                    // The step lesson of the same case still loads.
                    Assert.IsNotNull(repo.GetCase(k, out _), key);
                }

                Assert.IsNotNull(data.LoadManualCase(new CaseKey("tomato", "early_blight"), out var ok), string.Join("; ", ok));
            }
        }

        [Test]
        public void ADatabaseWithoutManualTablesKeepsTheStepLessons()
        {
            var old = sql.Replace("CREATE TABLE case_profile", "CREATE TABLE unused_profile").Replace("INSERT INTO case_profile", "INSERT INTO unused_profile");
            using (var repo = new SqliteTreatmentRepository(() => OpenContent(old)))
            {
                repo.InitializeAsync().Wait();
                var c = repo.GetCase(new CaseKey("tomato", "early_blight"), out var error);
                Assert.IsNotNull(c, error);
                Assert.IsNull(c.Manual);
                Assert.IsNotNull(c.ManualError);
                Assert.IsNotEmpty(c.Steps);
            }
        }

        [Test]
        public void ManualLevelsAreAbstractTeachingScalesWithoutProducts()
        {
            var forbidden = new[] { "ml/l", "ml per", "g/l", "kg/ha", "ppm", "dose:", "mancozeb", "chlorothalonil", "imidacloprid" };
            foreach (var info in DiseaseClasses.AllCases)
            {
                var m = Load(info.PlantId, info.DiseaseId).Manual;
                foreach (var control in m.Controls)
                {
                    Assert.AreEqual(0f, control.Min);
                    Assert.AreEqual(10f, control.Max);
                    var text = (control.Name + control.Description + control.FeedbackLow + control.FeedbackHigh + control.FeedbackOk).ToLowerInvariant();
                    foreach (var word in forbidden)
                        Assert.IsFalse(text.Contains(word), $"{info.PlantId}/{info.DiseaseId}/{control.ControlId} mentions '{word}'.");
                }
            }
        }

        // ------------------------------------------------------------------ buttons

        [Test]
        public void PlusMinusMoveOneStepAndClampAtTheLimits()
        {
            var c = Load("tomato", "early_blight");
            var buttons = new TreatmentButtonController(c.Manual);
            var control = c.Manual.Controls[0];
            Assert.AreEqual(control.Start, buttons.ValueOf(control.ControlId));
            Assert.AreEqual(control.Start + control.Step, buttons.Increase(control.ControlId).Value, 1e-4f);
            Assert.AreEqual(control.Start, buttons.Decrease(control.ControlId).Value, 1e-4f);

            for (var i = 0; i < 40; i++)
                buttons.Decrease(control.ControlId);
            Assert.AreEqual(control.Min, buttons.ValueOf(control.ControlId));
            var atMin = buttons.Decrease(control.ControlId);
            Assert.AreEqual(control.Min, atMin.Value);
            StringAssert.Contains("minimum", atMin.Message);

            for (var i = 0; i < 40; i++)
                buttons.Increase(control.ControlId);
            Assert.AreEqual(control.Max, buttons.ValueOf(control.ControlId));
            StringAssert.Contains("maximum", buttons.Increase(control.ControlId).Message);
            Assert.IsFalse(buttons.Increase("no_such_control").Accepted);

            // Restored values are snapped onto the step grid and clamped.
            buttons.Restore(control.ControlId, 3.4f, true);
            Assert.AreEqual(3f, buttons.ValueOf(control.ControlId));
            buttons.Restore(control.ControlId, 99f, true);
            Assert.AreEqual(control.Max, buttons.ValueOf(control.ControlId));
        }

        [Test]
        public void NothingIsAutoSelected()
        {
            foreach (var info in DiseaseClasses.AllCases)
            {
                var c = Load(info.PlantId, info.DiseaseId);
                var buttons = new TreatmentButtonController(c.Manual);
                foreach (var control in c.Manual.Controls)
                {
                    Assert.IsFalse(buttons.IsTouched(control.ControlId));
                    Assert.Less(buttons.ValueOf(control.ControlId), control.RecommendedMin, $"{c.Key}/{control.ControlId} starts at its recommendation.");
                }

                var untouched = TreatmentValidationManager.Assess(c.Manual, buttons.Values);
                Assert.IsFalse(untouched.Valid);
                Assert.AreEqual(c.Manual.Controls.Count(k => k.Required), untouched.Missing.Count);
                Assert.IsTrue(untouched.Controls.All(k => k.Status == ControlStatus.NotSet));
            }

            // Apply is refused until every required control has been set by the learner.
            var session = NewSession(Load("tomato", "early_blight"));
            session.Start();
            session.ConfirmDiagnosis();
            var refused = session.Apply();
            Assert.IsFalse(refused.Accepted);
            StringAssert.Contains("Set every treatment level", refused.Message);
            Assert.IsFalse(session.HasApplied);
        }

        // ------------------------------------------------------------------ validation and rules

        [Test]
        public void ValidationComparesWithTheRecommendationAndUsesDatabaseFeedback()
        {
            var c = Load("tomato", "early_blight");
            var m = c.Manual;
            var best = TreatmentValidationManager.Assess(m, Values(m, Middle));
            Assert.IsTrue(best.Valid);
            Assert.AreEqual(1f, best.Score, 1e-4f);
            Assert.AreEqual(AssessmentBand.Good, best.Band);
            Assert.IsTrue(best.Controls.All(k => k.Status == ControlStatus.WithinRecommended));
            Assert.IsTrue(best.Controls.All(k => k.Feedback == k.Control.FeedbackOk));

            var low = TreatmentValidationManager.Assess(m, Values(m, k => k.Min));
            Assert.IsTrue(low.Valid);
            Assert.Less(low.Score, TreatmentValidationManager.WeakScore);
            Assert.IsTrue(low.Controls.All(k => k.Status == ControlStatus.BelowRecommended && k.Feedback == k.Control.FeedbackLow));

            var high = TreatmentValidationManager.Assess(m, Values(m, k => k.ControlId == "protect" ? k.Max : Middle(k)));
            var protect = high.Controls.Single(k => k.Control.ControlId == "protect");
            Assert.AreEqual(ControlStatus.AboveRecommended, protect.Status);
            Assert.AreEqual(protect.Control.FeedbackHigh, protect.Feedback);
            Assert.Less(high.Score, best.Score);

            // One step outside the range scores less than inside, more than far outside.
            var control = m.Controls[0];
            Assert.AreEqual(1f, TreatmentValidationManager.Closeness(control, control.RecommendedMin));
            var near = TreatmentValidationManager.Closeness(control, control.RecommendedMin - control.Step);
            Assert.Greater(near, 0f);
            Assert.Less(near, 1f);
            Assert.AreEqual(0f, TreatmentValidationManager.Closeness(control, control.Min));
        }

        [Test]
        public void CombinationRulesPenaliseHarmfulMixes()
        {
            // Heavy overhead irrigation with a protective spray on a leaf-wetness disease.
            var eb = Load("tomato", "early_blight").Manual;
            var wet = TreatmentValidationManager.Assess(eb, Values(eb, k => k.ControlId == "drip" ? 1f : Middle(k)));
            Assert.IsTrue(wet.TriggeredRules.Any(r => r.RuleId == "wet_spray"));
            var dry = TreatmentValidationManager.Assess(eb, Values(eb, Middle));
            Assert.IsEmpty(dry.TriggeredRules);
            Assert.Less(wet.Score, dry.Score);
            Assert.IsTrue(wet.FeedbackLines().Contains(eb.Rules.Single(r => r.RuleId == "wet_spray").Feedback));

            // Predators and heavy contact treatment do not mix.
            var mites = Load("tomato", "spider_mites").Manual;
            var clash = TreatmentValidationManager.Assess(mites, Values(mites, k => k.ControlId == "contact" ? 9f : Middle(k)));
            Assert.IsTrue(clash.TriggeredRules.Any(r => r.RuleId == "kills_predators"));

            // Replaced cases: containment must be chosen, otherwise the treatment cannot help at all.
            foreach (var key in ReplacedCases)
            {
                var m = Load(key).Manual;
                var uncontained = TreatmentValidationManager.Assess(m, Values(m, k => k.ControlId == "containment" ? 3f : Middle(k)));
                Assert.IsTrue(uncontained.Blocked, key);
                Assert.IsFalse(uncontained.Valid, key);
                Assert.AreEqual(0f, uncontained.Score, key);
                var contained = TreatmentValidationManager.Assess(m, Values(m, Middle));
                Assert.IsTrue(contained.Valid, key);
                Assert.AreEqual(AssessmentBand.Good, contained.Band, key);
            }
        }

        // ------------------------------------------------------------------ recovery

        [Test]
        public void RecoveryIsOutcomeAware()
        {
            foreach (var info in DiseaseClasses.AllCases)
            {
                var c = Load(info.PlantId, info.DiseaseId);
                var m = c.Manual;
                var initial = LeafRecoveryState.Initial(m);
                var best = LeafRecoveryModel.Evaluate(m, c.Outcome, TreatmentValidationManager.Assess(m, Values(m, Middle)));
                var worst = LeafRecoveryModel.Evaluate(m, c.Outcome, TreatmentValidationManager.Assess(m, Values(m, k => k.Min)));

                Assert.LessOrEqual(worst.Health, initial.Health + 1e-5f, $"{c.Key}: a poor treatment improved the leaf.");
                Assert.GreaterOrEqual(worst.Severity, initial.Severity - 1e-5f, $"{c.Key}: a poor treatment reduced severity.");
                Assert.AreEqual(0f, worst.SpotFade, $"{c.Key}");
                Assert.Less(best.Health, 1f, $"{c.Key} reached full health.");

                if (c.Outcome == TreatmentOutcome.Replaced)
                {
                    // Treatment cannot save the infected leaf; correct containment only stops it worsening.
                    Assert.AreEqual(initial.Health, best.Health, 1e-5f, $"{c.Key}");
                    Assert.AreEqual(initial.Severity, best.Severity, 1e-5f, $"{c.Key}");
                    Assert.AreEqual(0f, best.SpotFade + best.YellowFade + best.GreenGain + best.VeinGain + best.Worsen, 1e-5f, $"{c.Key}");
                }
                else
                {
                    Assert.Greater(best.Health, initial.Health, $"{c.Key}");
                    Assert.Less(best.Severity, initial.Severity, $"{c.Key}");
                    Assert.Greater(best.Severity, 0f, $"{c.Key}: old lesions vanished.");
                    Assert.Greater(best.SpotFade, 0f, $"{c.Key}");
                    Assert.LessOrEqual(best.SpotFade, 0.8f, $"{c.Key}: spots must never fully fade.");
                    Assert.Greater(best.YellowFade, 0f, $"{c.Key}");
                }
            }

            // Pest suppression improves less than disease management at the same (perfect) score.
            var managed = Load("tomato", "early_blight");
            var pest = Load("tomato", "spider_mites");
            float Gain(TreatmentCase k) => LeafRecoveryModel.Evaluate(k.Manual, k.Outcome, true, 1f).Health - k.Manual.InitialHealth;
            Assert.Less(Gain(pest), Gain(managed));
            Assert.Greater(Gain(pest), 0f);

            // A blocked (uncontained) Replaced case gets worse.
            var blight = Load("tomato", "late_blight");
            var bad = LeafRecoveryModel.Evaluate(blight.Manual, blight.Outcome, false, 0f);
            Assert.Less(bad.Health, blight.Manual.InitialHealth);
            Assert.Greater(bad.Worsen, 0f);
        }

        [Test]
        public void HealthMonitorAnimatesOnlyTowardsAnAppliedTarget()
        {
            var m = Load("tomato", "early_blight").Manual;
            var initial = LeafRecoveryState.Initial(m);
            var monitor = new LeafHealthMonitor(initial);
            monitor.Advance(60f);
            Assert.AreEqual(initial, monitor.Current);

            var target = LeafRecoveryModel.Evaluate(m, TreatmentOutcome.Managed, true, 1f);
            monitor.SetTarget(target, 8f);
            Assert.IsTrue(monitor.IsImproving);
            monitor.Advance(4f);
            Assert.Greater(monitor.Current.Health, initial.Health);
            Assert.Less(monitor.Current.Health, target.Health);
            monitor.Advance(10f);
            Assert.AreEqual(target, monitor.Current);
            Assert.IsFalse(monitor.IsTransitioning);
        }

        // ------------------------------------------------------------------ timer and session

        [Test]
        public void TimerAloneNeverChangesTheLeaf()
        {
            var c = Load("tomato", "early_blight");
            var session = NewSession(c);
            session.Start();
            var before = session.Target;
            for (var i = 0; i < 700; i++)
                session.Tick(0.1f);
            Assert.AreEqual(before, session.Target);
            Assert.IsFalse(session.HasApplied);
            Assert.AreEqual(TreatmentTimer.SessionSeconds, session.Timer.Elapsed);
            // When time runs out the session goes to the final assessment; nothing was treated.
            Assert.AreEqual(ManualStage.FinalAssessment, session.Stage);
            Assert.IsTrue(session.Finish().Accepted);
            Assert.AreEqual("NotTreated", session.Record.Outcome);
            Assert.AreEqual(c.Manual.InitialHealth, session.Record.FinalHealth.Value, 1e-5f);
            Assert.AreEqual(c.Manual.InitialSeverity, session.Record.FinalSeverity.Value, 1e-5f);
        }

        [Test]
        public void TimerWindowsArePausedGuidance()
        {
            // One minute in five 12-second windows.
            Assert.AreEqual(60f, TreatmentTimer.SessionSeconds);
            Assert.AreEqual(12f, TreatmentTimer.StageSeconds);
            var timer = new TreatmentTimer();
            Assert.AreEqual(ManualStage.Diagnosis, TreatmentTimer.GuidanceStageAt(0f));
            Assert.AreEqual(ManualStage.Diagnosis, TreatmentTimer.GuidanceStageAt(11.9f));
            Assert.AreEqual(ManualStage.Selection, TreatmentTimer.GuidanceStageAt(13f));
            Assert.AreEqual(ManualStage.Monitoring, TreatmentTimer.GuidanceStageAt(47.9f));
            Assert.AreEqual(ManualStage.FinalAssessment, TreatmentTimer.GuidanceStageAt(60f));
            Assert.AreEqual((24f, 36f), TreatmentTimer.Window(ManualStage.Application));
            Assert.AreEqual("(0-12 s)", TreatmentTimer.WindowLabel(ManualStage.Diagnosis));
            Assert.AreEqual("(48-60 s)", TreatmentTimer.WindowLabel(ManualStage.FinalAssessment));
            Assert.AreEqual("1:00", TreatmentTimer.Clock(TreatmentTimer.SessionSeconds));
            timer.Start(10f);
            timer.Pause();
            timer.Tick(5f);
            Assert.AreEqual(10f, timer.Elapsed);
            timer.Resume();
            timer.Tick(5f);
            Assert.AreEqual(15f, timer.Elapsed);
            Assert.AreEqual("0:45", TreatmentTimer.Clock(timer.Remaining));

            // Stages advance on actions, not on the clock: at 18 s the session can still be in Diagnosis.
            var session = NewSession(Load("apple", "apple_scab"));
            session.Start();
            session.Tick(18f);
            Assert.AreEqual(ManualStage.Diagnosis, session.Stage);
            Assert.AreEqual(ManualStage.Selection, session.Timer.GuidanceStage);
        }

        [Test]
        public void SessionStagesFollowLearnerActions()
        {
            var c = Load("tomato", "septoria_leaf_spot");
            var session = NewSession(c);
            Assert.IsFalse(session.Adjust(c.Manual.Controls[0].ControlId, 1).Accepted);
            session.Start();
            Assert.AreEqual(ManualStage.Diagnosis, session.Stage);
            Assert.IsFalse(session.Increase(c.Manual.Controls[0].ControlId).Accepted, "Adjusting before the diagnosis is reviewed.");
            Assert.IsFalse(session.ContinueToMonitoring().Accepted);
            Assert.IsTrue(session.ConfirmDiagnosis().Accepted);
            Assert.AreEqual(ManualStage.Selection, session.Stage);
            foreach (var control in c.Manual.Controls)
                SetTo(session, control, Middle(control));
            var applied = session.Apply();
            Assert.IsTrue(applied.Accepted && applied.TargetChanged && applied.StageChanged);
            Assert.AreEqual(ManualStage.Application, session.Stage);
            Assert.Greater(session.Target.Health, c.Manual.InitialHealth);
            Assert.IsTrue(session.ContinueToMonitoring().Accepted);
            Assert.IsFalse(session.ProceedToFinal().Accepted, "Final assessment needs a health check.");
            Assert.IsTrue(session.CheckHealth(session.Target).Accepted);

            // Further adjustment in monitoring: a pending change must be applied before the final assessment.
            Assert.IsTrue(session.Decrease(c.Manual.Controls[0].ControlId).Accepted);
            Assert.IsTrue(session.HasPendingChanges);
            Assert.IsFalse(session.ProceedToFinal().Accepted);
            Assert.IsTrue(session.Apply().Accepted);
            Assert.IsFalse(session.HasPendingChanges);
            Assert.IsTrue(session.ProceedToFinal().Accepted);
            Assert.IsFalse(session.Increase(c.Manual.Controls[0].ControlId).Accepted, "Adjustments are closed in the final assessment.");
            Assert.IsTrue(session.Finish().Accepted);
            Assert.AreEqual(ManualStage.Completed, session.Stage);
            Assert.IsTrue(session.Record.Completed);
            Assert.AreEqual(5, session.Record.StagesCompleted.Count);
            Assert.AreEqual(2, session.Record.ApplyCount);
            Assert.IsTrue(session.Record.Actions.Any(a => !a.Accepted), "Refused actions are kept in the history.");
            CollectionAssert.AreEqual(Enumerable.Range(1, session.Record.Actions.Count), session.Record.Actions.Select(a => a.Seq));
        }

        [Test]
        public void SuccessDependsOnValidActions()
        {
            var managed = NewSession(Load("tomato", "early_blight"));
            PlaySession(managed, Middle);
            Assert.AreEqual("Managed", managed.Record.Outcome);
            Assert.Greater(managed.Record.FinalHealth.Value, managed.Record.InitialHealth);
            StringAssert.Contains(TreatmentResultManager.Disclaimer, TreatmentResultManager.BuildResult(managed.Case, managed.Record, managed.AppliedAssessment()));

            var poor = NewSession(Load("tomato", "early_blight"));
            PlaySession(poor, k => k.Min);
            Assert.AreEqual("NotControlled", poor.Record.Outcome);
            Assert.LessOrEqual(poor.Record.FinalHealth.Value, poor.Record.InitialHealth);

            var contained = NewSession(Load("tomato", "mosaic_virus"));
            PlaySession(contained, Middle);
            Assert.AreEqual("Contained", contained.Record.Outcome);
            Assert.AreEqual(contained.Record.InitialHealth, contained.Record.FinalHealth.Value, 1e-5f, "A Replaced leaf never improves.");
            StringAssert.Contains("replace", TreatmentResultManager.BuildResult(contained.Case, contained.Record, contained.AppliedAssessment()).ToLowerInvariant());

            var uncontained = NewSession(Load("grape", "esca"));
            PlaySession(uncontained, k => k.ControlId == "containment" ? 4f : Middle(k));
            Assert.AreEqual("NotContained", uncontained.Record.Outcome);
            Assert.Less(uncontained.Record.FinalHealth.Value, uncontained.Record.InitialHealth);
        }

        // ------------------------------------------------------------------ progress database

        [Test]
        public void ProgressMigrationFromV1KeepsExistingRows()
        {
            var path = Path.Combine(Path.GetTempPath(), $"btp_progress_v1_{Guid.NewGuid():N}.db");
            try
            {
                // A version-1 file as the previous build wrote it, with saved step-lesson progress.
                using (var v1 = new SQLiteConnection(path))
                {
                    v1.Execute("CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT)");
                    v1.Execute("CREATE TABLE case_progress (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, content_version INTEGER, route TEXT, outcome TEXT, " +
                               "completed INTEGER NOT NULL DEFAULT 0, updated_utc INTEGER NOT NULL, PRIMARY KEY (plant_id, disease_id))");
                    v1.Execute("CREATE TABLE progress_steps (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, step_id TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, step_id))");
                    v1.Execute("INSERT INTO settings VALUES ('schema_version', '1')");
                    v1.Execute("INSERT INTO settings VALUES ('last_case', 'tomato/leaf_mold')");
                    v1.Execute("INSERT INTO case_progress VALUES ('tomato', 'leaf_mold', 2, 'protect', 'Managed', 1, ?)", DateTime.UtcNow.Ticks);
                    v1.Execute("INSERT INTO progress_steps VALUES ('tomato', 'leaf_mold', 'inspect')");
                    v1.Execute("INSERT INTO progress_steps VALUES ('tomato', 'leaf_mold', 'confirm')");
                }

                using (var progress = new SqliteProgressRepository(path))
                {
                    Assert.IsTrue(progress.IsReady, progress.Error);
                    Assert.AreEqual(2, progress.StoredSchemaVersion);
                    Assert.AreEqual("2", progress.GetSetting("schema_version"));
                    Assert.AreEqual("tomato/leaf_mold", progress.GetSetting("last_case"));
                    var restored = progress.Load(new CaseKey("tomato", "leaf_mold"), 2);
                    Assert.IsTrue(restored.Completed);
                    Assert.AreEqual("protect", restored.Route);
                    CollectionAssert.AreEquivalent(new[] { "inspect", "confirm" }, restored.CompletedSteps);
                    Assert.IsNotNull(progress.CreateLearner("Asha"));
                }

                // Reopening a v2 file is a no-op: data stays, version stays.
                using (var again = new SqliteProgressRepository(path))
                {
                    Assert.AreEqual(2, again.StoredSchemaVersion);
                    Assert.AreEqual(1, again.ListLearners().Count);
                    Assert.IsTrue(again.Load(new CaseKey("tomato", "leaf_mold"), 2).Completed);
                }
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Test]
        public void LearnersAreLocalAndRemembered()
        {
            using (var progress = new SqliteProgressRepository(":memory:"))
            {
                var saves = new ProgressSaveManager(progress);
                Assert.IsEmpty(saves.Learners());
                var first = saves.CreateNextLearner();
                var second = saves.CreateNextLearner();
                Assert.AreEqual("Learner 1", first.Name);
                Assert.AreEqual("Learner 2", second.Name);
                Assert.AreEqual(first.LearnerId, progress.CreateLearner("learner 1").LearnerId, "Names are unique, case-insensitive.");
                Assert.IsNull(progress.CreateLearner("   "));
                saves.RememberLearner(second);
                Assert.AreEqual(second.LearnerId, saves.LastLearner().LearnerId);
                saves.RememberMode(true);
                Assert.IsTrue(saves.ManualModeRemembered);
            }
        }

        [Test]
        public void SessionSaveAndRestoreRoundTrips()
        {
            var c = Load("tomato", "early_blight");
            using (var progress = new SqliteProgressRepository(":memory:"))
            {
                var saves = new ProgressSaveManager(progress);
                var learner = saves.CreateNextLearner();
                var other = saves.CreateNextLearner();

                // Step-lesson progress in the same file must survive manual sessions.
                var lesson = progress.Load(c.Key, c.ContentVersion);
                lesson.CompletedSteps.Add("inspect");
                Assert.IsTrue(progress.Save(lesson));

                var session = NewSession(c, learner.LearnerId);
                session.Start();
                session.ConfirmDiagnosis();
                foreach (var control in c.Manual.Controls.Take(2))
                    SetTo(session, control, Middle(control));
                session.Tick(9.5f);
                Assert.IsTrue(saves.Save(session), progress.Error);
                Assert.Greater(session.Record.SessionId, 0);

                // A completed session of another learner is untouched by later saves.
                var finished = NewSession(c, other.LearnerId);
                PlaySession(finished, Middle);
                Assert.IsTrue(saves.Save(finished));

                foreach (var control in c.Manual.Controls.Skip(2))
                    SetTo(session, control, Middle(control));
                Assert.IsTrue(session.Apply().Accepted);
                Assert.IsTrue(saves.Save(session));
                Assert.IsTrue(saves.Save(session), "Saving twice must not duplicate history.");

                var resumable = saves.FindResumable(learner.LearnerId, c.Key);
                Assert.IsNotNull(resumable);
                Assert.AreEqual(session.Record.SessionId, resumable.SessionId);
                Assert.AreEqual(ManualStage.Application, resumable.CurrentStage);
                Assert.AreEqual(session.Record.ElapsedSeconds, resumable.ElapsedSeconds, 1e-3f);
                Assert.AreEqual(session.Record.Actions.Count, resumable.Actions.Count);
                Assert.AreEqual(session.Record.AppliedScore.Value, resumable.AppliedScore.Value, 1e-5f);
                CollectionAssert.AreEquivalent(session.Record.StagesCompleted.Keys, resumable.StagesCompleted.Keys);

                var restored = new TreatmentSessionManager(c, resumable);
                Assert.AreEqual(session.Stage, restored.Stage);
                Assert.AreEqual(session.Target, restored.Target);
                Assert.AreEqual(session.Timer.Elapsed, restored.Timer.Elapsed, 1e-3f);
                foreach (var control in c.Manual.Controls)
                {
                    Assert.AreEqual(session.Buttons.ValueOf(control.ControlId), restored.Buttons.ValueOf(control.ControlId));
                    Assert.AreEqual(session.Buttons.IsTouched(control.ControlId), restored.Buttons.IsTouched(control.ControlId));
                }

                Assert.IsFalse(restored.HasPendingChanges);
                Assert.IsTrue(restored.Resume().Accepted);

                // Starting over keeps the old session (abandoned), never deletes it.
                Assert.IsTrue(saves.Abandon(restored.Record, DateTime.UtcNow));
                Assert.IsNull(saves.FindResumable(learner.LearnerId, c.Key));
                Assert.AreEqual(1, progress.ListManualSessions(learner.LearnerId).Count);
                Assert.AreEqual(ManualSessionRecord.OutcomeAbandoned, progress.ListManualSessions(learner.LearnerId)[0].Outcome);
                var done = progress.ListManualSessions(other.LearnerId).Single();
                Assert.IsTrue(done.Completed);
                Assert.AreEqual("Managed", done.Outcome);
                Assert.IsTrue(progress.Load(c.Key, c.ContentVersion).CompletedSteps.Contains("inspect"));
            }
        }

        [Test]
        public void SessionsSavedPastTheTimeLimitResumeInTheFinalAssessment()
        {
            var c = Load("tomato", "early_blight");
            using (var progress = new SqliteProgressRepository(":memory:"))
            {
                var saves = new ProgressSaveManager(progress);
                var learner = saves.CreateNextLearner();
                var session = NewSession(c, learner.LearnerId);
                session.Start();
                session.ConfirmDiagnosis();
                foreach (var control in c.Manual.Controls)
                    SetTo(session, control, Middle(control));
                Assert.IsTrue(session.Apply().Accepted);
                // As a longer session length would have saved it: 95 s elapsed, still in Application.
                session.Record.ElapsedSeconds = 95f;
                Assert.IsTrue(saves.Save(session));

                var restored = new TreatmentSessionManager(c, saves.FindResumable(learner.LearnerId, c.Key));
                Assert.AreEqual(ManualStage.FinalAssessment, restored.Stage);
                Assert.AreEqual(TreatmentTimer.SessionSeconds, restored.Timer.Elapsed);
                Assert.AreEqual(TreatmentTimer.SessionSeconds, restored.Record.ElapsedSeconds);
                Assert.AreEqual(session.Target, restored.Target, "the applied treatment still counts");
                Assert.IsFalse(restored.Tick(1f).StageChanged);
                Assert.IsTrue(restored.Finish().Accepted);
                Assert.AreEqual("Managed", restored.Record.Outcome);
                Assert.IsTrue(saves.Save(restored));
            }
        }

        [Test]
        public void RepeatedRefusedPressesCollapseInTheHistoryButAreAllRecorded()
        {
            var c = Load("tomato", "early_blight");
            var session = NewSession(c);
            session.Start();
            session.ConfirmDiagnosis();
            foreach (var control in c.Manual.Controls)
                SetTo(session, control, Middle(control));
            session.Apply();
            session.ContinueToMonitoring();
            var before = session.Record.Actions.Count;
            for (var i = 0; i < 4; i++)
                Assert.IsFalse(session.ProceedToFinal().Accepted);
            Assert.AreEqual(before + 4, session.Record.Actions.Count, "every press is recorded");

            var history = session.RecentHistory(6);
            Assert.AreEqual(6, history.Count);
            Assert.IsFalse(history[0].action.Accepted);
            Assert.AreEqual(4, history[0].repeats);
            StringAssert.Contains("health at least once", history[0].action.Message);
            Assert.IsTrue(history.Skip(1).All(h => h.repeats == 1));
            // Accepted entries are never merged.
            Assert.AreEqual(session.Record.Actions[before - 1].Message, history[1].action.Message);
        }
    }
}
