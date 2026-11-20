using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Builds the branching root system seen through the soil tray's front window: tapered four-sided tubes that fan out
    /// and down from under the stem, in a thin layer just behind the window (a rhizotron view). Deterministic per seed and
    /// scaled to the tray (width, soil depth) and the plant's size. Mesh space: origin at the stem on the soil surface,
    /// X across the window, Y up, Z away from the window; the layer lies between <c>front</c> and <c>front + depth</c>.
    /// </summary>
    public static class ProceduralRoots
    {
        public struct Settings
        {
            /// <summary>Half the soil width available to the roots.</summary>
            public float HalfWidth;
            /// <summary>Soil depth below the surface.</summary>
            public float Depth;
            /// <summary>Z of the window layer's front (towards the viewer is -Z).</summary>
            public float Front;
            /// <summary>Thickness of the visible root layer.</summary>
            public float Layer;
            /// <summary>0..1: bigger plants get more and thicker roots.</summary>
            public float Vigour;
            public int Seed;
        }

        sealed class Builder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();

            /// <summary>A tapered square tube from a to b.</summary>
            public void Segment(Vector3 a, Vector3 b, float ra, float rb)
            {
                var axis = b - a;
                if (axis.sqrMagnitude < 1e-10f)
                    return;
                var forward = axis.normalized;
                var side = Vector3.Cross(forward, Vector3.forward);
                if (side.sqrMagnitude < 1e-6f)
                    side = Vector3.Cross(forward, Vector3.right);
                side.Normalize();
                var up = Vector3.Cross(side, forward).normalized;
                var origin = Vertices.Count;
                var directions = new[] { side, up, -side, -up };
                foreach (var d in directions)
                {
                    Vertices.Add(a + d * ra);
                    Normals.Add(d);
                }

                foreach (var d in directions)
                {
                    Vertices.Add(b + d * rb);
                    Normals.Add(d);
                }

                for (var i = 0; i < 4; i++)
                {
                    var j = (i + 1) % 4;
                    Triangles.AddRange(new[] { origin + i, origin + 4 + i, origin + 4 + j, origin + i, origin + 4 + j, origin + j });
                }
            }
        }

        public static Mesh Build(Settings settings, string name = "ProceduralRoots")
        {
            var builder = new Builder();
            var random = new System.Random(settings.Seed);
            float Next() => (float)random.NextDouble();
            var vigour = Mathf.Clamp01(settings.Vigour);
            var halfWidth = Mathf.Max(0.05f, settings.HalfWidth);
            var depth = Mathf.Max(0.04f, settings.Depth);
            var layer = Mathf.Max(0.004f, settings.Layer);
            var mainRoots = 13 + Mathf.RoundToInt(vigour * 8f);
            var baseRadius = Mathf.Lerp(0.0042f, 0.008f, vigour) * Mathf.Clamp(depth / 0.2f, 0.6f, 1.6f);
            var segments = 0;

            Vector3 Clamp(Vector3 p)
            {
                p.x = Mathf.Clamp(p.x, -halfWidth + 0.006f, halfWidth - 0.006f);
                p.y = Mathf.Clamp(p.y, -depth + 0.006f, -0.004f);
                p.z = Mathf.Clamp(p.z, settings.Front + 0.001f, settings.Front + layer);
                return p;
            }

            void Grow(Vector3 start, Vector3 direction, float length, float radius, int level)
            {
                var steps = level == 0 ? 7 : level == 1 ? 5 : 3;
                var position = start;
                var dir = direction.normalized;
                for (var s = 0; s < steps && segments < 2400; s++)
                {
                    // Roots wander and bend downwards as they grow.
                    dir = (dir + new Vector3((Next() - 0.5f) * 0.7f, -0.25f - Next() * 0.25f, (Next() - 0.5f) * 0.3f)).normalized;
                    var next = Clamp(position + dir * (length / steps));
                    var r0 = radius * (1f - s / (float)steps * 0.6f);
                    var r1 = radius * (1f - (s + 1) / (float)steps * 0.6f);
                    builder.Segment(position, next, r0, Mathf.Max(r1, 0.0008f));
                    segments++;
                    if (level < 3 && Next() < (level == 0 ? 0.9f : level == 1 ? 0.7f : 0.45f))
                    {
                        var branch = new Vector3(dir.x + (Next() < 0.5f ? -1f : 1f) * (0.6f + Next() * 0.6f), dir.y * 0.6f - 0.2f, (Next() - 0.5f) * 0.2f);
                        Grow(next, branch, length * (0.35f + Next() * 0.25f), r1 * 0.65f, level + 1);
                    }

                    position = next;
                }
            }

            // The tap root and the fibrous main roots fan out from just under the stem.
            var crown = new Vector3(0f, -0.006f, settings.Front + layer * 0.4f);
            Grow(crown, Vector3.down, depth * 0.9f, baseRadius * 1.25f, 0);
            for (var i = 0; i < mainRoots; i++)
            {
                var t = mainRoots > 1 ? i / (float)(mainRoots - 1) : 0.5f;
                var spread = Mathf.Lerp(-1f, 1f, t) + (Next() - 0.5f) * 0.25f;
                var direction = new Vector3(spread * 1.3f, -0.6f - Next() * 0.5f, (Next() - 0.5f) * 0.4f);
                var length = Mathf.Lerp(depth * 0.9f, Mathf.Max(depth, halfWidth) * 1.2f, Next()) * (0.75f + vigour * 0.4f);
                Grow(crown + new Vector3((Next() - 0.5f) * 0.01f, 0f, 0f), direction, length, baseRadius * (0.7f + Next() * 0.3f), 0);
            }

            var mesh = new Mesh { name = name };
            if (builder.Vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(builder.Vertices);
            mesh.SetNormals(builder.Normals);
            mesh.SetTriangles(builder.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A stable seed from a specimen ID, so a specimen always shows the same roots.</summary>
        public static int SeedFor(string specimenId)
        {
            unchecked
            {
                var h = 31;
                foreach (var c in specimenId ?? "")
                    h = h * 131 + c;
                return h & 0x7FFFFFFF;
            }
        }
    }
}
