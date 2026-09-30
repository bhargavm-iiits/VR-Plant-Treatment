using UnityEngine;

namespace BTP.UI
{
    /// <summary>Turns a label about the vertical axis so it always faces the viewer.</summary>
    public sealed class FaceCamera : MonoBehaviour
    {
        Camera viewer;

        void LateUpdate()
        {
            if (viewer == null || !viewer.isActiveAndEnabled)
                viewer = Camera.main;
            if (viewer == null)
                return;

            var away = transform.position - viewer.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
