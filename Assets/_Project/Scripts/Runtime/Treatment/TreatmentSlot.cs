using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>A named placement point in a treatment environment prefab (bench, controls, disposal, ...).</summary>
    public sealed class TreatmentSlot : MonoBehaviour
    {
        [SerializeField] string slotName;
        [Tooltip("Spacing between several targets placed in this slot, along its local X axis.")]
        [SerializeField, Min(0f)] float spacing = 0.25f;

        public string SlotName => slotName;
        public float Spacing => spacing;

        internal void Configure(string name, float space)
        {
            slotName = name;
            spacing = space;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.13f, 0.83f, 0.93f, 0.8f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.08f);
        }
    }
}