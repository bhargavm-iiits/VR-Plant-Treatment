using System.IO;
using BTP.Treatment;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Editor
{
    /// <summary>
    /// BTP/Setup/NPK Treatment Assets: everything the guided treatment's fertiliser needs, idempotent.
    /// <list type="bullet">
    /// <item>The open fertiliser sack: Poly Haven's "Compost Bag 02" (CC0, sack/), imported readable at file scale (the
    /// packet fits it to the bench at runtime). Per product (N/P/K/NPK) a sack texture is generated from its diffuse: the
    /// teal woven bag and black liner become white woven polypropylene, and the product's label (name, grade, nutrient
    /// line, net weight) is rendered with TextMeshPro and printed over the bag's original panel, keeping the weave.</item>
    /// <item>Per product a tiling granule-bed texture (albedo + normal, 4.5 cm tile, two layers of packed granules in the
    /// product's palette) for the sack's fill and the scoop's heap under the instanced 3D granules.</item>
    /// <item>The granule material (BTP/Fertiliser Granule) with ambientCG's Plaster 001 (CC0) micro normal/roughness.</item>
    /// <item>The grain scoop (OBJ) at 0.25 m with its PBR material, as before.</item>
    /// <item>Resources/BTP/GuidedTreatmentConfig.asset pointing at all of it, with the products' labels and granule looks.</item>
    /// </list>
    /// </summary>
    public static class NpkAssetSetup
    {
        public const string Root = "Assets/Lab_Treatment_Assets/NPK";
        public const string LegacyPacketModel = Root + "/fertilizer/model/Fertiizer.fbx";
        public const string SackModel = Root + "/sack/model/compost_bag_02_1k.fbx";
        public const string SackTextures = Root + "/sack/textures";
        public const string GranuleTextures = Root + "/granule/textures";
        public const string ScoopModel = Root + "/grain-scoop/model/Scoop.obj";
        public const string ScoopTextures = Root + "/grain-scoop/textures";
        public const string Materials = ProjectPaths.Root + "/Materials/Treatment/Npk";
        public const string Generated = ProjectPaths.Root + "/Textures/Treatment/Npk";
        public const string ConfigPath = ProjectPaths.Resources + "/GuidedTreatmentConfig.asset";
        public const string GranuleShader = "BTP/Fertiliser Granule";

        /// <summary>Raw scoop length (OBJ units): scaled to 0.25 m.</summary>
        const float ScoopRawLength = 4.7865f, ScoopSize = 0.25f;
        /// <summary>The sack's contents surface in its UVs (centre, radius) and its printed panel (pixels of the 1024 map, top-left origin).</summary>
        static readonly Vector4 ContentsUv = new Vector4(0.278f, 0.795f, 0.172f, 0f);
        static readonly RectInt PrintPanel = new RectInt(78, 560, 208, 304);
        /// <summary>Bed texture: size and the physical size of its tile (FertiliserPacket / FertiliserScoop map 4.5 cm to one tile).</summary>
        const int BedSize = 512;
        const float BedTileMetres = 0.045f;

        [MenuItem("BTP/Setup/NPK Treatment Assets")]
        public static void Run()
        {
            ConfigureModel(SackModel, 1f);
            ConfigureModel(ScoopModel, ScoopSize / ScoopRawLength);
            ProjectPaths.EnsureFolder(Generated);
            ProjectPaths.EnsureFolder(Materials);

            // Scoop (unchanged).
            TextureSettings.ConfigureAlbedo(ScoopTextures + "/Scoop_albedo.jpeg", 2048, 1024);
            TextureSettings.ConfigureNormal(ScoopTextures + "/Scoop_normal.png", 2048, 1024);
            TextureSettings.ConfigureMask(ScoopTextures + "/Scoop_AO.jpeg", 1024, 512);
            var mask = BuildMetalSmoothMask();
            var scoop = MaterialFactory.Lit(Materials + "/GrainScoop.mat", Color.white, 1f, 1f,
                AssetDatabase.LoadAssetAtPath<Texture2D>(ScoopTextures + "/Scoop_albedo.jpeg"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(ScoopTextures + "/Scoop_normal.png"), mask);
            var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(ScoopTextures + "/Scoop_AO.jpeg");
            if (ao != null)
            {
                scoop.SetTexture("_OcclusionMap", ao);
                scoop.SetFloat("_OcclusionStrength", 1f);
                scoop.EnableKeyword("_OCCLUSIONMAP");
                EditorUtility.SetDirty(scoop);
            }

            var granule = GranuleMaterial();

            var config = AssetDatabase.LoadAssetAtPath<GuidedTreatmentConfig>(ConfigPath);
            if (config == null)
            {
                ProjectPaths.EnsureFolder(ProjectPaths.Resources);
                config = ScriptableObject.CreateInstance<GuidedTreatmentConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            // Products: the commercial names and grades, the built-in granule looks written out so they can be tuned.
            var defaults = ScriptableObject.CreateInstance<GuidedTreatmentConfig>();
            foreach (var product in config.products)
            {
                var template = defaults.FindProduct(product.id);
                if (template != null)
                {
                    product.label = template.label;
                    product.fullName = template.fullName;
                    product.bandColor = template.bandColor;
                }

                GranuleLook.Defaults(product.id, out var colours, out var weights, out var shape, out var size, out var gloss, out var speckle);
                product.granuleColors = colours;
                product.granuleWeights = weights;
                product.granuleShape = shape;
                product.granuleSizeMm = size;
                product.granuleGloss = gloss;
                product.granuleSpeckle = speckle;
            }

            Object.DestroyImmediate(defaults);

            var sackNormalPath = SackTextures + "/compost_bag_02_nor_gl_1k.jpg";
            TextureSettings.ConfigureNormal(sackNormalPath, 1024, 1024);
            var sackNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(sackNormalPath);
            var sackMask = BuildSackMask();
            var sackBase = Recolour(LoadRaw(SackTextures + "/compost_bag_02_diff_1k.jpg"));
            foreach (var product in config.products)
            {
                var look = GranuleLook.For(product);
                BuildBed(product.id, look, out var bedAlbedo, out var bedNormal);
                product.bedMaterial = MaterialFactory.Lit(Materials + "/GranuleBed_" + product.id + ".mat", Color.white, 0f, look.Gloss * 0.55f, bedAlbedo,
                    bedNormal, null, normalScale: 1f);
                var sackAlbedo = BuildSackAlbedo(product, sackBase);
                var material = MaterialFactory.Lit(Materials + "/FertiliserSack_" + product.id + ".mat", Color.white, 0f, 1f, sackAlbedo, sackNormal,
                    sackMask, normalScale: 1f);
                material.SetTexture("_OcclusionMap", sackMask);
                material.SetFloat("_OcclusionStrength", 1f);
                material.EnableKeyword("_OCCLUSIONMAP");
                EditorUtility.SetDirty(material);
                product.packetMaterial = material;
            }

            config.packetModel = AssetDatabase.LoadAssetAtPath<GameObject>(SackModel);
            config.packetMaterial = config.products.Count > 0 ? config.products[0].packetMaterial : null;
            // The bag's print faces its +Z: turned to face the packet's front (-X).
            config.packetModelYaw = -90f;
            config.packetFootprint = 0.2f;
            config.packetContentsUv = ContentsUv;
            config.scoopModel = AssetDatabase.LoadAssetAtPath<GameObject>(ScoopModel);
            config.scoopMaterial = scoop;
            config.granuleMaterial = granule;
            config.visualGranulesPerGranule = 7;
            config.packetHeapGranules = 380;
            config.scoopHeapGranules = 300;
            config.scoopHollowRadius = 0.022f;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] NPK treatment assets ready: sack {Triangles(config.packetModel)} tris ({config.products.Count} printed sacks + granule beds), " +
                      $"scoop {Triangles(config.scoopModel)} tris, granule material {AssetDatabase.GetAssetPath(granule)}; config at {ConfigPath}.");
        }

        // ------------------------------------------------------------------ granule material

        static Material GranuleMaterial()
        {
            var normalPath = GranuleTextures + "/Plaster001_1K-JPG_NormalGL.jpg";
            var roughPath = GranuleTextures + "/Plaster001_1K-JPG_Roughness.jpg";
            TextureSettings.ConfigureNormal(normalPath, 1024, 512);
            TextureSettings.ConfigureMask(roughPath, 512, 256);
            var material = MaterialFactory.GetOrCreate(Materials + "/FertiliserGranule.mat", GranuleShader);
            material.SetTexture("_DetailNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetTexture("_DetailRoughness", AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ granule bed

        /// <summary>
        /// A tiling bed of packed granules (two layers, the lower one shaded by the upper) in the look's palette: albedo
        /// with the crevices dark, and a tangent-space normal map of the granules' domes.
        /// </summary>
        static void BuildBed(string id, GranuleLook look, out Texture2D albedo, out Texture2D normal)
        {
            const int n = BedSize;
            var pxPerMetre = n / BedTileMetres;
            var colours = new Color32[n * n];
            var normals = new Color32[n * n];
            // Layer cells: top layer 17 x 17, lower layer 15 x 15 offset (wraps: the tile repeats seamlessly).
            int[] cellsPerSide = { 17, 15 };
            float[] layerBase = { 0f, -0.55f };
            var grains = new Grain[2][];
            for (var layer = 0; layer < 2; layer++)
            {
                var cells = cellsPerSide[layer];
                grains[layer] = new Grain[cells * cells];
                for (var cy = 0; cy < cells; cy++)
                for (var cx = 0; cx < cells; cx++)
                {
                    var key = GranuleMeshes.Hash((uint)(layer * 7919 + cy * cells + cx) ^ (uint)id.GetHashCode());
                    look.Sample(key, out var colour, out _, out var radius, out _, out var seed);
                    var size = n / (float)cells;
                    var jx = (GranuleMeshes.Hash(key ^ 0x1234u) & 0xFFFF) / 65535f;
                    var jy = (GranuleMeshes.Hash(key ^ 0x9876u) & 0xFFFF) / 65535f;
                    grains[layer][cy * cells + cx] = new Grain
                    {
                        X = (cx + 0.2f + jx * 0.6f) * size + (layer == 1 ? size * 0.5f : 0f),
                        Y = (cy + 0.2f + jy * 0.6f) * size + (layer == 1 ? size * 0.5f : 0f),
                        R = Mathf.Min(radius * pxPerMetre * 1.05f, size * 0.62f),
                        Colour = ((Color)new Color(colour.x, colour.y, colour.z)).gamma,
                        Seed = seed,
                    };
                }
            }

            // Crevices: in shadow, but a heap of white prills stays light between its granules.
            var gap = look.Average * 0.3f;
            gap.a = 1f;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var bestH = -10f;
                Grain best = default;
                float bdx = 0f, bdy = 0f;
                for (var layer = 0; layer < 2; layer++)
                {
                    var cells = cellsPerSide[layer];
                    var size = n / (float)cells;
                    var ox = layer == 1 ? size * 0.5f : 0f;
                    var cx = Mathf.FloorToInt((x - ox) / size);
                    var cy = Mathf.FloorToInt((y - ox) / size);
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var gx = ((cx + dx) % cells + cells) % cells;
                        var gy = ((cy + dy) % cells + cells) % cells;
                        var g = grains[layer][gy * cells + gx];
                        // Nearest periodic image of the grain.
                        var ddx = Wrap(x - g.X, n);
                        var ddy = Wrap(y - g.Y, n);
                        var d2 = ddx * ddx + ddy * ddy;
                        if (d2 >= g.R * g.R)
                            continue;
                        var h = layerBase[layer] * g.R + Mathf.Sqrt(g.R * g.R - d2);
                        if (h > bestH)
                        {
                            bestH = h;
                            best = g;
                            bdx = ddx;
                            bdy = ddy;
                        }
                    }
                }

                var i = y * n + x;
                if (bestH <= -10f)
                {
                    colours[i] = (Color32)gap;
                    normals[i] = new Color32(128, 128, 255, 255);
                    continue;
                }

                // Crevice shading: low points (the lower layer, the grain's flanks) are darker.
                var ao = Mathf.Lerp(0.42f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((bestH + best.R * 0.35f) / (best.R * 1.25f))));
                var speck = GranuleMeshes.Noise(new Vector3(x * 0.35f, y * 0.35f, best.Seed * 3f));
                var fleck = Mathf.SmoothStep(0.74f, 0.84f, GranuleMeshes.Noise(new Vector3(x * 0.9f, y * 0.9f, best.Seed * 5f + 11f)));
                var c = best.Colour * Mathf.Lerp(1f, 0.8f + 0.4f * speck, look.Speckle);
                c = Color.Lerp(c, c * 0.5f, fleck * look.Speckle);
                c *= ao;
                c.a = 1f;
                colours[i] = (Color32)c;
                var nv = new Vector3(bdx / best.R, bdy / best.R, Mathf.Sqrt(Mathf.Max(0.02f, 1f - (bdx * bdx + bdy * bdy) / (best.R * best.R)))).normalized;
                nv = Vector3.Lerp(Vector3.forward, nv, 0.85f).normalized;
                normals[i] = new Color32((byte)(nv.x * 127.5f + 127.5f), (byte)(nv.y * 127.5f + 127.5f), (byte)(nv.z * 127.5f + 127.5f), 255);
            }

            var albedoPath = Generated + "/GranuleBed_" + id + ".png";
            var normalPath = Generated + "/GranuleBed_" + id + "_Normal.png";
            WritePng(albedoPath, colours, n, n);
            WritePng(normalPath, normals, n, n);
            TextureSettings.ConfigureAlbedo(albedoPath, 512, 512);
            TextureSettings.ConfigureNormal(normalPath, 512, 512);
            albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        }

        struct Grain
        {
            public float X, Y, R, Seed;
            public Color Colour;
        }

        static float Wrap(float d, int n)
        {
            if (d > n * 0.5f)
                return d - n;
            if (d < -n * 0.5f)
                return d + n;
            return d;
        }

        // ------------------------------------------------------------------ sack

        /// <summary>
        /// The bag's diffuse turned into white woven polypropylene: its broad tone (teal bag, black liner, dirt) is replaced
        /// by an off-white whose shade only follows the original's light and dark areas a little, and its fine detail
        /// (the weave, creases) is kept.
        /// </summary>
        static Color32[] Recolour(Texture2D source)
        {
            var size = source.width;
            var pixels = source.GetPixels32();
            Object.DestroyImmediate(source);
            var lum = new float[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
                lum[i] = (0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b) / 255f;
            var broad = BoxBlur(lum, size, 10);
            var white = new Color(0.9f, 0.895f, 0.865f);
            var result = new Color32[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var detail = lum[i] - broad[i];
                var tone = Mathf.Lerp(0.8f, 1f, Mathf.Clamp01((broad[i] - 0.12f) / 0.4f));
                var c = white * tone * (1f + detail * 2.4f);
                c.a = 1f;
                result[i] = (Color32)c;
            }

            return result;
        }

        static float[] BoxBlur(float[] values, int size, int radius)
        {
            var temp = new float[values.Length];
            var output = new float[values.Length];
            var span = radius * 2 + 1;
            for (var y = 0; y < size; y++)
            {
                var sum = 0f;
                for (var k = -radius; k <= radius; k++)
                    sum += values[y * size + Mathf.Clamp(k, 0, size - 1)];
                for (var x = 0; x < size; x++)
                {
                    temp[y * size + x] = sum / span;
                    sum += values[y * size + Mathf.Clamp(x + radius + 1, 0, size - 1)] - values[y * size + Mathf.Clamp(x - radius, 0, size - 1)];
                }
            }

            for (var x = 0; x < size; x++)
            {
                var sum = 0f;
                for (var k = -radius; k <= radius; k++)
                    sum += temp[Mathf.Clamp(k, 0, size - 1) * size + x];
                for (var y = 0; y < size; y++)
                {
                    output[y * size + x] = sum / span;
                    sum += temp[Mathf.Clamp(y + radius + 1, 0, size - 1) * size + x] - temp[Mathf.Clamp(y - radius, 0, size - 1) * size + x];
                }
            }

            return output;
        }

        /// <summary>The white sack with the product's label printed over the original panel (the weave shows through the ink).</summary>
        static Texture2D BuildSackAlbedo(GuidedTreatmentConfig.Product product, Color32[] baseColours)
        {
            const int size = 1024;
            var pixels = (Color32[])baseColours.Clone();
            // The label is rendered upright, then laid into the panel turned 90 degrees (the bag's print runs along V).
            var labelW = PrintPanel.height * 2;
            var labelH = PrintPanel.width * 2;
            var label = RenderLabel(product, labelW, labelH);
            var weaveMean = 0.9f;
            const int CleanRows = 120;
            // The model's own contents surface (under the fill mesh) in the granules' shade, so any sliver that shows
            // between the fill and the liner reads as granules in shadow, not white plastic.
            var shade = GranuleLook.For(product).Average * 0.45f;
            for (var row = 0; row < size; row++)
            for (var px = 0; px < size; px++)
            {
                var d = new Vector2((px + 0.5f) / size - ContentsUv.x, (row + 0.5f) / size - ContentsUv.y).magnitude;
                if (d > ContentsUv.z + 0.015f)
                    continue;
                var i = row * size + px;
                var t = Mathf.Clamp01((ContentsUv.z + 0.015f - d) / 0.01f);
                var c = (Color)pixels[i];
                var s = shade * (0.7f + 0.6f * GranuleMeshes.Noise(new Vector3(px * 0.4f, row * 0.4f, 3f)));
                s.a = 1f;
                pixels[i] = (Color32)Color.Lerp(c, s, t);
            }

            for (var py = PrintPanel.yMin; py < PrintPanel.yMax; py++)
            for (var px = PrintPanel.xMin; px < PrintPanel.xMax; px++)
            {
                // Panel space: label right runs up the image (-py), label up runs to the image's left (-px).
                var u = (PrintPanel.yMax - py - 0.5f) / PrintPanel.height;
                var v = (PrintPanel.xMax - px - 0.5f) / PrintPanel.width;
                var ink = label[Mathf.Clamp((int)(v * labelH), 0, labelH - 1) * labelW + Mathf.Clamp((int)(u * labelW), 0, labelW - 1)];
                var row = size - 1 - py;
                var i = row * size + px;
                // The weave from the clean bag above the panel (mirrored copies), so the old print does not show through.
                var k = (py - PrintPanel.yMin) % (2 * CleanRows);
                var sy = PrintPanel.yMin - 1 - (k < CleanRows ? k : 2 * CleanRows - 1 - k);
                var clean = (Color)baseColours[(size - 1 - sy) * size + px];
                var baseColour = (Color)pixels[i];
                var weave = Mathf.Clamp((clean.r + clean.g + clean.b) / 3f / weaveMean, 0.85f, 1.1f);
                // Soft edge so the print sits in the weave.
                var edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * 60f);
                var printed = (Color)ink * weave;
                printed.a = 1f;
                pixels[i] = (Color32)Color.Lerp(baseColour, printed, edge);
            }

            var path = Generated + "/FertiliserSack_" + product.id + ".png";
            WritePng(path, pixels, size, size);
            TextureSettings.ConfigureAlbedo(path, 1024, 1024);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>The printed label (upright, opaque, labelW x labelH pixels), rendered with TextMeshPro in a preview scene.</summary>
        static Color32[] RenderLabel(GuidedTreatmentConfig.Product product, int width, int height)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
            var created = new System.Collections.Generic.List<Object>();
            try
            {
                // World units: 1 = 100 px.
                float w = width / 100f, h = height / 100f;
                var unlit = Shader.Find(MaterialFactory.UnlitShader);
                Material Solid(Color c)
                {
                    var m = new Material(unlit);
                    m.SetColor("_BaseColor", c);
                    created.Add(m);
                    return m;
                }

                GameObject Add(GameObject go)
                {
                    SceneManager.MoveGameObjectToScene(go, scene);
                    return go;
                }

                void Rect(float x, float y, float rw, float rh, Color c, float z = 1f)
                {
                    var q = Add(GameObject.CreatePrimitive(PrimitiveType.Quad));
                    Object.DestroyImmediate(q.GetComponent<Collider>());
                    q.transform.position = new Vector3(x - w * 0.5f + rw * 0.5f, h * 0.5f - y - rh * 0.5f, z);
                    q.transform.localScale = new Vector3(rw, rh, 1f);
                    q.GetComponent<MeshRenderer>().sharedMaterial = Solid(c);
                }

                void Text(string text, float x, float y, float tw, float th, Color c, FontStyles style, TextAlignmentOptions align, float maxSize)
                {
                    var go = Add(new GameObject("Text", typeof(RectTransform)));
                    var t = go.AddComponent<TextMeshPro>();
                    if (TMP_Settings.defaultFontAsset != null)
                        t.font = TMP_Settings.defaultFontAsset;
                    t.text = text;
                    t.color = c;
                    t.fontStyle = style;
                    t.alignment = align;
                    t.enableAutoSizing = true;
                    t.fontSizeMin = 0.1f;
                    t.fontSizeMax = maxSize;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.rectTransform.sizeDelta = new Vector2(tw, th);
                    t.rectTransform.position = new Vector3(x - w * 0.5f + tw * 0.5f, h * 0.5f - y - th * 0.5f, 0f);
                    t.ForceMeshUpdate();
                }

                var band = product.bandColor;
                var ink = Color.Lerp(band, Color.black, 0.45f);
                var paper = new Color(0.93f, 0.925f, 0.9f);
                var split = product.label.IndexOf(' ');
                var name = (split > 0 ? product.label.Substring(0, split) : product.label).ToUpperInvariant();
                var grade = split > 0 ? product.label.Substring(split + 1) : "";
                Rect(0f, 0f, w, h, paper, 2f);
                Rect(0f, 0f, w, h * 0.3f, band);
                Rect(0f, h * 0.3f, w, h * 0.025f, Color.Lerp(band, Color.white, 0.5f));
                Rect(0f, h * 0.9f, w, h * 0.1f, band);
                Text(name, w * 0.06f, h * 0.02f, w * 0.88f, h * 0.27f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, 400f);
                Text(grade, w * 0.06f, h * 0.36f, w * 0.88f, h * 0.24f, ink, FontStyles.Bold, TextAlignmentOptions.Center, 400f);
                Text(product.fullName ?? "", w * 0.06f, h * 0.61f, w * 0.88f, h * 0.1f, new Color(0.18f, 0.18f, 0.17f), FontStyles.Normal,
                    TextAlignmentOptions.Center, 200f);
                Text(NutrientLine(grade), w * 0.06f, h * 0.73f, w * 0.88f, h * 0.08f, ink, FontStyles.Bold, TextAlignmentOptions.Center, 150f);
                Text("NET WT 5 kg   •   LAB TRAINING SAMPLE", w * 0.06f, h * 0.912f, w * 0.88f, h * 0.075f, Color.white, FontStyles.Bold,
                    TextAlignmentOptions.Center, 150f);

                var camGo = Add(new GameObject("LabelCamera"));
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.orthographic = true;
                cam.orthographicSize = h * 0.5f;
                cam.aspect = w / h;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = paper;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 50f;
                cam.allowHDR = false;
                cam.allowMSAA = true;
                cam.targetTexture = rt;
                cam.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var read = new Texture2D(width, height, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                read.Apply();
                RenderTexture.active = previous;
                cam.targetTexture = null;
                var result = read.GetPixels32();
                Object.DestroyImmediate(read);
                // GetPixels32 rows run bottom-up; callers index row 0 = bottom (label v = 0 at the bottom).
                return result;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                foreach (var o in created)
                    if (o != null)
                        Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>"19-19-19" -> "N 19% • P2O5 19% • K2O 19%" (only the non-zero nutrients).</summary>
        static string NutrientLine(string grade)
        {
            var parts = grade.Split('-');
            if (parts.Length != 3)
                return "";
            string[] names = { "N", "P<sub>2</sub>O<sub>5</sub>", "K<sub>2</sub>O" };
            var line = "";
            for (var i = 0; i < 3; i++)
            {
                if (parts[i].Trim() == "0")
                    continue;
                line += (line.Length > 0 ? "  •  " : "") + names[i] + " " + parts[i].Trim() + "%";
            }

            return line;
        }

        /// <summary>URP mask for the sack: R metallic 0, G ambient occlusion, A smoothness (from Poly Haven's ARM: AO, roughness, metal).</summary>
        static Texture2D BuildSackMask()
        {
            var output = Generated + "/FertiliserSack_Mask.png";
            var arm = LoadRaw(SackTextures + "/compost_bag_02_arm_1k.jpg");
            if (arm == null)
                return null;
            var pixels = arm.GetPixels32();
            var size = arm.width;
            Object.DestroyImmediate(arm);
            var mask = new Color32[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                // Woven polypropylene: a soft plastic sheen; the liner a little glossier than the weave.
                var smooth = Mathf.Clamp01((255 - pixels[i].g) / 255f * 0.85f + 0.12f);
                mask[i] = new Color32(0, pixels[i].r, 0, (byte)(smooth * 255f));
            }

            WritePng(output, mask, size, size);
            TextureSettings.ConfigureMask(output, 1024, 512);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(output);
        }

        // ------------------------------------------------------------------ shared

        static void WritePng(string path, Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        static int Triangles(GameObject model)
        {
            var count = 0;
            if (model == null)
                return 0;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    count += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
            return count;
        }

        static void ConfigureModel(string path, float scale)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                Debug.LogError("[BTP] Missing model " + path + ".");
                return;
            }

            var changed = !Mathf.Approximately(importer.globalScale, scale) || importer.materialImportMode != ModelImporterMaterialImportMode.None ||
                          importer.importAnimation || importer.importCameras || importer.importLights || !importer.isReadable;
            importer.globalScale = scale;
            importer.useFileScale = true;
            // The project's own URP materials replace the embedded ones; no animation, cameras or lights.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            // Readable: the sack's contents surface and the scoop's bowl are measured from the mesh at runtime.
            importer.isReadable = true;
            if (changed)
                importer.SaveAndReimport();
        }

        /// <summary>URP's metallic/smoothness mask for the scoop: R = metallic, A = 1 - roughness, 1024 px (written once).</summary>
        static Texture2D BuildMetalSmoothMask()
        {
            var output = Generated + "/Scoop_MetalSmooth.png";
            if (!File.Exists(output))
            {
                var metallic = LoadRaw(ScoopTextures + "/Scoop_metallic.jpeg");
                var roughness = LoadRaw(ScoopTextures + "/Scoop_roughness.jpeg");
                if (metallic == null || roughness == null)
                    return null;
                const int size = 1024;
                var m = Resize(metallic, size);
                var r = Resize(roughness, size);
                var pixels = new Color32[size * size];
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(m[i].r, m[i].r, m[i].r, (byte)(255 - r[i].r));
                var packed = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                packed.SetPixels32(pixels);
                packed.Apply();
                ProjectPaths.EnsureFolder(Generated);
                File.WriteAllBytes(output, packed.EncodeToPNG());
                Object.DestroyImmediate(packed);
                Object.DestroyImmediate(metallic);
                Object.DestroyImmediate(roughness);
                AssetDatabase.ImportAsset(output);
            }

            TextureSettings.ConfigureMask(output, 1024, 512);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(output);
        }

        static Texture2D LoadRaw(string path)
        {
            if (!File.Exists(path))
                return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
        }

        static Color32[] Resize(Texture2D source, int size)
        {
            var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            var pixels = read.GetPixels32();
            Object.DestroyImmediate(read);
            return pixels;
        }
    }
}
