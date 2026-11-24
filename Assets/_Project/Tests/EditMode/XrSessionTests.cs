using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BTP.Core;
using BTP.Plants;
using BTP.Treatment;
using BTP.XR;
using NUnit.Framework;
using SQLite;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BTP.Tests
{
    /// <summary>
    /// The CropScan XR client without a server: the shared case map and specimens, session validation, the recipe
    /// adapter against the real treatment content, the fixture backend's save rules (pinned recipe, revisions, one write
    /// per idempotency key, a lost response, a restart), launch dedupe, backoff, start arguments and the case lock.
    /// </summary>
    public sealed class XrSessionTests
    {
        const string MapPath = "Assets/_Project/Resources/XR/xr_case_map.json";
        const string SpecimenPath = "Assets/_Project/Resources/XR/xr_specimens.json";
        const string FixturePath = "Assets/_Project/Resources/XR/Fixtures/xr-test-001.json";
        const string SqlPath = "Assets/_Project/Data/Treatment/treatment_content.sql";
        const string PlantCatalogPath = "Assets/_Project/Data/PlantCatalog.asset";

        static XrCaseMap map;
        static XrSpecimenCatalog specimens;
        static string sql;
        SqliteTreatmentRepository content;

        [OneTimeSetUp]
        public void LoadShared()
        {
            map = XrCaseMap.Parse(File.ReadAllText(MapPath), out var mapProblems);
            Assert.That(mapProblems, Is.Empty, string.Join("\n", mapProblems));
            specimens = XrSpecimenCatalog.Parse(File.ReadAllText(SpecimenPath), out var specimenProblems);
            Assert.That(specimenProblems, Is.Empty, string.Join("\n", specimenProblems));
            sql = File.ReadAllText(SqlPath);
        }

        [SetUp]
        public void OpenContent()
        {
            content = new SqliteTreatmentRepository(() =>
            {
                var db = new SQLiteConnection(":memory:");
                db.ExecuteScript(sql);
                return db;
            });
            content.InitializeAsync().Wait();
            Assert.IsTrue(content.IsReady, content.Error);
        }

        [TearDown]
        public void CloseContent() => content.Dispose();

        TreatmentCase Local(CaseKey key)
        {
            var c = content.GetCase(key, out var error);
            Assert.IsNotNull(c, error);
            return c;
        }

        static XrSessionData Fixture()
        {
            var session = FixtureXrBackend.ParseFixture(File.ReadAllText(FixturePath), out var error);
            Assert.IsNotNull(session, error);
            return session;
        }

        bool RecipeSupported(XrCaseMap.Row row)
        {
            var c = content.GetCase(new CaseKey(row.plantId, row.diseaseId), out _);
            return c != null && TreatmentCaseValidator.ValidateManual(c).Count == 0;
        }

        // ------------------------------------------------------------------ case map and specimens

        [Test]
        public void CaseMap_ResolvesTheFirstThreeBackendClassesExactly()
        {
            Assert.That(map.Version, Is.GreaterThan(0));
            Assert.IsTrue(map.TryResolve("Tomato___Early_blight", out var tomato, out _));
            Assert.IsTrue(map.TryResolve("Potato___Early_blight", out var potato, out _));
            Assert.IsTrue(map.TryResolve("Pepper,_bell___Bacterial_spot", out var pepper, out _));
            Assert.That((tomato.plantId, tomato.diseaseId), Is.EqualTo(("tomato", "early_blight")));
            Assert.That((potato.plantId, potato.diseaseId), Is.EqualTo(("potato", "early_blight")));
            Assert.That((pepper.plantId, pepper.diseaseId), Is.EqualTo(("bell_pepper", "bacterial_spot")));
            // Same disease name, different crops: different Unity cases, specimens and recipes.
            Assert.That(potato.specimenId, Is.Not.EqualTo(tomato.specimenId));
            Assert.That(potato.recipeId, Is.Not.EqualTo(tomato.recipeId));
        }

        [Test]
        public void CaseMap_CoversEveryFarmDiseaseRow()
        {
            // v2: whichever disease the uploaded leaf shows, the learner lands at that disease's row.
            Assert.That(map.Rows.Count, Is.EqualTo(DiseaseClasses.All.Count));
            foreach (var info in DiseaseClasses.All)
                Assert.That(map.Rows.Any(r => r.plantId == info.PlantId && r.diseaseId == info.DiseaseId), Is.True, $"{info.PlantId}/{info.DiseaseId}");
        }

        [Test]
        public void CaseMap_UnknownOrHealthyClass_HasAReasonAndNoFallbackCase()
        {
            Assert.IsFalse(map.TryResolve("Orange___Haunglongbing_(Citrus_greening)", out var row, out var reason));
            Assert.IsNull(row);
            StringAssert.Contains("Orange___Haunglongbing_(Citrus_greening)", reason);
            Assert.IsFalse(map.TryResolve("Tomato___healthy", out _, out var healthy));
            StringAssert.Contains("healthy", healthy);
            // Matching is exact: no case-insensitive or partial match into Unity's default case.
            Assert.IsFalse(map.TryResolve("tomato___early_blight", out _, out _));
            Assert.IsFalse(map.TryResolve("", out _, out _));
        }

        [Test]
        public void CaseMap_RejectsInvalidRows()
        {
            var json = "{\"mapVersion\":1,\"rows\":[" +
                       "{\"backendDiseaseId\":\"A\",\"plantId\":\"tomato\",\"diseaseId\":\"early_blight\",\"specimenId\":\"s1\",\"recipeId\":\"r\"}," +
                       "{\"backendDiseaseId\":\"A\",\"plantId\":\"tomato\",\"diseaseId\":\"late_blight\",\"specimenId\":\"s2\",\"recipeId\":\"r\"}," +
                       "{\"backendDiseaseId\":\"B\",\"plantId\":\"tomato\",\"diseaseId\":\"no_such_disease\",\"specimenId\":\"s3\",\"recipeId\":\"r\"}," +
                       "{\"backendDiseaseId\":\"C\",\"plantId\":\"potato\",\"diseaseId\":\"early_blight\",\"specimenId\":\"s1\",\"recipeId\":\"r\"}," +
                       "{\"backendDiseaseId\":\"D\",\"plantId\":\"potato\",\"diseaseId\":\"late_blight\",\"specimenId\":\"\",\"recipeId\":\"r\"}]}";
            var parsed = XrCaseMap.Parse(json, out var problems);
            Assert.That(parsed.Rows.Count, Is.EqualTo(1));
            Assert.That(problems.Count, Is.EqualTo(4), string.Join("\n", problems));
        }

        [Test]
        public void EveryMappedRow_HasAMatchingSpecimenAndAManualLesson()
        {
            foreach (var row in map.Rows)
            {
                Assert.IsTrue(specimens.TryGet(row.specimenId, out var specimen), row.specimenId);
                Assert.That((specimen.plantId, specimen.diseaseId), Is.EqualTo((row.plantId, row.diseaseId)), row.specimenId);
                Assert.IsTrue(RecipeSupported(row), $"{row.plantId}/{row.diseaseId} needs a valid manual profile");
            }
        }

        [Test]
        public void StandCandidates_StartAtTheRowHeadAndFaceThePlant()
        {
            var plant = new Vector3(-9.75f, 0.25f, -0.25f);
            var candidates = XrSpecimenCatalog.StandCandidates(plant, Vector3.forward, 1.6f);
            Assert.That(candidates[0].z, Is.EqualTo(-1.85f).Within(1e-4f));
            Assert.That(candidates[0].x, Is.EqualTo(plant.x).Within(1e-4f));
            foreach (var candidate in candidates)
                Assert.That(Vector3.Distance(new Vector3(candidate.x, 0f, candidate.z), new Vector3(plant.x, 0f, plant.z)), Is.GreaterThanOrEqualTo(1.6f - 1e-4f));
            var facing = XrSpecimenCatalog.Facing(candidates[0], plant) * Vector3.forward;
            Assert.That(Vector3.Dot(facing, Vector3.forward), Is.GreaterThan(0.99f));

            // With a side offset the learner stands just beside the row's sign, still in front of and facing the plant.
            var beside = XrSpecimenCatalog.StandCandidates(plant, Vector3.forward, 1.2f, 0.55f)[0];
            Assert.That(beside.x, Is.EqualTo(plant.x + 0.55f).Within(1e-4f));
            Assert.That(beside.z, Is.EqualTo(plant.z - 1.2f).Within(1e-4f));
            var towardPlant = (XrSpecimenCatalog.Facing(beside, plant) * Vector3.forward).normalized;
            Assert.That(Vector3.Dot(towardPlant, (new Vector3(plant.x, 0f, plant.z) - new Vector3(beside.x, 0f, beside.z)).normalized), Is.GreaterThan(0.999f));
        }

        // ------------------------------------------------------------------ session validation

        [Test]
        public void Fixture_IsAValidTomatoEarlyBlightSessionWithoutAResult()
        {
            var session = Fixture();
            Assert.That(session.Case, Is.EqualTo(new CaseKey("tomato", "early_blight")));
            Assert.IsFalse(session.HasResult, "a JSON null result is no result");
            var problems = XrSessionValidator.Validate(session, map, specimens, RecipeSupported);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Validator_RefusesSessionsThatDisagreeWithTheMapping()
        {
            var wrongCase = Fixture();
            wrongCase.plantId = "potato";
            Assert.That(XrSessionValidator.Validate(wrongCase, map, specimens, RecipeSupported), Is.Not.Empty);

            var wrongSpecimen = Fixture();
            wrongSpecimen.specimenId = "farm-potato-early-blight-01";
            Assert.That(XrSessionValidator.Validate(wrongSpecimen, map, specimens, RecipeSupported), Is.Not.Empty);

            var unsupported = Fixture();
            unsupported.backendDiseaseId = "Orange___Haunglongbing_(Citrus_greening)";
            var problems = XrSessionValidator.Validate(unsupported, map, specimens, RecipeSupported);
            Assert.That(problems.Single(), Does.Contain("Orange___Haunglongbing_(Citrus_greening)"));

            var arOnly = Fixture();
            arOnly.plantId = arOnly.diseaseId = "";
            Assert.That(XrSessionValidator.Validate(arOnly, map, specimens, RecipeSupported).Single(), Does.Contain("AR-only"));

            var unpinned = Fixture();
            unpinned.recipeVersion = 0;
            Assert.That(XrSessionValidator.Validate(unpinned, map, specimens, RecipeSupported), Is.Not.Empty);
        }

        [Test]
        public void Validator_PotatoEarlyBlight_ResolvesToThePotatoCase()
        {
            var potato = Fixture();
            map.TryResolve("Potato___Early_blight", out var row, out _);
            potato.backendDiseaseId = row.backendDiseaseId;
            potato.plantId = row.plantId;
            potato.diseaseId = row.diseaseId;
            potato.specimenId = row.specimenId;
            potato.recipeId = row.recipeId;
            Assert.That(XrSessionValidator.Validate(potato, map, specimens, RecipeSupported), Is.Empty);
            Assert.That(potato.Case, Is.Not.EqualTo(Fixture().Case));
        }

        // ------------------------------------------------------------------ recipe

        [Test]
        public void Recipe_FromLocalCase_ValidatesAndScoresLikeTheLocalProfile()
        {
            var session = Fixture();
            var local = Local(session.Case);
            var recipe = XrRecipeAdapter.FromCase(local, session.recipeId, session.recipeVersion, "fixture");
            var problems = XrRecipeAdapter.Validate(recipe, session, local, null);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));

            var adapted = XrRecipeAdapter.Apply(recipe, local);
            Assert.That(adapted, Is.Not.SameAs(local));
            Assert.That(adapted.Manual, Is.Not.SameAs(local.Manual));
            Assert.That(adapted.Manual.Controls.Select(k => k.ControlId), Is.EqualTo(local.Manual.Controls.Select(k => k.ControlId)));
            // The same levels score the same with the recipe as with the bundled profile.
            var values = local.Manual.Controls.ToDictionary(k => k.ControlId,
                k => new ControlSelection { Value = (k.RecommendedMin + k.RecommendedMax) * 0.5f, Touched = true });
            var expected = TreatmentValidationManager.Assess(local.Manual, values);
            var actual = TreatmentValidationManager.Assess(adapted.Manual, values);
            Assert.That(actual.Score, Is.EqualTo(expected.Score).Within(1e-5f));
            Assert.That(actual.Valid, Is.EqualTo(expected.Valid));
        }

        [Test]
        public void Recipe_ThatDoesNotFitThisBuild_IsRefused()
        {
            var session = Fixture();
            var local = Local(session.Case);
            XrRecipe Fresh() => XrRecipeAdapter.FromCase(local, session.recipeId, session.recipeVersion, "fixture");

            var version = Fresh();
            version.recipeVersion = 2;
            Assert.That(XrRecipeAdapter.Validate(version, session, local, null), Has.Some.Contains("version"));

            var range = Fresh();
            range.controls[0].recommendedMax = range.controls[0].max + 5f;
            Assert.That(XrRecipeAdapter.Validate(range, session, local, null), Has.Some.Contains("recommended range"));

            var unknown = Fresh();
            unknown.controls[0].controlId = "no_such_control";
            Assert.That(XrRecipeAdapter.Validate(unknown, session, local, null), Is.Not.Empty);

            var missing = Fresh();
            missing.controls = missing.controls.Skip(1).ToArray();
            Assert.That(XrRecipeAdapter.Validate(missing, session, local, null), Has.Some.Contains("required"));

            var asset = Fresh();
            Assert.That(XrRecipeAdapter.Validate(asset, session, local, key => false), Has.Some.Contains("asset catalog"));

            var otherCase = Fresh();
            otherCase.plantId = "potato";
            Assert.That(XrRecipeAdapter.Validate(otherCase, session, local, null), Is.Not.Empty);
        }

        // ------------------------------------------------------------------ fixture backend save rules

        static XrTreatmentSubmission Submission(XrSessionData session, long manualSessionId = 7, string outcome = "Managed") => new XrTreatmentSubmission
        {
            xrSessionId = session.xrSessionId, recipeId = session.recipeId, recipeVersion = session.recipeVersion, expectedRevision = session.revision,
            manualSessionId = manualSessionId, outcome = outcome, initialSeverity = 0.4f, finalSeverity = 0.1f, initialHealth = 0.55f,
            finalHealth = 0.8f, score = 0.9f, valid = true,
        };

        FixtureXrBackend Backend(string store = null) => new FixtureXrBackend(Fixture(), Local, store);

        [Test]
        public void Fixture_SaveWritesOnce_AndARetriedKeyReturnsTheSameResult()
        {
            var backend = Backend();
            var session = backend.GetSession("xr-test-001").Result.Value;
            var submission = Submission(session);

            var first = backend.SubmitTreatmentResult(submission).Result;
            Assert.That(first.Status, Is.EqualTo(XrSubmitStatus.Saved), first.Message);
            Assert.That(first.Session.revision, Is.EqualTo(1));
            Assert.That(first.Session.treatmentResult.improvement, Is.EqualTo(XrImprovement.Improved));

            var retry = backend.SubmitTreatmentResult(submission).Result;
            Assert.That(retry.Status, Is.EqualTo(XrSubmitStatus.Saved));
            Assert.That(retry.Session.revision, Is.EqualTo(1));
            Assert.That(backend.WriteCount, Is.EqualTo(1));
        }

        [Test]
        public void Fixture_LostResponse_RetryWithTheSameKey_LeavesOneResult()
        {
            var backend = Backend();
            backend.DropNextResponse = true;
            var submission = Submission(backend.GetSession("xr-test-001").Result.Value);

            var lost = backend.SubmitTreatmentResult(submission).Result;
            Assert.That(lost.Status, Is.EqualTo(XrSubmitStatus.Failed));
            Assert.That(backend.WriteCount, Is.EqualTo(1), "the save was committed although its response was lost");

            var retried = backend.SubmitTreatmentResult(submission).Result;
            Assert.That(retried.Status, Is.EqualTo(XrSubmitStatus.Saved));
            Assert.That(retried.Session.revision, Is.EqualTo(1));
            Assert.That(backend.WriteCount, Is.EqualTo(1));
        }

        [Test]
        public void Fixture_StaleRevisionConflicts_AndAWrongRecipeIsRejected()
        {
            var backend = Backend();
            var start = backend.GetSession("xr-test-001").Result.Value;
            Assert.That(backend.SubmitTreatmentResult(Submission(start, 1)).Result.Status, Is.EqualTo(XrSubmitStatus.Saved));

            // A second assessment that still expects revision 0 must not overwrite revision 1 blindly.
            var conflict = backend.SubmitTreatmentResult(Submission(start, 2, "PartlyManaged")).Result;
            Assert.That(conflict.Status, Is.EqualTo(XrSubmitStatus.Conflict));
            Assert.That(conflict.Session.revision, Is.EqualTo(1));
            Assert.That(conflict.Session.treatmentResult.outcome, Is.EqualTo("Managed"));

            var reloaded = backend.GetSession("xr-test-001").Result.Value;
            var again = backend.SubmitTreatmentResult(Submission(reloaded, 2, "PartlyManaged")).Result;
            Assert.That(again.Status, Is.EqualTo(XrSubmitStatus.Saved));
            Assert.That(again.Session.revision, Is.EqualTo(2));
            Assert.That(again.Session.treatmentResult.improvement, Is.EqualTo(XrImprovement.Partial));

            var wrongRecipe = Submission(again.Session, 3);
            wrongRecipe.recipeVersion = 99;
            Assert.That(backend.SubmitTreatmentResult(wrongRecipe).Result.Status, Is.EqualTo(XrSubmitStatus.Rejected));
            var badOutcome = Submission(again.Session, 4, "Cured");
            Assert.That(backend.SubmitTreatmentResult(badOutcome).Result.Status, Is.EqualTo(XrSubmitStatus.Rejected));
        }

        [Test]
        public void Fixture_SavedResult_SurvivesARestart()
        {
            var store = Path.Combine(Path.GetTempPath(), $"btp_xr_store_{Guid.NewGuid():N}.json");
            try
            {
                var backend = Backend(store);
                backend.SubmitTreatmentResult(Submission(backend.GetSession("xr-test-001").Result.Value)).Wait();

                var restarted = Backend(store);
                var session = restarted.GetSession("xr-test-001").Result.Value;
                Assert.That(session.revision, Is.EqualTo(1));
                Assert.IsTrue(session.HasResult);
                Assert.That(restarted.WriteCount, Is.EqualTo(1));

                restarted.Reset(Fixture());
                Assert.That(restarted.GetSession("xr-test-001").Result.Value.revision, Is.EqualTo(0));
            }
            finally
            {
                if (File.Exists(store))
                    File.Delete(store);
            }
        }

        [Test]
        public void Fixture_Recipe_IsBuiltFromTheBundledContentAndPinned()
        {
            var backend = Backend();
            var session = backend.GetSession("xr-test-001").Result.Value;
            var recipe = backend.GetRecipe("xr-test-001").Result;
            Assert.IsTrue(recipe.Ok, recipe.Error);
            Assert.That(recipe.Value.source, Is.EqualTo("fixture"));
            Assert.That(recipe.Value.recipeVersion, Is.EqualTo(session.recipeVersion));
            Assert.That(XrRecipeAdapter.Validate(recipe.Value, session, Local(session.Case), null), Is.Empty);
        }

        [Test]
        public void IdempotencyKey_IsStableForTheSameAssessment()
        {
            var session = Fixture();
            Assert.That(Submission(session).IdempotencyKey, Is.EqualTo(Submission(session).IdempotencyKey));
            Assert.That(Submission(session, 8).IdempotencyKey, Is.Not.EqualTo(Submission(session).IdempotencyKey));
            session.revision = 1;
            Assert.That(Submission(session).IdempotencyKey, Does.EndWith(":r1"));
        }

        // ------------------------------------------------------------------ farm visuals, launches, arguments

        [Test]
        public void FarmDiseaseAmount_KeepsTheThreeOutcomeBandsDistinct()
        {
            XrTreatmentResult R(string outcome, float final) => new XrTreatmentResult
                { outcome = outcome, improvement = XrImprovement.Of(outcome), initialSeverity = 0.4f, finalSeverity = final };
            var improved = XrImprovement.FarmDiseaseAmount(R("Managed", 0.12f));
            var partial = XrImprovement.FarmDiseaseAmount(R("PartlyManaged", 0.12f));
            var none = XrImprovement.FarmDiseaseAmount(R("NotControlled", 0.12f));
            Assert.That(improved, Is.LessThan(partial));
            Assert.That(partial, Is.LessThan(none));
            Assert.That(none, Is.EqualTo(1f));
            Assert.That(XrImprovement.FarmDiseaseAmount(null), Is.EqualTo(1f));
            Assert.That(XrImprovement.Of("Contained"), Is.EqualTo(XrImprovement.Improved));
        }

        [Test]
        public void Deduper_RefusesReplays_StaysBounded_AndSurvivesARestart()
        {
            var deduper = new XrLaunchDeduper();
            Assert.IsTrue(deduper.TryBegin("launch-1"));
            Assert.IsFalse(deduper.TryBegin("launch-1"));
            Assert.IsFalse(deduper.TryBegin(""));
            var restored = XrLaunchDeduper.Deserialize(deduper.Serialize());
            Assert.IsFalse(restored.TryBegin("launch-1"));
            for (var i = 0; i < XrLaunchDeduper.Capacity + 10; i++)
                restored.TryBegin("bulk-" + i);
            Assert.That(restored.Count, Is.EqualTo(XrLaunchDeduper.Capacity));
            Assert.IsTrue(restored.TryBegin("launch-1"), "the oldest IDs are forgotten first");
        }

        [Test]
        public void Backoff_GrowsAndIsCapped()
        {
            Assert.That(XrBackoff.Delay(0, 0).TotalSeconds, Is.EqualTo(1).Within(1e-6));
            Assert.That(XrBackoff.Delay(3, 0).TotalSeconds, Is.EqualTo(8).Within(1e-6));
            Assert.That(XrBackoff.Delay(50, 0.99).TotalSeconds, Is.LessThanOrEqualTo(XrBackoff.Max * 1.2));
            Assert.That(XrBackoff.Delay(2, 0.5), Is.GreaterThan(XrBackoff.Delay(2, 0)));
        }

        [Test]
        public void LaunchArgs_SelectOfflineFixtureOrConnected()
        {
            var offline = XrLaunchArgs.Parse(new[] { "Player.exe" });
            Assert.IsFalse(offline.IsFixture || offline.IsConnected);

            var fixture = XrLaunchArgs.Parse(new[] { "Player.exe", "-xr-fixture", "-xr-fixture-drop-response" });
            Assert.That(fixture.FixturePath, Is.EqualTo(XrLaunchArgs.DefaultFixture));
            Assert.IsTrue(fixture.DropFirstResponse);

            var connected = XrLaunchArgs.Parse(new[] { "Player.exe", "-xr-api", "https://staging.example.org/" });
            Assert.IsTrue(connected.IsConnected);
            Assert.That(connected.ApiBaseUrl, Is.EqualTo("https://staging.example.org"));
            Assert.That(connected.SocketUrl, Is.EqualTo("wss://staging.example.org/ws/xr"));
        }

        [Test]
        public void ApiAddress_UsesQuestFallbackOnlyWhenNoOverrideIsConfigured()
        {
            var args = XrLaunchArgs.Parse(Array.Empty<string>());
            var config = new XrPlayerConfig();
            Assert.That(config.ResolveApiBaseUrl(args), Is.Null);
            Assert.That(config.ResolveApiBaseUrl(args, XrPlayerConfig.QuestApiBaseUrl), Is.EqualTo(XrPlayerConfig.QuestApiBaseUrl));

            config.apiBaseUrl = "https://config.example.org";
            Assert.That(config.ResolveApiBaseUrl(args, XrPlayerConfig.QuestApiBaseUrl), Is.EqualTo(config.apiBaseUrl));

            args.ApiBaseUrl = "https://command-line.example.org";
            Assert.That(config.ResolveApiBaseUrl(args, XrPlayerConfig.QuestApiBaseUrl), Is.EqualTo(args.ApiBaseUrl));
        }

        // ------------------------------------------------------------------ Enter the field: link, arguments, pipe

        const string Uuid = "3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47";

        [TestCase("cropscanxr://open-field?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47")]
        [TestCase("cropscanxr://open-field/?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47")]
        [TestCase("CropScanXR://Open-Field?scan=3F2B8C1E-9D4A-4B7E-8F21-6C0D5E9A1B47")]
        [TestCase("cropscanxr://open-field?scan=3f2b8c1e%2D9d4a-4b7e-8f21-6c0d5e9a1b47")]
        public void OpenFieldLink_AcceptsOneScanUuid_InCanonicalForm(string link)
        {
            Assert.IsTrue(XrScanLink.TryParseOpenFieldUri(link, out var scanId, out var error), error);
            Assert.That(scanId, Is.EqualTo(Uuid));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("https://open-field?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47")]
        [TestCase("cropscanxr://other?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47")]
        [TestCase("cropscanxr://open-field/lab?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47")]
        [TestCase("cropscanxr://open-field?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47&scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b48")]
        [TestCase("cropscanxr://open-field?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47&x=1")]
        [TestCase("cropscanxr://open-field?id=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47")]
        [TestCase("cropscanxr://open-field?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47#frag")]
        [TestCase("cropscanxr://user@open-field?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47")]
        [TestCase("cropscanxr://open-field:8080?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47")]
        [TestCase("cropscanxr://open-field?scan=not-a-uuid")]
        [TestCase("cropscanxr://open-field?scan={3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47}")]
        [TestCase("cropscanxr://open-field?scan=3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47%20--xr-api%20http://evil")]
        [TestCase("cropscanxr://open-field?scan=")]
        public void OpenFieldLink_RefusesAnythingElse(string link)
        {
            Assert.IsFalse(XrScanLink.TryParseOpenFieldUri(link, out var scanId, out var error));
            Assert.IsNull(scanId);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void ScanArgument_IsReadOnceAndValidated()
        {
            Assert.IsTrue(XrScanLink.TryFindScanArgument(new[] { "Player.exe", "--crop-scan-id", Uuid.ToUpperInvariant() }, out var id, out var error), error);
            Assert.That(id, Is.EqualTo(Uuid));
            Assert.IsFalse(XrScanLink.TryFindScanArgument(new[] { "Player.exe" }, out _, out error));
            Assert.IsNull(error, "no flag is not an error");
            Assert.IsFalse(XrScanLink.TryFindScanArgument(new[] { "Player.exe", "--crop-scan-id" }, out _, out error));
            Assert.That(error, Is.Not.Null);
            Assert.IsFalse(XrScanLink.TryFindScanArgument(new[] { "Player.exe", "--crop-scan-id", "x" }, out _, out error));
            Assert.IsFalse(XrScanLink.TryFindScanArgument(new[] { "--crop-scan-id", Uuid, "--crop-scan-id", Uuid }, out _, out error));
            Assert.That(error, Does.Contain("twice"));
        }

        [Test]
        public void PipeLines_AreOnlyPingOrOpenUuid_AndPipeNamesArePerUser()
        {
            Assert.IsTrue(XrScanLink.TryParsePipeLine("PING", out var ping, out _));
            Assert.IsTrue(ping);
            Assert.IsTrue(XrScanLink.TryParsePipeLine("OPEN " + Uuid, out ping, out var scanId));
            Assert.IsFalse(ping);
            Assert.That(scanId, Is.EqualTo(Uuid));
            Assert.IsFalse(XrScanLink.TryParsePipeLine("OPEN ../../x", out _, out _));
            Assert.IsFalse(XrScanLink.TryParsePipeLine("open " + Uuid, out _, out _));
            Assert.IsFalse(XrScanLink.TryParsePipeLine(new string('A', 500), out _, out _));
            Assert.That(XrScanLink.PipeName("alice"), Is.EqualTo(XrScanLink.PipeName("ALICE")));
            Assert.That(XrScanLink.PipeName("alice"), Is.Not.EqualTo(XrScanLink.PipeName("bob")));
            // The local backend computes the same name in Python: sha256("alice") -> first 8 bytes in hex.
            Assert.That(XrScanLink.PipeName("Alice"), Is.EqualTo("CropScanXR.OpenField.2bd806c97f0e00af"));
            Assert.That(XrScanLink.EditorPipeName("Alice"), Is.EqualTo("CropScanXR.Editor.2bd806c97f0e00af"));
        }

        [Test]
        public void PipeServer_QueuesValidScans_AnswersPing_AndRefusesGarbage()
        {
            var name = "CropScanXR.Test." + Guid.NewGuid().ToString("N");
            using (var server = new XrScanPipeServer(name))
            {
                server.Start();
                string Send(string line)
                {
                    using (var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut))
                    {
                        client.Connect(3000);
                        var writer = new StreamWriter(client) { AutoFlush = true, NewLine = "\n" };
                        var reader = new StreamReader(client);
                        writer.WriteLine(line);
                        return reader.ReadLine();
                    }
                }

                Assert.That(Send("PING"), Is.EqualTo(XrScanLink.Ok));
                Assert.That(Send("OPEN " + Uuid.ToUpperInvariant()), Is.EqualTo(XrScanLink.Ok));
                Assert.That(Send("OPEN not-a-scan"), Does.StartWith(XrScanLink.ErrorPrefix));
                Assert.That(Send("OPEN " + Uuid), Is.EqualTo(XrScanLink.Ok));

                Assert.IsTrue(server.TryDequeue(out var first));
                Assert.That(first, Is.EqualTo(Uuid));
                Assert.IsTrue(server.TryDequeue(out _));
                Assert.IsFalse(server.TryDequeue(out _), "PING and refused lines queue nothing");
                Assert.IsNull(server.Error);
            }
        }

        // ------------------------------------------------------------------ staging preview sessions

        [Test]
        public void PreviewSession_IsBuiltFromAMappedScanOnly()
        {
            var tomato = ScanPreviewXrBackend.SessionFor(new XrScan { id = Uuid, status = "done", disease_id = "Tomato___Early_blight" }, map, out var reason);
            Assert.IsNotNull(tomato, reason);
            Assert.That(tomato.xrSessionId, Is.EqualTo(ScanPreviewXrBackend.SessionPrefix + Uuid));
            Assert.That(XrSessionValidator.Validate(tomato, map, specimens, RecipeSupported), Is.Empty);

            var potato = ScanPreviewXrBackend.SessionFor(new XrScan { id = Uuid, disease_id = "Potato___Early_blight" }, map, out _);
            Assert.That(potato.Case, Is.Not.EqualTo(tomato.Case));

            Assert.IsNull(ScanPreviewXrBackend.SessionFor(new XrScan { id = Uuid, status = "processing" }, map, out reason));
            StringAssert.Contains("processing", reason);
            Assert.IsNull(ScanPreviewXrBackend.SessionFor(new XrScan { id = Uuid, disease_id = "Tomato___healthy" }, map, out reason));
            StringAssert.Contains("healthy", reason);
            Assert.IsNull(ScanPreviewXrBackend.SessionFor(new XrScan { id = Uuid, disease_id = "Squash___Powdery_mildew" }, map, out _));
        }

        [Test]
        public void ScanRecord_ParsesThePublishedShape_WithNullClassification()
        {
            var json = "{\"id\":\"" + Uuid + "\",\"created_at\":\"2026-11-10T10:00:00Z\",\"status\":\"pending\",\"disease_id\":null," +
                       "\"confidence\":null,\"top3\":[],\"model_version\":\"bioclip_vit_b16@v6\",\"info\":null,\"can_enter_field\":false}";
            var scan = JsonUtility.FromJson<XrScan>(json);
            Assert.That(scan.id, Is.EqualTo(Uuid));
            Assert.That(scan.disease_id, Is.Null.Or.Empty);
        }

        [Test]
        public void LocalStore_ReopensTheSameSessionForTheSameScan_AndANewScanGetsItsOwn()
        {
            var store = new FixtureXrBackend(null, Local, null);
            var first = ScanPreviewXrBackend.SessionFor(new XrScan { id = Uuid, disease_id = "Tomato___Early_blight" }, map, out _);
            var stored = store.AddSessionIfMissing(first);
            store.SubmitTreatmentResult(Submission(stored)).Wait();

            var again = store.AddSessionIfMissing(ScanPreviewXrBackend.SessionFor(new XrScan { id = Uuid, disease_id = "Tomato___Early_blight" }, map, out _));
            Assert.That(again.revision, Is.EqualTo(1), "reopening keeps the saved result");
            Assert.That(store.OpenSessionForScan(Uuid).Result.Value.xrSessionId, Is.EqualTo(again.xrSessionId));

            var other = "0b6a3c7e-1111-4c22-9a33-5d44e5f66a77";
            var second = store.AddSessionIfMissing(ScanPreviewXrBackend.SessionFor(new XrScan { id = other, disease_id = "Tomato___Early_blight" }, map, out _));
            Assert.That(second.xrSessionId, Is.Not.EqualTo(again.xrSessionId));
            Assert.That(second.revision, Is.EqualTo(0), "a second scan of the same disease has its own progress");
        }

        // ------------------------------------------------------------------ case lock

        [Test]
        public void LockedCase_RefusesOtherCasesUntilReleased()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(PlantCatalogPath);
            if (catalog == null)
                Assert.Ignore("PlantCatalog.asset not built yet: run BTP > Setup > 2 - Build Plant Assets.");
            var go = new GameObject("SelectionStateTest");
            go.SetActive(false);
            var state = go.AddComponent<SelectionState>();
            try
            {
                state.Bind(catalog);
                typeof(SelectionState).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, null);
                string refused = null;
                state.SelectionRefused += reason => refused = reason;

                Assert.IsTrue(state.LockCase("tomato", "early_blight", "scan open"));
                Assert.IsTrue(state.IsLocked);
                Assert.IsFalse(state.SelectCase("potato", "early_blight"));
                Assert.That(refused, Is.EqualTo("scan open"));
                Assert.IsFalse(state.SelectDisease("late_blight"));
                state.SelectPlant("apple");
                Assert.That((state.Current.PlantId, state.CurrentDiseaseId), Is.EqualTo(("tomato", "early_blight")));
                Assert.IsTrue(state.SelectCase("tomato", "early_blight"));

                state.UnlockCase();
                Assert.IsTrue(state.SelectCase("potato", "early_blight"));
                Assert.That(state.Current.PlantId, Is.EqualTo("potato"));
            }
            finally
            {
                Object.DestroyImmediate(go);
                typeof(SelectionState).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }
        }
    }
}
