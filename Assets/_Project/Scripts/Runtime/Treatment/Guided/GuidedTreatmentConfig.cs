using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Gameplay values of the guided, hands-on chamber treatment (Resources/BTP/GuidedTreatmentConfig; built by
    /// BTP/Setup/NPK Treatment Assets). The four steps read their targets from the case's <see cref="TreatmentProfile"/>:
    /// temperature from the day/night bands, scoops from the fertiliser dose band, sprays from the spray dose band and the
    /// water meter from the root-zone band. When the profile prescribes nothing for a factor (no supplement, monitoring
    /// only) the maintenance defaults below are used so every case still teaches all four steps; per-case overrides win
    /// over both. Missing asset: <see cref="Load"/> returns an in-memory default.
    /// </summary>
    [CreateAssetMenu(menuName = "BTP/Guided Treatment Config", fileName = "GuidedTreatmentConfig")]
    public sealed class GuidedTreatmentConfig : ScriptableObject
    {
        public const string ResourcePath = "BTP/GuidedTreatmentConfig";

        /// <summary>One case's overrides ("tomato/early_blight"); a negative number keeps the profile-derived value.</summary>
        [Serializable]
        public sealed class CaseOverride
        {
            [Tooltip("Case id as the API names it: plantId/diseaseId.")]
            public string caseId;
            [Tooltip("Scoops of fertiliser (negative: from the profile / default).")]
            public int scoops = -1;
            [Tooltip("Sprays (negative: from the profile / default).")]
            public int sprays = -1;
            [Tooltip("Water meter setting in % (NaN or below -100: from the profile).")]
            public float waterAmount = float.NaN;
            [Tooltip("Fertiliser product id (N, P, K, NPK); empty: from the profile's nutrient.")]
            public string product;
        }

        /// <summary>A fertiliser packet on the bench.</summary>
        [Serializable]
        public sealed class Product
        {
            public string id = "NPK";
            [Tooltip("Big packet label.")]
            public string label = "NPK 19-19-19";
            [Tooltip("Profile nutrient names that select this product (case-insensitive).")]
            public string[] nutrients = Array.Empty<string>();
            public Color bandColor = new Color(0.2f, 0.62f, 0.3f);
            [Tooltip("Second label line printed on the sack (e.g. \"Di-ammonium phosphate\").")]
            public string fullName = "";

            [Header("Granules (no colours: the built-in look for the id)")]
            [Tooltip("Granule colours (sRGB); empty uses the built-in palette for the id (N urea, P DAP, K MOP, NPK).")]
            public Color[] granuleColors = Array.Empty<Color>();
            [Tooltip("Relative share of each colour.")]
            public float[] granuleWeights = Array.Empty<float>();
            public GranuleShape granuleShape = GranuleShape.Granule;
            [Tooltip("Granule diameter range (mm).")]
            public Vector2 granuleSizeMm = new Vector2(2f, 4f);
            [Range(0f, 1f)] public float granuleGloss = 0.55f;
            [Range(0f, 1f)] public float granuleSpeckle = 0.4f;

            [Header("Generated (BTP/Setup/NPK Treatment Assets)")]
            [Tooltip("The sack with this product's printed label.")]
            public Material packetMaterial;
            [Tooltip("Dense granule bed (the sack's fill and the scoop's heap under the 3D granules).")]
            public Material bedMaterial;
        }

        [Header("Mode")]
        [Tooltip("Run the guided, hands-on treatment in the chamber (off: the original four-control kiosk flow).")]
        public bool guidedEnabled = true;

        [Header("Fertiliser")]
        public List<Product> products = new List<Product>
        {
            new Product { id = "N", label = "Urea 46-0-0", fullName = "Prilled urea (46% N)", nutrients = new[] { "nitrogen", "n" }, bandColor = new Color(0.22f, 0.45f, 0.85f) },
            new Product { id = "P", label = "DAP 18-46-0", fullName = "Di-ammonium phosphate", nutrients = new[] { "phosphorus", "phosphate", "p" }, bandColor = new Color(0.85f, 0.42f, 0.18f) },
            new Product { id = "K", label = "MOP 0-0-60", fullName = "Muriate of potash (KCl)", nutrients = new[] { "potassium", "potash", "k" }, bandColor = new Color(0.78f, 0.22f, 0.24f) },
            new Product { id = "NPK", label = "NPK 19-19-19", fullName = "Complex fertiliser, granular", nutrients = new[] { "npk", "balanced" }, bandColor = new Color(0.2f, 0.62f, 0.3f) },
        };
        [Tooltip("Product used when the profile confirms no nutrient (balanced maintenance feed).")]
        public string maintenanceProduct = "NPK";
        [Tooltip("Scoops when the profile prescribes no supplement.")]
        [Min(1)] public int maintenanceScoops = 2;
        [Tooltip("Profile fertiliser training units per scoop (a dose of 4 units at 2 per scoop = 2 scoops).")]
        [Min(0.1f)] public float doseUnitsPerScoop = 2f;
        [Tooltip("Share of a scoop's granules that must settle in the root zone for the scoop to count.")]
        [Range(0.1f, 1f)] public float scoopLandingShare = 0.6f;
        [Tooltip("Root-zone radius around the stem (metres) on top of the plant's base radius.")]
        [Min(0.02f)] public float rootZoneMargin = 0.09f;
        [Min(4)] public int granulesPerScoop = 40;

        [Header("Granule visuals")]
        [Tooltip("BTP/Fertiliser Granule material (GPU-instanced granules). Missing: the simple granule mesh is drawn instead.")]
        public Material granuleMaterial;
        [Tooltip("Drawn granules per simulated (counted) granule of a pour.")]
        [Range(1, 8)] public int visualGranulesPerGranule = 7;
        [Tooltip("3D granules on the visible top of each sack's fill.")]
        [Range(0, 900)] public int packetHeapGranules = 380;
        [Tooltip("3D granules on the full scoop's heap.")]
        [Range(0, 600)] public int scoopHeapGranules = 300;
        [Tooltip("Beyond this distance (m) from the head granules use the 20-triangle mesh.")]
        [Min(0.2f)] public float granuleLodDistance = 1.5f;
        [Tooltip("Radius (m) of the hollow a scoop leaves in the sack's top layer of granules.")]
        [Range(0f, 0.05f)] public float scoopHollowRadius = 0.022f;

        [Header("Spray")]
        [Tooltip("Sprays when the profile indicates no spray (protective training spray).")]
        [Min(1)] public int maintenanceSprays = 3;
        [Tooltip("Profile spray training units per spray press.")]
        [Min(0.1f)] public float sprayUnitsPerPress = 1f;
        [Tooltip("Seconds of mist per trigger press.")]
        [Min(0.1f)] public float sprayBurstSeconds = 0.45f;

        [Header("Water")]
        [Tooltip("Round the water meter target to this many percent.")]
        [Min(1f)] public float waterRounding = 1f;

        [Header("Sunlight")]
        [Tooltip("Seconds of the lighting response after a correct temperature Apply.")]
        [Min(1f)] public float sunlightSeconds = 4.5f;
        [Tooltip("Disease removed by the sunlight response (one colour step).")]
        [Range(0f, 0.5f)] public float sunlightColourStep = 0.15f;

        [Header("Tools")]
        [Tooltip("Released within this distance (m) of its holder, a tool settles back into it.")]
        [Min(0.05f)] public float homeSnapDistance = 0.25f;
        [Min(0.05f)] public float homeSettleSeconds = 0.25f;
        [Tooltip("A dropped tool returns home after lying still this long (s).")]
        [Min(1f)] public float idleReturnSeconds = 8f;

        [Header("Bench props")]
        [Tooltip("Small lab-dressing props on the benches nearest the chamber that become grabbable in Play mode (hierarchy paths; " +
                 "run BTP/Treatment/Make Bench Props Grabbable once so they are not statically batched).")]
        public List<string> benchProps = new List<string>
        {
            "Lab Dressing/SprayMixingBench/Jug", "Lab Dressing/SprayMixingBench/Bottle", "Lab Dressing/SprayMixingBench/StirPaddle",
            "Lab Dressing/SprayMixingBench/SprayBottle", "Lab Dressing/SprayMixingBench/PowderBag", "Lab Dressing/SprayMixingBench/Scale",
            "Lab Dressing/SamplePrepIsland/Beaker", "Lab Dressing/SamplePrepIsland/ConicalFlask", "Lab Dressing/SamplePrepIsland/Reagent",
            "Lab Dressing/SamplePrepIsland/PhMeter",
        };

        [Header("Models (optional; procedural fallbacks are built without them)")]
        public GameObject packetModel;
        public Material packetMaterial;
        [Tooltip("Yaw that turns the model's printed front towards the packet's front (-X).")]
        public float packetModelYaw = 180f;
        [Tooltip("The packet model is scaled so its larger footprint side is this long (m).")]
        [Min(0.05f)] public float packetFootprint = 0.16f;
        [Tooltip("UV circle (centre xy, radius z) of the model's contents surface (the open sack's fill); radius 0: none.")]
        public Vector4 packetContentsUv = Vector4.zero;
        public GameObject scoopModel;
        public Material scoopMaterial;

        [Header("Per-case overrides")]
        public List<CaseOverride> overrides = new List<CaseOverride>();

        static GuidedTreatmentConfig cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cached = null;

        /// <summary>The project's config (Resources), or a default instance when the asset does not exist.</summary>
        public static GuidedTreatmentConfig Load()
        {
            if (cached != null)
                return cached;
            cached = Resources.Load<GuidedTreatmentConfig>(ResourcePath);
            if (cached == null)
            {
                cached = CreateInstance<GuidedTreatmentConfig>();
                cached.hideFlags = HideFlags.DontSave;
            }

            return cached;
        }

        public CaseOverride OverrideFor(string caseId)
        {
            if (string.IsNullOrEmpty(caseId))
                return null;
            foreach (var entry in overrides)
                if (entry != null && string.Equals(entry.caseId, caseId, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }

        public Product FindProduct(string id)
        {
            foreach (var product in products)
                if (product != null && string.Equals(product.id, id, StringComparison.OrdinalIgnoreCase))
                    return product;
            return null;
        }

        /// <summary>The product for a profile nutrient ("Potassium" -> K); the maintenance product when none is confirmed.</summary>
        public Product ProductForNutrient(string nutrient)
        {
            if (!string.IsNullOrWhiteSpace(nutrient))
            {
                var key = nutrient.Trim().ToLowerInvariant();
                foreach (var product in products)
                {
                    if (product?.nutrients == null)
                        continue;
                    foreach (var name in product.nutrients)
                        if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                            return product;
                }
            }

            return FindProduct(maintenanceProduct) ?? (products.Count > 0 ? products[0] : new Product());
        }
    }
}
