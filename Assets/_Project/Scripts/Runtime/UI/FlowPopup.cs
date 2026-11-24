using System;
using System.Collections.Generic;
using BTP.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.UI
{
    /// <summary>
    /// A world-space message pop-up (built at runtime, no assets; the controller menu's look): an accent bar, a title, a
    /// short message and one or two buttons. It opens 0.9 m in front of the viewer, a little below the eye line, facing
    /// them, and swings back into view when the head turns well away. XR rays, hands and the desktop mouse ray press it
    /// (TrackedDeviceGraphicRaycaster); a press pulses the controller that pointed at it, closes the pop-up and runs the
    /// button's action. One pop-up at a time: a new one replaces the last. It belongs to a scene and goes with it.
    /// </summary>
    public sealed class FlowPopup : MonoBehaviour
    {
        /// <summary>A button: its label, what it does (null only closes) and whether it is the primary (accent) one.</summary>
        public readonly struct Choice
        {
            public readonly string Label;
            public readonly Action OnPress;
            public readonly bool Primary;

            public Choice(string label, Action onPress, bool primary = true)
            {
                Label = label;
                OnPress = onPress;
                Primary = primary;
            }
        }

        const float Width = 640f, Pad = 28f, AccentHeight = 8f, TitleHeight = 46f, ButtonHeight = 78f, Gap = 16f;
        const float UnitsToMetres = 0.001f, Distance = 0.9f, DropDegrees = 10f, FollowAngle = 35f, TurnRate = 4f, PopSeconds = 0.25f;
        const float HapticAmplitude = 0.3f, HapticDuration = 0.06f;
        const int SortingOrder = 80;
        static readonly Color Dark = new Color(0.04f, 0.07f, 0.09f, 1f);

        static FlowPopup current;

        readonly List<Button> buttons = new List<Button>();
        readonly List<Choice> choices = new List<Choice>();
        Canvas canvas;
        CanvasGroup group;
        RectTransform rect;
        TextMeshProUGUI titleText, bodyText;
        Transform head;
        Quaternion anchor = Quaternion.identity;
        Vector3 lastHead;
        bool placed, following;
        float age;

        /// <summary>The pop-up on screen, or null.</summary>
        public static FlowPopup Current => current != null ? current : null;

        public string Title => titleText != null ? titleText.text : "";
        public string Body => bodyText != null ? bodyText.text : "";
        public int ChoiceCount => choices.Count;
        public string ChoiceLabel(int index) => index >= 0 && index < choices.Count ? choices[index].Label : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => current = null;

        /// <summary>Shows a pop-up in <paramref name="scene"/> (the active scene when invalid), replacing the one on screen.</summary>
        public static FlowPopup Show(Scene scene, string title, string body, Color accent, params Choice[] buttons)
        {
            if (current != null)
                current.Close();
            var go = new GameObject("Flow popup", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster),
                typeof(CanvasGroup));
            if (scene.IsValid() && scene.isLoaded && go.scene != scene)
                SceneManager.MoveGameObjectToScene(go, scene);
            var popup = go.AddComponent<FlowPopup>();
            popup.Build(title, body, accent, buttons);
            current = popup;
            popup.Place(0f);
            Debug.Log($"[BTP] Pop-up: {title} - {body}");
            return popup;
        }

        /// <summary>Presses button <paramref name="index"/> (tests, verification): closes the pop-up, then runs its action.</summary>
        public bool Press(int index)
        {
            if (index < 0 || index >= choices.Count)
                return false;
            var action = choices[index].OnPress;
            Debug.Log($"[BTP] Pop-up: '{choices[index].Label}' pressed.");
            Close();
            action?.Invoke();
            return true;
        }

        public void Close()
        {
            if (current == this)
                current = null;
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        void OnDestroy()
        {
            if (current == this)
                current = null;
        }

        void LateUpdate()
        {
            age += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(age / PopSeconds);
            var ease = 1f - (1f - t) * (1f - t);
            group.alpha = ease;
            rect.localScale = Vector3.one * (UnitsToMetres * Mathf.Lerp(0.85f, 1f, ease));
            Place(Time.unscaledDeltaTime);
        }

        /// <summary>In front of the head (snapped on the first frame or after a teleport), swinging back when the head turns away.</summary>
        void Place(float deltaTime)
        {
            if (head == null || !head.gameObject.activeInHierarchy)
            {
                var main = Camera.main;
                if (main == null)
                    return;
                head = main.transform;
                canvas.worldCamera = main;
                placed = false;
            }

            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var view = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : anchor;
            if (!placed || (head.position - lastHead).sqrMagnitude > 0.25f)
            {
                anchor = view;
                placed = true;
                following = false;
            }
            else
            {
                if (Quaternion.Angle(anchor, view) > FollowAngle)
                    following = true;
                if (following)
                {
                    anchor = Quaternion.Slerp(anchor, view, 1f - Mathf.Exp(-TurnRate * deltaTime));
                    following = Quaternion.Angle(anchor, view) > 1f;
                }
            }

            lastHead = head.position;
            var position = head.position + anchor * (Quaternion.Euler(DropDegrees, 0f, 0f) * Vector3.forward) * Distance;
            rect.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
        }

        // ------------------------------------------------------------------ building

        void Build(string title, string body, Color accent, Choice[] buttonChoices)
        {
            rect = (RectTransform)transform;
            canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = SortingOrder;
            canvas.worldCamera = Camera.main;
            GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            rect.localScale = Vector3.one * UnitsToMetres;

            var background = gameObject.AddComponent<Image>();
            background.color = UIPalette.Panel;
            var bar = NewImage("Accent", rect, accent);
            Place(bar.rectTransform, 0f, AccentHeight);

            var y = AccentHeight + Pad * 0.75f;
            titleText = NewText("Title", rect, title, 36f, FontStyles.Bold, accent == UIPalette.Warning ? UIPalette.Warning : UIPalette.TextPrimary);
            Place(titleText.rectTransform, y, TitleHeight, Pad);
            y += TitleHeight + Gap * 0.5f;

            bodyText = NewText("Body", rect, body, 28f, FontStyles.Normal, UIPalette.TextSecondary);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.richText = true;
            var bodyHeight = Mathf.Ceil(bodyText.GetPreferredValues(body ?? "", Width - 2f * Pad, 0f).y) + 4f;
            Place(bodyText.rectTransform, y, bodyHeight, Pad);
            y += bodyHeight + Gap;

            var list = buttonChoices != null && buttonChoices.Length > 0 ? buttonChoices : new[] { new Choice("OK", null) };
            var count = Mathf.Min(list.Length, 2);
            var buttonWidth = (Width - 2f * Pad - (count - 1) * Gap) / count;
            for (var i = 0; i < count; i++)
            {
                var index = i;
                choices.Add(list[i]);
                var button = NewButton(list[i], accent, () => Press(index));
                var buttonRect = (RectTransform)button.transform;
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0f, 1f);
                buttonRect.pivot = new Vector2(0f, 1f);
                buttonRect.anchoredPosition = new Vector2(Pad + i * (buttonWidth + Gap), -y);
                buttonRect.sizeDelta = new Vector2(buttonWidth, ButtonHeight);
                buttons.Add(button);
            }

            rect.sizeDelta = new Vector2(Width, y + ButtonHeight + Pad);
        }

        Button NewButton(Choice choice, Color accent, Action press)
        {
            var image = NewImage(choice.Label, rect, Color.white);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = choice.Primary ? accent : UIPalette.ButtonNormal;
            colors.highlightedColor = choice.Primary ? Color.Lerp(accent, Color.white, 0.35f) : UIPalette.ButtonHover;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = UIPalette.ButtonPressed;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                VrControllerRig.PulseControllersPointingAt(canvas, HapticAmplitude, HapticDuration);
                press();
            });
            var label = NewText("Label", image.rectTransform, choice.Label, 30f, FontStyles.Bold, choice.Primary ? Dark : UIPalette.TextPrimary);
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(10f, 4f);
            label.rectTransform.offsetMax = new Vector2(-10f, -4f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 20f;
            label.fontSizeMax = 30f;
            return button;
        }

        static void Place(RectTransform target, float top, float height, float inset = 0f)
        {
            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(0.5f, 1f);
            target.offsetMin = new Vector2(inset, -top - height);
            target.offsetMax = new Vector2(-inset, -top);
        }

        static Image NewImage(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, FontStyles style, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(parent, false);
            label.text = text ?? "";
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
            return label;
        }
    }
}
