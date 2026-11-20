namespace BTP.Treatment
{
    /// <summary>
    /// Counts discrete doses (scoops that land in the root zone, sprays that reach the plant) against a target (pure; unit
    /// tested). A miss never counts. Reaching the target starts a short confirmation window
    /// (<see cref="ConfirmSeconds"/>): one more dose inside it is an excess, so the step does not succeed and must be reset
    /// (<see cref="Reset"/>); otherwise <see cref="Tick"/> reports the step complete once.
    /// </summary>
    public sealed class DoseCounter
    {
        public enum Result
        {
            /// <summary>The dose did not reach the target area: not counted.</summary>
            Missed,
            /// <summary>Counted; more are needed.</summary>
            Counted,
            /// <summary>Counted and the target is reached (confirmation pending).</summary>
            Reached,
            /// <summary>More than the target: the step needs a reset.</summary>
            Excess,
            /// <summary>Ignored (already complete or excess).</summary>
            Ignored,
        }

        public const float DefaultConfirmSeconds = 1.2f;

        public int Target { get; private set; }
        public int Count { get; private set; }
        public int Misses { get; private set; }
        public bool Excess => Count > Target;
        public bool Complete { get; private set; }
        public float ConfirmSeconds { get; set; } = DefaultConfirmSeconds;
        float held;

        public DoseCounter(int target) => Target = target < 1 ? 1 : target;

        /// <summary>One dose: <paramref name="hit"/> when it reached the target area.</summary>
        public Result Register(bool hit)
        {
            if (Complete)
                return Result.Ignored;
            if (!hit)
            {
                Misses++;
                return Result.Missed;
            }

            if (Excess)
                return Result.Ignored;
            Count++;
            held = 0f;
            if (Count > Target)
                return Result.Excess;
            return Count == Target ? Result.Reached : Result.Counted;
        }

        /// <summary>Advances the confirmation window; true exactly once when the target held without an excess.</summary>
        public bool Tick(float deltaTime)
        {
            if (Complete || Count != Target)
                return false;
            held += deltaTime;
            if (held < ConfirmSeconds)
                return false;
            Complete = true;
            return true;
        }

        /// <summary>Reset step: no doses counted.</summary>
        public void Reset()
        {
            Count = 0;
            Misses = 0;
            held = 0f;
            Complete = false;
        }

        /// <summary>"NPK 19-19-19 · 2/3 scoops".</summary>
        public string Progress(string prefix, string noun) =>
            (string.IsNullOrEmpty(prefix) ? "" : prefix + " · ") + $"{Count}/{Target} {noun}{(Target == 1 ? "" : "s")}";
    }
}
