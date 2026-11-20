using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>What a completed guided treatment applied to one Farm specimen (the Farm recap reads it).</summary>
    [Serializable]
    public sealed class GuidedTreatmentRecord
    {
        public string specimenId;
        public string plantId;
        public string diseaseId;
        public string plantName;
        public string diseaseName;
        public float dayTemperature;
        public float nightTemperature;
        public bool hasNight;
        public string temperatureUnit = "°C";
        public string product;
        public int scoops;
        public int sprays;
        public float waterAmount;
        public float rootZoneAfter;
        public string waterUnit = "%";
        public float diseaseBefore = 1f;
        public string completedUtc;

        /// <summary>The recap lines: temperature, fertiliser, spray, water.</summary>
        public string[] Lines()
        {
            var u = string.IsNullOrEmpty(temperatureUnit) ? "°C" : temperatureUnit;
            var w = string.IsNullOrEmpty(waterUnit) ? "%" : waterUnit;
            return new[]
            {
                hasNight
                    ? $"Temperature: {GuidedStepPlan.Number(dayTemperature)}{u} day / {GuidedStepPlan.Number(nightTemperature)}{u} night"
                    : $"Temperature: {GuidedStepPlan.Number(dayTemperature)}{u}",
                $"Fertiliser: {GuidedStepPlan.Plural(scoops, "scoop")} of {product}",
                $"Spray: {GuidedStepPlan.Plural(sprays, "spray")} on the leaves",
                $"Water: {GuidedStepPlan.Signed(waterAmount, w)} (root zone {GuidedStepPlan.Number(rootZoneAfter)}{w})",
            };
        }
    }

    /// <summary>
    /// Saves guided treatment results against the specimen's persistent ID (<see cref="BTP.Core.SpecimenRef"/>): in memory
    /// for the running session, in the learner progress database (settings "guided_result/{id}") when it is open, else in
    /// a JSON file in persistentDataPath. Also keeps the queue of recaps the Farm still has to show, one plant at a time.
    /// </summary>
    public static class GuidedTreatmentResults
    {
        public const string KeyPrefix = "guided_result/";
        public const string PendingKey = "guided_recap_pending";
        public const string FileName = "guided_treatments.json";

        [Serializable]
        sealed class FileData
        {
            public List<GuidedTreatmentRecord> records = new List<GuidedTreatmentRecord>();
            public List<string> pending = new List<string>();
        }

        static readonly Dictionary<string, GuidedTreatmentRecord> Memory = new Dictionary<string, GuidedTreatmentRecord>();
        static readonly List<string> MemoryPending = new List<string>();

        /// <summary>Tests: a JSON file instead of persistentDataPath's.</summary>
        internal static string FileOverride { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Memory.Clear();
            MemoryPending.Clear();
        }

        /// <summary>Clears the in-memory copy (tests: proves a reload reads the persisted one).</summary>
        internal static void ClearMemory()
        {
            Memory.Clear();
            MemoryPending.Clear();
        }

        public static string Key(string specimenId) => KeyPrefix + specimenId;

        static string FilePath => FileOverride ?? Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Saves <paramref name="record"/> and queues its Farm recap. Returns where it went ("database", "file", "memory").</summary>
        public static string Save(GuidedTreatmentRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.specimenId))
                return null;
            record.completedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Memory[record.specimenId] = record;
            MemoryPending.Remove(record.specimenId);
            MemoryPending.Add(record.specimenId);

            var store = SpecimenTreatmentResults.Store;
            if (store != null)
            {
                store.SetSetting(Key(record.specimenId), JsonUtility.ToJson(record));
                var pending = ParsePending(store.GetSetting(PendingKey));
                pending.Remove(record.specimenId);
                pending.Add(record.specimenId);
                store.SetSetting(PendingKey, string.Join("\n", pending));
                return "database";
            }

            try
            {
                var data = ReadFile();
                data.records.RemoveAll(r => r == null || r.specimenId == record.specimenId);
                data.records.Add(record);
                data.pending.Remove(record.specimenId);
                data.pending.Add(record.specimenId);
                WriteFile(data);
                return "file";
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BTP] Guided treatment result kept in memory only: " + exception.Message);
                return "memory";
            }
        }

        public static GuidedTreatmentRecord Load(string specimenId)
        {
            if (string.IsNullOrEmpty(specimenId))
                return null;
            if (Memory.TryGetValue(specimenId, out var cached))
                return cached;
            GuidedTreatmentRecord record = null;
            var store = SpecimenTreatmentResults.Store;
            if (store != null)
                record = Parse(store.GetSetting(Key(specimenId)));
            if (record == null)
            {
                foreach (var r in SafeReadFile().records)
                    if (r != null && r.specimenId == specimenId)
                        record = r;
            }

            if (record != null)
                Memory[specimenId] = record;
            return record;
        }

        public static GuidedTreatmentRecord Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var record = JsonUtility.FromJson<GuidedTreatmentRecord>(json);
                return record != null && !string.IsNullOrEmpty(record.specimenId) ? record : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Specimens whose recap the Farm has not shown yet, oldest first.</summary>
        public static List<string> Pending()
        {
            var result = new List<string>(MemoryPending);
            var store = SpecimenTreatmentResults.Store;
            var persisted = store != null ? ParsePending(store.GetSetting(PendingKey)) : SafeReadFile().pending;
            foreach (var id in persisted)
                if (!result.Contains(id))
                    result.Add(id);
            return result;
        }

        /// <summary>The player pressed OK on <paramref name="specimenId"/>'s recap: it is not shown again.</summary>
        public static void Acknowledge(string specimenId)
        {
            if (string.IsNullOrEmpty(specimenId))
                return;
            MemoryPending.Remove(specimenId);
            var store = SpecimenTreatmentResults.Store;
            if (store != null)
            {
                var pending = ParsePending(store.GetSetting(PendingKey));
                if (pending.Remove(specimenId))
                    store.SetSetting(PendingKey, string.Join("\n", pending));
                return;
            }

            try
            {
                var data = ReadFile();
                if (data.pending.Remove(specimenId))
                    WriteFile(data);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BTP] Could not update the recap queue: " + exception.Message);
            }
        }

        static List<string> ParsePending(string value)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(value))
                return list;
            foreach (var line in value.Split('\n'))
            {
                var id = line.Trim();
                if (id.Length > 0 && !list.Contains(id))
                    list.Add(id);
            }

            return list;
        }

        static FileData SafeReadFile()
        {
            try
            {
                return ReadFile();
            }
            catch (Exception)
            {
                return new FileData();
            }
        }

        static FileData ReadFile()
        {
            var path = FilePath;
            if (!File.Exists(path))
                return new FileData();
            var data = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
            return data ?? new FileData();
        }

        static void WriteFile(FileData data)
        {
            var path = FilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }
    }
}
