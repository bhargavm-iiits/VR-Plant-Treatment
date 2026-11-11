using BTP.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Editor
{
    /// <summary>
    /// Adds the editor preview camera to the existing Farm and Lab scenes without rebuilding
    /// them, so any manual edits are kept. Scenes that already have one are left unchanged.
    /// </summary>
    public static class PreviewCameraInstaller
    {
        [MenuItem("BTP/Setup/6 - Add Preview Cameras To Environment Scenes", priority = 6)]
        public static void AddToEnvironmentScenes()
        {
            var active = SceneManager.GetActiveScene();
            if (active.isDirty)
            {
                Debug.LogError("[BTP] Save the open scene first; adding preview cameras opens other scenes.");
                return;
            }

            foreach (var path in new[] { ProjectPaths.FarmScene, ProjectPaths.LabScene })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (Object.FindFirstObjectByType<ScenePreviewCamera>(FindObjectsInactive.Include) != null)
                    continue;

                var anchor = Object.FindFirstObjectByType<SpawnAnchor>(FindObjectsInactive.Include);
                if (anchor == null)
                {
                    Debug.LogError($"[BTP] {path} has no SpawnAnchor; no preview camera added.");
                    continue;
                }

                EnvironmentKit.PreviewCamera(anchor.transform);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[BTP] Added a preview camera to {path}.");
            }

            if (!string.IsNullOrEmpty(active.path))
                EditorSceneManager.OpenScene(active.path, OpenSceneMode.Single);
        }
    }
}
