using System;
using System.Collections.Generic;
using System.Text;

namespace BTP.Treatment
{
    /// <summary>
    /// The player's current values of a profile's eight settings: numbers on the profile's min + k * step grid inside
    /// [min, max], and the water-delivery choice. Apply captures a <see cref="Clone"/>; Check Health sends that copy
    /// with <see cref="ToEvaluationJson"/>.
    /// </summary>
    public sealed class TreatmentSettings
    {
        readonly Dictionary<string, double> numbers = new Dictionary<string, double>();
        readonly Dictionary<string, string> choices = new Dictionary<string, string>();

        static string Key(string factorId, string subId) => factorId + "." + subId;

        /// <summary>Every setting at the profile's start value.</summary>
        public static TreatmentSettings FromStart(TreatmentProfile profile)
        {
            var settings = new TreatmentSettings();
            if (profile == null)
                return settings;
            foreach (var factor in profile.Factors)
            foreach (var sub in factor.Subfactors)
            {
                if (sub.IsChoice)
                    settings.choices[Key(factor.Id, sub.Id)] = sub.StartChoice;
                else
                    settings.numbers[Key(factor.Id, sub.Id)] = Snap(sub, sub.Start);
            }

            return settings;
        }

        /// <summary>
        /// Every setting at the bottom of its range (the kiosk's starting point: the trainee dials each control up to the
        /// chart target): numbers at the subfactor's minimum, choices at their first option (the start choice when the
        /// subfactor lists no options).
        /// </summary>
        public static TreatmentSettings FromMinimum(TreatmentProfile profile)
        {
            var settings = new TreatmentSettings();
            if (profile == null)
                return settings;
            foreach (var factor in profile.Factors)
            foreach (var sub in factor.Subfactors)
            {
                if (sub.IsChoice)
                    settings.choices[Key(factor.Id, sub.Id)] = sub.Options.Count > 0 ? sub.Options[0].Value : sub.StartChoice;
                else
                    settings.numbers[Key(factor.Id, sub.Id)] = Snap(sub, sub.Min);
            }

            return settings;
        }

        public double Get(string factorId, string subId) => numbers.TryGetValue(Key(factorId, subId), out var value) ? value : 0d;

        public string GetChoice(string factorId, string subId) => choices.TryGetValue(Key(factorId, subId), out var value) ? value : null;

        /// <summary>Sets a numeric value, snapped to the subfactor's grid and clamped to its range.</summary>
        public void Set(TreatmentSubfactor sub, string factorId, double value)
        {
            if (sub == null || sub.IsChoice)
                return;
            numbers[Key(factorId, sub.Id)] = Snap(sub, value);
        }

        public void SetChoice(string factorId, string subId, string value) => choices[Key(factorId, subId)] = value;

        /// <summary>The nearest value of the min + k * step grid inside [min, max].</summary>
        public static double Snap(TreatmentSubfactor sub, double value)
        {
            var step = sub.Step > 0d ? sub.Step : 1d;
            var k = Math.Round((value - sub.Min) / step);
            var snapped = sub.Min + k * step;
            if (snapped > sub.Max + 1e-9)
                snapped = sub.Min + Math.Floor((sub.Max - sub.Min) / step + 1e-9) * step;
            if (snapped < sub.Min)
                snapped = sub.Min;
            return Math.Round(snapped, 6);
        }

        public bool CanStep(TreatmentProfile profile, string factorId, string subId, int direction)
        {
            var sub = profile?.Find(factorId, subId);
            if (sub == null || direction == 0)
                return false;
            if (sub.IsChoice)
                return sub.Options.Count > 1;
            var current = Get(factorId, subId);
            return direction > 0 ? current + sub.Step <= sub.Max + 1e-9 : current - sub.Step >= sub.Min - 1e-9;
        }

        /// <summary>Moves a numeric setting one step (+1 / -1) on its grid, clamped; returns whether it changed.</summary>
        public bool Step(TreatmentProfile profile, string factorId, string subId, int direction)
        {
            var sub = profile?.Find(factorId, subId);
            if (sub == null)
                return false;
            if (sub.IsChoice)
                return Cycle(profile, factorId, subId, direction);
            var before = Get(factorId, subId);
            var k = Math.Round((before - sub.Min) / sub.Step) + Math.Sign(direction);
            var after = Snap(sub, sub.Min + k * sub.Step);
            numbers[Key(factorId, subId)] = after;
            return Math.Abs(after - before) > 1e-9;
        }

