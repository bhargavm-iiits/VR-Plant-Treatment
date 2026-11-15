using System.IO;
using System.Linq;
using BTP.Plants;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Editor
{
    /// <summary>
    /// Replaces the tomato with the nine Blender disease tomato plants, exported for VR by
    /// Tools/blender_tomato_plants.py (sources in SourceAssets/Tomato_Plant, outside Assets): ~1.75 m real-size
    /// plants with the supplied Meshy disease leaf as curved cut-out cards, decimated fruit (one atlas), stems,
    /// flowers and soil mound, two mesh LODs plus a baked billboard, and the GLBs' 8 s leaf breeze replayed by
    /// the Farm Recovery shader (_LEAF_WIND, baked by <see cref="LeafWindBaker"/>, as the cherry does).
    ///
    /// Every disease gets its own prefab, Prefabs/Plants/Tomato/Tomato_&lt;diseaseId&gt;_Farm.prefab, ready to use.
    /// The scenes show one variant everywhere: Tomato_Farm.prefab and Tomato_Lab.prefab are rebuilt in place
    /// (GUIDs and every scene/prefab instance kept) from <see cref="DiseaseClasses.DefaultDiseaseId"/>("tomato"),
    /// early blight. The leaf material is the Farm Recovery pair (the supplied diseased leaf, and a healthy copy
    /// with its lesions recoloured green), so the Farm six-week timeline still fades the disease; fruit, stems,
    /// flowers and soil are plain Simple Lit and never change.
    /// </summary>
    public static class TomatoPlantBuilder
    {
        public const string Folder = ProjectPaths.Root + "/Models/TomatoPlant";
        public const string VariantFolder = ProjectPaths.PlantPrefabs + "/Tomato";
        public const string FarmPrefab = ProjectPaths.PlantPrefabs + "/Tomato_Farm.prefab";
        public const string LabPrefab = ProjectPaths.PlantPrefabs + "/Tomato_Lab.prefab";

        /// <summary>The nine tomato diseaseIds of DiseaseClasses, in the GLBs' order (01..09).</summary>
        public static readonly string[] Diseases =
        {
            "bacterial_spot", "early_blight", "late_blight", "leaf_mold", "mosaic_virus",
            "septoria_leaf_spot", "target_spot", "spider_mites", "yellow_leaf_curl_virus",
        };

        // LOD switch points (screen-relative height). Quest (LOD bias 1) keeps the ~12k-triangle mesh to about
        // 1.8 m / (2.2 x 0.22) = 3.7 m, the ~4k mesh to about 7.5 m, then the 4-triangle billboard; PC about twice as
        // far. The mid mesh was kept to 12 m before the Farm had nine tomato disease rows (45 plants): from the head
        // path that put ~20 four-draw mid meshes in view, where billboards cost one static batch per row.
        const float NearUntil = 0.22f;
        const float FarUntil = 0.11f;
        const float CullAt = 0.008f;
        // Per-view billboard bake size (atlas 1024 x 512): plenty for a plant that is under 0.07 of the screen.
        const int BillboardViewSize = 512;
        // Leaf sprites are 1024 on their long side (a ~0.25 m leaf); Quest gets 512, ample at arm's length.
        const int LeafTextureSize = 1024;
        const int LeafTextureAndroid = 512;

        public static string VariantPrefab(string diseaseId) => $"{VariantFolder}/Tomato_{diseaseId}_Farm.prefab";
        public static string ModelPath(string diseaseId) => $"{Folder}/Tomato_{diseaseId}.fbx";
        public static string LeafMaterialPath(string diseaseId) => $"{Folder}/TomatoLeaf_{diseaseId}.mat";
        public static string WindTexturePath(string diseaseId) => $"{Folder}/Tomato_{diseaseId}_Wind.asset";

        sealed class Variant
        {
            public string DiseaseId;
            public MeshFilter Near;
            public MeshFilter Far;
            public Mesh BillboardMesh;
            public Material BillboardMaterial;
        }

        [MenuItem("BTP/Setup/Replace Tomato With Blender Tomato Plants (Wind)", priority = 24)]
        public static void Apply()
        {
            // Picks up a fresh Tools/blender_tomato_plants.py export made while the editor was open.
            AssetDatabase.Refresh();
            ProjectPaths.EnsureFolder(VariantFolder);
            var shown = DiseaseClasses.DefaultDiseaseId("tomato");
            var shared = SharedMaterials();
            Variant shownVariant = null;
            try
            {
                for (var i = 0; i < Diseases.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("BTP tomato plants", Diseases[i], (float)i / Diseases.Length);
                    var variant = BuildVariant(Diseases[i], shared);
                    if (variant != null && variant.DiseaseId == shown)
                        shownVariant = variant;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (shownVariant == null)
            {
                Debug.LogWarning($"[BTP] {ModelPath(shown)} is missing; run Tools/blender_tomato_plants.py first. The old tomato is kept.");
                return;
            }

            BlenderTreeBuilder.Rebuild(FarmPrefab, root => FarmContent(root, shownVariant));
            // The Lab prefab is decoration only now (the pedestal shows single leaves): the near mesh, no LODs.
            BlenderTreeBuilder.Rebuild(LabPrefab, root =>
            {
                if (root.TryGetComponent<LODGroup>(out var lod))
                    Object.DestroyImmediate(lod);
                AddPart(root, "Model", shownVariant.Near, true);
            });
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Tomato replaced with the Blender {shown} plant ({shownVariant.Near.sharedMesh.bounds.size.y:0.00} m, LOD0 {Tris(shownVariant.Near.sharedMesh)} / LOD1 {Tris(shownVariant.Far.sharedMesh)} triangles + billboard).");
        }

        /// <summary>Stem, soil, flower and fruit materials, the same for every variant.</summary>
        static (string name, Material material)[] SharedMaterials()
        {
            // Colours are the GLBs' linear base colours (GLB_verification.json); Simple Lit takes sRGB.
            var stem = Matte(MaterialFactory.Solid($"{Folder}/TomatoStem.mat", new Color(0.12f, 0.24f, 0.025f).gamma, 0.2f));
            var soil = Matte(MaterialFactory.Solid($"{Folder}/TomatoSoil.mat", new Color(0.035f, 0.014f, 0.004f).gamma, 0.05f));
            var flower = Matte(MaterialFactory.Solid($"{Folder}/TomatoFlower.mat", new Color(0.97f, 0.8f, 0.25f), 0.25f));
            // Fruit atlas: red, green and orange body and the calyx, 512 each, from the supplied 4K maps.
            var atlasPath = $"{Folder}/TomatoFruit.png";
            TextureSettings.ConfigureAlbedo(atlasPath, 1024, 512);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath)
                        ?? throw new FileNotFoundException($"[BTP] {atlasPath} is missing; run Tools/blender_tomato_plants.py first.");
            var fruit = MaterialFactory.SimpleLit($"{Folder}/TomatoFruit.mat", atlas, null, false);
            MaterialFactory.Glossy(fruit, 0.7f, new Color(0.4f, 0.4f, 0.4f));
            return new[] { ("TomatoStem", stem), ("TomatoSoil", soil), ("TomatoFlower", flower), ("TomatoFruit", fruit) };
        }

        /// <summary>
        /// No specular. MaterialFactory.SimpleLit leaves Simple Lit's _SpecularHighlights at 0, which is the
        /// "specular colour" source (0.5 grey), not "off"; that sheen turns the dark loam and the stems pale grey.
        /// </summary>
        internal static Material Matte(Material material)
        {
            material.SetFloat("_SpecularHighlights", (float)SimpleLitGUI.SpecularSource.NoSpecular);
            BaseShaderGUI.SetMaterialKeywords(material, SimpleLitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Variant BuildVariant(string diseaseId, (string name, Material material)[] shared)
        {
            var modelPath = ModelPath(diseaseId);
            if (!File.Exists(modelPath))
            {
                Debug.LogWarning($"[BTP] {modelPath} is missing; run Tools/blender_tomato_plants.py. Tomato {diseaseId} skipped.");
                return null;
            }

            var leaf = LeafMaterial(diseaseId);
            var remaps = shared.Append(($"TomatoLeaf_{diseaseId}", leaf)).ToArray();
            // Exact mesh: no compression (wind indices and pivots must survive) and MikkTSpace tangents.
            BlenderTreeBuilder.ConfigureModel(modelPath, remaps, false, true);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var variant = new Variant
            {
                DiseaseId = diseaseId,
                Near = model.GetComponentsInChildren<MeshFilter>().First(f => f.name.Contains("LOD0")),
                Far = model.GetComponentsInChildren<MeshFilter>().First(f => f.name.Contains("LOD1")),
            };
            var wind = LeafWindBaker.Bake($"{Folder}/Tomato_{diseaseId}_Wind.bytes", variant.Near.sharedMesh, variant.Far.sharedMesh,
                WindTexturePath(diseaseId), $"Tomato {diseaseId}");
            LeafWindBaker.Assign(leaf, wind);

            CreateOrRebuild(VariantPrefab(diseaseId), root =>
            {
                // The billboard is baked from the near mesh alone, before the far mesh joins it.
                var probe = AddPart(root, "Model", variant.Near, true);
                var billboard = BillboardBaker.Bake(root, $"Tomato_{diseaseId}", BillboardViewSize);
                Object.DestroyImmediate(probe.gameObject);
                variant.BillboardMesh = billboard.Mesh;
                variant.BillboardMaterial = MaterialFactory.FarmRecovery($"{ProjectPaths.PlantMaterials}/Tomato_{diseaseId}_Billboard.mat",
                    billboard.HealthyTexture, billboard.Texture, BillboardNoise(), null, true, PlantAssetPipeline.BillboardMaskScale);
                FarmContent(root, variant);
            });
            return variant;
        }

        /// <summary>
        /// The leaf card material: the supplied disease leaf (diseased, Week 1), a healthy copy (lesions and halos
        /// recoloured to the leaf's own green, Week 6) and the recovery mask, cut-out and double-sided, with wind.
        /// </summary>
        static Material LeafMaterial(string diseaseId)
        {
            var diseasedPath = $"{Folder}/TomatoLeaf_{diseaseId}.png";
            var healthyPath = BlenderTreeBuilder.WriteHealthySprite(diseasedPath, $"{Folder}/TomatoLeaf_{diseaseId}_Healthy.png");
            var diseased = BlenderTreeBuilder.ImportSprite(diseasedPath, LeafTextureSize);
            var healthy = BlenderTreeBuilder.ImportSprite(healthyPath, LeafTextureSize);
            // Cut-out import (coverage-preserving mips, ASTC on Quest); ImportSprite's clamp wrap is kept.
            foreach (var path in new[] { diseasedPath, healthyPath })
                TextureSettings.ConfigureCutout(path, LeafTextureSize, LeafTextureAndroid);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(FarmRecoveryMasks.Create(healthy, $"{Folder}/TomatoLeaf_{diseaseId}_RecoveryMask.png", "tomato"));
            var material = MaterialFactory.FarmRecovery(LeafMaterialPath(diseaseId), healthy, diseased, mask, null, true);
            material.SetFloat("_Cutoff", 0.45f);
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D BillboardNoise()
        {
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(PlantAssetPipeline.BillboardNoisePath);
            return noise != null ? noise : AssetDatabase.LoadAssetAtPath<Texture2D>(FarmRecoveryMasks.CreateNoise(PlantAssetPipeline.BillboardNoisePath));
        }

        /// <summary>Farm prefab content: near mesh, far mesh and billboard under one LODGroup.</summary>
        static void FarmContent(GameObject root, Variant variant)
        {
            var near = AddPart(root, "Model", variant.Near, true);
            // The mid mesh casts no shadow: within the 20 m Quest shadow distance only the near mesh does.
            var far = AddPart(root, "ModelFar", variant.Far, false);
            var billboard = new GameObject("Billboard", typeof(MeshFilter), typeof(MeshRenderer));
            billboard.transform.SetParent(root.transform, false);
            billboard.GetComponent<MeshFilter>().sharedMesh = variant.BillboardMesh;
            var renderer = billboard.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = variant.BillboardMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            DiseaseVariantBuilder.MakeBatchable(billboard);

            if (!root.TryGetComponent<LODGroup>(out var group))
                group = root.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(NearUntil, new Renderer[] { near }),
                new LOD(FarUntil, new Renderer[] { far }),
                new LOD(CullAt, new Renderer[] { renderer }),
            });
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
        }

        /// <summary>
        /// Applies the row LOD settings above (switch points, no mid-mesh shadow, batching-static billboard) to a built
        /// tomato Farm prefab in place, without re-importing or re-baking it. Used by DiseaseVariantBuilder for the
        /// nine tomato disease rows; a rebuild with <see cref="Apply"/> gives the same result.
        /// </summary>
        internal static void TuneForRows(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var group = root.GetComponent<LODGroup>();
                var lods = group.GetLODs();
                if (lods.Length != 3)
                    throw new System.InvalidOperationException($"{prefabPath}: expected mesh, far mesh and billboard LODs.");
                lods[0].screenRelativeTransitionHeight = NearUntil;
                lods[1].screenRelativeTransitionHeight = FarUntil;
                lods[2].screenRelativeTransitionHeight = CullAt;
                group.SetLODs(lods);
                foreach (var renderer in lods[1].renderers)
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                foreach (var renderer in lods[2].renderers)
                    DiseaseVariantBuilder.MakeBatchable(renderer.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// A renderer for one imported LOD mesh. Unlike the trees it keeps the export's origin: the main stem's base
        /// on the ground at (0, 0, 0). Centring each LOD on its own bounds would shift the LODs apart by centimetres.
        /// </summary>
        internal static MeshRenderer AddPart(GameObject root, string name, MeshFilter source, bool shadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.transform.SetLocalPositionAndRotation(source.transform.localPosition, source.transform.localRotation);
            go.transform.localScale = source.transform.localScale;
            go.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return renderer;
        }

        /// <summary>Rebuilds an existing prefab in place (GUID kept) or creates it.</summary>
        static void CreateOrRebuild(string path, System.Action<GameObject> build)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                BlenderTreeBuilder.Rebuild(path, build);
                return;
            }

            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                build(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        internal static int Tris(Mesh mesh)
        {
            var count = 0;
            for (var s = 0; s < mesh.subMeshCount; s++)
                count += (int)mesh.GetIndexCount(s) / 3;
            return count;
        }
    }
}
