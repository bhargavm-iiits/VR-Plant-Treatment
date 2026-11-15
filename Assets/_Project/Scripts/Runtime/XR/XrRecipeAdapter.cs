using System;
using System.Collections.Generic;
using System.Linq;
using BTP.Treatment;

namespace BTP.XR
{
    /// <summary>
    /// Bridges the backend's pinned treatment recipe and Unity's local treatment content. The recipe carries the
    /// structure the server validates against (control ranges, recommended bands, weights, rules, starting state); the
    /// learner-facing text and the scene assets stay in the bundled SQLite content. <see cref="Validate"/> refuses a
    /// recipe that does not fit this build; <see cref="Apply"/> returns a copy of the local case with the recipe's numbers,
    /// which TreatmentServices then serves for the connected session.
    /// </summary>
    public static class XrRecipeAdapter
    {
        const float Epsilon = 1e-4f;

        /// <summary>Exports a local case as a recipe: the seed the backend stores, and the fixture's recipe.</summary>
        public static XrRecipe FromCase(TreatmentCase c, string recipeId, int recipeVersion, string source)
        {
            if (c?.Manual == null)
                return null;
            var m = c.Manual;
            return new XrRecipe
            {
                recipeId = recipeId,
                recipeVersion = recipeVersion,
                plantId = c.Key.PlantId,
                diseaseId = c.Key.DiseaseId,
                contentVersion = c.ContentVersion,
                source = source,
                initialSeverity = m.InitialSeverity,
                initialHealth = m.InitialHealth,
                maxSeverityDrop = m.MaxSeverityDrop,
                maxHealthGain = m.MaxHealthGain,
                maxWorsen = m.MaxWorsen,
                controls = m.Controls.Select(k => new XrRecipeControl
                {
                    controlId = k.ControlId, order = k.Order, min = k.Min, max = k.Max, step = k.Step, start = k.Start,
                    recommendedMin = k.RecommendedMin, recommendedMax = k.RecommendedMax, weight = k.Weight, required = k.Required
                }).ToArray(),
                rules = m.Rules.Select(r => new XrRecipeRule
                {
                    ruleId = r.RuleId, controlA = r.ControlA, aMin = r.AMin, aMax = r.AMax, controlB = r.ControlB, bMin = r.BMin, bMax = r.BMax,
                    penalty = r.Penalty, blocks = r.Blocks
                }).ToArray(),
                assetKeys = AssetKeys(c).ToArray(),
            };
        }

        /// <summary>The Unity asset keys a case's scene needs.</summary>
        public static IEnumerable<string> AssetKeys(TreatmentCase c)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(c.SpecimenAsset))
                keys.Add(c.SpecimenAsset);
            foreach (var target in c.Targets)
            {
                if (!string.IsNullOrEmpty(target.AssetKey))
                    keys.Add(target.AssetKey);
                if (!string.IsNullOrEmpty(target.MaterialKey))
                    keys.Add(target.MaterialKey);
            }

