using UnityEngine;

namespace BTP.Core
{
    /// <summary>
    /// Where the user stands when an environment loads or when they recenter: the XR rig is
    /// moved so the head is above this point, facing along its forward direction.
    /// </summary>
    public sealed class SpawnAnchor : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.13f, 0.83f, 0.93f);
            var position = transform.position;
            var eye = position + Vector3.up * 1.6f;
            Gizmos.DrawWireSphere(position, 0.3f);
            Gizmos.DrawLine(position, eye);
            Gizmos.DrawLine(eye, eye + transform.forward * 0.6f);
        }
    }
}
