using System;
using System.Collections.Generic;

namespace BTP.Treatment
{
    /// <summary>
    /// Saves manual treatment sessions and learner profiles through the one progress repository
    /// (<see cref="SqliteProgressRepository"/>). Every action is saved immediately; clock-only changes are saved
    /// at most every <see cref="ClockSaveSeconds"/>. Nothing here deletes saved progress.
    /// </summary>
    public sealed class ProgressSaveManager
    {
        public const string LastLearnerSetting = "last_learner";
        public const string ModeSetting = "treatment_mode";
        public const float ClockSaveSeconds = 2f;

        readonly IProgressRepository progress;
        float sinceSave;

        public ProgressSaveManager(IProgressRepository progress)
        {
            this.progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        public bool IsReady => progress.IsReady;
        public string Error => progress.Error;

        public IReadOnlyList<LearnerRecord> Learners() => progress.ListLearners();

        /// <summary>The remembered learner, or null.</summary>
        public LearnerRecord LastLearner() =>
            long.TryParse(progress.GetSetting(LastLearnerSetting), out var id) ? progress.GetLearner(id) : null;

        public void RememberLearner(LearnerRecord learner)
        {
            if (learner != null)
                progress.SetSetting(LastLearnerSetting, learner.LearnerId.ToString());
        }

        /// <summary>Creates "Learner N" with the first free number (button-only VR has no keyboard).</summary>
        public LearnerRecord CreateNextLearner(string prefix = "Learner")
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var learner in progress.ListLearners())
                names.Add(learner.Name);
            for (var i = 1; i < 10000; i++)
                if (!names.Contains($"{prefix} {i}"))
                    return progress.CreateLearner($"{prefix} {i}");
            return null;
        }

        public bool ManualModeRemembered => progress.GetSetting(ModeSetting) == "manual";

        public void RememberMode(bool manual) => progress.SetSetting(ModeSetting, manual ? "manual" : "step");

        public ManualSessionRecord FindResumable(long learnerId, CaseKey key)
        {
            var record = progress.LatestUnfinishedSession(learnerId, key);
            return record != null && record.IsResumable ? record : null;
        }

        /// <summary>Marks an unfinished session as abandoned (an UPDATE; the row and its history stay).</summary>
        public bool Abandon(ManualSessionRecord record, DateTime utc)
        {
            if (record == null || record.Completed)
                return false;
            record.Outcome = ManualSessionRecord.OutcomeAbandoned;
            record.UpdatedUtc = utc;
            return progress.SaveManualSession(record);
        }

        public bool Save(TreatmentSessionManager session)
        {
            if (session == null)
                return false;
            sinceSave = 0f;
            return progress.SaveManualSession(session.Record);
        }

        /// <summary>Call every frame with the elapsed time; saves the clock periodically.</summary>
        public bool SaveIfDue(TreatmentSessionManager session, float seconds)
        {
            if (session == null || !session.IsActive)
                return false;
            sinceSave += seconds;
            return sinceSave >= ClockSaveSeconds && Save(session);
        }
    }
}
