using System;
using System.Collections.Generic;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.XR
{
    /// <summary>
    /// The connected-scan panel: mode badge (FIXTURE is always labelled), the case badge, what is saved for this scan
    /// and up to four context actions (Enter Lab, Enter Treatment, Return to Farm, Retry, confirm a switch). Built at
    /// runtime, so no scene has to change; it lives under the XRBootstrap coordinator. It is a compact card at the top
    /// of the user's view (see <see cref="PlaceAbove"/>): re-placed whenever an environment loads, it then follows the
    /// head lazily, swinging back only when the user turns more than <see cref="FollowAngle"/> away. XR ray and the
    /// desktop mouse ray both press it.
    /// </summary>
    public sealed class XrStatusPanel : MonoBehaviour
    {
        public readonly struct Action
        {
            public readonly string Label;
            public readonly System.Action OnPress;
            public readonly bool Enabled;

            public Action(string label, System.Action onPress, bool enabled = true)
            {
                Label = label;
                OnPress = onPress;
                Enabled = enabled;
            }
        }

        // Compact layout: 540 x 300 canvas units at 1 mm per unit = 0.54 m x 0.30 m (was 0.76 m x 0.52 m, ~41% of the area).
        // Text stays legible at ~1.06 m: body 21 units = 21 mm type, ~14.5 mm cap height.
        const float Width = 540f;
        const float Height = 300f;
        const float Pad = 16f;
        const float ModeHeight = 40f;
        const float ButtonHeight = 52f;
        const float ButtonGap = 8f;
        const float UnitsToMetres = 0.001f;
        const int MaxActions = 4;

        /// <summary>Panel centre in the head's yaw/pitch frame: 1.0 m ahead, 0.35 m above the eye (~19 degrees up).</summary>
        public static readonly Vector3 ViewOffset = new Vector3(0f, 0.35f, 1.0f);
        /// <summary>The head may turn this far (degrees) from where the panel is before it swings back in front.</summary>
        public const float FollowAngle = 35f;
        /// <summary>The head pitch used for placement is clamped to this range, so looking down at a plant leaves the panel up.</summary>
        public const float MinPitch = -30f, MaxPitch = 20f;
        const float SettleAngle = 1f;
        const float TurnRate = 4f;
        const float MoveRate = 10f;
        const float SnapDistance = 2f;
        /// <summary>Canvas sorting order: above the other world-space UI it may overlap (still depth-tested, no ZTest Always).</summary>
        public const int SortingOrder = 50;

        Transform head;
        Quaternion anchor = Quaternion.identity;
        bool following;
        bool snapNext = true;

        TMP_Text mode;
        Image modeBackground;
        TMP_Text title;
        TMP_Text body;
        readonly List<Button> buttons = new List<Button>();
        readonly List<System.Action> handlers = new List<System.Action>();

        public static XrStatusPanel Create(Transform parent)
        {
            var go = new GameObject("XR Scan Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster),
                typeof(CanvasEventCamera));
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<XrStatusPanel>();
            panel.Build();
            return panel;
        }

        void Build()
        {
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(Width, Height);
            rect.localScale = Vector3.one * UnitsToMetres;
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = SortingOrder;
            var scaler = GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            scaler.referencePixelsPerUnit = 100f;

            var background = Node("Background", rect).gameObject.AddComponent<Image>();
            Stretch(background.rectTransform, 0f);
            background.color = UIPalette.Panel;

            modeBackground = Node("Mode", rect).gameObject.AddComponent<Image>();
            Place(modeBackground.rectTransform, 0f, 0f, Width, ModeHeight);
            mode = Text("ModeText", modeBackground.rectTransform, 21f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(mode.rectTransform, 5f);
            // The long staging-preview badge shrinks to fit the bar instead of spilling out of it.
            mode.enableAutoSizing = true;
            mode.fontSizeMin = 16f;
            mode.fontSizeMax = 21f;

            var titleTop = ModeHeight + 8f;
            title = Text("Title", rect, 28f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            Place(title.rectTransform, Pad, titleTop, Width - Pad * 2f, 34f);
            title.enableAutoSizing = true;
            title.fontSizeMin = 22f;
            title.fontSizeMax = 28f;

            var bodyTop = titleTop + 38f;
            var buttonTop = Height - Pad * 0.75f - ButtonHeight;
            body = Text("Body", rect, 21f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            Place(body.rectTransform, Pad, bodyTop, Width - Pad * 2f, buttonTop - 6f - bodyTop);
            body.lineSpacing = -8f;
            body.overflowMode = TextOverflowModes.Ellipsis;
            body.color = UIPalette.TextSecondary;

            for (var i = 0; i < MaxActions; i++)
            {
                var image = Node($"Action{i + 1}", rect).gameObject.AddComponent<Image>();
                Place(image.rectTransform, Pad, buttonTop, ButtonWidth(MaxActions), ButtonHeight);
                var button = image.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var colors = button.colors;
                colors.normalColor = UIPalette.ButtonNormal;
                colors.highlightedColor = UIPalette.ButtonHover;
                colors.pressedColor = UIPalette.ButtonPressed;
                colors.selectedColor = UIPalette.ButtonNormal;
                colors.disabledColor = UIPalette.ButtonDisabled;
                button.colors = colors;
                var label = Text("Label", image.rectTransform, 21f, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(label.rectTransform, 4f);
                label.enableAutoSizing = true;
                label.fontSizeMin = 17f;
                label.fontSizeMax = 21f;
                var index = i;
                handlers.Add(null);
                button.onClick.AddListener(() => handlers[index]?.Invoke());
                buttons.Add(button);
            }
        }

        public void Show(string modeText, Color modeColor, string titleText, string bodyText, IReadOnlyList<Action> actions)
        {
            mode.text = modeText ?? "";
            modeBackground.color = modeColor;
            mode.color = modeColor.grayscale > 0.55f ? Color.black : Color.white;
            title.text = titleText ?? "";
            body.text = bodyText ?? "";
            var shown = 0;
            for (var i = 0; i < buttons.Count; i++)
                if (actions != null && i < actions.Count && !string.IsNullOrEmpty(actions[i].Label))
                    shown++;
            // The visible buttons share the full row, so two actions get wide, easy-to-hit buttons.
            var width = ButtonWidth(Mathf.Max(1, shown));
            var slot = 0;
            for (var i = 0; i < buttons.Count; i++)
            {
                var visible = actions != null && i < actions.Count && !string.IsNullOrEmpty(actions[i].Label);
                buttons[i].gameObject.SetActive(visible);
                handlers[i] = visible ? actions[i].OnPress : null;
                if (!visible)
                    continue;
                var rect = (RectTransform)buttons[i].transform;
                rect.anchoredPosition = new Vector2(Pad + slot * (width + ButtonGap), rect.anchoredPosition.y);
                rect.sizeDelta = new Vector2(width, ButtonHeight);
                slot++;
                buttons[i].interactable = actions[i].Enabled;
                buttons[i].GetComponentInChildren<TMP_Text>().text = actions[i].Label;
            }
        }

        static float ButtonWidth(int count) => (Width - Pad * 2f - ButtonGap * (count - 1)) / count;

        /// <summary>
        /// Puts the panel at the top of <paramref name="head"/>'s view: <see cref="ViewOffset"/> in the head's yaw and
        /// clamped pitch (no roll), tilted to face the eye. From then on it follows that head lazily (see LateUpdate).
        /// </summary>
        public void PlaceAbove(Transform head)
        {
            if (head == null)
                return;
            this.head = head;
            anchor = ViewFrame(head);
            following = false;
            snapNext = false;
            ApplyPose(1f);
        }

        void OnEnable() => snapNext = true;

        /// <summary>
        /// Lazy follow: the panel keeps its place in the view until the head turns more than <see cref="FollowAngle"/>
        /// away, then swings back in front and settles; it moves with the head's position (snapping on teleports).
        /// </summary>
        void LateUpdate()
        {
            if (head == null)
            {
                var main = Camera.main;
                if (main == null)
                    return;
                head = main.transform;
                snapNext = true;
            }

            var view = ViewFrame(head);
            if (snapNext)
            {
                anchor = view;
                following = false;
                snapNext = false;
                ApplyPose(1f);
                return;
            }

            var angle = Quaternion.Angle(anchor, view);
            if (angle > FollowAngle)
                following = true;
            if (following)
            {
                anchor = Quaternion.Slerp(anchor, view, 1f - Mathf.Exp(-TurnRate * Time.unscaledDeltaTime));
                if (Quaternion.Angle(anchor, view) < SettleAngle)
                    following = false;
            }

            ApplyPose(1f - Mathf.Exp(-MoveRate * Time.unscaledDeltaTime));
        }

        /// <summary>The head's yaw and clamped pitch, without roll: the frame <see cref="ViewOffset"/> is measured in.</summary>
        static Quaternion ViewFrame(Transform head)
        {
            var euler = head.rotation.eulerAngles;
            var pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), MinPitch, MaxPitch);
            return Quaternion.Euler(pitch, euler.y, 0f);
        }

        void ApplyPose(float moveBlend)
        {
            var target = head.position + anchor * ViewOffset;
            var position = (transform.position - target).sqrMagnitude > SnapDistance * SnapDistance
                ? target
                : Vector3.Lerp(transform.position, target, moveBlend);
            // Facing the eye: the canvas front looks back at the head, so it tilts down toward a viewer below it.
            var facing = position - head.position;
            if (facing.sqrMagnitude < 1e-6f)
                facing = anchor * Vector3.forward;
            var rotation = Quaternion.LookRotation(facing, anchor * Vector3.up);
            transform.SetPositionAndRotation(position, rotation);
        }

        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static TMP_Text Text(string name, Transform parent, float size, FontStyles style, TextAlignmentOptions alignment)
        {
            var label = Node(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = UIPalette.TextPrimary;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
