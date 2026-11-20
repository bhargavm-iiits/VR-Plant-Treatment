using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTP.Treatment
{
    /// <summary>One choice of a choice subfactor (water delivery): the value sent to the server and its label.</summary>
    public sealed class TreatmentChoiceOption
    {
        public string Value;
        public string Label;
    }

    /// <summary>
    /// One adjustable setting of a treatment profile. Numeric subfactors have a range, step, start value and target band;
    /// the water-delivery subfactor is a choice with options, a string start value and the target options.
    /// </summary>
    public sealed class TreatmentSubfactor
    {
        public string Id;
        public string Label;
        public string Unit;
        public double Min;
        public double Max;
        public double Step = 1d;
        public double Start;
        public double TargetMin;
        public double TargetMax;
        public bool IsChoice;
        public string StartChoice;
        public readonly List<TreatmentChoiceOption> Options = new List<TreatmentChoiceOption>();
        public readonly List<string> Targets = new List<string>();

        /// <summary>Decimals shown for a value: from the step (0.5 -> 1, 1 -> 0, 0.25 -> 2).</summary>
        public int Decimals
        {
            get
            {
                var step = Math.Abs(Step);
                for (var d = 0; d < 4; d++)
                {
                    var scaled = step * Math.Pow(10, d);
                    if (Math.Abs(scaled - Math.Round(scaled)) < 1e-6)
                        return d;
                }

                return 4;
            }
        }

        /// <summary>"°C" and "%" attach to the number; other units ("training units") follow a space.</summary>
        static bool Tight(string unit) => unit == "%" || unit == "°C" || unit == "°F" || string.IsNullOrEmpty(unit);

        public string Number(double value) => value.ToString("F" + Decimals, CultureInfo.InvariantCulture);

        /// <summary>A number without trailing zeros ("22", "22.5"), for target bands and steps.</summary>
        public static string Compact(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>"30.0°C", "72%", "3 training units", "2 applications" ("1 application").</summary>
        public string Format(double value)
        {
            if (IsChoice)
                return "";
            var number = Number(value);
            var unit = Unit ?? "";
            if (Tight(unit))
                return number + unit;
            // A single item reads "1 application", "1 training unit".
            if (Math.Abs(value - 1d) < 1e-9 && unit.EndsWith("s", StringComparison.Ordinal))
                unit = unit.Substring(0, unit.Length - 1);
            return number + " " + unit;
        }

        string Band()
        {
            var unit = Unit ?? "";
            var range = Math.Abs(TargetMin - TargetMax) < 1e-9 ? Compact(TargetMin) : Compact(TargetMin) + "–" + Compact(TargetMax);
            return Tight(unit) ? range + unit : range + " " + unit;
        }

        /// <summary>"Target 22–27°C", "Target 0 training units"; a choice lists its target options.</summary>
        public string TargetText
        {
            get
            {
                if (!IsChoice)
                    return "Target " + Band();
                var labels = new List<string>();
                foreach (var target in Targets)
                    labels.Add(ShortLabel(target));
                return labels.Count > 0 ? "Target " + string.Join(" or ", labels) : "";
            }
        }

        /// <summary>"Step 0.5°C".</summary>
        public string StepText
        {
            get
            {
                if (IsChoice)
                    return "";
                var unit = Unit ?? "";
                var number = Compact(Step);
                if (Tight(unit))
                    return "Step " + number + unit;
                if (Math.Abs(Step - 1d) < 1e-9 && unit.EndsWith("s", StringComparison.Ordinal))
                    unit = unit.Substring(0, unit.Length - 1);
                return "Step " + number + " " + unit;
            }
        }

        /// <summary>The backend label of a choice value ("Hold steady"), or the value itself.</summary>
        public string OptionLabel(string value)
        {
            foreach (var option in Options)
                if (option.Value == value)
                    return string.IsNullOrEmpty(option.Label) ? option.Value : option.Label;
            return value ?? "";
        }

        /// <summary>The short on-panel label of a water-delivery value: "Hold steady", "Root-zone", "Overhead".</summary>
        public string ShortLabel(string value)
        {
            switch (value)
            {
                case "hold": return "Hold steady";
                case "rootZone": return "Root-zone";
                case "overhead": return "Overhead";
                default: return OptionLabel(value);
            }
        }

        public bool IsTargetChoice(string value) => Targets.Contains(value);

        public bool OnTarget(double value) => value >= TargetMin - 1e-9 && value <= TargetMax + 1e-9;

        public int IndexOfOption(string value)
        {
            for (var i = 0; i < Options.Count; i++)
                if (Options[i].Value == value)
                    return i;
            return -1;
        }
    }

    /// <summary>One of the four controls (temperature, fertiliser, spray, soil moisture) with its two subfactors.</summary>
    public sealed class TreatmentFactor
    {
        public string Id;
        public string Label;
        /// <summary>Fertiliser: the confirmed deficient nutrient ("Potassium"), or null when none is confirmed.</summary>
        public string Nutrient;
        /// <summary>Fertiliser: why a supplement is (or is not) indicated.</summary>
        public string Evidence;
        /// <summary>Spray: whether a spray is indicated for this case.</summary>
        public bool Indicated;
        /// <summary>Spray: the marked target ("Marked lower-canopy foliage"), or null.</summary>
        public string Target;
        public readonly List<TreatmentSubfactor> Subfactors = new List<TreatmentSubfactor>();

        public TreatmentSubfactor Find(string subId)
        {
            foreach (var sub in Subfactors)
                if (sub.Id == subId)
                    return sub;
            return null;
        }
    }

    /// <summary>
    /// A treatment case profile from GET /api/treatment/cases/{plantId}/{diseaseId}: starting values, limits, steps and
    /// target bands of the four controls, in the order the panel shows them.
    /// </summary>
    public sealed class TreatmentProfile
    {
        public const string Temperature = "temperature", Fertiliser = "fertiliser", Spray = "spray", SoilMoisture = "soilMoisture";

        /// <summary>The four controls and their two subfactors, in panel order.</summary>
        public static readonly (string factor, string[] subs)[] Layout =
        {
            (Temperature, new[] { "day", "night" }),
            (Fertiliser, new[] { "dose", "applications" }),
            (Spray, new[] { "dose", "targetCoverage" }),
            (SoilMoisture, new[] { "rootZone", "irrigation" }),
        };

        public string CaseId;
        public string PlantId;
        public string DiseaseId;
        public string Plant;
        public string Disease;
        public string Outcome;
        public string OutcomeLabel;
        public int ProfileVersion;
        public string Scenario;
        public readonly List<string> Scenarios = new List<string>();
        public string Description;
        public string TrainingNote;
        public string OutcomeFeedback;
        public string CareTarget;
        public string Setup;
        public double LeafWetnessStart;
        public readonly List<TreatmentFactor> Factors = new List<TreatmentFactor>();

        public TreatmentFactor Find(string factorId)
        {
            foreach (var factor in Factors)
                if (factor.Id == factorId)
                    return factor;
            return null;
        }

        public TreatmentSubfactor Find(string factorId, string subId) => Find(factorId)?.Find(subId);

        /// <summary>The confirmed deficient nutrient, or null (no supplement for this case).</summary>
        public string Nutrient => Find(Fertiliser)?.Nutrient;

        /// <summary>The fertiliser packet's big label: the confirmed nutrient upper-cased ("POTASSIUM"), or null for no packet.</summary>
        public static string PacketLabel(string nutrient) =>
            string.IsNullOrWhiteSpace(nutrient) ? null : nutrient.Trim().ToUpperInvariant();

        /// <summary>"Severe scenario" for a non-standard scenario; null for the standard one.</summary>
        public static string ScenarioLine(string scenario)
        {
            if (string.IsNullOrEmpty(scenario) || scenario == "standard")
                return null;
            return char.ToUpperInvariant(scenario[0]) + scenario.Substring(1) + " scenario";
        }

        /// <summary>Parses a profile response; null with <paramref name="error"/> when it is malformed or incomplete.</summary>
        public static TreatmentProfile Parse(string json, out string error)
        {
            if (!SimpleJson.TryParse(json, out var value, out error))
                return null;
            var root = SimpleJson.Object(value);
            if (root == null)
            {
                error = "The treatment profile is not a JSON object.";
                return null;
            }

            var profile = new TreatmentProfile
            {
                CaseId = SimpleJson.String(root, "caseId"),
                PlantId = SimpleJson.String(root, "plantId"),
                DiseaseId = SimpleJson.String(root, "diseaseId"),
                Plant = SimpleJson.String(root, "plant"),
                Disease = SimpleJson.String(root, "disease"),
                Outcome = SimpleJson.String(root, "outcome"),
                OutcomeLabel = SimpleJson.String(root, "outcomeLabel"),
                ProfileVersion = (int)Math.Round(SimpleJson.Number(root, "profileVersion", 0d)),
                Scenario = SimpleJson.String(root, "scenario") ?? "standard",
                Description = SimpleJson.String(root, "description"),
                TrainingNote = SimpleJson.String(root, "trainingNote"),
                OutcomeFeedback = SimpleJson.String(root, "outcomeFeedback"),
                CareTarget = SimpleJson.String(root, "careTarget"),
                Setup = SimpleJson.String(root, "setup"),
                LeafWetnessStart = SimpleJson.Number(root, "leafWetnessStart", 0d),
            };
            foreach (var item in SimpleJson.Array(SimpleJson.Get(root, "scenarios")) ?? new List<object>())
                if (item is string scenario)
                    profile.Scenarios.Add(scenario);
            if (profile.Scenarios.Count == 0)
                profile.Scenarios.Add(profile.Scenario);

            foreach (var item in SimpleJson.Array(SimpleJson.Get(root, "factors")) ?? new List<object>())
            {
                var f = SimpleJson.Object(item);
                if (f == null)
                    continue;
                var factor = new TreatmentFactor
                {
                    Id = SimpleJson.String(f, "id"),
                    Label = SimpleJson.String(f, "label"),
                    Nutrient = SimpleJson.String(f, "nutrient"),
                    Evidence = SimpleJson.String(f, "evidence"),
                    Indicated = SimpleJson.Bool(f, "indicated"),
                    Target = SimpleJson.String(f, "target"),
                };
                foreach (var subItem in SimpleJson.Array(SimpleJson.Get(f, "subfactors")) ?? new List<object>())
                {
                    var s = SimpleJson.Object(subItem);
                    if (s == null)
                        continue;
                    var sub = new TreatmentSubfactor
                    {
                        Id = SimpleJson.String(s, "id"),
                        Label = SimpleJson.String(s, "label"),
                        Unit = SimpleJson.String(s, "unit") ?? "",
                    };
                    var options = SimpleJson.Array(SimpleJson.Get(s, "options"));
                    if (options != null || SimpleJson.Get(s, "start") is string)
                    {
                        sub.IsChoice = true;
                        sub.StartChoice = SimpleJson.String(s, "start");
                        foreach (var o in options ?? new List<object>())
                        {
                            var option = SimpleJson.Object(o);
                            if (option != null)
                                sub.Options.Add(new TreatmentChoiceOption { Value = SimpleJson.String(option, "value"), Label = SimpleJson.String(option, "label") });
                        }

                        foreach (var t in SimpleJson.Array(SimpleJson.Get(s, "targets")) ?? new List<object>())
                            if (t is string target)
                                sub.Targets.Add(target);
                    }
                    else
                    {
                        sub.Min = SimpleJson.Number(s, "min");
                        sub.Max = SimpleJson.Number(s, "max");
                        sub.Step = SimpleJson.Number(s, "step", 1d);
                        sub.Start = SimpleJson.Number(s, "start");
                        sub.TargetMin = SimpleJson.Number(s, "targetMin");
                        sub.TargetMax = SimpleJson.Number(s, "targetMax");
                    }

                    factor.Subfactors.Add(sub);
                }

                profile.Factors.Add(factor);
            }

            var problems = profile.Validate();
            if (problems.Count > 0)
            {
                error = "The treatment profile is incomplete: " + string.Join("; ", problems);
                return null;
            }

            error = null;
            return profile;
        }

        /// <summary>
        /// Problems with the profile's shape: exactly the four controls in order (temperature day/night, fertiliser
        /// dose/applications, spray dose/targetCoverage, soil moisture rootZone/irrigation), sane numeric ranges and a
        /// water-delivery choice whose start is one of its options. Empty when the profile can drive the panel.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrEmpty(PlantId) || string.IsNullOrEmpty(DiseaseId))
                problems.Add("no plant or disease ID");
            if (ProfileVersion < 1)
                problems.Add("no profile version");
            if (Factors.Count != Layout.Length)
                problems.Add($"{Factors.Count} controls instead of {Layout.Length}");
            for (var i = 0; i < Layout.Length && i < Factors.Count; i++)
            {
                var (factorId, subs) = Layout[i];
                var factor = Factors[i];
                if (factor.Id != factorId)
                {
                    problems.Add($"control {i + 1} is '{factor.Id}', expected '{factorId}'");
                    continue;
                }

                if (factor.Subfactors.Count != subs.Length)
                {
                    problems.Add($"{factorId} has {factor.Subfactors.Count} settings instead of {subs.Length}");
                    continue;
                }

                for (var j = 0; j < subs.Length; j++)
                {
                    var sub = factor.Subfactors[j];
                    if (sub.Id != subs[j])
                    {
                        problems.Add($"{factorId} setting {j + 1} is '{sub.Id}', expected '{subs[j]}'");
                        continue;
                    }

                    var mustBeChoice = factorId == SoilMoisture && sub.Id == "irrigation";
                    if (sub.IsChoice != mustBeChoice)
                        problems.Add($"{factorId}.{sub.Id} should {(mustBeChoice ? "" : "not ")}be a choice");
                    else if (sub.IsChoice)
                    {
                        if (sub.Options.Count == 0 || sub.IndexOfOption(sub.StartChoice) < 0)
                            problems.Add($"{factorId}.{sub.Id} start '{sub.StartChoice}' is not one of its options");
                    }
                    else if (!(sub.Step > 0d) || sub.Max < sub.Min || sub.Start < sub.Min - 1e-9 || sub.Start > sub.Max + 1e-9)
                        problems.Add($"{factorId}.{sub.Id} has an invalid range");
                }
            }

            return problems;
        }
    }
}
