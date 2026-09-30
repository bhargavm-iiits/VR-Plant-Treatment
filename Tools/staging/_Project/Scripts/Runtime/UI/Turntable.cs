using UnityEngine;

namespace BTP.UI
{
    /// <summary>Slowly spins a preview model about its vertical axis.</summary>
    public sealed class Turntable : MonoBehaviour
    {
        [SerializeField] float degreesPerSecond = 20f;

        void Update() => transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }
}
