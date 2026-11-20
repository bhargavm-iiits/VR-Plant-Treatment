using System;
using System.Globalization;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>The last settings applied to one Farm specimen in the chamber, and what the application left on the soil.</summary>
    [Serializable]
    public sealed class SpecimenSettingsRecord
    {
        public string specimenId;
        public string plantId;
        public string diseaseId;
        public string scenario;
        public int profileVersion;
        /// <summary>The applied settings as the strict evaluation body (the same JSON Check Health sends).</summary>
        public string values;
        /// <summary>Fertiliser granules settled on the soil by the last application (0: none).</summary>
        public int granules;
        public float dose;
        public int applications;
        public int applyCount;
        /// <summary>Continuous pours over the soil by the time of Apply (Addendum 5: each counts as one application).</summary>
        public int pours;
        /// <summary>"Applied 3 training units x 2 applications: 48 granules on the soil."</summary>
        public string description;
        /// <summary>WATER station state (Addendum 3): current moisture, setpoint, water to add, outlet used, moved, wet patch.</summary>
        public bool hasSoil;
        public float soilCurrent;
        public float soilSetpoint;
        public float soilAmount;
        public bool soilDelivered;
        public float soilMoved;
        public float soilWet;
        public string savedUtc;
    }

    /// <summary>
    /// Per-specimen applied settings in the learner progress database (settings table) under
    /// "specimen_settings/{specimenId}": returning to a specimen restores its applied values and settled granules, and a
    /// different specimen of the same crop starts from its own record.
    /// </summary>
    public static class SpecimenSettingsStore
    {
        public const string KeyPrefix = "specimen_settings/";

        public static string Key(string specimenId) => KeyPrefix + specimenId;

        public static bool Save(SpecimenSettingsRecord record)
        {
            var store = SpecimenTreatmentResults.Store;
            if (store == null || record == null || string.IsNullOrEmpty(record.specimenId))
                return false;
            record.savedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            store.SetSetting(Key(record.specimenId), JsonUtility.ToJson(record));
            return true;
        }

        /// <summary>The saved record for <paramref name="specimenId"/>, or null.</summary>
        public static SpecimenSettingsRecord Load(string specimenId)
        {
            var store = SpecimenTreatmentResults.Store;
            if (store == null || string.IsNullOrEmpty(specimenId))
                return null;
            return Parse(store.GetSetting(Key(specimenId)));
        }

        public static SpecimenSettingsRecord Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var record = JsonUtility.FromJson<SpecimenSettingsRecord>(json);
                return record != null && !string.IsNullOrEmpty(record.specimenId) ? record : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Whether a saved record belongs to this profile (same case, scenario and version).</summary>
        public static bool Matches(SpecimenSettingsRecord record, TreatmentProfile profile) =>
            record != null && profile != null && record.plantId == profile.PlantId && record.diseaseId == profile.DiseaseId &&
            record.scenario == profile.Scenario && record.profileVersion == profile.ProfileVersion;

        public static string Describe(double dose, double applications, int granules, TreatmentProfile profile)
        {
            var doseSub = profile?.Find(TreatmentProfile.Fertiliser, "dose");
            var appSub = profile?.Find(TreatmentProfile.Fertiliser, "applications");
            var amount = doseSub != null ? doseSub.Format(dose) : dose.ToString("0", CultureInfo.InvariantCulture);
            var times = appSub != null ? appSub.Format(applications) : applications.ToString("0", CultureInfo.InvariantCulture);
            return granules > 0 ? $"Applied {amount} x {times}: {granules} granules on the soil." : $"Applied {amount} x {times}: no granules.";
        }
    }
}
