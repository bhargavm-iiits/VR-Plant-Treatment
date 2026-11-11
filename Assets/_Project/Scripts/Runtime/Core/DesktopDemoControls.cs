using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace BTP.Core
{
    /// <summary>
    /// Keyboard and mouse demo mode for running without a headset (Editor Play mode or the
    /// Windows build). While no XR headset is active, the mouse cursor acts as the controller
    /// ray: left click selects plants and presses the world-space buttons, exactly as the
    /// trigger does. Hold the right mouse button to look around, WASD to walk. As soon as a
    /// headset becomes active, head tracking and the real controllers take over again.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class DesktopDemoControls : MonoBehaviour
    {
        [Tooltip("Root of the XR rig (the XR Origin).")]
        [SerializeField] Transform rigRoot;
        [Tooltip("The tracked head camera inside the rig.")]
        [SerializeField] Camera head;
        [SerializeField, Min(0.5f)] float eyeHeight = 1.6f;
        [SerializeField, Min(0.1f)] float walkSpeed = 2f;
        [SerializeField, Min(0.01f)] float lookSensitivity = 0.15f;
        [SerializeField, Min(0.1f)] float maxClickDistance = 30f;

        XRRayInteractor mouseRay;
        TrackedPoseDriver headTracking;
        bool active;
        bool showHelp = true;
        float pitch;
        GUIStyle helpStyle;

        /// <summary>True while the keyboard and mouse drive the rig (no XR headset active).</summary>
        public bool IsActive => active;

        internal void Bind(Transform rig, Camera headCamera)
        {
            rigRoot = rig;
            head = headCamera;
        }

        void Awake()
        {
            if (rigRoot == null)
                rigRoot = transform;
            if (head == null)
                head = GetComponentInChildren<Camera>(true);
            if (head == null)
            {
                Debug.LogError("[BTP] DesktopDemoControls needs the rig's head camera.", this);
                enabled = false;
                return;
            }

            headTracking = head.GetComponent<TrackedPoseDriver>();
            mouseRay = CreateMouseRay();
        }

        void OnDisable() => SetActive(false);

        void Update()
        {
            SetActive(!HeadsetActive());
            if (!active)
                return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null)
            {
                HandleKeys(keyboard);
                Walk(keyboard);
            }

            if (mouse != null)
                Look(mouse);
            KeepEyeHeight();
            if (mouse != null)
                AimMouseRay(mouse);
        }

        /// <summary>
        /// Without a headset the XR Origin cannot use floor tracking and raises its camera offset
        /// instead, so the eye height is enforced in world space above the rig's floor.
        /// </summary>
        void KeepEyeHeight()
        {
            var eye = head.transform.position;
            eye.y = rigRoot.position.y + eyeHeight;
            head.transform.position = eye;
        }

        /// <summary>
        /// A headset counts as active if XR is rendering or an HMD device exists. XRSettings.isDeviceActive
        /// alone can stay false over Link/OpenXR, which froze the view while the controllers kept tracking.
        /// </summary>
        static bool HeadsetActive()
        {
            if (XRSettings.isDeviceActive)
                return true;

            SubsystemManager.GetSubsystems(Displays);
            foreach (var display in Displays)
            {
                if (display.running)
                    return true;
            }

            foreach (var device in InputSystem.devices)
            {
                // The XR Interaction Simulator's fake headset is not a real one.
                if (device is XRHMD && !device.GetType().Name.StartsWith("XRSimulated"))
                    return true;
            }

            return false;
        }

        static readonly List<XRDisplaySubsystem> Displays = new();

        void SetActive(bool value)
        {
            if (active == value)
                return;
            active = value;

            // Head tracking would reset the camera pose every frame; the mouse owns it here.
            if (headTracking != null)
                headTracking.enabled = !value;
            if (mouseRay != null)
                mouseRay.gameObject.SetActive(value);

            if (value && head != null)
            {
                pitch = 0f;
                head.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                KeepEyeHeight();
                Debug.Log("[BTP] No XR headset active: keyboard and mouse demo controls enabled (H for help).");
            }
        }

        void HandleKeys(Keyboard keyboard)
        {
            if (keyboard.hKey.wasPressedThisFrame || keyboard.f1Key.wasPressedThisFrame)
                showHelp = !showHelp;

            var environment = EnvironmentController.Instance;
            if (environment == null)
                return;
            if (keyboard.lKey.wasPressedThisFrame)
                environment.EnterLab();
            if (keyboard.fKey.wasPressedThisFrame)
                environment.ReturnToFarm();
            // R (recenter) is handled by RecenterControl.
        }

        /// <summary>Right mouse button held: yaw turns the rig about the head, pitch tilts the head.</summary>
        void Look(Mouse mouse)
        {
            if (!mouse.rightButton.isPressed)
                return;

            var delta = mouse.delta.ReadValue() * lookSensitivity;
            rigRoot.RotateAround(head.transform.position, Vector3.up, delta.x);
            pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
            head.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>WASD / arrow keys walk on the floor plane; Shift walks faster; Q/E turn.</summary>
        void Walk(Keyboard keyboard)
        {
            var input = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;

            var turn = 0f;
            if (keyboard.eKey.isPressed) turn += 1f;
            if (keyboard.qKey.isPressed) turn -= 1f;
            if (turn != 0f)
                rigRoot.RotateAround(head.transform.position, Vector3.up, turn * 90f * Time.deltaTime);

            if (input == Vector2.zero)
                return;

            var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(head.transform.right, Vector3.up).normalized;
            var speed = walkSpeed * (keyboard.shiftKey.isPressed ? 2.5f : 1f);
            rigRoot.position += (forward * input.y + right * input.x).normalized * (speed * Time.deltaTime);
        }

        /// <summary>Points the ray from the eye through the cursor; left click is the trigger.</summary>
        void AimMouseRay(Mouse mouse)
        {
            var ray = head.ScreenPointToRay(mouse.position.ReadValue());
            mouseRay.transform.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction, Vector3.up));

            // No clicks while looking around, so turning never presses a button by accident.
            var pressed = mouse.leftButton.isPressed && !mouse.rightButton.isPressed;
            var value = pressed ? 1f : 0f;
            mouseRay.selectInput.QueueManualState(pressed, value);
            mouseRay.uiPressInput.QueueManualState(pressed, value);
        }

        XRRayInteractor CreateMouseRay()
        {
            var go = new GameObject("Desktop Mouse Ray");
            go.SetActive(false);
            go.transform.SetParent(rigRoot, false);

            var ray = go.AddComponent<XRRayInteractor>();
            ray.lineType = XRRayInteractor.LineType.StraightLine;
            ray.maxRaycastDistance = maxClickDistance;
            ray.enableUIInteraction = true;
            ray.manipulateAttachTransform = false;
            ray.hoverToSelect = false;

            // Plants and buttons only: the teleport surfaces stay for the real controllers.
            var teleport = InteractionLayerMask.GetMask("Teleport");
            ray.interactionLayers = teleport != 0 ? ~teleport : ~0;

            ray.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            ray.uiPressInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            ray.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
            return ray;
        }

        void OnGUI()
        {
            if (!active)
                return;

            helpStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                padding = new RectOffset(12, 12, 10, 10),
                normal = { textColor = Color.white },
            };

            const string help =
                "<b>Desktop demo (no headset)</b>\n" +
                "Left click: select plant / press button\n" +
                "Hold right mouse: look around\n" +
                "W A S D / arrows: walk   Shift: faster\n" +
                "Q / E: turn\n" +
                "L: enter Lab   F: back to Farm   R: recenter\n" +
                "H: hide this help";
            var text = showHelp ? help : "H: controls";
            helpStyle.richText = true;
            var size = helpStyle.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(12f, 12f, size.x, size.y), text, helpStyle);
        }
    }
}
