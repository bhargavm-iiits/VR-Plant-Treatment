namespace BTP.Treatment
{
    /// <summary>
    /// The Apply gate of the chamber kiosk (pure; unit tested). Each of the four controls is "done" when it reads the chart
    /// target: Temperature, Fertiliser and Spray when every numeric setting is inside its target band; Soil Moisture when the
    /// soil the WATER station reached (rounded as Check Health sends it) with the delivery actually used is on target and
    /// the water is not running. Apply is offered only with all four done.
    /// </summary>
    public static class TreatmentGate
    {
        public const int ControlCount = 4;

        /// <summary>Whether control <paramref name="index"/> (0..3, panel order) reads its chart target.</summary>
        public static bool ControlDone(TreatmentProfile profile, TreatmentSettings settings, int index, RootZoneWateringModel watering)
        {
            if (profile == null || settings == null || index < 0 || index >= TreatmentProfile.Layout.Length)
                return false;
            var factorId = TreatmentProfile.Layout[index].factor;
            var factor = profile.Find(factorId);
            if (factor == null)
                return false;
            if (factorId == TreatmentProfile.SoilMoisture)
                return SoilDone(profile, settings, watering);
            foreach (var sub in factor.Subfactors)
            {
                if (sub.IsChoice)
                    continue;
                if (!sub.OnTarget(settings.Get(factorId, sub.Id)))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Soil Moisture: with the WATER station's model, the moisture it reached (as sent to the server) and its delivery;
        /// without one, the panel's own two settings (as the server's hold rule would measure them).
        /// </summary>
        public static bool SoilDone(TreatmentProfile profile, TreatmentSettings settings, RootZoneWateringModel watering)
        {
            var rootZone = profile?.Find(TreatmentProfile.SoilMoisture, "rootZone");
            var irrigation = profile?.Find(TreatmentProfile.SoilMoisture, "irrigation");
            if (rootZone == null)
                return false;
            if (watering != null)
                return !watering.Running &&
                       SoilMoistureRules.Status(rootZone, irrigation, watering.MeasuredForServer, watering.Irrigation) == SoilMoistureRules.Verdict.OnTarget;
            if (settings == null)
                return false;
            var choice = settings.GetChoice(TreatmentProfile.SoilMoisture, "irrigation") ?? SoilMoistureRules.Hold;
            var measured = SoilMoistureRules.MeasuredRootZone(rootZone.Start, settings.Get(TreatmentProfile.SoilMoisture, "rootZone"), choice);
            return SoilMoistureRules.Status(rootZone, irrigation, measured, choice) == SoilMoistureRules.Verdict.OnTarget;
        }

        /// <summary>How many of the four controls are done (0..4).</summary>
        public static int DoneCount(TreatmentProfile profile, TreatmentSettings settings, RootZoneWateringModel watering)
        {
            var done = 0;
            for (var i = 0; i < ControlCount; i++)
                if (ControlDone(profile, settings, i, watering))
                    done++;
            return done;
        }

        public static bool AllDone(TreatmentProfile profile, TreatmentSettings settings, RootZoneWateringModel watering) =>
            DoneCount(profile, settings, watering) == ControlCount;

        /// <summary>"Set all 4 controls to the chart targets (2/4 done)".</summary>
        public static string Hint(int done) => $"Set all {ControlCount} controls to the chart targets ({done}/{ControlCount} done)";
    }
}
