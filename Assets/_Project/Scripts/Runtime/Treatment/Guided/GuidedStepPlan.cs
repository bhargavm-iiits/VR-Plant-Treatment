using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTP.Treatment
{
    /// <summary>The four hands-on steps, in the order the guide runs them.</summary>
    public enum GuidedStepKind
    {
        Sunlight = 0,
        Fertiliser = 1,
        Spray = 2,
        Water = 3,
    }

    /// <summary>
    /// One guided step with its exact target, read from the case profile (and <see cref="GuidedTreatmentConfig"/>):
    /// what the instruction popup says and what the step's validation accepts. Pure data (unit tested).
    /// </summary>
    public sealed class GuidedStepPlan
    {
        public GuidedStepKind Kind;
        public int Index;
        public int Count;
        public string Title;
        /// <summary>The popup's one-line instruction ("Set the temperature board to 24.5°C day and 18°C night, then press Apply.").</summary>
        public string Instruction;
        /// <summary>The exact target shown big on the popup ("Day 24.5°C · Night 18°C", "2 scoops", "3 sprays", "−14%").</summary>
        public string TargetText;
        /// <summary>True when the profile prescribes nothing for this factor and the config's maintenance value is used.</summary>
        public bool Maintenance;

        // Sunlight / temperature.
        public double DayTarget, NightTarget, DayMin, DayMax, NightMin, NightMax;
        public string TemperatureUnit = "°C";

        // Fertiliser and spray.
        public int DoseTarget;
        public string ProductId;
        public string ProductLabel;

        // Water.
        public double WaterStart, WaterAmount, WaterMin, WaterMax;
        public string WaterUnit = "%";

        public string StepLabel => $"Step {Index + 1}/{Count}";

        /// <summary>"2 scoops", "1 scoop", "3 sprays".</summary>
        public static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        internal static string Number(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>"+12%", "−14%", "0%" (a true minus sign).</summary>
        public static string Signed(double value, string unit)
        {
            var rounded = Math.Round(value, 2);
            if (Math.Abs(rounded) < 1e-9)
                return "0" + unit;
            return (rounded > 0 ? "+" : "−") + Number(Math.Abs(rounded)) + unit;
        }
    }

    /// <summary>
    /// Builds the four <see cref="GuidedStepPlan"/>s for a profile and validates the player's actions against them
    /// (pure; unit tested). Temperature targets are the middle of each band on the profile's step grid; any value inside
    /// the band is accepted. Scoops and sprays come from the dose bands (config units per scoop / spray), or the config's
    /// maintenance values when the profile prescribes none. The water meter target moves the root zone to the middle of
    /// its band; any amount that lands inside the band is accepted.
    /// </summary>
    public static class GuidedPlanner
    {
        public static List<GuidedStepPlan> Build(TreatmentProfile profile, GuidedTreatmentConfig config)
        {
            var plans = new List<GuidedStepPlan>();
            if (profile == null)
                return plans;
            config ??= GuidedTreatmentConfig.Load();
            var custom = config.OverrideFor(profile.CaseId ?? $"{profile.PlantId}/{profile.DiseaseId}");

            // 1. Sunlight and temperature.
            var day = profile.Find(TreatmentProfile.Temperature, "day");
            var night = profile.Find(TreatmentProfile.Temperature, "night");
            var sun = new GuidedStepPlan { Kind = GuidedStepKind.Sunlight, Title = "Sunlight & temperature" };
            if (day != null)
            {
                sun.DayMin = day.TargetMin;
                sun.DayMax = day.TargetMax;
                sun.DayTarget = TreatmentSettings.Snap(day, (day.TargetMin + day.TargetMax) * 0.5d);
                sun.TemperatureUnit = string.IsNullOrEmpty(day.Unit) ? "°C" : day.Unit;
            }

            if (night != null)
            {
                sun.NightMin = night.TargetMin;
                sun.NightMax = night.TargetMax;
                sun.NightTarget = TreatmentSettings.Snap(night, (night.TargetMin + night.TargetMax) * 0.5d);
            }

            var u = sun.TemperatureUnit;
            sun.TargetText = night != null
                ? $"Day {GuidedStepPlan.Number(sun.DayTarget)}{u}  ·  Night {GuidedStepPlan.Number(sun.NightTarget)}{u}"
                : $"{GuidedStepPlan.Number(sun.DayTarget)}{u}";
            sun.Instruction = night != null
                ? $"Set the temperature board to {GuidedStepPlan.Number(sun.DayTarget)}{u} (day) and {GuidedStepPlan.Number(sun.NightTarget)}{u} (night), then press Apply."
                : $"Set the temperature board to {GuidedStepPlan.Number(sun.DayTarget)}{u}, then press Apply.";
            plans.Add(sun);

            // 2. Fertiliser scoops.
            var fertiliser = profile.Find(TreatmentProfile.Fertiliser);
            var dose = profile.Find(TreatmentProfile.Fertiliser, "dose");
            var product = config.ProductForNutrient(fertiliser?.Nutrient);
            if (!string.IsNullOrEmpty(custom?.product) && config.FindProduct(custom.product) != null)
                product = config.FindProduct(custom.product);
            var prescribed = dose != null && dose.TargetMax > 1e-9 && !string.IsNullOrWhiteSpace(fertiliser?.Nutrient);
            var scoops = prescribed
                ? Math.Max(1, (int)Math.Round((dose.TargetMin + dose.TargetMax) * 0.5d / Math.Max(0.1d, config.doseUnitsPerScoop), MidpointRounding.AwayFromZero))
                : config.maintenanceScoops;
            if (custom != null && custom.scoops > 0)
                scoops = custom.scoops;
            var feed = new GuidedStepPlan
            {
                Kind = GuidedStepKind.Fertiliser, Title = "NPK fertiliser", DoseTarget = scoops, Maintenance = !prescribed,
                ProductId = product.id, ProductLabel = product.label,
            };
            feed.TargetText = $"{GuidedStepPlan.Plural(scoops, "scoop")} of {product.label}";
            feed.Instruction = $"Open the glass and add {GuidedStepPlan.Plural(scoops, "scoop")} of {product.label} to the soil around the roots.";
            plans.Add(feed);

            // 3. Spray presses.
            var spray = profile.Find(TreatmentProfile.Spray);
            var sprayDose = profile.Find(TreatmentProfile.Spray, "dose");
            var sprayPrescribed = spray != null && spray.Indicated && sprayDose != null && sprayDose.TargetMin > 1e-9;
            var sprays = sprayPrescribed
                ? Math.Max(1, (int)Math.Ceiling(sprayDose.TargetMin / Math.Max(0.1d, config.sprayUnitsPerPress) - 1e-9))
                : config.maintenanceSprays;
            if (custom != null && custom.sprays > 0)
                sprays = custom.sprays;
            var mist = new GuidedStepPlan { Kind = GuidedStepKind.Spray, Title = "Manual spray", DoseTarget = sprays, Maintenance = !sprayPrescribed };
            mist.TargetText = GuidedStepPlan.Plural(sprays, "spray");
            mist.Instruction = $"Open the glass and apply exactly {GuidedStepPlan.Plural(sprays, "spray")} to the leaves.";
            plans.Add(mist);

            // 4. Water meter.
            var rootZone = profile.Find(TreatmentProfile.SoilMoisture, "rootZone");
            var water = new GuidedStepPlan { Kind = GuidedStepKind.Water, Title = "Humidity & watering" };
            if (rootZone != null)
            {
                water.WaterUnit = string.IsNullOrEmpty(rootZone.Unit) ? "%" : rootZone.Unit;
                water.WaterStart = rootZone.Start;
                water.WaterMin = rootZone.TargetMin;
                water.WaterMax = rootZone.TargetMax;
                var rounding = Math.Max(Math.Max(rootZone.Step, 1e-3d), config.waterRounding);
                var amount = Math.Round(((rootZone.TargetMin + rootZone.TargetMax) * 0.5d - rootZone.Start) / rounding) * rounding;
                if (custom != null && !float.IsNaN(custom.waterAmount) && custom.waterAmount >= -100f)
                    amount = custom.waterAmount;
                // Keep the result on the soil's own range.
                amount = Math.Max(rootZone.Min - rootZone.Start, Math.Min(rootZone.Max - rootZone.Start, amount));
                water.WaterAmount = amount;
            }

            water.TargetText = GuidedStepPlan.Signed(water.WaterAmount, water.WaterUnit);
            water.Instruction = $"Set the water meter to {water.TargetText}, then press Apply (ON) with the glass closed.";
            plans.Add(water);

            for (var i = 0; i < plans.Count; i++)
            {
                plans[i].Index = i;
                plans[i].Count = plans.Count;
            }

            return plans;
        }

        /// <summary>
        /// Re-aims the water step at the soil's actual moisture (a specimen revisited keeps its watered soil): the meter target
        /// moves <paramref name="current"/> to the middle of the band, rounded like <see cref="Build"/>, inside the soil's range.
        /// </summary>
        public static void RetargetWater(GuidedStepPlan plan, TreatmentSubfactor rootZone, double current, GuidedTreatmentConfig config)
        {
            if (plan == null || rootZone == null)
                return;
            config ??= GuidedTreatmentConfig.Load();
            var rounding = Math.Max(Math.Max(rootZone.Step, 1e-3d), config.waterRounding);
            var amount = Math.Round(((rootZone.TargetMin + rootZone.TargetMax) * 0.5d - current) / rounding) * rounding;
            amount = Math.Max(rootZone.Min - current, Math.Min(rootZone.Max - current, amount));
            plan.WaterStart = current;
            plan.WaterAmount = amount;
            plan.TargetText = GuidedStepPlan.Signed(amount, plan.WaterUnit);
            plan.Instruction = $"Set the water meter to {plan.TargetText}, then press Apply (ON) with the glass closed.";
        }

        /// <summary>Whether the board's day/night values are inside the bands; otherwise what is wrong.</summary>
        public static bool ValidateTemperature(GuidedStepPlan plan, double day, double night, bool hasNight, out string problem)
        {
            var u = plan.TemperatureUnit;
            string Check(string name, double value, double min, double max, double target)
            {
                if (value >= min - 1e-9 && value <= max + 1e-9)
                    return null;
                return $"{name} is {GuidedStepPlan.Number(value)}{u} — {(value < min ? "too low" : "too high")}. Set {GuidedStepPlan.Number(target)}{u}.";
            }

            problem = Check("Day temperature", day, plan.DayMin, plan.DayMax, plan.DayTarget);
            if (problem == null && hasNight)
                problem = Check("Night temperature", night, plan.NightMin, plan.NightMax, plan.NightTarget);
            return problem == null;
        }

        /// <summary>Whether adding <paramref name="amount"/> to <paramref name="current"/> lands in the root-zone band.</summary>
        public static bool ValidateWater(GuidedStepPlan plan, double current, double amount, out string problem)
        {
            var result = current + amount;
            var u = plan.WaterUnit;
            if (result >= plan.WaterMin - 1e-9 && result <= plan.WaterMax + 1e-9)
            {
                problem = null;
                return true;
            }

            problem = $"Water meter {GuidedStepPlan.Signed(amount, u)} leaves the root zone at {GuidedStepPlan.Number(result)}{u}. " +
                      $"Set {plan.TargetText}.";
            return false;
        }

        /// <summary>
        /// The eight settings recorded when every step is done: the board's temperatures, each fertiliser and spray setting
        /// on its profile target (prescribed doses from the counted scoops/sprays, clamped to the band), soil left to the
        /// station's watering model.
        /// </summary>
        public static TreatmentSettings ComposeSettings(TreatmentProfile profile, TreatmentSettings board, IReadOnlyList<GuidedStepPlan> plans,
            int scoops, int sprays, GuidedTreatmentConfig config)
        {
            var settings = board != null ? board.Clone() : TreatmentSettings.FromMinimum(profile);
            if (profile == null)
                return settings;
            config ??= GuidedTreatmentConfig.Load();
            void OnBand(string factorId, string subId, double? preferred)
            {
                var sub = profile.Find(factorId, subId);
                if (sub == null || sub.IsChoice)
                    return;
                var value = preferred ?? (sub.TargetMin + sub.TargetMax) * 0.5d;
                value = Math.Max(sub.TargetMin, Math.Min(sub.TargetMax, value));
                settings.Set(sub, factorId, value);
                // Snapping can leave a narrow band; fall back to its minimum.
                if (!sub.OnTarget(settings.Get(factorId, subId)))
                    settings.Set(sub, factorId, sub.TargetMin);
            }

            GuidedStepPlan Plan(GuidedStepKind kind)
            {
                foreach (var plan in plans)
                    if (plan.Kind == kind)
                        return plan;
                return null;
            }

            var feed = Plan(GuidedStepKind.Fertiliser);
            OnBand(TreatmentProfile.Fertiliser, "dose", feed != null && !feed.Maintenance ? scoops * (double)config.doseUnitsPerScoop : (double?)null);
            OnBand(TreatmentProfile.Fertiliser, "applications", null);
            var mist = Plan(GuidedStepKind.Spray);
            OnBand(TreatmentProfile.Spray, "dose", mist != null && !mist.Maintenance ? sprays * (double)config.sprayUnitsPerPress : (double?)null);
            OnBand(TreatmentProfile.Spray, "targetCoverage", null);
            return settings;
        }
    }
}
