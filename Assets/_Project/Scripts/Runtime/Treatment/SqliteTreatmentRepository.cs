using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using KeepAttribute = UnityEngine.Scripting.PreserveAttribute;

namespace BTP.Treatment
{
    /// <summary>Read access to the bundled treatment content.</summary>
    public interface ITreatmentRepository : IDisposable
    {
        bool IsReady { get; }
        /// <summary>Why the content could not be opened; null when ready.</summary>
        string Error { get; }
        int ContentVersion { get; }
        Task InitializeAsync();
        /// <summary>Every case in the database, in plant then display order.</summary>
        IReadOnlyList<CaseSummary> ListCases();
        /// <summary>Loads one case with parameterised queries. Returns null (with <paramref name="error"/>) when missing.</summary>
        TreatmentCase GetCase(CaseKey key, out string error);
        TreatmentTool GetTool(string toolKey);
    }

    /// <summary>
    /// Treatment content stored in a read-only SQLite database (built from
    /// Assets/_Project/Data/Treatment/treatment_content.sql). Every lesson's instructions, methods,
    /// prerequisites, completion rules, follow-up, outcome and environment configuration are read
    /// from here; no disease-specific advice lives in C#.
    /// </summary>
    public sealed class SqliteTreatmentRepository : ITreatmentRepository
    {
        readonly Func<SQLiteConnection> open;
        SQLiteConnection db;
        readonly Dictionary<string, TreatmentTool> tools = new Dictionary<string, TreatmentTool>();
        readonly Dictionary<string, TreatmentEnvironment> environments = new Dictionary<string, TreatmentEnvironment>();
        List<CaseSummary> summaries = new List<CaseSummary>();

        /// <param name="open">Opens a connection to the content database (e.g. SQLiteAsset.CreateConnection).</param>
        public SqliteTreatmentRepository(Func<SQLiteConnection> open)
        {
            this.open = open;
        }

        public bool IsReady { get; private set; }
        public string Error { get; private set; }
        public int ContentVersion { get; private set; }

        public Task InitializeAsync()
        {
            try
            {
                Initialize();
            }
            catch (Exception exception)
            {
                Fail("Treatment content could not be opened: " + exception.Message);
            }

            return Task.CompletedTask;
        }

        void Initialize()
        {
            Dispose();
            if (open == null)
            {
                Fail("No treatment content database is assigned.");
                return;
            }

            db = open();
            if (db == null)
            {
                Fail("The treatment content database is missing.");
                return;
            }

            var version = db.ExecuteScalar<string>("SELECT value FROM content_info WHERE key = ?", "content_version");
            ContentVersion = int.TryParse(version, out var parsed) ? parsed : 0;

            tools.Clear();
            foreach (var row in db.Query<ToolRow>("SELECT tool_key, name, asset_key, description FROM tools"))
                tools[row.tool_key] = new TreatmentTool { ToolKey = row.tool_key, Name = row.name, AssetKey = row.asset_key, Description = row.description };

            environments.Clear();
            foreach (var row in db.Query<EnvironmentRow>("SELECT environment_key, display_name, prefab_key, description FROM environments"))
                environments[row.environment_key] = new TreatmentEnvironment
                {
                    EnvironmentKey = row.environment_key, DisplayName = row.display_name, PrefabKey = row.prefab_key, Description = row.description
                };
            foreach (var row in db.Query<FixtureRow>(
                         "SELECT environment_key, target_id, target_tag, asset_key, slot, label, module_key FROM environment_fixtures ORDER BY rowid"))
            {
                if (!environments.TryGetValue(row.environment_key, out var environment))
                    continue;
                environment.Fixtures.Add(new TreatmentTargetDef
                {
                    TargetId = row.target_id, Tag = row.target_tag, AssetKey = row.asset_key, Slot = row.slot, Label = row.label, Module = row.module_key
                });
            }

            summaries = new List<CaseSummary>();
            foreach (var row in db.Query<CaseRow>(
                         "SELECT plant_id, disease_id, plant_name, disease_name, category, environment_key FROM cases ORDER BY plant_name, rowid"))
                summaries.Add(new CaseSummary
                {
                    Key = new CaseKey(row.plant_id, row.disease_id), PlantName = row.plant_name, DiseaseName = row.disease_name,
                    Category = row.category, EnvironmentKey = row.environment_key
                });

            if (summaries.Count == 0)
            {
                Fail("The treatment content database contains no cases.");
                return;
            }

            IsReady = true;
            Error = null;
        }

