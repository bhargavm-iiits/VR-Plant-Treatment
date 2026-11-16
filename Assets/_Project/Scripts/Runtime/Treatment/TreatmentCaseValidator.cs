using System;
using System.Collections.Generic;
using System.Linq;

namespace BTP.Treatment
{
    /// <summary>
    /// Checks a loaded case before its lesson may start: required records, references between
    /// rows, action handlers, tools, target tags, routes, follow-up and asset bindings. Any problem
    /// blocks only that lesson; another disease or a generic treatment is never substituted.
    /// </summary>
    public static class TreatmentCaseValidator
    {
        /// <param name="toolExists">True when the tools table defines the key.</param>
        /// <param name="assetExists">True when the Unity asset catalog resolves the key; null skips asset checks.</param>
        public static List<string> Validate(TreatmentCase c, Func<string, bool> toolExists, Func<string, bool> assetExists)
        {
            var problems = new List<string>();
            if (c == null)
            {
                problems.Add("The case record is missing.");
                return problems;
            }

            if (string.IsNullOrEmpty(c.PlantName) || string.IsNullOrEmpty(c.DiseaseName))
                problems.Add("Display names are missing.");
            if (c.Outcome == TreatmentOutcome.None)
                problems.Add("The realistic outcome is missing.");
            if (c.Environment == null)
                problems.Add($"Environment profile '{c.EnvironmentKey}' is missing.");
            if (c.Steps.Count == 0)
                problems.Add("The case has no lesson steps.");
            if (c.Evidence.Count == 0 || c.Evidence.Any(e => string.IsNullOrEmpty(e.Url)))
                problems.Add("Every case needs at least one evidence source with a URL.");
            if (c.Methods.Count == 0)
                problems.Add("The case lists no management methods.");
            if (c.Methods.Any(m => string.IsNullOrEmpty(m.Limitations)))
                problems.Add("Every management method needs its limitations.");
            if (!c.Steps.Any(s => s.Action == TreatmentActions.Observe))
                problems.Add("The lesson has no follow-up observation.");

            var stepIds = new HashSet<string>();
            foreach (var step in c.Steps)
                if (!stepIds.Add(step.StepId))
                    problems.Add($"Duplicate step ID '{step.StepId}'.");

            var tags = c.AvailableTags();
            var targetIds = new HashSet<string>();
            foreach (var target in c.SceneTargets())
                if (!targetIds.Add(target.TargetId))
                    problems.Add($"Duplicate target ID '{target.TargetId}'.");

            var settableRoutes = new HashSet<string>(c.Steps.SelectMany(s => s.Options).Where(o => o.Correct && !string.IsNullOrEmpty(o.Route))
                .Select(o => o.Route));

            foreach (var step in c.Steps)
            {
                var label = $"Step '{step.StepId}'";
                if (string.IsNullOrEmpty(step.Title) || string.IsNullOrEmpty(step.Instruction))
                    problems.Add($"{label} has no title or instruction.");
                if (!TreatmentActions.All.Contains(step.Action))
                    problems.Add($"{label} uses action '{step.Action}', which has no handler.");
                foreach (var prerequisite in step.Prerequisites)
                    if (!stepIds.Contains(prerequisite))
                        problems.Add($"{label} requires unknown step '{prerequisite}'.");
                if (!string.IsNullOrEmpty(step.Route) && !settableRoutes.Contains(step.Route))
                    problems.Add($"{label} belongs to route '{step.Route}', which no choice selects.");

                if (TreatmentActions.IsPanelAction(step.Action))
                {
                    if (step.Action == TreatmentActions.Choose && !step.Options.Any(o => o.Correct))
                        problems.Add($"{label} is a choice with no valid option.");
                    continue;
                }

                if (string.IsNullOrEmpty(step.TargetTag))
                    problems.Add($"{label} has no target.");
                else if (!tags.Contains(step.TargetTag))
                    problems.Add($"{label} targets '{step.TargetTag}', which this case does not place in the scene.");
                if (!string.IsNullOrEmpty(step.ToolKey))
                {
                    if (toolExists != null && !toolExists(step.ToolKey))
                        problems.Add($"{label} uses unknown tool '{step.ToolKey}'.");
                    if (!c.RackTools.Contains(step.ToolKey))
                        problems.Add($"{label} needs '{step.ToolKey}', which is not on this case's tool rack.");
                }

                if (step.Action == TreatmentActions.Observe && c.FindFollowUp(step.CheckpointId) == null)
                    problems.Add($"{label} records follow-up '{step.CheckpointId}', which is missing.");
            }

            // Removed material must always reach the disposal container before the lesson can end.
            for (var i = 0; i < c.Steps.Count; i++)
            {
                var action = c.Steps[i].Action;
                if ((action == TreatmentActions.Cut || action == TreatmentActions.Collect) &&
                    !c.Steps.Skip(i + 1).Any(s => s.Action == TreatmentActions.Dispose))
                    problems.Add($"Step '{c.Steps[i].StepId}' removes material but no later step disposes of it.");
            }

            // After the infected original is removed, its attached parts leave with it, so later steps
            // must target something that stays in the scene.
            var removal = c.Steps.FindIndex(s => s.Action == TreatmentActions.RemovePlant);
            if (removal >= 0)
                foreach (var step in c.Steps.Skip(removal + 1))
                {
                    var placed = c.Targets.Where(t => t.Tag == step.TargetTag).ToList();
                    if (placed.Count > 0 && placed.All(t => t.Slot.StartsWith("plant_") || t.Slot.StartsWith("leaf_") || t.Slot == "root_zone"))
                        problems.Add($"Step '{step.StepId}' targets '{step.TargetTag}' on the removed original plant.");
                }

            foreach (var target in c.Targets)
            {
                if (!string.IsNullOrEmpty(target.ShowAfterStep) && !stepIds.Contains(target.ShowAfterStep))
                    problems.Add($"Target '{target.TargetId}' appears after unknown step '{target.ShowAfterStep}'.");
                if (!string.IsNullOrEmpty(target.HideAfterStep) && !stepIds.Contains(target.HideAfterStep))
                    problems.Add($"Target '{target.TargetId}' hides after unknown step '{target.HideAfterStep}'.");
            }

            // A replacement lesson must end with a distinct new plant, never a cured original.
            if (c.Outcome == TreatmentOutcome.Replaced)
            {
                if (!c.Steps.Any(s => s.Action == TreatmentActions.Plant))
                    problems.Add("A replacement lesson needs a step that plants clean stock.");
                if (!c.Steps.Any(s => s.Action == TreatmentActions.Contain || s.Action == TreatmentActions.RemovePlant))
                    problems.Add("A replacement lesson needs the infected original to be contained or removed.");
                if (c.Steps.Any(s => s.Action == TreatmentActions.Spray && s.TargetTag == "infected_plant"))
                    problems.Add("A replacement lesson must not treat the infected original as curable.");
            }

            if (assetExists != null)
            {
                void RequireAsset(string key, string what)
                {
                    if (string.IsNullOrEmpty(key))
                        problems.Add($"{what} has no asset key.");
                    else if (!assetExists(key))
                        problems.Add($"{what} uses asset '{key}', which the Unity asset catalog does not bind.");
                }

                RequireAsset(c.SpecimenAsset, "The specimen");
                if (c.Environment != null)
                    RequireAsset(c.Environment.PrefabKey, "The environment");
                foreach (var target in c.SceneTargets())
                {
                    RequireAsset(target.AssetKey, $"Target '{target.TargetId}'");
                    if (!string.IsNullOrEmpty(target.MaterialKey) && !assetExists(target.MaterialKey))
                        problems.Add($"Target '{target.TargetId}' uses material '{target.MaterialKey}', which the asset catalog does not bind.");
                }
            }

            return problems;
        }

