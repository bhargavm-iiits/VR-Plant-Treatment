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
            // The 24 Farm classes first (the Farm gallery reads only those plants), then the 34 Lab-only conditions for the
            // Lab's reference card: a PlantVillage photo (.jpg) where the dataset has one, otherwise a render of the
            // supplied specimen (.png, Tools/prepare_leaf_references.py), labelled as such.
            foreach (var info in DiseaseClasses.AllCases)
            {
                var path = $"{ProjectPaths.DiseaseLeaves}/{info.PlantId}/{info.DiseaseId}.jpg";
                string note = null;
                if (DiseaseClasses.IsLabOnlyPlant(info.PlantId))
                {
                    note = "PlantVillage photo";
                    if (!System.IO.File.Exists(path))
                    {
                        path = $"{ProjectPaths.DiseaseLeaves}/{info.PlantId}/{info.DiseaseId}.png";
                        note = "Specimen render, not a field photo";
                    }

                    if (System.IO.File.Exists(path))
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError($"[BTP] Disease gallery: leaf image missing: {path}");
                    catalog.Add(info.PlantId, info.DiseaseId, info.DisplayName, null, note);
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
                catalog.Add(info.PlantId, info.DiseaseId, info.DisplayName, AssetDatabase.LoadAssetAtPath<Texture2D>(path), note);
            }

            EditorUtility.SetDirty(catalog);
            Debug.Log($"[BTP] Disease gallery: {catalog.TotalCount} reference images ({DiseaseClasses.All.Count} Farm classes + " +
                      $"{DiseaseClasses.LabOnly.Count} Lab-only).");
            return catalog;
        }
    }
}
