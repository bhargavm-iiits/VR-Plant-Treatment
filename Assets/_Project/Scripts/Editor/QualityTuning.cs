using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BTP.Editor
{
    /// <summary>
    /// Applies the plant-clarity quality settings from BuildAll so they survive a menu rebuild.
    /// Mobile quality level (Android: Quest 2 baseline, Quest 3 raised at run time by DeviceQualityProfile):
    /// LOD bias 1, per-texture anisotropy, 4x MSAA, 1 cascade of 1024 shadows over 20 m (unchanged).
    /// PC quality level (PC VR): LOD bias 3 (near meshes stay much longer), forced anisotropy, and 4x MSAA
    /// on the PC URP asset (it was off, which made alpha-clipped leaf edges shimmer).
    /// </summary>
    static class QualityTuning
    {
        const string PcPipelineAsset = "Assets/Settings/PC_RPAsset.asset";
        const float PcLodBias = 3f;

        public static void Apply()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[BTP] Could not open QualitySettings; set the PC quality level's LOD Bias to 3 by hand.");
            }
            else
            {
                var serialized = new SerializedObject(assets[0]);
                var levels = serialized.FindProperty("m_QualitySettings");
                for (var i = 0; i < levels.arraySize; i++)
                {
                    var level = levels.GetArrayElementAtIndex(i);
                    if (level.FindPropertyRelative("name").stringValue != "PC")
                        continue;
                    level.FindPropertyRelative("lodBias").floatValue = PcLodBias;
                    level.FindPropertyRelative("anisotropicTextures").intValue = 2; // Force Enable
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcPipelineAsset);
            if (pc != null && pc.msaaSampleCount < 4)
            {
                pc.msaaSampleCount = 4;
                EditorUtility.SetDirty(pc);
            }
        }
    }
}
