using System;
using System.Collections.Generic;

namespace BTP.Treatment
{
    /// <summary>The value of one treatment control and whether the learner has set it.</summary>
    public struct ControlSelection
    {
        public float Value;
        /// <summary>False until the learner presses − or + at least once. Untouched controls never count as chosen.</summary>
        public bool Touched;
    }

    /// <summary>Result of a − / + press.</summary>
    public readonly struct AdjustResult
    {
        public readonly bool Accepted;
        public readonly float Value;
        public readonly string Message;

        public AdjustResult(bool accepted, float value, string message)
        {
            Accepted = accepted;
            Value = value;
            Message = message;
        }
    }

    /// <summary>
    /// The − / + button logic for the manual treatment controls. Each press moves exactly one authored step,
    /// clamped to the control's min and max. Controls start at their authored start value (below the
    /// recommendation) and are marked untouched, so nothing is ever auto-selected for the learner.
    /// Pure C#: no Unity objects, so EditMode tests cover it directly.
    /// </summary>
    public sealed class TreatmentButtonController
    {
        readonly ManualTreatmentProfile profile;
        readonly Dictionary<string, ControlSelection> values = new Dictionary<string, ControlSelection>();

        public TreatmentButtonController(ManualTreatmentProfile profile)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Reset();
        }

        public ManualTreatmentProfile Profile => profile;

        public IReadOnlyDictionary<string, ControlSelection> Values => values;

        /// <summary>Every control back to its start value, untouched.</summary>
        public void Reset()
        {
            values.Clear();
            foreach (var control in profile.Controls)
                values[control.ControlId] = new ControlSelection { Value = Snap(control, control.Start), Touched = false };
        }

        public float ValueOf(string controlId) => values.TryGetValue(controlId, out var s) ? s.Value : float.NaN;

        public bool IsTouched(string controlId) => values.TryGetValue(controlId, out var s) && s.Touched;

        /// <summary>Required controls the learner has not set yet, in display order.</summary>
        public List<TreatmentControlDef> UntouchedRequired()
        {
            var list = new List<TreatmentControlDef>();
            foreach (var control in profile.Controls)
                if (control.Required && !IsTouched(control.ControlId))
                    list.Add(control);
            return list;
        }

        public AdjustResult Increase(string controlId) => Adjust(controlId, +1);

        public AdjustResult Decrease(string controlId) => Adjust(controlId, -1);

        /// <summary>Moves a control by <paramref name="direction"/> steps (sign only), clamped to its limits.</summary>
        public AdjustResult Adjust(string controlId, int direction)
        {
            var control = profile.FindControl(controlId);
            if (control == null)
                return new AdjustResult(false, float.NaN, "That treatment control is not part of this case.");
            var current = values[controlId];
            if (direction == 0)
                return new AdjustResult(false, current.Value, "No change.");
            var index = control.StepIndex(current.Value) + Math.Sign(direction);
            var clamped = Math.Max(0, Math.Min(control.StepCount, index));
            var next = control.ValueAt(clamped);
            // Pressing at a limit still counts as a deliberate choice (the learner chose the extreme), but says so.
            values[controlId] = new ControlSelection { Value = next, Touched = true };
            if (clamped != index)
                return new AdjustResult(true, next, direction > 0 ? $"{control.Name} is already at its maximum ({Format(control.Max)})." :
                    $"{control.Name} is already at its minimum ({Format(control.Min)}).");
            return new AdjustResult(true, next, $"{control.Name}: {Format(next)}");
        }

        /// <summary>Restores a saved selection (values are re-snapped and clamped, so edited content stays safe).</summary>
        public void Restore(string controlId, float value, bool touched)
        {
            var control = profile.FindControl(controlId);
            if (control == null)
                return;
            values[controlId] = new ControlSelection { Value = Snap(control, value), Touched = touched };
        }

        static float Snap(TreatmentControlDef control, float value)
        {
            if (!(control.Step > 0f))
                return Math.Max(control.Min, Math.Min(control.Max, value));
            var index = Math.Max(0, Math.Min(control.StepCount, control.StepIndex(value)));
            return control.ValueAt(index);
        }

        public static string Format(float value) =>
            Math.Abs(value - Math.Round(value)) < 1e-3 ? ((int)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}
