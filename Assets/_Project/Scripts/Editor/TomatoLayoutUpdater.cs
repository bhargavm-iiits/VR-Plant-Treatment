using System.Collections.Generic;
using System.Linq;
using BTP.Farm;
using BTP.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Editor
{
    /// <summary>
    /// Applies the real-size tomato layout to the Lab scene and the Treatment room that are already built, without
    /// regenerating them (the Lab is also changed by later setup steps: preview cameras, the leaf display, the
    /// Treatment hand-off). Only the tomato parts are touched. (The Farm's tomatoes are its nine tomato disease rows,
    /// built by FarmSceneBuilder / FarmDiseaseRows; a fresh Farm build is complete, so it has no in-place step.)
    ///   Lab: the tomato decor planter (one plant, scaled to fit, LabSceneBuilder.PlaceDecor);
    ///   Treatment greenhouse room prefab: the potted tomato and bell pepper, moved clear of the walls.
    /// A fresh build from the scene builders gives the same result. Safe to run again.
    /// </summary>
    public static class TomatoLayoutUpdater
    {
        [MenuItem("BTP/Setup/Update Tomato Layout In Built Scenes", priority = 25)]
        public static void UpdateAll()
        {
            var active = SceneManager.GetActiveScene();
            if (active.isDirty)
            {
                Debug.LogError("[BTP] Save the open scene first; updating the tomato layout opens other scenes.");
                return;
            }

            var activePath = active.path;
            Plant("tomato");
            UpdateLab();
            UpdateGreenhouseRoom();
            if (!string.IsNullOrEmpty(activePath))
                EditorSceneManager.OpenScene(activePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// A plant from the catalog. Loaded after each OpenScene: opening a scene unloads assets loaded before it,
        /// and an unloaded PlantDefinition compares equal to null.
        /// </summary>
        static PlantDefinition Plant(string id)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            if (catalog == null || !catalog.TryGet(id, out var plant) || plant.FarmPrefab == null)
                throw new System.InvalidOperationException($"[BTP] No {id} in the plant catalog; run BTP/Setup/2 - Build Plant Assets first.");
            return plant;
        }

        static void UpdateLab()
        {
            var scene = EditorSceneManager.OpenScene(ProjectPaths.LabScene, OpenSceneMode.Single);
            var tomato = Plant("tomato");
            var planters = Find(scene, "Planters");
            if (planters == null)
                throw new System.InvalidOperationException($"[BTP] {ProjectPaths.LabScene} has no Planters; rebuild it with BTP/Setup/5 - Build Lab Scene.");
            var instances = planters.Cast<Transform>().Where(c => PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject) == tomato.FarmPrefab).ToList();
            if (instances.Count == 0)
            {
                Debug.LogWarning($"[BTP] {ProjectPaths.LabScene}: no tomato on the planters; nothing to update.");
                return;
            }

            // The planter centre is the middle of its plants; planters stand at x = -5 + 2 i (LabSceneBuilder).
            var x = instances.Average(t => t.localPosition.x);
            var index = Mathf.RoundToInt((x + 5f) / 2f);
            var sibling = instances.Min(t => t.GetSiblingIndex());
            foreach (var instance in instances)
                Object.DestroyImmediate(instance.gameObject);
            LabSceneBuilder.PlaceDecor(planters, tomato, x, index);
            planters.GetChild(planters.childCount - 1).SetSiblingIndex(sibling);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BTP] {ProjectPaths.LabScene}: {instances.Count} tomato decor plants -> 1 at x {x:0.00}, scale {LabSceneBuilder.DecorTomatoScale}.");
        }

        static void UpdateGreenhouseRoom()
        {
            var path = TreatmentAssetBuilder.GreenhousePrefab;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[BTP] {path} not built yet; BTP/Treatment/2 - Build Treatment Assets places the tomato correctly.");
                return;
            }

            var moves = new List<(GameObject prefab, Vector3 from, Vector3 to)>
            {
                (Plant("tomato").FarmPrefab, new Vector3(2.9f, 0f, 3.2f), TreatmentAssetBuilder.TomatoIndoor),
                (Plant("bell_pepper").FarmPrefab, new Vector3(2.9f, 0f, 2.3f), TreatmentAssetBuilder.PepperIndoor),
            };
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var moved = 0;
                foreach (Transform child in root.transform)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                    foreach (var (prefab, from, to) in moves)
                    {
                        if (prefab != null && source == prefab && (child.localPosition - from).sqrMagnitude < 1e-4f)
                        {
                            child.localPosition = to;
                            moved++;
                        }
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[BTP] {path}: moved {moved} potted plants (tomato to {TreatmentAssetBuilder.TomatoIndoor}, pepper to {TreatmentAssetBuilder.PepperIndoor}).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Transform Root(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(g => g.name == name)?.transform;

        static Transform Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var match = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                if (match != null)
                    return match;
            }

            return null;
        }
    }
}
