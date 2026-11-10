using UnityEngine;

namespace BTP.UI
{
    /// <summary>
    /// Gives a world-space canvas the XR camera as its event camera. The camera lives in
    /// XRBootstrap, so environment scenes cannot reference it in the Inspector.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class CanvasEventCamera : MonoBehaviour
    {
        Canvas canvas;

        void Awake() => canvas = GetComponent<Canvas>();

        void LateUpdate()
        {
            if (canvas.worldCamera != null && canvas.worldCamera.isActiveAndEnabled)
                return;
            canvas.worldCamera = Camera.main;
        }
    }
}
