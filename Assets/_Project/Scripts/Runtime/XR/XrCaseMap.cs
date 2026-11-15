using System;
using System.Collections.Generic;
using BTP.Plants;
using UnityEngine;

namespace BTP.XR
{
    /// <summary>
    /// The shared, versioned mapping from an exact backend disease_id (for example "Tomato___Early_blight") to a Unity
    /// teaching case, its Farm specimen and its recipe (Resources/XR/xr_case_map.json; the backend keeps the same file).
    /// Matching is exact and case-sensitive: "Tomato___Early_blight" and "Potato___Early_blight" are different cases, and
    /// an unknown class is reported with a reason instead of falling back to Unity's default case.
    /// </summary>
    public sealed class XrCaseMap
    {
        public const string ResourcePath = "XR/xr_case_map";

        [Serializable]
        public sealed class Row
        {
            public string backendDiseaseId;
            public string plantId;
            public string diseaseId;
            public string specimenId;
            public string recipeId;
        }

        [Serializable]
        sealed class File
        {
            public int mapVersion = 0;
            public Row[] rows = Array.Empty<Row>();
        }

        readonly Dictionary<string, Row> byBackendId = new Dictionary<string, Row>(StringComparer.Ordinal);

        public int Version { get; private set; }
        public IReadOnlyCollection<Row> Rows => byBackendId.Values;

        XrCaseMap() { }

        /// <summary>Parses and checks the map. Invalid rows are left out and reported in <paramref name="problems"/>.</summary>
        public static XrCaseMap Parse(string json, out List<string> problems)
        {
            problems = new List<string>();
            var map = new XrCaseMap();
            File file;
            try
            {
                file = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<File>(json);
            }
            catch (Exception exception)
            {
                problems.Add("The case map is not valid JSON: " + exception.Message);
                return map;
            }

            if (file == null)
            {
                problems.Add("The case map is empty.");
                return map;
            }

            map.Version = file.mapVersion;
            if (file.mapVersion <= 0)
                problems.Add("The case map has no mapVersion.");
            var specimens = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in file.rows ?? Array.Empty<Row>())
            {
                if (row == null)
                    continue;
                var label = string.IsNullOrEmpty(row.backendDiseaseId) ? "(row without backendDiseaseId)" : row.backendDiseaseId;
                if (string.IsNullOrEmpty(row.backendDiseaseId) || string.IsNullOrEmpty(row.plantId) || string.IsNullOrEmpty(row.diseaseId) ||
                    string.IsNullOrEmpty(row.specimenId) || string.IsNullOrEmpty(row.recipeId))
                {
                    problems.Add($"{label}: every row needs backendDiseaseId, plantId, diseaseId, specimenId and recipeId.");
                    continue;
                }

                if (!IsUnityCase(row.plantId, row.diseaseId))
                {
                    problems.Add($"{label}: {row.plantId}/{row.diseaseId} is not one of Unity's disease cases.");
                    continue;
                }

                if (map.byBackendId.ContainsKey(row.backendDiseaseId))
                {
                    problems.Add($"{label}: listed twice.");
                    continue;
                }

                if (!specimens.Add(row.specimenId))
                {
                    problems.Add($"{label}: specimen '{row.specimenId}' is already used by another row.");
                    continue;
                }

                map.byBackendId[row.backendDiseaseId] = row;
            }

            return map;
        }

        public static XrCaseMap LoadDefault(out List<string> problems)
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                problems = new List<string> { $"Resources/{ResourcePath}.json is missing." };
                return Parse(null, out _);
            }

            return Parse(asset.text, out problems);
        }

        /// <summary>Resolves an exact backend class. False with a learner-facing reason when Unity has no case for it.</summary>
        public bool TryResolve(string backendDiseaseId, out Row row, out string reason)
        {
            row = null;
            if (string.IsNullOrEmpty(backendDiseaseId))
            {
                reason = "The scan has no diagnosis yet.";
                return false;
            }

            if (byBackendId.TryGetValue(backendDiseaseId, out row))
            {
                reason = null;
                return true;
            }

            reason = backendDiseaseId.EndsWith("healthy", StringComparison.OrdinalIgnoreCase)
                ? "The leaf was classified as healthy, so there is no disease to treat in VR."
                : $"'{backendDiseaseId}' has no VR treatment case in this build yet.";
            return false;
        }

        public Row FindBySpecimen(string specimenId)
        {
            foreach (var row in byBackendId.Values)
                if (row.specimenId == specimenId)
                    return row;
            return null;
        }

        // Connected CropScan XR covers the 24 PlantVillage classes only; the ten other crops stay local to this app.
        static bool IsUnityCase(string plantId, string diseaseId) => DiseaseClasses.IsPlantVillageCase(plantId, diseaseId);
    }
}
