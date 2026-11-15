using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTP.Plants;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Farm models for the ten crops that first existed only as Lab leaf specimens (blueberry, chilli, cotton, mango,
    /// orange, rice, soybean, squash, sugarcane, wheat): one whole plant per condition, 34 in all, from the supplied
    /// Blender plants in Assets/Plant_Asstes, reduced for VR by Tools/blender_farm_crops.py into
    /// Models/FarmCrops/&lt;Stem&gt; (two mesh LODs, the disease leaf texture, one atlas for every other part, and
    /// &lt;Stem&gt;_export.json, which this builder reads).
    ///
    /// For every condition it builds the Farm prefab the disease rows place (Prefabs/Plants/Variants/
    /// &lt;Stem&gt;_&lt;disease&gt;_Farm.prefab: near mesh, mid mesh, baked billboard) and its DiseaseVariantCatalog
    /// entry; for every crop, &lt;Stem&gt;_Farm.prefab (the crop's default condition), set as the crop's Farm model.
    /// Leaves are cards under the supplied leaf texture as a cut-out sprite, so every leaf has its exact outline.
    /// The leaf material is the Farm Recovery pair (the supplied diseased leaf, and a healthy copy with its lesions
    /// recoloured to the leaf's own green), so the Farm six-week timeline fades the disease as it does for the
    /// other crops; everything else is one double-sided material on the export's atlas and never changes.
    /// Both replay the plant's own Blender wind (LeafWindBaker, version 3 data: every moving part, with travel),
    /// which is why the stems and fruit use the Farm Recovery shader too: it is the shader that has the wind.
    /// Existing prefabs are rebuilt in place, so GUIDs and scene instances stay valid.
    ///
    /// Run after BTP/Setup/8 (the crops' plant definitions) and before BTP/Setup/4 - Build Farm Scene.
    /// BTP/Setup/26 - Build Farm Disease Variants calls <see cref="BuildVariants"/> too, so a full rebuild of the
    /// variants keeps these crops.
    /// </summary>
    public static class FarmCropBuilder
    {
        public const string Folder = ProjectPaths.Root + "/Models/FarmCrops";
        const int BillboardViewSize = 512;
        const int LeafTextureSize = 1024;

        [Serializable]
        sealed class Export
        {
            public string plantId;
            public string stem;
            public Variant[] variants;
        }

        [Serializable]
        sealed class Variant
        {
            public string disease;
            public string fbx;
            public string leaf_texture;
            public string parts_texture;
            public string wind_file;
            public int leaf_atlas_size;
            public float height;
            public float width;
            public int lod0_triangles;
            public int lod1_triangles;
        }

        /// <summary>
        /// LOD switch points (screen-relative height). Quest (LOD bias 1) keeps a level to about
        /// height / (2.2 x value) metres: a 0.75 m chilli shows its ~13k-triangle mesh to 2.8 m and its ~4k mesh to
        /// 5.7 m; a 3 m sugarcane to 6 m and 11 m; a 3.8 m mango (42k) to 7 m and 11 m. Beyond that the plant is a
        /// 4-triangle billboard. PC keeps each level two to three times further.
        /// </summary>
        static (float near, float mid, float cull) LodsFor(float height) =>
            height > 3.3f ? (0.25f, 0.16f, 0.006f) : height > 1.8f ? (0.22f, 0.12f, 0.006f) : (0.12f, 0.06f, 0.006f);

        public static string Stem(string plantId) => DiseaseVariantBuilder.Stem(plantId);

        public static string FarmPrefab(string plantId) => $"{ProjectPaths.PlantPrefabs}/{Stem(plantId)}_Farm.prefab";

        static string CropFolder(string plantId) => $"{Folder}/{Stem(plantId)}";

        public static string LeafMaterialPath(string plantId, string diseaseId) => $"{CropFolder(plantId)}/{Stem(plantId)}Leaf_{diseaseId}.mat";

        [MenuItem("BTP/Setup/27 - Build Farm Crops (ten field and tropical crops)", priority = 27)]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Exit Play mode before building the Farm crops.");
            var variants = AssetDatabase.LoadAssetAtPath<DiseaseVariantCatalog>(ProjectPaths.VariantCatalog)
                           ?? throw new InvalidOperationException("Run BTP/Setup/26 - Build Farm Disease Variants first.");
            var built = BuildVariants();
            var order = DiseaseClasses.AllCases.Select(i => i.PlantId + "/" + i.DiseaseId).ToList();
            var entries = variants.Entries.Where(e => !DiseaseClasses.IsLabOnlyPlant(e.plantId)).Concat(built)
                .OrderBy(e => order.IndexOf(e.plantId + "/" + e.diseaseId)).ToList();
            variants.SetEntries(entries);
            EditorUtility.SetDirty(variants);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Built {built.Count} Farm crop plants; {ProjectPaths.VariantCatalog} has {entries.Count} entries.");
        }

        /// <summary>
        /// Command line: the ten crops' Farm models, the Lab crop pager's page names, then the Farm scene with their rows.
        /// Unity.exe -batchmode -projectPath . -executeMethod BTP.Editor.FarmCropBuilder.RunAll -quit
        /// (not -nographics: the billboards are rendered).
        /// </summary>
        public static void RunAll()
        {
            var result = Automation.Run("farm_crops", () =>
            {
                BuildAll();
                // The Lab's crop pager names its two pages; the names no longer say "Lab-only".
                LabOnlyCropSetup.PatchLabScene();
                FarmSceneBuilder.Build();
            });
            if (Application.isBatchMode && result != "done")
                EditorApplication.Exit(1);
        }

        /// <summary>
        /// Builds all 34 plants and the ten crops' Farm prefabs, sets each crop's Farm model in its plant definition,
        /// and returns the variant catalog entries (in DiseaseClasses order). The caller stores them.
        /// </summary>
        public static List<DiseaseVariantCatalog.Entry> BuildVariants()
        {
            // Picks up a fresh Tools/blender_farm_crops.py export made while the editor was open.
            AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog)
                          ?? throw new InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first.");
            ProjectPaths.EnsureFolder(ProjectPaths.VariantPrefabs);
            var entries = new List<DiseaseVariantCatalog.Entry>();
            var ids = DiseaseClasses.LabOnlyPlantIds;
            try
            {
                for (var p = 0; p < ids.Length; p++)
                {
                    var plantId = ids[p];
                    EditorUtility.DisplayProgressBar("BTP Farm crops", plantId, (float)p / ids.Length);
                    if (!catalog.TryGet(plantId, out var plant))
                        throw new InvalidOperationException($"No plant definition for {plantId}; run BTP/Setup/8 - Add Lab-only Crops first.");
                    var export = Load(plantId);
                    var parts = new Dictionary<string, Texture2D>();
                    foreach (var info in DiseaseClasses.For(plantId))
                    {
                        var variant = export.variants.FirstOrDefault(v => v.disease == info.DiseaseId)
                                      ?? throw new InvalidOperationException($"{ExportPath(plantId)} has no '{info.DiseaseId}' plant; run Tools/blender_farm_crops.py.");
                        entries.Add(BuildVariant(plantId, variant, parts));
                    }

                    // The crop's own Farm model: its default condition (what the recovery driver and tests read).
                    var defaultDisease = DiseaseClasses.DefaultDiseaseId(plantId);
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(DiseaseVariantBuilder.VariantPrefab(plantId, defaultDisease));
                    var farmPath = FarmPrefab(plantId);
                    DiseaseVariantBuilder.CreateOrRebuild(farmPath, root => CopyContent(source, root));
                    plant.SetPrefabs(AssetDatabase.LoadAssetAtPath<GameObject>(farmPath), plant.LabPrefab);
                    EditorUtility.SetDirty(plant);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            return entries;
        }

        static string ExportPath(string plantId) => $"{CropFolder(plantId)}/{Stem(plantId)}_export.json";

        static Export Load(string plantId)
        {
            var path = ExportPath(plantId);
            if (!File.Exists(path))
                throw new FileNotFoundException($"[BTP] {path} is missing; run Tools/blender_farm_crops.py first.");
            var export = JsonUtility.FromJson<Export>(File.ReadAllText(path));
            if (export == null || export.plantId != plantId || export.variants == null)
                throw new InvalidOperationException($"{path} is not the {plantId} export.");
            return export;
        }

        static DiseaseVariantCatalog.Entry BuildVariant(string plantId, Variant variant, Dictionary<string, Texture2D> partsCache)
        {
            var folder = CropFolder(plantId);
            var stem = Stem(plantId);
            var name = $"{stem}_{variant.disease}";
            var leaf = LeafMaterial(plantId, variant);
            if (!partsCache.TryGetValue(variant.parts_texture, out var atlas))
                partsCache[variant.parts_texture] = atlas = PartsAtlas(folder, variant.parts_texture);
            // Its own material per condition: the wind texture differs, the atlas is shared.
            var parts = PartsMaterial($"{folder}/{stem}Parts_{variant.disease}.mat", atlas);

            var modelPath = $"{folder}/{variant.fbx}";
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"[BTP] {modelPath} is missing; run Tools/blender_farm_crops.py first.");
            // Exact mesh: no compression, the wind part indices and the tagged vertices in UV1 must survive.
            BlenderTreeBuilder.ConfigureModel(modelPath, new[] { ($"{stem}Leaf", leaf), ($"{stem}Parts", parts) }, false, true);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var filters = model.GetComponentsInChildren<MeshFilter>();
            var near = filters.First(f => f.name.Contains("LOD0"));
            var far = filters.First(f => f.name.Contains("LOD1"));
            if (!string.IsNullOrEmpty(variant.wind_file))
            {
                // LOD1 is built in the same space with the same part indices, so only LOD0 is fitted.
                var wind = LeafWindBaker.Bake($"{folder}/{variant.wind_file}", near.sharedMesh, null, $"{folder}/{name}_Wind.asset", name);
                LeafWindBaker.Assign(leaf, wind);
                LeafWindBaker.Assign(parts, wind);
            }
            var (nearUntil, midUntil, cullAt) = LodsFor(variant.height);

            var path = DiseaseVariantBuilder.VariantPrefab(plantId, variant.disease);
            DiseaseVariantBuilder.CreateOrRebuild(path, root =>
            {
                // The billboard is baked from the near mesh alone, before the mid mesh joins it.
                var nearRenderer = TomatoPlantBuilder.AddPart(root, "Model", near, true);
                var bake = BillboardBaker.Bake(root, name, BillboardViewSize);
                var billboardMaterial = MaterialFactory.FarmRecovery($"{folder}/{name}_Billboard.mat", bake.HealthyTexture, bake.Texture,
                    BillboardNoise(), null, true, PlantAssetPipeline.BillboardMaskScale);
                // The mid mesh casts no shadow: within the 20 m Quest shadow distance only the near mesh does.
                var midRenderer = TomatoPlantBuilder.AddPart(root, "ModelFar", far, false);
                var billboard = new GameObject("Billboard", typeof(MeshFilter), typeof(MeshRenderer));
                billboard.transform.SetParent(root.transform, false);
                billboard.GetComponent<MeshFilter>().sharedMesh = bake.Mesh;
                var billboardRenderer = billboard.GetComponent<MeshRenderer>();
                billboardRenderer.sharedMaterial = billboardMaterial;
                billboardRenderer.shadowCastingMode = ShadowCastingMode.Off;
                DiseaseVariantBuilder.MakeBatchable(billboard);
                SetLods(root, (nearUntil, nearRenderer), (midUntil, midRenderer), (cullAt, billboardRenderer));
            });

            Debug.Log($"[BTP] {plantId}/{variant.disease}: {variant.height:0.00} m, LOD0 {TomatoPlantBuilder.Tris(near.sharedMesh)} / " +
                      $"LOD1 {TomatoPlantBuilder.Tris(far.sharedMesh)} triangles + billboard.");
            return new DiseaseVariantCatalog.Entry
            {
                plantId = plantId, diseaseId = variant.disease, farmPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path), leafMaterial = leaf,
            };
        }

        /// <summary>
        /// The leaf material: the supplied disease leaf (Week 1), a healthy copy (lesions and chlorosis recoloured to
        /// the leaf's own green, Week 6) and the recovery mask. Cut-out and double-sided: the leaves are cards,
        /// and the sprite's alpha is the leaf's exact outline.
        /// </summary>
        static Material LeafMaterial(string plantId, Variant variant)
        {
            var folder = CropFolder(plantId);
            var diseasedPath = $"{folder}/{variant.leaf_texture}";
            if (!File.Exists(diseasedPath))
                throw new FileNotFoundException($"[BTP] {diseasedPath} is missing; run Tools/blender_farm_crops.py first.");
            var stemName = Path.GetFileNameWithoutExtension(variant.leaf_texture);
            var healthyPath = BlenderTreeBuilder.WriteHealthySprite(diseasedPath, $"{folder}/{stemName}_Healthy.png");
            AssetDatabase.ImportAsset(healthyPath, ImportAssetOptions.ForceSynchronousImport);
            // The sprite sheet is 1024 px for a plant with a few distinct leaves, 2048 px for one with dozens (the
            // rice and wheat blades are each their own sprite); Quest gets half.
            var size = variant.leaf_atlas_size > 0 ? variant.leaf_atlas_size : LeafTextureSize;
            foreach (var path in new[] { diseasedPath, healthyPath })
            {
                BlenderTreeBuilder.ImportSprite(path, size);
                // Cut-out import (coverage-preserving mips, ASTC on Quest); ImportSprite's clamp wrap is kept.
                TextureSettings.ConfigureCutout(path, size, size / 2);
            }
            var diseased = AssetDatabase.LoadAssetAtPath<Texture2D>(diseasedPath);
            var healthy = AssetDatabase.LoadAssetAtPath<Texture2D>(healthyPath);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(FarmRecoveryMasks.Create(healthyPath, $"{folder}/{stemName}_RecoveryMask.png", plantId));
            var material = MaterialFactory.FarmRecovery(LeafMaterialPath(plantId, variant.disease), healthy, diseased, mask, null, true);
            material.SetFloat("_Cutoff", 0.45f);
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D PartsAtlas(string folder, string texture)
        {
            var atlasPath = $"{folder}/{texture}";
            if (!File.Exists(atlasPath))
                throw new FileNotFoundException($"[BTP] {atlasPath} is missing; run Tools/blender_farm_crops.py first.");
            TextureSettings.ConfigureAlbedo(atlasPath, 2048, 1024);
            // The earlier Simple Lit parts material (one per atlas, no wind) is replaced by the per-condition ones.
            var old = $"{folder}/{Path.GetFileNameWithoutExtension(texture)}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(old) != null)
                AssetDatabase.DeleteAsset(old);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        }

        /// <summary>
        /// Stems, fruit, flowers and wood on the export's atlas: opaque, double-sided (petals and husks are sheets).
        /// BTP/Farm Recovery with the atlas as both its healthy and its diseased map, so the timeline leaves it as
        /// it is; the shader is used for its wind.
        /// </summary>
        static Material PartsMaterial(string path, Texture2D atlas)
        {
            var material = MaterialFactory.FarmRecovery(path, atlas, atlas, BillboardNoise(), null, false);
            material.SetFloat("_Cull", 0f);
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D BillboardNoise()
        {
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(PlantAssetPipeline.BillboardNoisePath);
            return noise != null ? noise : AssetDatabase.LoadAssetAtPath<Texture2D>(FarmRecoveryMasks.CreateNoise(PlantAssetPipeline.BillboardNoisePath));
        }

        static void SetLods(GameObject root, params (float until, Renderer renderer)[] levels)
        {
            if (!root.TryGetComponent<LODGroup>(out var group))
                group = root.AddComponent<LODGroup>();
            group.SetLODs(levels.Select(l => new LOD(l.until, new[] { l.renderer })).ToArray());
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
        }

        /// <summary>The same renderers and LOD levels as <paramref name="source"/> (meshes and materials shared).</summary>
        static void CopyContent(GameObject source, GameObject root)
        {
            var sourceLods = source.GetComponent<LODGroup>().GetLODs();
            var copies = new Dictionary<Renderer, Renderer>();
            foreach (var renderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                var go = new GameObject(renderer.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root.transform, false);
                go.transform.SetLocalPositionAndRotation(renderer.transform.localPosition, renderer.transform.localRotation);
                go.transform.localScale = renderer.transform.localScale;
                go.GetComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var copy = go.GetComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = renderer.shadowCastingMode;
                if (renderer.name == "Billboard")
                    DiseaseVariantBuilder.MakeBatchable(go);
                copies[renderer] = copy;
            }

            if (!root.TryGetComponent<LODGroup>(out var group))
                group = root.AddComponent<LODGroup>();
            group.SetLODs(sourceLods.Select(l => new LOD(l.screenRelativeTransitionHeight, l.renderers.Select(r => copies[r]).ToArray())).ToArray());
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
        }
    }
}
