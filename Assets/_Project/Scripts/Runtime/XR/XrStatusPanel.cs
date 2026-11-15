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
    /// runtime, so no scene has to change; it lives under the XRBootstrap coordinator and is placed beside the user
    /// whenever an environment loads or the specimen is focused. XR ray and the desktop mouse ray both press it.
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

        const float Width = 760f;
        const float Height = 520f;
        const int MaxActions = 4;

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
            rect.localScale = Vector3.one * 0.001f;
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            var scaler = GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            scaler.referencePixelsPerUnit = 100f;

            var background = Node("Background", rect).gameObject.AddComponent<Image>();
            Stretch(background.rectTransform, 0f);
            background.color = UIPalette.Panel;

            modeBackground = Node("Mode", rect).gameObject.AddComponent<Image>();
            Place(modeBackground.rectTransform, 0f, 0f, Width, 56f);
            mode = Text("ModeText", modeBackground.rectTransform, 24f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(mode.rectTransform, 8f);

            title = Text("Title", rect, 34f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            Place(title.rectTransform, 24f, 66f, Width - 48f, 48f);
            title.enableAutoSizing = true;
            title.fontSizeMin = 22f;
            title.fontSizeMax = 34f;

            body = Text("Body", rect, 21f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            Place(body.rectTransform, 24f, 120f, Width - 48f, Height - 120f - 96f);
            body.overflowMode = TextOverflowModes.Truncate;
            body.color = UIPalette.TextSecondary;

            var buttonWidth = (Width - 24f * 2f - 12f * (MaxActions - 1)) / MaxActions;
            for (var i = 0; i < MaxActions; i++)
            {
                var image = Node($"Action{i + 1}", rect).gameObject.AddComponent<Image>();
                Place(image.rectTransform, 24f + i * (buttonWidth + 12f), Height - 84f, buttonWidth, 68f);
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
                var label = Text("Label", image.rectTransform, 22f, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(label.rectTransform, 6f);
                label.enableAutoSizing = true;
                label.fontSizeMin = 14f;
                label.fontSizeMax = 22f;
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
            for (var i = 0; i < buttons.Count; i++)
            {
                var visible = actions != null && i < actions.Count && !string.IsNullOrEmpty(actions[i].Label);
                buttons[i].gameObject.SetActive(visible);
                handlers[i] = visible ? actions[i].OnPress : null;
                if (!visible)
                    continue;
                buttons[i].interactable = actions[i].Enabled;
                buttons[i].GetComponentInChildren<TMP_Text>().text = actions[i].Label;
            }
        }

        /// <summary>Puts the panel 1.15 m from the head, 35 degrees to the left at chest height, facing the user.</summary>
        public void PlaceBeside(Transform head)
        {
            if (head == null)
                return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            forward = Quaternion.Euler(0f, -35f, 0f) * forward.normalized;
            var position = head.position + forward * 1.15f + Vector3.down * 0.3f;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up) * Quaternion.Euler(12f, 0f, 0f));
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
