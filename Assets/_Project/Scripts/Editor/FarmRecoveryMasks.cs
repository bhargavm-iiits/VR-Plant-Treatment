using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Recovery threshold masks for the BTP/Farm Recovery shader. R holds, per texel, the percentile of its
    /// distance to the nearest lesion seed (same seeds as the Lab masks), so as the disease amount falls the
    /// diseased albedo recedes towards the seeds. Non-leaf texels are 0; they look the same in both albedos.
    /// Masks are linear data textures of at most 1024 px. Illustrative only.
    /// </summary>
    static class FarmRecoveryMasks
    {
        const int MaxSize = 1024;
        const int NoiseSize = 256;

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

            var seed = LabDiseaseMasks.SeedFor(plantId);
            var count = width * height;
            var leaf = new float[count];
            var distance = new float[count];
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
                    // A looser leaf test than the disease bake, so every texel the bake changed gets a threshold.
                    var weight = Mathf.Clamp01((g - Mathf.Max(r, b * 0.9f) + 0.02f) / 0.03f);
                    leaf[i] = weight;
                    if (weight > 0f)
                        distance[i] = LabDiseaseMasks.LesionDistance((x + 0.5f) / width, v, seed);
                }
            });

            var table = LabDiseaseMasks.RankTable(leaf, distance);
            var output = new Color32[count];
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var value = leaf[i] > 0f ? (byte)Mathf.Round(LabDiseaseMasks.Rank(table, distance[i]) * 255f) : (byte)0;
                    output[i] = new Color32(value, value, value, 255);
                }
            });

            return Save(output, width, height, maskAssetPath);
        }

        /// <summary>A plant-independent, tiling threshold noise for billboard atlases (percentile-ranked, so uniform).</summary>
        public static string CreateNoise(string maskAssetPath)
        {
            var count = NoiseSize * NoiseSize;
            var leaf = new float[count];
            var values = new float[count];
            for (var y = 0; y < NoiseSize; y++)
            {
                for (var x = 0; x < NoiseSize; x++)
                {
                    var i = y * NoiseSize + x;
                    leaf[i] = 1f;
                    values[i] = LabDiseaseMasks.LesionDistance((x + 0.5f) / NoiseSize, (y + 0.5f) / NoiseSize, 4242);
                }
            }

            var table = LabDiseaseMasks.RankTable(leaf, values);
            var output = new Color32[count];
            for (var i = 0; i < count; i++)
            {
                var value = (byte)Mathf.Round(LabDiseaseMasks.Rank(table, values[i]) * 255f);
                output[i] = new Color32(value, value, value, 255);
            }

            Save(output, NoiseSize, NoiseSize, maskAssetPath);
            // Billboards tile this noise across their atlas, so it must repeat.
            var importer = (TextureImporter)AssetImporter.GetAtPath(maskAssetPath);
            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }

            return maskAssetPath;
        }

        static string Save(Color32[] output, int width, int height, string maskAssetPath)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(output);
            texture.Apply();
            var bytes = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            ProjectPaths.EnsureFolder(Path.GetDirectoryName(maskAssetPath)?.Replace('\\', '/'));
            File.WriteAllBytes(maskAssetPath, bytes);
            AssetDatabase.ImportAsset(maskAssetPath, ImportAssetOptions.ForceUpdate);
            TextureSettings.ConfigureData(maskAssetPath, MaxSize);
            return maskAssetPath;
        }
    }
}
