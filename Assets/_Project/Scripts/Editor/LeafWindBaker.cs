using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Turns a Blender per-leaf wind table into the float texture read by LeafWind.hlsl (BTP/Farm Recovery with
    /// _LEAF_WIND). Used by the cherry tree (Tools/blender_cherry_tree.py: CherryTree_Wind.bytes) and the nine
    /// tomato plants (Tools/blender_tomato_plants.py: Tomato_&lt;diseaseId&gt;_Wind.bytes).
    ///
    /// The Blender keys are world-space rotations in Blender's axes; the shader needs them in the imported mesh's
    /// object space. Rather than hard-coding the FBX axis/unit conversion (easy to get subtly wrong, and it
    /// changes with export and import settings), the mapping is measured: one vertex per leaf is tagged in UV1
    /// (u = leaf + 1, v = 1), so its Unity position pairs with the Blender position of that vertex (its "anchor")
    /// from the data file. A least-squares fit over all anchors gives x_unity = A x_blender + t; A is a scaled
    /// orthogonal matrix P (possibly a reflection, det -1, since Unity's FBX import flips handedness), each key D
    /// maps to P D Pᵀ and each Blender pivot to A pivot + t.
    ///
    /// Two file formats, both little-endian float32:
    ///   version 1 (cherry, no header): per leaf the pivot xyz, then <see cref="LegacyKeys"/> x (w, x, y, z).
    ///     The tagged vertex is the pivot itself (the cherry blade's base vertex), in both LODs.
    ///   version 2 (tomatoes): "BTPW", uint32 version, uint32 leaves, uint32 keys, float32 loop seconds,
    ///     uint32 anchor sets (one per LOD), then per leaf: pivot xyz, anchor xyz per set, keys x (w, x, y, z).
    ///     The tomato leaves are cards whose nearest vertex to the pivot is a few millimetres off it, so the
    ///     anchor (fit) and the pivot (rotation centre) are stored separately.
    ///   version 3 (the field and tropical crops, Tools/blender_farm_crops.py): as version 2, then per leaf
    ///     keys x (x, y, z): the travel of the pivot relative to the exported pose. Their stems sway too, so
    ///     every moving part (leaf, stem, fruit) is a "leaf" here and follows its parent exactly. The texture
    ///     is then 2 x keys + 1 wide: rotations, travel, pivot (the material's _WindTravel tells the shader).
    ///
    /// Texture layout (RGBAFloat, point, clamp, no mips): width keys + 1, height = leaf count; column k &lt; keys
    /// is key k as a Unity quaternion (xyzw), the last column the pivot in mesh space. The shader reads the key
    /// count from the width, and the loop length from the material's _WindLoopSeconds.
    /// </summary>
    public static class LeafWindBaker
    {
        /// <summary>Key count of the headerless version-1 files (the cherry: 48 keys, 1/6 s apart).</summary>
        public const int LegacyKeys = 48;
        public const float LegacyLoopSeconds = 8f;

        public readonly struct Result
        {
            public readonly Texture2D Texture;
            public readonly int Leaves;
            public readonly int Keys;
            public readonly float LoopSeconds;
            public readonly float MaxAngle;
            public readonly float Residual;
            /// <summary>The texture also holds each part's travel keys (version 3 data).</summary>
            public readonly bool Travel;

            public Result(Texture2D texture, int leaves, int keys, float loopSeconds, float maxAngle, float residual, bool travel = false)
            {
                Travel = travel;
                Texture = texture;
                Leaves = leaves;
                Keys = keys;
                LoopSeconds = loopSeconds;
                MaxAngle = maxAngle;
                Residual = residual;
            }
        }

        /// <summary>What a wind file holds, in Blender world space.</summary>
        sealed class WindData
        {
            public int Version;
            public int Keys;
            public float LoopSeconds;
            public Vector3[] Pivots;
            /// <summary>[set][leaf]: the tagged vertex of LOD0 (set 0) and LOD1 (set 1).</summary>
            public Vector3[][] Anchors;
            /// <summary>[leaf, key] = (w, x, y, z).</summary>
            public double[,][] Rotations;
            /// <summary>[leaf, key]: travel of the pivot (version 3); null otherwise.</summary>
            public Vector3[,] Travel;
        }

        /// <summary>Applies a bake to a wind leaf material: texture, loop length, keyword.</summary>
        public static void Assign(Material material, Result result)
        {
            material.SetTexture("_WindTex", result.Texture);
            material.SetFloat("_WindLoopSeconds", result.LoopSeconds);
            material.SetFloat("_WindTravel", result.Travel ? 1f : 0f);
            material.SetFloat("_LeafWind", 1f);
            material.EnableKeyword("_LEAF_WIND");
            EditorUtility.SetDirty(material);
        }

        public static Result Bake(string bytesPath, Mesh lod0, Mesh lod1, string texturePath, string label)
        {
            if (!File.Exists(bytesPath))
                throw new FileNotFoundException($"[BTP] {bytesPath} is missing; run the {label} Blender export (Tools/blender_*.py) first.");

            var data = Read(bytesPath);
            var n = data.Pivots.Length;
            var keys = data.Keys;
            var width = (data.Travel != null ? 2 * keys : keys) + 1;
            // UV1 can be stored as half precision in builds (Player vertex compression): integers are exact to 2048.
            if (n >= 2048)
                throw new InvalidOperationException($"[BTP] {n} {label} leaves: wind indices above 2047 would not survive half-precision UVs.");

            var tagged0 = TaggedPositions(lod0, n, out var missing);
            if (missing > 0)
                throw new InvalidOperationException($"[BTP] {missing} of {n} {label} leaves have no tagged vertex in {lod0.name} (UV1 = (leaf + 1, 1)). Was the FBX imported with mesh compression?");

            var (a, t) = FitAffine(data.Anchors[0], tagged0);
            var scale = Math.Pow(Math.Abs(Det(a)), 1.0 / 3.0);
            var residual = 0.0;
            for (var i = 0; i < n; i++)
                residual = Math.Max(residual, (Apply(a, t, data.Anchors[0][i]) - tagged0[i]).magnitude);
            if (residual > 1e-3 * scale)
                throw new InvalidOperationException($"[BTP] {label} wind: Blender anchors do not map onto the Unity mesh by one rigid transform (max residual {residual:E2} m, limit {1e-3 * scale:E2}). Re-export the FBX and wind data together.");

            var p = new double[3, 3];
            for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                p[r, c] = a[r, c] / scale;

            // Version 1 tags the pivot itself, so its measured position is used directly (as the cherry always did);
            // version 2 maps the stored pivot through the fit.
            var pivots = new Vector3[n];
            for (var i = 0; i < n; i++)
                pivots[i] = data.Version == 1 ? tagged0[i] : Apply(a, t, data.Pivots[i]);

            var pixels = new float[width * n * 4];
            var maxAngle = 0f;
            var conversionError = 0f;
            for (var i = 0; i < n; i++)
            {
                var previous = Quaternion.identity;
                for (var k = 0; k < keys; k++)
                {
                    var m = Mul(Mul(p, Rotation(data.Rotations[i, k])), Transpose(p));
                    var forward = MulV(m, Vector3.forward);
                    var up = MulV(m, Vector3.up);
                    var q = Quaternion.LookRotation(forward, up);
                    // Same hemisphere as the previous key (key 0 is identity), so the shader can blend neighbours.
                    if (Quaternion.Dot(q, previous) < 0f)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    previous = q;
                    // Self-check: the quaternion must reproduce the matrix on every axis.
                    conversionError = Mathf.Max(conversionError, (q * Vector3.right - MulV(m, Vector3.right)).magnitude,
                        (q * Vector3.up - up).magnitude, (q * Vector3.forward - forward).magnitude);
                    maxAngle = Mathf.Max(maxAngle, 2f * Mathf.Acos(Mathf.Min(1f, Mathf.Abs(q.w))) * Mathf.Rad2Deg);
                    var o = (i * width + k) * 4;
                    pixels[o] = q.x;
                    pixels[o + 1] = q.y;
                    pixels[o + 2] = q.z;
                    pixels[o + 3] = q.w;
                }

                if (data.Travel != null)
                {
                    // A displacement: only the linear part of the fit applies.
                    for (var k = 0; k < keys; k++)
                    {
                        var d = Apply(a, Vector3.zero, data.Travel[i, k]);
                        var to = (i * width + keys + k) * 4;
                        pixels[to] = d.x;
                        pixels[to + 1] = d.y;
                        pixels[to + 2] = d.z;
                        pixels[to + 3] = 0f;
                    }
                }

                var po = (i * width + width - 1) * 4;
                pixels[po] = pivots[i].x;
                pixels[po + 1] = pivots[i].y;
                pixels[po + 2] = pivots[i].z;
                pixels[po + 3] = 1f;
            }

            if (conversionError > 1e-3f)
                throw new InvalidOperationException($"[BTP] {label} wind: key rotation conversion error {conversionError:E2} (fit matrix not orthogonal?).");

            // LOD1 shares the leaf placement; it must use the same mesh space or its leaves would swing about wrong pivots.
            if (lod1 != null)
            {
                var tagged1 = TaggedPositions(lod1, n, out var missing1);
                var anchors1 = data.Anchors[Math.Min(1, data.Anchors.Length - 1)];
                var lodError = 0f;
                for (var i = 0; i < n; i++)
                    lodError = Mathf.Max(lodError, (tagged1[i] - Apply(a, t, anchors1[i])).magnitude);
                if (missing1 > 0 || lodError > 1e-3f * (float)scale)
                    Debug.LogWarning($"[BTP] {label} LOD1 leaf anchors do not match the LOD0 fit ({missing1} missing, max {lodError:E2} m); LOD1 wind will look wrong.");
            }

            var texture = WriteTexture(texturePath, width, n, pixels);
            Debug.Log($"[BTP] {label} wind baked: {n} leaves x {keys} keys over {data.LoopSeconds} s, max angle {maxAngle:F1} deg, anchor fit residual {residual:E2} m (scale {scale:F4}, det {(Det(a) < 0 ? "-" : "+")}), texture {texturePath}.");
            return new Result(texture, n, keys, data.LoopSeconds, maxAngle, (float)residual, data.Travel != null);
        }

        static WindData Read(string path)
        {
            // The file is little-endian float32 (struct '<f'); so are all platforms the editor runs on.
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("Big-endian editor.");
            var bytes = File.ReadAllBytes(path);
            var offset = 0;
            float Next()
            {
                var v = BitConverter.ToSingle(bytes, offset);
                offset += 4;
                return v;
            }

            int NextInt()
            {
                var v = BitConverter.ToInt32(bytes, offset);
                offset += 4;
                return v;
            }

            Vector3 NextVector() => new Vector3(Next(), Next(), Next());

            var data = new WindData();
            int n, sets;
            if (bytes.Length >= 24 && bytes[0] == 'B' && bytes[1] == 'T' && bytes[2] == 'P' && bytes[3] == 'W')
            {
                offset = 4;
                data.Version = NextInt();
                if (data.Version != 2 && data.Version != 3)
                    throw new InvalidDataException($"[BTP] {path}: wind data version {data.Version} is not supported.");
                n = NextInt();
                data.Keys = NextInt();
                data.LoopSeconds = Next();
                sets = NextInt();
                var expected = offset + n * (3 + 3 * sets + data.Keys * (data.Version == 3 ? 7 : 4)) * 4;
                if (n <= 0 || data.Keys < 2 || sets < 1 || data.LoopSeconds <= 0f || bytes.Length != expected)
                    throw new InvalidDataException($"[BTP] {path}: header ({n} leaves, {data.Keys} keys, {sets} anchor sets, {data.LoopSeconds} s) does not match its {bytes.Length} bytes.");
            }
            else
            {
                data.Version = 1;
                data.Keys = LegacyKeys;
                data.LoopSeconds = LegacyLoopSeconds;
                sets = 0;
                const int floatsPerLeaf = 3 + LegacyKeys * 4;
                if (bytes.Length == 0 || bytes.Length % (floatsPerLeaf * 4) != 0)
                    throw new InvalidDataException($"[BTP] {path}: {bytes.Length} bytes is not a whole number of leaves ({floatsPerLeaf * 4} bytes each).");
                n = bytes.Length / (floatsPerLeaf * 4);
            }

            data.Pivots = new Vector3[n];
            data.Anchors = new Vector3[Math.Max(sets, 1)][];
            for (var s = 0; s < data.Anchors.Length; s++)
                data.Anchors[s] = new Vector3[n];
            data.Rotations = new double[n, data.Keys][];
            if (data.Version == 3)
                data.Travel = new Vector3[n, data.Keys];
            for (var i = 0; i < n; i++)
            {
                data.Pivots[i] = NextVector();
                if (sets == 0)
                    data.Anchors[0][i] = data.Pivots[i];
                for (var s = 0; s < sets; s++)
                    data.Anchors[s][i] = NextVector();
                for (var k = 0; k < data.Keys; k++)
                    data.Rotations[i, k] = new double[] { Next(), Next(), Next(), Next() }; // w, x, y, z
                if (data.Travel != null)
                    for (var k = 0; k < data.Keys; k++)
                        data.Travel[i, k] = NextVector();
            }

            return data;
        }

        /// <summary>Mean position of each leaf's tagged vertices (split vertices share one position).</summary>
        static Vector3[] TaggedPositions(Mesh mesh, int leaves, out int missing)
        {
            var sums = new Vector3[leaves];
            var counts = new int[leaves];
            // MeshData reads the imported mesh in the editor even though it is not Read/Write enabled.
            using (var array = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var data = array[0];
                if (!data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1))
                    throw new InvalidOperationException($"[BTP] {mesh.name} has no UV1 (Wind) channel.");
                using var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                using var uvs = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp);
                data.GetVertices(positions);
                data.GetUVs(1, uvs);
                for (var v = 0; v < data.vertexCount; v++)
                {
                    var uv = uvs[v];
                    var leaf = Mathf.RoundToInt(uv.x) - 1;
                    if (leaf >= leaves)
                        throw new InvalidOperationException($"[BTP] {mesh.name}: wind index {leaf + 1} exceeds the {leaves} leaves in the wind data. Re-export both together.");
                    if (leaf < 0 || uv.y < 0.5f)
                        continue;
                    sums[leaf] += positions[v];
                    counts[leaf]++;
                }
            }

            missing = 0;
            for (var i = 0; i < leaves; i++)
            {
                if (counts[i] == 0)
                    missing++;
                else
                    sums[i] /= counts[i];
            }

            return sums;
        }

        static (double[,] a, Vector3 t) FitAffine(Vector3[] from, Vector3[] to)
        {
            // Normal equations of the 4-parameter-per-row least squares, in double.
            var g = new double[4, 4];
            var b = new double[4, 3];
            for (var i = 0; i < from.Length; i++)
            {
                var h = new double[] { from[i].x, from[i].y, from[i].z, 1.0 };
                var y = new double[] { to[i].x, to[i].y, to[i].z };
                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                        g[r, c] += h[r] * h[c];
                    for (var d = 0; d < 3; d++)
                        b[r, d] += h[r] * y[d];
                }
            }

            var x = Solve(g, b);
            var a = new double[3, 3];
            for (var d = 0; d < 3; d++)
            for (var c = 0; c < 3; c++)
                a[d, c] = x[c, d];
            return (a, new Vector3((float)x[3, 0], (float)x[3, 1], (float)x[3, 2]));
        }

        /// <summary>Gauss-Jordan with partial pivoting: solves g x = b for a 4x4 g and 3 right-hand sides.</summary>
        static double[,] Solve(double[,] g, double[,] b)
        {
            const int n = 4, m = 3;
            var aug = new double[n, n + m];
            for (var r = 0; r < n; r++)
            {
                for (var c = 0; c < n; c++)
                    aug[r, c] = g[r, c];
                for (var c = 0; c < m; c++)
                    aug[r, n + c] = b[r, c];
            }

            for (var col = 0; col < n; col++)
            {
                var pivot = col;
                for (var r = col + 1; r < n; r++)
                    if (Math.Abs(aug[r, col]) > Math.Abs(aug[pivot, col]))
                        pivot = r;
                if (Math.Abs(aug[pivot, col]) < 1e-12)
                    throw new InvalidOperationException("[BTP] Leaf wind: anchors are degenerate (cannot fit the mesh transform).");
                for (var c = 0; c < n + m; c++)
                    (aug[col, c], aug[pivot, c]) = (aug[pivot, c], aug[col, c]);
                for (var r = 0; r < n; r++)
                {
                    if (r == col)
                        continue;
                    var f = aug[r, col] / aug[col, col];
                    for (var c = col; c < n + m; c++)
                        aug[r, c] -= f * aug[col, c];
                }
            }

            var x = new double[n, m];
            for (var r = 0; r < n; r++)
            for (var c = 0; c < m; c++)
                x[r, c] = aug[r, n + c] / aug[r, r];
            return x;
        }

        /// <summary>Rotation matrix of a unit quaternion (w, x, y, z), Hamilton convention (as Blender's mathutils).</summary>
        static double[,] Rotation(double[] q)
        {
            var len = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            double w = q[0] / len, x = q[1] / len, y = q[2] / len, z = q[3] / len;
            return new[,]
            {
                { 1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y) },
                { 2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x) },
                { 2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y) },
            };
        }

        static double[,] Mul(double[,] l, double[,] r)
        {
            var o = new double[3, 3];
            for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
            for (var k = 0; k < 3; k++)
                o[i, j] += l[i, k] * r[k, j];
            return o;
        }

        static double[,] Transpose(double[,] m)
        {
            var o = new double[3, 3];
            for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                o[i, j] = m[j, i];
            return o;
        }

        static Vector3 MulV(double[,] m, Vector3 v) => new Vector3(
            (float)(m[0, 0] * v.x + m[0, 1] * v.y + m[0, 2] * v.z),
            (float)(m[1, 0] * v.x + m[1, 1] * v.y + m[1, 2] * v.z),
            (float)(m[2, 0] * v.x + m[2, 1] * v.y + m[2, 2] * v.z));

        static Vector3 Apply(double[,] a, Vector3 t, Vector3 v) => MulV(a, v) + t;

        static double Det(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
            m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
            m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

        /// <summary>Writes the texture asset, updating it in place so the material's reference (GUID) survives.</summary>
        static Texture2D WriteTexture(string path, int width, int leaves, float[] pixels)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var created = texture == null;
            if (created)
                texture = new Texture2D(width, leaves, TextureFormat.RGBAFloat, false, true);
            else if (texture.width != width || texture.height != leaves || texture.format != TextureFormat.RGBAFloat)
                texture.Reinitialize(width, leaves, TextureFormat.RGBAFloat, false);

            texture.name = Path.GetFileNameWithoutExtension(path);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            texture.SetPixelData(pixels, 0);
            // Kept readable (about 1 MB for the cherry, a few KB per tomato) so the EditMode tests can verify the keys.
            texture.Apply(false, false);
            if (created)
                AssetDatabase.CreateAsset(texture, path);
            else
                EditorUtility.SetDirty(texture);
            AssetDatabase.SaveAssets();
            return texture;
        }
    }
}
