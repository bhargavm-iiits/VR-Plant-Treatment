using BTP.Plants;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Builds DiseaseGalleryCatalog.asset from the PlantVillage sample leaves in
    /// Assets/_Project/Textures/DiseaseLeaves/&lt;plantId&gt;/&lt;diseaseId&gt;.jpg and the class list in
    /// <see cref="DiseaseClasses"/>. Called by Build Plant Assets, so the order stays 2, 4, 6.
    /// </summary>
    static class DiseaseGalleryBuilder
    {
        public static DiseaseGalleryCatalog Build()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DiseaseGalleryCatalog>(ProjectPaths.DiseaseCatalog);
            if (catalog == null)
            {
                ProjectPaths.EnsureFolder(ProjectPaths.Data);
                catalog = ScriptableObject.CreateInstance<DiseaseGalleryCatalog>();
                AssetDatabase.CreateAsset(catalog, ProjectPaths.DiseaseCatalog);
            }

            catalog.Clear();
            foreach (var info in DiseaseClasses.All)
            {
                var path = $"{ProjectPaths.DiseaseLeaves}/{info.PlantId}/{info.DiseaseId}.jpg";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError($"[BTP] Disease gallery: leaf image missing: {path}");
                    catalog.Add(info.PlantId, info.DiseaseId, info.DisplayName, null);
                    continue;
                }

                // 256 px sample leaves: small, mipmapped, compressed, no anisotropy needed (cards face the viewer).
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 256;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.anisoLevel = 1;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.isReadable = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                catalog.Add(info.PlantId, info.DiseaseId, info.DisplayName, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            }

            EditorUtility.SetDirty(catalog);
            Debug.Log($"[BTP] Disease gallery: {catalog.TotalCount} diseases across {DiseaseClasses.All.Count} classes.");
            return catalog;
        }
    }
}
