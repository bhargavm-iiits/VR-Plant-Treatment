using System;
using System.Collections.Generic;
using System.Reflection;
using BTP.Farm;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.Core
{
    /// <summary>
    /// Controller support that is shared by every environment, installed on the XR rig (it installs itself at runtime when the
    /// rig does not have it yet, so no scene rebuild is needed). It
    /// 1. makes the controller ray interactors select with the trigger OR the grip and registers them, so hand-held tools can
    ///    require the grip to hold (<see cref="ControllerGripSelectFilter"/>) and keep the trigger for using them;
    /// 2. opens a world-space controller menu with the left Y button (environments, recenter, smooth walking, legend);
    /// 3. handles the right B button as Back: closes the menu, else the Farm disease ring.
    /// The thumbsticks and the menu button belong to the rig (teleport, snap turn, recenter) and are not bound here; the right
    /// A opens and closes the treatment chamber's door while the user stands at it (BTP.Treatment.ChamberDoor).
    /// </summary>
    public sealed class VrControllerRig : MonoBehaviour
    {
        const float MenuDistance = 0.7f, MenuScale = 0.0012f, HapticAmplitude = 0.25f, HapticDuration = 0.06f;
        const float MenuWidth = 560f, MenuHeight = 800f, ButtonHeight = 62f, ButtonGap = 10f, Margin = 24f;
        const string ManagerTypeName = "ControllerInputActionManager", SmoothProperty = "smoothMotionEnabled";

        static readonly HashSet<IXRInteractor> Controllers = new HashSet<IXRInteractor>();
        static bool subscribed;

        InputAction menuAction;
        InputAction backAction;
        bool menuRequested;
        bool backRequested;
        readonly List<NearFarInteractor> interactors = new List<NearFarInteractor>();
        Camera head;

        Canvas menuCanvas;
        Button farmButton, labButton, treatmentButton, walkButton;
        TMP_Text walkLabel;
        EnvironmentId? shownEnvironment;
        List<Component> managers = new List<Component>();

        /// <summary>True while the controller menu is showing.</summary>
        public bool IsMenuOpen => menuCanvas != null && menuCanvas.gameObject.activeSelf;

        /// <summary>True for a controller ray interactor registered by the rig (never for hands, the mouse ray or tests).</summary>
        public static bool IsControllerInteractor(IXRInteractor interactor) => interactor != null && Controllers.Contains(interactor);

        /// <summary>True when the interactor is a registered controller whose activate (trigger) input is held right now.</summary>
        internal static bool IsActivateHeldOnController(IXRInteractor interactor) =>
            IsControllerInteractor(interactor) && interactor is XRBaseInputInteractor input && input.activateInput.ReadIsPerformed();

        internal static void RegisterControllerInteractor(IXRInteractor interactor)
        {
            if (interactor != null)
                Controllers.Add(interactor);
        }

        internal static void UnregisterControllerInteractor(IXRInteractor interactor) => Controllers.Remove(interactor);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Controllers.Clear();
            subscribed = false;
        }

        // ------------------------------------------------------------------ self installation

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!subscribed)
            {
                subscribed = true;
                SceneManager.sceneLoaded += (_, _) => TryInstall();
            }

            TryInstall();
        }

        static void TryInstall()
        {
            var existing = FindFirstObjectByType<VrControllerRig>();
            if (existing != null)
            {
                existing.RefreshInteractors();
                return;
            }

            GameObject rig = null;
            var desktop = FindFirstObjectByType<DesktopDemoControls>();
            if (desktop != null)
                rig = desktop.gameObject;
            else
            {
                var interactor = FindFirstObjectByType<NearFarInteractor>();
                if (interactor != null)
                    rig = interactor.transform.root.gameObject;
            }

            if (rig != null)
                rig.AddComponent<VrControllerRig>();
        }

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            menuAction = CreateAction("Controller Menu", "<XRController>{LeftHand}/{SecondaryButton}");
            backAction = CreateAction("Controller Back", "<XRController>{RightHand}/{SecondaryButton}");
            menuAction.performed += OnMenuPerformed;
            backAction.performed += OnBackPerformed;
            menuAction.Enable();
            backAction.Enable();
            RefreshInteractors();
        }

        void OnDisable()
        {
            DisposeAction(ref menuAction, OnMenuPerformed);
            DisposeAction(ref backAction, OnBackPerformed);
        }

        void OnDestroy()
        {
            foreach (var interactor in interactors)
                UnregisterControllerInteractor(interactor);
            interactors.Clear();
            if (menuCanvas != null)
                Destroy(menuCanvas.gameObject);
        }

        static InputAction CreateAction(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Button);
            action.AddBinding(binding);
            return action;
        }

        static void DisposeAction(ref InputAction action, Action<InputAction.CallbackContext> handler)
        {
            if (action == null)
                return;
            action.performed -= handler;
            action.Disable();
            action.Dispose();
            action = null;
        }

        void OnMenuPerformed(InputAction.CallbackContext _) => menuRequested = true;
        void OnBackPerformed(InputAction.CallbackContext _) => backRequested = true;

        /// <summary>Finds the controller ray interactors (parent named "Controller"; never the hand interactors) and configures each once.</summary>
        internal void RefreshInteractors()
        {
            foreach (var interactor in GetComponentsInChildren<NearFarInteractor>(true))
            {
                var parent = interactor.transform.parent;
                if (parent == null || !parent.name.Contains("Controller") || Controllers.Contains(interactor))
                    continue;
                if (interactor.handedness != InteractorHandedness.Left && interactor.handedness != InteractorHandedness.Right)
                {
                    // Not set on the interactor: take it from the controller object's name ("Left Controller" / "Right Controller").
                    if (parent.name.StartsWith("Left", StringComparison.OrdinalIgnoreCase))
                        interactor.handedness = InteractorHandedness.Left;
                    else if (parent.name.StartsWith("Right", StringComparison.OrdinalIgnoreCase))
                        interactor.handedness = InteractorHandedness.Right;
                    else
                        continue;
                }
                // Select = trigger OR grip. A plain reader (not two bindings on one action) so that letting go of one of the two
                // while the other stays down never ends the selection.
                var reader = new TriggerOrGripReader(interactor.handedness);
                interactor.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ObjectReference;
                interactor.selectInput.SetObjectReference(reader);
                interactors.Add(interactor);
                RegisterControllerInteractor(interactor);
            }
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (menuRequested)
            {
                menuRequested = false;
                if (IsMenuOpen)
                    CloseMenu();
                else
                    OpenMenu();
            }

            if (backRequested)
            {
                backRequested = false;
                Back();
            }

            if (IsMenuOpen)
            {
                var environment = EnvironmentController.Instance;
                // An environment switch starting or finishing closes the menu.
                if (environment != null && (environment.IsSwitching || environment.Current != shownEnvironment))
                    CloseMenu();
                else
                    RefreshMenu();
            }
        }

        void Back()
        {
            if (IsMenuOpen)
            {
                CloseMenu();
                return;
            }

            var gallery = FindFirstObjectByType<DiseaseGalleryController>();
            if (gallery != null && gallery.IsOpen)
                gallery.Close();
        }

        // ------------------------------------------------------------------ menu

        void OpenMenu()
        {
            if (head == null)
                head = Camera.main != null ? Camera.main : GetComponentInChildren<Camera>(true);
            if (head == null)
                return;
            if (menuCanvas == null)
                BuildMenu();
            managers = FindManagers();
            var flat = head.transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            flat = flat.sqrMagnitude < 1e-4f ? Vector3.forward : flat.normalized;
            var transformCanvas = menuCanvas.transform;
            transformCanvas.SetPositionAndRotation(head.transform.position + flat * MenuDistance, Quaternion.LookRotation(flat, Vector3.up));
            menuCanvas.worldCamera = head;
            shownEnvironment = EnvironmentController.Instance != null ? EnvironmentController.Instance.Current : null;
            menuCanvas.gameObject.SetActive(true);
            RefreshMenu();
        }

        void CloseMenu()
        {
            if (menuCanvas != null)
                menuCanvas.gameObject.SetActive(false);
        }

        void RefreshMenu()
        {
            var environment = EnvironmentController.Instance;
            var current = environment != null ? environment.Current : null;
            var busy = environment == null || environment.IsSwitching;
            SetEnvironmentButton(farmButton, "Farm", EnvironmentId.Farm, current, busy);
            SetEnvironmentButton(labButton, "Visiting Lab", EnvironmentId.Lab, current, busy);
            SetEnvironmentButton(treatmentButton, "Treatment Lab", EnvironmentId.Treatment, current, busy);
            if (walkLabel != null)
                walkLabel.text = "Smooth walking: " + (SmoothWalkingOn() ? "On" : "Off");
            walkButton.interactable = managers.Count > 0;
        }

        static void SetEnvironmentButton(Button button, string label, EnvironmentId id, EnvironmentId? current, bool busy)
        {
            var here = current.HasValue && current.Value == id;
            button.interactable = !here && !busy;
            button.GetComponentInChildren<TMP_Text>().text = here ? label + "  (here)" : label;
        }

        void BuildMenu()
        {
            var canvasObject = new GameObject("Controller menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            // Not a child of the rig: the menu stays where it was opened while the user teleports or turns.
            SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
            menuCanvas = canvasObject.GetComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.WorldSpace;
            menuCanvas.worldCamera = head;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(MenuWidth, MenuHeight);
            rect.localScale = Vector3.one * MenuScale;

            var background = canvasObject.AddComponent<Image>();
            background.color = UIPalette.Panel;

            var y = -Margin;
            var title = CreateText(rect, "Title", "Controller menu", 34f, UIPalette.Cyan, TextAlignmentOptions.Center, y, 48f);
            title.fontStyle = FontStyles.Bold;
            y -= 48f + ButtonGap;

            farmButton = CreateButton(rect, "Farm", y, () => SwitchTo(EnvironmentId.Farm));
            y -= ButtonHeight + ButtonGap;
            labButton = CreateButton(rect, "Visiting Lab", y, () => SwitchTo(EnvironmentId.Lab));
            y -= ButtonHeight + ButtonGap;
            treatmentButton = CreateButton(rect, "Treatment Lab", y, () => SwitchTo(EnvironmentId.Treatment));
            y -= ButtonHeight + ButtonGap;
            CreateButton(rect, "Recenter", y, () =>
            {
                CloseMenu();
                if (EnvironmentController.Instance != null)
                    EnvironmentController.Instance.Recenter();
            });
            y -= ButtonHeight + ButtonGap;
            walkButton = CreateButton(rect, "Smooth walking: Off", y, ToggleSmoothWalking);
            walkLabel = walkButton.GetComponentInChildren<TMP_Text>();
            y -= ButtonHeight + ButtonGap;
            CreateButton(rect, "Close", y, CloseMenu);
            y -= ButtonHeight + ButtonGap * 2f;

            var legend = CreateText(rect, "Legend",
                "<color=#22D3EE>Trigger</color>  select / press / use a tool\n" +
                "<color=#22D3EE>Grip</color>  hold a tool\n" +
                "<color=#22D3EE>Stick</color>  teleport / turn\n" +
                "<color=#22D3EE>Y</color>  this menu     <color=#22D3EE>B</color>  back / close\n" +
                "<color=#22D3EE>Menu</color>  recenter\n" +
                "<color=#22D3EE>A (right)</color>  open or close the chamber door (near the glass)\n" +
                "<color=#22D3EE>Lab</color>  point at the leaf, hold trigger to turn it",
                21f, UIPalette.TextSecondary, TextAlignmentOptions.TopLeft, y, -y - Margin);
            legend.richText = true;
            legend.textWrappingMode = TextWrappingModes.Normal;
            canvasObject.SetActive(false);
        }

        void SwitchTo(EnvironmentId id)
        {
            CloseMenu();
            var environment = EnvironmentController.Instance;
            if (environment == null)
                return;
            switch (id)
            {
                case EnvironmentId.Farm: environment.ReturnToFarm(); break;
                case EnvironmentId.Lab: environment.EnterLab(); break;
                case EnvironmentId.Treatment: environment.EnterTreatment(); break;
            }
        }

        Button CreateButton(RectTransform parent, string label, float top, Action onClick)
        {
            var rect = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(Margin, top - ButtonHeight);
            rect.offsetMax = new Vector2(-Margin, top);
            var image = rect.GetComponent<Image>();
            image.color = Color.white;
            var button = rect.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = UIPalette.ButtonNormal;
            colors.highlightedColor = UIPalette.ButtonHover;
            colors.selectedColor = UIPalette.ButtonNormal;
            colors.pressedColor = UIPalette.ButtonPressed;
            colors.disabledColor = UIPalette.ButtonSelected;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                Haptic(parent.GetComponent<Canvas>());
                onClick();
            });
            CreateText(rect, "Label", label, 28f, UIPalette.TextPrimary, TextAlignmentOptions.Center, 0f, 0f, true);
            return button;
        }

        static TextMeshProUGUI CreateText(RectTransform parent, string name, string text, float size, Color color,
            TextAlignmentOptions alignment, float top, float height, bool stretch = false)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            var rect = label.rectTransform;
            rect.SetParent(parent, false);
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(8f, 4f);
                rect.offsetMax = new Vector2(-8f, -4f);
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(Margin, top - height);
                rect.offsetMax = new Vector2(-Margin, top);
            }

            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>A short pulse on the controller that is pointing at the menu.</summary>
        void Haptic(Canvas canvas)
        {
            foreach (var interactor in interactors)
            {
                if (interactor == null || !interactor.TryGetCurrentUIRaycastResult(out var hit) || hit.gameObject == null)
                    continue;
                if (canvas == null || hit.gameObject.GetComponentInParent<Canvas>()?.rootCanvas == canvas)
                    interactor.SendHapticImpulse(HapticAmplitude, HapticDuration);
            }
        }

        // ------------------------------------------------------------------ smooth walking

        /// <summary>
        /// The XRI sample's ControllerInputActionManager (a sample type the runtime assembly does not reference) of the LEFT
        /// controller: with smooth motion on the left stick walks, and the right stick keeps teleport and snap turn.
        /// </summary>
        List<Component> FindManagers()
        {
            var found = new List<Component>();
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != ManagerTypeName)
                    continue;
                if (behaviour.GetType().GetProperty(SmoothProperty, BindingFlags.Public | BindingFlags.Instance) == null)
                    continue;
                if (IsLeft(behaviour.transform))
                    found.Add(behaviour);
            }

            return found;
        }

        static bool IsLeft(Transform node)
        {
            for (var t = node; t != null; t = t.parent)
                if (t.name.StartsWith("Left", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        bool SmoothWalkingOn()
        {
            foreach (var manager in managers)
            {
                var property = manager.GetType().GetProperty(SmoothProperty, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.GetValue(manager) is bool on && on)
                    return true;
            }

            return false;
        }

        void ToggleSmoothWalking()
        {
            var enable = !SmoothWalkingOn();
            foreach (var manager in managers)
                manager.GetType().GetProperty(SmoothProperty, BindingFlags.Public | BindingFlags.Instance)?.SetValue(manager, enable);
            RefreshMenu();
        }

        // ------------------------------------------------------------------ select input

        /// <summary>The select button of a controller: pressed while the trigger or the grip is pressed.</summary>
        sealed class TriggerOrGripReader : IXRInputButtonReader
        {
            readonly InteractorHandedness hand;

            public TriggerOrGripReader(InteractorHandedness handedness) => hand = handedness;

            ButtonControl Trigger => ControllerButtons.Find(hand, ControllerButtons.Trigger);
            ButtonControl Grip => ControllerButtons.Find(hand, ControllerButtons.Grip);

            public bool ReadIsPerformed() => Pressed(Trigger) || Pressed(Grip);

            public bool ReadWasPerformedThisFrame() =>
                ReadIsPerformed() && (PressedNow(Trigger) || PressedNow(Grip));

            public bool ReadWasCompletedThisFrame() =>
                !ReadIsPerformed() && (ReleasedNow(Trigger) || ReleasedNow(Grip));

            public float ReadValue()
            {
                var trigger = Trigger;
                var grip = Grip;
                return Mathf.Max(trigger != null ? trigger.ReadValue() : 0f, grip != null ? grip.ReadValue() : 0f);
            }

            public bool TryReadValue(out float value)
            {
                value = ReadValue();
                return ReadIsPerformed();
            }

            static bool Pressed(ButtonControl button) => button != null && button.isPressed;
            static bool PressedNow(ButtonControl button) => button != null && button.wasPressedThisFrame;
            static bool ReleasedNow(ButtonControl button) => button != null && button.wasReleasedThisFrame;
        }
    }
}
