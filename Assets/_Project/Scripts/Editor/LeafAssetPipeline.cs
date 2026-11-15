using System;
using System.Collections.Generic;
using System.IO;
using BTP.Plants;
using BTP.Lab;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>Builds display prefabs from optimized copies, keeping source leaf files intact.</summary>
    public static class LeafAssetPipeline
    {
        public const string CatalogPath = "Assets/_Project/Data/LeafCatalog.asset";
        const string Prefabs = "Assets/_Project/Prefabs/Leaves";
        const string Materials = "Assets/_Project/Materials/Leaves";
        const string Textures = "Assets/_Project/Textures/Leaves";
        [Serializable] sealed class Manifest { public Source[] entries; }
        [Serializable] sealed class Source
        {
            public string plantId;
            public string diseaseId;
            public string model;
            public string albedo;
        }
        [Serializable] sealed class SurfaceManifest { public SurfaceEntry[] entries; }
        [Serializable] sealed class SurfaceEntry { public string name; public float[] euler; }

        [MenuItem("BTP/Setup/6 - Prepare Leaf Display Assets")]
        public static void Build()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("Tools/leaf_manifest.json"));
            var surfaces = JsonUtility.FromJson<SurfaceManifest>(File.ReadAllText("Tools/leaf_surface_manifest.json"));
            if (manifest.entries.Length != DiseaseClasses.AllCases.Count)
                throw new InvalidOperationException($"Leaf manifest must cover all {DiseaseClasses.AllCases.Count} conditions " +
                                                    $"(24 Farm + {DiseaseClasses.LabOnly.Count} Lab-only); it lists {manifest.entries.Length}.");
            foreach (var info in DiseaseClasses.AllCases)
                if (Array.FindIndex(manifest.entries, e => e.plantId == info.PlantId && e.diseaseId == info.DiseaseId) < 0)
                    throw new InvalidOperationException($"Leaf manifest has no model for {info.PlantId}/{info.DiseaseId}.");
            foreach (var folder in new[] { Prefabs, Materials, Textures })
                ProjectPaths.EnsureFolder(folder);
            var shader = Shader.Find("BTP/Leaf Recovery");
            if (shader == null) throw new InvalidOperationException("Leaf Recovery shader is missing.");
            var catalog = AssetDatabase.LoadAssetAtPath<LeafCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LeafCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var entries = new List<LeafCatalog.Entry>();
            foreach (var source in manifest.entries)
            {
                string name = source.plantId + "_" + source.diseaseId;
                AssetDatabase.ImportAsset(source.model, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(source.model);
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = false;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
                importer.normalSmoothingAngle = 120f;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(source.model);
                if (model == null) throw new InvalidOperationException("Leaf model missing: " + source.model);
                string texturePath = Textures + "/" + name + ".png";
                if (!File.Exists(texturePath))
                {
                    var (pixels, width, height) = FarmDiseaseTextures.Read(source.albedo);
                    while (width > 2048 || height > 2048)
                        (pixels, width, height) = FarmDiseaseTextures.Halve(pixels, width, height);
                    var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    image.SetPixels32(pixels);
                    image.Apply();
                    File.WriteAllBytes(texturePath, image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                }
                TextureSettings.ConfigureAlbedo(texturePath, 2048, 1024);
                string materialPath = Materials + "/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.shader = shader;
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                string healthy = Textures + "/" + name + "_Healthy.png";
                string recovery = Textures + "/" + name + "_Recovery.png";
                string normal = Textures + "/" + name + "_normal.png";
                string roughness = Textures + "/" + name + "_roughness.png";
                // A supplied map can be missing (soybean/septoria_brown_spot has no roughness texture): it is skipped and
                // the material keeps the shader default (_RoughnessMap "gray" = mid roughness) instead of an invented map.
                bool hasRoughness = File.Exists(roughness);
                foreach (var path in new[] { healthy, recovery, normal, roughness })
                    if (path != roughness || hasRoughness)
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                TextureSettings.ConfigureAlbedo(healthy, 2048, 1024);
                TextureSettings.ConfigureData(recovery, 1024);
                TextureSettings.ConfigureNormal(normal, 2048, 1024);
                if (hasRoughness)
                    TextureSettings.ConfigureData(roughness, 1024);
                else
                    Debug.LogWarning($"[BTP] {name}: no supplied roughness map; the leaf material uses the shader's default roughness.");
                material.SetTexture("_HealthyMap", AssetDatabase.LoadAssetAtPath<Texture2D>(healthy));
                material.SetTexture("_RecoveryMap", AssetDatabase.LoadAssetAtPath<Texture2D>(recovery));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                material.SetTexture("_RoughnessMap", hasRoughness ? AssetDatabase.LoadAssetAtPath<Texture2D>(roughness) : null);
                material.SetFloat("_BumpScale", 0.65f);
                material.SetFloat("_Transmission", 0.12f);
                material.SetFloat("_Recovery", 0f);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                var root = new GameObject(name);
                try
                {
                    var child = UnityEngine.Object.Instantiate(model, root.transform, false);
                    child.name = "Leaf";
                    foreach (var surface in surfaces.entries)
                        if (surface.name == name) child.transform.localEulerAngles = new Vector3(surface.euler[0], surface.euler[1], surface.euler[2]);
                    foreach (var renderer in child.GetComponentsInChildren<Renderer>())
                    {
                        var slots = new Material[renderer.sharedMaterials.Length];
                        for (int i = 0; i < slots.Length; i++) slots[i] = material;
                        renderer.sharedMaterials = slots;
                    }
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
                    entries.Add(new LeafCatalog.Entry { plantId = source.plantId, diseaseId = source.diseaseId, prefab = prefab });
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            catalog.SetEntries(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[BTP] Leaf catalog prepared: " + entries.Count + " supplied condition models.");
        }

        public static void RefreshHealthySurfaces()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("Tools/leaf_manifest.json"));
            foreach (var source in manifest.entries)
            {
                string name = source.plantId + "_" + source.diseaseId;
                string healthy = Textures + "/" + name + "_Healthy.png";
                string recovery = Textures + "/" + name + "_Recovery.png";
                AssetDatabase.ImportAsset(healthy, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(recovery, ImportAssetOptions.ForceSynchronousImport);
                TextureSettings.ConfigureAlbedo(healthy, 2048, 1024);
                TextureSettings.ConfigureData(recovery, 1024);
            }
            var data = AssetDatabase.LoadAssetAtPath<TomatoRecoveryData>(ProjectPaths.RecoveryData);
            data.ResetToBrief();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }
    }
}