        void Fail(string message)
        {
            IsReady = false;
            Error = message;
        }

        public IReadOnlyList<CaseSummary> ListCases() => summaries;

        public TreatmentTool GetTool(string toolKey) =>
            toolKey != null && tools.TryGetValue(toolKey, out var tool) ? tool : null;

        public TreatmentCase GetCase(CaseKey key, out string error)
        {
            error = null;
            if (!IsReady)
            {
                error = Error ?? "Treatment content is not ready.";
                return null;
            }

            if (!key.IsValid)
            {
                error = "No plant and disease are selected.";
                return null;
            }

            try
            {
                return LoadCase(key, out error);
            }
            catch (Exception exception)
            {
                error = $"Treatment data for {key} could not be read: {exception.Message}";
                return null;
            }
        }

        TreatmentCase LoadCase(CaseKey key, out string error)
        {
            error = null;
            var p = key.PlantId;
            var d = key.DiseaseId;
            var rows = db.Query<CaseRow>(
                "SELECT plant_id, disease_id, plant_name, disease_name, category, scenario, outcome, environment_key, specimen_asset, " +
                "specimen_scale, summary, result_text, content_version FROM cases WHERE plant_id = ? AND disease_id = ?", p, d);
            if (rows.Count == 0)
            {
                error = $"No treatment record exists for {key}.";
                return null;
            }

            var row = rows[0];
            var result = new TreatmentCase
            {
                Key = key,
                PlantName = row.plant_name,
                DiseaseName = row.disease_name,
                Category = row.category,
                Scenario = row.scenario,
                Outcome = Enum.TryParse(row.outcome, out TreatmentOutcome outcome) ? outcome : TreatmentOutcome.None,
                EnvironmentKey = row.environment_key,
                SpecimenAsset = row.specimen_asset,
                SpecimenScale = row.specimen_scale <= 0 ? 1f : (float)row.specimen_scale,
                Summary = row.summary,
                ResultText = row.result_text,
                ContentVersion = row.content_version,
            };
            environments.TryGetValue(row.environment_key ?? string.Empty, out result.Environment);

            foreach (var module in db.Query<KeyRow>("SELECT module_key AS value FROM case_modules WHERE plant_id = ? AND disease_id = ?", p, d))
                result.Modules.Add(module.value);
            foreach (var tool in db.Query<KeyRow>("SELECT tool_key AS value FROM case_tools WHERE plant_id = ? AND disease_id = ? ORDER BY rowid", p, d))
                result.RackTools.Add(tool.value);

            foreach (var target in db.Query<TargetRow>(
                         "SELECT target_id, target_tag, asset_key, material_key, slot, label, show_after_step, hide_after_step " +
                         "FROM case_targets WHERE plant_id = ? AND disease_id = ? ORDER BY rowid", p, d))
                result.Targets.Add(new TreatmentTargetDef
                {
                    TargetId = target.target_id, Tag = target.target_tag, AssetKey = target.asset_key, MaterialKey = target.material_key,
                    Slot = target.slot, Label = target.label, ShowAfterStep = target.show_after_step, HideAfterStep = target.hide_after_step
                });

            foreach (var method in db.Query<MethodRow>(
                         "SELECT method_id, name, purpose, limitations FROM methods WHERE plant_id = ? AND disease_id = ? ORDER BY rowid", p, d))
                result.Methods.Add(new TreatmentMethod { MethodId = method.method_id, Name = method.name, Purpose = method.purpose, Limitations = method.limitations });

            foreach (var step in db.Query<StepRow>(
                         "SELECT step_id, ord, phase, title, instruction, action, tool_key, target_tag, required_count, route, method_id, " +
                         "checkpoint_id, contaminates, requires_clean_tool, success_text, wrong_tool_feedback, wrong_target_feedback, " +
                         "prerequisite_feedback FROM steps WHERE plant_id = ? AND disease_id = ? ORDER BY ord", p, d))
                result.Steps.Add(new TreatmentStep
                {
                    StepId = step.step_id, Order = step.ord, Phase = step.phase, Title = step.title, Instruction = step.instruction,
                    Action = step.action, ToolKey = step.tool_key, TargetTag = step.target_tag, RequiredCount = Math.Max(1, step.required_count),
                    Route = step.route, MethodId = step.method_id, CheckpointId = step.checkpoint_id, Contaminates = step.contaminates != 0,
                    RequiresCleanTool = step.requires_clean_tool != 0, SuccessText = step.success_text, WrongToolFeedback = step.wrong_tool_feedback,
                    WrongTargetFeedback = step.wrong_target_feedback, PrerequisiteFeedback = step.prerequisite_feedback,
                });

            foreach (var prerequisite in db.Query<PrerequisiteRow>(
                         "SELECT step_id, requires_step_id FROM step_prereqs WHERE plant_id = ? AND disease_id = ?", p, d))
                result.FindStep(prerequisite.step_id)?.Prerequisites.Add(prerequisite.requires_step_id);

            foreach (var option in db.Query<OptionRow>(
                         "SELECT step_id, option_id, ord, label, correct, route, feedback FROM step_options " +
                         "WHERE plant_id = ? AND disease_id = ? ORDER BY step_id, ord", p, d))
                result.FindStep(option.step_id)?.Options.Add(new TreatmentOption
                {
                    OptionId = option.option_id, Order = option.ord, Label = option.label, Correct = option.correct != 0,
                    Route = option.route, Feedback = option.feedback
                });

            foreach (var followUp in db.Query<FollowUpRow>(
                         "SELECT checkpoint_id, prompt, finding, escalation FROM followups WHERE plant_id = ? AND disease_id = ? ORDER BY rowid", p, d))
                result.FollowUps.Add(new TreatmentFollowUp
                {
                    CheckpointId = followUp.checkpoint_id, Prompt = followUp.prompt, Finding = followUp.finding, Escalation = followUp.escalation
                });

            foreach (var evidence in db.Query<EvidenceRow>(
                         "SELECT publisher, url, reviewed, region, limitations FROM evidence WHERE plant_id = ? AND disease_id = ? ORDER BY rowid", p, d))
                result.Evidence.Add(new TreatmentEvidence
                {
                    Publisher = evidence.publisher, Url = evidence.url, Reviewed = evidence.reviewed, Region = evidence.region, Limitations = evidence.limitations
                });

            LoadManual(result);
            return result;
        }

