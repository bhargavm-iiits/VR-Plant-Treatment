using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTP.XR
{
    /// <summary>
    /// Farm specimens a connected session can focus (Resources/XR/xr_specimens.json). Each names one plant of a Farm
    /// disease row and an authored standing distance; <see cref="StandCandidates"/> turns that into standing points on
    /// clear ground (the row head first, then the side walkways). Pure data, so it is unit tested without a scene.
    /// </summary>
    public sealed class XrSpecimenCatalog
    {
        public const string ResourcePath = "XR/xr_specimens";

        [Serializable]
        public sealed class Specimen
        {
            public string specimenId;
            public string plantId;
            public string diseaseId;
            public int plantIndex;
            public float standDistance = 1.5f;
            /// <summary>Metres to the side (row's right is positive), so the row's name sign is beside the learner, not in front.</summary>
            public float standSide;
        }

        [Serializable]
        sealed class File
        {
            public Specimen[] specimens = Array.Empty<Specimen>();
        }

        readonly Dictionary<string, Specimen> byId = new Dictionary<string, Specimen>(StringComparer.Ordinal);

        XrSpecimenCatalog() { }

        public IReadOnlyCollection<Specimen> All => byId.Values;

        public static XrSpecimenCatalog Parse(string json, out List<string> problems)
        {
            problems = new List<string>();
            var catalog = new XrSpecimenCatalog();
            File file = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(json))
                    file = JsonUtility.FromJson<File>(json);
            }
            catch (Exception exception)
            {
                problems.Add("The specimen list is not valid JSON: " + exception.Message);
            }

            foreach (var specimen in file?.specimens ?? Array.Empty<Specimen>())
            {
                if (specimen == null || string.IsNullOrEmpty(specimen.specimenId))
                {
                    problems.Add("A specimen has no specimenId.");
                    continue;
                }

                if (string.IsNullOrEmpty(specimen.plantId) || string.IsNullOrEmpty(specimen.diseaseId) || specimen.plantIndex < 0 ||
                    specimen.standDistance < 0.5f || specimen.standDistance > 5f || Mathf.Abs(specimen.standSide) > 2f)
                {
                    problems.Add($"Specimen '{specimen.specimenId}' needs a plant, a disease, plantIndex >= 0, a standDistance of 0.5-5 m and a standSide within 2 m.");
                    continue;
                }

                if (!catalog.byId.ContainsKey(specimen.specimenId))
                    catalog.byId.Add(specimen.specimenId, specimen);
                else
                    problems.Add($"Specimen '{specimen.specimenId}' is listed twice.");
            }

            return catalog;
        }

        public static XrSpecimenCatalog LoadDefault(out List<string> problems)
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset != null)
                return Parse(asset.text, out problems);
            problems = new List<string> { $"Resources/{ResourcePath}.json is missing." };
            return new XrSpecimenCatalog();
        }

        public bool TryGet(string specimenId, out Specimen specimen)
        {
            specimen = null;
            return !string.IsNullOrEmpty(specimenId) && byId.TryGetValue(specimenId, out specimen);
        }

        /// <summary>
        /// Standing points for a plant, in preference order: in front of it towards the row head (rows run along the row
        /// root's +Z, so the head is its -Z), shifted <paramref name="side"/> metres so the row's sign is beside the learner;
        /// then straight in front; then the left and right walkways. Heights are left at the plant's base; the scene code
        /// drops each onto the floor and checks it is clear. The learner always faces the plant.
        /// </summary>
        public static Vector3[] StandCandidates(Vector3 plant, Vector3 rowForward, float distance, float side = 0f)
        {
            var forward = Vector3.ProjectOnPlane(rowForward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            return new[]
            {
                plant - forward * distance + right * side,
                plant - forward * distance,
                plant - right * distance,
                plant + right * distance,
                plant - forward * (distance + 0.8f) + right * side,
            };
        }

        /// <summary>Yaw rotation at <paramref name="stand"/> that faces <paramref name="plant"/>.</summary>
        public static Quaternion Facing(Vector3 stand, Vector3 plant)
        {
            var look = Vector3.ProjectOnPlane(plant - stand, Vector3.up);
            return look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
        }
    }
}