        /// <summary>Moves a choice to the next (+1) or previous (-1) option, wrapping around.</summary>
        public bool Cycle(TreatmentProfile profile, string factorId, string subId, int direction)
        {
            var sub = profile?.Find(factorId, subId);
            if (sub == null || !sub.IsChoice || sub.Options.Count == 0)
                return false;
            var index = sub.IndexOfOption(GetChoice(factorId, subId));
            var count = sub.Options.Count;
            var next = ((index < 0 ? 0 : index) + (direction >= 0 ? 1 : -1) + count) % count;
            var before = GetChoice(factorId, subId);
            choices[Key(factorId, subId)] = sub.Options[next].Value;
            return before != sub.Options[next].Value;
        }

        public TreatmentSettings Clone()
        {
            var copy = new TreatmentSettings();
            foreach (var pair in numbers)
                copy.numbers[pair.Key] = pair.Value;
            foreach (var pair in choices)
                copy.choices[pair.Key] = pair.Value;
            return copy;
        }

        public bool SameAs(TreatmentSettings other)
        {
            if (other == null || other.numbers.Count != numbers.Count || other.choices.Count != choices.Count)
                return false;
            foreach (var pair in numbers)
                if (!other.numbers.TryGetValue(pair.Key, out var value) || Math.Abs(value - pair.Value) > 1e-9)
                    return false;
            foreach (var pair in choices)
                if (!other.choices.TryGetValue(pair.Key, out var value) || value != pair.Value)
                    return false;
            return true;
        }

        /// <summary>
        /// The strict evaluation body: {"profileVersion":1,"values":{"temperature":{"day":25,"night":17},...}} with the
        /// profile's factors and subfactors in order, numbers in the invariant culture, applications as a JSON integer
        /// and the water delivery as its option value. No extra keys.
        /// </summary>
        public string ToEvaluationJson(TreatmentProfile profile)
        {
            var builder = new StringBuilder();
            builder.Append("{\"profileVersion\":").Append(profile.ProfileVersion).Append(",\"values\":{");
            for (var i = 0; i < profile.Factors.Count; i++)
            {
                var factor = profile.Factors[i];
                if (i > 0)
                    builder.Append(',');
                builder.Append(SimpleJson.Quote(factor.Id)).Append(":{");
                for (var j = 0; j < factor.Subfactors.Count; j++)
                {
                    var sub = factor.Subfactors[j];
                    if (j > 0)
                        builder.Append(',');
                    builder.Append(SimpleJson.Quote(sub.Id)).Append(':');
                    if (sub.IsChoice)
                        builder.Append(SimpleJson.Quote(GetChoice(factor.Id, sub.Id) ?? sub.StartChoice));
                    else if (sub.Id == "applications")
                        builder.Append(SimpleJson.Integer(Get(factor.Id, sub.Id)));
                    else
                        builder.Append(SimpleJson.Number(Get(factor.Id, sub.Id)));
                }

                builder.Append('}');
            }

            builder.Append("}}");
            return builder.ToString();
        }

        /// <summary>Settings from a stored evaluation body (the per-specimen saved settings), checked against the profile.</summary>
        public static TreatmentSettings FromEvaluationJson(TreatmentProfile profile, string json)
        {
            if (profile == null || !SimpleJson.TryParse(json, out var value, out _))
                return null;
            var values = SimpleJson.Object(SimpleJson.Get(SimpleJson.Object(value), "values"));
            if (values == null)
                return null;
            // Every value is overwritten below (a missing one rejects the record), so the base only fixes the key set.
            var settings = FromMinimum(profile);
            foreach (var factor in profile.Factors)
            {
                var stored = SimpleJson.Object(SimpleJson.Get(values, factor.Id));
                if (stored == null)
                    return null;
                foreach (var sub in factor.Subfactors)
                {
                    if (sub.IsChoice)
                    {
                        var choice = SimpleJson.String(stored, sub.Id);
                        if (sub.IndexOfOption(choice) < 0)
                            return null;
                        settings.SetChoice(factor.Id, sub.Id, choice);
                    }
                    else
                    {
                        if (!SimpleJson.TryNumber(stored, sub.Id, out var number))
                            return null;
                        settings.Set(sub, factor.Id, number);
                    }
                }
            }

            return settings;
        }
    }
}
