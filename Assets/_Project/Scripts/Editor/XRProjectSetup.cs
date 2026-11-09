using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace BTP.Editor
{
    /// <summary>
    /// One-click XR project configuration: OpenXR for Meta Quest (Android) and Windows PC VR,
    /// Quest player settings, a Quest-2-first URP profile and the Editor XR simulator.
    /// </summary>
    public static class XRProjectSetup
    {
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string SimulatorPrefab = "Assets/Samples/XR Interaction Toolkit/3.5.1/XR Interaction Simulator/XR Interaction Simulator.prefab";
        public const string ApplicationId = "com.btp.plantdiseasevr";

        [MenuItem("BTP/Setup/1 - Configure XR Project", priority = 1)]
        public static void Configure()
        {
            NameTeleportInteractionLayer();
            ConfigureLoaders();
            ConfigureOpenXR(BuildTargetGroup.Android, true);
            ConfigureOpenXR(BuildTargetGroup.Standalone, false);
            ConfigurePlayer();
            ConfigureQuestRenderPipeline();
            ConfigureSimulator();
            AssetDatabase.SaveAssets();
            Debug.Log("[BTP] XR project configured: OpenXR for Quest (Android) and Windows PC VR.");
        }

        /// <summary>XRI samples reserve interaction layer 31 for teleportation.</summary>
        static void NameTeleportInteractionLayer()
        {
            var guids = AssetDatabase.FindAssets("InteractionLayerSettings t:ScriptableObject");
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                var serialized = new SerializedObject(asset);
                var names = serialized.FindProperty("m_LayerNames");
                if (names == null)
                    continue;
                if (names.arraySize < 32)
                    names.arraySize = 32;
                names.GetArrayElementAtIndex(XRSceneWiring.TeleportLayerIndex).stringValue = XRSceneWiring.TeleportLayerName;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return;
            }

            Debug.LogError("[BTP] InteractionLayerSettings asset not found; open Project Settings > XR Interaction Toolkit once to create it.");
        }

        static void ConfigureLoaders()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                ProjectPaths.EnsureFolder("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!perTarget.HasSettingsForBuildTarget(group))
                    perTarget.CreateDefaultSettingsForBuildTarget(group);
                if (!perTarget.HasManagerSettingsForBuildTarget(group))
                    perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

                var general = perTarget.SettingsForBuildTarget(group);
                general.InitManagerOnStart = true;
                XRPackageMetadataStore.AssignLoader(general.AssignedSettings, OpenXRLoader, group);
                EditorUtility.SetDirty(general);
                EditorUtility.SetDirty(general.AssignedSettings);
            }

            EditorUtility.SetDirty(perTarget);
        }

        static void ConfigureOpenXR(BuildTargetGroup group, bool quest)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                Debug.LogError($"[BTP] No OpenXR settings for {group}.");
                return;
            }

            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;

            void Enable<T>() where T : OpenXRFeature
            {
                var feature = settings.GetFeature<T>();
                if (feature == null)
                {
                    Debug.LogWarning($"[BTP] OpenXR feature {typeof(T).Name} is not available for {group}.");
                    return;
                }

                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }

            // Controllers stay fully functional everywhere; hand tracking is used when the runtime provides it.
            Enable<OculusTouchControllerProfile>();
            Enable<MetaQuestTouchPlusControllerProfile>();
            Enable<HandInteractionProfile>();
            Enable<HandTracking>();
            if (quest)
            {
                Enable<MetaQuestFeature>();
                Enable<MetaQuestTouchProControllerProfile>();
                Enable<MetaHandTrackingAim>();
            }
            else
            {
                Enable<ValveIndexControllerProfile>();
                Enable<HTCViveControllerProfile>();
                Enable<KHRSimpleControllerProfile>();
            }

            EditorUtility.SetDirty(settings);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "BTP";
            PlayerSettings.productName = "Plant Disease VR";
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Meta Quest: Vulkan only, IL2CPP, ARM64, ASTC textures, Android 10+ (every Quest 2/3 runs newer).
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, ApplicationId);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC };
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // Windows PC VR: DX11 is the most widely supported OpenXR graphics API on PC runtimes.
            var standalone = NamedBuildTarget.Standalone;
            PlayerSettings.SetApplicationIdentifier(standalone, ApplicationId);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
        }

        /// <summary>
        /// Quest 2 baseline on the mobile URP asset: no HDR, 4x MSAA (cheap on tile GPUs), full
        /// render scale, one 20 m shadow cascade, no additional lights, no depth/opaque copies.
        /// </summary>
        static void ConfigureQuestRenderPipeline()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath("Assets/Settings/Mobile_RPAsset.asset");
            if (asset == null)
            {
                Debug.LogWarning("[BTP] Assets/Settings/Mobile_RPAsset.asset not found; URP left unchanged.");
                return;
            }

            var so = new SerializedObject(asset);
            void Set(string name, System.Action<SerializedProperty> apply)
            {
                var property = so.FindProperty(name);
                if (property != null)
                    apply(property);
                else
                    Debug.LogWarning($"[BTP] URP asset has no {name}.");
            }

            Set("m_SupportsHDR", p => p.boolValue = false);
            Set("m_MSAA", p => p.intValue = 4);
            Set("m_RenderScale", p => p.floatValue = 1f);
            Set("m_ShadowDistance", p => p.floatValue = 20f);
            Set("m_ShadowCascadeCount", p => p.intValue = 1);
            Set("m_MainLightShadowmapResolution", p => p.intValue = 1024);
            Set("m_SoftShadowsSupported", p => p.boolValue = false);
            Set("m_AdditionalLightsRenderingMode", p => p.intValue = 0);
            Set("m_RequireDepthTexture", p => p.boolValue = false);
            Set("m_RequireOpaqueTexture", p => p.boolValue = false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The XR Interaction Simulator spawns in Play mode in the Editor only, for testing without a headset.</summary>
        static void ConfigureSimulator()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefab);
            foreach (var guid in AssetDatabase.FindAssets("XRDeviceSimulatorSettings t:ScriptableObject"))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                var so = new SerializedObject(asset);
                var automatic = so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab");
                if (automatic == null)
                    continue;
                automatic.boolValue = prefab != null;
                so.FindProperty("m_AutomaticallyInstantiateInEditorOnly").boolValue = true;
                so.FindProperty("m_UseClassic").boolValue = false;
                so.FindProperty("m_SimulatorPrefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }

            Debug.LogWarning("[BTP] XRDeviceSimulatorSettings not found; the Editor simulator was not configured.");
        }
    }
}
