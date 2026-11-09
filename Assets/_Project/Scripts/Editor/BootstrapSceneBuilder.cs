using System.Collections.Generic;
using System.IO;
using BTP.Core;
using BTP.Plants;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.Editor
{
    /// <summary>
    /// Builds XRBootstrap.unity: the one persistent XR rig (controllers + hands), fade overlay,
    /// XR UI event system and the shared selection / environment systems. It is first in the
    /// build and loads the Farm (Envirornment.unity) on start.
    /// </summary>
    public static class BootstrapSceneBuilder
    {
        const string HandsRig = "Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";

        [MenuItem("BTP/Setup/3 - Build XR Bootstrap Scene", priority = 3)]
        public static void Build()
        {
            // Load assets after NewScene: opening a scene unloads unused assets loaded before it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            if (catalog == null)
                throw new System.InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first.");
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandsRig);
            if (rigPrefab == null)
                throw new System.InvalidOperationException($"Import the XR Interaction Toolkit 'Hands Interaction Demo' sample ({HandsRig}).");

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            rig.name = "XR Origin (XR Rig)";
            var origin = rig.GetComponent<XROrigin>();
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            PrefabUtility.RecordPrefabInstancePropertyModifications(origin);

            var camera = origin.Camera;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 450f;
            camera.clearFlags = CameraClearFlags.Skybox;
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cameraData);

            ConfigureLocomotion(rig);
            SelectWithTrigger(rig);
            rig.AddComponent<RecenterControl>();
            rig.AddComponent<DesktopDemoControls>().Bind(rig.transform, camera);

            var fade = CreateFadeOverlay(camera.transform);

            new GameObject("XR Interaction Manager", typeof(XRInteractionManager));
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));

            var systems = new GameObject("BTP Systems");
            systems.AddComponent<SelectionState>().Bind(catalog);
            systems.AddComponent<EnvironmentController>().Bind(SceneName(ProjectPaths.FarmScene), SceneName(ProjectPaths.LabScene),
                rig.transform, camera.transform, fade);
            systems.AddComponent<DeviceQualityProfile>();

            EditorSceneManager.SaveScene(scene, ProjectPaths.BootstrapScene);
            UpdateBuildScenes();
            Debug.Log($"[BTP] Built {ProjectPaths.BootstrapScene}.");
        }

        /// <summary>Teleport and 30° snap turn by default; smooth movement and smooth turn stay off.</summary>
        static void ConfigureLocomotion(GameObject rig)
        {
            foreach (var snapTurn in rig.GetComponentsInChildren<SnapTurnProvider>(true))
            {
                snapTurn.turnAmount = 30f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(snapTurn);
            }

            foreach (var behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "ControllerInputActionManager")
                    continue;
                var so = new SerializedObject(behaviour);
                so.FindProperty("m_SmoothMotionEnabled").boolValue = false;
                so.FindProperty("m_SmoothTurnEnabled").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Controllers select plants with the trigger, like pressing UI buttons; there is nothing
        /// to grab, so the grip is not needed. Hands keep pinch-to-select.
        /// </summary>
        static void SelectWithTrigger(GameObject rig)
        {
            foreach (var interactor in rig.GetComponentsInChildren<NearFarInteractor>(true))
            {
                if (interactor.transform.parent == null || !interactor.transform.parent.name.Contains("Controller"))
                    continue;
                var so = new SerializedObject(interactor);
                foreach (var field in new[] { "m_InputActionReferencePerformed", "m_InputActionReferenceValue" })
                {
                    var activate = so.FindProperty($"m_ActivateInput.{field}");
                    var select = so.FindProperty($"m_SelectInput.{field}");
                    if (activate != null && select != null && activate.objectReferenceValue != null)
                        select.objectReferenceValue = activate.objectReferenceValue;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static FadeOverlay CreateFadeOverlay(Transform head)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "FadeOverlay";
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(head, false);
            sphere.transform.localScale = Vector3.one * 0.6f;
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFactory.GetOrCreate(ProjectPaths.UIMaterials + "/FadeOverlay.mat", MaterialFactory.FadeShader);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var fade = sphere.AddComponent<FadeOverlay>();
            fade.Bind(renderer);
            return fade;
        }

        static string SceneName(string path) => Path.GetFileNameWithoutExtension(path);

        /// <summary>XRBootstrap first (it loads the Farm), then the two environments.</summary>
        public static void UpdateBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (var path in new[] { ProjectPaths.BootstrapScene, ProjectPaths.FarmScene, ProjectPaths.LabScene })
            {
                if (File.Exists(path))
                    scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
