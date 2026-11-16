using System;
using System.Collections.Generic;

namespace BTP.Treatment
{
    /// <summary>A local learner profile (offline; several students can share one headset).</summary>
    public sealed class LearnerRecord
    {
        public long LearnerId;
        public string Name;
        public DateTime CreatedUtc;
    }

    /// <summary>One entry of a session's action history (button presses, accepted or refused).</summary>
    public sealed class ManualActionRecord
    {
        public int Seq;
        public DateTime Utc;
        public ManualStage Stage;
        public string Action;
        public string ControlId;
        public float? Value;
        public bool Accepted;
        public string Message;
    }

    /// <summary>
    /// A manual treatment session as stored in the progress database (tables manual_sessions, manual_selections,
    /// manual_actions, manual_stages). Sessions are only ever inserted or updated, never deleted.
    /// </summary>
    public sealed class ManualSessionRecord
    {
        public const string OutcomeAbandoned = "Abandoned";

        /// <summary>0 until the session is first saved.</summary>
        public long SessionId;
        public long LearnerId;
        public CaseKey Key;
        public int ContentVersion;
        public DateTime StartedUtc;
        public DateTime UpdatedUtc;
        public DateTime? CompletedUtc;
        public float InitialSeverity;
        public float InitialHealth;
        public float? FinalSeverity;
        public float? FinalHealth;
        /// <summary>The leaf state after the last applied treatment (the target the visual animates towards).</summary>
        public float CurrentSeverity;
        public float CurrentHealth;
        /// <summary>Score of the last applied treatment; null until something is applied.</summary>
        public float? AppliedScore;
        public bool AppliedValid;
        public int ApplyCount;
        public int HealthChecks;
        public string Outcome;
        public bool Completed;
        public ManualStage CurrentStage = ManualStage.NotStarted;
        public float ElapsedSeconds;

        public readonly Dictionary<string, ControlSelection> Selections = new Dictionary<string, ControlSelection>();
        /// <summary>The levels at the last Apply (manual_selections.applied_value), so the applied treatment can be re-assessed.</summary>
        public readonly Dictionary<string, float> AppliedValues = new Dictionary<string, float>();
        public readonly List<ManualActionRecord> Actions = new List<ManualActionRecord>();
        public readonly Dictionary<ManualStage, DateTime> StagesCompleted = new Dictionary<ManualStage, DateTime>();

        /// <summary>"plant/disease": the lesson this session belongs to.</summary>
        public string LessonId => Key.ToString();

        public bool IsResumable => !Completed && Outcome != OutcomeAbandoned && CurrentStage != ManualStage.NotStarted;
    }
}
