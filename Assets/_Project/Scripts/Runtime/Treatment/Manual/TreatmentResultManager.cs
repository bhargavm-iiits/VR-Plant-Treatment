using System.Text;

namespace BTP.Treatment
{
    /// <summary>
    /// Builds the final result of a manual session (initial vs final simulated severity and health, outcome,
    /// feedback and the disclaimer) and the short summaries shown after each Apply. Disease-specific text comes
    /// from the database (feedback, rules, recovery note, case result); only neutral framing is written here.
    /// </summary>
    public static class TreatmentResultManager
    {
        public const string Disclaimer = "Educational simulation — real infected tissue does not regenerate.";

        public static string BandLabel(AssessmentBand band)
        {
            switch (band)
            {
                case AssessmentBand.Good: return "Well matched to the recommendation";
                case AssessmentBand.Partial: return "Partly matched";
                case AssessmentBand.Weak: return "Weak match";
                default: return "Poor match";
            }
        }

        public static string ApplySummary(TreatmentOutcome outcome, TreatmentAssessment assessment)
        {
            var sb = new StringBuilder();
            if (assessment.Blocked)
                sb.Append("Treatment applied, but it cannot help: ");
            else
                sb.Append($"Treatment applied ({BandLabel(assessment.Band).ToLowerInvariant()}, score {TreatmentSessionManager.Percent(assessment.Score)}). ");
            foreach (var rule in assessment.TriggeredRules)
            {
                sb.Append(rule.Feedback);
                sb.Append(' ');
            }

            if (outcome == TreatmentOutcome.Replaced && assessment.Valid)
                sb.Append("Containment stops further spread; the infected leaf itself cannot recover.");
            return sb.ToString().Trim();
        }

        /// <summary>Stable outcome key stored in manual_sessions.outcome.</summary>
        public static string OutcomeKey(TreatmentOutcome caseOutcome, bool applied, bool valid, float score)
        {
            if (!applied)
                return "NotTreated";
            var band = valid ? TreatmentValidationManager.BandOf(score) : AssessmentBand.Poor;
            switch (caseOutcome)
            {
                case TreatmentOutcome.Replaced:
                    if (!valid)
                        return "NotContained";
                    return band >= AssessmentBand.Partial ? "Contained" : "PoorlyContained";
                case TreatmentOutcome.PestSuppressed:
                    return band == AssessmentBand.Good ? "PestSuppressed" : band == AssessmentBand.Partial ? "PartlySuppressed" : "NotControlled";
                default:
                    return band == AssessmentBand.Good ? "Managed" : band == AssessmentBand.Partial ? "PartlyManaged" : "NotControlled";
            }
        }

        public static string OutcomeLabel(string key)
        {
            switch (key)
            {
                case "Managed": return "Disease managed (simulated)";
                case "PartlyManaged": return "Disease partly managed (simulated)";
                case "PestSuppressed": return "Pest suppressed (simulated)";
                case "PartlySuppressed": return "Pest partly suppressed (simulated)";
                case "Contained": return "Infected leaf contained — replace with clean stock";
                case "PoorlyContained": return "Containment incomplete — replace with clean stock";
                case "NotContained": return "Infected material not contained";
                case "NotControlled": return "Disease not controlled";
                case "NotTreated": return "No treatment was applied";
                default: return "Not finished";
            }
        }

        public static bool IsSuccess(string key) => key == "Managed" || key == "PestSuppressed" || key == "Contained";

        /// <summary>The final results panel text.</summary>
        public static string BuildResult(TreatmentCase c, ManualSessionRecord record, TreatmentAssessment applied)
        {
            var m = c.Manual;
            var sb = new StringBuilder();
            var outcome = record.Outcome ?? OutcomeKey(c.Outcome, record.AppliedScore.HasValue, record.AppliedValid, record.AppliedScore ?? 0f);
            var finalHealth = record.FinalHealth ?? record.CurrentHealth;
            var finalSeverity = record.FinalSeverity ?? record.CurrentSeverity;
            sb.AppendLine($"<b>{OutcomeLabel(outcome)}</b>");
            sb.AppendLine();
            sb.AppendLine($"Leaf health: {TreatmentSessionManager.Percent(record.InitialHealth)} to <b>{TreatmentSessionManager.Percent(finalHealth)}</b>");
            sb.AppendLine($"Active severity: {TreatmentSessionManager.Percent(record.InitialSeverity)} to <b>{TreatmentSessionManager.Percent(finalSeverity)}</b>");
            if (record.AppliedScore.HasValue)
                sb.AppendLine($"Treatment match: {TreatmentSessionManager.Percent(record.AppliedScore.Value)} ({BandLabel(record.AppliedValid ? TreatmentValidationManager.BandOf(record.AppliedScore.Value) : AssessmentBand.Poor).ToLowerInvariant()})");
            sb.AppendLine($"Applied {record.ApplyCount}×, {record.HealthChecks} health check(s), {TreatmentTimer.Clock(record.ElapsedSeconds)} used");
            sb.AppendLine();
            if (applied != null)
            {
                sb.AppendLine("<b>Feedback</b>");
                var lines = 0;
                foreach (var line in applied.FeedbackLines())
                {
                    if (lines++ >= 6)
                        break;
                    sb.AppendLine("• " + line);
                }

                sb.AppendLine();
            }

            if (m != null && !string.IsNullOrEmpty(m.RecoveryNote))
                sb.AppendLine(m.RecoveryNote);
            if (IsSuccess(outcome) && !string.IsNullOrEmpty(c.ResultText))
                sb.AppendLine(c.ResultText);
            sb.AppendLine();
            sb.AppendLine($"<color=#FBBF24>{Disclaimer}</color>");
            return sb.ToString();
        }
    }
}
