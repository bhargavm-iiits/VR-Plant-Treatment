using System;
using UnityEngine;

namespace BTP.Lab
{
    /// <summary>The recovery demo's current week, with Previous / Next / direct selection / Reset.</summary>
    public sealed class RecoveryTimeline
    {
        public const int FirstWeek = 1;

        readonly TomatoRecoveryData data;

        public RecoveryTimeline(TomatoRecoveryData data)
        {
            this.data = data;
        }

        public int CurrentWeek { get; private set; } = FirstWeek;

        public int WeekCount => data.WeekCount;

        public RecoveryWeek Current => data.GetWeek(CurrentWeek);

        public bool CanGoBack => CurrentWeek > FirstWeek;

        public bool CanGoForward => CurrentWeek < WeekCount;

        /// <summary>Raised with the new week number whenever the week changes.</summary>
        public event Action<int> WeekChanged;

        public void Next() => SetWeek(CurrentWeek + 1);

        public void Previous() => SetWeek(CurrentWeek - 1);

        /// <summary>Returns to Week 1. Only the demo is reset; the plant selection is untouched.</summary>
        public void Reset() => SetWeek(FirstWeek);

        public void SetWeek(int week)
        {
            week = Mathf.Clamp(week, FirstWeek, WeekCount);
            if (week == CurrentWeek)
                return;
            CurrentWeek = week;
            WeekChanged?.Invoke(week);
        }
    }
}
