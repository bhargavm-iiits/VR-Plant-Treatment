using System;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>Granule shape family: picks the <see cref="GranuleMeshes"/> variants a product uses.</summary>
    public enum GranuleShape
    {
        /// <summary>Smooth round prills (urea).</summary>
        Prill,
        /// <summary>Rounded, slightly lumpy granules (compound NPK, DAP).</summary>
        Granule,
        /// <summary>Angular cleaved crystals (muriate of potash).</summary>
        Crystal,
    }

    /// <summary>
    /// How one fertiliser product's granules look: a weighted colour palette (sRGB in the config, linear here when the
    /// project renders in linear space), the shape family, the diameter range and the coating's gloss and speckle. Every
    /// granule draws its colour, variant, size and rotation from a hash of its id, so a heap or a pour always looks the
    /// same. A product without colours in the config gets the built-in look for its id (N urea, P DAP, K MOP, NPK).
    /// </summary>
    public sealed class GranuleLook
    {
        public readonly Color[] Colours;
        readonly float[] cumulative;
        public readonly GranuleShape Shape;
        /// <summary>Diameter range in metres.</summary>
        public readonly float MinDiameter, MaxDiameter;
        public readonly float Gloss, Speckle;
        public Color Average { get; }

        public GranuleLook(Color[] colours, float[] weights, GranuleShape shape, Vector2 diameterMm, float gloss, float speckle)
        {
            if (colours == null || colours.Length == 0)
                colours = new[] { new Color(0.9f, 0.89f, 0.85f) };
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Colours = new Color[colours.Length];
            cumulative = new float[colours.Length];
            var total = 0f;
            var average = Color.black;
            for (var i = 0; i < colours.Length; i++)
            {
                Colours[i] = linear ? colours[i].linear : colours[i];
                var w = weights != null && i < weights.Length ? Mathf.Max(0f, weights[i]) : 1f;
                total += w;
                cumulative[i] = total;
                average += colours[i] * w;
            }

            Average = total > 0f ? average / total : colours[0];
            if (total <= 0f)
                for (var i = 0; i < cumulative.Length; i++)
                    cumulative[i] = i + 1f;
            Shape = shape;
            MinDiameter = Mathf.Max(0.5f, Mathf.Min(diameterMm.x, diameterMm.y)) * 0.001f;
            MaxDiameter = Mathf.Max(0.5f, Mathf.Max(diameterMm.x, diameterMm.y)) * 0.001f;
            Gloss = Mathf.Clamp01(gloss);
            Speckle = Mathf.Clamp01(speckle);
        }

        /// <summary>The look of <paramref name="product"/> (its config palette, else the built-in one for its id).</summary>
        public static GranuleLook For(GuidedTreatmentConfig.Product product)
        {
            if (product == null)
                return Default("NPK");
            if (product.granuleColors != null && product.granuleColors.Length > 0)
                return new GranuleLook(product.granuleColors, product.granuleWeights, product.granuleShape, product.granuleSizeMm, product.granuleGloss,
                    product.granuleSpeckle);
            return Default(product.id);
        }

        /// <summary>Built-in looks: N urea prills, P DAP granules, K MOP crystals, NPK mixed compound granules.</summary>
        public static GranuleLook Default(string id)
        {
            Defaults(id, out var colours, out var weights, out var shape, out var size, out var gloss, out var speckle);
            return new GranuleLook(colours, weights, shape, size, gloss, speckle);
        }

        public static void Defaults(string id, out Color[] colours, out float[] weights, out GranuleShape shape, out Vector2 sizeMm, out float gloss,
            out float speckle)
        {
            switch ((id ?? "").Trim().ToUpperInvariant())
            {
                case "N":
                    // Urea prills: bright white, smooth, slightly glassy.
                    colours = new[] { new Color(0.95f, 0.95f, 0.93f), new Color(0.98f, 0.98f, 0.97f), new Color(0.9f, 0.9f, 0.88f) };
                    weights = new[] { 3f, 2f, 1f };
                    shape = GranuleShape.Prill;
                    sizeMm = new Vector2(1.8f, 2.8f);
                    gloss = 0.62f;
                    speckle = 0.1f;
                    break;
                case "P":
                    // DAP: grey-brown / taupe granules with darker ones mixed in, matte.
                    colours = new[]
                    {
                        new Color(0.47f, 0.42f, 0.37f), new Color(0.56f, 0.51f, 0.45f), new Color(0.38f, 0.34f, 0.3f), new Color(0.27f, 0.24f, 0.22f),
                    };
                    weights = new[] { 3f, 3f, 2f, 2f };
                    shape = GranuleShape.Granule;
                    sizeMm = new Vector2(2.4f, 4f);
                    gloss = 0.32f;
                    speckle = 0.55f;
                    break;
                case "K":
                    // MOP: pink-red cleaved crystals with a faint crystalline sheen.
                    colours = new[]
                    {
                        new Color(0.78f, 0.38f, 0.32f), new Color(0.86f, 0.52f, 0.45f), new Color(0.63f, 0.25f, 0.21f), new Color(0.93f, 0.72f, 0.66f),
                    };
                    weights = new[] { 3f, 3f, 2f, 1f };
                    shape = GranuleShape.Crystal;
                    sizeMm = new Vector2(1.6f, 3.4f);
                    gloss = 0.5f;
                    speckle = 0.35f;
                    break;
                default:
                    // Compound NPK 19-19-19: off-white, light blue and rose granules with a waxy anti-caking coat.
                    colours = new[]
                    {
                        new Color(0.89f, 0.88f, 0.83f), new Color(0.56f, 0.75f, 0.86f), new Color(0.89f, 0.6f, 0.62f), new Color(0.74f, 0.74f, 0.71f),
                        new Color(0.45f, 0.66f, 0.8f),
                    };
                    weights = new[] { 4f, 2.5f, 3f, 1f, 1f };
                    shape = GranuleShape.Granule;
                    sizeMm = new Vector2(2f, 4f);
                    gloss = 0.56f;
                    speckle = 0.45f;
                    break;
            }
        }

        /// <summary>Variant range of the shape family (see <see cref="GranuleMeshes"/>).</summary>
        public void VariantRange(out int min, out int max)
        {
            switch (Shape)
            {
                case GranuleShape.Prill:
                    min = 0;
                    max = 2;
                    break;
                case GranuleShape.Crystal:
                    min = 5;
                    max = 7;
                    break;
                default:
                    min = 1;
                    max = 4;
                    break;
            }
        }

        /// <summary>
        /// The granule with id <paramref name="key"/>: its colour (linear rgb, a = smoothness), mesh variant, radius (m),
        /// rotation and speckle seed.
        /// </summary>
        public void Sample(uint key, out Vector4 colour, out float variant, out float radius, out Quaternion rotation, out float seed)
        {
            var h0 = GranuleMeshes.Hash(key * 0x9E3779B1u + 0x632BE5ABu);
            var h1 = GranuleMeshes.Hash(h0 ^ 0x85EBCA6Bu);
            var h2 = GranuleMeshes.Hash(h1 ^ 0xC2B2AE35u);
            var h3 = GranuleMeshes.Hash(h2 ^ 0x27D4EB2Fu);
            float U(uint h) => (h & 0xFFFFFF) / (float)0x1000000;

            var pick = U(h0) * cumulative[cumulative.Length - 1];
            var index = 0;
            while (index < cumulative.Length - 1 && pick > cumulative[index])
                index++;
            var c = Colours[index];
            // Small per-granule tone variation (batch-to-batch shade).
            var tone = 0.88f + U(h1) * 0.22f;
            colour = new Vector4(c.r * tone, c.g * tone, c.b * tone, Mathf.Clamp01(Gloss * (0.85f + U(h2 >> 8) * 0.3f)));
            VariantRange(out var min, out var max);
            variant = Mathf.Min(max, min + (int)(U(h2) * (max - min + 1)));
            // Sizes skew small (sieved product: most granules near the lower-middle of the range).
            var s = U(h3);
            radius = Mathf.Lerp(MinDiameter, MaxDiameter, Mathf.Pow(s, 1.4f)) * 0.5f;
            rotation = RandomRotation(h3 ^ 0x165667B1u);
            seed = U(GranuleMeshes.Hash(h3 + 0x9E3779B9u)) * 64f;
        }

        /// <summary>A uniformly random rotation from a hash.</summary>
        public static Quaternion RandomRotation(uint h)
        {
            var a = GranuleMeshes.Hash(h);
            var b = GranuleMeshes.Hash(a ^ 0x7FEB352Du);
            var c = GranuleMeshes.Hash(b ^ 0x846CA68Bu);
            float u1 = (a & 0xFFFFFF) / (float)0x1000000, u2 = (b & 0xFFFFFF) / (float)0x1000000, u3 = (c & 0xFFFFFF) / (float)0x1000000;
            var s1 = Mathf.Sqrt(1f - u1);
            var s2 = Mathf.Sqrt(u1);
            return new Quaternion(s1 * Mathf.Sin(2f * Mathf.PI * u2), s1 * Mathf.Cos(2f * Mathf.PI * u2), s2 * Mathf.Sin(2f * Mathf.PI * u3),
                s2 * Mathf.Cos(2f * Mathf.PI * u3));
        }
    }
}
