using UnityEngine;

namespace BTP.Farm
{
    /// <summary>
    /// Cyan ground ring around a farm plant: faint (or hidden, for a disease row) when idle, brighter on hover,
    /// bright when selected.
    /// </summary>
    public sealed class SelectionRing : MonoBehaviour
    {
        public enum State
        {
            Idle,
            Hover,
            Selected
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] Renderer ringRenderer;
        [SerializeField] Color color = new Color(0.13f, 0.83f, 0.93f, 1f);
        [SerializeField, Range(0f, 1f)] float idleAlpha = 0.18f;
        [SerializeField, Range(0f, 1f)] float hoverAlpha = 0.5f;
        [SerializeField, Range(0f, 1f)] float selectedAlpha = 0.95f;
        [Tooltip("Hide the ring while idle (a disease row's one ring, which moves to the pointed-at plant).")]
        [SerializeField] bool hideWhenIdle;

        MaterialPropertyBlock block;

        internal void Bind(Renderer target, bool hideIdle = false)
        {
            ringRenderer = target;
            hideWhenIdle = hideIdle;
        }

        void Awake() => SetState(State.Idle);

        public void SetState(State state)
        {
            if (ringRenderer == null)
                return;
            ringRenderer.enabled = !(hideWhenIdle && state == State.Idle);
            block ??= new MaterialPropertyBlock();

            var alpha = state == State.Selected ? selectedAlpha : state == State.Hover ? hoverAlpha : idleAlpha;
            block.SetColor(BaseColorId, new Color(color.r, color.g, color.b, alpha));
            block.SetFloat(IntensityId, state == State.Selected ? 2.2f : 1.3f);
            ringRenderer.SetPropertyBlock(block);
        }
    }
}
