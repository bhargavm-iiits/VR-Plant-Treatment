using BTP.Treatment;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Lab
{
    /// <summary>
    /// The Lab leaf's grab feedback (owned by <see cref="LeafMouseRotation"/>, built at runtime, no assets): a thin cyan ring
    /// round the leaf and a pill label under it ("Press GRIP to pick up the leaf"), shown while a controller or hand points
    /// at the leaf on its stand (with <c>held</c> the ring turns green and the label says how to put it back). The canvas
    /// stands at the leaf's centre, a little towards the viewer, faces the viewer and fades in and out. It has no raycaster,
    /// so it never takes a controller ray or a UI press.
    /// </summary>
    public sealed class LeafGrabCue
    {
        public const string HoverText = "Press GRIP to pick up the leaf";
        public const string HeldText = "Turn your hand to see the back · GRIP again to put it back";
        public const string MouseText = "Drag with the mouse to turn the leaf";

        const float UnitsPerMetre = 1000f, LabelHeight = 64f, TowardsViewer = 0.06f, FadeRate = 12f;
        const int RingPixels = 256, RingStroke = 5;

        static Sprite ringSprite;

        readonly GameObject root;
        readonly RectTransform rootRect;
        readonly CanvasGroup group;
        readonly Image ring;
        readonly Image labelBack;
        readonly TextMeshProUGUI label;
        readonly float radius;
        string labelShown;

        /// <summary>True while the cue is on screen (also while it fades out).</summary>
        public bool Visible => root != null && root.activeSelf;
        public string Label => labelShown;
        public Color RingColor => ring != null ? ring.color : Color.clear;

        /// <param name="parent">The leaf model (the cue is destroyed with it).</param>
        /// <param name="ringRadius">Ring radius in metres (round the whole leaf).</param>
        public LeafGrabCue(Transform parent, float ringRadius)
        {
            radius = Mathf.Max(ringRadius, 0.05f);
            root = new GameObject("Leaf grab cue", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 15;
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;
            rootRect = (RectTransform)root.transform;
            var diameter = radius * 2f * UnitsPerMetre;
            rootRect.sizeDelta = new Vector2(diameter, diameter + LabelHeight * 2f);

            ring = NewImage("Ring", rootRect, RingSprite, UIPalette.Cyan);
            ring.rectTransform.sizeDelta = new Vector2(diameter, diameter);

            labelBack = NewImage("Label", rootRect, ChamberDoorPopup.PillSprite, new Color(UIPalette.Panel.r, UIPalette.Panel.g, UIPalette.Panel.b, 0.9f));
            labelBack.type = Image.Type.Sliced;
            labelBack.pixelsPerUnitMultiplier = 31f / (LabelHeight * 0.5f);
            labelBack.rectTransform.anchoredPosition = new Vector2(0f, -(diameter * 0.5f + LabelHeight * 0.75f));
            label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(labelBack.rectTransform, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(28f, 0f);
            label.rectTransform.offsetMax = new Vector2(-28f, 0f);
            label.fontSize = 34f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = UIPalette.TextPrimary;
            label.raycastTarget = false;
            root.SetActive(false);
        }

        public void Destroy()
        {
            if (root != null)
                Object.Destroy(root);
        }

        /// <summary>
        /// One frame: shows the cue while <paramref name="hovered"/> or <paramref name="held"/> (fades otherwise), round
        /// <paramref name="centre"/>, facing <paramref name="eye"/>.
        /// </summary>
        public void Tick(bool hovered, bool held, bool mouse, Vector3 centre, Vector3 eye, float deltaTime)
        {
            var show = hovered || held;
            if (show && !root.activeSelf)
                root.SetActive(true);
            if (!root.activeSelf)
                return;
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, FadeRate * deltaTime);
            if (!show && group.alpha <= 0f)
            {
                root.SetActive(false);
                return;
            }

            if (show)
            {
                SetLabel(held ? HeldText : mouse ? MouseText : HoverText);
                var tint = held ? UIPalette.HealthGood : UIPalette.Cyan;
                // Hover breathes gently; a held leaf shows a steady, brighter ring.
                tint.a = held ? 1f : 0.65f + 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));
                ring.color = tint;
            }

            var toEye = eye - centre;
            var position = toEye.sqrMagnitude > 1e-4f ? centre + toEye.normalized * Mathf.Min(TowardsViewer, toEye.magnitude * 0.5f) : centre;
            var away = position - eye;
            var rotation = away.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(away.normalized, Vector3.up) : rootRect.rotation;
            rootRect.SetPositionAndRotation(position, rotation);
            // One canvas unit is a millimetre whatever the leaf model's scale.
            var parentScale = rootRect.parent != null ? Mathf.Abs(rootRect.parent.lossyScale.x) : 1f;
            rootRect.localScale = Vector3.one * (1f / UnitsPerMetre / Mathf.Max(parentScale, 1e-4f));
        }

        void SetLabel(string text)
        {
            if (text == labelShown)
                return;
            labelShown = text;
            label.text = text;
            var width = Mathf.Ceil(label.GetPreferredValues(text).x) + 56f;
            labelBack.rectTransform.sizeDelta = new Vector2(width, LabelHeight);
        }

        static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A thin white anti-aliased ring, generated once.</summary>
        static Sprite RingSprite
        {
            get
            {
                if (ringSprite != null)
                    return ringSprite;
                var texture = new Texture2D(RingPixels, RingPixels, TextureFormat.RGBA32, false)
                {
                    name = "LeafGrabRing", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color32[RingPixels * RingPixels];
                const float half = RingPixels * 0.5f, outer = half - 1f;
                for (var y = 0; y < RingPixels; y++)
                for (var x = 0; x < RingPixels; x++)
                {
                    var d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
                    var alpha = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - (outer - RingStroke));
                    pixels[y * RingPixels + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                ringSprite = Sprite.Create(texture, new Rect(0f, 0f, RingPixels, RingPixels), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                ringSprite.name = "LeafGrabRing";
                ringSprite.hideFlags = HideFlags.DontSave;
                return ringSprite;
            }
        }
    }
}
