using System;
using System.Collections.Generic;

namespace BTP.Treatment
{
    /// <summary>
    /// The manual-treatment data of one case, read from the content database tables case_profile,
    /// treatment_controls and control_rules. Every number is an abstract teaching value authored in SQL
    /// (no products or doses); nothing disease-specific lives in C#.
    /// </summary>
    public sealed class ManualTreatmentProfile
    {
        public CaseKey Key;
        /// <summary>One-sentence description of the specimen's current severity.</summary>
        public string Description;
        public readonly List<string> Symptoms = new List<string>();
        /// <summary>Simulated share of the leaf affected at the start (0..1).</summary>
        public float InitialSeverity;
        /// <summary>Simulated leaf health at the start (0..1).</summary>
        public float InitialHealth;
        /// <summary>Largest fraction of the initial severity that a perfect assessment removes.</summary>
        public float MaxSeverityDrop;
        /// <summary>Largest absolute health gain of a perfect assessment.</summary>
        public float MaxHealthGain;
        /// <summary>Largest absolute worsening after a poor or invalid treatment.</summary>
        public float MaxWorsen;
        // Visual caps for the leaf shader (0..1), scaled by the validated assessment.
        public float MaxSpotFade;
        public float MaxYellowFade;
        public float MaxGreenGain;
        public float MaxVeinGain;
        public string RecoveryNote;

        public readonly List<TreatmentControlDef> Controls = new List<TreatmentControlDef>();
        public readonly List<TreatmentControlRule> Rules = new List<TreatmentControlRule>();

        public TreatmentControlDef FindControl(string controlId)
        {
            foreach (var control in Controls)
                if (control.ControlId == controlId)
                    return control;
            return null;
        }
    }

    /// <summary>One adjustable treatment level (an abstract 0-10 scale) derived from a methods row.</summary>
    public sealed class TreatmentControlDef
    {
        public string ControlId;
        public int Order;
        public string MethodId;
        public string Name;
        public string Description;
        public float Min;
        public float Max;
        public float Step;
        public float Start;
        public float RecommendedMin;
        public float RecommendedMax;
        public float Weight = 1f;
        public bool Required = true;
        public string FeedbackLow;
        public string FeedbackHigh;
        public string FeedbackOk;

        /// <summary>Number of steps from <see cref="Min"/> to <paramref name="value"/>; used for exact grid snapping.</summary>
        public int StepIndex(float value) => (int)Math.Round((value - Min) / Step);

        public float ValueAt(int stepIndex) => (float)Math.Round(Min + stepIndex * Step, 4);

        public int StepCount => StepIndex(Max);
    }

    /// <summary>
    /// An invalid or harmful combination: applies when control A is inside [AMin, AMax] and, if set, control B
    /// is inside [BMin, BMax]. A blocking rule means the treatment cannot help at all (e.g. an infected plant
    /// that is not contained).
    /// </summary>
    public sealed class TreatmentControlRule
    {
        public string RuleId;
        public string ControlA;
        public float AMin;
        public float AMax;
        public string ControlB;
        public float BMin;
        public float BMax;
        public float Penalty;
        public bool Blocks;
        public string Feedback;

        public bool Matches(Func<string, float?> valueOf)
        {
            var a = valueOf(ControlA);
            if (a == null || a.Value < AMin - 1e-4f || a.Value > AMax + 1e-4f)
                return false;
            if (string.IsNullOrEmpty(ControlB))
                return true;
            var b = valueOf(ControlB);
            return b != null && b.Value >= BMin - 1e-4f && b.Value <= BMax + 1e-4f;
        }
    }
}
