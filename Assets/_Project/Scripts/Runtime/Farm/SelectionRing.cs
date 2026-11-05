using UnityEngine;

namespace BTP.Farm
{
    /// <summary>Cyan ground ring around a farm specimen: faint when idle, brighter on hover, bright when selected.</summary>
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

        MaterialPropertyBlock block;

        internal void Bind(Renderer target) => ringRenderer = target;

        void Awake() => SetState(State.Idle);

        public void SetState(State state)
        {
            if (ringRenderer == null)
                return;
            block ??= new MaterialPropertyBlock();

            var alpha = state == State.Selected ? selectedAlpha : state == State.Hover ? hoverAlpha : idleAlpha;
            block.SetColor(BaseColorId, new Color(color.r, color.g, color.b, alpha));
            block.SetFloat(IntensityId, state == State.Selected ? 2.2f : 1.3f);
            ringRenderer.SetPropertyBlock(block);
        }
    }
}
