using BTP.UI;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>Generated meshes for the environments, saved under Assets/_Project/Models/Generated.</summary>
    static class ProceduralMeshes
    {
        /// <summary>
        /// A ring of low-poly mountains around the scene. UV.y is the normalised height so the
        /// gradient texture shades forest, rock and snow; distance fog adds the blue haze.
        /// </summary>
        public static Mesh MountainRing(string name, float innerRadius, float outerRadius, float minHeight, float maxHeight, int seed)
        {
            return GetOrCreate(name, () =>
            {
                const int segments = 160;
                var random = new System.Random(seed);
                var phase = (float)random.NextDouble() * 100f;
                var ridge = (innerRadius + outerRadius) * 0.5f;
                var radii = new[] { innerRadius, ridge, outerRadius };

                var vertices = new Vector3[(segments + 1) * 3];
                var uvs = new Vector2[vertices.Length];
                for (var i = 0; i <= segments; i++)
                {
                    var t = (float)i / segments;
                    var angle = t * Mathf.PI * 2f;
                    var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    var peak = Mathf.PerlinNoise(phase + t * 9f, 0.3f) * 0.65f + Mathf.PerlinNoise(phase + t * 31f, 0.7f) * 0.35f;
                    var height = Mathf.Lerp(minHeight, maxHeight, Mathf.Pow(peak, 1.6f));
                    var wobble = (Mathf.PerlinNoise(phase + t * 17f, 2.1f) - 0.5f) * (outerRadius - innerRadius) * 0.3f;
                    var heights = new[] { -2f, height, height * 0.55f };
                    for (var r = 0; r < 3; r++)
                    {
                        var radius = radii[r] + (r == 1 ? wobble : 0f);
                        vertices[i * 3 + r] = direction * radius + Vector3.up * heights[r];
                        uvs[i * 3 + r] = new Vector2(t, Mathf.Clamp01(heights[r] / maxHeight));
                    }
                }

                var triangles = new int[segments * 2 * 6];
                var k = 0;
                for (var i = 0; i < segments; i++)
                {
                    for (var r = 0; r < 2; r++)
                    {
                        int a = i * 3 + r, b = a + 1, c = a + 3, d = a + 4;
                        triangles[k++] = a; triangles[k++] = b; triangles[k++] = c;
                        triangles[k++] = b; triangles[k++] = d; triangles[k++] = c;
                    }
                }

                var mesh = new Mesh { name = name, vertices = vertices, uv = uvs, triangles = triangles };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        public static Mesh Cone(string name, float radius, float height, int segments = 24)
        {
            return GetOrCreate(name, () =>
            {
                var vertices = new Vector3[segments * 3];
                var triangles = new int[segments * 3];
                for (var i = 0; i < segments; i++)
                {
                    var a0 = i * Mathf.PI * 2f / segments;
                    var a1 = (i + 1) * Mathf.PI * 2f / segments;
                    vertices[i * 3] = new Vector3(Mathf.Sin(a0) * radius, 0f, Mathf.Cos(a0) * radius);
                    vertices[i * 3 + 1] = new Vector3(0f, height, 0f);
                    vertices[i * 3 + 2] = new Vector3(Mathf.Sin(a1) * radius, 0f, Mathf.Cos(a1) * radius);
                    // Base, next base, apex: clockwise seen from outside, so faces point outward.
                    triangles[i * 3] = i * 3;
                    triangles[i * 3 + 1] = i * 3 + 2;
                    triangles[i * 3 + 2] = i * 3 + 1;
                }

                var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        public static Mesh Ring(string name, float innerRadius, float outerRadius) =>
            GetOrCreate(name, () => RingMesh.Build(innerRadius, outerRadius));

        static Mesh GetOrCreate(string name, System.Func<Mesh> build)
        {
            var path = $"{ProjectPaths.Meshes}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                return existing;
            ProjectPaths.EnsureFolder(ProjectPaths.Meshes);
            var mesh = build();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
