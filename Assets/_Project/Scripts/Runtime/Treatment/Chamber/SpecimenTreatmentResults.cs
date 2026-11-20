using System;
using System.Globalization;
using BTP.Core;
using BTP.Farm;
using BTP.XR;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The latest Check Health result of one Farm specimen, kept in the learner progress database (settings table) under
    /// "specimen_result/{specimenId}", so returning to the Farm shows it on that plant and no other plant of the crop.
    /// </summary>
    [Serializable]
    public sealed class SpecimenTreatmentRecord
    {
        public string specimenId;
        public string plantId;
        public string diseaseId;
        public float score;
        public bool passed;
        public string outcome;
        public string outcomeLabel;
        /// <summary>Disease shown on the specimen when it was captured (1 diseased, 0 healthy).</summary>
        public float startDiseaseAmount = 1f;
        /// <summary>Disease shown on the specimen after the checked treatment.</summary>
        public float diseaseAmount = 1f;
        public string savedUtc;
    }

    public static class SpecimenTreatmentResults
    {
        public const string KeyPrefix = "specimen_result/";

        /// <summary>Disease left on the specimen after a check: a passed check clears most of it; a partial one less.</summary>
        public static float ResultingDiseaseAmount(float startAmount, float score, bool passed)
        {
            startAmount = Mathf.Clamp01(startAmount);
            var recovery = passed ? 0.85f : 0.45f * Mathf.Clamp01(score);
            return Mathf.Clamp01(startAmount * (1f - recovery));
        }

        public static string Key(string specimenId) => KeyPrefix + specimenId;

        /// <summary>Tests and the editor verification use their own progress database instead of TreatmentServices'.</summary>
        internal static IProgressRepository StoreOverride { get; set; }

        /// <summary>The learner progress database (settings table), or null when it is not open.</summary>
        internal static IProgressRepository Store
        {
            get
            {
                if (StoreOverride != null)
                    return StoreOverride.IsReady ? StoreOverride : null;
                var services = TreatmentServices.Instance;
                return services != null && services.Progress != null && services.Progress.IsReady ? services.Progress : null;
            }
        }

        /// <summary>
        /// The session's results in memory too, so a result saved while the progress database is closed still shows on the
        /// Farm plant until the app quits.
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<string, SpecimenTreatmentRecord> Memory =
            new System.Collections.Generic.Dictionary<string, SpecimenTreatmentRecord>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Memory.Clear();

        /// <summary>Saves the result (in memory always); true when it reached the progress database.</summary>
        public static bool Save(SpecimenTreatmentRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.specimenId))
                return false;
            record.savedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Memory[record.specimenId] = record;
            var store = Store;
            if (store == null)
                return false;
            store.SetSetting(Key(record.specimenId), JsonUtility.ToJson(record));
            return true;
        }

        public static SpecimenTreatmentRecord Load(string specimenId)
        {
            if (string.IsNullOrEmpty(specimenId))
                return null;
            var store = Store;
            var record = store != null ? Parse(store.GetSetting(Key(specimenId))) : null;
            if (record == null && Memory.TryGetValue(specimenId, out var cached))
                record = cached;
            return record;
        }

        public static SpecimenTreatmentRecord Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var record = JsonUtility.FromJson<SpecimenTreatmentRecord>(json);
                return record != null && !string.IsNullOrEmpty(record.specimenId) ? record : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>"Treatment: Managed (score 100%)" for the Farm name card, or null when the specimen has no result.</summary>
        public static string Describe(SpecimenRef specimen)
        {
            var record = specimen != null ? Load(specimen.SpecimenId) : null;
            return record != null ? Describe(record) : null;
        }

        public static string Describe(SpecimenTreatmentRecord record)
        {
            var label = record.passed && !string.IsNullOrEmpty(record.outcomeLabel) ? record.outcomeLabel : "Settings need review";
            return $"Treatment: {label} (score {Mathf.RoundToInt(record.score * 100f)}%)";
        }

        /// <summary>Shows each saved specimen result on its own plant of a Farm planting.</summary>
        public static void ApplyToFarm(SelectablePlant planting)
        {
            if (planting == null || (Store == null && Memory.Count == 0))
                return;
            var count = planting.IsRow ? planting.Plants.Length : 1;
            for (var i = 0; i < count; i++)
            {
                var plant = planting.PlantAt(i);
                var record = plant != null ? Load(planting.SpecimenAt(i).SpecimenId) : null;
                if (record == null)
                    continue;
                var visual = plant.GetComponent<FarmSpecimenVisual>() ?? plant.gameObject.AddComponent<FarmSpecimenVisual>();
                visual.Show(record.diseaseAmount, false);
            }
        }
    }
}
