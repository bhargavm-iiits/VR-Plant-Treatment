using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>Builds round tubes along paths (the water hose, the gooseneck outlet, the water column).</summary>
    public static class TubeMesh
    {
        /// <summary>A cubic Bezier sampled into <paramref name="samples"/> points.</summary>
        public static List<Vector3> Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int samples)
        {
            var points = new List<Vector3>(samples);
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)(samples - 1);
                var u = 1f - t;
                points.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d);
            }

            return points;
        }

        /// <summary>
        /// Fills <paramref name="mesh"/> with a tube through world-space <paramref name="points"/> (expressed in
        /// <paramref name="space"/>), radius from <paramref name="startRadius"/> to <paramref name="endRadius"/>, capped ends.
        /// </summary>
        public static void Fill(Mesh mesh, Transform space, IReadOnlyList<Vector3> points, float startRadius, float endRadius, int sides = 10)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            mesh.Clear();
            if (points == null || points.Count < 2)
                return;
            var previousSide = Vector3.zero;
            for (var i = 0; i < points.Count; i++)
            {
                var forward = i < points.Count - 1 ? points[i + 1] - points[i] : points[i] - points[i - 1];
                forward = forward.sqrMagnitude > 1e-10f ? forward.normalized : Vector3.down;
                // Parallel transport keeps the tube from twisting.
                var side = previousSide.sqrMagnitude > 0.5f ? Vector3.ProjectOnPlane(previousSide, forward) : Vector3.Cross(forward, Vector3.up);
                if (side.sqrMagnitude < 1e-6f)
                    side = Vector3.Cross(forward, Vector3.right);
                side.Normalize();
                previousSide = side;
                var up = Vector3.Cross(side, forward);
                var radius = Mathf.Lerp(startRadius, endRadius, i / (float)(points.Count - 1));
                for (var s = 0; s < sides; s++)
                {
                    var angle = s * Mathf.PI * 2f / sides;
                    var normal = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    vertices.Add(space.InverseTransformPoint(points[i] + normal * radius));
                    normals.Add(space.InverseTransformDirection(normal));
                }
            }

            for (var i = 0; i < points.Count - 1; i++)
            for (var s = 0; s < sides; s++)
            {
                var a = i * sides + s;
                var b = i * sides + (s + 1) % sides;
                triangles.AddRange(new[] { a, b, a + sides, b, b + sides, a + sides });
            }

            // End caps.
            foreach (var (index, sign) in new[] { (0, -1f), (points.Count - 1, 1f) })
            {
                var centre = vertices.Count;
                vertices.Add(space.InverseTransformPoint(points[index]));
                var axis = index == 0 ? points[0] - points[1] : points[index] - points[index - 1];
                normals.Add(space.InverseTransformDirection(axis.normalized));
                for (var s = 0; s < sides; s++)
                {
                    var a = index * sides + s;
                    var b = index * sides + (s + 1) % sides;
                    if (sign < 0f)
                        triangles.AddRange(new[] { centre, b, a });
                    else
                        triangles.AddRange(new[] { centre, a, b });
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
