using System;

namespace BTP.Treatment
{
    /// <summary>
    /// The simulated state of the specimen leaf: health and active severity for the panels, and the shader
    /// amounts for the same leaf mesh (spots drying, yellowing fading, green tissue and veins returning, or
    /// worsening). All values are 0..1.
    /// </summary>
    public struct LeafRecoveryState : IEquatable<LeafRecoveryState>
    {
        public float Health;
        public float Severity;
        public float SpotFade;
        public float YellowFade;
        public float GreenGain;
        public float VeinGain;
        public float Worsen;

        public static LeafRecoveryState Initial(ManualTreatmentProfile profile) =>
            new LeafRecoveryState { Health = profile.InitialHealth, Severity = profile.InitialSeverity };

        public static LeafRecoveryState Lerp(LeafRecoveryState a, LeafRecoveryState b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            float L(float x, float y) => x + (y - x) * t;
            return new LeafRecoveryState
            {
                Health = L(a.Health, b.Health), Severity = L(a.Severity, b.Severity), SpotFade = L(a.SpotFade, b.SpotFade),
                YellowFade = L(a.YellowFade, b.YellowFade), GreenGain = L(a.GreenGain, b.GreenGain), VeinGain = L(a.VeinGain, b.VeinGain),
                Worsen = L(a.Worsen, b.Worsen),
            };
        }

        public bool Equals(LeafRecoveryState o) =>
            Math.Abs(Health - o.Health) < 1e-5f && Math.Abs(Severity - o.Severity) < 1e-5f && Math.Abs(SpotFade - o.SpotFade) < 1e-5f &&
            Math.Abs(YellowFade - o.YellowFade) < 1e-5f && Math.Abs(GreenGain - o.GreenGain) < 1e-5f && Math.Abs(VeinGain - o.VeinGain) < 1e-5f &&
            Math.Abs(Worsen - o.Worsen) < 1e-5f;

        public override bool Equals(object obj) => obj is LeafRecoveryState other && Equals(other);
        public override int GetHashCode() => Health.GetHashCode() ^ (Severity.GetHashCode() * 397);
    }

    /// <summary>
    /// Outcome-aware mapping from a validated assessment to the leaf state. Improvement is driven only by the
    /// assessment score and scaled by the per-case caps stored in SQL:
    /// Managed — yellowing fades, green tissue and veins return, spots dry and stop spreading but never vanish;
    /// PestSuppressed — speckling stops spreading, moderate improvement;
    /// Replaced — treatment cannot save the leaf: it never improves, and only stops worsening when contained.
    /// Weak or invalid treatments give little or no improvement, or worsening.
    /// </summary>
    public static class LeafRecoveryModel
    {
        /// <summary>Scores at or below this give no improvement; below it the leaf worsens.</summary>
        public const float ImprovementThreshold = 0.35f;

        /// <summary>0..1 improvement factor of an assessment (0 for invalid treatments and every Replaced case).</summary>
        public static float Improvement(TreatmentOutcome outcome, bool valid, float score)
        {
            if (!valid || outcome == TreatmentOutcome.Replaced)
                return 0f;
            return Clamp01((score - ImprovementThreshold) / (1f - ImprovementThreshold));
        }

        /// <summary>0..1 worsening factor: full for a blocked (e.g. uncontained) treatment, rising as the score falls below the threshold.</summary>
        public static float Worsening(bool valid, float score) => !valid ? 1f : Clamp01((ImprovementThreshold - score) / ImprovementThreshold);

        public static LeafRecoveryState Evaluate(ManualTreatmentProfile profile, TreatmentOutcome outcome, TreatmentAssessment assessment) =>
            assessment == null ? LeafRecoveryState.Initial(profile) : Evaluate(profile, outcome, assessment.Valid, assessment.Score);

        /// <summary>The leaf state after applying a treatment that scored <paramref name="score"/>.</summary>
        public static LeafRecoveryState Evaluate(ManualTreatmentProfile profile, TreatmentOutcome outcome, bool valid, float score)
        {
            var state = LeafRecoveryState.Initial(profile);
            var improvement = Improvement(outcome, valid, score);
            var worsen = Worsening(valid, score) * profile.MaxWorsen;
            var replaced = outcome == TreatmentOutcome.Replaced;

            state.Severity = Clamp01(profile.InitialSeverity * (1f - improvement * (replaced ? 0f : profile.MaxSeverityDrop)) + worsen);
            state.Health = Clamp01(profile.InitialHealth + improvement * (replaced ? 0f : profile.MaxHealthGain) - worsen);
            if (!replaced)
            {
                state.SpotFade = improvement * Math.Min(profile.MaxSpotFade, 0.8f);
                state.YellowFade = improvement * profile.MaxYellowFade;
                state.GreenGain = improvement * profile.MaxGreenGain;
                state.VeinGain = improvement * profile.MaxVeinGain;
            }

            state.Worsen = profile.MaxWorsen > 0f ? Clamp01(worsen / 0.15f) : 0f;
            return state;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>
    /// Smoothly moves the displayed leaf state from where it is to a new target over a fixed duration (SmootherStep).
    /// Time only animates towards a target set by a validated Apply; it never sets a target itself, so the session
    /// timer can never cure the leaf.
    /// </summary>
    public sealed class LeafHealthMonitor
    {
        LeafRecoveryState from;
        LeafRecoveryState to;
        float elapsed;
        float duration;

        public LeafHealthMonitor(LeafRecoveryState initial)
        {
            from = to = Current = initial;
        }

        public LeafRecoveryState Current { get; private set; }
        public LeafRecoveryState Target => to;
        public bool IsTransitioning => elapsed < duration;
        /// <summary>True while moving towards a healthier state.</summary>
        public bool IsImproving => IsTransitioning && to.Health > from.Health + 1e-4f;
        public float Progress => duration > 0f ? Math.Min(1f, elapsed / duration) : 1f;

        public void SetTarget(LeafRecoveryState target, float seconds)
        {
            from = Current;
            to = target;
            elapsed = 0f;
            duration = Math.Max(0f, seconds);
            if (duration <= 0f)
                Current = from = to;
        }

        /// <summary>Jumps to a state without animation (restoring a saved session).</summary>
        public void Snap(LeafRecoveryState state)
        {
            from = to = Current = state;
            elapsed = duration = 0f;
        }

        public void Advance(float seconds)
        {
            if (!IsTransitioning)
                return;
            elapsed = Math.Min(duration, elapsed + Math.Max(0f, seconds));
            var t = Progress;
            Current = LeafRecoveryState.Lerp(from, to, t * t * t * (t * (t * 6f - 15f) + 10f));
            if (!IsTransitioning)
                Current = from = to;
        }
    }
}
