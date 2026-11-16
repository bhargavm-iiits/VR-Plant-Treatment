using BTP.Lab;
using UnityEngine;

namespace BTP.Farm
{
    /// <summary>Pure helpers for the Farm six-week timeline (unit-tested without a scene).</summary>
    public static class FarmRecoveryMath
    {
        /// <summary>Seconds a crop takes to blend from one week's look to the next, as in the Lab.</summary>
        public const float TransitionSeconds = 1.6f;

        /// <summary>
        /// How diseased the crop looks (1 = Week 1 diseased albedo, 0 = healthy), from the shared example table:
        /// the week's disease severity relative to Week 1's.
        /// </summary>
        public static float DiseaseAmount(RecoveryWeek week, RecoveryWeek firstWeek)
        {
            if (firstWeek.diseaseSeverity <= 0)
                return 0f;
            return Mathf.Clamp01((float)week.diseaseSeverity / firstWeek.diseaseSeverity);
        }

        public static float DiseaseAmount(TomatoRecoveryData data, int week) =>
            data == null ? 1f : DiseaseAmount(data.GetWeek(week), data.GetWeek(RecoveryTimeline.FirstWeek));

        /// <summary>Smooth step used for the timed blend (eases in and out, stays within 0..1).</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>One-line summary shown on the console, e.g. "Week 3 of 6 · Leaf 65% · Chlorophyll 70% · Severity 25%".</summary>
        public static string StatusLine(RecoveryWeek week, int weekCount) =>
            $"Week {week.week} of {weekCount} · Leaf {week.leafHealth}% · Chlorophyll {week.chlorophyll}% · Severity {week.diseaseSeverity}%";

        /// <summary>
        /// Where the console stands: on the line from the plant to the viewer, just outside the card ring when there
        /// is room (never closer than 0.7 m to the viewer, never inside 0.5 m of the plant), at a fixed height.
        /// </summary>
        public static Vector3 ConsolePosition(Vector3 plant, Vector3 viewer, float ringRadius, float height)
        {
            var toViewer = viewer - plant;
            toViewer.y = 0f;
            var viewerDistance = toViewer.magnitude;
            var direction = viewerDistance > 1e-3f ? toViewer / viewerDistance : Vector3.back;
            var distance = ConsoleDistance(ringRadius, viewerDistance);
            var position = plant + direction * distance;
            position.y = height;
            return position;
        }

        public static float ConsoleDistance(float ringRadius, float viewerDistance)
        {
            var wanted = ringRadius + 0.3f;
            var limit = Mathf.Max(0.5f, viewerDistance - 0.7f);
            return Mathf.Clamp(wanted, 0.5f, limit);
        }
    }
}
