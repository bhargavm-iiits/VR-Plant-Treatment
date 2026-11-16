using System;
using System.Collections.Generic;
using System.Linq;

namespace BTP.Treatment
{
    /// <summary>Result of one learner action in a manual session.</summary>
    public readonly struct ManualActionResult
    {
        public readonly bool Accepted;
        public readonly string Message;
        public readonly bool StageChanged;
        /// <summary>True when the leaf's recovery target changed (a treatment was applied).</summary>
        public readonly bool TargetChanged;

        public ManualActionResult(bool accepted, string message, bool stageChanged = false, bool targetChanged = false)
        {
            Accepted = accepted;
            Message = message;
            StageChanged = stageChanged;
            TargetChanged = targetChanged;
        }
    }

    /// <summary>
    /// The manual treatment workflow as a pure C# state machine (no scenes needed, so EditMode tests cover it):
    /// Diagnosis (review the disease information) → Selection (set the levels with − / +) → Application (apply;
    /// the leaf animates towards the validated result) → Monitoring (further adjustments and health checks) →
    /// Final assessment → Completed. Stages advance on learner actions; the 1-minute timer is guidance and,
    /// when it runs out, only moves the session to the final assessment. Only a validated Apply changes the
    /// leaf's recovery target; time never does.
    /// </summary>
    public sealed class TreatmentSessionManager
    {
        public const string ActionStart = "start";
        public const string ActionResume = "resume";
        public const string ActionConfirmDiagnosis = "confirm_diagnosis";
        public const string ActionIncrease = "increase";
        public const string ActionDecrease = "decrease";
        public const string ActionApply = "apply";
        public const string ActionContinue = "continue_monitoring";
        public const string ActionCheckHealth = "check_health";
        public const string ActionProceed = "proceed_final";
        public const string ActionFinish = "finish";
        public const string ActionTimeUp = "time_up";
        const string TimeUpMessage = "Time is up: the session moves to the final assessment. Applied treatments still count.";

        readonly Func<DateTime> clock;

        public TreatmentSessionManager(TreatmentCase content, ManualSessionRecord record, Func<DateTime> utcNow = null)
        {
            Case = content ?? throw new ArgumentNullException(nameof(content));
            Profile = content.Manual ?? throw new ArgumentException("The case has no manual treatment profile.", nameof(content));
            Record = record ?? throw new ArgumentNullException(nameof(record));
            clock = utcNow ?? (() => DateTime.UtcNow);
            Buttons = new TreatmentButtonController(Profile);
            Timer = new TreatmentTimer();

            foreach (var pair in Record.Selections)
                Buttons.Restore(pair.Key, pair.Value.Value, pair.Value.Touched);
            SyncSelections();
            // A restored session continues from its saved elapsed time.
            if (Record.CurrentStage != ManualStage.NotStarted)
                Timer.Start(Record.ElapsedSeconds);
            if (Record.CurrentStage == ManualStage.Completed)
                Timer.Stop();
            // A session saved with more time than the session allows (e.g. under a longer session length) is clamped
            // and resumes in the final assessment.
            Record.ElapsedSeconds = Timer.Elapsed;
            if (IsActive && Timer.TimeUp && Stage < ManualStage.FinalAssessment)
            {
                Record.CurrentStage = ManualStage.FinalAssessment;
                Log(ActionTimeUp, true, TimeUpMessage, stage: true);
            }
        }

        /// <summary>A fresh, unsaved record for a learner and case.</summary>
        public static ManualSessionRecord NewRecord(TreatmentCase content, long learnerId, DateTime utc)
        {
            var profile = content.Manual ?? throw new ArgumentException("The case has no manual treatment profile.", nameof(content));
            return new ManualSessionRecord
            {
                LearnerId = learnerId, Key = content.Key, ContentVersion = content.ContentVersion, StartedUtc = utc, UpdatedUtc = utc,
                InitialSeverity = profile.InitialSeverity, InitialHealth = profile.InitialHealth,
                CurrentSeverity = profile.InitialSeverity, CurrentHealth = profile.InitialHealth,
            };
        }

