using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTP.Lab
{
    [Serializable]
    public struct RecoveryWeek
    {
        public int week;
        [Range(0, 100)] public int leafHealth;
        [Range(0, 100)] public int chlorophyll;
        [Range(0, 100)] public int diseaseSeverity;
        [Tooltip("Timeline caption; only Week 3 has one in the project brief.")]
        public string caption;
    }

    /// <summary>
    /// Fixed example values for the illustrative Tomato / Early Blight recovery prototype.
    /// They drive the UI and leaf visuals reproducibly; they are not measured or scientific data.
    /// </summary>
    [CreateAssetMenu(menuName = "BTP/Tomato Recovery Data", fileName = "TomatoRecoveryData")]
    public sealed class TomatoRecoveryData : ScriptableObject
    {
        public const string Disclaimer = "Illustrative prototype. Not diagnostic or treatment advice.";
        public const string TreatmentCaption = "Demo treatment applied";
        public const int TreatmentWeek = 3;

        /// <summary>The table from the project brief (Docs/Master_Prompt.md, section 9).</summary>
        public static readonly RecoveryWeek[] BriefWeeks =
        {
            new RecoveryWeek { week = 1, leafHealth = 25, chlorophyll = 30, diseaseSeverity = 85 },
            new RecoveryWeek { week = 2, leafHealth = 40, chlorophyll = 45, diseaseSeverity = 70 },
            new RecoveryWeek { week = 3, leafHealth = 65, chlorophyll = 70, diseaseSeverity = 25, caption = TreatmentCaption },
            new RecoveryWeek { week = 4, leafHealth = 75, chlorophyll = 80, diseaseSeverity = 18 },
            new RecoveryWeek { week = 5, leafHealth = 88, chlorophyll = 90, diseaseSeverity = 8 },
            new RecoveryWeek { week = 6, leafHealth = 96, chlorophyll = 96, diseaseSeverity = 2 },
        };

        [SerializeField] string plantId = "tomato";
        [SerializeField] string diseaseName = "Early Blight";
        [SerializeField] List<RecoveryWeek> weeks = new List<RecoveryWeek>(BriefWeeks);

        public string PlantId => plantId;
        public string DiseaseName => diseaseName;
        public IReadOnlyList<RecoveryWeek> Weeks => weeks;
        public int WeekCount => weeks.Count;

        /// <summary>Returns a week by number (1-based), clamped to the available weeks.</summary>
        public RecoveryWeek GetWeek(int week) => weeks[Mathf.Clamp(week, 1, weeks.Count) - 1];

        internal void ResetToBrief() => weeks = new List<RecoveryWeek>(BriefWeeks);
    }
}
