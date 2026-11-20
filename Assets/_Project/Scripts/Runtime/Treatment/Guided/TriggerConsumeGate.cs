using System.Collections.Generic;

namespace BTP.Treatment
{
    /// <summary>
    /// The trigger-consume rule of the instruction popup (pure; unit tested). While a popup is up (<see cref="Modal"/>) no
    /// tool use or Apply is allowed from any source. The press that dismisses it (OK) is consumed for every source that is
    /// still holding its trigger at that moment (<see cref="ConsumeHeld"/>): that source stays blocked until its trigger is
    /// released (<see cref="Observe"/> with pressed = false); a new press after the release works normally. Sources are
    /// keyed by name ("left", "right", "mouse", "hand-left", ...).
    /// </summary>
    public sealed class TriggerConsumeGate
    {
        readonly HashSet<string> consumed = new HashSet<string>();
        readonly Dictionary<string, bool> held = new Dictionary<string, bool>();

        /// <summary>True while a popup that blocks tool use is showing.</summary>
        public bool Modal { get; set; }

        /// <summary>Records whether <paramref name="source"/>'s trigger is down now; a release ends its consumed press.</summary>
        public void Observe(string source, bool pressed)
        {
            if (string.IsNullOrEmpty(source))
                return;
            held[source] = pressed;
            if (!pressed)
                consumed.Remove(source);
        }

        /// <summary>The popup's OK was pressed: every source whose trigger is down now has that press consumed.</summary>
        public void ConsumeHeld()
        {
            foreach (var pair in held)
                if (pair.Value)
                    consumed.Add(pair.Key);
        }

        /// <summary>Consumes <paramref name="source"/>'s current press (the OK came from it), whether or not it was observed.</summary>
        public void Consume(string source)
        {
            if (string.IsNullOrEmpty(source))
                return;
            consumed.Add(source);
            held[source] = true;
        }

        /// <summary>Whether a tool use / Apply from <paramref name="source"/> may happen now.</summary>
        public bool Allows(string source) => !Modal && (string.IsNullOrEmpty(source) || !consumed.Contains(source));

        public bool IsConsumed(string source) => !string.IsNullOrEmpty(source) && consumed.Contains(source);

        public void Clear()
        {
            consumed.Clear();
            held.Clear();
            Modal = false;
        }
    }
}