            return keys;
        }

        /// <param name="assetExists">True when the local TreatmentAssetCatalog binds the key; null skips asset checks.</param>
        public static List<string> Validate(XrRecipe recipe, XrSessionData session, TreatmentCase local, Func<string, bool> assetExists)
        {
            var problems = new List<string>();
            if (recipe == null)
            {
                problems.Add("The treatment recipe could not be read.");
                return problems;
            }

            if (session != null)
            {
                if (recipe.recipeId != session.recipeId)
                    problems.Add($"The recipe is '{recipe.recipeId}', but the session pins '{session.recipeId}'.");
                if (recipe.recipeVersion != session.recipeVersion)
                    problems.Add($"The recipe is version {recipe.recipeVersion}, but the session pins version {session.recipeVersion}.");
                if (recipe.Case != session.Case)
                    problems.Add($"The recipe is for {recipe.Case}, but the session is {session.Case}.");
            }

            if (local == null)
            {
                problems.Add($"This build has no local treatment content for {recipe.Case}.");
                return problems;
            }

            if (local.Manual == null)
            {
                problems.Add(local.ManualError ?? $"This build has no manual treatment profile for {recipe.Case}.");
                return problems;
            }

            if (recipe.Case != local.Key)
                problems.Add($"The recipe is for {recipe.Case}, not the local case {local.Key}.");

            bool Unit(float v) => v >= 0f && v <= 1f && !float.IsNaN(v);
            if (!Unit(recipe.initialSeverity) || !Unit(recipe.initialHealth) || !Unit(recipe.maxSeverityDrop) || !Unit(recipe.maxHealthGain) || !Unit(recipe.maxWorsen))
                problems.Add("The recipe's starting state and caps must be between 0 and 1.");

            var controls = recipe.controls ?? Array.Empty<XrRecipeControl>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in controls)
            {
                if (k == null || string.IsNullOrEmpty(k.controlId))
                {
                    problems.Add("A recipe control has no controlId.");
                    continue;
                }

                var label = $"Control '{k.controlId}'";
                if (!ids.Add(k.controlId))
                    problems.Add($"{label} is listed twice.");
                if (local.Manual.FindControl(k.controlId) == null)
                    problems.Add($"{label} has no button row in this build.");
                if (!(k.max > k.min) || !(k.step > 0f))
                {
                    problems.Add($"{label} needs max > min and a positive step.");
                    continue;
                }

                var steps = (k.max - k.min) / k.step;
                if (Math.Abs(steps - Math.Round(steps)) > 1e-3)
                    problems.Add($"{label}'s range is not a whole number of steps.");
                if (k.recommendedMin > k.recommendedMax + Epsilon || k.recommendedMin < k.min - Epsilon || k.recommendedMax > k.max + Epsilon)
                    problems.Add($"{label}'s recommended range is outside its range.");
                if (k.start < k.min - Epsilon || k.start > k.max + Epsilon)
                    problems.Add($"{label} starts outside its range.");
                if (k.weight < 0f || float.IsNaN(k.weight))
                    problems.Add($"{label} has a negative weight.");
            }

            foreach (var required in local.Manual.Controls.Where(k => k.Required))
                if (!ids.Contains(required.ControlId))
                    problems.Add($"The recipe leaves out required control '{required.ControlId}'.");
            if (ids.Count < 2)
                problems.Add("The recipe needs at least two controls.");

            foreach (var rule in recipe.rules ?? Array.Empty<XrRecipeRule>())
            {
                if (rule == null)
                    continue;
                var label = $"Rule '{rule.ruleId}'";
                if (string.IsNullOrEmpty(rule.ruleId))
                    problems.Add("A recipe rule has no ruleId.");
                if (!ids.Contains(rule.controlA ?? ""))
                    problems.Add($"{label} uses unknown control '{rule.controlA}'.");
                if (!string.IsNullOrEmpty(rule.controlB) && !ids.Contains(rule.controlB))
                    problems.Add($"{label} uses unknown control '{rule.controlB}'.");
                if (rule.aMin > rule.aMax + Epsilon || (!string.IsNullOrEmpty(rule.controlB) && rule.bMin > rule.bMax + Epsilon))
                    problems.Add($"{label} has an empty range.");
                if (rule.penalty < 0f || rule.penalty > 1f)
                    problems.Add($"{label}'s penalty must be between 0 and 1.");
                if (local.Manual.Rules.All(r => r.RuleId != rule.ruleId))
                    problems.Add($"{label} has no feedback text in this build.");
            }

            if (assetExists != null)
                foreach (var key in recipe.assetKeys ?? Array.Empty<string>())
                    if (!string.IsNullOrEmpty(key) && !assetExists(key))
                        problems.Add($"Asset '{key}' is not bound in this build's asset catalog.");

            if (problems.Count == 0)
                problems.AddRange(TreatmentCaseValidator.ValidateManual(Apply(recipe, local)).Select(p => "Recipe: " + p));
            return problems;
        }

        /// <summary>A copy of <paramref name="local"/> whose manual profile uses the recipe's numbers. The local case is unchanged.</summary>
        public static TreatmentCase Apply(XrRecipe recipe, TreatmentCase local)
        {
            var copy = CopyCase(local);
            var source = local.Manual;
            var m = new ManualTreatmentProfile
            {
                Key = source.Key,
                Description = source.Description,
                InitialSeverity = recipe.initialSeverity,
                InitialHealth = recipe.initialHealth,
                MaxSeverityDrop = recipe.maxSeverityDrop,
                MaxHealthGain = recipe.maxHealthGain,
                MaxWorsen = recipe.maxWorsen,
                MaxSpotFade = source.MaxSpotFade,
                MaxYellowFade = source.MaxYellowFade,
                MaxGreenGain = source.MaxGreenGain,
                MaxVeinGain = source.MaxVeinGain,
                RecoveryNote = source.RecoveryNote,
            };
            m.Symptoms.AddRange(source.Symptoms);
            foreach (var k in (recipe.controls ?? Array.Empty<XrRecipeControl>()).OrderBy(k => k.order))
            {
                var text = source.FindControl(k.controlId);
                m.Controls.Add(new TreatmentControlDef
                {
                    ControlId = k.controlId, Order = k.order, MethodId = text?.MethodId, Name = text?.Name ?? k.controlId,
                    Description = text?.Description ?? "", Min = k.min, Max = k.max, Step = k.step, Start = k.start,
                    RecommendedMin = k.recommendedMin, RecommendedMax = k.recommendedMax, Weight = k.weight, Required = k.required,
                    FeedbackLow = text?.FeedbackLow, FeedbackHigh = text?.FeedbackHigh, FeedbackOk = text?.FeedbackOk,
                });
            }

            foreach (var r in recipe.rules ?? Array.Empty<XrRecipeRule>())
            {
                var text = source.Rules.FirstOrDefault(x => x.RuleId == r.ruleId);
                m.Rules.Add(new TreatmentControlRule
                {
                    RuleId = r.ruleId, ControlA = r.controlA, AMin = r.aMin, AMax = r.aMax, ControlB = string.IsNullOrEmpty(r.controlB) ? null : r.controlB,
                    BMin = r.bMin, BMax = r.bMax, Penalty = r.penalty, Blocks = r.blocks, Feedback = text?.Feedback ?? "",
                });
            }

            copy.Manual = m;
            return copy;
        }

        static TreatmentCase CopyCase(TreatmentCase c)
        {
            var copy = new TreatmentCase
            {
                Key = c.Key, PlantName = c.PlantName, DiseaseName = c.DiseaseName, Category = c.Category, Scenario = c.Scenario,
                Outcome = c.Outcome, EnvironmentKey = c.EnvironmentKey, SpecimenAsset = c.SpecimenAsset, SpecimenScale = c.SpecimenScale,
                Summary = c.Summary, ResultText = c.ResultText, ContentVersion = c.ContentVersion, Environment = c.Environment,
                ManualError = c.ManualError,
            };
            copy.Modules.AddRange(c.Modules);
            copy.RackTools.AddRange(c.RackTools);
            copy.Targets.AddRange(c.Targets);
            copy.Methods.AddRange(c.Methods);
            copy.Steps.AddRange(c.Steps);
            copy.FollowUps.AddRange(c.FollowUps);
            copy.Evidence.AddRange(c.Evidence);
            return copy;
        }
    }
}
