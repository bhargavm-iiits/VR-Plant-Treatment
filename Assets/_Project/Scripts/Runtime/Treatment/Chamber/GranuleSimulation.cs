using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The fertiliser granules poured from the packet (pure and deterministic; no PhysX, so it is cheap on Quest and the
    /// same every time for a specimen). Granules leave the packet's open corner one after another, fall under gravity,
    /// bounce lightly once or twice on the soil surface and settle in a band beside the stem: never on the stem, never
    /// outside the tray. Settled granules stay where they are.
    /// </summary>
    public sealed class GranuleSimulation
    {
        public const float Gravity = 9.81f;
        public const float Radius = 0.0045f;
        /// <summary>Typical drawn granule radius (2-4 mm granules); <see cref="Radius"/> stays the collision radius.</summary>
        public const float VisualRadius = 0.0015f;
        public const int MinCount = 40, MaxCount = 160;
        const float ReleaseSeconds = 1.1f;
        const int MaxBounces = 2;

        public struct Granule
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Delay;
            public int Bounces;
            public bool Settled;
            public bool Released;
            /// <summary>Stable id of the granule in this simulation (its look: colour, shape, size).</summary>
            public uint Id;
            /// <summary>Visual orientation: tumbles while airborne, rolls on a bounce, frozen once settled.</summary>
            public Quaternion Rotation;
            /// <summary>Angular velocity (axis * radians per second).</summary>
            public Vector3 Spin;
        }

        /// <summary>Where the band of settled granules lies, in world space.</summary>
        public struct Area
        {
            /// <summary>Soil surface height.</summary>
            public float SoilY;
            /// <summary>Inner soil rectangle (tray walls excluded) in XZ: min corner and max corner.</summary>
            public Vector2 Min, Max;
            /// <summary>The stem's position in XZ.</summary>
            public Vector2 Stem;
            /// <summary>No granule settles closer than this to the stem.</summary>
            public float StemClearance;
            /// <summary>Band radius from the stem: inner and outer.</summary>
            public float BandInner, BandOuter;
            /// <summary>Unit XZ direction from the stem to the band's middle (towards the pour side).</summary>
            public Vector2 BandDirection;
            /// <summary>True when the chamber's four glass walls bound the pour (<see cref="WallMin"/>..<see cref="WallMax"/>).</summary>
            public bool HasWalls;
            /// <summary>Inner faces of the glass walls in XZ: min corner (left, front) and max corner (right, back).</summary>
            public Vector2 WallMin, WallMax;
        }

        /// <summary>Share of the speed into a glass wall a granule keeps when it bounces off (and along it, after friction).</summary>
        public const float WallRestitution = 0.2f, WallFriction = 0.3f;

        /// <summary>
        /// Keeps a granule of <paramref name="radius"/> inside the glass walls: a granule crossing a wall's inner plane is put
        /// back on it, its speed into the wall is reflected and scaled by <see cref="WallRestitution"/> and its speed along
        /// the wall loses <see cref="WallFriction"/>. Returns true when it touched a wall.
        /// </summary>
        public static bool CollideWalls(ref Vector3 position, ref Vector3 velocity, Vector2 min, Vector2 max, float radius)
        {
            var hit = false;
            const float keep = 1f - WallFriction;
            if (position.x - radius < min.x || position.x + radius > max.x)
            {
                var left = position.x - radius < min.x;
                position.x = left ? min.x + radius : max.x - radius;
                if (left ? velocity.x < 0f : velocity.x > 0f)
                    velocity.x = -velocity.x * WallRestitution;
                velocity.y *= keep;
                velocity.z *= keep;
                hit = true;
            }

            if (position.z - radius < min.y || position.z + radius > max.y)
            {
                var front = position.z - radius < min.y;
                position.z = front ? min.y + radius : max.y - radius;
                if (front ? velocity.z < 0f : velocity.z > 0f)
                    velocity.z = -velocity.z * WallRestitution;
                velocity.x *= keep;
                velocity.y *= keep;
                hit = true;
            }

            return hit;
        }

        /// <summary>Adds one granule at <paramref name="position"/> moving at <paramref name="velocity"/> now (tests; spills).</summary>
        public void Launch(Vector3 position, Vector3 velocity) =>
            granules.Add(WithSpin(new Granule { Position = position, Velocity = velocity, Delay = time }));

        /// <summary>
        /// Gives a new granule its id, a random orientation and a tumble (from a hash of the seed and its index: the
        /// shared random sequence, and so where granules land, is unchanged).
        /// </summary>
        Granule WithSpin(Granule g)
        {
            var id = Hash((uint)seed * 0x9E3779B1u + (uint)granules.Count * 0x85EBCA77u + 0x27D4EB2Fu);
            g.Id = id;
            g.Rotation = RandomRotation(id);
            var h = Hash(id ^ 0x165667B1u);
            var axis = new Vector3((h & 0xFF) / 127.5f - 1f, ((h >> 8) & 0xFF) / 127.5f - 1f, ((h >> 16) & 0xFF) / 127.5f - 1f);
            if (axis.sqrMagnitude < 1e-4f)
                axis = Vector3.right;
            g.Spin = axis.normalized * (6f + (h >> 24) / 255f * 22f);
            return g;
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        static Quaternion RandomRotation(uint h)
        {
            var a = Hash(h);
            var b = Hash(a ^ 0x7FEB352Du);
            var c = Hash(b ^ 0x846CA68Bu);
            float u1 = (a & 0xFFFFFF) / (float)0x1000000, u2 = (b & 0xFFFFFF) / (float)0x1000000, u3 = (c & 0xFFFFFF) / (float)0x1000000;
            float s1 = Mathf.Sqrt(1f - u1), s2 = Mathf.Sqrt(u1);
            return new Quaternion(s1 * Mathf.Sin(2f * Mathf.PI * u2), s1 * Mathf.Cos(2f * Mathf.PI * u2), s2 * Mathf.Sin(2f * Mathf.PI * u3),
                s2 * Mathf.Cos(2f * Mathf.PI * u3));
        }

        readonly List<Granule> granules = new List<Granule>();
        readonly Area area;
        readonly System.Random random;
        readonly int seed;
        float time;

        public IReadOnlyList<Granule> Granules => granules;
        public int Count => granules.Count;
        public Area Band => area;
        public float Elapsed => time;

        public bool AllSettled
        {
            get
            {
                foreach (var g in granules)
                    if (!g.Settled)
                        return false;
                return true;
            }
        }

        /// <summary>Granules for an applied dose: none without a supplement, else 8 per unit x application, 40-160.</summary>
        public static int CountFor(double dose, double applications)
        {
            if (dose <= 0d || applications <= 0d)
                return 0;
            return Mathf.Clamp(Mathf.RoundToInt((float)(dose * applications * 8d)), MinCount, MaxCount);
        }

        /// <summary>An empty pour (Addendum 5): granules are added with <see cref="Emit"/> while the held packet is tilted.</summary>
        public GranuleSimulation(int seed, Area band)
        {
            area = band;
            random = new System.Random(seed);
            this.seed = seed;
        }

        /// <summary>Adds <paramref name="count"/> granules leaving <paramref name="source"/> now, aimed into the band.</summary>
        public void Emit(Vector3 source, int count)
        {
            for (var i = 0; i < count; i++)
                granules.Add(WithSpin(NewGranule(source, time)));
        }

        public GranuleSimulation(int count, int seed, Vector3 source, Area band)
        {
            area = band;
            random = new System.Random(seed);
            this.seed = seed;
            for (var i = 0; i < Mathf.Max(0, count); i++)
                granules.Add(WithSpin(NewGranule(source, ReleaseSeconds * i / Mathf.Max(1, count))));
        }

        Granule NewGranule(Vector3 source, float delay)
        {
            var band = area;
            float Next() => (float)random.NextDouble();
            var direction = band.BandDirection.sqrMagnitude > 1e-6f ? band.BandDirection.normalized : Vector2.left;
            var baseAngle = Mathf.Atan2(direction.y, direction.x);
            {
                // A landing point in the band (within +-55 degrees of its middle), kept inside the soil rectangle.
                var angle = baseAngle + (Next() - 0.5f) * Mathf.Deg2Rad * 110f;
                var radius = Mathf.Lerp(band.BandInner, band.BandOuter, Mathf.Sqrt(Next()));
                var target = band.Stem + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                target = Clamp(target);

                // Leave the corner with a small random downward speed and reach the target when meeting the soil.
                var start = source + new Vector3((Next() - 0.5f) * 0.01f, 0f, (Next() - 0.5f) * 0.01f);
                var vy = -0.05f - Next() * 0.2f;
                var fall = Mathf.Max(start.y - (band.SoilY + Radius), 0.01f);
                var t = (vy + Mathf.Sqrt(vy * vy + 2f * Gravity * fall)) / Gravity;
                // Bounces carry the granule a little further along the same line; aim short so it ends near the target.
                var horizontal = (target - new Vector2(start.x, start.z)) / Mathf.Max(t, 0.05f) * 0.86f;
                return new Granule
                {
                    Position = start,
                    Velocity = new Vector3(horizontal.x, vy, horizontal.y),
                    Delay = delay,
                };
            }
        }

        Vector2 Clamp(Vector2 point)
        {
            var margin = Radius * 2f;
            point.x = Mathf.Clamp(point.x, area.Min.x + margin, area.Max.x - margin);
            point.y = Mathf.Clamp(point.y, area.Min.y + margin, area.Max.y - margin);
            var fromStem = point - area.Stem;
            if (fromStem.magnitude < area.StemClearance)
            {
                var away = fromStem.sqrMagnitude > 1e-8f ? fromStem.normalized : (area.BandDirection.sqrMagnitude > 1e-6f ? area.BandDirection.normalized : Vector2.left);
                point = area.Stem + away * area.StemClearance;
                point.x = Mathf.Clamp(point.x, area.Min.x + margin, area.Max.x - margin);
                point.y = Mathf.Clamp(point.y, area.Min.y + margin, area.Max.y - margin);
            }

            return point;
        }

        /// <summary>Advances the simulation; large steps are split so bounces stay stable.</summary>
        public void Step(float deltaTime)
        {
            var remaining = Mathf.Max(0f, deltaTime);
            while (remaining > 1e-6f)
            {
                var dt = Mathf.Min(remaining, 1f / 120f);
                remaining -= dt;
                time += dt;
                for (var i = 0; i < granules.Count; i++)
                {
                    var g = granules[i];
                    if (g.Settled)
                        continue;
                    if (time < g.Delay)
                        continue;
                    g.Released = true;
                    g.Velocity.y -= Gravity * dt;
                    g.Position += g.Velocity * dt;
                    var spin = g.Spin.magnitude;
                    if (spin > 1e-4f)
                        g.Rotation = Quaternion.AngleAxis(spin * dt * Mathf.Rad2Deg, g.Spin / spin) * g.Rotation;
                    if (area.HasWalls && CollideWalls(ref g.Position, ref g.Velocity, area.WallMin, area.WallMax, Radius))
                        g.Spin = new Vector3(-g.Spin.z, g.Spin.x * 0.8f, g.Spin.y) * 0.7f;
                    var floor = area.SoilY + Radius;
                    if (g.Position.y <= floor)
                    {
                        g.Position.y = floor;
                        if (g.Bounces < MaxBounces && -g.Velocity.y > 0.35f)
                        {
                            g.Velocity.y = -g.Velocity.y * 0.28f;
                            g.Velocity.x *= 0.3f;
                            g.Velocity.z *= 0.3f;
                            g.Bounces++;
                            // Friction with the soil turns the slide into a roll (about the axis across the motion).
                            var roll = Vector3.Cross(Vector3.up, new Vector3(g.Velocity.x, 0f, g.Velocity.z)) / VisualRadius;
                            g.Spin = Vector3.Lerp(g.Spin * 0.4f, roll, 0.7f);
                        }
                        else
                        {
                            Settle(ref g);
                        }
                    }

                    granules[i] = g;
                }
            }
        }

        void Settle(ref Granule g)
        {
            var xz = Clamp(new Vector2(g.Position.x, g.Position.z));
            g.Position = new Vector3(xz.x, area.SoilY + Radius, xz.y);
            g.Velocity = Vector3.zero;
            g.Spin = Vector3.zero;
            g.Settled = true;
            g.Released = true;
        }

        /// <summary>Runs until every granule has settled (or <paramref name="maxSeconds"/> passed); returns whether all settled.</summary>
        public bool RunToEnd(float maxSeconds = 20f)
        {
            var guard = 0f;
            while (!AllSettled && guard < maxSeconds)
            {
                Step(1f / 60f);
                guard += 1f / 60f;
            }

            return AllSettled;
        }

        /// <summary>A stable seed for a specimen's granules ("rice/brown_spot#03" always pours the same way).</summary>
        public static int SeedFor(string specimenId, int applyCount)
        {
            unchecked
            {
                var h = 17 + applyCount * 7919;
                foreach (var c in specimenId ?? "")
                    h = h * 31 + c;
                return h & 0x7FFFFFFF;
            }
        }
    }
}
