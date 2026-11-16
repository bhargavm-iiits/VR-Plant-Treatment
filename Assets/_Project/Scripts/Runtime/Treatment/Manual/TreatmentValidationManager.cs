using System;
using System.Collections.Generic;

namespace BTP.Treatment
{
    /// <summary>Where a control's value sits relative to its authored recommendation.</summary>
    public enum ControlStatus
    {
        NotSet,
        BelowRecommended,
        WithinRecommended,
        AboveRecommended
    }

    /// <summary>How well a whole treatment matched the recommendation.</summary>
    public enum AssessmentBand
    {
        Poor,
        Weak,
        Partial,
        Good
    }

    public sealed class ControlAssessment
    {
        public TreatmentControlDef Control;
        public float Value;
        public bool Touched;
        public ControlStatus Status;
        /// <summary>0..1 closeness to the recommended range.</summary>
        public float Closeness;
        /// <summary>The authored feedback for this status (from SQL).</summary>
        public string Feedback;
    }

    public sealed class TreatmentAssessment
    {
        /// <summary>0..1 after rule penalties.</summary>
        public float Score;
        /// <summary>False when a required control is not set or a blocking rule applies: the treatment cannot help.</summary>
        public bool Valid;
        public AssessmentBand Band;
        public readonly List<ControlAssessment> Controls = new List<ControlAssessment>();
        public readonly List<TreatmentControlRule> TriggeredRules = new List<TreatmentControlRule>();
        public readonly List<TreatmentControlDef> Missing = new List<TreatmentControlDef>();

        public bool Blocked
        {
            get
            {
                foreach (var rule in TriggeredRules)
                    if (rule.Blocks)
                        return true;
                return false;
            }
        }

        /// <summary>Every feedback line: missing controls, triggered rules, then one line per control.</summary>
        public IEnumerable<string> FeedbackLines()
        {
            foreach (var control in Missing)
                yield return $"{control.Name}: not set yet.";
            foreach (var rule in TriggeredRules)
                yield return rule.Feedback;
            foreach (var control in Controls)
                if (control.Touched || control.Control.Required)
                    yield return $"{control.Control.Name}: {control.Feedback}";
        }
    }

    /// <summary>
    /// Validates the learner's treatment levels against the case's authored recommendation and combination rules.
    /// Closeness is 1 inside the recommended range and falls off with distance from it; the weighted mean minus rule
    /// penalties gives a 0..1 score. All feedback text comes from the database.
    /// </summary>
    public static class TreatmentValidationManager
    {
        public const float GoodScore = 0.85f;
        public const float PartialScore = 0.6f;
        public const float WeakScore = 0.35f;

        public static ControlStatus StatusOf(TreatmentControlDef control, float value, bool touched)
        {
            if (!touched)
                return ControlStatus.NotSet;
            if (value < control.RecommendedMin - 1e-4f)
                return ControlStatus.BelowRecommended;
            if (value > control.RecommendedMax + 1e-4f)
                return ControlStatus.AboveRecommended;
            return ControlStatus.WithinRecommended;
        }

        /// <summary>1 inside the recommended range; 0 once the value is a quarter of the scale (at least two steps) away.</summary>
        public static float Closeness(TreatmentControlDef control, float value)
        {
            var distance = value < control.RecommendedMin ? control.RecommendedMin - value :
                value > control.RecommendedMax ? value - control.RecommendedMax : 0f;
            if (distance <= 1e-4f)
                return 1f;
            var tolerance = Math.Max(2.5f * control.Step, 0.25f * (control.Max - control.Min));
            return Math.Max(0f, 1f - distance / tolerance);
        }

        public static AssessmentBand BandOf(float score) =>
            score >= GoodScore ? AssessmentBand.Good : score >= PartialScore ? AssessmentBand.Partial : score >= WeakScore ? AssessmentBand.Weak : AssessmentBand.Poor;

        public static TreatmentAssessment Assess(ManualTreatmentProfile profile, IReadOnlyDictionary<string, ControlSelection> values)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            var result = new TreatmentAssessment();
            float weighted = 0f, total = 0f;
            foreach (var control in profile.Controls)
            {
                values.TryGetValue(control.ControlId, out var selection);
                var status = StatusOf(control, selection.Value, selection.Touched);
                var closeness = selection.Touched ? Closeness(control, selection.Value) : 0f;
                result.Controls.Add(new ControlAssessment
                {
                    Control = control, Value = selection.Value, Touched = selection.Touched, Status = status, Closeness = closeness,
                    Feedback = status == ControlStatus.BelowRecommended ? control.FeedbackLow :
                        status == ControlStatus.AboveRecommended ? control.FeedbackHigh :
                        status == ControlStatus.WithinRecommended ? control.FeedbackOk : "Not set yet.",
                });
                if (control.Required && !selection.Touched)
                    result.Missing.Add(control);
                // An optional control the learner left alone neither helps nor hurts.
                if (!control.Required && !selection.Touched)
                    continue;
                weighted += control.Weight * closeness;
                total += control.Weight;
            }

            float? ValueOf(string id) => values.TryGetValue(id, out var s) && s.Touched ? s.Value : (float?)null;
            var penalty = 0f;
            foreach (var rule in profile.Rules)
            {
                if (!rule.Matches(ValueOf))
                    continue;
                result.TriggeredRules.Add(rule);
                penalty += rule.Penalty;
            }

            var score = total > 0f ? weighted / total : 0f;
            result.Score = Math.Max(0f, Math.Min(1f, score - penalty));
            result.Valid = result.Missing.Count == 0 && !result.Blocked;
            if (result.Blocked)
                result.Score = 0f;
            result.Band = result.Valid ? BandOf(result.Score) : AssessmentBand.Poor;
            return result;
        }
    }
}
