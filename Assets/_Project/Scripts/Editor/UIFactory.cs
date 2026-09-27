using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Editor
{
    /// <summary>
    /// Builds world-space uGUI in the lab reference style: charcoal translucent rounded panels,
    /// cyan outlines, white TextMeshPro text. Canvases use 1 unit = 1 mm, so font sizes are
    /// millimetres (30 mm body text is about 1.4 degrees tall at 1.25 m).
    /// </summary>
    static class UIFactory
    {
        public const float MillimetreScale = 0.001f;

        // Corner radius in canvas units: sprite border (24 px) / multiplier.
        const float CornerMultiplier = 1.4f;

        public static Canvas WorldCanvas(string name, Transform parent, Vector2 sizeMm, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasEventCamera));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = sizeMm;
            rect.localPosition = position;
            rect.localRotation = rotation;
            rect.localScale = Vector3.one * MillimetreScale;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            scaler.referencePixelsPerUnit = 100f;
            XRSceneWiring.MakeCanvasInteractive(canvas);
            return canvas;
        }

        /// <summary>Rotation for a canvas at <paramref name="position"/> so it faces a viewer at <paramref name="viewer"/>.</summary>
        public static Quaternion Facing(Vector3 position, Vector3 viewer, float tiltDegrees = 0f)
        {
            var away = position - viewer;
            away.y = 0f;
            return Quaternion.LookRotation(away.normalized, Vector3.up) * Quaternion.Euler(tiltDegrees, 0f, 0f);
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Positions a child by its top-left corner, in millimetres from the parent's top-left.</summary>
        public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        /// <summary>Rounded charcoal panel with a thin cyan border, filling <paramref name="parent"/>.</summary>
        public static Image Panel(RectTransform parent, Color? color = null, bool border = true)
        {
            var background = RoundedImage("Background", parent, color ?? UIPalette.Panel, ProceduralTextures.RoundedFill);
            Stretch(background.rectTransform);
            background.transform.SetAsFirstSibling();
            if (border)
            {
                var outline = RoundedImage("Border", parent, UIPalette.PanelBorder, ProceduralTextures.RoundedOutline);
                Stretch(outline.rectTransform);
                outline.raycastTarget = false;
                outline.transform.SetSiblingIndex(1);
            }

            return background;
        }

        public static Image RoundedImage(string name, Transform parent, Color color, Sprite sprite)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = CornerMultiplier;
            image.color = color;
            return image;
        }

        public static Image Rectangle(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = style;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// Rounded button: colour transition gives hover and press feedback; the returned
        /// SelectedStateVisual adds a persistent cyan outline for the selected state.
        /// </summary>
        public static Button Button(string name, Transform parent, string text, float fontSize, out SelectedStateVisual selected,
            Color? normal = null, Color? textColor = null)
        {
            var image = RoundedImage(name, parent, Color.white, ProceduralTextures.RoundedFill);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = normal ?? UIPalette.ButtonNormal;
            colors.highlightedColor = UIPalette.ButtonHover;
            colors.pressedColor = UIPalette.ButtonPressed;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = UIPalette.ButtonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;

            var outline = RoundedImage("SelectedOutline", image.transform, UIPalette.Cyan, ProceduralTextures.RoundedOutline);
            Stretch(outline.rectTransform, -2f);
            outline.raycastTarget = false;
            outline.enabled = false;

            var label = Text("Label", image.transform, text, fontSize, textColor ?? UIPalette.TextPrimary, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 8f);

            selected = image.gameObject.AddComponent<SelectedStateVisual>();
            selected.Bind(outline, normal.HasValue ? null : button);
            return button;
        }

        /// <summary>A labelled percentage bar row: name on the left, track and fill, value on the right.</summary>
        public static MetricBar Metric(string name, Transform parent, string label, bool higherIsBetter, float x, float y, float width)
        {
            var row = Place(Node(name, parent), x, y, width, 56f);
            Place(Text("Name", row, label, 28f, UIPalette.TextPrimary, TextAlignmentOptions.MidlineLeft).rectTransform, 0f, 0f, width * 0.38f, 56f);

            var track = RoundedImage("Track", row, UIPalette.TrackBackground, ProceduralTextures.RoundedFill);
            track.raycastTarget = false;
            track.pixelsPerUnitMultiplier = 4f;
            Place(track.rectTransform, width * 0.4f, 18f, width * 0.44f, 20f);

            var fill = RoundedImage("Fill", track.transform, UIPalette.HealthGood, ProceduralTextures.RoundedFill);
            fill.raycastTarget = false;
            fill.pixelsPerUnitMultiplier = 4f;
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0.5f, 1f);
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;

            var value = Text("Value", row, "0%", 30f, UIPalette.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            Place(value.rectTransform, width * 0.84f, 0f, width * 0.16f, 56f);

            var bar = row.gameObject.AddComponent<MetricBar>();
            bar.Bind(fillRect, fill, value, higherIsBetter);
            return bar;
        }
    }
}
