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
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.Core
{
    /// <summary>
    /// Controller support that is shared by every environment, installed on the XR rig (it installs itself at runtime when the
    /// rig does not have it yet, so no scene rebuild is needed). It
    /// 1. makes the controller ray interactors select with the trigger OR the grip and registers them, so hand-held tools can
    ///    require the grip (<see cref="ControllerGripSelectFilter"/>; the treatment tools are picked up and put back with a grip
    ///    press, BTP.Treatment.ToolGripToggle, held through <see cref="SetHoldingTool"/>) and keep the trigger for using them;
    /// 2. opens a world-space controller menu with the left Y button (environments, recenter, smooth walking, legend);
    /// 3. handles the right B button as Back: closes the menu, else an open chamber door the user stands at
    ///    (<see cref="AddBackHandler"/>), else the Farm disease ring;
    /// 4. sets smooth locomotion once (<see cref="ConfigureLocomotion"/>): the left stick walks and the right stick turns
    ///    continuously (no teleport arc, no snap turn), and smooth walking never lifts the user off the floor.
    /// The thumbsticks and the menu button belong to the rig (move, turn, recenter) and are not bound here; the right
    /// A opens and closes the treatment chamber's door while the user stands at it (BTP.Treatment.ChamberDoor).
    /// </summary>
    public sealed class VrControllerRig : MonoBehaviour
    {
        const float MenuDistance = 0.7f, MenuScale = 0.0012f, HapticAmplitude = 0.25f, HapticDuration = 0.06f;
        const float MenuWidth = 560f, MenuHeight = 860f, ButtonHeight = 62f, ButtonGap = 10f, Margin = 24f;
        const string ManagerTypeName = "ControllerInputActionManager", SmoothProperty = "smoothMotionEnabled", SmoothTurnProperty = "smoothTurnEnabled";
        const string TeleportModeField = "m_TeleportMode";
        /// <summary>Smooth walking speed (m/s): a calm walk (the XRI default 2.5 m/s is a jog and less comfortable).</summary>
        internal const float MoveSpeed = 1.75f;
        /// <summary>Smooth turn speed (degrees/s).</summary>
        internal const float TurnSpeed = 75f;
        /// <summary>Highest step (m) the walking rig climbs: the Farm's soil and path layers, nothing that stands on the floor.</summary>
        internal const float StepOffset = 0.05f;
        /// <summary>Rise (m) above the walk's floor that is put back down (see <see cref="KeepOnFloor"/>).</summary>
        const float FloorTolerance = 0.1f;

        static readonly HashSet<IXRInteractor> Controllers = new HashSet<IXRInteractor>();
        /// <summary>Asked in order on right B when the controller menu is closed (the chamber door closes); the first that acts wins.</summary>
        static readonly List<Func<bool>> BackHandlers = new List<Func<bool>>();
        /// <summary>Controllers that hold a tool by grip toggle: their select stays active without a button held.</summary>
        static readonly HashSet<IXRInteractor> HoldingTool = new HashSet<IXRInteractor>();
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

        bool locomotionConfigured;
        ContinuousMoveProvider moveProvider;
        TeleportationProvider teleportProvider;
        float floorY;

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

        internal static void UnregisterControllerInteractor(IXRInteractor interactor)
        {
            Controllers.Remove(interactor);
            HoldingTool.Remove(interactor);
        }

        /// <summary>The registered, active controller interactor of <paramref name="hand"/>, or null.</summary>
        internal static XRBaseInputInteractor ControllerFor(InteractorHandedness hand)
        {
            foreach (var interactor in Controllers)
                if (interactor is XRBaseInputInteractor input && input != null && input.isActiveAndEnabled && input.handedness == hand)
                    return input;
            return null;
        }

        /// <summary>
        /// Marks a controller as holding a tool by grip toggle (BTP.Treatment.ToolGripToggle): its select input then reads as
        /// pressed, so the selection is kept after the grip is let go, until the toggle ends it.
        /// </summary>
        internal static void SetHoldingTool(IXRInteractor interactor, bool holding)
        {
            if (interactor == null)
                return;
            if (holding)
                HoldingTool.Add(interactor);
            else
                HoldingTool.Remove(interactor);
        }

        internal static bool IsHoldingTool(IXRInteractor interactor) => interactor != null && HoldingTool.Contains(interactor);

        /// <summary>A short pulse on each registered controller whose UI ray is on <paramref name="canvas"/> (a world pop-up's button press).</summary>
        internal static void PulseControllersPointingAt(Canvas canvas, float amplitude, float duration)
        {
            if (canvas == null)
                return;
            foreach (var interactor in Controllers)
            {
                if (!(interactor is NearFarInteractor nearFar) || nearFar == null || !nearFar.TryGetCurrentUIRaycastResult(out var hit) || hit.gameObject == null)
                    continue;
                var hitCanvas = hit.gameObject.GetComponentInParent<Canvas>();
                if (hitCanvas != null && hitCanvas.rootCanvas == canvas.rootCanvas)
                    nearFar.SendHapticImpulse(amplitude, duration);
            }
        }

        /// <summary>Adds a right-B handler (BTP.Treatment.ChamberDoor: close the open door the user stands at).</summary>
        internal static void AddBackHandler(Func<bool> handler)
        {
            if (handler != null && !BackHandlers.Contains(handler))
                BackHandlers.Add(handler);
        }

        internal static void RemoveBackHandler(Func<bool> handler) => BackHandlers.Remove(handler);

        /// <summary>Right B: the controller menu first, then the registered handlers (the chamber door), else false.</summary>
        internal static bool HandleBack(bool menuOpen, Action closeMenu)
        {
            if (menuOpen)
            {
                closeMenu?.Invoke();
                return true;
            }

            for (var i = BackHandlers.Count - 1; i >= 0; i--)
                if (BackHandlers[i] != null && BackHandlers[i]())
                    return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Controllers.Clear();
            HoldingTool.Clear();
            BackHandlers.Clear();
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
                existing.IgnoreFloorMarkers();
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
            ConfigureLocomotion();
            InputSystem.onDeviceChange += OnDeviceChange;
            Debug.Log("[BTP] Input: " + ControllerButtons.DescribeControllers());
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            DisposeAction(ref menuAction, OnMenuPerformed);
            DisposeAction(ref backAction, OnBackPerformed);
        }

        /// <summary>Logs the XR controllers whenever one is added or removed (which device each hand's buttons come from).</summary>
        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is UnityEngine.InputSystem.XR.XRController && (change == InputDeviceChange.Added || change == InputDeviceChange.Removed))
                Debug.Log($"[BTP] Input: {device.layout} {change.ToString().ToLowerInvariant()}. " + ControllerButtons.DescribeControllers());
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
                var reader = new TriggerOrGripReader(interactor);
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

        // After the locomotion providers and the XR body transformer have moved the rig this frame.
        void LateUpdate() => KeepOnFloor();

        void Back()
        {
            // The menu first, then an open chamber door the user stands at, else the Farm disease ring.
            if (HandleBack(IsMenuOpen, CloseMenu))
                return;

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
            // Not a child of the rig: the menu stays where it was opened while the user walks, teleports or turns.
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
            walkButton = CreateButton(rect, "Smooth walking: On", y, ToggleSmoothWalking);
            walkLabel = walkButton.GetComponentInChildren<TMP_Text>();
            y -= ButtonHeight + ButtonGap;
            CreateButton(rect, "Close", y, CloseMenu);
            y -= ButtonHeight + ButtonGap * 2f;

            var legend = CreateText(rect, "Legend",
                "<color=#22D3EE>Trigger</color>  select / press / use a tool\n" +
                "<color=#22D3EE>Grip</color>  pick up / put back a tool\n" +
                "<color=#22D3EE>Left stick</color>  walk\n" +
                "<color=#22D3EE>Right stick</color>  turn\n" +
                "<color=#22D3EE>Y</color>  this menu     <color=#22D3EE>B</color>  back / close\n" +
                "<color=#22D3EE>Menu</color>  recenter\n" +
                "<color=#22D3EE>A (right)</color>  open the chamber door (near the glass)\n" +
                "<color=#22D3EE>B (right)</color>  close the chamber door\n" +
                "<color=#22D3EE>Lab</color>  grip: pick up the leaf / put it back",
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

        // ------------------------------------------------------------------ smooth locomotion

        /// <summary>
        /// Smooth locomotion by default, set once per rig (so a menu choice then sticks across environment switches): the
        /// LEFT stick walks (continuous move towards where the head looks, with strafing) and the RIGHT stick only turns,
        /// continuously (<see cref="ApplyStickDefaults"/>). Comfortable speeds, a pulled-back stick never snaps the view round
        /// 180 degrees, and walking neither climbs onto things nor gets stopped by the floor pads.
        /// </summary>
        internal void ConfigureLocomotion()
        {
            if (locomotionConfigured)
                return;
            locomotionConfigured = true;
            ApplyStickDefaults(gameObject);
            foreach (var move in GetComponentsInChildren<ContinuousMoveProvider>(true))
            {
                move.moveSpeed = MoveSpeed;
                move.enableStrafe = true;
                move.enableFly = false;
            }

            foreach (var turn in GetComponentsInChildren<ContinuousTurnProvider>(true))
            {
                turn.turnSpeed = TurnSpeed;
                turn.enableTurnAround = false;
            }

            // No gravity on this rig: a step-up would never come back down, so only the floor's few-centimetre layers are stepped.
            foreach (var body in GetComponentsInChildren<CharacterController>(true))
                body.stepOffset = Mathf.Min(body.stepOffset, StepOffset);
            moveProvider = GetComponentInChildren<ContinuousMoveProvider>(true);
            teleportProvider = GetComponentInChildren<TeleportationProvider>(true);
            floorY = RigTransform().position.y;
            IgnoreFloorMarkers();
        }

        /// <summary>
        /// Sets the XRI sample's ControllerInputActionManager of each controller (a sample type the runtime assembly does not
        /// reference, so by name and reflection): smooth motion on the LEFT (its stick walks) and smooth turn on the RIGHT,
        /// whose stick no longer teleports (no arc, no jump: walking and turning only). Smooth turn is on for both, so turning
        /// smooth walking off in the menu gives the left stick teleport and a smooth turn, never a snap turn. Returns how many
        /// managers were set.
        /// </summary>
        internal static int ApplyStickDefaults(GameObject rig)
        {
            var count = 0;
            foreach (var behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!IsManager(behaviour))
                    continue;
                var left = OnHand(behaviour.transform, "Left");
                if (!left && !OnHand(behaviour.transform, "Right"))
                    continue;
                SetBool(behaviour, SmoothTurnProperty, true);
                SetBool(behaviour, SmoothProperty, left);
                if (!left)
                    UnbindTeleport(behaviour);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Unbinds the manager's teleport-mode action (an empty override path disables a binding; runtime only, the input
        /// asset is unchanged). The manager keeps enabling the action, but the stick can no longer start a teleport.
        /// </summary>
        static void UnbindTeleport(Component manager)
        {
            var field = manager.GetType().GetField(TeleportModeField, BindingFlags.NonPublic | BindingFlags.Instance);
            var action = field != null && field.GetValue(manager) is InputActionReference reference && reference != null ? reference.action : null;
            if (action == null)
                return;
            for (var i = 0; i < action.bindings.Count; i++)
                action.ApplyBindingOverride(i, string.Empty);
        }

        /// <summary>
        /// The viewpoint and station pads (TeleportationAnchor) are floor markers, but their collider is a 0.45 m ball: the
        /// walking rig's CharacterController ignores them (teleport rays still hit them). Re-applied after every scene load.
        /// </summary>
        void IgnoreFloorMarkers()
        {
            // IgnoreCollision needs both colliders active and enabled.
            var bodies = GetComponentsInChildren<CharacterController>();
            if (bodies.Length == 0)
                return;
            foreach (var anchor in FindObjectsByType<TeleportationAnchor>(FindObjectsInactive.Exclude))
            foreach (var marker in anchor.GetComponentsInChildren<Collider>())
            foreach (var body in bodies)
                if (marker.enabled && body.enabled && marker != body)
                    Physics.IgnoreCollision(body, marker, true);
        }

        static bool IsManager(MonoBehaviour behaviour) =>
            behaviour != null && behaviour.GetType().Name == ManagerTypeName &&
            behaviour.GetType().GetProperty(SmoothProperty, BindingFlags.Public | BindingFlags.Instance) != null;

        static void SetBool(Component manager, string property, bool value) =>
            manager.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.SetValue(manager, value);

        Transform RigTransform()
        {
            var origin = moveProvider != null && moveProvider.mediator != null ? moveProvider.mediator.xrOrigin : null;
            return origin != null && origin.Origin != null ? origin.Origin.transform : transform;
        }

        /// <summary>
        /// Smooth walking stays on the floor. The rig has no gravity (the XR Origin Hands sample turns its GravityProvider
        /// off), so anything that lifted the CharacterController while walking (the sample's 0.5 m step offset, now
        /// <see cref="StepOffset"/>, or a slope) would leave the user floating after stepping off. While the move provider walks,
        /// the rig never rises more than <see cref="FloorTolerance"/> above the floor it stood on when the walk began;
        /// teleports, recentring and environment switches set the floor anew.
        /// </summary>
        void KeepOnFloor()
        {
            if (moveProvider == null)
                return;
            var rig = RigTransform();
            var position = rig.position;
            // Ended counts too: the last step of a walk may land in the frame the stick is released.
            var walking = moveProvider.locomotionState != LocomotionState.Idle;
            var teleporting = teleportProvider != null && teleportProvider.isLocomotionActive;
            if (!walking || teleporting || position.y < floorY)
            {
                floorY = position.y;
                return;
            }

            if (position.y - floorY <= FloorTolerance)
                return;
            position.y = floorY;
            rig.position = position;
            // The CharacterController reads the moved transform on its next Move (auto-sync is off).
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ smooth walking (menu)

        /// <summary>
        /// The ControllerInputActionManager of the LEFT controller: with smooth motion on its stick walks (the default), off
        /// it teleports and turns. The right stick always only turns.
        /// </summary>
        List<Component> FindManagers()
        {
            var found = new List<Component>();
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
                if (IsManager(behaviour) && OnHand(behaviour.transform, "Left"))
                    found.Add(behaviour);
            return found;
        }

        /// <summary>True when <paramref name="node"/> or a parent is named after the hand ("Left Controller", "Right Controller").</summary>
        internal static bool OnHand(Transform node, string side)
        {
            for (var t = node; t != null; t = t.parent)
                if (t.name.StartsWith(side, StringComparison.OrdinalIgnoreCase))
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
                SetBool(manager, SmoothProperty, enable);
            RefreshMenu();
        }

        // ------------------------------------------------------------------ select input

        /// <summary>
        /// The select button of a controller: pressed while the trigger or the grip is pressed, or while the controller holds a
        /// tool by grip toggle.
        /// </summary>
        sealed class TriggerOrGripReader : IXRInputButtonReader
        {
            readonly IXRInteractor owner;
            readonly InteractorHandedness hand;

            public TriggerOrGripReader(IXRInteractor interactor)
            {
                owner = interactor;
                hand = interactor.handedness;
            }

            ButtonControl Trigger => ControllerButtons.Find(hand, ControllerButtons.Trigger);
            ButtonControl Grip => ControllerButtons.Find(hand, ControllerButtons.Grip);

            // A tool held by grip toggle keeps the select active with no button down (BTP.Treatment.ToolGripToggle).
            public bool ReadIsPerformed() => IsHoldingTool(owner) || Pressed(Trigger) || Pressed(Grip);

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
