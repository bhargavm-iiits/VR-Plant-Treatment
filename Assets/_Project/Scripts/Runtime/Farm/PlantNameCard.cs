using System;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Farm
{
    /// <summary>
    /// Small world-space card above a selected specimen: plant name and what to do next. A planting can add one action
    /// button under the card at runtime (the Farm's optional "Treat Plant", off by default), so the Farm scene needs no
    /// rebuild for it.
    /// </summary>
    public sealed class PlantNameCard : MonoBehaviour
    {
        public const float ActionWidth = 420f, ActionHeight = 96f, ActionGap = 14f;

        [SerializeField] GameObject card;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;

        Button action;
        Action onAction;

        /// <summary>The runtime action button under the card, or null before <see cref="SetAction"/>.</summary>
        public Button ActionButton => action;

        internal void Bind(GameObject cardRoot, TMP_Text titleText, TMP_Text subtitleText)
        {
            card = cardRoot;
            title = titleText;
            subtitle = subtitleText;
        }

        public void Show(string plantName, string detail)
        {
            title.text = plantName;
            subtitle.text = detail;
            card.SetActive(true);
        }

        public void Hide() => card.SetActive(false);

        /// <summary>
        /// Adds (once) a cyan action button just below the card, shown and hidden with it, that calls
        /// <paramref name="pressed"/>. Built from the card's own sprite and font, on the card's XR-interactive canvas.
        /// </summary>
        public Button SetAction(string label, Action pressed)
        {
            onAction = pressed;
            if (action == null && card != null)
                action = CreateAction(label);
            else if (action != null)
                action.GetComponentInChildren<TMP_Text>().text = label;
            return action;
        }

        Button CreateAction(string label)
        {
            var parent = (RectTransform)card.transform;
            var background = card.GetComponentInChildren<Image>(true);
            var go = new GameObject("TreatPlantButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -ActionGap);
            rect.sizeDelta = new Vector2(ActionWidth, ActionHeight);
            var image = go.GetComponent<Image>();
            if (background != null)
            {
                image.sprite = background.sprite;
                image.type = background.type;
                image.pixelsPerUnitMultiplier = background.pixelsPerUnitMultiplier;
            }

            image.color = Color.white;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = UIPalette.Cyan;
            colors.highlightedColor = new Color(0.45f, 0.93f, 1f, 1f);
            colors.pressedColor = UIPalette.ButtonPressed;
            colors.selectedColor = UIPalette.Cyan;
            colors.disabledColor = UIPalette.ButtonDisabled;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.onClick.AddListener(() => onAction?.Invoke());

            var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.rectTransform.SetParent(rect, false);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(10f, 6f);
            text.rectTransform.offsetMax = new Vector2(-10f, -6f);
            if (title != null)
                text.font = title.font;
            text.text = label;
            text.fontSize = 40f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.02f, 0.11f, 0.14f);
            text.raycastTarget = false;
            return button;
        }
    }
}
