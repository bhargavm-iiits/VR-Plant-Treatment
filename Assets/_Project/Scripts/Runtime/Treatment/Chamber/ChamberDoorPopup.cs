using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// The chamber door's GTA-style interaction pop-up (owned by <see cref="ChamberDoor"/>; built on first use, no assets):
    /// a small world-space widget beside the door handle with a round button glyph on the left — a dark disc with a thin
    /// pulsing cyan rim and a bold white "A" (a rounded keycap with "O" in the desktop demo) — and a one-word action label
    /// ("Open" / "Close") on the right, both on a translucent dark pill. It faces the user (yaw plus a little pitch) and is
    /// scaled with the viewing distance so it reads about the same size from 0.5 to 1.3 m. It pops in with an ease-out-back
    /// (<see cref="ChamberDoorPrompt.PopInScale"/>) and shrinks out; a press squashes and brightens the badge and fills a
    /// thin ring round it (<see cref="Press"/>), then the pop-up hides while the leaf swings and pops back in with the new
    /// label. Circle, ring, keycap and pill sprites are generated once at runtime (anti-aliased). The canvas sorts after
    /// the chamber glass (it stands in front of it) but keeps depth testing, so walls still hide it.
    /// </summary>
    public sealed class ChamberDoorPopup
    {
        // Canvas units; one unit is ScalePerMetre metres when seen from 1 m (the canvas scales with the distance).
        const float Height = 96f, RingSize = 84f, BadgeSize = 64f, RimWidth = 4f, Gap = 14f, RightPad = 32f;
        const float LabelFontSize = 40f, GlyphFontSize = 40f, ShadowOffset = 2.5f;
        const float ScalePerMetre = 0.0009f, MinDistance = 0.4f, MaxDistance = 1.6f, MaxPitch = 20f, Follow = 8f;
        const int SortingOrder = 20;
        // Generated sprites (pixels): the pill and keycap are nine-sliced with these corner radii.
        const int DiscPixels = 128, RingStroke = 7, PillPixels = 64, KeyPixels = 64, KeyCorner = 20;

        static readonly Color BackgroundColor = new Color(UIPalette.Panel.r, UIPalette.Panel.g, UIPalette.Panel.b, 0.78f);
        static readonly Color BadgeColor = new Color(0.05f, 0.07f, 0.09f, 1f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.55f);

        static Sprite disc, ring, pill, keycap;

        readonly GameObject root;
        readonly RectTransform rootRect, widget, badge, labelRect, shadowRect;
        readonly CanvasGroup group;
        readonly Image rim, fill, progress;
        readonly TextMeshProUGUI glyph, label, shadow;

        bool on;
        float inTime, outTime = -1f, outScale = 1f, outAlpha = 1f, pressTime = -1f;
        string labelShown, glyphShown;
        bool keycapShown;

        /// <summary>True while the pop-up is on screen (also while it pops out).</summary>
        public bool Visible => root != null && root.activeSelf;
        /// <summary>The action label shown ("Open" / "Close"); null before the first show.</summary>
        public string Label => labelShown;
        /// <summary>The button glyph shown ("A" / "O"); null before the first show.</summary>
        public string Glyph => glyphShown;
        /// <summary>True during the press feedback.</summary>
        public bool Pressing => pressTime >= 0f;

        public ChamberDoorPopup(Transform parent)
        {
            root = new GameObject("DoorPopup", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // After the (transparent) chamber glass behind it; depth testing stays on.
            canvas.sortingOrder = SortingOrder;
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;
            rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(240f, Height);
            rootRect.localScale = Vector3.one * ScalePerMetre;

            widget = Rect("Widget", rootRect);
            Stretch(widget);
            var background = AddImage(widget, "Background", PillSprite, BackgroundColor);
            Stretch(background.rectTransform);
            background.type = Image.Type.Sliced;
            // Corners of half the pill's height: fully round ends.
            background.pixelsPerUnitMultiplier = (PillPixels * 0.5f - 1f) / (Height * 0.5f);

            // The badge sits concentric with the pill's round left end.
            badge = Rect("Badge", widget);
            badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
            badge.sizeDelta = new Vector2(RingSize, RingSize);
            badge.anchoredPosition = new Vector2(Height * 0.5f, 0f);
            progress = AddImage(badge, "Ring", RingSprite, UIPalette.Cyan);
            Stretch(progress.rectTransform);
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Radial360;
            progress.fillOrigin = (int)Image.Origin360.Top;
            progress.fillClockwise = true;
            progress.fillAmount = 0f;
            progress.enabled = false;
            rim = AddImage(badge, "Rim", DiscSprite, UIPalette.Cyan);
            rim.rectTransform.sizeDelta = new Vector2(BadgeSize, BadgeSize);
            fill = AddImage(badge, "Fill", DiscSprite, BadgeColor);
            fill.rectTransform.sizeDelta = new Vector2(BadgeSize - 2f * RimWidth, BadgeSize - 2f * RimWidth);
            glyph = AddText(fill.rectTransform, "Glyph", GlyphFontSize, TextAlignmentOptions.CenterGeoAligned);
            Stretch(glyph.rectTransform);
            glyph.color = UIPalette.TextPrimary;

            var labelX = Height * 0.5f + RingSize * 0.5f + Gap;
            shadow = AddText(widget, "LabelShadow", LabelFontSize, TextAlignmentOptions.MidlineLeft);
            shadow.color = ShadowColor;
            shadowRect = shadow.rectTransform;
            PlaceLabel(shadowRect, labelX + ShadowOffset, -ShadowOffset);
            label = AddText(widget, "Label", LabelFontSize, TextAlignmentOptions.MidlineLeft);
            label.color = UIPalette.TextPrimary;
            labelRect = label.rectTransform;
            PlaceLabel(labelRect, labelX, 0f);
            root.SetActive(false);
        }

        /// <summary>Destroys the widget (the generated sprites are shared and kept).</summary>
        public void Destroy()
        {
            if (root != null)
                Object.Destroy(root);
        }

        /// <summary>Plays the press feedback (squash, flash, ring fill) and keeps the pop-up up meanwhile; ignored while hidden.</summary>
        public void Press()
        {
            if (on)
                pressTime = 0f;
        }

        /// <summary>Hides at once (no pop-out).</summary>
        public void Hide()
        {
            on = false;
            outTime = -1f;
            pressTime = -1f;
            group.alpha = 0f;
            if (root.activeSelf)
                root.SetActive(false);
        }

        /// <summary>
        /// One frame: pops in when <paramref name="show"/> turns true (with the label for <paramref name="open"/>), pops out when
        /// it turns false (held up through a press), animates, and places the widget at <paramref name="anchor"/> facing
        /// <paramref name="eye"/> (<paramref name="fallbackForward"/> when the eye is right above it).
        /// </summary>
        public void Tick(bool show, bool open, bool desktopMode, Vector3 anchor, Vector3 eye, Vector3 fallbackForward, float deltaTime)
        {
            var press = -1f;
            if (pressTime >= 0f)
            {
                pressTime += deltaTime;
                if (pressTime >= ChamberDoorPrompt.PressSeconds)
                    pressTime = -1f;
                else
                    press = pressTime / ChamberDoorPrompt.PressSeconds;
            }

            show |= press >= 0f;
            var snap = false;
            if (show && !on)
            {
                on = true;
                snap = true;
                inTime = 0f;
                outTime = -1f;
                if (!root.activeSelf)
                    root.SetActive(true);
            }
            else if (!show && on)
            {
                on = false;
                outTime = 0f;
                outScale = widget.localScale.x;
                outAlpha = group.alpha;
            }

            if (!on && outTime < 0f)
                return;

            // The label changes only while it is up and at rest: a press keeps the old word until the pop-up returns.
            SetGlyph(desktopMode);
            if (on && press < 0f)
                SetLabel(open);

            float scale, alpha;
            if (on)
            {
                var t = inTime / ChamberDoorPrompt.PopInSeconds;
                inTime += deltaTime;
                scale = ChamberDoorPrompt.PopInScale(t);
                alpha = ChamberDoorPrompt.PopInAlpha(t);
            }
            else
            {
                outTime += deltaTime;
                var t = outTime / ChamberDoorPrompt.PopOutSeconds;
                if (t >= 1f)
                {
                    Hide();
                    return;
                }

                scale = outScale * ChamberDoorPrompt.PopOutScale(t);
                alpha = outAlpha * ChamberDoorPrompt.PopOutAlpha(t);
            }

            widget.localScale = new Vector3(scale, scale, 1f);
            group.alpha = alpha;
            AnimateBadge(press);
            Place(anchor, eye, fallbackForward, snap, deltaTime);
        }

        void AnimateBadge(float press)
        {
            // Idle: the rim breathes gently. Press: the badge squashes, flashes brighter and the ring fills once.
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            var rimColor = UIPalette.Cyan;
            rimColor.a = 0.72f + 0.28f * pulse;
            var fillColor = BadgeColor;
            var squash = 1f;
            if (press >= 0f)
            {
                var flash = ChamberDoorPrompt.PressFlash(press);
                rimColor = Color.Lerp(rimColor, Color.white, flash * 0.8f);
                fillColor = Color.Lerp(BadgeColor, UIPalette.ButtonPressed, flash);
                squash = ChamberDoorPrompt.PressScale(press);
            }

            rim.color = rimColor;
            fill.color = fillColor;
            var size = new Vector3(squash, squash, 1f);
            rim.rectTransform.localScale = size;
            fill.rectTransform.localScale = size;

            // The ring is for the round A badge only (it would cut the keycap's corners).
            var ringOn = press >= 0f && !keycapShown;
            if (progress.enabled != ringOn)
                progress.enabled = ringOn;
            if (!ringOn)
                return;
            progress.fillAmount = ChamberDoorPrompt.RingFill(press);
            var ringColor = UIPalette.Cyan;
            ringColor.a = press < 0.75f ? 1f : (1f - press) / 0.25f;
            progress.color = ringColor;
        }

        /// <summary>Follows the anchor (snaps on pop-in), turns to the eye (yaw plus a clamped pitch) and scales with the distance.</summary>
        void Place(Vector3 anchor, Vector3 eye, Vector3 fallbackForward, bool snap, float deltaTime)
        {
            var position = snap ? anchor : Vector3.Lerp(rootRect.position, anchor, 1f - Mathf.Exp(-Follow * deltaTime));
            var away = position - eye;
            var flat = new Vector3(away.x, 0f, away.z);
            Quaternion rotation;
            if (flat.sqrMagnitude > 1e-4f)
            {
                var pitch = Mathf.Clamp(Mathf.Atan2(away.y, flat.magnitude) * Mathf.Rad2Deg, -MaxPitch, MaxPitch);
                rotation = Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
            }
            else
            {
                fallbackForward.y = 0f;
                rotation = Quaternion.LookRotation(fallbackForward.sqrMagnitude > 1e-6f ? fallbackForward.normalized : Vector3.forward, Vector3.up);
            }

            rootRect.SetPositionAndRotation(position, rotation);
            // Constant apparent size: metres per canvas unit grow with the distance (undoing the parent's scale).
            var distance = Mathf.Clamp(away.magnitude, MinDistance, MaxDistance);
            var parentScale = rootRect.parent != null ? rootRect.parent.lossyScale.x : 1f;
            rootRect.localScale = Vector3.one * (ScalePerMetre * distance / (Mathf.Abs(parentScale) > 1e-4f ? parentScale : 1f));
        }

        void SetLabel(bool open)
        {
            var text = ChamberDoorPrompt.PromptLabel(open);
            if (text == labelShown)
                return;
            labelShown = text;
            label.text = text;
            shadow.text = text;
            // Measured while active (TMP loads its font when the object wakes); the pill hugs the word.
            var width = Mathf.Ceil(label.GetPreferredValues(text).x);
            labelRect.sizeDelta = new Vector2(width + 4f, Height);
            shadowRect.sizeDelta = labelRect.sizeDelta;
            rootRect.sizeDelta = new Vector2(Height * 0.5f + RingSize * 0.5f + Gap + width + RightPad, Height);
        }

        void SetGlyph(bool desktopMode)
        {
            var text = ChamberDoorPrompt.GlyphFor(desktopMode);
            if (text != glyphShown)
            {
                glyphShown = text;
                glyph.text = text;
            }

            if (keycapShown == desktopMode && rim.sprite != null)
                return;
            keycapShown = desktopMode;
            // A round controller button in VR; a rounded square keycap on the keyboard.
            SetBadgeShape(rim, BadgeSize);
            SetBadgeShape(fill, BadgeSize - 2f * RimWidth);
        }

        void SetBadgeShape(Image image, float size)
        {
            if (keycapShown)
            {
                image.sprite = KeycapSprite;
                image.type = Image.Type.Sliced;
                // Corner radius of about a fifth of the cap.
                image.pixelsPerUnitMultiplier = KeyCorner / (size * 0.22f);
            }
            else
            {
                image.sprite = DiscSprite;
                image.type = Image.Type.Simple;
                image.pixelsPerUnitMultiplier = 1f;
            }
        }

        // ------------------------------------------------------------------ building

        static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void PlaceLabel(RectTransform rect, float x, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(100f, Height);
        }

        static Image AddImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static TextMeshProUGUI AddText(Transform parent, string name, float size, TextAlignmentOptions alignment)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.rectTransform.SetParent(parent, false);
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        // ------------------------------------------------------------------ generated sprites

        /// <summary>A white anti-aliased disc (badge rim and fill).</summary>
        internal static Sprite DiscSprite => disc != null ? disc : (disc = Generate("DoorPopupDisc", DiscPixels, Vector4.zero, (x, y) =>
            Mathf.Clamp01(DiscPixels * 0.5f - Distance(x, y, DiscPixels))));

        /// <summary>A thin white anti-aliased ring (the press progress ring; radial fill).</summary>
        internal static Sprite RingSprite => ring != null ? ring : (ring = Generate("DoorPopupRing", DiscPixels, Vector4.zero, (x, y) =>
        {
            var d = Distance(x, y, DiscPixels);
            var outer = DiscPixels * 0.5f - 1f;
            return Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - (outer - RingStroke));
        }));

        /// <summary>A white anti-aliased disc nine-sliced into a pill (fully round ends).</summary>
        internal static Sprite PillSprite => pill != null ? pill : (pill = Generate("DoorPopupPill", PillPixels, Vector4.one * (PillPixels * 0.5f - 1f), (x, y) =>
            Mathf.Clamp01(PillPixels * 0.5f - Distance(x, y, PillPixels))));

        /// <summary>A white anti-aliased rounded square, nine-sliced at its corner radius (the desktop keycap).</summary>
        internal static Sprite KeycapSprite => keycap != null ? keycap : (keycap = Generate("DoorPopupKeycap", KeyPixels, Vector4.one * KeyCorner, (x, y) =>
        {
            // Signed distance to a rounded box filling the texture.
            const float half = KeyPixels * 0.5f, inner = half - KeyCorner;
            var qx = Mathf.Abs(x + 0.5f - half) - inner;
            var qy = Mathf.Abs(y + 0.5f - half) - inner;
            var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f);
            return Mathf.Clamp01(KeyCorner - outside);
        }));

        static float Distance(int x, int y, int size)
        {
            var half = size * 0.5f;
            return new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
        }

        static Sprite Generate(string name, int size, Vector4 border, System.Func<int, int, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(x, y) * 255f));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
