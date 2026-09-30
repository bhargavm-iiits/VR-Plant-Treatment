using System.Collections;
using UnityEngine;

namespace BTP.Core
{
    /// <summary>
    /// Fades the view to a colour and back, using a camera-attached mesh drawn with the
    /// BTP/Fade Overlay shader. Starts opaque so the first environment fades in.
    /// </summary>
    public sealed class FadeOverlay : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] Renderer overlayRenderer;
        [SerializeField] Color color = Color.black;
        [SerializeField, Range(0f, 1f)] float startAlpha = 1f;

        MaterialPropertyBlock block;

        public float Alpha { get; private set; }

        internal void Bind(Renderer target) => overlayRenderer = target;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            SetAlpha(startAlpha);
        }

        public void SetAlpha(float alpha)
        {
            Alpha = Mathf.Clamp01(alpha);
            if (overlayRenderer == null)
                return;
            overlayRenderer.enabled = Alpha > 0.001f;
            block.SetColor(ColorId, new Color(color.r, color.g, color.b, Alpha));
            overlayRenderer.SetPropertyBlock(block);
        }

        public IEnumerator FadeTo(float targetAlpha, float duration)
        {
            var from = Alpha;
            for (var time = 0f; time < duration; time += Time.unscaledDeltaTime)
            {
                SetAlpha(Mathf.Lerp(from, targetAlpha, time / duration));
                yield return null;
            }

            SetAlpha(targetAlpha);
        }
    }
}
