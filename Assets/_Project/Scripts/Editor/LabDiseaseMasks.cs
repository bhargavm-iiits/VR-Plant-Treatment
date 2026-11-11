using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Bakes a per-plant disease mask for the Lab's BTP/Leaf Disease shader, in the same channel layout
    /// as the Tomato's hand-derived mask: R = leaf pixels (green-dominant pixels of the healthy albedo, so
    /// fruit, stems and soil stay untouched), G = lesion threshold field, B = chlorosis threshold field.
    /// The shader shows a lesion where the week's severity exceeds G, so lesions grow from seed spots as
    /// the severity rises and shrink back as it falls. Both fields are percentile-ranked over the leaf
    /// pixels, so a severity of x covers roughly the same share of leaf area on every plant.
    /// Masks are at most 1024 px, linear data textures; nothing here is imported at 4096.
    /// Purely visual and illustrative; it makes no claim about real disease progression.
    /// </summary>
    static class LabDiseaseMasks
    {
        const int MaxSize = 1024;
        const float LesionCells = 26f;     // seed grid across the UV range
        const float LesionCoverage = 1.5f; // G = rank * 1.5: severity 0.85 covers about 57% of the leaf area

        static int SeedFor(string plantId)
        {
            unchecked
            {
                var h = 17;
                foreach (var c in plantId)
                    h = h * 31 + c;
                return h & 0xFFFF;
            }
        }

        public static string Create(string albedoAssetPath, string maskAssetPath, string plantId)
        {
            var (pixels, width, height) = FarmDiseaseTextures.Read(albedoAssetPath);
            return Write(pixels, width, height, maskAssetPath, plantId);
        }

        public static string Create(Texture2D albedo, string maskAssetPath, string plantId)
        {
            var (pixels, width, height) = FarmDiseaseTextures.Read(albedo);
            return Write(pixels, width, height, maskAssetPath, plantId);
        }

        static string Write(Color32[] pixels, int width, int height, string maskAssetPath, string plantId)
        {
            while (width > MaxSize)
                (pixels, width, height) = FarmDiseaseTextures.Halve(pixels, width, height);

            var seed = SeedFor(plantId);
            var count = width * height;
            var leaf = new float[count];
            var lesion = new float[count];
            var chlorosis = new float[count];

            Parallel.For(0, height, y =>
            {
                var v = (y + 0.5f) / height;
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var p = pixels[i];
                    if (p.a < 8)
                        continue;
                    float r = p.r / 255f, g = p.g / 255f, b = p.b / 255f;
                    var weight = Mathf.Clamp01((g - Mathf.Max(r, b * 0.9f) - 0.005f) / 0.035f);
                    leaf[i] = weight;
                    if (weight <= 0f)
                        continue;
                    var u = (x + 0.5f) / width;
                    lesion[i] = LesionDistance(u, v, seed);
                    chlorosis[i] = FarmDiseaseTextures.Fbm(u * 7f, v * 7f, seed + 31);
                }
            });

            var lesionRank = RankTable(leaf, lesion);
            var chlorosisRank = RankTable(leaf, chlorosis);

            var output = new Color32[count];
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    if (leaf[i] <= 0f)
                    {
                        // Not a leaf: never lesioned or yellowed.
                        output[i] = new Color32(0, 255, 255, 255);
                        continue;
                    }

                    var g = Mathf.Clamp01(Rank(lesionRank, lesion[i]) * LesionCoverage);
                    var b = Rank(chlorosisRank, chlorosis[i]);
                    output[i] = new Color32((byte)Mathf.Round(leaf[i] * 255f), (byte)Mathf.Round(g * 255f), (byte)Mathf.Round(b * 255f), 255);
                }
            });

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(output);
            texture.Apply();
            var bytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            ProjectPaths.EnsureFolder(Path.GetDirectoryName(maskAssetPath)?.Replace('\\', '/'));
            File.WriteAllBytes(maskAssetPath, bytes);
            AssetDatabase.ImportAsset(maskAssetPath, ImportAssetOptions.ForceUpdate);
            TextureSettings.ConfigureData(maskAssetPath, MaxSize);
            return maskAssetPath;
        }

        /// <summary>Distance to the nearest lesion seed (0 at a seed), slightly roughened. Seeds sit in a jittered grid.</summary>
        static float LesionDistance(float u, float v, int seed)
        {
            var px = u * LesionCells;
            var py = v * LesionCells;
            var cx = Mathf.FloorToInt(px);
            var cy = Mathf.FloorToInt(py);
            var best = 2f;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (FarmDiseaseTextures.Hash01(nx, ny, seed + 1) > 0.7f)
                        continue; // this cell has no seed
                    var sx = nx + 0.25f + 0.5f * FarmDiseaseTextures.Hash01(nx, ny, seed + 2);
                    var sy = ny + 0.25f + 0.5f * FarmDiseaseTextures.Hash01(nx, ny, seed + 3);
                    var ddx = px - sx;
                    var ddy = py - sy;
                    var distance = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    // Per-seed size, so some lesions grow faster than others.
                    distance /= 0.7f + 0.6f * FarmDiseaseTextures.Hash01(nx, ny, seed + 4);
                    best = Mathf.Min(best, distance);
                }
            }

            var rough = FarmDiseaseTextures.ValueNoise(px * 4f, py * 4f, seed + 5) - 0.5f;
            return best + rough * 0.25f;
        }

        /// <summary>Sorted values of the leaf pixels, used to convert a value into its percentile.</summary>
        static float[] RankTable(float[] leaf, float[] values)
        {
            var list = new System.Collections.Generic.List<float>(values.Length / 2);
            for (var i = 0; i < values.Length; i++)
            {
                if (leaf[i] > 0f)
                    list.Add(values[i]);
            }

            var table = list.ToArray();
            Array.Sort(table);
            return table;
        }

        static float Rank(float[] table, float value)
        {
            if (table.Length == 0)
                return 1f;
            var index = Array.BinarySearch(table, value);
            if (index < 0)
                index = ~index;
            return Mathf.Clamp01((float)index / table.Length);
        }
    }
}
