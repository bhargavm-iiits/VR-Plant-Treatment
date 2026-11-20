using System;

namespace BTP.Core
{
    /// <summary>
    /// One Farm plant the player selected: its case (plant and disease) plus which plant of the disease row it is. Two
    /// plants of the same crop and disease differ only by <see cref="PlantIndex"/>, so the specimen ID includes it. Plain
    /// data, carried by <see cref="SelectionState"/> across scene switches.
    /// </summary>
    [Serializable]
    public sealed class SpecimenRef : IEquatable<SpecimenRef>
    {
        public readonly string SpecimenId;
        public readonly string PlantId;
        public readonly string DiseaseId;
        /// <summary>The plant's index in its Farm row (0 for a single specimen).</summary>
        public readonly int PlantIndex;

        public SpecimenRef(string specimenId, string plantId, string diseaseId, int plantIndex)
        {
            PlantId = plantId ?? string.Empty;
            DiseaseId = diseaseId ?? string.Empty;
            PlantIndex = Math.Max(0, plantIndex);
            SpecimenId = string.IsNullOrEmpty(specimenId) ? DefaultId(PlantId, DiseaseId, PlantIndex) : specimenId;
        }

        /// <summary>"tomato/early_blight#03": stable across sessions because Farm rows are built deterministically.</summary>
        public static string DefaultId(string plantId, string diseaseId, int plantIndex) =>
            $"{plantId}/{(string.IsNullOrEmpty(diseaseId) ? "specimen" : diseaseId)}#{plantIndex + 1:00}";

        public bool IsCase(string plantId, string diseaseId) => PlantId == plantId && DiseaseId == diseaseId;

        public bool Equals(SpecimenRef other) =>
            other != null && SpecimenId == other.SpecimenId && PlantId == other.PlantId && DiseaseId == other.DiseaseId && PlantIndex == other.PlantIndex;

        public override bool Equals(object obj) => obj is SpecimenRef other && Equals(other);
        public override int GetHashCode() => SpecimenId.GetHashCode();
        public override string ToString() => SpecimenId;
    }
}
