using System;
using UnityEngine;

namespace BTP.Core
{
    /// <summary>Where the treated plant's after-treatment story is (see <see cref="RecoveryFlow"/>).</summary>
    public enum RecoveryFlowStage
    {
        /// <summary>No successful treatment yet this session.</summary>
        None,
        /// <summary>Treated: the visiting Lab shows its weekly changes.</summary>
        LabPending,
        /// <summary>The Lab reached the last week: the Farm button is offered.</summary>
        WeeksViewed,
        /// <summary>The Farm showed the plant healed.</summary>
        FarmHealed
    }

    /// <summary>
    /// The after-treatment story of one specimen, kept for the session (plain state, unit-tested): a successful treatment
    /// in the Treatment lab (<see cref="TreatmentApplied"/>, with a "Go to Lab" pop-up) → the visiting Lab steps the
    /// treated leaf week by week (<see cref="WeekReached"/>; reaching the last week once offers "See changes in the Farm")
    /// → the Farm shows the same plant healed and highlights it (<see cref="FarmShown"/>), and keeps it healed. A newer
    /// successful treatment starts a new story; a failed one changes nothing. Skipping the Lab leaves the story waiting.
    /// </summary>
    public static class RecoveryFlow
    {
        /// <summary>The Lab timeline's first week: the leaf as treated (still diseased).</summary>
        public const int FirstWeek = 1;

        public static RecoveryFlowStage Stage { get; private set; }
        public static string PlantId { get; private set; }
        public static string DiseaseId { get; private set; }
        /// <summary>The Farm plant that was treated (or the disease row's first plant when none was carried); null when the crop has no Farm plant.</summary>
        public static SpecimenRef Specimen { get; private set; }
        /// <summary>The disease shown on the plant when it was treated (1 diseased .. 0 healthy).</summary>
        public static float StartAmount { get; private set; } = 1f;
        /// <summary>Share of that disease the treatment clears by the last week: 1 heals fully, less heals partly.</summary>
        public static float Healed { get; private set; } = 1f;
        /// <summary>The treatment's outcome key ("Managed", "PartlyManaged", ...) or label, for the pop-ups.</summary>
        public static string Outcome { get; private set; }
        /// <summary>The furthest Lab week shown for the story.</summary>
        public static int HighestWeek { get; private set; }
        /// <summary>Bumped by every new story, so a view can tell a fresh one from one it already introduced.</summary>
        public static int Generation { get; private set; }

        /// <summary>Raised after any change.</summary>
        public static event Action Changed;

        public static bool IsActive => Stage != RecoveryFlowStage.None;
        public static bool FullyHealed => Healed >= 0.999f;
        /// <summary>The Lab's "See changes in the Farm" is unlocked (the last week was reached at least once).</summary>
        public static bool FarmUnlocked => Stage == RecoveryFlowStage.WeeksViewed || Stage == RecoveryFlowStage.FarmHealed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Reset();
            Generation = 0;
            Changed = null;
        }

        /// <summary>True when the story is about this case.</summary>
        public static bool IsFor(string plantId, string diseaseId) =>
            IsActive && !string.IsNullOrEmpty(plantId) && plantId == PlantId && diseaseId == DiseaseId;

        /// <summary>
        /// A treatment of (<paramref name="plantId"/>, <paramref name="diseaseId"/>) succeeded. The same treatment reported
        /// again while the Lab is still pending (a repeated save or Check Health) only updates the result; any other starts a
        /// new story at <see cref="RecoveryFlowStage.LabPending"/>. Returns true when a new story started.
        /// </summary>
        public static bool TreatmentApplied(string plantId, string diseaseId, SpecimenRef specimen, float startAmount, float healed, string outcome)
        {
            if (string.IsNullOrEmpty(plantId) || string.IsNullOrEmpty(diseaseId))
                return false;
            var repeat = Stage == RecoveryFlowStage.LabPending && IsFor(plantId, diseaseId) && Equals(specimen, Specimen);
            Healed = Mathf.Clamp01(healed);
            Outcome = outcome;
            if (repeat)
            {
                Changed?.Invoke();
                return false;
            }

            Stage = RecoveryFlowStage.LabPending;
            PlantId = plantId;
            DiseaseId = diseaseId;
            Specimen = specimen;
            StartAmount = Mathf.Clamp01(startAmount);
            HighestWeek = FirstWeek;
            Generation++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// The Lab showed <paramref name="week"/> of <paramref name="weekCount"/> for a case. True exactly once per story:
        /// when the treated case first reaches the last week (the story moves to <see cref="RecoveryFlowStage.WeeksViewed"/>).
        /// </summary>
        public static bool WeekReached(string plantId, string diseaseId, int week, int weekCount)
        {
            if (!IsFor(plantId, diseaseId))
                return false;
            HighestWeek = Mathf.Max(HighestWeek, week);
            if (Stage != RecoveryFlowStage.LabPending || week < weekCount)
                return false;
            Stage = RecoveryFlowStage.WeeksViewed;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The Farm showed the healed plant. True the first time (the highlight plays then).</summary>
        public static bool FarmShown()
        {
            if (Stage != RecoveryFlowStage.WeeksViewed)
                return false;
            Stage = RecoveryFlowStage.FarmHealed;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Ends the story (tests, a fresh session).</summary>
        public static void Clear()
        {
            Reset();
            Changed?.Invoke();
        }

        static void Reset()
        {
            Stage = RecoveryFlowStage.None;
            PlantId = null;
            DiseaseId = null;
            Specimen = null;
            StartAmount = 1f;
            Healed = 1f;
            Outcome = null;
            HighestWeek = 0;
        }

        /// <summary>Disease left on the plant at the end of the story (1 diseased .. 0 healthy).</summary>
        public static float FinalDiseaseAmount => StartAmount * (1f - Healed);

        /// <summary>The Lab leaf's healing share for a case: the story's for the treated case, else 1 (the demo heals fully).</summary>
        public static float LeafHealing(string plantId, string diseaseId) => IsFor(plantId, diseaseId) ? Healed : 1f;

        /// <summary>
        /// How much a manual session's outcome heals: managed, suppressed or contained heal fully, the "partly" outcomes
        /// half way, anything else (poorly contained, not controlled, not treated, not finished) is a failed treatment (null).
        /// </summary>
        public static float? HealedFor(string outcomeKey)
        {
            switch (outcomeKey)
            {
                case "Managed":
                case "PestSuppressed":
                case "Contained":
                    return 1f;
                case "PartlyManaged":
                case "PartlySuppressed":
                    return 0.5f;
                default:
                    return null;
            }
        }
    }
}