        public TreatmentCase Case { get; }
        public ManualTreatmentProfile Profile { get; }
        public ManualSessionRecord Record { get; }
        public TreatmentButtonController Buttons { get; }
        public TreatmentTimer Timer { get; }

        public ManualStage Stage => Record.CurrentStage;
        public bool IsActive => Stage != ManualStage.NotStarted && Stage != ManualStage.Completed;
        public bool HasApplied => Record.AppliedScore.HasValue;

        /// <summary>Live assessment of the current (not necessarily applied) levels, for the adjustment panel.</summary>
        public TreatmentAssessment Preview() => TreatmentValidationManager.Assess(Profile, Buttons.Values);

        /// <summary>The leaf state the last applied treatment leads to (the initial state before anything is applied).</summary>
        public LeafRecoveryState Target =>
            HasApplied ? LeafRecoveryModel.Evaluate(Profile, Case.Outcome, Record.AppliedValid, Record.AppliedScore.Value) : LeafRecoveryState.Initial(Profile);

        /// <summary>Assessment of the levels as they were at the last Apply; null before anything is applied.</summary>
        public TreatmentAssessment AppliedAssessment()
        {
            if (!HasApplied)
                return null;
            var values = new Dictionary<string, ControlSelection>();
            foreach (var pair in Record.AppliedValues)
                values[pair.Key] = new ControlSelection { Value = pair.Value, Touched = true };
            return TreatmentValidationManager.Assess(Profile, values);
        }

        /// <summary>True when a level was changed after the last Apply.</summary>
        public bool HasPendingChanges
        {
            get
            {
                if (!HasApplied)
                    return false;
                foreach (var pair in Buttons.Values)
                {
                    var appliedHas = Record.AppliedValues.TryGetValue(pair.Key, out var applied);
                    if (pair.Value.Touched != appliedHas || (appliedHas && Math.Abs(applied - pair.Value.Value) > 1e-4f))
                        return true;
                }

                return false;
            }
        }

        public bool CanAdjust => Stage == ManualStage.Selection || Stage == ManualStage.Application || Stage == ManualStage.Monitoring;
        public bool CanApply => CanAdjust;

        // ------------------------------------------------------------------ actions

        public ManualActionResult Start()
        {
            if (Stage != ManualStage.NotStarted)
                return Log(ActionStart, false, "This session has already started.");
            Record.CurrentStage = ManualStage.Diagnosis;
            Timer.Start(0f);
            return Log(ActionStart, true, "Session started. Review the disease information, then confirm the diagnosis.", stage: true);
        }

        /// <summary>Records that a saved session was resumed (after a restart or case switch).</summary>
        public ManualActionResult Resume()
        {
            if (!IsActive)
                return new ManualActionResult(false, "There is no unfinished session to resume.");
            Timer.Resume();
            return Log(ActionResume, true, $"Session resumed at {StageName(Stage)} ({TreatmentTimer.Clock(Timer.Elapsed)} elapsed).");
        }

        public ManualActionResult ConfirmDiagnosis()
        {
            if (Stage != ManualStage.Diagnosis)
                return Log(ActionConfirmDiagnosis, false, Stage == ManualStage.NotStarted ? "Start the session first." : "The diagnosis is already confirmed.");
            CompleteStage(ManualStage.Diagnosis, ManualStage.Selection);
            return Log(ActionConfirmDiagnosis, true, "Diagnosis reviewed. Set each treatment level with - and +, then apply the treatment.", stage: true);
        }

        public ManualActionResult Increase(string controlId) => Adjust(controlId, +1);

        public ManualActionResult Decrease(string controlId) => Adjust(controlId, -1);

        public ManualActionResult Adjust(string controlId, int direction)
        {
            var action = direction > 0 ? ActionIncrease : ActionDecrease;
            if (!CanAdjust)
                return Log(action, false, Stage == ManualStage.Diagnosis || Stage == ManualStage.NotStarted
                    ? "Review the disease information and confirm the diagnosis before adjusting the treatment."
                    : "Treatment adjustments are closed for the final assessment.", controlId);
            var result = Buttons.Adjust(controlId, direction);
            SyncSelections();
            return Log(action, result.Accepted, result.Message, controlId, result.Accepted ? result.Value : (float?)null);
        }

