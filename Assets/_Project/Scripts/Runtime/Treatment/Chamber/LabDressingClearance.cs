using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Keeps the decorative lab dressing (benches, shelving, trolleys; built by the editor's Furnish Treatment Lab tool) out
    /// of the way of the lesson. The chamber is fitted to the carried plant at runtime and the stations, anchors and the
    /// overview spawn move with it (<see cref="StationLayout"/>), so each floor-standing dressing zone is re-checked whenever
    /// a new layout is placed: a zone that would come within <see cref="StationLayout.MinGap"/> of the chamber, a station or
    /// its approach zone, sit near the spawn, cross the chamber door's swing or stand between the spawn and the chamber or a
    /// station is switched off. Purely visual: no physics or interaction is involved.
    /// </summary>
    public sealed class LabDressingClearance : MonoBehaviour
    {
        /// <summary>One floor-standing group and its floor footprint (world X/Z, axis-aligned).</summary>
        [System.Serializable]
        public struct Zone
        {
            public GameObject Root;
            public Vector2 Min;
            public Vector2 Max;
        }

        /// <summary>Free floor kept around the overview spawn (the user's start point).</summary>
        public const float SpawnClearance = 1.5f;
        /// <summary>Extra margin beyond the chamber door's swing radius.</summary>
        public const float DoorMargin = 0.3f;

        [SerializeField] ChamberStation station;
        [SerializeField] TreatmentChamber chamber;
        [SerializeField] Transform chamberLine;
        [SerializeField] List<Zone> zones = new List<Zone>();

        StationLayout.Result applied;

        public IReadOnlyList<Zone> Zones => zones;

        public void Bind(ChamberStation chamberStation, TreatmentChamber specimenChamber, Transform line, List<Zone> dressingZones)
        {
            station = chamberStation;
            chamber = specimenChamber;
            chamberLine = line;
            zones = dressingZones ?? new List<Zone>();
        }

        void LateUpdate()
        {
            if (station == null)
                return;
            var layout = station.Stations;
            if (layout == null || ReferenceEquals(layout, applied))
                return;
            applied = layout;
            Apply(layout);
        }

        /// <summary>Shows every zone that is clear of <paramref name="layout"/> and hides the others; returns how many are hidden.</summary>
        public int Apply(StationLayout.Result layout)
        {
            var hidden = 0;
            DoorInLine(out var hinge, out var doorRadius);
            foreach (var zone in zones)
            {
                if (zone.Root == null)
                    continue;
                var blocked = Blocks(ToLine(zone), layout, hinge, doorRadius);
                if (zone.Root.activeSelf == blocked)
                    zone.Root.SetActive(!blocked);
                if (blocked)
                    hidden++;
            }

            if (hidden > 0)
                Debug.Log($"[BTP] Lab dressing: {hidden} of {zones.Count} zone(s) hidden to keep the walkways round the chamber and stations clear.");
            return hidden;
        }

        /// <summary>The zone's footprint corners in chamber-line space.</summary>
        public Vector2[] ToLine(Zone zone)
        {
            var corners = new[]
            {
                new Vector3(zone.Min.x, 0f, zone.Min.y), new Vector3(zone.Max.x, 0f, zone.Min.y),
                new Vector3(zone.Max.x, 0f, zone.Max.y), new Vector3(zone.Min.x, 0f, zone.Max.y),
            };
            var result = new Vector2[4];
            for (var i = 0; i < 4; i++)
            {
                var p = chamberLine != null ? chamberLine.InverseTransformPoint(corners[i]) : corners[i];
                result[i] = new Vector2(p.x, p.z);
            }

            return result;
        }

        /// <summary>The door hinge (line space) and the leaf width plus margin; a zero radius when the door is unknown.</summary>
        void DoorInLine(out Vector2 hinge, out float radius)
        {
            hinge = Vector2.zero;
            radius = 0f;
            if (chamber == null || !chamber.HasLayout)
                return;
            var h = chamber.DoorHingeWorld;
            var e = chamber.DoorEdgeWorld;
            var hl = chamberLine != null ? chamberLine.InverseTransformPoint(h) : h;
            hinge = new Vector2(hl.x, hl.z);
            radius = new Vector2(e.x - h.x, e.z - h.z).magnitude + DoorMargin;
        }

        /// <summary>
        /// True when a dressing footprint (line space, convex) is in the way of <paramref name="layout"/>: within
        /// <see cref="StationLayout.MinGap"/> of the chamber, a station base or its approach zone, within
        /// <see cref="SpawnClearance"/> of the overview spawn, inside the door swing (hinge and radius, line space; radius 0 =
        /// unknown) or across the view from the spawn to the chamber front or a station.
        /// </summary>
        public static bool Blocks(Vector2[] footprint, StationLayout.Result layout, Vector2 doorHinge = default, float doorRadius = 0f)
        {
            var chamberRect = layout.Chamber;
            var chamberCorners = new[]
            {
                new Vector2(chamberRect.xMin, chamberRect.yMin), new Vector2(chamberRect.xMax, chamberRect.yMin),
                new Vector2(chamberRect.xMax, chamberRect.yMax), new Vector2(chamberRect.xMin, chamberRect.yMax),
            };
            if (StationLayout.Distance(footprint, chamberCorners) < StationLayout.MinGap)
                return true;
            foreach (var s in layout.Stations)
            {
                if (StationLayout.Distance(footprint, s.Footprint) < StationLayout.MinGap)
                    return true;
                if (StationLayout.Distance(footprint, s.ApproachZone) < StationLayout.MinGap)
                    return true;
            }

            var spawn = layout.Overview;
            if (StationLayout.Distance(footprint, Dot(spawn)) < SpawnClearance)
                return true;
            if (doorRadius > 0f && StationLayout.Distance(footprint, Dot(doorHinge)) < doorRadius)
                return true;

            // Lines of sight from the spawn: the chamber front and every station base.
            if (StationLayout.Overlaps(footprint, Hull(spawn, chamberCorners[0], chamberCorners[1])))
                return true;
            foreach (var s in layout.Stations)
            {
                var points = new List<Vector2> { spawn };
                points.AddRange(s.Footprint);
                if (StationLayout.Overlaps(footprint, Hull(points.ToArray())))
                    return true;
            }

            return false;
        }

        static Vector2[] Dot(Vector2 p) => new[]
        {
            p + new Vector2(-0.005f, -0.005f), p + new Vector2(0.005f, -0.005f), p + new Vector2(0.005f, 0.005f), p + new Vector2(-0.005f, 0.005f),
        };

        /// <summary>Convex hull (Andrew's monotone chain), counter-clockwise.</summary>
        public static Vector2[] Hull(params Vector2[] points)
        {
            var sorted = new List<Vector2>(points);
            sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (sorted.Count < 3)
                return sorted.ToArray();
            var hull = new List<Vector2>();
            for (var pass = 0; pass < 2; pass++)
            {
                var start = hull.Count;
                for (var i = 0; i < sorted.Count; i++)
                {
                    var p = pass == 0 ? sorted[i] : sorted[sorted.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }

                hull.RemoveAt(hull.Count - 1);
            }

            return hull.ToArray();
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}
