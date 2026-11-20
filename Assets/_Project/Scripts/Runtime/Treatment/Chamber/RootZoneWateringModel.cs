using System;

namespace BTP.Treatment
{
    /// <summary>
    /// The WATER station's root-zone moisture (pure; unit tested). "Water to add" is in percentage points of the profile's
    /// step and may be negative (drain: most cases start too wet). The setpoint is the current moisture plus the amount,
    /// clamped to the profile range, and is recomputed from the current moisture whenever the amount changes while the
    /// water is off. ON starts or resumes moving the moisture towards the setpoint at a fixed rate and stops by itself
    /// exactly at it; the amount then resets to 0, so ON again adds nothing. OFF stops at once and keeps what was added.
    /// Check Health reports the moisture reached (rounded to the step) with "rootZone" delivery once the outlet was used,
    /// else "hold" (and then the moisture is still the start value, as the server's hold rule requires).
    /// </summary>
    public sealed class RootZoneWateringModel
    {
        public const double DefaultRate = 4d;
        const double Epsilon = 1e-6;

        public enum State
        {
            /// <summary>No amount chosen (or it was used up): ON does nothing.</summary>
            ChooseAmount,
            /// <summary>An amount is chosen and ON has not been pressed yet.</summary>
            Ready,
            Watering,
            Draining,
            /// <summary>OFF stopped the water before the setpoint.</summary>
            Paused,
            ReachedOnTarget,
            ReachedOutsideTarget,
        }

        public readonly double Start, Min, Max, Step, TargetMin, TargetMax, Rate;

        public double Current { get; private set; }
        public double Setpoint { get; private set; }
        /// <summary>"Water to add" in percentage points (negative drains).</summary>
        public double Amount { get; private set; }
        public bool Running { get; private set; }
        /// <summary>True once water was added or drained through the outlet.</summary>
        public bool Delivered { get; private set; }
        /// <summary>Total percentage points moved through the outlet (both directions), for the wet patch.</summary>
        public double Moved { get; private set; }
        bool paused;
        bool reached;

        public RootZoneWateringModel(double start, double min, double max, double step, double targetMin, double targetMax, double rate = DefaultRate)
        {
            Min = min;
            Max = Math.Max(min, max);
            Step = step > 0d ? step : 1d;
            Start = Clamp(start);
            TargetMin = targetMin;
            TargetMax = targetMax;
            Rate = rate > 0d ? rate : DefaultRate;
            Current = Setpoint = Start;
        }

        /// <summary>The model for a profile's root-zone subfactor; null when the profile has none.</summary>
        public static RootZoneWateringModel For(TreatmentProfile profile, double rate = DefaultRate)
        {
            var sub = profile?.Find(TreatmentProfile.SoilMoisture, "rootZone");
            return sub == null ? null : new RootZoneWateringModel(sub.Start, sub.Min, sub.Max, sub.Step, sub.TargetMin, sub.TargetMax, rate);
        }

        double Clamp(double value) => Math.Max(Min, Math.Min(Max, value));

        public bool Draining => Running && Setpoint < Current - Epsilon;
        public bool Watering => Running && Setpoint > Current + Epsilon;
        public bool InTarget => Current >= TargetMin - Epsilon && Current <= TargetMax + Epsilon;

        /// <summary>The smallest and largest amount allowed now (the range kept inside [min, max]).</summary>
        public double MinAmount => Min - Current;
        public double MaxAmount => Max - Current;

        public State Status
        {
            get
            {
                if (Running)
                    return Setpoint < Current ? State.Draining : State.Watering;
                if (paused && Math.Abs(Setpoint - Current) > Epsilon)
                    return State.Paused;
                if (Math.Abs(Amount) > Epsilon && Math.Abs(Setpoint - Current) > Epsilon)
                    return State.Ready;
                if (reached)
                    return InTarget ? State.ReachedOnTarget : State.ReachedOutsideTarget;
                return State.ChooseAmount;
            }
        }

