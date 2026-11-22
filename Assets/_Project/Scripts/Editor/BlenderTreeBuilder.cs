using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Editor
{
    /// <summary>
    /// Replaces the apple and peach plants with the Blender trees, exported for VR by
    /// Tools/blender_apple_tree.py and Tools/blender_peach_tree.py: every supplied diseased leaf becomes a
    /// cut-out card in its exact place, with simplified wood and fruit, two LODs. The Farm and Lab prefabs
    /// are rebuilt in place, so their GUIDs and every scene instance stay valid. Leaf cards use the Farm
    /// Recovery shader (diseased sprite, generated healthy sprite, recovery mask), so the Farm's
    /// illustrative timeline still fades the disease.
    ///
    /// The cherry (Tools/blender_cherry_tree.py, Assets/Plant_Asstes/Cherry_Tree) is different: its 1,225 leaves
    /// are real leaf meshes on the supplied Cherry1 atlas, and it keeps its Blender leaf wind. The 8 s loop is
    /// baked by <see cref="LeafWindBaker"/> into a float texture that the Farm Recovery shader replays per
    /// leaf in the vertex stage (_LEAF_WIND), shadows included; its materials come from <see cref="CherryMaterials"/>.
    /// The tomato plants reuse this machinery from <see cref="TomatoPlantBuilder"/>.
    /// </summary>
    public static class BlenderTreeBuilder
    {
        sealed class TreeSpec
        {
            public string PlantId;
            public string Folder;
            public string Model;
            public string FarmPrefab;
            public string LabPrefab;
            /// <summary>Height in the scenes, in metres.</summary>
            public float Height;
            public (string name, Color color, float smoothness)[] Solids;
            public string[] Leaves;
            /// <summary>Extra parts added to the Farm prefab only (the grape's pergola module).</summary>
            public System.Action<GameObject> FarmExtras;
            /// <summary>Recalculate fully smoothed normals (low-poly berries then read as round).</summary>
            public bool SmoothNormals;
            /// <summary>Replaces the Solids/Leaves material set for trees whose materials are not solid + sprite leaf.</summary>
            public System.Func<TreeSpec, (string name, Material material)[]> Materials;
            /// <summary>
            /// Exact vertex data: no mesh compression (vertex-encoded data such as wind indices must survive) and
            /// MikkTSpace tangents for the normal-mapped atlas.
            /// </summary>
            public bool ExactMesh;
            /// <summary>Runs after the model import, before the prefabs are rebuilt (the cherry's wind bake).</summary>
            public System.Action<TreeSpec, Mesh, Mesh> AfterImport;
        }

        static readonly TreeSpec Apple = new TreeSpec
        {
            PlantId = "apple",
            Folder = ProjectPaths.Root + "/Models/AppleTree",
            Model = "AppleTree.fbx",
            FarmPrefab = ProjectPaths.PlantPrefabs + "/Apple_Farm.prefab",
            LabPrefab = ProjectPaths.PlantPrefabs + "/Apple_Lab.prefab",
            Height = 4f,
            Solids = new[]
            {
                ("AppleBark", new Color(0.36f, 0.27f, 0.2f), 0.1f),
                ("AppleFruit", new Color(0.72f, 0.08f, 0.07f), 0.55f),
                ("AppleStem", new Color(0.4f, 0.27f, 0.14f), 0.2f),
            },
            Leaves = new[] { "AppleLeaf_BlackRot", "AppleLeaf_Scab", "AppleLeaf_Rust" },
        };

        static readonly TreeSpec Peach = new TreeSpec
        {
            PlantId = "peach",
            Folder = ProjectPaths.Root + "/Models/PeachTree",
            Model = "PeachTree.fbx",
            FarmPrefab = ProjectPaths.PlantPrefabs + "/Peach_Farm.prefab",
            LabPrefab = ProjectPaths.PlantPrefabs + "/Peach_Lab.prefab",
            Height = 3.2f,
            Solids = new[]
            {
                ("PeachBark", new Color(0.38f, 0.3f, 0.24f), 0.1f),
                ("PeachSkin", new Color(0.96f, 0.6f, 0.32f), 0.35f),
                ("PeachStem", new Color(0.42f, 0.38f, 0.18f), 0.2f),
            },
            Leaves = new[] { "PeachLeaf" },
        };

        /// <summary>
        /// Pergola (bower) vine after the reference vineyard photo: trunk to 1.9 m, flat canopy at 2.0-2.3 m,
        /// bunches hanging below. Height 0 keeps the exported real-world size. The Farm prefab is one 3.2 m
        /// pergola module (posts, overhead wires, mulched ridge, drip line); modules placed 3.2 m apart join up.
        /// </summary>
        static readonly TreeSpec Grape = new TreeSpec
        {
            PlantId = "grape",
            Folder = ProjectPaths.Root + "/Models/GrapeVine",
            Model = "GrapeVine.fbx",
            FarmPrefab = ProjectPaths.PlantPrefabs + "/Grape_Farm.prefab",
            LabPrefab = ProjectPaths.PlantPrefabs + "/Grape_Lab.prefab",
            Height = 0f,
            Solids = new[]
            {
                ("GrapeWood", new Color(0.34f, 0.27f, 0.21f), 0.1f),
                ("GrapeCane", new Color(0.46f, 0.31f, 0.2f), 0.15f),
                ("GrapeStem", new Color(0.45f, 0.56f, 0.24f), 0.2f),
                ("GrapeBerry", new Color(0.66f, 0.8f, 0.34f), 0.6f),
            },
            Leaves = new[] { "GrapeLeaf_BlackRot", "GrapeLeaf_Esca", "GrapeLeaf_LeafBlight" },
            FarmExtras = PergolaModule,
            SmoothNormals = true,
            // Berry positions are tiny relative to the vine. Quantising the
            // combined mesh distorts their silhouettes, even with smooth normals.
            ExactMesh = true,
        };

        /// <summary>
        /// Cherry tree with the supplied Cherry1 leaves, original cherries (decimated) and the Blender leaf wind.
        /// Leaves and bark share the Cherry1 atlas, so the Farm's existing cherry diseased bake and recovery mask
        /// (made by PlantAssetPipeline from the same atlas) apply as they are.
        /// </summary>
        static readonly TreeSpec Cherry = new TreeSpec
        {
            PlantId = "cherry",
            Folder = ProjectPaths.Root + "/Models/CherryTree",
            Model = "CherryTree.fbx",
            FarmPrefab = ProjectPaths.PlantPrefabs + "/Cherry_Farm.prefab",
            LabPrefab = ProjectPaths.PlantPrefabs + "/Cherry_Lab.prefab",
            Height = 4f,
            Solids = new (string, Color, float)[0],
            Leaves = new string[0],
            Materials = CherryMaterials,
            ExactMesh = true,
            AfterImport = BakeCherryWind,
        };

        /// <summary>Alpha cutoff of every sprite/atlas leaf material; the leaf textures' mips preserve coverage at this value.</summary>
        public const float LeafCutoff = 0.45f;

        public const string CherrySource = ProjectPaths.SourcePlants + "/Cherry_Tree";
        public const string CherryAtlasNormal = CherrySource + "/Cherry/fbx/Cherry1_Normal.tga";

        static (string name, Material material)[] CherryMaterials(TreeSpec spec)
        {
            Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path)
                                           ?? throw new System.IO.FileNotFoundException($"[BTP] {path} is missing (run BTP/Setup/2 - Build Plant Assets for the cherry atlas bakes).");

            var healthyPath = ProjectPaths.PlantTextures + "/Cherry_BaseAlpha.png";
            TextureSettings.ConfigureCutout(healthyPath, alphaReference: LeafCutoff);
            TextureSettings.ConfigureNormal(CherryAtlasNormal);
            var healthy = Load(healthyPath);
            var diseased = Load(ProjectPaths.PlantTextures + "/Cherry_FarmDiseased.png");
            var mask = Load(ProjectPaths.PlantTextures + "/Cherry_FarmRecoveryMask.png");
            var normal = Load(CherryAtlasNormal);

            // Leaves: cut-out, both faces, the timeline's healthy/diseased blend, plus the per-leaf wind.
            var leaf = MaterialFactory.FarmRecovery($"{spec.Folder}/CherryLeaf.mat", healthy, diseased, mask, normal, true);
            leaf.SetFloat("_Cutoff", LeafCutoff);
            leaf.SetFloat("_LeafWind", 1f);
            leaf.EnableKeyword("_LEAF_WIND");
            leaf.doubleSidedGI = true;
            EditorUtility.SetDirty(leaf);

            // Bark: the same atlas (its bark region is opaque), back-face culled, no wind.
            var bark = MaterialFactory.FarmRecovery($"{spec.Folder}/CherryBark.mat", healthy, diseased, mask, normal, false);

            // Blender's linear base colour (0.115, 0.155, 0.024), as the sRGB colour the material expects.
            var pedicel = MaterialFactory.Solid($"{spec.Folder}/CherryPedicel.mat", new Color(0.115f, 0.155f, 0.024f).gamma, 0.25f);

            var colorPath = CherrySource + "/BakedTextures/Color.png";
            var fruitNormalPath = CherrySource + "/BakedTextures/Normal.png";
            TextureSettings.ConfigureAlbedo(colorPath, 1024, 512);
            TextureSettings.ConfigureNormal(fruitNormalPath, 1024, 512);
            var fruit = MaterialFactory.SimpleLit($"{spec.Folder}/CherryFruit.mat", Load(colorPath), Load(fruitNormalPath), false);
            MaterialFactory.Glossy(fruit, 0.65f, new Color(0.35f, 0.35f, 0.35f));

            return new[] { ("CherryLeaf", leaf), ("CherryBark", bark), ("CherryPedicel", pedicel), ("CherryFruit", fruit) };
        }

        static void BakeCherryWind(TreeSpec spec, Mesh lod0, Mesh lod1)
        {
            var result = LeafWindBaker.Bake($"{spec.Folder}/CherryTree_Wind.bytes", lod0, lod1, $"{spec.Folder}/CherryTree_Wind.asset", "Cherry");
            LeafWindBaker.Assign(AssetDatabase.LoadAssetAtPath<Material>($"{spec.Folder}/CherryLeaf.mat"), result);
        }

        public const float PergolaModule_Size = 3.2f;
        public const float PergolaHeight = 2.12f;

        /// <summary>One pergola bay around the vine: galvanised corner posts, overhead wire grid, mulched ridge, drip line.</summary>
        static void PergolaModule(GameObject root)
        {
            var folder = Grape.Folder;
            var steel = MaterialFactory.Solid($"{folder}/PergolaPost.mat", new Color(0.62f, 0.64f, 0.66f), 0.55f);
            var wire = MaterialFactory.Solid($"{folder}/PergolaWire.mat", new Color(0.15f, 0.15f, 0.16f), 0.4f);
            var straw = MaterialFactory.Solid($"{folder}/Mulch.mat", new Color(0.74f, 0.6f, 0.38f), 0.05f);
            var soil = MaterialFactory.Solid($"{folder}/RidgeSoil.mat", new Color(0.42f, 0.3f, 0.2f), 0.05f);
            var pipe = MaterialFactory.Solid($"{folder}/DripLine.mat", new Color(0.06f, 0.06f, 0.06f), 0.3f);
            var group = new GameObject("Pergola");
            group.transform.SetParent(root.transform, false);
            var half = PergolaModule_Size * 0.5f;

            GameObject Part(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, Vector3 euler = default)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(group.transform, false);
                go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
                go.transform.localScale = scale;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                return go;
            }

            foreach (var x in new[] { -half, half })
            foreach (var z in new[] { -half, half })
                Part(PrimitiveType.Cylinder, "PergolaPost", new Vector3(x, PergolaHeight * 0.5f, z), new Vector3(0.07f, PergolaHeight * 0.5f, 0.07f), steel);
            for (var i = 0; i <= 4; i++)
            {
                var t = -half + i * PergolaModule_Size / 4f;
                Part(PrimitiveType.Cylinder, "PergolaWireZ", new Vector3(t, PergolaHeight, 0f), new Vector3(0.012f, half, 0.012f), wire, new Vector3(90f, 0f, 0f));
                Part(PrimitiveType.Cylinder, "PergolaWireX", new Vector3(0f, PergolaHeight, t), new Vector3(0.012f, half, 0.012f), wire, new Vector3(0f, 0f, 90f));
            }

            // Mulched planting ridge along the row (local Z) and the drip line resting on it.
            Part(PrimitiveType.Cube, "RidgeSoil", new Vector3(0f, 0.06f, 0f), new Vector3(1.0f, 0.12f, PergolaModule_Size), soil);
            Part(PrimitiveType.Cube, "Mulch", new Vector3(0f, 0.125f, 0f), new Vector3(0.9f, 0.03f, PergolaModule_Size), straw);
            Part(PrimitiveType.Cylinder, "DripLine", new Vector3(0.32f, 0.16f, 0f), new Vector3(0.025f, half, 0.025f), pipe, new Vector3(90f, 0f, 0f));
        }

        [MenuItem("BTP/Setup/Replace Grape With Blender Pergola Vine", priority = 22)]
        public static void ApplyGrape() => Apply(Grape);

        [MenuItem("BTP/Setup/Replace Apple With Blender Apple Tree", priority = 20)]
        public static void ApplyApple() => Apply(Apple);

        [MenuItem("BTP/Setup/Replace Peach With Blender Peach Tree", priority = 21)]
        public static void ApplyPeach() => Apply(Peach);

        [MenuItem("BTP/Setup/Replace Cherry With Blender Cherry Tree (Wind)", priority = 23)]
        public static void ApplyCherry() => Apply(Cherry);

        static void Apply(TreeSpec spec)
        {
            var modelPath = $"{spec.Folder}/{spec.Model}";
            if (!System.IO.File.Exists(modelPath))
            {
                Debug.LogWarning($"[BTP] {modelPath} is missing; run its Tools/blender_*_tree.py export first. The old {spec.PlantId} model is kept.");
                return;
            }

            var remaps = spec.Materials?.Invoke(spec) ?? spec.Solids.Select(s => (s.name, MaterialFactory.Solid($"{spec.Folder}/{s.name}.mat", s.color, s.smoothness)))
                .Concat(spec.Leaves.Select(l => (l, Leaf(spec, l)))).ToArray();
            ConfigureModel(modelPath, remaps, spec.SmoothNormals, spec.ExactMesh);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var lod0 = model.GetComponentsInChildren<MeshFilter>().First(f => f.name.Contains("LOD0"));
            var lod1 = model.GetComponentsInChildren<MeshFilter>().First(f => f.name.Contains("LOD1"));
            spec.AfterImport?.Invoke(spec, lod0.sharedMesh, lod1.sharedMesh);
            var scale = spec.Height > 0f ? spec.Height / Mathf.Max(lod0.sharedMesh.bounds.size.y * lod0.transform.lossyScale.y, 0.01f) : 1f;

            Rebuild(spec.FarmPrefab, root =>
            {
                var near = AddModel(root, "Model", lod0, scale);
                var far = AddModel(root, "ModelFar", lod1, scale);
                var group = root.GetComponent<LODGroup>() ?? root.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.18f, new Renderer[] { near }), new LOD(0.02f, new Renderer[] { far }) });
                group.RecalculateBounds();
                spec.FarmExtras?.Invoke(root);
            });
            Rebuild(spec.LabPrefab, root => AddModel(root, "Model", lod0, scale));
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] {spec.PlantId} replaced with the Blender tree ({(spec.Height > 0f ? spec.Height : lod0.sharedMesh.bounds.size.y)} m, LOD0 {Tris(lod0.sharedMesh)} / LOD1 {Tris(lod1.sharedMesh)} triangles).");
        }

        static int Tris(Mesh mesh) => mesh.triangles.Length / 3;

        internal static Texture2D ImportSprite(string path, int maxSize = 512)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = maxSize;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The healthy state for the Farm's illustrative recovery timeline: the same leaf with its non-green
        /// (lesion, halo) pixels recoloured to the leaf's own average healthy green, shading kept.
        /// </summary>
        internal static string WriteHealthySprite(string diseasedPath, string healthyPath)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(System.IO.File.ReadAllBytes(diseasedPath));
            var pixels = source.GetPixels();
            Color sum = default;
            var count = 0;
            foreach (var c in pixels)
                if (c.a > 0.5f && c.g > c.r * 1.08f && c.g > c.b * 1.25f)
                {
                    sum += c;
                    count++;
                }

            var green = count > 0 ? sum / count : new Color(0.3f, 0.5f, 0.2f);
            var greenLuma = green.r * 0.3f + green.g * 0.59f + green.b * 0.11f;
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.a <= 0.05f || (c.g > c.r * 1.12f && c.g > c.b * 1.3f))
                    continue;
                var shade = Mathf.Clamp((c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / Mathf.Max(greenLuma, 0.01f), 0.75f, 1.2f);
                pixels[i] = new Color(green.r * shade, green.g * shade, green.b * shade, c.a);
            }

            source.SetPixels(pixels);
            System.IO.File.WriteAllBytes(healthyPath, source.EncodeToPNG());
            Object.DestroyImmediate(source);
            return healthyPath;
        }

        static Material Leaf(TreeSpec spec, string sprite)
        {
            var diseasedPath = $"{spec.Folder}/{sprite}.png";
            var healthyPath = WriteHealthySprite(diseasedPath, $"{spec.Folder}/{sprite}_Healthy.png");
            ImportSprite(diseasedPath);
            ImportSprite(healthyPath);
            // Cut-out import (coverage-preserving mips, ASTC on Quest); ImportSprite's clamp wrap is kept.
            // The sprites are 512 px; Quest keeps 512 too.
            TextureSettings.ConfigureCutout(diseasedPath, 512, 512, LeafCutoff);
            TextureSettings.ConfigureCutout(healthyPath, 512, 512, LeafCutoff);
            var diseased = AssetDatabase.LoadAssetAtPath<Texture2D>(diseasedPath);
            var healthy = AssetDatabase.LoadAssetAtPath<Texture2D>(healthyPath);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(FarmRecoveryMasks.Create(healthy, $"{spec.Folder}/{sprite}_RecoveryMask.png", spec.PlantId));
            var material = MaterialFactory.FarmRecovery($"{spec.Folder}/{sprite}.mat", healthy, diseased, mask, null, true);
            material.SetFloat("_Cutoff", LeafCutoff);
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static void ConfigureModel(string modelPath, (string name, Material material)[] materials, bool smooth, bool exact)
        {
            // A freshly exported FBX has no importer until the asset database has seen it.
            if (AssetImporter.GetAtPath(modelPath) == null)
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.isReadable = false;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            // Mesh compression quantises positions and UVs, which can open the millimetre joints where leaves, fruit
            // and stems were attached to the wood (Tools/attach_plant_parts.py embeds parts only 1 mm), and the wind
            // indices/pivots need exact values; compression only shrinks the build, not runtime memory.
            importer.meshCompression = ModelImporterMeshCompression.Off;
            if (exact)
            {
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.generateSecondaryUV = false;
            }
            if (smooth)
            {
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.normalSmoothingAngle = 180f;
            }
            foreach (var (name, material) in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
            importer.SaveAndReimport();
        }

        internal static void Rebuild(string prefabPath, System.Action<GameObject> build)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (Transform child in root.transform.Cast<Transform>().ToList())
                    Object.DestroyImmediate(child.gameObject);
                build(root);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Renderer AddModel(GameObject root, string name, MeshFilter source, float scale)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.transform.localRotation = source.transform.localRotation;
            go.transform.localScale = source.transform.localScale * scale;
            go.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            // Stand the tree on the ground at the root's origin.
            var bounds = source.sharedMesh.bounds;
            var bottom = go.transform.localRotation * Vector3.Scale(bounds.center - new Vector3(0f, bounds.extents.y, 0f), go.transform.localScale);
            go.transform.localPosition = -bottom;
            return renderer;
        }
    }
}
