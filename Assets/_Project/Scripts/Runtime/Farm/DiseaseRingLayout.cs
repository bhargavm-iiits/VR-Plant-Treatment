using UnityEngine;

namespace BTP.Farm
{
    /// <summary>Pure helpers for the Farm disease ring (no scene needed, so they are unit-tested).</summary>
    public static class DiseaseRingLayout
    {
        /// <summary>Evenly spaced angle in degrees for card <paramref name="index"/> of <paramref name="count"/>.</summary>
        public static float AngleDegrees(int index, int count, float startDegrees = 0f)
        {
            if (count <= 0)
                return startDegrees;
            return startDegrees + 360f * index / count;
        }

        /// <summary>Horizontal offset from the ring centre (angle 0 = +Z, increasing clockwise seen from above).</summary>
        public static Vector3 Offset(float angleDegrees, float radius)
        {
            var radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius);
        }

        /// <summary>
        /// Ring radius: the plant's horizontal extent plus a margin, never below 0.9 m (small plants)
        /// and never above 2.4 m (large trees).
        /// </summary>
        public static float RadiusFor(float plantExtent) => Mathf.Clamp(plantExtent + 0.6f, 0.9f, 2.4f);

        /// <summary>True when the viewer is within <paramref name="maxDistance"/> metres of the plant on the ground plane.</summary>
        public static bool IsWithin(Vector3 viewer, Vector3 plant, float maxDistance)
        {
            var dx = viewer.x - plant.x;
            var dz = viewer.z - plant.z;
            return dx * dx + dz * dz <= maxDistance * maxDistance;
        }

        /// <summary>Ease-out-back: overshoots slightly past 1 before settling.</summary>
        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            var u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>Per-card progress 0..1 for a staggered animation at <paramref name="time"/> seconds.</summary>
        public static float CardProgress(float time, int index, float stagger, float duration) =>
            Mathf.Clamp01((time - index * stagger) / Mathf.Max(duration, 1e-3f));

        /// <summary>Total time until the last of <paramref name="count"/> cards has finished.</summary>
        public static float TotalTime(int count, float stagger, float duration) =>
            Mathf.Max(count - 1, 0) * stagger + duration;
    }
}