        /// <summary>
        /// Manual-treatment data for the same case. A missing table or row leaves <see cref="TreatmentCase.Manual"/> null
        /// with <see cref="TreatmentCase.ManualError"/> set: only the manual mode becomes unavailable, never the step lesson.
        /// </summary>
        void LoadManual(TreatmentCase result)
        {
            var p = result.Key.PlantId;
            var d = result.Key.DiseaseId;
            try
            {
                var profiles = db.Query<ProfileRow>(
                    "SELECT description, symptoms, initial_severity, initial_health, max_severity_drop, max_health_gain, max_worsen, " +
                    "max_spot_fade, max_yellow_fade, max_green_gain, max_vein_gain, recovery_note FROM case_profile WHERE plant_id = ? AND disease_id = ?", p, d);
                if (profiles.Count == 0)
                {
                    result.ManualError = $"No manual treatment profile exists for {result.Key}.";
                    return;
                }

                var row = profiles[0];
                var profile = new ManualTreatmentProfile
                {
                    Key = result.Key, Description = row.description, InitialSeverity = (float)row.initial_severity, InitialHealth = (float)row.initial_health,
                    MaxSeverityDrop = (float)row.max_severity_drop, MaxHealthGain = (float)row.max_health_gain, MaxWorsen = (float)row.max_worsen,
                    MaxSpotFade = (float)row.max_spot_fade, MaxYellowFade = (float)row.max_yellow_fade, MaxGreenGain = (float)row.max_green_gain,
                    MaxVeinGain = (float)row.max_vein_gain, RecoveryNote = row.recovery_note,
                };
                if (!string.IsNullOrEmpty(row.symptoms))
                    foreach (var symptom in row.symptoms.Split('|'))
                        if (!string.IsNullOrWhiteSpace(symptom))
                            profile.Symptoms.Add(symptom.Trim());

                foreach (var control in db.Query<ControlRow>(
                             "SELECT control_id, ord, method_id, name, description, min_value, max_value, step_value, start_value, rec_min, rec_max, " +
                             "weight, required, feedback_low, feedback_high, feedback_ok FROM treatment_controls WHERE plant_id = ? AND disease_id = ? ORDER BY ord", p, d))
                    profile.Controls.Add(new TreatmentControlDef
                    {
                        ControlId = control.control_id, Order = control.ord, MethodId = control.method_id, Name = control.name, Description = control.description,
                        Min = (float)control.min_value, Max = (float)control.max_value, Step = (float)control.step_value, Start = (float)control.start_value,
                        RecommendedMin = (float)control.rec_min, RecommendedMax = (float)control.rec_max, Weight = (float)control.weight,
                        Required = control.required != 0, FeedbackLow = control.feedback_low, FeedbackHigh = control.feedback_high, FeedbackOk = control.feedback_ok,
                    });

                foreach (var r in db.Query<RuleRow>(
                             "SELECT rule_id, control_a, a_min, a_max, control_b, b_min, b_max, penalty, blocks, feedback FROM control_rules " +
                             "WHERE plant_id = ? AND disease_id = ? ORDER BY rowid", p, d))
                    profile.Rules.Add(new TreatmentControlRule
                    {
                        RuleId = r.rule_id, ControlA = r.control_a, AMin = (float)r.a_min, AMax = (float)r.a_max, ControlB = r.control_b,
                        BMin = (float)(r.b_min ?? 0d), BMax = (float)(r.b_max ?? 0d), Penalty = (float)r.penalty, Blocks = r.blocks != 0, Feedback = r.feedback,
                    });

                result.Manual = profile;
            }
            catch (Exception exception)
            {
                result.Manual = null;
                result.ManualError = $"Manual treatment data for {result.Key} could not be read: {exception.Message}";
            }
        }

