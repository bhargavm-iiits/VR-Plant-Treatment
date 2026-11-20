using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Geometry tests of the hands-on steps (pure; unit tested): where a scoop's granules landed relative to the selected
    /// plant's root zone, whether a spray cone reaches the plant's foliage bounds, and the eased return of a released tool
    /// to its holder.
    /// </summary>
    public static class GuidedGeometry
    {
        /// <summary>The root zone: a disc of <paramref name="radius"/> round the stem, clipped to the tray soil.</summary>
        public static bool InRootZone(Vector3 point, Vector2 stem, float radius, Vector2 soilMin, Vector2 soilMax)
        {
            if (point.x < soilMin.x || point.x > soilMax.x || point.z < soilMin.y || point.z > soilMax.y)
                return false;
            var d = new Vector2(point.x - stem.x, point.z - stem.y);
            return d.sqrMagnitude <= radius * radius;
        }

        /// <summary>Share (0..1) of <paramref name="points"/> inside the root zone; 0 for none.</summary>
        public static float RootZoneShare(IEnumerable<Vector3> points, Vector2 stem, float radius, Vector2 soilMin, Vector2 soilMax)
        {
            var inside = 0;
            var total = 0;
            if (points != null)
            {
                foreach (var p in points)
                {
                    total++;
                    if (InRootZone(p, stem, radius, soilMin, soilMax))
                        inside++;
                }
            }

            return total == 0 ? 0f : inside / (float)total;
        }

        /// <summary>
        /// Share (0..1) of <paramref name="rays"/> rays in a cone of <paramref name="coneDegrees"/> from
        /// <paramref name="origin"/> along <paramref name="direction"/> that hit <paramref name="target"/> within
        /// <paramref name="range"/> (the same golden-angle pattern as the training sprayer).
        /// </summary>
        public static float ConeHitShare(Vector3 origin, Vector3 direction, float coneDegrees, float range, Bounds target, int rays = 12)
        {
            if (direction.sqrMagnitude < 1e-8f || rays <= 0)
                return 0f;
            direction.Normalize();
            var rotation = Quaternion.LookRotation(direction, Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
            var hits = 0;
            for (var i = 0; i < rays; i++)
            {
                var angle = i * 2.39996f;
                var spread = coneDegrees * Mathf.Sqrt((i + 0.5f) / rays);
                var dir = Quaternion.AngleAxis(spread, rotation * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f)) * direction;
                if (target.IntersectRay(new Ray(origin, dir), out var distance) && distance <= range)
                    hits++;
            }

            return hits / (float)rays;
        }

        /// <summary>Ease-in-out (smoothstep) of 0..1.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The pose <paramref name="elapsed"/> seconds into an eased settle of <paramref name="duration"/> from a release pose to home.</summary>
        public static void SettlePose(Vector3 fromPosition, Quaternion fromRotation, Vector3 homePosition, Quaternion homeRotation, float elapsed, float duration,
            out Vector3 position, out Quaternion rotation)
        {
            var t = duration <= 1e-4f ? 1f : Ease(elapsed / duration);
            position = Vector3.LerpUnclamped(fromPosition, homePosition, t);
            rotation = Quaternion.Slerp(fromRotation, homeRotation, t);
        }

        /// <summary>A release within <paramref name="snapDistance"/> of home settles there; farther away the tool drops.</summary>
        public static bool ReleaseSettlesHome(Vector3 releasePosition, Vector3 homePosition, float snapDistance) =>
            (releasePosition - homePosition).sqrMagnitude <= snapDistance * snapDistance;

        /// <summary>
        /// Whether a dropped tool should go home now: lying still for <paramref name="idleLimit"/>, fallen below the floor, or
        /// out of reach (<paramref name="maxDistance"/> from home).
        /// </summary>
        public static bool ShouldAutoReturn(float idleSeconds, float idleLimit, Vector3 position, float floorY, Vector3 homePosition, float maxDistance) =>
            idleSeconds >= idleLimit || position.y < floorY - 0.5f || (position - homePosition).sqrMagnitude > maxDistance * maxDistance;
    }
}
