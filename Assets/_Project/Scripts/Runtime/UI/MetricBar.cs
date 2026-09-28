using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.UI
{
    /// <summary>A labelled percentage bar. Higher-is-better metrics shade red to green, others green to red.</summary>
    public sealed class MetricBar : MonoBehaviour
    {
        [SerializeField] RectTransform fill;
        [SerializeField] Image fillImage;
        [SerializeField] TMP_Text valueLabel;
        [SerializeField] bool higherIsBetter = true;

        internal void Bind(RectTransform fillRect, Image image, TMP_Text value, bool higherIsGood)
        {
            fill = fillRect;
            fillImage = image;
            valueLabel = value;
            higherIsBetter = higherIsGood;
        }

        /// <summary>Shows a value from 0 to 100.</summary>
        public void SetValue(int percent)
        {
            percent = Mathf.Clamp(percent, 0, 100);
            var t = percent / 100f;
            if (fill != null)
                fill.anchorMax = new Vector2(t, 1f);
            if (fillImage != null)
            {
                var goodness = higherIsBetter ? t : 1f - t;
                fillImage.color = Color.Lerp(UIPalette.HealthBad, UIPalette.HealthGood, goodness);
            }

            if (valueLabel != null)
                valueLabel.text = $"{percent}%";
        }
    }
}
