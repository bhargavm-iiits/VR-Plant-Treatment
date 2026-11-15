using System.IO;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Small tileable textures for the lightweight Farm and Lab geometry, plus the rounded
    /// sprites used by the world-space UI. Generated once into Assets/_Project/Textures.
    /// </summary>
    static class ProceduralTextures
    {
        const string Folder = ProjectPaths.Root + "/Textures/Environment";
        const string UIFolder = ProjectPaths.Root + "/Textures/UI";

        public static Texture2D Grass => Noise("Grass", new Color(0.30f, 0.45f, 0.17f), new Color(0.46f, 0.58f, 0.24f), new Color(0.55f, 0.52f, 0.28f), 11);
        public static Texture2D Soil => Noise("Soil", new Color(0.23f, 0.15f, 0.09f), new Color(0.36f, 0.24f, 0.14f), new Color(0.44f, 0.33f, 0.22f), 23);
        public static Texture2D DirtPath => Noise("DirtPath", new Color(0.52f, 0.38f, 0.25f), new Color(0.66f, 0.51f, 0.35f), new Color(0.58f, 0.49f, 0.39f), 37);
        public static Texture2D Wood => Planks("Wood", new Color(0.36f, 0.24f, 0.14f), new Color(0.52f, 0.36f, 0.22f));
        public static Texture2D FloorTiles => Tiles("FloorTiles", new Color(0.80f, 0.82f, 0.84f), new Color(0.66f, 0.69f, 0.72f));
        public static Texture2D Mountain => VerticalGradient("Mountain", new[]
        {
            (0.00f, new Color(0.22f, 0.33f, 0.18f)),
            (0.45f, new Color(0.30f, 0.40f, 0.26f)),
            (0.70f, new Color(0.43f, 0.45f, 0.44f)),
            (0.90f, new Color(0.60f, 0.62f, 0.64f)),
            (1.00f, new Color(0.88f, 0.90f, 0.93f)),
        });

        /// <summary>Low rolling hills on the Farm horizon: olive at the foot to a pale grey-green crest (fog adds the haze).</summary>
        public static Texture2D Hills => VerticalGradient("Hills", new[]
        {
            (0.00f, new Color(0.29f, 0.35f, 0.21f)),
            (0.40f, new Color(0.35f, 0.41f, 0.26f)),
            (0.75f, new Color(0.42f, 0.46f, 0.35f)),
            (1.00f, new Color(0.52f, 0.55f, 0.48f)),
        });

        /// <summary>Dry, pale roadside grass for the verge beside the road.</summary>
        public static Texture2D Verge => Noise("Verge", new Color(0.43f, 0.43f, 0.25f), new Color(0.62f, 0.59f, 0.38f), new Color(0.72f, 0.66f, 0.47f), 53);

        /// <summary>
        /// Asphalt across one road width (V) and one repeat along it (U), with the white edge lines painted in,
        /// so the markings cannot z-fight with the road surface.
        /// </summary>
        public static Texture2D Road => RoadTexture("Road", 512);

        /// <summary>
        /// The greenhouse's polyethylene film, one tile per 4 x 4 m (the cover mesh's UVs are in metres): faint
        /// wrinkles running across U and one overlapping sheet seam across V, on near-white. Alpha (0.8 to 1) scales
        /// the material's own, so the seam and wrinkle crests read slightly more opaque. 128 px, about 3 cm a texel.
        /// </summary>
        public static Texture2D GreenhouseFilm => FilmTexture("GreenhouseFilm", 128);

        /// <summary>9-sliced rounded rectangle (fill) and outline sprites for panels and buttons.</summary>
        public static Sprite RoundedFill => RoundedSprite("RoundedFill", 0);
        public static Sprite RoundedOutline => RoundedSprite("RoundedOutline", 4);

        static Texture2D Noise(string name, Color dark, Color light, Color accent, int seed, int size = 256)
        {
            var path = $"{Folder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            var random = new System.Random(seed);
            var offset = new Vector2((float)random.NextDouble() * 100f, (float)random.NextDouble() * 100f);
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var n = TileableNoise(x, y, size, 8, offset) * 0.6f + TileableNoise(x, y, size, 32, offset * 2f) * 0.4f;
                    var speck = TileableNoise(x, y, size, 64, offset * 3f);
                    var color = Color.Lerp(dark, light, n);
                    color = Color.Lerp(color, accent, Mathf.Clamp01((speck - 0.7f) * 2.5f));
                    pixels[y * size + x] = color;
                }
            }

            return Save(path, pixels, size, size, TextureWrapMode.Repeat);
        }

        static Texture2D Planks(string name, Color dark, Color light, int size = 256)
        {
            var path = $"{Folder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            var pixels = new Color[size * size];
            var offset = new Vector2(13f, 71f);
            for (var y = 0; y < size; y++)
            {
                var plank = y / (size / 4);
                for (var x = 0; x < size; x++)
                {
                    var grain = TileableNoise(x, y, size, 4, offset + Vector2.one * plank) * 0.5f +
                                Mathf.PerlinNoise(x * 0.02f + plank * 7f, y * 0.4f) * 0.5f;
                    var seam = (y % (size / 4)) < 2 ? 0.45f : 1f;
                    pixels[y * size + x] = Color.Lerp(dark, light, grain) * seam;
                }
            }

            return Save(path, pixels, size, size, TextureWrapMode.Repeat);
        }

        static Texture2D Tiles(string name, Color tile, Color grout, int size = 256)
        {
            var path = $"{Folder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            var pixels = new Color[size * size];
            var offset = new Vector2(3f, 5f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var edge = x % (size / 2) < 3 || y % (size / 2) < 3;
                    var n = TileableNoise(x, y, size, 16, offset) * 0.06f;
                    pixels[y * size + x] = edge ? grout : tile * (0.97f + n);
                }
            }

            return Save(path, pixels, size, size, TextureWrapMode.Repeat);
        }

        static Texture2D VerticalGradient(string name, (float t, Color color)[] stops, int height = 128)
        {
            var path = $"{Folder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            const int width = 4;
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                var t = y / (height - 1f);
                var color = stops[stops.Length - 1].color;
                for (var i = 0; i < stops.Length - 1; i++)
                {
                    if (t <= stops[i + 1].t)
                    {
                        color = Color.Lerp(stops[i].color, stops[i + 1].color, Mathf.InverseLerp(stops[i].t, stops[i + 1].t, t));
                        break;
                    }
                }

                for (var x = 0; x < width; x++)
                    pixels[y * width + x] = color;
            }

            return Save(path, pixels, width, height, TextureWrapMode.Clamp);
        }

        static Texture2D RoadTexture(string name, int size)
        {
            var path = $"{Folder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            var dark = new Color(0.19f, 0.19f, 0.20f);
            var light = new Color(0.29f, 0.29f, 0.30f);
            var paint = new Color(0.90f, 0.90f, 0.88f);
            var offset = new Vector2(29f, 7f);
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                var v = (y + 0.5f) / size;
                // Edge lines about 0.2 m wide, 0.25 m in from each edge of a 6.5 m road.
                var line = (v > 0.038f && v < 0.068f) || (v > 0.932f && v < 0.962f);
                for (var x = 0; x < size; x++)
                {
                    var n = TileableNoise(x, y, size, 16, offset) * 0.5f + TileableNoise(x, y, size, 64, offset * 2f) * 0.5f;
                    var speck = TileableNoise(x, y, size, 128, offset * 3f);
                    var color = Color.Lerp(dark, light, n);
                    color = Color.Lerp(color, new Color(0.40f, 0.40f, 0.40f), Mathf.Clamp01((speck - 0.75f) * 3f));
                    if (line)
                        color = Color.Lerp(paint, color, Mathf.Clamp01((TileableNoise(x, y, size, 32, offset * 5f) - 0.72f) * 2.5f));
                    pixels[y * size + x] = color;
                }
            }

            return Save(path, pixels, size, size, TextureWrapMode.Repeat, size);
        }

        static Texture2D FilmTexture(string name, int size)
        {
            var path = $"{Folder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            var offset = new Vector2(41f, 17f);
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                var v = (y + 0.5f) / size;
                // A sheet overlap across the tile: a slightly denser band with a fine edge line either side.
                var seamDistance = Mathf.Abs(v - 0.5f) * size;
                var seam = Mathf.Clamp01(2.5f - seamDistance) * 0.6f + Mathf.Clamp01(1.2f - Mathf.Abs(seamDistance - 3f)) * 0.4f;
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size;
                    // Wrinkles: wavy stripes along V (integer frequencies keep the tile seamless).
                    var wander = Mathf.Sin(v * Mathf.PI * 4f + u * Mathf.PI * 2f) * 0.18f;
                    var wrinkle = Mathf.Pow(0.5f + 0.5f * Mathf.Sin((u * 7f + wander) * Mathf.PI * 2f), 3f);
                    var mottle = TileableNoise(x, y, size, 4, offset);
                    var shade = 0.96f + wrinkle * 0.018f + mottle * 0.015f;
                    var alpha = 0.84f + wrinkle * 0.06f + mottle * 0.06f;
                    shade = Mathf.Lerp(shade, 0.985f, seam);
                    alpha = Mathf.Lerp(alpha, 1f, seam);
                    pixels[y * size + x] = new Color(shade, shade, shade, alpha);
                }
            }

            return Save(path, pixels, size, size, TextureWrapMode.Repeat, size);
        }

        static Sprite RoundedSprite(string name, int stroke)
        {
            var path = $"{UIFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
                return existing;

            const int size = 64;
            const float radius = 20f;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Signed distance to a rounded square filling the texture.
                    var px = Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius);
                    var py = Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius);
                    var outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    var alpha = Mathf.Clamp01(0.5f - outside);
                    if (stroke > 0)
                        alpha *= Mathf.Clamp01(outside + stroke + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            ProjectPaths.EnsureFolder(UIFolder);
            WritePng(path, pixels, size, size);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(24, 24, 24, 24);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static float TileableNoise(int x, int y, int size, int frequency, Vector2 offset)
        {
            // Sample Perlin noise on a torus so the texture tiles seamlessly.
            var u = (float)x / size * Mathf.PI * 2f;
            var v = (float)y / size * Mathf.PI * 2f;
            var r = frequency / (Mathf.PI * 2f);
            var a = Mathf.PerlinNoise(offset.x + Mathf.Cos(u) * r, offset.y + Mathf.Sin(u) * r);
            var b = Mathf.PerlinNoise(offset.x + 50f + Mathf.Cos(v) * r, offset.y + 50f + Mathf.Sin(v) * r);
            return Mathf.Clamp01((a + b) * 0.5f * 1.3f - 0.15f);
        }

        static Texture2D Save(string path, Color[] pixels, int width, int height, TextureWrapMode wrap, int maxSize = 256)
        {
            ProjectPaths.EnsureFolder(Folder);
            WritePng(path, pixels, width, height);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.maxTextureSize = maxSize;
            if (maxSize > 256)
                importer.anisoLevel = 8; // the road is seen at grazing angles
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = maxSize, format = TextureImporterFormat.ASTC_6x6
            });
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void WritePng(string path, Color[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
}