        public void Dispose()
        {
            db?.Dispose();
            db = null;
            IsReady = false;
        }

        // Row types for sqlite-net mapping. [Keep] keeps them under IL2CPP code stripping.
#pragma warning disable IDE1006, CS0649
        [Keep] sealed class KeyRow { [Keep] public string value { get; set; } }
        [Keep] sealed class ToolRow
        {
            [Keep] public string tool_key { get; set; }
            [Keep] public string name { get; set; }
            [Keep] public string asset_key { get; set; }
            [Keep] public string description { get; set; }
        }
        [Keep] sealed class EnvironmentRow
        {
            [Keep] public string environment_key { get; set; }
            [Keep] public string display_name { get; set; }
            [Keep] public string prefab_key { get; set; }
            [Keep] public string description { get; set; }
        }
        [Keep] sealed class FixtureRow
        {
            [Keep] public string environment_key { get; set; }
            [Keep] public string target_id { get; set; }
            [Keep] public string target_tag { get; set; }
            [Keep] public string asset_key { get; set; }
            [Keep] public string slot { get; set; }
            [Keep] public string label { get; set; }
            [Keep] public string module_key { get; set; }
        }
        [Keep] sealed class CaseRow
        {
            [Keep] public string plant_id { get; set; }
            [Keep] public string disease_id { get; set; }
            [Keep] public string plant_name { get; set; }
            [Keep] public string disease_name { get; set; }
            [Keep] public string category { get; set; }
            [Keep] public string scenario { get; set; }
            [Keep] public string outcome { get; set; }
            [Keep] public string environment_key { get; set; }
            [Keep] public string specimen_asset { get; set; }
            [Keep] public double specimen_scale { get; set; }
            [Keep] public string summary { get; set; }
            [Keep] public string result_text { get; set; }
            [Keep] public int content_version { get; set; }
        }
        [Keep] sealed class TargetRow
        {
            [Keep] public string target_id { get; set; }
            [Keep] public string target_tag { get; set; }
            [Keep] public string asset_key { get; set; }
            [Keep] public string material_key { get; set; }
            [Keep] public string slot { get; set; }
            [Keep] public string label { get; set; }
            [Keep] public string show_after_step { get; set; }
            [Keep] public string hide_after_step { get; set; }
        }
        [Keep] sealed class MethodRow
        {
            [Keep] public string method_id { get; set; }
            [Keep] public string name { get; set; }
            [Keep] public string purpose { get; set; }
            [Keep] public string limitations { get; set; }
        }
        [Keep] sealed class StepRow
        {
            [Keep] public string step_id { get; set; }
            [Keep] public int ord { get; set; }
            [Keep] public string phase { get; set; }
            [Keep] public string title { get; set; }
            [Keep] public string instruction { get; set; }
            [Keep] public string action { get; set; }
            [Keep] public string tool_key { get; set; }
            [Keep] public string target_tag { get; set; }
            [Keep] public int required_count { get; set; }
            [Keep] public string route { get; set; }
            [Keep] public string method_id { get; set; }
            [Keep] public string checkpoint_id { get; set; }
            [Keep] public int contaminates { get; set; }
            [Keep] public int requires_clean_tool { get; set; }
            [Keep] public string success_text { get; set; }
            [Keep] public string wrong_tool_feedback { get; set; }
            [Keep] public string wrong_target_feedback { get; set; }
            [Keep] public string prerequisite_feedback { get; set; }
        }
        [Keep] sealed class PrerequisiteRow
        {
            [Keep] public string step_id { get; set; }
            [Keep] public string requires_step_id { get; set; }
        }
        [Keep] sealed class OptionRow
        {
            [Keep] public string step_id { get; set; }
            [Keep] public string option_id { get; set; }
            [Keep] public int ord { get; set; }
            [Keep] public string label { get; set; }
            [Keep] public int correct { get; set; }
            [Keep] public string route { get; set; }
            [Keep] public string feedback { get; set; }
        }
        [Keep] sealed class FollowUpRow
        {
            [Keep] public string checkpoint_id { get; set; }
            [Keep] public string prompt { get; set; }
            [Keep] public string finding { get; set; }
            [Keep] public string escalation { get; set; }
        }
        [Keep] sealed class EvidenceRow
        {
            [Keep] public string publisher { get; set; }
            [Keep] public string url { get; set; }
            [Keep] public string reviewed { get; set; }
            [Keep] public string region { get; set; }
            [Keep] public string limitations { get; set; }
        }
        [Keep] sealed class ProfileRow
        {
            [Keep] public string description { get; set; }
            [Keep] public string symptoms { get; set; }
            [Keep] public double initial_severity { get; set; }
            [Keep] public double initial_health { get; set; }
            [Keep] public double max_severity_drop { get; set; }
            [Keep] public double max_health_gain { get; set; }
            [Keep] public double max_worsen { get; set; }
            [Keep] public double max_spot_fade { get; set; }
            [Keep] public double max_yellow_fade { get; set; }
            [Keep] public double max_green_gain { get; set; }
            [Keep] public double max_vein_gain { get; set; }
            [Keep] public string recovery_note { get; set; }
        }
        [Keep] sealed class ControlRow
        {
            [Keep] public string control_id { get; set; }
            [Keep] public int ord { get; set; }
            [Keep] public string method_id { get; set; }
            [Keep] public string name { get; set; }
            [Keep] public string description { get; set; }
            [Keep] public double min_value { get; set; }
            [Keep] public double max_value { get; set; }
            [Keep] public double step_value { get; set; }
            [Keep] public double start_value { get; set; }
            [Keep] public double rec_min { get; set; }
            [Keep] public double rec_max { get; set; }
            [Keep] public double weight { get; set; }
            [Keep] public int required { get; set; }
            [Keep] public string feedback_low { get; set; }
            [Keep] public string feedback_high { get; set; }
            [Keep] public string feedback_ok { get; set; }
        }
        [Keep] sealed class RuleRow
        {
            [Keep] public string rule_id { get; set; }
            [Keep] public string control_a { get; set; }
            [Keep] public double a_min { get; set; }
            [Keep] public double a_max { get; set; }
            [Keep] public string control_b { get; set; }
            [Keep] public double? b_min { get; set; }
            [Keep] public double? b_max { get; set; }
            [Keep] public double penalty { get; set; }
            [Keep] public int blocks { get; set; }
            [Keep] public string feedback { get; set; }
        }
#pragma warning restore IDE1006, CS0649
    }
}