using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Building blocks for <see cref="TreatmentLabDressing"/>: a small shared material palette (realistic PBR surfaces, see
    /// TreatmentLabDressing.Real.cs), low-poly generated meshes (bevelled boxes, lathed bottles and glassware) stored as assets,
    /// 3D TextMeshPro labels and signs, and the supplied models (spray bottle FBX, gardening tools, gloves and pruning shears
    /// GLBs) brought to real-world size.
    /// Everything is a plain static renderer: no colliders unless asked for, no scripts.
    /// </summary>
    public static partial class TreatmentLabDressing
    {
        const string MaterialFolder = ProjectPaths.Root + "/Materials/Treatment/Dressing";
        const string MeshFolder = ProjectPaths.Meshes + "/LabDressing";
        const StaticEditorFlags PropFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;

        // ------------------------------------------------------------------ palette

        /// <summary>The dressing's shared materials (one draw-call family each; static batching merges per material).</summary>
        sealed class Palette
        {
            public Material Stainless, Painted, White, Glass, Rubber, Cardboard, Paper, Wood, Soil, Leaf, LeafSick, Terracotta;
            public Material Fungicide, Bactericide, Fertiliser, Organic, Safety, AmberGlass, LiquidBlue, LiquidAmber, SignGreen, SignBlue, Led;

            public Material SackGreen, SackBrown, SackWhite;

            public Palette()
            {
                // Realistic CC0 surfaces (ambientCG) on the generated shapes; planar UVs are in metres.
                Stainless = Surface("Stainless", "Metal009", new Color(0.92f, 0.93f, 0.95f), 2f);
                Painted = Surface("PaintedSteel", "Plastic010", new Color(0.46f, 0.5f, 0.55f), 2f, smoothness: 0.8f);
                White = Surface("WhitePlastic", "Plastic010", new Color(0.97f, 0.97f, 0.96f), 2f, smoothness: 0.9f);
                Glass = MaterialFactory.Lit($"{MaterialFolder}/LabGlass.mat", new Color(0.84f, 0.94f, 1f, 0.22f), 0f, 0.9f, transparent: true, doubleSided: true);
                Rubber = Surface("Rubber", "Rubber004", new Color(0.55f, 0.55f, 0.57f), 3f);
                Cardboard = Surface("Cardboard", "Cardboard004", new Color(0.9f, 0.82f, 0.7f), 1.5f);
                Paper = Solid("LabelPaper", new Color(0.95f, 0.95f, 0.91f), 0.1f);
                Wood = Surface("PalletWood", "Wood049", new Color(0.95f, 0.88f, 0.78f), 1.2f);
                Soil = Surface("Soil", "Ground048", Color.white, 6f, normalScale: 0.6f);
                SackGreen = Surface("SackGreen", "Fabric010", new Color(0.2f, 0.45f, 0.24f), 3f, useColour: false, smoothness: 0.6f);
                SackBrown = Surface("SackBrown", "Fabric010", new Color(0.55f, 0.4f, 0.26f), 3f, useColour: false, smoothness: 0.6f);
                SackWhite = Surface("SackWhite", "Fabric010", new Color(0.88f, 0.88f, 0.85f), 3f, useColour: false, smoothness: 0.6f);
                Leaf = Solid("SeedlingLeaf", new Color(0.3f, 0.56f, 0.2f), 0.2f);
                LeafSick = Solid("SickLeaf", new Color(0.6f, 0.52f, 0.17f), 0.15f);
                Terracotta = Solid("Terracotta", new Color(0.66f, 0.36f, 0.22f), 0.1f);
                Fungicide = Solid("CodeFungicideBlue", new Color(0.12f, 0.35f, 0.72f), 0.4f);
                Bactericide = Solid("CodeBactericideRed", new Color(0.72f, 0.14f, 0.12f), 0.4f);
                Fertiliser = Solid("CodeFertiliserGreen", new Color(0.17f, 0.5f, 0.23f), 0.4f);
                Organic = Solid("CodeOrganicAmber", new Color(0.82f, 0.55f, 0.1f), 0.4f);
                Safety = Solid("SafetyYellow", new Color(0.95f, 0.76f, 0.07f), 0.35f);
                AmberGlass = Solid("AmberGlass", new Color(0.33f, 0.17f, 0.05f), 0.75f);
                LiquidBlue = Solid("LiquidBlue", new Color(0.32f, 0.58f, 0.86f), 0.8f);
                LiquidAmber = Solid("LiquidAmber", new Color(0.8f, 0.6f, 0.18f), 0.8f);
                SignGreen = Solid("SignGreen", new Color(0.0f, 0.5f, 0.27f), 0.3f);
                SignBlue = Solid("SignBlue", new Color(0.05f, 0.3f, 0.66f), 0.3f);
                Led = MaterialFactory.Glow($"{MaterialFolder}/GrowLed.mat", new Color(0.95f, 0.72f, 1f), 1.6f);
            }

            static Material Solid(string name, Color color, float smoothness) => MaterialFactory.Solid($"{MaterialFolder}/{name}.mat", color, smoothness);
        }

        static Palette pal;

        // ------------------------------------------------------------------ generated meshes

        static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();

        /// <summary>Loads or (re)writes a generated mesh asset; the same content keeps the same GUID across rebuilds.</summary>
        static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (MeshCache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var path = $"{MeshFolder}/{key}.asset";
            var fresh = build();
            fresh.name = key;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                ProjectPaths.EnsureFolder(MeshFolder);
                AssetDatabase.CreateAsset(fresh, path);
                existing = fresh;
            }
            else
            {
                // Copied through the Mesh API, not CopySerialized: that leaves the GPU copy of a mesh whose vertex or index
                // count changed stale in the open scene (it draws nothing or garbage until the editor restarts).
                CopyMesh(fresh, existing);
                existing.name = key;
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(existing);
            }

            MeshCache[key] = existing;
            return existing;
        }

        /// <summary>Replaces <paramref name="target"/>'s geometry with <paramref name="source"/>'s (positions, normals, tangents, colours, UV sets, submeshes, bounds).</summary>
        static void CopyMesh(Mesh source, Mesh target)
        {
            target.Clear();
            target.indexFormat = source.indexFormat;
            target.SetVertices(source.vertices);
            if (source.HasVertexAttribute(VertexAttribute.Normal))
                target.SetNormals(source.normals);
            if (source.HasVertexAttribute(VertexAttribute.Tangent))
                target.SetTangents(source.tangents);
            if (source.HasVertexAttribute(VertexAttribute.Color))
                target.SetColors(source.colors32);
            var uv2 = new List<Vector2>();
            var uv4 = new List<Vector4>();
            for (var channel = 0; channel < 8; channel++)
            {
                var attribute = VertexAttribute.TexCoord0 + channel;
                if (!source.HasVertexAttribute(attribute))
                    continue;
                if (source.GetVertexAttributeDimension(attribute) == 2)
                {
                    source.GetUVs(channel, uv2);
                    target.SetUVs(channel, uv2);
                }
                else
                {
                    source.GetUVs(channel, uv4);
                    target.SetUVs(channel, uv4);
                }
            }

            target.subMeshCount = source.subMeshCount;
            for (var s = 0; s < source.subMeshCount; s++)
                target.SetTriangles(source.GetTriangles(s), s, false);
            target.bounds = source.bounds;
        }

        static int Mm(float metres) => Mathf.RoundToInt(metres * 1000f);

        /// <summary>Accumulates flat- or smooth-shaded polygons into submeshes; windings are fixed from the given normal.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<List<int>> submeshes = new List<List<int>>();

            List<int> Sub(int index)
            {
                while (submeshes.Count <= index)
                    submeshes.Add(new List<int>());
                return submeshes[index];
            }

            /// <summary>A flat convex polygon facing <paramref name="normal"/> (UVs planar in metres).</summary>
            public void Poly(int sub, Vector3 normal, params Vector3[] points)
            {
                var n = normal.normalized;
                var u = Mathf.Abs(n.y) > 0.7f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(n, u).normalized;
                u = Vector3.Cross(w, n);
                var start = vertices.Count;
                foreach (var p in points)
                {
                    vertices.Add(p);
                    normals.Add(n);
                    uvs.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w)));
                }

                var front = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), n) > 0f;
                var tris = Sub(sub);
                for (var i = 1; i < points.Length - 1; i++)
                {
                    tris.Add(start);
                    tris.Add(front ? start + i : start + i + 1);
                    tris.Add(front ? start + i + 1 : start + i);
                }
            }

            /// <summary>A quad seen from both sides (thin leaves, labels).</summary>
            public void TwoSided(int sub, Vector3 normal, params Vector3[] points)
            {
                Poly(sub, normal, points);
                Poly(sub, -normal, points);
            }

            /// <summary>A smooth quad strip segment: four points with their own normals (lathe walls).</summary>
            public void SmoothQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
            {
                var start = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                normals.AddRange(new[] { na, nb, nc, nd });
                uvs.AddRange(new[] { uvA, uvB, uvC, uvD });
                var faceNormal = na + nb + nc + nd;
                var front = Vector3.Dot(Vector3.Cross(b - a, c - a), faceNormal) > 0f;
                Sub(sub).AddRange(front
                    ? new[] { start, start + 1, start + 2, start, start + 2, start + 3 }
                    : new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            public Mesh Build()
            {
                var mesh = new Mesh();
                if (vertices.Count > 65000)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = Mathf.Max(1, submeshes.Count);
                for (var i = 0; i < submeshes.Count; i++)
                    mesh.SetTriangles(submeshes[i], i);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }

            /// <summary>An axis-aligned box (all six faces, flat-shaded).</summary>
            public void Box(int sub, Vector3 centre, Vector3 size)
            {
                var h = size * 0.5f;
                for (var axis = 0; axis < 3; axis++)
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var n = Vector3.zero;
                    n[axis] = sign;
                    var u = (axis + 1) % 3;
                    var v = (axis + 2) % 3;
                    var corners = new Vector3[4];
                    var k = 0;
                    foreach (var (su, sv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    {
                        var p = Vector3.zero;
                        p[axis] = sign * h[axis];
                        p[u] = su * h[u];
                        p[v] = sv * h[v];
                        corners[k++] = centre + p;
                    }

                    Poly(sub, n, corners);
                }
            }
        }

        /// <summary>A box of exactly <paramref name="size"/> with chamfered edges (44 triangles), centred on its origin.</summary>
        static Mesh BevelBoxMesh(Vector3 size, float bevel)
        {
            var key = $"Bevel_{Mm(size.x)}x{Mm(size.y)}x{Mm(size.z)}_{Mm(bevel)}";
            return Cached(key, () =>
            {
                var h = size * 0.5f;
                var b = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.45f);
                // The point of corner s (signs) that lies on the face across 'axis'.
                Vector3 P(int axis, Vector3 s)
                {
                    var p = new Vector3(s.x * (h.x - b), s.y * (h.y - b), s.z * (h.z - b));
                    p[axis] = s[axis] * h[axis];
                    return p;
                }

                var builder = new MeshBuilder();
                var pairs = new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) };
                for (var axis = 0; axis < 3; axis++)
                {
                    var u = (axis + 1) % 3;
                    var v = (axis + 2) % 3;
                    // Main faces.
                    for (var sign = -1f; sign <= 1f; sign += 2f)
                    {
                        var points = new Vector3[4];
                        for (var i = 0; i < 4; i++)
                        {
                            var s = Vector3.zero;
                            s[axis] = sign;
                            s[u] = pairs[i].Item1;
                            s[v] = pairs[i].Item2;
                            points[i] = P(axis, s);
                        }

                        var n = Vector3.zero;
                        n[axis] = sign;
                        builder.Poly(0, n, points);
                    }

                    // Edge chamfers along 'axis' between the u and v faces.
                    foreach (var (su, sv) in pairs)
                    {
                        Vector3 Corner(float sa)
                        {
                            var s = Vector3.zero;
                            s[axis] = sa;
                            s[u] = su;
                            s[v] = sv;
                            return s;
                        }

                        var n = Vector3.zero;
                        n[u] = su;
                        n[v] = sv;
                        builder.Poly(0, n, P(u, Corner(-1f)), P(v, Corner(-1f)), P(v, Corner(1f)), P(u, Corner(1f)));
                    }
                }

                // Corner triangles.
                for (var i = 0; i < 8; i++)
                {
                    var s = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                    builder.Poly(0, s, P(0, s), P(1, s), P(2, s));
                }

                return builder.Build();
            });
        }

        /// <summary>
        /// A surface of revolution about +Y from a (radius, height) profile, bottom to top: flat-shaded along the profile
        /// (crisp shoulders), smooth around. Caps close the first/last ring when its radius is not zero.
        /// </summary>
        static Mesh LatheMesh(string key, Vector2[] profile, int segments, bool capBottom = true, bool capTop = true)
        {
            return Cached($"Lathe_{key}_{segments}", () =>
            {
                var builder = new MeshBuilder();
                Vector3 Ring(Vector2 p, int j)
                {
                    var a = j * Mathf.PI * 2f / segments;
                    return new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x);
                }

                Vector3 Normal(Vector2 n2, int j)
                {
                    var a = j * Mathf.PI * 2f / segments;
                    return new Vector3(Mathf.Cos(a) * n2.x, n2.y, Mathf.Sin(a) * n2.x).normalized;
                }

                for (var i = 0; i < profile.Length - 1; i++)
                {
                    var p0 = profile[i];
                    var p1 = profile[i + 1];
                    var t = p1 - p0;
                    if (t.sqrMagnitude < 1e-10f)
                        continue;
                    var n2 = new Vector2(t.y, -t.x).normalized;
                    for (var j = 0; j < segments; j++)
                    {
                        var u0 = (float)j / segments;
                        var u1 = (float)(j + 1) / segments;
                        builder.SmoothQuad(0, Ring(p0, j), Ring(p0, j + 1), Ring(p1, j + 1), Ring(p1, j),
                            Normal(n2, j), Normal(n2, j + 1), Normal(n2, j + 1), Normal(n2, j),
                            new Vector2(u0, p0.y), new Vector2(u1, p0.y), new Vector2(u1, p1.y), new Vector2(u0, p1.y));
                    }
                }

                void Cap(Vector2 p, Vector3 n)
                {
                    if (p.x < 1e-4f)
                        return;
                    var points = new Vector3[segments];
                    for (var j = 0; j < segments; j++)
                        points[j] = Ring(p, j);
                    builder.Poly(0, n, points);
                }

                if (capBottom)
                    Cap(profile[0], Vector3.down);
                if (capTop)
                    Cap(profile[profile.Length - 1], Vector3.up);
                return builder.Build();
            });
        }

        /// <summary>Unit cylinder (radius 0.5, height 1, base on the origin).</summary>
        static Mesh CylinderMesh(int segments = 12) => LatheMesh("Cylinder", new[] { new Vector2(0.5f, 0f), new Vector2(0.5f, 1f) }, segments);

        // Profiles (metres, base at 0).
        static readonly Vector2[] BottleProfile =
            { new Vector2(0.042f, 0f), new Vector2(0.045f, 0.01f), new Vector2(0.045f, 0.17f), new Vector2(0.03f, 0.205f), new Vector2(0.016f, 0.215f), new Vector2(0.016f, 0.235f) };
        static readonly Vector2[] ReagentProfile =
            { new Vector2(0.032f, 0f), new Vector2(0.034f, 0.008f), new Vector2(0.034f, 0.11f), new Vector2(0.02f, 0.135f), new Vector2(0.013f, 0.14f), new Vector2(0.013f, 0.155f) };
        static readonly Vector2[] BeakerProfile =
            { new Vector2(0.038f, 0f), new Vector2(0.04f, 0.004f), new Vector2(0.04f, 0.11f), new Vector2(0.043f, 0.115f), new Vector2(0.037f, 0.115f), new Vector2(0.037f, 0.004f) };
        static readonly Vector2[] FlaskProfile =
            { new Vector2(0.05f, 0f), new Vector2(0.052f, 0.006f), new Vector2(0.05f, 0.03f), new Vector2(0.016f, 0.12f), new Vector2(0.016f, 0.16f), new Vector2(0.019f, 0.165f) };
        static readonly Vector2[] CylinderGlassProfile =
            { new Vector2(0.018f, 0.012f), new Vector2(0.018f, 0.26f), new Vector2(0.021f, 0.265f) };
        static readonly Vector2[] JarProfile =
            { new Vector2(0.03f, 0f), new Vector2(0.032f, 0.006f), new Vector2(0.032f, 0.075f), new Vector2(0.026f, 0.085f), new Vector2(0.026f, 0.09f) };
        static readonly Vector2[] PotProfile =
            { new Vector2(0.045f, 0f), new Vector2(0.062f, 0.11f), new Vector2(0.068f, 0.112f), new Vector2(0.068f, 0.125f), new Vector2(0.058f, 0.125f) };
        static readonly Vector2[] JugProfile =
            { new Vector2(0.06f, 0f), new Vector2(0.062f, 0.005f), new Vector2(0.07f, 0.17f), new Vector2(0.074f, 0.175f), new Vector2(0.068f, 0.175f), new Vector2(0.06f, 0.008f) };
        static readonly Vector2[] PetriProfile =
            { new Vector2(0.045f, 0f), new Vector2(0.046f, 0.012f), new Vector2(0.043f, 0.012f), new Vector2(0.043f, 0.002f) };
        static readonly Vector2[] BowlProfile =
            { new Vector2(0.03f, 0f), new Vector2(0.14f, 0.07f), new Vector2(0.16f, 0.09f), new Vector2(0.14f, 0.09f), new Vector2(0.04f, 0.03f) };
        static readonly Vector2[] DomeProfile =
            { new Vector2(0.055f, 0f), new Vector2(0.05f, 0.03f), new Vector2(0.032f, 0.055f), new Vector2(0f, 0.065f) };
        static readonly Vector2[] TubProfile =
            { new Vector2(0.2f, 0f), new Vector2(0.24f, 0.28f), new Vector2(0.255f, 0.29f), new Vector2(0.235f, 0.29f), new Vector2(0.195f, 0.02f) };

        // ------------------------------------------------------------------ scene objects

        /// <summary>A static mesh renderer under <paramref name="parent"/> (local pose); small props cast no shadows.</summary>
        static GameObject Prop(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 euler, Vector3 scale, bool shadows = false)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, PropFlags);
            return go;
        }

        static Mesh unitCube;
        static Mesh UnitCube => unitCube != null ? unitCube : unitCube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        /// <summary>A box centred at <paramref name="centre"/>; bevelled (a mesh of its exact size) unless tiny or <paramref name="bevel"/> is 0.</summary>
        static GameObject Box(string name, Transform parent, Vector3 centre, Vector3 size, Material material, float bevel = 0.008f,
            Vector3 euler = default, bool shadows = false)
        {
            if (bevel <= 0f || Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < 0.012f)
                return Prop(name, parent, UnitCube, material, centre, euler, size, shadows);
            return Prop(name, parent, BevelBoxMesh(size, bevel), material, centre, euler, Vector3.one, shadows);
        }

        /// <summary>An upright cylinder standing on <paramref name="basePoint"/>.</summary>
        static GameObject Cyl(string name, Transform parent, Vector3 basePoint, float radius, float height, Material material, int segments = 12,
            Vector3 euler = default, bool shadows = false) =>
            Prop(name, parent, CylinderMesh(segments), material, basePoint, euler, new Vector3(radius * 2f, height, radius * 2f), shadows);

        /// <summary>A cylinder between two local points (rails, pipes, lances).</summary>
        static GameObject Rod(string name, Transform parent, Vector3 from, Vector3 to, float radius, Material material, int segments = 8)
        {
            var axis = to - from;
            var go = Prop(name, parent, CylinderMesh(segments), material, from, Vector3.zero, new Vector3(radius * 2f, axis.magnitude, radius * 2f));
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            return go;
        }

        static GameObject Lathe(string name, Transform parent, string key, Vector2[] profile, Material material, Vector3 basePoint, float scale = 1f,
            int segments = 12, bool capTop = true) =>
            Prop(name, parent, LatheMesh(key, profile, segments, true, capTop), material, basePoint, Vector3.zero, Vector3.one * scale);

        static Transform Group(string name, Transform parent, Vector3 position, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            GameObjectUtility.SetStaticEditorFlags(go, PropFlags);
            return go.transform;
        }

        /// <summary>A static box collider on a unit root (benches, shelving and cabinets only), so hands and rays stop at them.</summary>
        static void Solid(Transform unit, Vector3 centre, Vector3 size)
        {
            var box = unit.gameObject.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        // ------------------------------------------------------------------ text

        static readonly Color Ink = new Color(0.08f, 0.09f, 0.1f);

        /// <summary>
        /// A 3D TextMeshPro label (no canvas, no raycaster) facing local -Z, auto-sized into <paramref name="size"/> metres;
        /// <paramref name="maxHeight"/> caps the letter height (metres).
        /// </summary>
        static TextMeshPro Text(Transform parent, string text, Vector3 position, Vector2 size, Color color, float maxHeight = 0.05f,
            FontStyles style = FontStyles.Bold, TextAlignmentOptions alignment = TextAlignmentOptions.Center, Vector3 euler = default)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = TMP_Settings.defaultFontAsset;
            var rect = tmp.rectTransform;
            rect.sizeDelta = size;
            rect.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
            tmp.text = text;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.enableAutoSizing = true;
            // TextMeshPro (3D) font size is about ten times the line height in metres.
            tmp.fontSizeMin = 0.04f;
            tmp.fontSizeMax = maxHeight * 10f;
            tmp.margin = Vector4.zero;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return tmp;
        }

        /// <summary>A thin plate with text on its front (local -Z) face; the plate's back sits at local z = 0.</summary>
        static Transform Sign(string name, Transform parent, string text, Vector3 position, Vector2 size, Material plate, Color textColor,
            float maxHeight = 0.08f, float yaw = 0f, float pitch = 0f)
        {
            var sign = new GameObject(name).transform;
            sign.SetParent(parent, false);
            sign.SetLocalPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0f));
            GameObjectUtility.SetStaticEditorFlags(sign.gameObject, PropFlags);
            const float t = 0.012f;
            Box("Plate", sign, new Vector3(0f, 0f, -t * 0.5f), new Vector3(size.x, size.y, t), plate, 0f);
            Text(sign, text, new Vector3(0f, 0f, -t - 0.002f), size * 0.9f, textColor, maxHeight);
            return sign;
        }

        /// <summary>A paper label stuck on a container's front face (local -Z at <paramref name="frontZ"/>).</summary>
        static void StickOn(Transform parent, string text, Vector3 centre, Vector2 size, float maxHeight = 0.02f)
        {
            Box("LabelPaper", parent, centre, new Vector3(size.x, size.y, 0.002f), pal.Paper, 0f);
            Text(parent, text, centre + new Vector3(0f, 0f, -0.0025f), size * 0.92f, Ink, maxHeight);
        }

        // ------------------------------------------------------------------ supplied models

        const string SprayBottleModel = "Assets/Lab_Treatment_Assets/spray-bottle/model/SprayBottle_Quest.fbx";
        const string GardeningToolsGlb = "Assets/Lab_Treatment_Assets/gardening_tools.glb";
        const string PruningShearsGlb = "Assets/Lab_Treatment_Assets/pruning_shears.glb";
        const string WorkGlovesGlb = "Assets/Lab_Treatment_Assets/work_gloves.glb";

        /// <summary>Bounds of a model's meshes in <paramref name="space"/>'s local coordinates.</summary>
        static Bounds LocalBounds(Transform space, GameObject model)
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var b = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = space.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (first)
                        bounds = new Bounds(p, Vector3.zero);
                    else
                        bounds.Encapsulate(p);
                    first = false;
                }
            }

            return bounds;
        }

        /// <summary>Scales <paramref name="model"/> (a child of <paramref name="holder"/>) so its longest side is <paramref name="length"/> and stands it, centred, on the holder's origin.</summary>
        static void Fit(Transform holder, GameObject model, float length)
        {
            var bounds = LocalBounds(holder, model);
            var longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > 1e-5f)
                model.transform.localScale *= length / longest;
            bounds = LocalBounds(holder, model);
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static void Finish(GameObject model, bool shadows = false)
        {
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, PropFlags);
        }

        /// <summary>The supplied Quest spray bottle (real size already, 0.25 m), standing on the local point.</summary>
        static GameObject SprayBottle(Transform parent, Vector3 position, float yaw)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(SprayBottleModel);
            var holder = Group("SprayBottle", parent, position, yaw);
            if (asset == null)
            {
                Lathe("Bottle", holder, "Bottle", BottleProfile, pal.White, Vector3.zero);
                return holder.gameObject;
            }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, holder);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            var bounds = LocalBounds(holder, model);
            // Keep the authored size if it is plausible; otherwise bring it to 0.25 m.
            if (bounds.size.y < 0.15f || bounds.size.y > 0.4f)
                Fit(holder, model, 0.25f);
            else
                model.transform.localPosition -= new Vector3(0f, bounds.min.y, 0f);
            Finish(model);
            return holder.gameObject;
        }

        /// <summary>How a copied tool mesh is laid: hung (longest axis up), flat (thinnest axis up) or as authored (glTF up).</summary>
        enum Pose
        {
            Hung,
            Flat,
            Authored,
        }

        /// <summary>
        /// Copies one mesh of a supplied GLB (by GameObject name prefix) into a holder, posed and scaled so its longest side
        /// is <paramref name="length"/> metres, resting on the holder's origin. Returns null if the mesh is missing.
        /// </summary>
        static Transform GlbMesh(Transform parent, string glbPath, string prefix, float length, Pose pose, Vector3 position, float yaw, Material tint = null)
        {
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (glb == null)
                return null;
            MeshFilter source = null;
            foreach (var filter in glb.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.name.StartsWith(prefix))
                {
                    source = filter;
                    break;
                }

            if (source == null)
                return null;
            var holder = Group(prefix.TrimEnd('_', '0').Trim(), parent, position, yaw);
            var go = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(holder, false);
            go.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = tint != null ? new[] { tint } : source.GetComponent<MeshRenderer>().sharedMaterials;

            var size = source.sharedMesh.bounds.size;
            var axes = new[] { 0, 1, 2 };
            System.Array.Sort(axes, (a, b) => size[b].CompareTo(size[a]));
            Vector3 Axis(int i)
            {
                var v = Vector3.zero;
                v[i] = 1f;
                return v;
            }

            switch (pose)
            {
                case Pose.Hung:
                    // Longest axis up, thinnest towards the wall (local Z), middle along X.
                    go.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(Axis(axes[2]), Axis(axes[0])));
                    break;
                case Pose.Flat:
                    // Thinnest axis up, longest along X.
                    go.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(Axis(axes[1]), Axis(axes[2])));
                    break;
                default:
                    go.transform.localRotation = Quaternion.Inverse(glb.transform.rotation) * source.transform.rotation;
                    break;
            }

            Fit(holder, go, length);
            Finish(go);
            return holder;
        }

        /// <summary>The supplied pruning shears (whole model without its dense spring), laid flat, <paramref name="length"/> long, red handles.</summary>
        static Transform PruningShears(Transform parent, Vector3 position, float yaw, float length = 0.21f)
        {
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(PruningShearsGlb);
            if (glb == null)
                return null;
            var holder = Group("PruningShears", parent, position, yaw);
            var copy = Object.Instantiate(glb, holder, false);
            copy.name = "Model";
            foreach (var filter in copy.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name.StartsWith("Helix"))
                    Object.DestroyImmediate(filter.gameObject);
            foreach (var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name.StartsWith("Hand"))
                    renderer.sharedMaterial = pal.Bactericide;
            copy.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy.transform.localScale = Vector3.one;
            var bounds = LocalBounds(holder, copy);
            if (bounds.size.x < bounds.size.y && bounds.size.x < bounds.size.z)
                copy.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else if (bounds.size.z < bounds.size.y && bounds.size.z < bounds.size.x)
                copy.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Fit(holder, copy, length);
            Finish(copy);
            return holder;
        }

        // ------------------------------------------------------------------ reused project prefabs

        /// <summary>
        /// Places a Treatment fixture prefab as pure decoration: unpacked, with every collider and script removed (only the
        /// canvas text that shows its name stays), so it can never take part in a lesson.
        /// </summary>
        static GameObject Decor(string prefabName, Transform parent, Vector3 position, float yaw, float scale = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ProjectPaths.Root}/Prefabs/Treatment/Fixtures/{prefabName}.prefab");
            if (prefab == null)
                return null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Decor_" + prefabName;
            instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = Vector3.one * scale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            // Scripts first (some require others), keeping only what draws the text.
            for (var pass = 0; pass < 2; pass++)
                foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour is TMP_Text || behaviour is TMP_SubMeshUI || behaviour is TMP_SubMesh || behaviour is UnityEngine.UI.CanvasScaler ||
                        behaviour is UnityEngine.UI.Image)
                        continue;
                    Object.DestroyImmediate(behaviour);
                }

            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                var renderer = t.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, PropFlags);
                }
            }

            return instance;
        }
    }
}
