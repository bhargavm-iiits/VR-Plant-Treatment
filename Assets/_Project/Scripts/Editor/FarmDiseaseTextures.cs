using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Bakes an illustrative "unhealthy foliage" copy of a plant's albedo texture for the Farm
    /// (yellowing, spots with halos, leaf-coloured pixels only). Fruit, stems and soil are not
    /// green, so they keep their colour. Because the effect lives in the texture, the near
    /// mesh and the baked billboard LOD are both diseased. The Lab keeps its own healthy texture.
    /// Purely visual; no diagnosis is implied and no disease data is attached.
    /// </summary>
    static class FarmDiseaseTextures
    {
        struct Style
        {
            public float Yellow;    // 0..1: how much of each leaf yellows
            public float Density;   // 0..1: share of spot cells that carry a spot
            public float CellPx;    // spot grid cell size in texels at 2048 px
            public float Radius;    // spot radius as a share of a cell
            public Color Core, Ring, Halo;
            public int Seed;
            public float Necrosis;  // share of leaf area that dries out brown
        }

        static readonly Color Chlorosis = new Color(1.0f, 0.86f, 0.32f);

        static Style StyleFor(string plantId)
        {
            Style S(float yellow, float density, float cell, float radius, Color core, Color ring, Color halo, int seed) =>
                new Style { Yellow = yellow, Density = density, CellPx = cell, Radius = radius, Core = core, Ring = ring, Halo = halo, Seed = seed };

            var style = StyleCore(plantId, S);
            style.Necrosis = plantId switch
            {
                "apple" => 0.10f, "bell_pepper" => 0.12f, "cherry" => 0.10f, "corn" => 0.22f, "grape" => 0.22f,
                "peach" => 0.18f, "potato" => 0.22f, "strawberry" => 0.10f, _ => 0.20f
            };
            return style;
        }

        static Style StyleCore(string plantId, Func<float, float, float, float, Color, Color, Color, int, Style> S)
        {
            switch (plantId)
            {
                case "apple": // scab-style: small dark olive spots, mild yellowing
                    return S(0.25f, 0.55f, 36f, 0.32f, new Color(0.10f, 0.12f, 0.05f), new Color(0.05f, 0.06f, 0.03f), new Color(0.75f, 0.80f, 0.25f), 11);
                case "bell_pepper": // many small dark spots with yellow halos
                    return S(0.30f, 0.60f, 30f, 0.28f, new Color(0.18f, 0.10f, 0.05f), new Color(0.09f, 0.05f, 0.03f), new Color(1.0f, 0.9f, 0.3f), 23);
                case "cherry": // purple-brown shot-hole style spotting
                    return S(0.30f, 0.70f, 28f, 0.22f, new Color(0.30f, 0.12f, 0.12f), new Color(0.18f, 0.06f, 0.07f), new Color(1.0f, 0.85f, 0.35f), 37);
                case "corn": // broad yellow-tan blotches
                    return S(0.45f, 0.50f, 40f, 0.40f, new Color(0.45f, 0.30f, 0.12f), new Color(0.30f, 0.18f, 0.07f), new Color(0.95f, 0.90f, 0.40f), 41);
                case "grape": // heavy yellow patches and brown leaf spots
                    return S(0.55f, 0.60f, 30f, 0.30f, new Color(0.32f, 0.20f, 0.08f), new Color(0.20f, 0.12f, 0.05f), new Color(1.0f, 0.9f, 0.3f), 53);
                case "peach": // reddish-brown blotches with yellowing
                    return S(0.40f, 0.45f, 34f, 0.38f, new Color(0.55f, 0.22f, 0.12f), new Color(0.40f, 0.12f, 0.08f), new Color(1.0f, 0.75f, 0.3f), 67);
                case "potato": // large dark blotches with pale halos
                    return S(0.35f, 0.50f, 40f, 0.45f, new Color(0.14f, 0.09f, 0.05f), new Color(0.07f, 0.05f, 0.03f), new Color(0.9f, 0.9f, 0.4f), 79);
                case "strawberry": // small purple-red leaf spots
                    return S(0.25f, 0.75f, 24f, 0.25f, new Color(0.42f, 0.10f, 0.16f), new Color(0.28f, 0.05f, 0.10f), new Color(0.9f, 0.85f, 0.4f), 83);
                default: // tomato: yellowing plus large brown ringed spots
                    return S(0.50f, 0.55f, 34f, 0.36f, new Color(0.23f, 0.15f, 0.07f), new Color(0.12f, 0.07f, 0.03f), new Color(1.0f, 0.92f, 0.35f), 97);
            }
        }

        /// <summary>Lesion core, ring and halo colours of a plant's illustrative disease, also used by the Lab shader.</summary>
        internal static (Color core, Color ring, Color halo) LesionColors(string plantId)
        {
            var style = StyleFor(plantId);
            return (style.Core, style.Ring, style.Halo);
        }

        /// <summary>Reads a texture asset at full file resolution (PNG/JPG) or through a blit (other formats).</summary>
        internal static (Color32[] pixels, int width, int height) Read(string sourceAssetPath)
        {
            var extension = Path.GetExtension(sourceAssetPath).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                raw.LoadImage(File.ReadAllBytes(sourceAssetPath));
                var result = (raw.GetPixels32(), raw.width, raw.height);
                UnityEngine.Object.DestroyImmediate(raw);
                return result;
            }

            return ReadViaBlit(AssetDatabase.LoadAssetAtPath<Texture2D>(sourceAssetPath));
        }

        /// <summary>Reads a texture object (for example a glTF import) through a blit.</summary>
        internal static (Color32[] pixels, int width, int height) Read(Texture2D source) => ReadViaBlit(source);

        /// <summary>Creates the diseased copy of a texture asset and imports it. Returns the new asset path.</summary>
        public static string Create(string sourceAssetPath, string outputPath, string plantId, bool cutout)
        {
            Color32[] pixels;
            int width, height;
            var extension = Path.GetExtension(sourceAssetPath).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                // Read the original file, not the imported (size-capped) texture, so the full resolution is kept.
                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                raw.LoadImage(File.ReadAllBytes(sourceAssetPath));
                width = raw.width;
                height = raw.height;
                pixels = raw.GetPixels32();
                UnityEngine.Object.DestroyImmediate(raw);
            }
            else
            {
                var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(sourceAssetPath);
                (pixels, width, height) = ReadViaBlit(imported);
            }

            return Write(pixels, width, height, outputPath, plantId, cutout);
        }

        /// <summary>Same as <see cref="Create(string,string,string,bool)"/> for a texture object (for example a glTF import).</summary>
        public static string Create(Texture2D source, string outputPath, string plantId, bool cutout)
        {
            var (pixels, width, height) = ReadViaBlit(source);
            return Write(pixels, width, height, outputPath, plantId, cutout);
        }

        static (Color32[] pixels, int width, int height) ReadViaBlit(Texture2D source)
        {
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var readback = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readback.Apply();
                var pixels = readback.GetPixels32();
                UnityEngine.Object.DestroyImmediate(readback);
                return (pixels, source.width, source.height);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static string Write(Color32[] pixels, int width, int height, string outputPath, string plantId, bool cutout)
        {
            // Alpha-clipped textures stay PNG at up to 2048; opaque Meshy textures keep up to 4096 as JPG (small files).
            var maxSize = cutout ? 2048 : 4096;
            while (width > maxSize)
            {
                (pixels, width, height) = Halve(pixels, width, height);
            }

            Apply(pixels, width, height, StyleFor(plantId), cutout);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            var bytes = cutout ? texture.EncodeToPNG() : texture.EncodeToJPG(93);
            UnityEngine.Object.DestroyImmediate(texture);

            ProjectPaths.EnsureFolder(Path.GetDirectoryName(outputPath)?.Replace('\\', '/'));
            File.WriteAllBytes(outputPath, bytes);
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            if (cutout)
                TextureSettings.ConfigureCutout(outputPath);
            else
                TextureSettings.ConfigureAlbedo(outputPath);
            return outputPath;
        }

        internal static (Color32[] pixels, int width, int height) Halve(Color32[] source, int width, int height)
        {
            var w = Math.Max(width / 2, 1);
            var h = Math.Max(height / 2, 1);
            var result = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (var dy = 0; dy < 2; dy++)
                    {
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var p = source[Math.Min(y * 2 + dy, height - 1) * width + Math.Min(x * 2 + dx, width - 1)];
                            r += p.r;
                            g += p.g;
                            b += p.b;
                            a += p.a;
                        }
                    }

                    result[y * w + x] = new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), (byte)(a / 4));
                }
            }

            return (result, w, h);
        }

        static void Apply(Color32[] pixels, int width, int height, Style style, bool cutout)
        {
            var cells = 2048f / (style.CellPx * 2.4f); // large lesions: roughly 15-25 px radius on a 2048 texture
            Parallel.For(0, height, y =>
            {
                var v = (y + 0.5f) / height;
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var pixel = pixels[i];
                    if (!cutout)
                        pixel.a = 255;
                    else if (pixel.a < 8)
                        continue;

                    float r = pixel.r / 255f, g = pixel.g / 255f, b = pixel.b / 255f;
                    // Leaf weight: how much greener than red/blue the pixel is. Fruit, stems and soil score 0.
                    var weight = Mathf.Clamp01((g - Mathf.Max(r, b * 0.9f) - 0.005f) / 0.035f);
                    if (weight <= 0f)
                    {
                        pixels[i] = pixel;
                        continue;
                    }

                    var u = (x + 0.5f) / width;
                    var lum = 0.299f * r + 0.587f * g + 0.114f * b;

                    // Chlorosis: patchy yellowing plus a mild overall fade.
                    var field = Fbm(u * 8f, v * 8f, style.Seed);
                    var threshold = 1f - Mathf.Clamp01(style.Yellow * 1.5f);
                    var patch = Smooth(threshold - 0.12f, threshold + 0.12f, field);
                    var yellowing = Mathf.Clamp01(patch + style.Yellow * 0.55f) * weight;
                    var yellowR = Mathf.Min(lum * 1.35f * Chlorosis.r, 1f);
                    var yellowG = Mathf.Min(lum * 1.35f * Chlorosis.g, 1f);
                    var yellowB = Mathf.Min(lum * 1.35f * Chlorosis.b, 1f);
                    r += (yellowR - r) * yellowing * 0.95f;
                    g += (yellowG - g) * yellowing * 0.95f;
                    b += (yellowB - b) * yellowing * 0.95f;

                    // Necrosis: whole dried-out brown patches of leaf.
                    var dry = Fbm(u * 14f + 5f, v * 14f + 9f, style.Seed + 21);
                    var necrosis = Smooth(1f - style.Necrosis * 1.8f - 0.04f, 1f - style.Necrosis * 1.8f + 0.04f, dry) * weight;
                    r += (style.Core.r * 1.3f - r) * necrosis * 0.92f;
                    g += (style.Core.g * 1.3f - g) * necrosis * 0.92f;
                    b += (style.Core.b * 1.3f - b) * necrosis * 0.92f;

                    // Spots: one candidate per grid cell, with an irregular edge, a pale halo and a darker rim.
                    var px = u * cells;
                    var py = v * cells;
                    var cellX = Mathf.FloorToInt(px);
                    var cellY = Mathf.FloorToInt(py);
                    var present = Hash01(cellX, cellY, style.Seed + 1) < Mathf.Min(style.Density * 1.4f, 0.95f);
                    if (present)
                    {
                        var centreX = 0.4f + 0.2f * Hash01(cellX, cellY, style.Seed + 2);
                        var centreY = 0.4f + 0.2f * Hash01(cellX, cellY, style.Seed + 3);
                        var radius = Mathf.Clamp(style.Radius * (0.6f + 0.8f * Hash01(cellX, cellY, style.Seed + 4)), 0.14f, 0.27f);
                        var dx = px - cellX - centreX;
                        var dy = py - cellY - centreY;
                        var edge = 1f + 0.35f * (ValueNoise(px * 5f, py * 5f, style.Seed + 5) - 0.5f);
                        var d = Mathf.Sqrt(dx * dx + dy * dy) / radius * edge;
                        if (d < 1.5f)
                        {
                            var halo = (1f - Smooth(1f, 1.5f, d)) * 0.85f * weight;
                            r += (Mathf.Min(lum * 1.25f * style.Halo.r, 1f) - r) * halo;
                            g += (Mathf.Min(lum * 1.25f * style.Halo.g, 1f) - g) * halo;
                            b += (Mathf.Min(lum * 1.25f * style.Halo.b, 1f) - b) * halo;
                        }

                        if (d < 1f)
                        {
                            var core = (1f - Smooth(0.88f, 1f, d)) * weight;
                            var ring = Smooth(0.62f, 0.78f, d);
                            var cr = Mathf.Lerp(style.Core.r, style.Ring.r, ring);
                            var cg = Mathf.Lerp(style.Core.g, style.Ring.g, ring);
                            var cb = Mathf.Lerp(style.Core.b, style.Ring.b, ring);
                            r += (cr - r) * core;
                            g += (cg - g) * core;
                            b += (cb - b) * core;
                        }
                    }

                    pixel.r = (byte)Mathf.Clamp(r * 255f + 0.5f, 0f, 255f);
                    pixel.g = (byte)Mathf.Clamp(g * 255f + 0.5f, 0f, 255f);
                    pixel.b = (byte)Mathf.Clamp(b * 255f + 0.5f, 0f, 255f);
                    pixels[i] = pixel;
                }
            });
        }

        internal static float Smooth(float a, float b, float x)
        {
            var t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        internal static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        internal static float ValueNoise(float x, float y, int seed)
        {
            var ix = Mathf.FloorToInt(x);
            var iy = Mathf.FloorToInt(y);
            var fx = x - ix;
            var fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            var a = Hash01(ix, iy, seed);
            var b = Hash01(ix + 1, iy, seed);
            var c = Hash01(ix, iy + 1, seed);
            var d = Hash01(ix + 1, iy + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        internal static float Fbm(float x, float y, int seed) =>
            ValueNoise(x, y, seed) * 0.55f + ValueNoise(x * 2.1f, y * 2.1f, seed + 7) * 0.30f + ValueNoise(x * 4.3f, y * 4.3f, seed + 13) * 0.15f;
    }
}
