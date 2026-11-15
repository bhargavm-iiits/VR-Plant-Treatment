using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Per-disease illustrative bakes for the Farm disease rows of plants whose model has no per-disease leaf of
    /// its own (corn and potato): a copy of the plant's albedo with that disease's typical lesion pattern on the
    /// leaf-coloured pixels only, so fruit, stems and soil keep their colour (the same leaf test as
    /// <see cref="FarmDiseaseTextures"/>, whose per-plant bakes the base prefabs keep using).
    ///   corn common rust: small, dense cinnamon-brown pustules;
    ///   corn gray leaf spot: narrow grey-tan rectangular lesions running with the veins;
    ///   corn northern leaf blight: long tan "cigar" lesions with darker margins;
    ///   potato early blight: dark-brown concentric target spots with a yellow halo;
    ///   potato late blight: large irregular dark water-soaked patches with pale margins.
    /// Lesion colours are blended from a hand-tuned base and the lesion pixels of the disease's PlantVillage photo
    /// (Textures/DiseaseLeaves), so each row reads like its photo. The corn atlases run their leaf blades along V,
    /// so elongated lesions are stretched along V. Purely visual; no diagnosis is implied.
    /// </summary>
    static class DiseaseLesionTextures
    {
        enum Shape { Pustule, Streak, Cigar, Target, Blotch }

        sealed class Style
        {
            public Shape Shape;
            public float Yellow;      // 0..1: overall chlorosis
            public float Necrosis;    // share of leaf area that dries out (cell shapes only)
            public float CellPx;      // lesion grid cell width in texels at 2048 px
            public float Aspect = 1f; // cell (and lesion) height / width; > 1 stretches lesions along V
            public float Density;     // share of cells that carry a lesion; for Blotch, the leaf area covered
            public float Size;        // lesion half-width as a share of a cell
            public Color Core, Ring, Halo;
            public int Seed;
        }

        /// <summary>Weight of the PlantVillage photo's lesion colours against the hand-tuned ones.</summary>
        const float PhotoWeight = 0.5f;

        /// <summary>Whether this plant/disease has a per-disease bake (otherwise the variant reuses its leaves).</summary>
        public static bool Has(string plantId, string diseaseId) => HandStyle(plantId, diseaseId) != null;

        static Style HandStyle(string plantId, string diseaseId)
        {
            switch ($"{plantId}/{diseaseId}")
            {
                case "corn/common_rust":
                    return new Style
                    {
                        Shape = Shape.Pustule, Yellow = 0.14f, Necrosis = 0f, CellPx = 14f, Density = 0.72f, Size = 0.22f, Seed = 101,
                        Core = new Color(0.62f, 0.30f, 0.10f), Ring = new Color(0.40f, 0.17f, 0.06f), Halo = new Color(0.86f, 0.80f, 0.36f),
                    };
                case "corn/gray_leaf_spot":
                    return new Style
                    {
                        Shape = Shape.Streak, Yellow = 0.20f, Necrosis = 0.06f, CellPx = 16f, Aspect = 4f, Density = 0.50f, Size = 0.22f, Seed = 103,
                        Core = new Color(0.66f, 0.62f, 0.52f), Ring = new Color(0.47f, 0.43f, 0.34f), Halo = new Color(0.86f, 0.84f, 0.48f),
                    };
                case "corn/northern_leaf_blight":
                    return new Style
                    {
                        Shape = Shape.Cigar, Yellow = 0.14f, Necrosis = 0.08f, CellPx = 34f, Aspect = 5f, Density = 0.36f, Size = 0.26f, Seed = 107,
                        Core = new Color(0.70f, 0.62f, 0.45f), Ring = new Color(0.36f, 0.30f, 0.20f), Halo = new Color(0.78f, 0.80f, 0.46f),
                    };
                case "potato/early_blight":
                    return new Style
                    {
                        Shape = Shape.Target, Yellow = 0.32f, Necrosis = 0.05f, CellPx = 52f, Density = 0.4f, Size = 0.3f, Seed = 109,
                        Core = new Color(0.22f, 0.16f, 0.09f), Ring = new Color(0.40f, 0.31f, 0.18f), Halo = new Color(0.98f, 0.88f, 0.30f),
                    };
                case "potato/late_blight":
                    return new Style
                    {
                        Shape = Shape.Blotch, Yellow = 0.10f, Necrosis = 0f, CellPx = 0f, Density = 0.42f, Size = 0f, Seed = 113,
                        Core = new Color(0.24f, 0.21f, 0.13f), Ring = new Color(0.14f, 0.14f, 0.09f), Halo = new Color(0.76f, 0.79f, 0.64f),
                    };
                default:
                    return null;
            }
        }

        /// <summary>The style with its core, ring and halo blended towards the PlantVillage photo's lesion colours.</summary>
        static Style StyleFor(string plantId, string diseaseId)
        {
            var style = HandStyle(plantId, diseaseId) ?? throw new System.ArgumentException($"No lesion style for {plantId}/{diseaseId}.");
            var photo = $"{ProjectPaths.DiseaseLeaves}/{plantId}/{diseaseId}.jpg";
            if (!File.Exists(photo))
            {
                Debug.LogWarning($"[BTP] {photo} is missing; {plantId}/{diseaseId} lesions use the hand-tuned colours only.");
                return style;
            }

            var (dark, mid, light, count) = PhotoPalette(photo);
            if (count < 500)
                return style;
            // Late blight's pale margin and the yellow halos come from the hand-tuned colours: the photos are lit
            // and white-balanced differently, and a muddy halo would hide the lesion edge.
            style.Core = Color.Lerp(style.Core, style.Shape == Shape.Target || style.Shape == Shape.Blotch ? dark : mid, PhotoWeight);
            style.Ring = Color.Lerp(style.Ring, dark, PhotoWeight);
            if (style.Shape == Shape.Streak || style.Shape == Shape.Cigar)
                style.Halo = Color.Lerp(style.Halo, light, 0.3f);
            Debug.Log($"[BTP] {plantId}/{diseaseId} lesion colours from {count} photo pixels: core {style.Core}, ring {style.Ring}.");
            return style;
        }

        /// <summary>
        /// Mean dark, middle and light thirds (by luminance) of a photo's lesion pixels: saturated enough not to be
        /// the grey or black backdrop, and not leaf green. Saturation-weighted, so glare counts for little.
        /// </summary>
        static (Color dark, Color mid, Color light, int count) PhotoPalette(string photoPath)
        {
            var (pixels, width, height) = FarmDiseaseTextures.Read(photoPath);
            var samples = new System.Collections.Generic.List<(float luma, Color colour, float weight)>();
            // Every other texel in both directions is plenty for a palette.
            for (var y = 0; y < height; y += 2)
            {
                for (var x = 0; x < width; x += 2)
                {
                    var p = pixels[y * width + x];
                    float r = p.r / 255f, g = p.g / 255f, b = p.b / 255f;
                    var max = Mathf.Max(r, Mathf.Max(g, b));
                    var min = Mathf.Min(r, Mathf.Min(g, b));
                    var saturation = max > 0f ? (max - min) / max : 0f;
                    if (saturation < 0.18f || max < 0.08f || (g > r * 1.02f && g > b * 1.05f))
                        continue;
                    samples.Add((0.299f * r + 0.587f * g + 0.114f * b, new Color(r, g, b), saturation));
                }
            }

            samples.Sort((a, b) => a.luma.CompareTo(b.luma));
            Color Mean(int from, int to)
            {
                Color sum = default;
                var total = 0f;
                for (var i = from; i < to; i++)
                {
                    sum += samples[i].colour * samples[i].weight;
                    total += samples[i].weight;
                }

                return total > 0f ? sum / total : Color.grey;
            }

            var n = samples.Count;
            return (Mean(0, n / 3), Mean(n / 3, 2 * n / 3), Mean(2 * n / 3, n), n);
        }

        /// <summary>Bakes the diseased copy of a texture object (for example a glTF import) and imports it.</summary>
        public static string Create(Texture2D source, string outputPath, string plantId, string diseaseId, bool cutout)
        {
            var (pixels, width, height) = FarmDiseaseTextures.Read(source);
            return Bake(pixels, width, height, outputPath, plantId, diseaseId, cutout);
        }

        /// <summary>Bakes the diseased copy of a texture asset (read at full file resolution) and imports it.</summary>
        public static string Create(string sourceAssetPath, string outputPath, string plantId, string diseaseId, bool cutout)
        {
            var (pixels, width, height) = FarmDiseaseTextures.Read(sourceAssetPath);
            return Bake(pixels, width, height, outputPath, plantId, diseaseId, cutout);
        }

        static string Bake(Color32[] pixels, int width, int height, string outputPath, string plantId, string diseaseId, bool cutout)
        {
            // Same caps as the per-plant bakes: cut-outs up to 2048, opaque up to 4096.
            var maxSize = cutout ? 2048 : 4096;
            while (width > maxSize)
                (pixels, width, height) = FarmDiseaseTextures.Halve(pixels, width, height);
            Apply(pixels, width, height, StyleFor(plantId, diseaseId), cutout);
            return FarmDiseaseTextures.Save(pixels, width, height, outputPath, cutout);
        }

        static readonly Color Chlorosis = new Color(1.0f, 0.86f, 0.32f);

        static void Apply(Color32[] pixels, int width, int height, Style style, bool cutout)
        {
            var cellsU = style.CellPx > 0f ? 2048f / (style.CellPx * 2.4f) : 0f;
            var cellsV = cellsU / style.Aspect;
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
                    // Leaf weight: how much greener than red/blue the pixel is (as FarmDiseaseTextures).
                    var weight = Mathf.Clamp01((g - Mathf.Max(r, b * 0.9f) - 0.005f) / 0.035f);
                    if (weight <= 0f)
                    {
                        pixels[i] = pixel;
                        continue;
                    }

                    var u = (x + 0.5f) / width;
                    var lum = 0.299f * r + 0.587f * g + 0.114f * b;
                    var colour = new Color(r, g, b);

                    // Chlorosis: patchy yellowing plus a mild overall fade.
                    var field = FarmDiseaseTextures.Fbm(u * 8f, v * 8f, style.Seed);
                    var threshold = 1f - Mathf.Clamp01(style.Yellow * 1.5f);
                    var yellowing = Mathf.Clamp01(FarmDiseaseTextures.Smooth(threshold - 0.12f, threshold + 0.12f, field) + style.Yellow * 0.55f) * weight;
                    colour = Color.Lerp(colour, Shade(Chlorosis, lum * 1.35f), yellowing * 0.95f);

                    if (style.Necrosis > 0f)
                    {
                        var dry = FarmDiseaseTextures.Fbm(u * 14f + 5f, v * 14f + 9f, style.Seed + 21);
                        var cut = 1f - style.Necrosis * 1.8f;
                        var necrosis = FarmDiseaseTextures.Smooth(cut - 0.04f, cut + 0.04f, dry) * weight;
                        colour = Color.Lerp(colour, style.Core * 1.1f, necrosis * 0.85f);
                    }

                    colour = style.Shape == Shape.Blotch
                        ? Blotch(colour, u, v, lum, weight, style)
                        : CellLesion(colour, u * cellsU, v * cellsV, lum, weight, style);

                    pixel.r = (byte)Mathf.Clamp(colour.r * 255f + 0.5f, 0f, 255f);
                    pixel.g = (byte)Mathf.Clamp(colour.g * 255f + 0.5f, 0f, 255f);
                    pixel.b = (byte)Mathf.Clamp(colour.b * 255f + 0.5f, 0f, 255f);
                    pixels[i] = pixel;
                }
            });
        }

        /// <summary>A colour at the given luminance-scaled brightness, clamped to 1.</summary>
        static Color Shade(Color tint, float scale) =>
            new Color(Mathf.Min(tint.r * scale, 1f), Mathf.Min(tint.g * scale, 1f), Mathf.Min(tint.b * scale, 1f));

        /// <summary>
        /// One candidate lesion per grid cell (cells are <see cref="Style.Aspect"/> times taller than wide, so lesions
        /// stretch along V), anywhere in its cell: the nearest lesion of the 3 x 3 neighbouring cells wins, so the
        /// pattern shows no grid.
        /// </summary>
        static Color CellLesion(Color colour, float px, float py, float lum, float weight, Style style)
        {
            var cellX = Mathf.FloorToInt(px);
            var cellY = Mathf.FloorToInt(py);
            var d = float.MaxValue;
            for (var oy = -1; oy <= 1; oy++)
            {
                for (var ox = -1; ox <= 1; ox++)
                {
                    var candidate = LesionDistance(px, py, cellX + ox, cellY + oy, style);
                    if (candidate < d)
                        d = candidate;
                }
            }

            var haloReach = style.Shape == Shape.Target ? 1.7f : style.Shape == Shape.Pustule ? 1.35f : 1.4f;
            if (d < haloReach)
            {
                var halo = (1f - FarmDiseaseTextures.Smooth(1f, haloReach, d)) * 0.8f * weight;
                colour = Color.Lerp(colour, Shade(style.Halo, lum * 1.25f), halo);
            }

            if (d >= 1f)
                return colour;

            Color lesion;
            if (style.Shape == Shape.Target)
            {
                // Concentric rings: alternating dark and lighter brown bands around a dark centre.
                var rings = 0.5f + 0.5f * Mathf.Cos(d * 3.5f * 2f * Mathf.PI);
                lesion = Color.Lerp(style.Ring, style.Core, Mathf.Max(rings, 1f - FarmDiseaseTextures.Smooth(0f, 0.25f, d)));
            }
            else
            {
                // Lighter centre, darker margin.
                lesion = Color.Lerp(style.Core, style.Ring, FarmDiseaseTextures.Smooth(0.6f, 0.85f, d));
            }

            var core = (1f - FarmDiseaseTextures.Smooth(0.85f, 1f, d)) * weight;
            return Color.Lerp(colour, lesion, core);
        }

        /// <summary>Normalised distance (1 = lesion edge) from (px, py) to the lesion of one cell; MaxValue if it has none.</summary>
        static float LesionDistance(float px, float py, int cellX, int cellY, Style style)
        {
            if (FarmDiseaseTextures.Hash01(cellX, cellY, style.Seed + 1) >= style.Density)
                return float.MaxValue;
            var centreX = cellX + FarmDiseaseTextures.Hash01(cellX, cellY, style.Seed + 2);
            var centreY = cellY + FarmDiseaseTextures.Hash01(cellX, cellY, style.Seed + 3);
            var size = style.Size * (0.7f + 0.6f * FarmDiseaseTextures.Hash01(cellX, cellY, style.Seed + 4));
            var dx = (px - centreX) / size;
            // Elongated shapes span more of the (tall) cell's height than of its width.
            var dy = (py - centreY) / (size * (style.Shape == Shape.Pustule || style.Shape == Shape.Target ? 1f : 1.45f));
            var edge = 1f + 0.3f * (FarmDiseaseTextures.ValueNoise(px * 6f, py * 6f, style.Seed + 5) - 0.5f);
            switch (style.Shape)
            {
                case Shape.Streak:
                    // Rectangular, bounded by the veins: a superellipse with flat sides and slightly rounded corners.
                    return Mathf.Pow(Mathf.Pow(Mathf.Abs(dx), 6f) + Mathf.Pow(Mathf.Abs(dy), 6f), 1f / 6f) * edge;
                case Shape.Cigar:
                    // Ellipse whose ends taper: the half-width shrinks towards the tips.
                    var taper = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(Mathf.Abs(dy)));
                    return Mathf.Sqrt(dx * dx / (taper * taper) + dy * dy) * edge;
                default:
                    return Mathf.Sqrt(dx * dx + dy * dy) * edge;
            }
        }

        /// <summary>
        /// Late blight: large irregular patches from a thresholded noise field, each with a dark water-soaked rim just
        /// inside its edge and a pale margin just outside it.
        /// </summary>
        static Color Blotch(Color colour, float u, float v, float lum, float weight, Style style)
        {
            var f = FarmDiseaseTextures.Fbm(u * 3.5f + 3f, v * 3.5f + 11f, style.Seed) * 0.8f +
                    FarmDiseaseTextures.ValueNoise(u * 40f, v * 40f, style.Seed + 9) * 0.2f;
            var t = 1f - style.Density;
            var inside = FarmDiseaseTextures.Smooth(t, t + 0.015f, f);
            var margin = FarmDiseaseTextures.Smooth(t - 0.06f, t, f) * (1f - inside);
            colour = Color.Lerp(colour, Shade(style.Halo, Mathf.Max(lum * 1.2f, 0.6f)), margin * 0.85f * weight);
            if (inside <= 0f)
                return colour;

            var rim = 1f - FarmDiseaseTextures.Smooth(t + 0.015f, t + 0.06f, f);
            var mottle = FarmDiseaseTextures.ValueNoise(u * 25f, v * 25f, style.Seed + 3);
            var lesion = Color.Lerp(Color.Lerp(style.Core, style.Core * 0.8f, mottle), style.Ring, rim);
            return Color.Lerp(colour, lesion, inside * weight);
        }
    }
}
