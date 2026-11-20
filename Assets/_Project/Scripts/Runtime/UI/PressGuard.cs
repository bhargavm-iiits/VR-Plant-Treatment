using System.Collections.Generic;

namespace BTP.UI
{
    /// <summary>
    /// One gesture presses a button once (pure; unit tested). With hand tracking a poke and the pinch ray can both reach the
    /// same button in one gesture; the second activation of the same button within <see cref="Window"/> seconds is ignored.
    /// Different buttons are independent.
    /// </summary>
    public sealed class PressGuard
    {
        public const float DefaultWindow = 0.25f;

        readonly Dictionary<object, float> last = new Dictionary<object, float>();

        public float Window { get; }

        public PressGuard(float window = DefaultWindow) => Window = window;

        /// <summary>True when <paramref name="button"/> may act at <paramref name="time"/> (seconds); records the press.</summary>
        public bool TryPress(object button, float time)
        {
            if (button == null)
                return false;
            if (last.TryGetValue(button, out var previous) && time - previous < Window && time >= previous)
                return false;
            last[button] = time;
            return true;
        }

        /// <summary>Lets the next press of <paramref name="button"/> through (press-and-hold auto-repeat).</summary>
        public void Forget(object button)
        {
            if (button != null)
                last.Remove(button);
        }

        /// <summary>The project-wide guard for world UI (touchscreen, WATER console).</summary>
        public static readonly PressGuard Shared = new PressGuard();
    }
}