        /// <summary>Validates the current levels and, if every required control is set, applies them to the leaf.</summary>
        public ManualActionResult Apply()
        {
            if (!CanApply)
                return Log(ActionApply, false, Stage == ManualStage.Diagnosis || Stage == ManualStage.NotStarted
                    ? "Confirm the diagnosis first."
                    : "The treatment can no longer be changed.");
            var assessment = Preview();
            if (assessment.Missing.Count > 0)
                return Log(ActionApply, false, "Set every treatment level first: " + string.Join(", ", assessment.Missing.Select(c => c.Name)) + ".");

            Record.AppliedScore = assessment.Score;
            Record.AppliedValid = assessment.Valid;
            Record.AppliedValues.Clear();
            foreach (var pair in Buttons.Values)
                if (pair.Value.Touched)
                    Record.AppliedValues[pair.Key] = pair.Value.Value;
            Record.ApplyCount++;
            var target = Target;
            Record.CurrentSeverity = target.Severity;
            Record.CurrentHealth = target.Health;
            var stageChanged = false;
            if (Stage == ManualStage.Selection)
            {
                CompleteStage(ManualStage.Selection, ManualStage.Application);
                stageChanged = true;
            }

            var message = TreatmentResultManager.ApplySummary(Case.Outcome, assessment);
            return Log(ActionApply, true, message, null, assessment.Score, stageChanged, true);
        }

        public ManualActionResult ContinueToMonitoring()
        {
            if (Stage != ManualStage.Application)
                return Log(ActionContinue, false, HasApplied ? "Monitoring has already started." : "Apply a treatment first.");
            CompleteStage(ManualStage.Application, ManualStage.Monitoring);
            return Log(ActionContinue, true, "Monitoring: check the leaf's health; adjust and re-apply if needed.", stage: true);
        }

        /// <summary>A health check in the monitoring stage: records the displayed (simulated) leaf state.</summary>
        public ManualActionResult CheckHealth(LeafRecoveryState displayed)
        {
            if (Stage != ManualStage.Monitoring)
                return Log(ActionCheckHealth, false, "Health checks are part of the monitoring stage.");
            Record.HealthChecks++;
            var trend = displayed.Health > Profile.InitialHealth + 0.005f ? "improving" :
                displayed.Health < Profile.InitialHealth - 0.005f ? "worse than at the start" : "unchanged";
            var message = $"Health check {Record.HealthChecks}: leaf health {Percent(displayed.Health)}, active severity {Percent(displayed.Severity)} ({trend}).";
            return Log(ActionCheckHealth, true, message, null, displayed.Health);
        }

        public ManualActionResult ProceedToFinal()
        {
            if (Stage != ManualStage.Monitoring)
                return Log(ActionProceed, false, "Reach the monitoring stage first.");
            if (Record.HealthChecks == 0)
                return Log(ActionProceed, false, "Check the leaf's health at least once before the final assessment.");
            if (HasPendingChanges)
                return Log(ActionProceed, false, "Some levels changed since the last Apply. Apply them, or set them back, before the final assessment.");
            CompleteStage(ManualStage.Monitoring, ManualStage.FinalAssessment);
            return Log(ActionProceed, true, "Final assessment: review the result and finish the session.", stage: true);
        }

        public ManualActionResult Finish()
        {
            if (Stage != ManualStage.FinalAssessment)
                return Log(ActionFinish, false, "Finish is available in the final assessment stage.");
            var final = Target;
            Record.FinalSeverity = final.Severity;
            Record.FinalHealth = final.Health;
            Record.Outcome = TreatmentResultManager.OutcomeKey(Case.Outcome, HasApplied, Record.AppliedValid, Record.AppliedScore ?? 0f);
            Record.Completed = true;
            Record.CompletedUtc = clock();
            CompleteStage(ManualStage.FinalAssessment, ManualStage.Completed);
            Timer.Stop();
            return Log(ActionFinish, true, "Session complete: " + TreatmentResultManager.OutcomeLabel(Record.Outcome) + ".", stage: true);
        }

