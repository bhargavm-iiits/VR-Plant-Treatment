using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The fertiliser granule meshes drawn by <see cref="GranuleRenderer"/> (BTP/Fertiliser Granule shader). One mesh per
    /// LOD carries all <see cref="Variants"/> granule shapes at once, so every granule on screen is one instanced draw per
    /// LOD: POSITION is the unit direction of each vertex; TEXCOORD0/1 hold the vertex's radius in variants 0-3 / 4-7 and
    /// TEXCOORD2..7 the 8 variants' normals (24 floats, variant k at floats 3k..3k+2). The shader picks the instance's
    /// variant. Near: a subdivided icosahedron (42 vertices, 80 triangles); far: the icosahedron (12 vertices, 20 triangles).
    /// Shapes (deterministic): 0-1 near-spherical prills (urea), 2-4 rounded granules (compound NPK, DAP), 5-7 angular
    /// crystals (potash), each normalised to a mean radius of 1.
    /// </summary>
    public static class GranuleMeshes
    {
        public const int Variants = 8;
        public const int NearTriangles = 80, FarTriangles = 20;

        static Mesh near, far;

        public static Mesh Near => near != null ? near : near = Build(1, "GranuleNear");
        public static Mesh Far => far != null ? far : far = Build(0, "GranuleFar");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            near = null;
            far = null;
        }

        /// <summary>The radius (mean 1) of <paramref name="variant"/> towards unit direction <paramref name="d"/>, before normalising.</summary>
        public static float RawRadius(int variant, Vector3 d)
        {
            var seed = 101 + variant * 37;
            if (variant >= 5)
                return CrystalRadius(seed, d);
            // Prills: almost round; granules: lumpy agglomerates, mildly ellipsoidal.
            var prill = variant < 2;
            var squash = prill ? 0.05f : 0.14f;
            var ax = 1f + (Hash01(seed, 1) - 0.5f) * 2f * squash;
            var ay = 1f + (Hash01(seed, 2) - 0.5f) * 2f * squash;
            var az = 1f + (Hash01(seed, 3) - 0.5f) * 2f * squash;
            var e = 1f / Mathf.Sqrt(d.x * d.x / (ax * ax) + d.y * d.y / (ay * ay) + d.z * d.z / (az * az));
            var offset = new Vector3(Hash01(seed, 4), Hash01(seed, 5), Hash01(seed, 6)) * 17f;
            var amp = prill ? 0.035f : 0.11f;
            var n = Noise(d * (prill ? 1.6f : 2.3f) + offset) * 0.7f + Noise(d * 4.6f + offset * 1.7f) * 0.3f;
            return e * (1f + amp * (n * 2f - 1f));
        }

        /// <summary>A random convex polyhedron (cleaved crystal): the nearest of 9 random planes, capped.</summary>
        static float CrystalRadius(int seed, Vector3 d)
        {
            var r = 1.25f;
            for (var k = 0; k < 9; k++)
            {
                var n = PlaneNormal(seed, k);
                var dot = Vector3.Dot(d, n);
                if (dot <= 1e-3f)
                    continue;
                var distance = 0.78f + Hash01(seed, 40 + k) * 0.22f;
                r = Mathf.Min(r, distance / dot);
            }

            return r;
        }

        static Vector3 PlaneNormal(int seed, int k)
        {
            // Uniform on the sphere; the first six roughly along the axes so every crystal is closed.
            var z = Hash01(seed, 10 + k) * 2f - 1f;
            var a = Hash01(seed, 20 + k) * Mathf.PI * 2f;
            var s = Mathf.Sqrt(1f - z * z);
            var random = new Vector3(s * Mathf.Cos(a), z, s * Mathf.Sin(a));
            if (k < 6)
            {
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                return (axes[k] + random * 0.55f).normalized;
            }

            return random;
        }

        /// <summary>The crystal's facet normal at <paramref name="d"/> (the plane that bounds it there), or zero.</summary>
        static Vector3 CrystalFacet(int seed, Vector3 d)
        {
            var best = 1.25f;
            var normal = Vector3.zero;
            for (var k = 0; k < 9; k++)
            {
                var n = PlaneNormal(seed, k);
                var dot = Vector3.Dot(d, n);
                if (dot <= 1e-3f)
                    continue;
                var r = (0.78f + Hash01(seed, 40 + k) * 0.22f) / dot;
                if (r < best)
                {
                    best = r;
                    normal = n;
                }
            }

            return normal;
        }

        static Mesh Build(int subdivisions, string name)
        {
            Icosphere(subdivisions, out var dirs, out var triangles);
            var count = dirs.Count;
            var radii = new float[Variants, count];
            var normals = new Vector3[Variants, count];
            for (var v = 0; v < Variants; v++)
            {
                var mean = 0f;
                for (var i = 0; i < count; i++)
                {
                    radii[v, i] = RawRadius(v, dirs[i]);
                    mean += radii[v, i];
                }

                mean /= count;
                var accumulated = new Vector3[count];
                for (var i = 0; i < count; i++)
                    radii[v, i] /= mean;
                for (var t = 0; t < triangles.Count; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    var pa = dirs[a] * radii[v, a];
                    var face = Vector3.Cross(dirs[b] * radii[v, b] - pa, dirs[c] * radii[v, c] - pa);
                    accumulated[a] += face;
                    accumulated[b] += face;
                    accumulated[c] += face;
                }

                for (var i = 0; i < count; i++)
                {
                    var n = accumulated[i].sqrMagnitude > 1e-12f ? accumulated[i].normalized : dirs[i];
                    if (v >= 5)
                    {
                        // Crystals: lean towards the facet normal so the cleaved faces read flat.
                        var facet = CrystalFacet(101 + v * 37, dirs[i]);
                        if (facet != Vector3.zero)
                            n = Vector3.Lerp(n, facet, 0.65f).normalized;
                    }

                    normals[v, i] = n;
                }
            }

            var positions = new List<Vector3>(dirs);
            var channels = new List<Vector4>[8];
            for (var c = 0; c < 8; c++)
                channels[c] = new List<Vector4>(count);
            for (var i = 0; i < count; i++)
            {
                channels[0].Add(new Vector4(radii[0, i], radii[1, i], radii[2, i], radii[3, i]));
                channels[1].Add(new Vector4(radii[4, i], radii[5, i], radii[6, i], radii[7, i]));
                var flat = new float[24];
                for (var v = 0; v < Variants; v++)
                {
                    flat[v * 3] = normals[v, i].x;
                    flat[v * 3 + 1] = normals[v, i].y;
                    flat[v * 3 + 2] = normals[v, i].z;
                }

                for (var c = 0; c < 6; c++)
                    channels[2 + c].Add(new Vector4(flat[c * 4], flat[c * 4 + 1], flat[c * 4 + 2], flat[c * 4 + 3]));
            }

            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(positions);
            // Unity's mesh normals (variant 0) for tools that read them; the shader uses the TEXCOORD normals.
            var normal0 = new List<Vector3>(count);
            for (var i = 0; i < count; i++)
                normal0.Add(normals[0, i]);
            mesh.SetNormals(normal0);
            for (var c = 0; c < 8; c++)
                mesh.SetUVs(c, channels[c]);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
            mesh.UploadMeshData(true);
            return mesh;
        }

        static void Icosphere(int subdivisions, out List<Vector3> vertices, out List<int> triangles)
        {
            var t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            vertices = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (var i = 0; i < vertices.Count; i++)
                vertices[i] = vertices[i].normalized;
            triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (var s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int>(triangles.Count * 4);
                var verts = vertices;
                int Mid(int a, int b)
                {
                    var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out var index))
                        return index;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    cache[key] = verts.Count - 1;
                    return verts.Count - 1;
                }

                for (var i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }

                triangles = next;
            }
        }

        // ------------------------------------------------------------------ deterministic noise

        /// <summary>A hash in [0, 1) of two integers (stable across platforms).</summary>
        public static float Hash01(int a, int b) => (Hash((uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u) & 0xFFFFFF) / (float)0x1000000;

        public static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        static float Lattice(int x, int y, int z) => (Hash((uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u) & 0xFFFF) / 65535f;

        /// <summary>Smooth 3D value noise in [0, 1].</summary>
        public static float Noise(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            var a = Mathf.Lerp(Mathf.Lerp(Lattice(x, y, z), Lattice(x + 1, y, z), fx), Mathf.Lerp(Lattice(x, y + 1, z), Lattice(x + 1, y + 1, z), fx), fy);
            var b = Mathf.Lerp(Mathf.Lerp(Lattice(x, y, z + 1), Lattice(x + 1, y, z + 1), fx),
                Mathf.Lerp(Lattice(x, y + 1, z + 1), Lattice(x + 1, y + 1, z + 1), fx), fy);
            return Mathf.Lerp(a, b, fz);
        }
    }
}
