using System;
using System.Collections.Generic;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// The guided treatment's in-view HUD (built at runtime, no assets; the door pop-up's look): one game-style card at a
    /// time in the middle of the view (step chip "Step 2/4", title, the exact target in large type, a one-line instruction
    /// and a large OK button that reacts on press-down), a small progress chip low in the view ("NPK 19-19-19 · 2/3
    /// scoops") and a transient toast above the chip for warnings (optionally with one action such as "Reset step"). Both
    /// follow the head lazily (they stay put until the head turns away, then swing back) and pop in with an ease-out-back.
    /// A step success plays a tick animation on the card before the next instruction. XR rays, hands and the desktop mouse
    /// ray press the buttons (TrackedDeviceGraphicRaycaster).
    /// </summary>
    public sealed class GuidedHud : MonoBehaviour
    {
        /// <summary>A card button: its label, what it does, and whether it is the primary (cyan) one.</summary>
        public readonly struct CardButton
        {
            public readonly string Label;
            public readonly Action<string> OnPress;
            public readonly bool Primary;

            public CardButton(string label, Action<string> onPress, bool primary = true)
            {
                Label = label;
                OnPress = onPress;
                Primary = primary;
            }
        }

        const float CardWidth = 560f, CardHeight = 318f, Pad = 22f, ButtonHeight = 74f;
        const float ChipWidth = 420f, ChipHeight = 54f, ToastWidth = 560f, ToastHeight = 64f;
        const float UnitsToMetres = 0.00085f, PopSeconds = 0.28f, TickSeconds = 1.1f;
        const float FollowAngle = 22f, TurnRate = 5f, MoveRate = 10f;
        public const int SortingOrder = 60;

        /// <summary>Card centre in the head's yaw/pitch frame (slightly below the eye line, 0.95 m ahead).</summary>
        public static readonly Vector3 CardOffset = new Vector3(0f, -0.03f, 0.8f);
        /// <summary>The chip low in the view, out of the way of the hands and the plant.</summary>
        public static readonly Vector3 ChipOffset = new Vector3(0f, -0.3f, 0.8f);

        static readonly Color CardColor = new Color(UIPalette.Panel.r, UIPalette.Panel.g, UIPalette.Panel.b, 0.94f);
        static readonly Color Dark = new Color(0.04f, 0.07f, 0.09f, 1f);

        sealed class Follower
        {
            public Transform Root;
            public Vector3 Offset;
            public Quaternion Anchor = Quaternion.identity;
            public Vector3 LastHead;
            public bool Following, Snap = true;
            public float MinPitch, MaxPitch;
        }

        Transform head;
        Follower cardFollow, chipFollow;
        CanvasGroup cardGroup, chipGroup, toastGroup;
        RectTransform cardWidget, chipWidget, toastWidget;
        TMP_Text stepText, titleText, targetText, bodyText, chipText, toastText;
        Image stepChip, accent, chipDot;
        readonly List<Button> buttons = new List<Button>();
        readonly List<TMP_Text> buttonLabels = new List<TMP_Text>();
        readonly List<Action<string>> handlers = new List<Action<string>>();
        Button toastButton;
        TMP_Text toastButtonLabel;
        Action<string> toastHandler;
        GameObject tickRoot;
        Image tickDisc;
        RectTransform tickMark;
        TMP_Text tickText;

        bool cardOn;
        float cardTime;
        float tickTime = -1f;
        Action afterTick;
        float toastUntil = -1f, toastTime;
        bool chipOn;
        float chipTime;

        /// <summary>True while the card (instruction, recap or completion) is up.</summary>
        public bool CardVisible => cardOn;
        public bool TickPlaying => tickTime >= 0f;
        public string CardTitle => titleText != null ? titleText.text : "";
        public string CardTarget => targetText != null ? targetText.text : "";
        public string CardBody => bodyText != null ? bodyText.text : "";
        public string CardStep => stepText != null ? stepText.text : "";
        public string ChipText => chipOn && chipText != null ? chipText.text : "";
        public string ToastText => toastGroup != null && toastGroup.gameObject.activeSelf && toastText != null ? toastText.text : "";
        public bool ToastHasAction => toastButton != null && toastButton.gameObject.activeSelf && toastGroup.gameObject.activeSelf;
        public IReadOnlyList<Button> CardButtons => buttons;

        public static GuidedHud Create(Transform parent)
        {
            var go = new GameObject("Guided HUD");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<GuidedHud>();
            hud.Build();
            return hud;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Shows the card (replacing whatever it showed) with up to two buttons.</summary>
        public void ShowCard(string step, string title, string target, string body, params CardButton[] actions)
        {
            tickTime = -1f;
            afterTick = null;
            tickRoot.SetActive(false);
            SetCardTexts(step, title, target, body);
            for (var i = 0; i < buttons.Count; i++)
            {
                var visible = actions != null && i < actions.Length && !string.IsNullOrEmpty(actions[i].Label);
                buttons[i].gameObject.SetActive(visible);
                handlers[i] = visible ? actions[i].OnPress : null;
                if (!visible)
                    continue;
                buttonLabels[i].text = actions[i].Label;
                var colors = buttons[i].colors;
                colors.normalColor = actions[i].Primary ? UIPalette.Cyan : UIPalette.ButtonNormal;
                colors.highlightedColor = actions[i].Primary ? Color.Lerp(UIPalette.Cyan, Color.white, 0.35f) : UIPalette.ButtonHover;
                colors.pressedColor = UIPalette.ButtonPressed;
                colors.selectedColor = colors.normalColor;
                buttons[i].colors = colors;
                buttonLabels[i].color = actions[i].Primary ? Dark : UIPalette.TextPrimary;
            }

            FitCardHeight();
            LayoutButtons(actions?.Length ?? 0);
            if (!cardOn)
            {
                cardOn = true;
                cardTime = 0f;
                cardFollow.Snap = true;
            }

            cardGroup.gameObject.SetActive(true);
            cardGroup.blocksRaycasts = true;
            cardGroup.interactable = true;
        }

        /// <summary>Hides the card at once (OK pressed).</summary>
        public void HideCard()
        {
            cardOn = false;
            tickTime = -1f;
            afterTick = null;
            cardGroup.blocksRaycasts = false;
            cardGroup.interactable = false;
            cardGroup.gameObject.SetActive(false);
        }

        /// <summary>The success tick on the card ("Temperature set"), then <paramref name="then"/> (usually the next instruction).</summary>
        public void PlaySuccess(string title, string line, Action then)
        {
            SetCardTexts("", title, "", line);
            for (var i = 0; i < buttons.Count; i++)
            {
                buttons[i].gameObject.SetActive(false);
                handlers[i] = null;
            }

            FitCardHeight();
            tickRoot.SetActive(true);
            tickText.text = title;
            tickTime = 0f;
            afterTick = then;
            if (!cardOn)
            {
                cardOn = true;
                cardTime = 0f;
                cardFollow.Snap = true;
            }

            cardGroup.gameObject.SetActive(true);
            cardGroup.blocksRaycasts = false;
        }

        /// <summary>The small progress chip ("NPK 19-19-19 · 2/3 scoops"); null hides it.</summary>
        public void SetChip(string text, Color dot)
        {
            var show = !string.IsNullOrEmpty(text);
            if (show)
            {
                // Measured while active (TMP loads its font when the object wakes), so the pill hugs the text.
                chipWidget.gameObject.SetActive(true);
                chipText.text = text;
                chipDot.color = dot;
                var width = Mathf.Clamp(chipText.GetPreferredValues(text, 4000f, ChipHeight).x + 84f, 200f, 760f);
                ((RectTransform)chipGroup.transform).sizeDelta = new Vector2(width, ChipHeight + ToastHeight + 16f);
                chipWidget.sizeDelta = new Vector2(width, ChipHeight);
            }

            if (show != chipOn)
            {
                chipOn = show;
                chipTime = 0f;
                chipWidget.gameObject.SetActive(show);
                if (show)
                    chipFollow.Snap = true;
            }
        }

        /// <summary>A transient warning above the chip; with <paramref name="actionLabel"/> it carries one button and stays longer.</summary>
        public void Toast(string text, Color tint, string actionLabel = null, Action<string> action = null, float seconds = 3.5f)
        {
            toastText.text = text;
            toastText.color = tint;
            var hasAction = !string.IsNullOrEmpty(actionLabel) && action != null;
            toastButton.gameObject.SetActive(hasAction);
            toastHandler = hasAction ? action : null;
            if (hasAction)
                toastButtonLabel.text = actionLabel;
            toastText.rectTransform.offsetMax = new Vector2(hasAction ? -170f : -18f, 0f);
            toastGroup.gameObject.SetActive(true);
            toastGroup.blocksRaycasts = hasAction;
            toastGroup.interactable = hasAction;
            toastTime = 0f;
            // seconds <= 0: stays until hidden or replaced (an action that must stay reachable, e.g. "Reset step").
            toastUntil = seconds <= 0f ? -1f : Time.unscaledTime + (hasAction ? Mathf.Max(seconds, 9f) : seconds);
            Debug.Log("[BTP] Guided toast: " + text);
            chipFollow.Snap |= !chipOn;
        }

        public void HideToast()
        {
            toastUntil = -1f;
            toastHandler = null;
            toastGroup.gameObject.SetActive(false);
        }

        /// <summary>Presses card button <paramref name="index"/> as <paramref name="source"/> (verification / tests).</summary>
        public bool Press(int index, string source = GuidedInputGate.Script)
        {
            if (!cardOn || index < 0 || index >= buttons.Count || !buttons[index].gameObject.activeSelf || handlers[index] == null)
                return false;
            handlers[index].Invoke(source);
            return true;
        }

        /// <summary>Presses the toast's action (verification).</summary>
        public bool PressToast(string source = GuidedInputGate.Script)
        {
            if (!ToastHasAction || toastHandler == null)
                return false;
            var handler = toastHandler;
            HideToast();
            handler(source);
            return true;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (head == null || !head.gameObject.activeInHierarchy)
            {
                var main = Camera.main;
                if (main == null)
                    return;
                head = main.transform;
                cardFollow.Snap = chipFollow.Snap = true;
            }

            var dt = Time.unscaledDeltaTime;
            if (cardOn)
            {
                cardTime += dt;
                var s = ChamberDoorPrompt.PopInScale(cardTime / PopSeconds);
                cardWidget.localScale = new Vector3(s, s, 1f);
                cardGroup.alpha = ChamberDoorPrompt.PopInAlpha(cardTime / PopSeconds);
                Follow(cardFollow, dt);
                AnimateTick(dt);
            }

            if (chipOn || toastGroup.gameObject.activeSelf)
            {
                chipTime += dt;
                var s = chipOn ? ChamberDoorPrompt.PopInScale(chipTime / PopSeconds) : 1f;
                chipWidget.localScale = new Vector3(s, s, 1f);
                if (toastGroup.gameObject.activeSelf)
                {
                    toastTime += dt;
                    var ts = ChamberDoorPrompt.PopInScale(toastTime / PopSeconds);
                    toastWidget.localScale = new Vector3(ts, ts, 1f);
                    if (toastUntil >= 0f && Time.unscaledTime > toastUntil)
                        HideToast();
                }

                Follow(chipFollow, dt);
            }
        }

        void AnimateTick(float dt)
        {
            if (tickTime < 0f)
                return;
            tickTime += dt;
            var t = tickTime / TickSeconds;
            var pop = ChamberDoorPrompt.PopInScale(Mathf.Clamp01(tickTime / 0.35f));
            tickDisc.rectTransform.localScale = new Vector3(pop, pop, 1f);
            // The tick draws itself in (its mask width grows) after the disc lands.
            var draw = Mathf.Clamp01((tickTime - 0.15f) / 0.3f);
            tickMark.localScale = new Vector3(draw, draw, 1f);
            var glow = UIPalette.HealthGood;
            glow.a = 1f - 0.4f * Mathf.Clamp01((t - 0.6f) / 0.4f);
            tickDisc.color = glow;
            if (t < 1f)
                return;
            tickTime = -1f;
            tickRoot.SetActive(false);
            var then = afterTick;
            afterTick = null;
            if (then != null)
                then();
            else
                HideCard();
        }

        void Follow(Follower f, float dt)
        {
            var view = ViewFrame(head, f.MinPitch, f.MaxPitch);
            // A teleport or an environment switch moves the head at once: the HUD comes along at once too.
            if ((head.position - f.LastHead).sqrMagnitude > 0.25f)
                f.Snap = true;
            f.LastHead = head.position;
            if (f.Snap)
            {
                f.Anchor = view;
                f.Following = false;
                f.Snap = false;
                Pose(f, 1f);
                return;
            }

            if (Quaternion.Angle(f.Anchor, view) > FollowAngle)
                f.Following = true;
            if (f.Following)
            {
                f.Anchor = Quaternion.Slerp(f.Anchor, view, 1f - Mathf.Exp(-TurnRate * dt));
                if (Quaternion.Angle(f.Anchor, view) < 1f)
                    f.Following = false;
            }

            Pose(f, 1f - Mathf.Exp(-MoveRate * dt));
        }

        static Quaternion ViewFrame(Transform head, float minPitch, float maxPitch)
        {
            var euler = head.rotation.eulerAngles;
            var pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), minPitch, maxPitch);
            return Quaternion.Euler(pitch, euler.y, 0f);
        }

        void Pose(Follower f, float blend)
        {
            var target = head.position + f.Anchor * f.Offset;
            var position = (f.Root.position - target).sqrMagnitude > 4f ? target : Vector3.Lerp(f.Root.position, target, blend);
            var facing = position - head.position;
            if (facing.sqrMagnitude < 1e-6f)
                facing = f.Anchor * Vector3.forward;
            f.Root.SetPositionAndRotation(position, Quaternion.LookRotation(facing, f.Anchor * Vector3.up));
        }

        /// <summary>Snaps the card and chip in front of the head now (after a teleport or scene load).</summary>
        public void Recentre()
        {
            if (cardFollow != null)
                cardFollow.Snap = true;
            if (chipFollow != null)
                chipFollow.Snap = true;
        }

        // ------------------------------------------------------------------ building

        void Build()
        {
            var card = NewCanvas("Card", new Vector2(CardWidth, CardHeight), out cardGroup);
            cardFollow = new Follower { Root = card, Offset = CardOffset, MinPitch = -25f, MaxPitch = 40f };
            cardWidget = Rect("Widget", card);
            Stretch(cardWidget);
            var background = AddImage(cardWidget, "Background", ChamberDoorPopup.KeycapSprite, CardColor);
            Stretch(background.rectTransform);
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 0.9f;
            accent = AddImage(cardWidget, "Accent", ChamberDoorPopup.PillSprite, UIPalette.Cyan);
            Place(accent.rectTransform, Pad, 10f, CardWidth - 2f * Pad, 6f);
            accent.type = Image.Type.Sliced;
            accent.pixelsPerUnitMultiplier = 10f;

            stepChip = AddImage(cardWidget, "StepChip", ChamberDoorPopup.PillSprite, UIPalette.Cyan);
            stepChip.type = Image.Type.Sliced;
            stepChip.pixelsPerUnitMultiplier = 1.5f;
            Place(stepChip.rectTransform, Pad, 28f, 118f, 40f);
            stepText = AddText(stepChip.rectTransform, "Step", 22f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(stepText.rectTransform);
            stepText.color = Dark;

            titleText = AddText(cardWidget, "Title", 28f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Place(titleText.rectTransform, Pad + 132f, 28f, CardWidth - 2f * Pad - 132f, 40f);
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 20f;
            titleText.fontSizeMax = 28f;

            targetText = AddText(cardWidget, "Target", 44f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Place(targetText.rectTransform, Pad, 76f, CardWidth - 2f * Pad, 58f);
            targetText.color = UIPalette.Cyan;
            targetText.enableAutoSizing = true;
            targetText.fontSizeMin = 26f;
            targetText.fontSizeMax = 44f;

            bodyText = AddText(cardWidget, "Body", 25f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            Place(bodyText.rectTransform, Pad, 140f, CardWidth - 2f * Pad, CardHeight - 140f - ButtonHeight - Pad - 8f);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.color = UIPalette.TextPrimary;
            bodyText.enableAutoSizing = true;
            bodyText.fontSizeMin = 18f;
            bodyText.fontSizeMax = 25f;
            bodyText.richText = true;

            for (var i = 0; i < 2; i++)
            {
                var image = AddImage(cardWidget, "Button" + (i + 1), ChamberDoorPopup.KeycapSprite, UIPalette.Cyan);
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1.2f;
                image.raycastTarget = true;
                var button = image.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var label = AddText(image.rectTransform, "Label", 32f, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                var index = i;
                handlers.Add(null);
                // Press-down: OK reacts the moment the trigger goes down (that press is then consumed).
                image.gameObject.AddComponent<HudPressHandler>().Pressed = source => handlers[index]?.Invoke(source);
                buttons.Add(button);
                buttonLabels.Add(label);
            }

            // Success tick (over the card's left side while it plays).
            tickRoot = Rect("Tick", cardWidget).gameObject;
            var tickRect = (RectTransform)tickRoot.transform;
            tickRect.anchorMin = tickRect.anchorMax = new Vector2(0.5f, 0f);
            tickRect.anchoredPosition = new Vector2(0f, 96f);
            tickRect.sizeDelta = new Vector2(120f, 120f);
            tickDisc = AddImage(tickRect, "Disc", ChamberDoorPopup.DiscSprite, UIPalette.HealthGood);
            Stretch(tickDisc.rectTransform);
            tickMark = Rect("Mark", tickDisc.rectTransform);
            Stretch(tickMark);
            Arm(tickMark, new Vector2(-0.2f, -0.06f), 0.34f, -45f);
            Arm(tickMark, new Vector2(0.12f, 0.06f), 0.56f, 48f);
            tickText = AddText(cardWidget, "TickTitle", 30f, FontStyles.Bold, TextAlignmentOptions.Center);
            tickText.gameObject.SetActive(false);
            tickRoot.SetActive(false);

            // Chip and toast (one canvas low in the view).
            var chip = NewCanvas("Chip", new Vector2(ChipWidth, ChipHeight + ToastHeight + 16f), out chipGroup);
            chipFollow = new Follower { Root = chip, Offset = ChipOffset, MinPitch = -25f, MaxPitch = 40f };
            chipGroup.blocksRaycasts = true;
            chipWidget = Rect("ChipWidget", chip);
            chipWidget.anchorMin = chipWidget.anchorMax = new Vector2(0.5f, 0f);
            chipWidget.pivot = new Vector2(0.5f, 0f);
            chipWidget.sizeDelta = new Vector2(ChipWidth, ChipHeight);
            var chipRim = AddImage(chipWidget, "Rim", ChamberDoorPopup.PillSprite, new Color(UIPalette.Cyan.r, UIPalette.Cyan.g, UIPalette.Cyan.b, 0.8f));
            Stretch(chipRim.rectTransform);
            chipRim.rectTransform.offsetMin = new Vector2(-3f, -3f);
            chipRim.rectTransform.offsetMax = new Vector2(3f, 3f);
            chipRim.type = Image.Type.Sliced;
            chipRim.pixelsPerUnitMultiplier = (64f * 0.5f - 1f) / (ChipHeight * 0.5f + 3f);
            var chipBack = AddImage(chipWidget, "Background", ChamberDoorPopup.PillSprite, new Color(0.05f, 0.08f, 0.1f, 0.97f));
            Stretch(chipBack.rectTransform);
            chipBack.type = Image.Type.Sliced;
            chipBack.pixelsPerUnitMultiplier = (64f * 0.5f - 1f) / (ChipHeight * 0.5f);
            chipDot = AddImage(chipWidget, "Dot", ChamberDoorPopup.DiscSprite, UIPalette.Cyan);
            chipDot.rectTransform.anchorMin = chipDot.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            chipDot.rectTransform.sizeDelta = new Vector2(20f, 20f);
            chipDot.rectTransform.anchoredPosition = new Vector2(30f, 0f);
            chipText = AddText(chipWidget, "Text", 26f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(chipText.rectTransform);
            chipText.rectTransform.offsetMin = new Vector2(50f, 0f);
            chipText.rectTransform.offsetMax = new Vector2(-18f, 0f);
            chipWidget.gameObject.SetActive(false);

            var toastRoot = Rect("Toast", chip);
            toastRoot.anchorMin = toastRoot.anchorMax = new Vector2(0.5f, 0f);
            toastRoot.pivot = new Vector2(0.5f, 0f);
            toastRoot.anchoredPosition = new Vector2(0f, ChipHeight + 12f);
            toastRoot.sizeDelta = new Vector2(ToastWidth, ToastHeight);
            toastGroup = toastRoot.gameObject.AddComponent<CanvasGroup>();
            toastWidget = Rect("Widget", toastRoot);
            Stretch(toastWidget);
            var toastBack = AddImage(toastWidget, "Background", ChamberDoorPopup.PillSprite, new Color(0.12f, 0.08f, 0.03f, 0.94f));
            Stretch(toastBack.rectTransform);
            toastBack.type = Image.Type.Sliced;
            toastBack.pixelsPerUnitMultiplier = (64f * 0.5f - 1f) / (ToastHeight * 0.5f);
            toastText = AddText(toastWidget, "Text", 24f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(toastText.rectTransform);
            toastText.rectTransform.offsetMin = new Vector2(26f, 0f);
            toastText.enableAutoSizing = true;
            toastText.fontSizeMin = 16f;
            toastText.fontSizeMax = 24f;
            toastText.textWrappingMode = TextWrappingModes.Normal;
            var actionImage = AddImage(toastWidget, "Action", ChamberDoorPopup.PillSprite, UIPalette.Warning);
            actionImage.type = Image.Type.Sliced;
            actionImage.pixelsPerUnitMultiplier = (64f * 0.5f - 1f) / 24f;
            actionImage.raycastTarget = true;
            actionImage.rectTransform.anchorMin = actionImage.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            actionImage.rectTransform.pivot = new Vector2(1f, 0.5f);
            actionImage.rectTransform.anchoredPosition = new Vector2(-10f, 0f);
            actionImage.rectTransform.sizeDelta = new Vector2(150f, 48f);
            toastButton = actionImage.gameObject.AddComponent<Button>();
            toastButton.targetGraphic = actionImage;
            toastButton.navigation = new Navigation { mode = Navigation.Mode.None };
            toastButtonLabel = AddText(actionImage.rectTransform, "Label", 22f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(toastButtonLabel.rectTransform);
            toastButtonLabel.color = Dark;
            actionImage.gameObject.AddComponent<HudPressHandler>().Pressed = source => PressToast(source);
            toastGroup.gameObject.SetActive(false);

            cardGroup.gameObject.SetActive(false);
            MakeOverlay(transform);
        }

        /// <summary>Render queue of the HUD: after the chamber glass and the room-dimming shells.</summary>
        public const int OverlayQueue = 3200;

        /// <summary>
        /// The HUD draws on top of the scene (no depth test, after every transparent), so chamber posts and glass never cut
        /// through a card: a HUD, not a sign in the room.
        /// </summary>
        static void MakeOverlay(Transform root)
        {
            var ui = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "GuidedHud (overlay)", renderQueue = OverlayQueue };
            ui.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                image.material = ui;
            var fonts = new Dictionary<Material, Material>();
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var shared = text.fontSharedMaterial;
                if (shared == null)
                    continue;
                if (!fonts.TryGetValue(shared, out var overlay))
                {
                    overlay = new Material(shared) { name = shared.name + " (overlay)", renderQueue = OverlayQueue };
                    overlay.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
                    fonts[shared] = overlay;
                }

                text.fontSharedMaterial = overlay;
            }
        }

        float cardHeight = CardHeight;

        /// <summary>The card grows to fit a long body (recaps) so the text never runs under the buttons.</summary>
        void FitCardHeight()
        {
            var bodyTop = targetText.gameObject.activeSelf ? 140f : 82f;
            var width = CardWidth - 2f * Pad;
            bodyText.enableAutoSizing = false;
            bodyText.fontSize = 24f;
            var needed = bodyText.GetPreferredValues(bodyText.text, width, 0f).y + 8f;
            cardHeight = Mathf.Clamp(bodyTop + needed + ButtonHeight + Pad + 10f, CardHeight, 600f);
            ((RectTransform)cardGroup.transform).sizeDelta = new Vector2(CardWidth, cardHeight);
            Place(bodyText.rectTransform, Pad, bodyTop, width, cardHeight - bodyTop - ButtonHeight - Pad - 6f);
            bodyText.enableAutoSizing = true;
        }

        void LayoutButtons(int count)
        {
            var shown = Mathf.Clamp(count, 1, buttons.Count);
            var gap = 14f;
            var width = shown == 1 ? 260f : (CardWidth - 2f * Pad - gap) / 2f;
            var total = shown * width + (shown - 1) * gap;
            var x = (CardWidth - total) * 0.5f;
            for (var i = 0; i < buttons.Count; i++)
            {
                if (!buttons[i].gameObject.activeSelf)
                    continue;
                Place((RectTransform)buttons[i].transform, x, cardHeight - Pad - ButtonHeight, width, ButtonHeight);
                x += width + gap;
            }
        }

        void SetCardTexts(string step, string title, string target, string body)
        {
            stepChip.gameObject.SetActive(!string.IsNullOrEmpty(step));
            stepText.text = step ?? "";
            titleText.text = title ?? "";
            var titleX = string.IsNullOrEmpty(step) ? Pad : Pad + 132f;
            Place(titleText.rectTransform, titleX, 28f, CardWidth - Pad - titleX, 40f);
            targetText.text = target ?? "";
            targetText.gameObject.SetActive(!string.IsNullOrEmpty(target));
            bodyText.text = body ?? "";
            var bodyTop = string.IsNullOrEmpty(target) ? 82f : 140f;
            Place(bodyText.rectTransform, Pad, bodyTop, CardWidth - 2f * Pad, CardHeight - bodyTop - ButtonHeight - Pad - 8f);
        }

        Transform NewCanvas(string name, Vector2 size, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster),
                typeof(CanvasEventCamera), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * UnitsToMetres;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            scaler.referencePixelsPerUnit = 100f;
            group = go.GetComponent<CanvasGroup>();
            return go.transform;
        }

        static void Arm(RectTransform parent, Vector2 centre, float length, float angle)
        {
            var bar = AddImage(parent, "Arm", null, Color.white);
            var rect = bar.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = centre * 120f;
            rect.sizeDelta = new Vector2(length * 120f, 15f);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

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

        static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
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

        static TextMeshProUGUI AddText(Transform parent, string name, float size, FontStyles style, TextAlignmentOptions alignment)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.rectTransform.SetParent(parent, false);
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.color = UIPalette.TextPrimary;
            return text;
        }

        /// <summary>Press-down handler for HUD buttons: reports which input pressed (for the trigger-consume rule).</summary>
        sealed class HudPressHandler : MonoBehaviour, IPointerDownHandler
        {
            public Action<string> Pressed;

            public void OnPointerDown(PointerEventData eventData)
            {
                var source = GuidedInputGate.Mouse;
                if (eventData is TrackedDeviceEventData tracked && tracked.interactor is IXRInteractor interactor)
                    source = GuidedInputGate.SourceOf(interactor);
                Pressed?.Invoke(source);
            }
        }
    }
}
