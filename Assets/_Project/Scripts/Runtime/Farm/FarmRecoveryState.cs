using System;
using System.Collections.Generic;
using BTP.Lab;
using UnityEngine;

namespace BTP.Farm
{
    /// <summary>
    /// The Farm six-week timeline week of each crop (plant ID), kept for the session like the Lab's week:
    /// it survives closing the console, re-selecting the plant and scene changes, until Reset or Play mode
    /// ends. Every crop starts at Week 1 (diseased). Illustrative only.
    /// </summary>
    public static class FarmRecoveryState
    {
        public const int FirstWeek = RecoveryTimeline.FirstWeek;
        public const int DefaultWeekCount = 6;

        static readonly Dictionary<string, int> Weeks = new Dictionary<string, int>();

        /// <summary>Raised with the plant ID and its new week whenever a crop's week changes.</summary>
        public static event Action<string, int> WeekChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Weeks.Clear();
            WeekChanged = null;
        }

        /// <summary>Clears every crop's week (tests and a fresh session).</summary>
        internal static void ClearAll() => Weeks.Clear();

        public static int GetWeek(string plantId) =>
            !string.IsNullOrEmpty(plantId) && Weeks.TryGetValue(plantId, out var week) ? week : FirstWeek;

        /// <summary>Sets a crop's week, clamped to 1..<paramref name="weekCount"/>. Returns the week now in effect.</summary>
        public static int SetWeek(string plantId, int week, int weekCount = DefaultWeekCount)
        {
            if (string.IsNullOrEmpty(plantId))
                return FirstWeek;
            week = Mathf.Clamp(week, FirstWeek, Mathf.Max(weekCount, FirstWeek));
            if (GetWeek(plantId) == week)
                return week;
            Weeks[plantId] = week;
            WeekChanged?.Invoke(plantId, week);
            return week;
        }

        public static int Step(string plantId, int delta, int weekCount = DefaultWeekCount) =>
            SetWeek(plantId, GetWeek(plantId) + delta, weekCount);

        /// <summary>Returns one crop to Week 1; other crops keep their week.</summary>
        public static int Reset(string plantId) => SetWeek(plantId, FirstWeek);
    }
}