        /// <summary>Advances the session clock. When time runs out the session moves to the final assessment; the leaf is untouched.</summary>
        public ManualActionResult Tick(float seconds)
        {
            if (!IsActive)
                return new ManualActionResult(false, null);
            Timer.Tick(seconds);
            Record.ElapsedSeconds = Timer.Elapsed;
            if (!Timer.TimeUp || Stage >= ManualStage.FinalAssessment)
                return new ManualActionResult(false, null);
            Record.CurrentStage = ManualStage.FinalAssessment;
            return Log(ActionTimeUp, true, TimeUpMessage, stage: true);
        }

        // ------------------------------------------------------------------ helpers

        void CompleteStage(ManualStage done, ManualStage next)
        {
            if (!Record.StagesCompleted.ContainsKey(done))
                Record.StagesCompleted[done] = clock();
            Record.CurrentStage = next;
        }

        void SyncSelections()
        {
            Record.Selections.Clear();
            foreach (var pair in Buttons.Values)
                Record.Selections[pair.Key] = pair.Value;
        }

        ManualActionResult Log(string action, bool accepted, string message, string controlId = null, float? value = null, bool stage = false,
            bool target = false)
        {
            var now = clock();
            Record.UpdatedUtc = now;
            Record.ElapsedSeconds = Timer.Elapsed;
            Record.Actions.Add(new ManualActionRecord
            {
                Seq = Record.Actions.Count == 0 ? 1 : Record.Actions[Record.Actions.Count - 1].Seq + 1, Utc = now, Stage = Record.CurrentStage,
                Action = action, ControlId = controlId, Value = value, Accepted = accepted, Message = message,
            });
            return new ManualActionResult(accepted, message, stage, target);
        }

        /// <summary>0..1 as a whole percentage; the panels and the result text round the same way.</summary>
        public static int PercentValue(float value) => (int)Math.Round(Math.Max(0f, Math.Min(1f, value)) * 100.0, MidpointRounding.AwayFromZero);

        public static string Percent(float value) => $"{PercentValue(value)}%";

        public static string StageName(ManualStage stage)
        {
            switch (stage)
            {
                case ManualStage.Diagnosis: return "Diagnosis";
                case ManualStage.Selection: return "Selection";
                case ManualStage.Application: return "Application";
                case ManualStage.Monitoring: return "Monitoring";
                case ManualStage.FinalAssessment: return "Final assessment";
                case ManualStage.Completed: return "Completed";
                default: return "Not started";
            }
        }

        /// <summary>
        /// The newest history entries for display, newest first, with consecutive identical refused entries collapsed into
        /// one (the newest) and their count. <see cref="ManualSessionRecord.Actions"/> still holds every entry.
        /// </summary>
        public List<(ManualActionRecord action, int repeats)> RecentHistory(int count)
        {
            var groups = new List<(ManualActionRecord action, int repeats)>();
            for (var i = Record.Actions.Count - 1; i >= 0; i--)
            {
                var a = Record.Actions[i];
                if (groups.Count > 0)
                {
                    var (last, repeats) = groups[groups.Count - 1];
                    if (!a.Accepted && !last.Accepted && a.Message == last.Message && a.Action == last.Action)
                    {
                        groups[groups.Count - 1] = (last, repeats + 1);
                        continue;
                    }
                }

                if (groups.Count == count)
                    break;
                groups.Add((a, 1));
            }

            return groups;
        }

        /// <summary>The newest history entries, newest first.</summary>
        public IEnumerable<ManualActionRecord> RecentActions(int count)
        {
            for (var i = Record.Actions.Count - 1; i >= 0 && count > 0; i--, count--)
                yield return Record.Actions[i];
        }
    }
}
