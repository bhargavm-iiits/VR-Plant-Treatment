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

        /// <summary>
        /// A closed ring of low, rolling hills (the Farm's hazy horizon). Unlike <see cref="MountainRing"/>, the
        /// noise is sampled on a circle, so the first and last segments meet and the ring has no seam or gap.
        /// Four rows per segment: a buried inner foot, a front shoulder, the ridge and a lower back edge.
        /// UV.y is the normalised height for the colour gradient. Rebuilt in place on every call.
        /// </summary>
        public static Mesh HillRing(string name, float innerRadius, float outerRadius, float minHeight, float maxHeight,
            int seed, int segments = 192)
        {
            return Replace(name, () =>
            {
                var random = new System.Random(seed);
                var offset = new Vector2((float)random.NextDouble() * 100f, (float)random.NextDouble() * 100f);

                // Periodic noise in [0, 1]: Perlin noise read around a circle of the given radius (frequency).
                float Loop(float t, float frequency, float phase)
                {
                    var angle = t * Mathf.PI * 2f;
                    return Mathf.PerlinNoise(offset.x + phase + Mathf.Cos(angle) * frequency, offset.y + phase + Mathf.Sin(angle) * frequency);
                }

                const int rows = 4;
                var vertices = new Vector3[segments * rows];
                var uvs = new Vector2[vertices.Length];
                for (var i = 0; i < segments; i++)
                {
                    var t = (float)i / segments;
                    var direction = new Vector3(Mathf.Sin(t * Mathf.PI * 2f), 0f, Mathf.Cos(t * Mathf.PI * 2f));
                    var shape = Loop(t, 1.6f, 0f) * 0.55f + Loop(t, 4.5f, 17f) * 0.3f + Loop(t, 11f, 41f) * 0.15f;
                    var height = Mathf.Lerp(minHeight, maxHeight, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, shape)));
                    var ridge = Mathf.Lerp(innerRadius, outerRadius, 0.45f + (Loop(t, 3f, 63f) - 0.5f) * 0.4f);
                    var radii = new[] { innerRadius, Mathf.Lerp(innerRadius, ridge, 0.55f), ridge, outerRadius };
                    var heights = new[] { -3f, height * (0.45f + Loop(t, 6f, 87f) * 0.2f), height, height * 0.45f };
                    for (var r = 0; r < rows; r++)
                    {
                        vertices[i * rows + r] = direction * radii[r] + Vector3.up * heights[r];
                        uvs[i * rows + r] = new Vector2(t * 8f, Mathf.Clamp01(heights[r] / maxHeight));
                    }
                }

                // Faces point inward (clockwise as seen from the centre); the last segment wraps to the first.
                var triangles = new int[segments * (rows - 1) * 6];
                var k = 0;
                for (var i = 0; i < segments; i++)
                {
                    var next = (i + 1) % segments;
                    for (var r = 0; r < rows - 1; r++)
                    {
                        int a = i * rows + r, b = a + 1, c = next * rows + r, d = c + 1;
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

        /// <summary>
        /// Flat, upward-facing quad of <paramref name="size"/> (X by Z metres) centred on its origin, with the UVs
        /// spanning <paramref name="uv"/> (x across X, y across Z) and tangents for normal maps. Rebuilt in place.
        /// </summary>
        public static Mesh GroundQuad(string name, Vector2 size, Rect uv)
        {
            return Replace(name, () =>
            {
                float x = size.x * 0.5f, z = size.y * 0.5f;
                var mesh = new Mesh
                {
                    name = name,
                    vertices = new[] { new Vector3(-x, 0f, -z), new Vector3(x, 0f, -z), new Vector3(-x, 0f, z), new Vector3(x, 0f, z) },
                    uv = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax) },
                    normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                    // Clockwise seen from above, so the quad faces up.
                    triangles = new[] { 0, 2, 1, 2, 3, 1 },
                };
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        /// <summary>
        /// A greenhouse skin: the cross-section <paramref name="outline"/> (X, Y; from the left eave corner over the
        /// roof to the right eave corner, X increasing) drops to the ground at both ends to make the side walls, and is
        /// extruded from <paramref name="minZ"/> to <paramref name="maxZ"/>. Both gable ends are capped with the same
        /// outline, minus a doorway of <paramref name="door"/> (width, height) centred on X = 0. Flat normals point
        /// outwards (render it double-sided); UVs are in metres (U along the length or across the gable, V up the
        /// profile). Rebuilt in place.
        /// </summary>
        public static Mesh GreenhouseCover(string name, Vector2[] outline, float minZ, float maxZ, Vector2 door)
        {
            return Replace(name, () =>
            {
                var vertices = new System.Collections.Generic.List<Vector3>();
                var normals = new System.Collections.Generic.List<Vector3>();
                var uvs = new System.Collections.Generic.List<Vector2>();
                var triangles = new System.Collections.Generic.List<int>();

                // A quad a-b-c-d (in order around it), wound so that its front faces along normal n.
                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
                {
                    var start = vertices.Count;
                    vertices.AddRange(new[] { a, b, c, d });
                    normals.AddRange(new[] { n, n, n, n });
                    uvs.AddRange(new[] { ua, ub, uc, ud });
                    // Unity's front faces are clockwise as seen from the front (cross(b - a, c - a) points at the viewer).
                    var front = Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0f;
                    triangles.AddRange(front
                        ? new[] { start, start + 1, start + 2, start, start + 2, start + 3 }
                        : new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                }

                // Walls and roof: the outline with both walls down to the ground, extruded along Z.
                var profile = new System.Collections.Generic.List<Vector2> { new Vector2(outline[0].x, 0f) };
                profile.AddRange(outline);
                profile.Add(new Vector2(outline[outline.Length - 1].x, 0f));
                var arc = 0f;
                for (var i = 0; i < profile.Count - 1; i++)
                {
                    Vector2 p = profile[i], q = profile[i + 1];
                    var length = Vector2.Distance(p, q);
                    if (length < 1e-4f)
                        continue;
                    var along = (q - p) / length;
                    var normal = new Vector3(-along.y, along.x, 0f);
                    Quad(new Vector3(p.x, p.y, minZ), new Vector3(q.x, q.y, minZ), new Vector3(q.x, q.y, maxZ), new Vector3(p.x, p.y, maxZ), normal,
                        new Vector2(minZ, arc), new Vector2(minZ, arc + length), new Vector2(maxZ, arc + length), new Vector2(maxZ, arc));
                    arc += length;
                }

                // Gable ends: vertical strips between the outline's corners and the doorway's jambs.
                float Top(float x)
                {
                    for (var i = 0; i < outline.Length - 1; i++)
                    {
                        if (x <= outline[i + 1].x)
                            return Mathf.Lerp(outline[i].y, outline[i + 1].y, Mathf.InverseLerp(outline[i].x, outline[i + 1].x, x));
                    }

                    return outline[outline.Length - 1].y;
                }

                var halfDoor = door.x * 0.5f;
                var columns = new System.Collections.Generic.SortedSet<float> { -halfDoor, halfDoor };
                foreach (var point in outline)
                    columns.Add(point.x);
                var xs = new System.Collections.Generic.List<float>(columns);
                foreach (var (z, facing) in new[] { (minZ, -1f), (maxZ, 1f) })
                {
                    var normal = new Vector3(0f, 0f, facing);
                    for (var i = 0; i < xs.Count - 1; i++)
                    {
                        float x0 = xs[i], x1 = xs[i + 1];
                        if (x1 - x0 < 1e-4f)
                            continue;
                        var bottom = Mathf.Abs((x0 + x1) * 0.5f) < halfDoor ? door.y : 0f;
                        float y0 = Top(x0), y1 = Top(x1);
                        Quad(new Vector3(x0, bottom, z), new Vector3(x1, bottom, z), new Vector3(x1, y1, z), new Vector3(x0, y0, z), normal,
                            new Vector2(x0, bottom), new Vector2(x1, bottom), new Vector2(x1, y1), new Vector2(x0, y0));
                    }
                }

                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        /// <summary>Axis-aligned boxes (flat-shaded, UVs in metres) merged into one mesh. Rebuilt in place.</summary>
        public static Mesh Boxes(string name, Bounds[] boxes)
        {
            return Replace(name, () =>
            {
                var vertices = new System.Collections.Generic.List<Vector3>();
                var normals = new System.Collections.Generic.List<Vector3>();
                var uvs = new System.Collections.Generic.List<Vector2>();
                var triangles = new System.Collections.Generic.List<int>();
                var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var box in boxes)
                {
                    foreach (var n in axes)
                    {
                        // Two unit axes u, w spanning the face; the winding is fixed up below from the normal.
                        var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                        var w = Vector3.Cross(n, u);
                        u = Vector3.Cross(w, n);
                        var centre = box.center + Vector3.Scale(n, box.extents);
                        var eu = Mathf.Abs(Vector3.Dot(u, box.extents));
                        var ew = Mathf.Abs(Vector3.Dot(w, box.extents));
                        var start = vertices.Count;
                        foreach (var (su, sw) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                        {
                            var p = centre + u * (su * eu) + w * (sw * ew);
                            vertices.Add(p);
                            normals.Add(n);
                            uvs.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w)));
                        }

                        var a = vertices[start];
                        var front = Vector3.Dot(Vector3.Cross(vertices[start + 1] - a, vertices[start + 2] - a), n) > 0f;
                        triangles.AddRange(front
                            ? new[] { start, start + 1, start + 2, start, start + 2, start + 3 }
                            : new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                    }
                }

                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateTangents();
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

        /// <summary>Like GetOrCreate, but an existing asset is overwritten with the new data (same GUID).</summary>
        static Mesh Replace(string name, System.Func<Mesh> build)
        {
            var path = $"{ProjectPaths.Meshes}/{name}.asset";
            var mesh = build();
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                ProjectPaths.EnsureFolder(ProjectPaths.Meshes);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