        /// <summary>Changes "Water to add" by one profile step (refused while the water runs).</summary>
        public bool StepAmount(int direction) => direction != 0 && SetAmount(Amount + Math.Sign(direction) * Step);

        /// <summary>Sets "Water to add" (snapped to the step, kept in range); the setpoint follows from the current moisture.</summary>
        public bool SetAmount(double amount)
        {
            if (Running)
                return false;
            var snapped = Math.Round(amount / Step) * Step;
            snapped = Math.Max(MinAmount, Math.Min(MaxAmount, snapped));
            snapped = Math.Round(snapped, 6);
            if (Math.Abs(snapped - Amount) < Epsilon && Math.Abs(Setpoint - Clamp(Current + snapped)) < Epsilon)
                return false;
            Amount = snapped;
            Setpoint = Clamp(Current + Amount);
            reached = false;
            return true;
        }

        /// <summary>ON: starts or resumes towards the setpoint. False when there is nothing to add (setpoint reached).</summary>
        public bool On()
        {
            if (Running)
                return false;
            if (Math.Abs(Setpoint - Current) < Epsilon)
            {
                reached = false;
                return false;
            }

            Running = true;
            paused = false;
            Delivered = true;
            return true;
        }

        /// <summary>OFF: stops at once; the moisture already added stays.</summary>
        public void Off()
        {
            if (!Running)
                return;
            Running = false;
            paused = Math.Abs(Setpoint - Current) > Epsilon;
        }

        /// <summary>Moves the moisture towards the setpoint; returns true on the tick it reaches it (auto-stop).</summary>
        public bool Tick(double seconds)
        {
            if (!Running || seconds <= 0d)
                return false;
            var step = Rate * seconds;
            var before = Current;
            var remaining = Setpoint - Current;
            if (Math.Abs(remaining) <= step + Epsilon)
            {
                Current = Setpoint;
                Moved += Math.Abs(Current - before);
                Running = false;
                paused = false;
                reached = true;
                Amount = 0d;
                return true;
            }

            Current += Math.Sign(remaining) * step;
            Moved += step;
            return false;
        }

        /// <summary>The moisture Check Health sends: rounded to the profile step grid.</summary>
        public double MeasuredForServer => Clamp(Min + Math.Round((Current - Min) / Step) * Step);

        /// <summary>The water delivery Check Health sends.</summary>
        public string Irrigation => Delivered ? SoilMoistureRules.RootZone : SoilMoistureRules.Hold;

        /// <summary>Restores a saved state (returning to a specimen).</summary>
        public void Restore(double current, double setpoint, double amount, bool delivered, double moved)
        {
            Running = false;
            Current = Clamp(current);
            Setpoint = Clamp(setpoint);
            Amount = Math.Max(Min - Current, Math.Min(Max - Current, amount));
            Delivered = delivered;
            Moved = Math.Max(0d, moved);
            paused = Math.Abs(Setpoint - Current) > Epsilon && Math.Abs(Amount) > Epsilon;
            reached = !paused && delivered;
        }

        /// <summary>The page status line: "Watering", "Draining", "Paused at 62%", "Setpoint reached - on target", ...</summary>
        public string StatusText(Func<double, string> format)
        {
            switch (Status)
            {
                case State.Watering: return "Watering";
                case State.Draining: return "Draining";
                case State.Paused: return "Paused at " + format(Current);
                case State.Ready: return Amount < 0d ? "Press ON to drain" : "Press ON to water";
                case State.ReachedOnTarget: return "Setpoint reached - on target";
                case State.ReachedOutsideTarget: return "Setpoint reached - outside target";
                default: return "Choose water to add";
            }
        }

        /// <summary>"20%" or, for a negative amount, "Drain 15%".</summary>
        public static string AmountText(double amount, Func<double, string> format) =>
            amount < -1e-9 ? "Drain " + format(-amount) : format(amount);
    }
}
