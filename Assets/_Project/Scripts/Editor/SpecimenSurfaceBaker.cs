using System.Collections.Generic;
using BTP.Plants;
using BTP.Treatment;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Bakes <see cref="SpecimenSurfacePoints"/>: evenly sampled vertices (mesh space) of the near (LOD0) meshes of every Farm
    /// crop prefab in the plant and disease-variant catalogs, so the Quest player knows where the chamber specimen's leaves
    /// are (the crop meshes are not CPU-readable there). Run by <see cref="QuestDemoBuild.Build"/> before every build.
    /// </summary>
    public static class SpecimenSurfaceBaker
    {
        const string AssetPath = "Assets/_Project/Resources/" + SpecimenSurfacePoints.ResourcePath + ".asset";

        [MenuItem("BTP/Treatment/Bake Specimen Surface Points", priority = 47)]
        public static void Bake()
        {
            var prefabs = new List<GameObject>();
            var plants = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            if (plants != null)
                foreach (var plant in plants.AllPlants)
                    if (plant != null && plant.FarmPrefab != null)
                        prefabs.Add(plant.FarmPrefab);
            var variants = AssetDatabase.LoadAssetAtPath<DiseaseVariantCatalog>(ProjectPaths.VariantCatalog);
            if (variants != null)
                foreach (var entry in variants.Entries)
                    if (entry != null && entry.farmPrefab != null)
                        prefabs.Add(entry.farmPrefab);

            var entries = new List<SpecimenSurfacePoints.Entry>();
            var seen = new HashSet<Mesh>();
            var vertices = new List<Vector3>();
            foreach (var prefab in prefabs)
            foreach (var renderer in SpecimenGeometry.NearRenderers(prefab))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !seen.Add(mesh))
                    continue;
                vertices.Clear();
                mesh.GetVertices(vertices);
                if (vertices.Count == 0)
                    continue;
                var stride = Mathf.Max(1, Mathf.CeilToInt(vertices.Count / (float)SpecimenSurfacePoints.MaxPointsPerMesh));
                var points = new List<Vector3>();
                for (var i = 0; i < vertices.Count; i += stride)
                    points.Add(vertices[i]);
                entries.Add(new SpecimenSurfacePoints.Entry { mesh = mesh, points = points.ToArray() });
            }

            var asset = AssetDatabase.LoadAssetAtPath<SpecimenSurfacePoints>(AssetPath);
            if (asset == null)
            {
                ProjectPaths.EnsureFolder(System.IO.Path.GetDirectoryName(AssetPath)?.Replace('\\', '/'));
                asset = ScriptableObject.CreateInstance<SpecimenSurfacePoints>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.SetEntries(entries);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Specimen surface points: {entries.Count} crop meshes from {prefabs.Count} Farm prefabs.");
        }
    }
}
