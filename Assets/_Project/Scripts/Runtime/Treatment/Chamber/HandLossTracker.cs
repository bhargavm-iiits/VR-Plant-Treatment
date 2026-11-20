using System;

namespace BTP.Treatment
{
    /// <summary>
    /// Hand tracking state for the treatment tools (pure; unit tested). A hand that was tracked and stops being tracked raises
    /// <see cref="HandLost"/> on that update (tools stop and return to their docks the same frame). When both hands stay
    /// untracked for more than <see cref="MessageDelay"/> seconds after hands were in use, <see cref="ShowMessage"/> asks for
    /// the short "Hands out of view" notice; it clears as soon as a hand is tracked again. Before any hand was ever tracked
    /// (controllers, desktop, simulator) nothing is reported.
    /// </summary>
    public sealed class HandLossTracker
    {
        public const float MessageDelay = 1f;
        public const string Message = "Hands out of view";

        readonly bool[] tracked = new bool[2];
        bool seen;
        float lostFor;

        /// <summary>0 = left, 1 = right.</summary>
        public event Action<int> HandLost;

        public bool ShowMessage { get; private set; }
        public bool Left => tracked[0];
        public bool Right => tracked[1];

        public void Update(float deltaTime, bool left, bool right)
        {
            var now = new[] { left, right };
            for (var i = 0; i < 2; i++)
            {
                if (tracked[i] && !now[i])
                    HandLost?.Invoke(i);
                tracked[i] = now[i];
            }

            if (left || right)
            {
                seen = true;
                lostFor = 0f;
                ShowMessage = false;
                return;
            }

            if (!seen)
                return;
            lostFor += deltaTime;
            ShowMessage = lostFor > MessageDelay;
        }

        public static string HandName(int hand) => hand == 0 ? "left" : "right";
    }
}
