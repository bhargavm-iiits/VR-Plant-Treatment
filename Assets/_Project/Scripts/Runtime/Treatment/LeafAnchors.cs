using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Points on a leaf specimen, computed in the editor from the leaf mesh and its diseased texture:
    /// lesion areas (least green), healthy tissue (greenest), the underside and the petiole. Local
    /// space of this transform; the front surface faces local -Z.
    /// </summary>
    public sealed class LeafAnchors : MonoBehaviour
    {
        [System.Serializable]
        public struct Point
        {
            public Vector3 position;
            public Vector3 normal;
        }

        [SerializeField] List<Point> lesions = new List<Point>();
        [SerializeField] List<Point> healthy = new List<Point>();
        [SerializeField] List<Point> underside = new List<Point>();
        [SerializeField] Point petiole;
        [SerializeField] Bounds leafBounds;
        [Tooltip("Vein segments on the front surface as consecutive start/end pairs (midrib first), baked in the editor.")]
        [SerializeField] List<Point> veins = new List<Point>();

        public IReadOnlyList<Point> Lesions => lesions;
        public IReadOnlyList<Point> Healthy => healthy;
        public IReadOnlyList<Point> Underside => underside;
        public Point Petiole => petiole;
        public Bounds LeafBounds => leafBounds;
        /// <summary>Baked vein segments (pairs of points, local space); empty on specimens built before veins were baked.</summary>
        public IReadOnlyList<Point> Veins => veins;

        internal void SetVeins(List<Point> segmentPoints) => veins = segmentPoints ?? new List<Point>();

        /// <summary>
        /// Vein segments in local space: the baked ones, or (older specimens) an approximation from the petiole to the
        /// leaf tip and out to each healthy and lesion point, which lie on the leaf surface.
        /// </summary>
        public List<(Point start, Point end)> VeinSegments()
        {
            var list = new List<(Point, Point)>();
            for (var i = 0; i + 1 < veins.Count; i += 2)
                list.Add((veins[i], veins[i + 1]));
            if (list.Count > 0)
                return list;
            var tip = new Point { position = new Vector3(petiole.position.x, leafBounds.max.y - leafBounds.size.y * 0.06f, petiole.position.z), normal = petiole.normal };
            var previous = petiole;
            for (var k = 1; k <= 4; k++)
            {
                var next = new Point { position = Vector3.Lerp(petiole.position, tip.position, k / 4f), normal = petiole.normal };
                list.Add((previous, next));
                previous = next;
            }

            foreach (var p in healthy)
                list.Add((new Point { position = new Vector3(petiole.position.x, p.position.y * 0.8f, p.position.z), normal = p.normal }, p));
            foreach (var p in lesions)
                list.Add((new Point { position = new Vector3(petiole.position.x, p.position.y * 0.8f, p.position.z), normal = p.normal }, p));
            return list;
        }

        internal void Set(List<Point> lesionPoints, List<Point> healthyPoints, List<Point> undersidePoints, Point petiolePoint, Bounds bounds)
        {
            lesions = lesionPoints;
            healthy = healthyPoints;
            underside = undersidePoints;
            petiole = petiolePoint;
            leafBounds = bounds;
        }

        /// <summary>World pose of the <paramref name="index"/>-th point of a slot, facing out of the surface.</summary>
        public bool TryGetPose(string slot, int index, out Vector3 position, out Quaternion rotation)
        {
            IReadOnlyList<Point> list;
            switch (slot)
            {
                case "leaf_lesion": list = lesions; break;
                case "leaf_healthy": list = healthy; break;
                case "leaf_underside": list = underside; break;
                case "leaf_petiole": list = new[] { petiole }; break;
                default: list = null; break;
            }

            if (list == null || list.Count == 0)
            {
                position = default;
                rotation = default;
                return false;
            }

            var point = list[index % list.Count];
            var normal = transform.TransformDirection(point.normal).normalized;
            position = transform.TransformPoint(point.position) + normal * 0.004f;
            // Targets' front faces (-Z) point out of the leaf surface.
            rotation = Quaternion.LookRotation(-normal, transform.up);
            return true;
        }
    }
}