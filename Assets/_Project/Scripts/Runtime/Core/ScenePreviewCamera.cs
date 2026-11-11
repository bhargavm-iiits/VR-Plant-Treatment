using UnityEngine;

namespace BTP.Core
{
    /// <summary>
    /// Shows an environment scene (Farm or Lab) in the Game view while editing: the XR camera
    /// lives in XRBootstrap, so these scenes have no camera of their own. At runtime the XR
    /// rig's camera takes over and this preview camera switches itself off, so there is never
    /// a second camera.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ScenePreviewCamera : MonoBehaviour
    {
        void Awake()
        {
            if (Application.isPlaying)
                gameObject.SetActive(false);
        }
    }
}