        /// <summary>
        /// Checks a case's manual-treatment profile: sane ranges, step &gt; 0, recommendation inside min/max, start inside
        /// the range and below the recommendation (never pre-selected), controls derived from the case's own methods,
        /// rules referring to real controls, and outcome-honest recovery caps. Any problem makes only this case's manual
        /// mode unavailable; another disease's data is never substituted.
        /// </summary>
        public static List<string> ValidateManual(TreatmentCase c)
        {
            var problems = new List<string>();
            if (c == null)
            {
                problems.Add("The case record is missing.");
                return problems;
            }

            var m = c.Manual;
            if (m == null)
            {
                problems.Add(c.ManualError ?? $"No manual treatment profile exists for {c.Key}.");
                return problems;
            }

            if (m.Key != c.Key)
                problems.Add($"The manual profile belongs to {m.Key}, not {c.Key}.");
            if (m.Symptoms.Count == 0)
                problems.Add("The manual profile lists no symptoms.");
            if (string.IsNullOrEmpty(m.RecoveryNote))
                problems.Add("The manual profile has no recovery note.");

            bool Unit(float v) => v >= 0f && v <= 1f && !float.IsNaN(v);
            foreach (var (value, name) in new[]
                     {
                         (m.InitialSeverity, "initial severity"), (m.InitialHealth, "initial health"), (m.MaxSeverityDrop, "severity drop cap"),
                         (m.MaxHealthGain, "health gain cap"), (m.MaxWorsen, "worsening cap"), (m.MaxSpotFade, "spot fade cap"),
                         (m.MaxYellowFade, "yellowing fade cap"), (m.MaxGreenGain, "green gain cap"), (m.MaxVeinGain, "vein gain cap")
                     })
                if (!Unit(value))
                    problems.Add($"The {name} must be between 0 and 1.");
            if (m.InitialSeverity <= 0f)
                problems.Add("A diseased specimen needs an initial severity above 0.");
            if (m.InitialHealth >= 1f)
                problems.Add("A diseased specimen cannot start fully healthy.");

            if (c.Outcome == TreatmentOutcome.Replaced)
            {
                // Treatment cannot save an infected original: no improvement is allowed.
                if (m.MaxSeverityDrop > 0f || m.MaxHealthGain > 0f || m.MaxSpotFade > 0f || m.MaxYellowFade > 0f || m.MaxGreenGain > 0f || m.MaxVeinGain > 0f)
                    problems.Add("A replacement case must not let treatment improve the infected leaf.");
                var containment = m.FindControl("containment");
                if (containment == null || !containment.Required)
                    problems.Add("A replacement case needs a required 'containment' control.");
                else if (!m.Rules.Any(r => r.Blocks && r.ControlA == "containment"))
                    problems.Add("A replacement case needs a blocking rule for missing containment.");
            }
            else
            {
                // Dead lesion tissue never disappears.
                if (m.MaxSpotFade > 0.8f)
                    problems.Add("Spot fading is capped at 0.8: lesions must never fully vanish.");
                if (m.MaxSeverityDrop >= 1f)
                    problems.Add("Severity cannot drop to zero: old lesions remain.");
                if (m.InitialHealth + m.MaxHealthGain >= 1f)
                    problems.Add("Health cannot reach 100%: old lesions remain.");
            }

            if (m.Controls.Count < 2)
                problems.Add("The manual profile needs at least two treatment controls.");
            if (!m.Controls.Any(k => k.Required))
                problems.Add("The manual profile needs at least one required control.");

            var ids = new HashSet<string>();
            var methodIds = new HashSet<string>(c.Methods.Select(x => x.MethodId));
            foreach (var k in m.Controls)
            {
                var label = $"Control '{k.ControlId}'";
                if (string.IsNullOrEmpty(k.ControlId) || !ids.Add(k.ControlId))
                    problems.Add($"{label} has a missing or duplicate ID.");
                if (string.IsNullOrEmpty(k.Name) || string.IsNullOrEmpty(k.Description))
                    problems.Add($"{label} has no name or description.");
                if (!methodIds.Contains(k.MethodId))
                    problems.Add($"{label} refers to method '{k.MethodId}', which this case does not list.");
                if (!(k.Step > 0f))
                {
                    problems.Add($"{label} needs a step above 0.");
                    continue;
                }

                if (!(k.Max > k.Min))
                    problems.Add($"{label} needs a maximum above its minimum.");
                if (k.RecommendedMin > k.RecommendedMax)
                    problems.Add($"{label} has a recommended minimum above its recommended maximum.");
                if (k.RecommendedMin < k.Min || k.RecommendedMax > k.Max)
                    problems.Add($"{label} recommends a range outside its limits.");
                if (k.Start < k.Min || k.Start > k.Max)
                    problems.Add($"{label} starts outside its limits.");
                if (k.Start >= k.RecommendedMin)
                    problems.Add($"{label} starts inside or above the recommended range; the recommendation must never be pre-selected.");
                foreach (var (value, name) in new[] { (k.Start, "start"), (k.RecommendedMin, "recommended minimum"), (k.RecommendedMax, "recommended maximum"), (k.Max, "maximum") })
                    if (Math.Abs((value - k.Min) / k.Step - Math.Round((value - k.Min) / k.Step)) > 1e-3)
                        problems.Add($"{label} {name} is not on its step grid.");
                if (!(k.Weight > 0f))
                    problems.Add($"{label} needs a weight above 0.");
                if (string.IsNullOrEmpty(k.FeedbackLow) || string.IsNullOrEmpty(k.FeedbackHigh) || string.IsNullOrEmpty(k.FeedbackOk))
                    problems.Add($"{label} needs low, high and in-range feedback.");
            }

            var ruleIds = new HashSet<string>();
            foreach (var r in m.Rules)
            {
                var label = $"Rule '{r.RuleId}'";
                if (string.IsNullOrEmpty(r.RuleId) || !ruleIds.Add(r.RuleId))
                    problems.Add($"{label} has a missing or duplicate ID.");
                if (m.FindControl(r.ControlA) == null)
                    problems.Add($"{label} refers to unknown control '{r.ControlA}'.");
                if (!string.IsNullOrEmpty(r.ControlB) && m.FindControl(r.ControlB) == null)
                    problems.Add($"{label} refers to unknown control '{r.ControlB}'.");
                if (r.AMin > r.AMax || (!string.IsNullOrEmpty(r.ControlB) && r.BMin > r.BMax))
                    problems.Add($"{label} has an inverted range.");
                if (r.Penalty < 0f || r.Penalty > 1f)
                    problems.Add($"{label} penalty must be between 0 and 1.");
                if (string.IsNullOrEmpty(r.Feedback))
                    problems.Add($"{label} has no feedback.");
            }

            return problems;
        }
    }
}