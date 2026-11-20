using System.Collections.Generic;
using BTP.Treatment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// BTP/Treatment/Make Bench Props Grabbable: the small props on the two lab benches nearest the chamber (the spray
    /// mixing bench and the sample prep island, see <see cref="GuidedTreatmentConfig.benchProps"/>) stop being static
    /// (statically batched meshes cannot move at runtime) and each prop's loose parts (a beaker's solution, a bottle's cap)
    /// become its children, so the guided treatment can make them grabbable in Play mode. Run on the open Treatment scene;
    /// save the scene afterwards. Idempotent.
    /// </summary>
    public static class TreatmentBenchProps
    {
        static readonly string[] LooseParts = { "Solution", "Cap", "Label", "LabelPaper" };

        [MenuItem("BTP/Treatment/Make Bench Props Grabbable")]
        public static void Run()
        {
            var config = AssetDatabase.LoadAssetAtPath<GuidedTreatmentConfig>(NpkAssetSetup.ConfigPath);
            var paths = (config != null ? config : ScriptableObject.CreateInstance<GuidedTreatmentConfig>()).benchProps;
            var changed = 0;
            foreach (var path in paths)
            {
                foreach (var prop in Find(path))
                {
                    AdoptLooseParts(prop);
                    foreach (var t in prop.GetComponentsInChildren<Transform>(true))
                    {
                        if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) == 0)
                            continue;
                        Undo.RecordObject(t.gameObject, "Make bench prop dynamic");
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                        changed++;
                    }
                }
            }

            if (changed > 0)
                EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[BTP] Bench props made dynamic: {changed} objects un-static'd across {paths.Count} prop paths.");
        }

        /// <summary>Every object at <paramref name="path"/> ("Lab Dressing/SprayMixingBench/Jug"; several siblings may share the name).</summary>
        static List<Transform> Find(string path)
        {
            var found = new List<Transform>();
            var parts = path.Split('/');
            for (var s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0])
                        continue;
                    var level = new List<Transform> { root.transform };
                    for (var i = 1; i < parts.Length; i++)
                    {
                        var next = new List<Transform>();
                        foreach (var t in level)
                            foreach (Transform child in t)
                                if (child.name == parts[i])
                                    next.Add(child);
                        level = next;
                    }

                    found.AddRange(level);
                }
            }

            return found;
        }

        /// <summary>A loose part that sits on this prop (its solution or cap, the next sibling) becomes its child.</summary>
        static void AdoptLooseParts(Transform prop)
        {
            var parent = prop.parent;
            if (parent == null)
                return;
            var index = prop.GetSiblingIndex();
            for (var i = index + 1; i < parent.childCount && i <= index + 3; i++)
            {
                var next = parent.GetChild(i);
                if (System.Array.IndexOf(LooseParts, next.name) < 0)
                    break;
                var offset = next.position - prop.position;
                if (new Vector2(offset.x, offset.z).magnitude > 0.12f)
                    break;
                Undo.SetTransformParent(next, prop, "Adopt prop part");
                i--;
            }
        }
    }
}
