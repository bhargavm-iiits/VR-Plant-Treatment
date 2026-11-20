using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Local previews for the Soil Moisture page that follow the server's rules exactly (pure; unit tested): leaf wetness
    /// is the start + 30 with overhead watering, else the start - 20, clamped to 0-100; holding steady keeps the
    /// root-zone moisture at its start value whatever the slider says.
    /// </summary>
    public static class SoilMoistureRules
    {
        public const string Hold = "hold", RootZone = "rootZone", Overhead = "overhead";

        public static double PreviewLeafWetness(double start, string irrigation) =>
            Mathf.Clamp((float)(start + (irrigation == Overhead ? 30d : -20d)), 0f, 100f);

        /// <summary>The root-zone moisture the delivery actually reaches.</summary>
        public static double MeasuredRootZone(double start, double set, string irrigation) => irrigation == Hold ? start : set;

        public enum Verdict
        {
            OnTarget,
            Below,
            Above,
            OverheadWetsLeaves,
            WrongDelivery,
        }

        /// <summary>The page's status for a measured root-zone value and the chosen delivery.</summary>
        public static Verdict Status(TreatmentSubfactor rootZone, TreatmentSubfactor irrigationSub, double measured, string irrigation)
        {
            if (irrigation == Overhead)
                return Verdict.OverheadWetsLeaves;
            if (rootZone != null && measured < rootZone.TargetMin - 1e-9)
                return Verdict.Below;
            if (rootZone != null && measured > rootZone.TargetMax + 1e-9)
                return Verdict.Above;
            if (irrigationSub != null && !irrigationSub.IsTargetChoice(irrigation))
                return Verdict.WrongDelivery;
            return Verdict.OnTarget;
        }

        public static string StatusText(Verdict verdict)
        {
            switch (verdict)
            {
                case Verdict.OnTarget: return "On target";
                case Verdict.Below: return "Below target";
                case Verdict.Above: return "Above target";
                case Verdict.OverheadWetsLeaves: return "Overhead wets the leaves";
                default: return "Deliver water at the root zone";
            }
        }

        /// <summary>"Leaf wetness 22/100".</summary>
        public static string LeafWetnessText(double wetness) => $"Leaf wetness {Mathf.RoundToInt((float)wetness)}/100";
    }
}
