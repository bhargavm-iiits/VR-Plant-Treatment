using System;

namespace BTP.Treatment
{
    /// <summary>
    /// Identifies one teaching case: a plant and one of its diseases. Disease IDs repeat across
    /// plants ("black_rot", "bacterial_spot", "early_blight"), so a case is never looked up by the
    /// disease ID alone.
    /// </summary>
    public readonly struct CaseKey : IEquatable<CaseKey>
    {
        public readonly string PlantId;
        public readonly string DiseaseId;

        public CaseKey(string plantId, string diseaseId)
        {
            PlantId = plantId ?? string.Empty;
            DiseaseId = diseaseId ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(PlantId) && !string.IsNullOrEmpty(DiseaseId);

        public bool Equals(CaseKey other) => PlantId == other.PlantId && DiseaseId == other.DiseaseId;
        public override bool Equals(object obj) => obj is CaseKey other && Equals(other);
        public override int GetHashCode() => ((PlantId?.GetHashCode() ?? 0) * 397) ^ (DiseaseId?.GetHashCode() ?? 0);
        public static bool operator ==(CaseKey a, CaseKey b) => a.Equals(b);
        public static bool operator !=(CaseKey a, CaseKey b) => !a.Equals(b);
        public override string ToString() => PlantId + "/" + DiseaseId;

        /// <summary>Parses "plant/disease"; returns false for anything else.</summary>
        public static bool TryParse(string text, out CaseKey key)
        {
            key = default;
            if (string.IsNullOrEmpty(text))
                return false;
            var slash = text.IndexOf('/');
            if (slash <= 0 || slash == text.Length - 1 || text.IndexOf('/', slash + 1) >= 0)
                return false;
            key = new CaseKey(text.Substring(0, slash), text.Substring(slash + 1));
            return true;
        }
    }
}
