using System;
using System.Collections.Generic;

namespace BTP.XR
{
    /// <summary>
    /// Remembers which launch jobs this player has started, so a replayed "vr.launch" event, a reconnect that lists the
    /// same pending job, or a retried web click never moves the user twice. Bounded, and serialisable to one setting so
    /// it survives a restart.
    /// </summary>
    public sealed class XrLaunchDeduper
    {
        public const int Capacity = 64;
        readonly LinkedList<string> order = new LinkedList<string>();
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        public int Count => seen.Count;

        /// <summary>True the first time a launch ID is seen; false for a duplicate or an empty ID.</summary>
        public bool TryBegin(string launchId)
        {
            if (string.IsNullOrEmpty(launchId) || seen.Contains(launchId))
                return false;
            seen.Add(launchId);
            order.AddLast(launchId);
            while (order.Count > Capacity)
            {
                seen.Remove(order.First.Value);
                order.RemoveFirst();
            }

            return true;
        }

        public bool Contains(string launchId) => !string.IsNullOrEmpty(launchId) && seen.Contains(launchId);

        /// <summary>Forgets a launch that could not even start (for example it arrived before the app could read it), so it can be retried.</summary>
        public void Forget(string launchId)
        {
            if (string.IsNullOrEmpty(launchId) || !seen.Remove(launchId))
                return;
            order.Remove(launchId);
        }

        public string Serialize() => string.Join("\n", order);

        public static XrLaunchDeduper Deserialize(string text)
        {
            var deduper = new XrLaunchDeduper();
            if (string.IsNullOrEmpty(text))
                return deduper;
            foreach (var id in text.Split('\n'))
                deduper.TryBegin(id.Trim());
            return deduper;
        }
    }

    /// <summary>Reconnect delays: exponential from <see cref="Base"/> to <see cref="Max"/> seconds, with up to 20% jitter.</summary>
    public static class XrBackoff
    {
        public const double Base = 1.0;
        public const double Max = 30.0;

        /// <param name="attempt">0 for the first retry.</param>
        /// <param name="jitter01">A random value in [0, 1).</param>
        public static TimeSpan Delay(int attempt, double jitter01)
        {
            var exponent = Math.Min(Math.Max(attempt, 0), 10);
            var seconds = Math.Min(Max, Base * Math.Pow(2, exponent));
            seconds *= 1.0 + 0.2 * Math.Max(0.0, Math.Min(1.0, jitter01));
            return TimeSpan.FromSeconds(Math.Min(seconds, Max * 1.2));
        }
    }
}
