using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Editor
{
    /// <summary>
    /// Replaces a sub-mesh of separate berries (one closed mesh island per berry) with sphere impostors for
    /// BTP/Berry Impostor: one quad per berry that the shader turns to the viewer and clips to the berry's exact
    /// round or oval outline, with per-pixel normals. Two triangles per berry instead of hundreds, and, unlike a
    /// decimated mesh, never faceted. Each island is fitted with a spheroid: centre, long axis (principal
    /// component), equatorial radius and polar half-length.
    /// </summary>
    static class BerryImpostors
    {
        public const string Shader = "BTP/Berry Impostor";

        public struct Berry
        {
            public Vector3 Centre;
            public Vector3 Axis;
            public float Radius;
            public float HalfLength;
        }

        public struct Result
        {
            public Mesh Mesh;
            public int Count;
            public float MinRadius, MaxRadius, MeanElongation;
        }

        /// <summary>
        /// The berries of <paramref name="source"/>'s sub-mesh <paramref name="submesh"/> as an impostor mesh saved at
        /// <paramref name="path"/> (rebuilt in place), in the source mesh's space.
        /// </summary>
        public static Result Build(Mesh source, int submesh, string name, string path)
        {
            var berries = Fit(source, submesh);
            if (berries.Count == 0)
                throw new InvalidOperationException($"{source.name}: sub-mesh {submesh} has no berries.");
            var mesh = CreateMesh(berries, name);
            var result = new Result { Mesh = DiseaseVariantBuilder.SaveMesh(mesh, path), Count = berries.Count, MinRadius = float.MaxValue };
            var elongation = 0f;
            foreach (var berry in berries)
            {
                result.MinRadius = Mathf.Min(result.MinRadius, berry.Radius);
                result.MaxRadius = Mathf.Max(result.MaxRadius, berry.Radius);
                elongation += berry.HalfLength / berry.Radius;
            }

            result.MeanElongation = elongation / berries.Count;
            return result;
        }

        /// <summary>One spheroid per connected island (vertices welded by position, so UV seams do not split a berry).</summary>
        public static List<Berry> Fit(Mesh source, int submesh)
        {
            var vertices = source.vertices;
            var triangles = source.GetTriangles(submesh);

            // Weld by position (0.01 mm), then union the corners of every triangle.
            var welded = new Dictionary<Vector3Int, int>();
            var weldOf = new int[vertices.Length];
            for (var i = 0; i < weldOf.Length; i++)
                weldOf[i] = -1;
            var positions = new List<Vector3>();
            int Weld(int vertex)
            {
                if (weldOf[vertex] >= 0)
                    return weldOf[vertex];
                var v = vertices[vertex];
                var key = new Vector3Int(Mathf.RoundToInt(v.x * 1e5f), Mathf.RoundToInt(v.y * 1e5f), Mathf.RoundToInt(v.z * 1e5f));
                if (!welded.TryGetValue(key, out var index))
                {
                    index = welded[key] = positions.Count;
                    positions.Add(v);
                }

                return weldOf[vertex] = index;
            }

            for (var t = 0; t < triangles.Length; t++)
                triangles[t] = Weld(triangles[t]);
            var parent = new int[positions.Count];
            for (var i = 0; i < parent.Length; i++)
                parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x)
                    x = parent[x] = parent[parent[x]];
                return x;
            }

            for (var t = 0; t < triangles.Length; t += 3)
            {
                var a = Find(triangles[t]);
                parent[Find(triangles[t + 1])] = a;
                parent[Find(triangles[t + 2])] = a;
            }

            // Islands in order of first appearance (deterministic).
            var islandOf = new Dictionary<int, int>();
            var islands = new List<List<int>>();
            var used = new bool[positions.Count];
            foreach (var corner in triangles)
            {
                if (used[corner])
                    continue;
                used[corner] = true;
                var root = Find(corner);
                if (!islandOf.TryGetValue(root, out var island))
                {
                    island = islandOf[root] = islands.Count;
                    islands.Add(new List<int>());
                }

                islands[island].Add(corner);
            }

            var berries = new List<Berry>(islands.Count);
            foreach (var island in islands)
            {
                if (island.Count < 4)
                    continue;
                berries.Add(FitSpheroid(island, positions));
            }

            return berries;
        }

        static Berry FitSpheroid(List<int> island, List<Vector3> positions)
        {
            var mean = Vector3.zero;
            foreach (var i in island)
                mean += positions[i];
            mean /= island.Count;

            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var i in island)
            {
                var p = positions[i] - mean;
                xx += p.x * p.x; xy += p.x * p.y; xz += p.x * p.z;
                yy += p.y * p.y; yz += p.y * p.z; zz += p.z * p.z;
            }

            // Long axis: the principal component (power iteration on the covariance).
            var axis = new Vector3(0.31f, 0.89f, 0.33f).normalized;
            for (var iteration = 0; iteration < 64; iteration++)
            {
                var next = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z, xy * axis.x + yy * axis.y + yz * axis.z, xz * axis.x + yz * axis.y + zz * axis.z);
                if (next.sqrMagnitude < 1e-20f)
                    break;
                axis = next.normalized;
            }

            float low = float.MaxValue, high = float.MinValue, radius = 0f;
            foreach (var i in island)
            {
                var p = positions[i] - mean;
                var along = Vector3.Dot(p, axis);
                low = Mathf.Min(low, along);
                high = Mathf.Max(high, along);
                radius = Mathf.Max(radius, (p - axis * along).magnitude);
            }

            var halfLength = (high - low) * 0.5f;
            // A nearly round island: its "long axis" is noise; a sphere of the mean size is the honest fit.
            if (halfLength < radius * 1.02f)
                halfLength = radius = Mathf.Max(radius, halfLength);
            return new Berry { Centre = mean + axis * ((high + low) * 0.5f), Axis = axis, Radius = radius, HalfLength = halfLength };
        }

        /// <summary>Four vertices per berry: centre (POSITION), long axis (NORMAL), corner and size (TEXCOORD0).</summary>
        public static Mesh CreateMesh(List<Berry> berries, string name)
        {
            var count = berries.Count;
            var positions = new Vector3[count * 4];
            var normals = new Vector3[count * 4];
            var uvs = new List<Vector4>(count * 4);
            var indices = new int[count * 6];
            var corners = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            var min = Vector3.one * float.MaxValue;
            var max = Vector3.one * float.MinValue;
            for (var b = 0; b < count; b++)
            {
                var berry = berries[b];
                for (var c = 0; c < 4; c++)
                {
                    positions[b * 4 + c] = berry.Centre;
                    normals[b * 4 + c] = berry.Axis;
                    uvs.Add(new Vector4(corners[c].x, corners[c].y, berry.Radius, berry.HalfLength));
                }

                indices[b * 6 + 0] = b * 4 + 0;
                indices[b * 6 + 1] = b * 4 + 2;
                indices[b * 6 + 2] = b * 4 + 1;
                indices[b * 6 + 3] = b * 4 + 0;
                indices[b * 6 + 4] = b * 4 + 3;
                indices[b * 6 + 5] = b * 4 + 2;
                // The quad covers the berry seen from any side (1.15: the shader's anti-aliasing margin).
                var extent = Vector3.one * (Mathf.Max(berry.Radius, berry.HalfLength) * 1.15f);
                min = Vector3.Min(min, berry.Centre - extent);
                max = Vector3.Max(max, berry.Centre + extent);
            }

            var mesh = new Mesh { name = name, indexFormat = positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
            return mesh;
        }
    }
}
