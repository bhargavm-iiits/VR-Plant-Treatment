using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BTP.Core
{
    /// <summary>
    /// Quest 2 is the performance baseline (the mobile URP asset). Quest 3 and Quest Pro have
    /// headroom for a sharper image, so they render at a higher resolution scale. PC VR uses
    /// the PC quality level and is left unchanged.
    /// </summary>
    public sealed class DeviceQualityProfile : MonoBehaviour
    {
        [SerializeField, Range(1f, 1.5f)] float highEndQuestRenderScale = 1.2f;

        void Awake()
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android)
                return;

            var device = $"{SystemInfo.deviceModel} {SystemInfo.graphicsDeviceName}";
            var highEnd = device.Contains("Quest 3") || device.Contains("Quest Pro") || device.Contains("740");
            if (highEnd && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = highEndQuestRenderScale;
                // Quest 3 / Pro headroom: near plant meshes stay longer and sharper texture filtering.
                // Quest 2 keeps the mobile quality level as authored (LOD bias 1, per-texture anisotropy, 20 m shadows).
                QualitySettings.lodBias = 2f;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                Debug.Log($"[BTP] {device}: render scale {highEndQuestRenderScale}.");
            }
        }
    }
}
