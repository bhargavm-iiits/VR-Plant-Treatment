using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Core
{
    /// <summary>
    /// Select filter for hand-held tools: a controller (see <see cref="VrControllerRig.IsControllerInteractor"/>) may
    /// select (and keep holding) the tool only while that hand's GRIP is pressed, so the TRIGGER stays free to use the tool
    /// (spray, water). Every other interactor (tracked hands, the desktop mouse ray, tests) is always allowed.
    /// The filter is re-evaluated every frame by the interaction manager, so releasing the grip drops the tool.
    /// </summary>
    public sealed class ControllerGripSelectFilter : IXRSelectFilter
    {
        /// <summary>The filter holds no state, so one instance serves every tool.</summary>
        public static readonly ControllerGripSelectFilter Instance = new ControllerGripSelectFilter();

        /// <summary>Reads whether a hand's grip is pressed. Replaced by tests.</summary>
        internal static Func<InteractorHandedness, bool> GripReader = ControllerButtons.IsGripPressed;

        public bool canProcess => true;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactor == null || !VrControllerRig.IsControllerInteractor(interactor))
                return true;
            return GripReader(interactor.handedness);
        }

        /// <summary>Adds the filter to the grab interactable once (safe to call repeatedly).</summary>
        public static void AddTo(XRGrabInteractable grab)
        {
            if (grab == null)
                return;
            grab.selectFilters.Remove(Instance);
            grab.selectFilters.Add(Instance);
        }
    }

    /// <summary>
    /// Reads controller buttons by their Input System usage ({TriggerButton}, {GripButton}, {PrimaryButton}) from the left or right XR
    /// controller device; a missing device or control reads as not pressed.
    /// </summary>
    internal static class ControllerButtons
    {
        public const string Trigger = "{TriggerButton}";
        public const string Grip = "{GripButton}";
        /// <summary>X on the left controller, A on the right.</summary>
        public const string Primary = "{PrimaryButton}";

        static readonly Dictionary<(InputDevice, string), ButtonControl> Cache = new Dictionary<(InputDevice, string), ButtonControl>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Cache.Clear();

        public static XRController Device(InteractorHandedness hand)
        {
            switch (hand)
            {
                case InteractorHandedness.Left: return XRController.leftHand;
                case InteractorHandedness.Right: return XRController.rightHand;
                default: return null;
            }
        }

        public static ButtonControl Find(InteractorHandedness hand, string usage)
        {
            var device = Device(hand);
            if (device == null)
                return null;
            var key = ((InputDevice)device, usage);
            if (Cache.TryGetValue(key, out var cached) && cached != null && cached.device.added)
                return cached;
            var found = InputControlPath.TryFindControl(device, usage) as ButtonControl;
            Cache[key] = found;
            return found;
        }

        public static bool IsPressed(InteractorHandedness hand, string usage)
        {
            var button = Find(hand, usage);
            return button != null && button.isPressed;
        }

        public static bool IsGripPressed(InteractorHandedness hand) => IsPressed(hand, Grip);

        /// <summary>The hand's thumbstick ({Primary2DAxis}); zero when there is no such controller.</summary>
        public static Vector2 Stick(InteractorHandedness hand)
        {
            var device = Device(hand);
            if (device == null)
                return Vector2.zero;
            var stick = InputControlPath.TryFindControl(device, "{Primary2DAxis}") as Vector2Control;
            return stick != null ? stick.ReadValue() : Vector2.zero;
        }
    }
}
