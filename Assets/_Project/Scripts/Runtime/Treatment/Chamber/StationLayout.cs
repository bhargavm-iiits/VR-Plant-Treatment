using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Addendum 6: places the treatment equipment as separate stations around the fixed chamber (pure; unit tested).
    /// Works in the hall's chamber-line space (X right, Z away from the viewer; the chamber exterior occupies
    /// X [-width, 0], Z [0, depth] and only ever grows to -X and +Z). The touchscreen kiosk stands at the chamber's
    /// front-right, the WATER CONTROL console at its front-left (beside the chamber's left edge, whatever its width) and
    /// the TREATMENT TOOLS bench along the open right-hand window wall, so the right side never depends on the chamber
    /// size. Each station has its own base, a 1 m approach zone in front of it and a teleport anchor in that zone facing
    /// it; the overview spawn stands in front of everything, far enough back to see all stations at a glance.
    /// A station's yaw turns its forward (+Z, away from the user standing at its anchor) in line space.
    /// </summary>
    public static class StationLayout
    {
        public enum Station
        {
            Kiosk,
            Water,
            Tools,
        }

        /// <summary>Clear walking width required between stations and around the chamber.</summary>
        public const float MinGap = 1.2f;
        /// <summary>Depth of the free approach zone in front of each station.</summary>
        public const float ApproachDepth = 1.0f;
        /// <summary>Stations keep at least this much from the hall walls.</summary>
        public const float WallMargin = 0.05f;
        /// <summary>Half the horizontal angle the overview must fit everything into (a 62-degree, 16:9 view is about 47).</summary>
        public const float OverviewHalfAngle = 41f;

        public static readonly Vector2 KioskSize = new Vector2(1.1f, 0.8f);
        public static readonly Vector2 WaterSize = new Vector2(1.0f, 0.7f);
        public static readonly Vector2 ToolsSize = new Vector2(2.0f, 0.65f);
        public const float KioskYaw = 25f, WaterYaw = -25f, ToolsYaw = 90f;

        /// <summary>One station: footprint centre, yaw, size (local X width, local Z depth) and its anchor.</summary>
        public struct Pose
        {
            public Station Id;
            public Vector2 Centre;
            public float Yaw;
            public Vector2 Size;
            /// <summary>Where the user stands (the head goes above it), facing the station.</summary>
            public Vector2 Anchor;

            public Vector2 Forward => new Vector2(Mathf.Sin(Yaw * Mathf.Deg2Rad), Mathf.Cos(Yaw * Mathf.Deg2Rad));
            public Vector2 Right => new Vector2(Forward.y, -Forward.x);

            public Vector2 Local(float x, float z) => Centre + Right * x + Forward * z;

            /// <summary>The base's four floor corners.</summary>
            public Vector2[] Footprint => new[]
            {
                Local(-Size.x * 0.5f, -Size.y * 0.5f), Local(Size.x * 0.5f, -Size.y * 0.5f), Local(Size.x * 0.5f, Size.y * 0.5f),
                Local(-Size.x * 0.5f, Size.y * 0.5f),
            };

            /// <summary>The free floor in front of the station (its width, ApproachDepth deep).</summary>
            public Vector2[] ApproachZone => new[]
            {
                Local(-Size.x * 0.5f, -Size.y * 0.5f - ApproachDepth), Local(Size.x * 0.5f, -Size.y * 0.5f - ApproachDepth),
                Local(Size.x * 0.5f, -Size.y * 0.5f), Local(-Size.x * 0.5f, -Size.y * 0.5f),
            };
        }

        public sealed class Result
        {
            public Pose Kiosk, Water, Tools;
            public Vector2 Overview;
            public float OverviewYaw;
            public Rect Chamber;
            public Rect Hall;
            public readonly List<string> Problems = new List<string>();
            public IEnumerable<Pose> Stations => new[] { Kiosk, Water, Tools };
            public bool Valid => Problems.Count == 0;
        }

        /// <summary>
        /// Station anchors relative to the station centre (local X, local Z): the kiosk's is set by its screen geometry
        /// (see <see cref="ChamberStation"/>), the others stand about 0.2 m in front of the base (seated reach to the controls and docks).
        /// </summary>
        public static Vector2 AnchorOffset(Station id, Vector2 kioskAnchor) => id switch
        {
            Station.Kiosk => kioskAnchor,
            Station.Water => new Vector2(0f, -WaterSize.y * 0.5f - 0.2f),
            _ => new Vector2(0f, -ToolsSize.y * 0.5f - 0.22f),
        };

        /// <summary>
        /// Lays the stations out for a chamber exterior of <paramref name="chamberWidth"/> x <paramref name="chamberDepth"/>
        /// inside <paramref name="hall"/> (line space; xMin/xMax, yMin = front wall Z, yMax = back wall Z).
        /// </summary>
        public static Result Compute(float chamberWidth, float chamberDepth, Rect hall, Vector2 kioskAnchor)
        {
            var result = new Result
            {
                Chamber = Rect.MinMaxRect(-chamberWidth, 0f, 0f, chamberDepth),
                Hall = hall,
            };

            // Kiosk: front-right, turned towards the room; the chamber never grows to +X, so this is fixed.
            result.Kiosk = Make(Station.Kiosk, new Vector2(2.0f, -0.85f), KioskYaw, KioskSize, kioskAnchor);
            // WATER CONTROL: front-left, its centre in line with the chamber's left edge, well in front of the glass.
            var waterX = Mathf.Max(-chamberWidth - 0.2f, hall.xMin + 0.75f);
            result.Water = Make(Station.Water, new Vector2(waterX, -1.85f), WaterYaw, WaterSize, kioskAnchor);
            // TREATMENT TOOLS: along the right-hand window wall, facing into the room.
            result.Tools = Make(Station.Tools, new Vector2(hall.xMax - WallMargin - ToolsSize.y * 0.5f, 1.6f), ToolsYaw, ToolsSize, kioskAnchor);

            PlaceOverview(result);
            Validate(result);
            return result;
        }

        static Pose Make(Station id, Vector2 centre, float yaw, Vector2 size, Vector2 kioskAnchor)
        {
            var pose = new Pose { Id = id, Centre = centre, Yaw = yaw, Size = size };
            var offset = AnchorOffset(id, kioskAnchor);
            pose.Anchor = pose.Local(offset.x, offset.y);
            return pose;
        }

        /// <summary>Everything that must be in the overview: every station's base and the chamber's front corners.</summary>
        static List<Vector2> OverviewPoints(Result r)
        {
            var points = new List<Vector2>();
            foreach (var s in r.Stations)
                points.AddRange(s.Footprint);
            points.Add(new Vector2(r.Chamber.xMin, r.Chamber.yMin));
            points.Add(new Vector2(r.Chamber.xMax, r.Chamber.yMin));
            points.Add(new Vector2(r.Chamber.xMin, r.Chamber.yMax));
            points.Add(new Vector2(r.Chamber.xMax, r.Chamber.yMax));
            return points;
        }

        /// <summary>Bearing in degrees (0 = +Z, positive to the right) from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float Bearing(Vector2 from, Vector2 to) => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;

        static void PlaceOverview(Result r)
        {
            var points = OverviewPoints(r);
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var frontZ = float.MaxValue;
            foreach (var p in points)
            {
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                frontZ = Mathf.Min(frontZ, p.y);
            }

            var x = Mathf.Clamp((minX + maxX) * 0.5f, r.Hall.xMin + 0.6f, r.Hall.xMax - 0.6f);
            var best = new Vector2(x, r.Hall.yMin + 0.6f);
            var bestSpan = float.MaxValue;
            for (var z = frontZ - 2.0f; z >= r.Hall.yMin + 0.6f - 1e-4f; z -= 0.05f)
            {
                var candidate = new Vector2(x, z);
                Span(candidate, points, out var low, out var high);
                var span = high - low;
                if (span < bestSpan)
                {
                    bestSpan = span;
                    best = candidate;
                }

                if (span <= OverviewHalfAngle * 2f)
                {
                    best = candidate;
                    break;
                }
            }

            r.Overview = best;
            Span(best, points, out var lo, out var hi);
            r.OverviewYaw = (lo + hi) * 0.5f;
        }

        static void Span(Vector2 from, List<Vector2> points, out float low, out float high)
        {
            low = float.MaxValue;
            high = float.MinValue;
            foreach (var p in points)
            {
                var b = Bearing(from, p);
                low = Mathf.Min(low, b);
                high = Mathf.Max(high, b);
            }
        }

        /// <summary>Half the horizontal angle the overview needs to show every station and the chamber front.</summary>
        public static float OverviewHalfSpan(Result r)
        {
            Span(r.Overview, OverviewPoints(r), out var low, out var high);
            return Mathf.Max(high - r.OverviewYaw, r.OverviewYaw - low);
        }

        static Vector2[] Corners(Rect rect) => new[]
        {
            new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax),
        };

        /// <summary>Records every rule the layout breaks (empty when valid).</summary>
        static void Validate(Result r)
        {
            var chamber = Corners(r.Chamber);
            var stations = new List<Pose>(r.Stations);
            for (var i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                var gap = Distance(s.Footprint, chamber);
                if (gap < MinGap - 1e-4f)
                    r.Problems.Add($"{s.Id}: {gap:0.00} m from the chamber (< {MinGap})");
                if (Distance(s.ApproachZone, chamber) < 0.3f - 1e-4f)
                    r.Problems.Add($"{s.Id}: approach zone runs into the chamber");
                foreach (var p in s.Footprint)
                    if (!Inside(r.Hall, p, WallMargin - 1e-4f))
                        r.Problems.Add($"{s.Id}: base outside the hall at {p}");
                foreach (var p in s.ApproachZone)
                    if (!Inside(r.Hall, p, 0f))
                        r.Problems.Add($"{s.Id}: approach zone outside the hall at {p}");
                if (!Inside(r.Hall, s.Anchor, 0.3f))
                    r.Problems.Add($"{s.Id}: anchor too close to a wall");
                for (var j = i + 1; j < stations.Count; j++)
                {
                    var o = stations[j];
                    var between = Distance(s.Footprint, o.Footprint);
                    if (between < MinGap - 1e-4f)
                        r.Problems.Add($"{s.Id}-{o.Id}: {between:0.00} m apart (< {MinGap})");
                    if (Overlaps(s.ApproachZone, o.Footprint) || Overlaps(o.ApproachZone, s.Footprint))
                        r.Problems.Add($"{s.Id}-{o.Id}: an approach zone is blocked");
                }
            }

            if (!Inside(r.Hall, r.Overview, 0.5f))
                r.Problems.Add("overview spawn outside the hall");
            foreach (var s in stations)
                if (Contains(s.Footprint, r.Overview) || Contains(s.ApproachZone, r.Overview))
                    r.Problems.Add($"overview spawn inside {s.Id}'s base or approach zone");
        }

        static bool Inside(Rect rect, Vector2 p, float margin) =>
            p.x >= rect.xMin + margin && p.x <= rect.xMax - margin && p.y >= rect.yMin + margin && p.y <= rect.yMax - margin;

        // ------------------------------------------------------------------ convex polygon geometry

        /// <summary>Clear distance between two convex polygons (0 when they touch or overlap).</summary>
        public static float Distance(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            if (Overlaps(a, b))
                return 0f;
            var best = float.MaxValue;
            for (var i = 0; i < a.Count; i++)
            for (var j = 0; j < b.Count; j++)
            {
                best = Mathf.Min(best, PointSegment(a[i], b[j], b[(j + 1) % b.Count]));
                best = Mathf.Min(best, PointSegment(b[j], a[i], a[(i + 1) % a.Count]));
            }

            return best;
        }

        static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Separating-axis test for two convex polygons.</summary>
        public static bool Overlaps(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b) => !Separated(a, b) && !Separated(b, a);

        static bool Separated(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            for (var i = 0; i < a.Count; i++)
            {
                var edge = a[(i + 1) % a.Count] - a[i];
                var axis = new Vector2(-edge.y, edge.x);
                Project(a, axis, out var aMin, out var aMax);
                Project(b, axis, out var bMin, out var bMax);
                if (aMax < bMin - 1e-6f || bMax < aMin - 1e-6f)
                    return true;
            }

            return false;
        }

        static void Project(IReadOnlyList<Vector2> poly, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var p in poly)
            {
                var d = Vector2.Dot(p, axis);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
        }

        public static bool Contains(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            var sign = 0;
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                var cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                var s = cross > 1e-6f ? 1 : cross < -1e-6f ? -1 : 0;
                if (s == 0)
                    continue;
                if (sign == 0)
                    sign = s;
                else if (s != sign)
                    return false;
            }

            return true;
        }
    }
}
