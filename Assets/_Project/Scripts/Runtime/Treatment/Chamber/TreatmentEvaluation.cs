using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTP.Treatment
{
    /// <summary>The server's verdict on one applied setting.</summary>
    public sealed class TreatmentEvaluationRow
    {
        public string FactorId;
        public string SubId;
        /// <summary>The numeric value the server scored (NaN for the water-delivery choice).</summary>
        public double Value = double.NaN;
        /// <summary>The choice value the server scored (water delivery), else null.</summary>
        public string Choice;
        public double Score;
        public bool OnTarget;
        public string Feedback;
    }

    /// <summary>
    /// The response of POST /api/treatment/cases/{plantId}/{diseaseId}/evaluate: overall score and outcome, the
    /// measured root-zone moisture and leaf wetness, and per-setting feedback.
    /// </summary>
    public sealed class TreatmentEvaluation
    {
        public string CaseId;
        public int ProfileVersion;
        public string Scenario;
        public double Score;
        public bool Passed;
        public string Outcome;
        public string OutcomeLabel;
        public string TargetOutcome;
        public string CareTarget;
        public double RootZoneMoisture;
        public double LeafWetness;
        public string Feedback;
        public readonly Dictionary<string, double> FactorScores = new Dictionary<string, double>();
        public readonly List<TreatmentEvaluationRow> Rows = new List<TreatmentEvaluationRow>();

        public TreatmentEvaluationRow Find(string factorId, string subId)
        {
            foreach (var row in Rows)
                if (row.FactorId == factorId && row.SubId == subId)
                    return row;
            return null;
        }

        public double FactorScore(string factorId) => FactorScores.TryGetValue(factorId, out var score) ? score : double.NaN;

        /// <summary>"Managed" when passed, else "Not yet on target".</summary>
        public string Headline => Passed && !string.IsNullOrEmpty(OutcomeLabel) ? OutcomeLabel : "Not yet on target";

        public string ScorePercent => Math.Round(Score * 100d).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static TreatmentEvaluation Parse(string json, out string error)
        {
            if (!SimpleJson.TryParse(json, out var value, out error))
                return null;
            var root = SimpleJson.Object(value);
            if (root == null || !SimpleJson.TryNumber(root, "score", out var score))
            {
                error = "The evaluation response has no score.";
                return null;
            }

            var result = new TreatmentEvaluation
            {
                CaseId = SimpleJson.String(root, "caseId"),
                ProfileVersion = (int)Math.Round(SimpleJson.Number(root, "profileVersion")),
                Scenario = SimpleJson.String(root, "scenario"),
                Score = score,
                Passed = SimpleJson.Bool(root, "passed"),
                Outcome = SimpleJson.String(root, "outcome"),
                OutcomeLabel = SimpleJson.String(root, "outcomeLabel"),
                TargetOutcome = SimpleJson.String(root, "targetOutcome"),
                CareTarget = SimpleJson.String(root, "careTarget"),
                RootZoneMoisture = SimpleJson.Number(root, "rootZoneMoisture", double.NaN),
                LeafWetness = SimpleJson.Number(root, "leafWetness", double.NaN),
                Feedback = SimpleJson.String(root, "feedback"),
            };
            foreach (var item in SimpleJson.Array(SimpleJson.Get(root, "factors")) ?? new List<object>())
            {
                var f = SimpleJson.Object(item);
                var factorId = SimpleJson.String(f, "id");
                if (factorId == null)
                    continue;
                result.FactorScores[factorId] = SimpleJson.Number(f, "score");
                foreach (var subItem in SimpleJson.Array(SimpleJson.Get(f, "subfactors")) ?? new List<object>())
                {
                    var s = SimpleJson.Object(subItem);
                    if (s == null)
                        continue;
                    var raw = SimpleJson.Get(s, "value");
                    result.Rows.Add(new TreatmentEvaluationRow
                    {
                        FactorId = factorId,
                        SubId = SimpleJson.String(s, "id"),
                        Value = raw is double d ? d : double.NaN,
                        Choice = raw as string,
                        Score = SimpleJson.Number(s, "score"),
                        OnTarget = SimpleJson.Bool(s, "onTarget"),
                        Feedback = SimpleJson.String(s, "feedback"),
                    });
                }
            }

            error = null;
            return result;
        }
    }
}
