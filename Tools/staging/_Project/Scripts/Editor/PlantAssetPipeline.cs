using System;
using System.Collections.Generic;
using System.Linq;
using BTP.Lab;
using BTP.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Builds everything the seven selectable plants need from the optimized models in
    /// Assets/_Project/Models/Plants (see Tools/decimate_all.ps1): import settings, URP
    /// materials, lab prefabs, farm prefabs (near mesh + baked billboard LOD), the plant
    /// catalog and the Tomato recovery data. Safe to run again; assets are updated in place.
    /// </summary>
    public static class PlantAssetPipeline
    {
        sealed class PlantSpec
        {
            public string Id;
            public string Name;
            public string Scientific;
            public string Family;
            public string Type;
            public string Summary;
            public float Height;             // real-world height in metres
            public string LabModel;          // null: built from the corn glTF
            public string FarmModel;
            public string LabAlbedo;
            public string LabNormal;
            public string FarmAlbedo;
            public bool Cutout;              // alpha-clipped leaf cards
            public bool RecoveryDemo;
            public float BillboardAt = 0.12f; // screen height where the near mesh gives way to the billboard
            public float CullAt = 0.012f;
            public int BillboardViewSize = 256;
        }

        const string Meshy = ProjectPaths.SourcePlants;

        static readonly PlantSpec[] Specs =
        {
            new PlantSpec
            {
                Id = "apple", Name = "Apple", Scientific = "Malus domestica", Family = "Rosaceae", Type = "Deciduous fruit tree",
                Summary = "Temperate orchard tree grown for its fruit, a pome. Orchard trees are usually grafted onto a rootstock that controls their size.",
                Height = 3.5f, LabModel = ProjectPaths.Models + "/Apple_Lab.fbx", FarmModel = ProjectPaths.Models + "/Apple_Farm.fbx",
                LabAlbedo = Meshy + "/Apple_new/Meshy_AI_apple_tree_3d_0923203849_image-to-3d-texture_fbx/Meshy_AI_apple_tree_3d_0923203849_image-to-3d-texture.png",
                LabNormal = Meshy + "/Apple_new/Meshy_AI_apple_tree_3d_0923203849_image-to-3d-texture_fbx/Meshy_AI_apple_tree_3d_0923203849_image-to-3d-texture_normal.png",
                BillboardAt = 0.45f, CullAt = 0.008f, BillboardViewSize = 512
            },
            new PlantSpec
            {
                Id = "bell_pepper", Name = "Bell Pepper", Scientific = "Capsicum annuum", Family = "Solanaceae", Type = "Warm-season fruiting vegetable",
                Summary = "Nightshade-family crop grown as an annual for its large, mild, blocky fruit, which ripens from green to red, yellow or orange.",
                Height = 0.7f, LabModel = ProjectPaths.Models + "/BellPepper_Lab.fbx", FarmModel = ProjectPaths.Models + "/BellPepper_Farm.fbx",
                LabAlbedo = Meshy + "/Bell Pepper/Meshy_AI_Colorful_Bell_Pepper__0923184552_texture_fbx/Meshy_AI_Colorful_Bell_Pepper__0923184552_texture.png",
                LabNormal = Meshy + "/Bell Pepper/Meshy_AI_Colorful_Bell_Pepper__0923184552_texture_fbx/Meshy_AI_Colorful_Bell_Pepper__0923184552_texture_normal.png",
                BillboardAt = 0.16f
            },
            new PlantSpec
            {
                Id = "cherry", Name = "Cherry", Scientific = "Prunus avium", Family = "Rosaceae", Type = "Deciduous fruit tree",
                Summary = "Sweet cherry: a temperate orchard tree grown for its stone fruit (a drupe), which grows in small clusters on long stalks.",
                Height = 3.5f, LabModel = ProjectPaths.Models + "/Cherry_Lab.fbx", FarmModel = ProjectPaths.Models + "/Cherry_Lab.fbx",
                LabAlbedo = ProjectPaths.PlantTextures + "/Cherry_BaseAlpha.png",
                LabNormal = Meshy + "/Cherry/fbx/Cherry1_Normal.tga",
                Cutout = true, BillboardAt = 0.3f, CullAt = 0.008f, BillboardViewSize = 512
            },
            new PlantSpec
            {
                Id = "corn", Name = "Corn (Maize)", Scientific = "Zea mays", Family = "Poaceae", Type = "Annual cereal grass",
                Summary = "Tall annual grass grown for its grain, one of the world's major cereal crops. Each plant carries a tassel on top and ears lower on the stalk.",
                // 60 corn plants in the field: keep full meshes to about 4 m so the block stays near 45k triangles.
                Height = 2.2f, Cutout = true, BillboardAt = 0.25f, CullAt = 0.01f, BillboardViewSize = 512
            },
            new PlantSpec
            {
                Id = "potato", Name = "Potato", Scientific = "Solanum tuberosum", Family = "Solanaceae", Type = "Tuber crop",
                Summary = "Nightshade-family crop grown for the starchy tubers that form underground; usually planted from seed tubers rather than seed.",
                Height = 0.5f, LabModel = ProjectPaths.Models + "/Potato_Lab.fbx", FarmModel = ProjectPaths.Models + "/Potato_Farm.fbx",
                LabAlbedo = ProjectPaths.PlantTextures + "/Potato_BaseAlpha.png",
                LabNormal = Meshy + "/potato-plants-collection/textures/Potato_Atlas_PBR_NORMAL.jpg",
                Cutout = true
            },
            new PlantSpec
            {
                Id = "strawberry", Name = "Strawberry", Scientific = "Fragaria × ananassa", Family = "Rosaceae", Type = "Low-growing perennial",
                Summary = "Low-growing perennial that spreads by runners. Its red 'fruit' is an enlarged receptacle carrying the true fruits, the small seed-like achenes.",
                Height = 0.3f, LabModel = ProjectPaths.Models + "/Strawberry_Lab.fbx", FarmModel = ProjectPaths.Models + "/Strawberry_Farm.fbx",
                LabAlbedo = Meshy + "/Strawberry_1/Meshy_AI_strawberry_plant_0923200556_texture_fbx/Meshy_AI_strawberry_plant_0923200556_texture.png"
            },
            new PlantSpec
            {
                Id = "tomato", Name = "Tomato", Scientific = "Solanum lycopersicum", Family = "Solanaceae", Type = "Warm-season fruiting vegetable",
                Summary = "Warm-season nightshade crop grown for its fruit. Vining types are usually staked or caged and trained to one or a few main stems.",
                Height = 1.0f, LabModel = ProjectPaths.Models + "/Tomato_Lab.fbx", FarmModel = ProjectPaths.Models + "/Tomato_Farm.fbx",
                LabAlbedo = Meshy + "/Tomato_asset/Meshy_AI_Single_Stem_Tomato_Pl_0923211133_image-to-3d-texture_fbx/Meshy_AI_Single_Stem_Tomato_Pl_0923211133_image-to-3d-texture.png",
                LabNormal = Meshy + "/Tomato_asset/Meshy_AI_Single_Stem_Tomato_Pl_0923211133_image-to-3d-texture_fbx/Meshy_AI_Single_Stem_Tomato_Pl_0923211133_image-to-3d-texture_normal.png",
                FarmAlbedo = Meshy + "/Tomato/Meshy_AI_Tomato_Plant_0923194145_texture_fbx/Meshy_AI_Tomato_Plant_0923194145_texture.png",
                RecoveryDemo = true
            },
        };

        const string CornSource = ProjectPaths.SourcePlants + "/maize_corn_plant.glb";
        const string TomatoDiseaseMask = ProjectPaths.PlantTextures + "/TomatoLab_DiseaseMask.png";

        [MenuItem("BTP/Setup/2 - Build Plant Assets", priority = 2)]
        public static void BuildAll()
        {
            try
            {
                foreach (var folder in new[]
                         {
                             ProjectPaths.PlantMaterials, ProjectPaths.PlantPrefabs, ProjectPaths.PlantData,
                             ProjectPaths.BillboardMeshes, ProjectPaths.BillboardTextures, ProjectPaths.Resources, ProjectPaths.Meshes
                         })
                    ProjectPaths.EnsureFolder(folder);

                CreateSetupErrorMaterial();
                var definitions = new List<PlantDefinition>();
                for (var i = 0; i < Specs.Length; i++)
                {
                    var spec = Specs[i];
                    EditorUtility.DisplayProgressBar("BTP plant assets", spec.Name, (float)i / Specs.Length);
                    var (farm, lab) = spec.Id == "corn" ? BuildCorn(spec) : BuildMeshPlant(spec);
                    definitions.Add(SaveDefinition(spec, farm, lab));
                }

                SaveCatalog(definitions);
                SaveRecoveryData();
                AssetDatabase.SaveAssets();
                Debug.Log($"[BTP] Built plant assets for {definitions.Count} plants.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static (GameObject farm, GameObject lab) BuildMeshPlant(PlantSpec spec)
        {
            var farmAlbedoPath = spec.FarmAlbedo ?? spec.LabAlbedo;
            ConfigureTexture(spec.LabAlbedo, spec.Cutout);
            if (farmAlbedoPath != spec.LabAlbedo)
                ConfigureTexture(farmAlbedoPath, spec.Cutout);
            if (spec.LabNormal != null)
                TextureSettings.ConfigureNormal(spec.LabNormal);

            ConfigureModel(spec.LabModel, spec.LabNormal != null, spec.Height);
            if (spec.FarmModel != spec.LabModel)
                ConfigureModel(spec.FarmModel, false, spec.Height);

            var labAlbedo = Load<Texture2D>(spec.LabAlbedo);
            var labNormal = spec.LabNormal != null ? Load<Texture2D>(spec.LabNormal) : null;
            var fileName = spec.Name.Replace(" ", "");

            Material labMaterial;
            if (spec.RecoveryDemo)
            {
                TextureSettings.ConfigureData(TomatoDiseaseMask);
                labMaterial = MaterialFactory.LeafDisease($"{ProjectPaths.PlantMaterials}/{fileName}_Lab.mat",
                    labAlbedo, labNormal, Load<Texture2D>(TomatoDiseaseMask));
            }
            else
            {
                labMaterial = MaterialFactory.SimpleLit($"{ProjectPaths.PlantMaterials}/{fileName}_Lab.mat", labAlbedo, labNormal, spec.Cutout);
            }

            var farmMaterial = MaterialFactory.SimpleLit($"{ProjectPaths.PlantMaterials}/{fileName}_Farm.mat",
                Load<Texture2D>(farmAlbedoPath), null, spec.Cutout);

            var lab = SaveModelPrefab($"{fileName}_Lab", Load<GameObject>(spec.LabModel), labMaterial);
            var farm = SaveFarmPrefab(spec, fileName, Load<GameObject>(spec.FarmModel), farmMaterial, null);
            return (farm, lab);
        }

        // ------------------------------------------------------------------ corn (glTFast)

        static (GameObject farm, GameObject lab) BuildCorn(PlantSpec spec)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(CornSource);
            if (source == null)
                throw new InvalidOperationException($"{CornSource} did not import as a model. Is com.unity.cloud.gltfast installed?");

            var (mesh, sourceMaterials) = CombineCorn(source, spec.Height);
            var materials = new Material[sourceMaterials.Count];
            for (var i = 0; i < sourceMaterials.Count; i++)
            {
                var gltf = sourceMaterials[i];
                var albedo = FindTexture(gltf, "baseColorTexture", "_BaseMap", "_MainTex", "basecolor");
                var normal = FindTexture(gltf, "normalTexture", "_BumpMap", "normal");
                var cutout = gltf.name.ToLowerInvariant().Contains("alpha") || gltf.renderQueue >= (int)RenderQueue.AlphaTest;
                materials[i] = MaterialFactory.SimpleLit($"{ProjectPaths.PlantMaterials}/Corn_{i}_{Sanitize(gltf.name)}.mat", albedo, normal, cutout);
            }

            var lab = SaveMeshPrefab("Corn_Lab", mesh, materials, true);
            var farm = SaveFarmPrefab(spec, "Corn", null, null, (mesh, materials));
            return (farm, lab);
        }

        /// <summary>
        /// Merges the glTF hierarchy into one mesh with one sub-mesh per material, pivoted at
        /// the bottom centre and scaled to <paramref name="height"/> metres.
        /// </summary>
        static (Mesh mesh, List<Material> materials) CombineCorn(GameObject source, float height)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                var filters = instance.GetComponentsInChildren<MeshFilter>();
                var bounds = new Bounds();
                var first = true;
                foreach (var filter in filters)
                {
                    var b = filter.GetComponent<Renderer>().bounds;
                    if (first)
                        bounds = b;
                    else
                        bounds.Encapsulate(b);
                    first = false;
                }

                var scale = height / bounds.size.y;
                var toPivot = Matrix4x4.Scale(Vector3.one * scale) *
                              Matrix4x4.Translate(-new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));

                var groups = new Dictionary<Material, List<CombineInstance>>();
                foreach (var filter in filters)
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    var mesh = filter.sharedMesh;
                    for (var sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        var material = renderer.sharedMaterials[Mathf.Min(sub, renderer.sharedMaterials.Length - 1)];
                        if (!groups.TryGetValue(material, out var list))
                            groups[material] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = toPivot * filter.transform.localToWorldMatrix });
                    }
                }

                var materials = groups.Keys.ToList();
                var parts = new List<CombineInstance>();
                foreach (var material in materials)
                {
                    var part = new Mesh();
                    part.CombineMeshes(groups[material].ToArray(), true, true);
                    parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                }

                var combined = new Mesh { name = "Corn_Combined" };
                combined.CombineMeshes(parts.ToArray(), false, false);
                combined.RecalculateBounds();
                foreach (var part in parts)
                    Object.DestroyImmediate(part.mesh);

                var path = ProjectPaths.Meshes + "/Corn_Combined.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(combined, existing);
                    Object.DestroyImmediate(combined);
                    combined = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(combined, path);
                }

                Debug.Log($"[BTP] Corn: {filters.Length} glTF meshes combined into {materials.Count} sub-meshes, " +
                          $"{combined.triangles.Length / 3} triangles, scaled x{scale:0.###} to {height} m.");
                return (combined, materials);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Texture2D FindTexture(Material material, params string[] candidates)
        {
            foreach (var property in material.GetTexturePropertyNames())
            {
                foreach (var candidate in candidates)
                {
                    if (property.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        material.GetTexture(property) is Texture2D texture)
                        return texture;
                }
            }

            return null;
        }

        static string Sanitize(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray());

        // ------------------------------------------------------------------ prefabs

        static GameObject SaveModelPrefab(string name, GameObject model, Material material)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = CreateRoot(name, scene);
                AddModel(root, model, material, scene);
                return PrefabUtility.SaveAsPrefabAsset(root, $"{ProjectPaths.PlantPrefabs}/{name}.prefab");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static GameObject SaveMeshPrefab(string name, Mesh mesh, Material[] materials, bool castShadows)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = CreateRoot(name, scene);
                AddMesh(root, "Model", mesh, materials, castShadows);
                return PrefabUtility.SaveAsPrefabAsset(root, $"{ProjectPaths.PlantPrefabs}/{name}.prefab");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Farm prefab: LODGroup with the near mesh (LOD0) and a baked billboard (LOD1).</summary>
        static GameObject SaveFarmPrefab(PlantSpec spec, string fileName, GameObject model, Material material,
            (Mesh mesh, Material[] materials)? combined)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = CreateRoot($"{fileName}_Farm", scene);
                var near = combined.HasValue
                    ? AddMesh(root, "Model", combined.Value.mesh, combined.Value.materials, true)
                    : AddModel(root, model, material, scene);

                var billboard = BillboardBaker.Bake(root, fileName, spec.BillboardViewSize);
                var billboardMaterial = MaterialFactory.SimpleLit($"{ProjectPaths.PlantMaterials}/{fileName}_Billboard.mat",
                    billboard.Texture, null, true);
                var far = AddMesh(root, "Billboard", billboard.Mesh, new[] { billboardMaterial }, false);

                var lodGroup = root.AddComponent<LODGroup>();
                lodGroup.SetLODs(new[]
                {
                    new LOD(spec.BillboardAt, near.GetComponentsInChildren<Renderer>()),
                    new LOD(spec.CullAt, far.GetComponentsInChildren<Renderer>())
                });
                lodGroup.fadeMode = LODFadeMode.None;
                lodGroup.RecalculateBounds();
                return PrefabUtility.SaveAsPrefabAsset(root, $"{ProjectPaths.PlantPrefabs}/{fileName}_Farm.prefab");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static GameObject CreateRoot(string name, Scene scene)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        static GameObject AddModel(GameObject root, GameObject model, Material material, Scene scene)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            instance.name = "Model";
            instance.transform.SetParent(root.transform, false);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }

            return instance;
        }

        static GameObject AddMesh(GameObject root, string name, Mesh mesh, Material[] materials, bool castShadows)
        {
            var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(child, root.scene);
            child.transform.SetParent(root.transform, false);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return child;
        }

        // ------------------------------------------------------------------ import settings

        static void ConfigureTexture(string path, bool cutout)
        {
            if (cutout)
                TextureSettings.ConfigureCutout(path);
            else
                TextureSettings.ConfigureAlbedo(path);
        }

        static void ConfigureModel(string path, bool tangents, float expectedHeight)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"Optimized model not found: {path}. Copy it from Tools/out/models (see Tools/decimate_all.ps1).");

            importer.useFileScale = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = tangents ? ModelImporterTangents.CalculateMikk : ModelImporterTangents.None;
            importer.meshOptimizationFlags = MeshOptimizationFlags.Everything;
            importer.SaveAndReimport();

            // The Blender export is in metres; correct the import scale if a unit mismatch crept in.
            var measured = MeasureHeight(Load<GameObject>(path));
            var ratio = expectedHeight / Mathf.Max(measured, 1e-4f);
            if (ratio < 0.8f || ratio > 1.25f)
            {
                importer.globalScale *= ratio;
                importer.SaveAndReimport();
                Debug.Log($"[BTP] {path}: imported {measured:0.###} m tall, rescaled x{ratio:0.###} to {expectedHeight} m.");
            }
        }

        static float MeasureHeight(GameObject model)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    return 0f;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                return bounds.size.y;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // ------------------------------------------------------------------ data assets

        static PlantDefinition SaveDefinition(PlantSpec spec, GameObject farm, GameObject lab)
        {
            var definition = LoadOrCreate<PlantDefinition>($"{ProjectPaths.PlantData}/{spec.Id}.asset");
            definition.Configure(spec.Id, spec.Name, spec.Scientific, spec.Family, spec.Type, spec.Summary, spec.RecoveryDemo);
            definition.SetPrefabs(farm, lab);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static void SaveCatalog(List<PlantDefinition> definitions)
        {
            var catalog = LoadOrCreate<PlantCatalog>(ProjectPaths.Catalog);
            catalog.SetPlants(definitions);
            EditorUtility.SetDirty(catalog);
            foreach (var problem in catalog.Validate())
                Debug.LogError($"[BTP] Plant catalog: {problem}", catalog);
        }

        static void SaveRecoveryData()
        {
            var data = LoadOrCreate<TomatoRecoveryData>(ProjectPaths.RecoveryData);
            data.ResetToBrief();
            EditorUtility.SetDirty(data);
        }

        static void CreateSetupErrorMaterial() =>
            MaterialFactory.Unlit($"{ProjectPaths.Resources}/SetupErrorMaterial.mat", new Color(0.9f, 0.1f, 0.1f));

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Missing asset: {path}");
            return asset;
        }
    }
}
