using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Core
{
    /// <summary>
    /// Select filter for hand-held tools: a controller (see <see cref="VrControllerRig.IsControllerInteractor"/>) may
    /// select (and keep holding) a tool whose trigger USES it (the sprayer) only while that hand's GRIP is pressed, so the
    /// TRIGGER stays free to spray (<see cref="Instance"/>). A tool the trigger does nothing with (the grain scoop, the
    /// fertiliser sacks, bench props) is held with the grip OR the trigger (<see cref="GripOrTrigger"/>): every other
    /// controller select in the app is the trigger, and refusing it silently made those tools look ungrabbable.
    /// Every other interactor (tracked hands, the desktop mouse ray, tests) is always allowed. The filter is re-evaluated
    /// every frame by the interaction manager, so releasing the holding button(s) drops the tool.
    /// </summary>
    public sealed class ControllerGripSelectFilter : IXRSelectFilter
    {
        /// <summary>Grip only (tools the trigger uses). The filters hold no state, so one instance serves every tool.</summary>
        public static readonly ControllerGripSelectFilter Instance = new ControllerGripSelectFilter(false);

        /// <summary>Grip or trigger (tools the trigger does not use).</summary>
        public static readonly ControllerGripSelectFilter GripOrTrigger = new ControllerGripSelectFilter(true);

        /// <summary>Reads whether a hand's grip is pressed. Replaced by tests.</summary>
        internal static Func<InteractorHandedness, bool> GripReader = ControllerButtons.IsGripPressed;

        /// <summary>Reads whether a hand's trigger is pressed. Replaced by tests.</summary>
        internal static Func<InteractorHandedness, bool> TriggerReader = ControllerButtons.IsTriggerPressed;

        readonly bool triggerHolds;

        ControllerGripSelectFilter(bool triggerAlsoHolds) => triggerHolds = triggerAlsoHolds;

        /// <summary>True when the trigger also picks up and holds the tool.</summary>
        public bool TriggerHolds => triggerHolds;

        public bool canProcess => true;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactor == null || !VrControllerRig.IsControllerInteractor(interactor))
                return true;
            return GripReader(interactor.handedness) || (triggerHolds && TriggerReader(interactor.handedness));
        }

        /// <summary>
        /// Adds the right filter to the grab interactable once (safe to call repeatedly): grip only when the trigger uses the
        /// tool (<paramref name="triggerUsesTool"/>), else grip or trigger.
        /// </summary>
        public static void AddTo(XRGrabInteractable grab, bool triggerUsesTool = true)
        {
            if (grab == null)
                return;
            grab.selectFilters.Remove(Instance);
            grab.selectFilters.Remove(GripOrTrigger);
            grab.selectFilters.Add(triggerUsesTool ? Instance : GripOrTrigger);
        }
    }

    /// <summary>
    /// Reads controller buttons by their Input System usage ({TriggerButton}, {GripButton}, {PrimaryButton}) from the left or right XR
    /// controller device; a missing device or control reads as not pressed. <see cref="IsHeld"/> reads every controller of a hand.
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

        public static ButtonControl Find(InteractorHandedness hand, string usage) => FindOn(Device(hand), usage);

        static ButtonControl FindOn(InputDevice device, string usage)
        {
            if (device == null)
                return null;
            var key = (device, usage);
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

        /// <summary>
        /// True while the trigger or the grip of ANY XR controller of that hand is down. A level read of every device with
        /// the hand's usage, so it depends neither on which device <see cref="XRController.leftHand"/> happens to return
        /// (OpenXR can add several per hand, e.g. the hand-interaction profile next to the Touch controller) nor on press
        /// edges (<see cref="ButtonControl.wasPressedThisFrame"/>), which an input sample taken just before rendering can
        /// leave unseen by the next Update. Callers derive their own edges from this level, frame to frame.
        /// </summary>
        public static bool IsHeld(InteractorHandedness hand)
        {
            var side = hand == InteractorHandedness.Left ? CommonUsages.LeftHand
                : hand == InteractorHandedness.Right ? CommonUsages.RightHand : default;
            if (side.IsEmpty())
                return false;
            foreach (var device in InputSystem.devices)
            {
                if (!(device is XRController controller) || !controller.added || !HasUsage(controller, side))
                    continue;
                if (Pressed(FindOn(controller, Trigger)) || Pressed(FindOn(controller, Grip)))
                    return true;
            }

            return false;
        }

        /// <summary>The XR controllers per hand and which one <see cref="XRController.leftHand"/>/<c>rightHand</c> returns (device logs).</summary>
        public static string DescribeControllers()
        {
            var text = new System.Text.StringBuilder();
            foreach (var device in InputSystem.devices)
            {
                if (!(device is XRController controller))
                    continue;
                text.Append(text.Length > 0 ? "; " : "").Append(controller.layout).Append(" [")
                    .Append(string.Join(",", controller.usages)).Append(controller.added ? "" : " removed").Append(']');
            }

            return $"XR controllers: {(text.Length > 0 ? text.ToString() : "none")}; leftHand={XRController.leftHand?.layout ?? "none"}, " +
                   $"rightHand={XRController.rightHand?.layout ?? "none"}";
        }

        static bool HasUsage(InputDevice device, InternedString usage)
        {
            foreach (var u in device.usages)
                if (u == usage)
                    return true;
            return false;
        }

        static bool Pressed(ButtonControl button) => button != null && button.isPressed;

        public static bool IsGripPressed(InteractorHandedness hand) => IsPressed(hand, Grip);

        public static bool IsTriggerPressed(InteractorHandedness hand) => IsPressed(hand, Trigger);

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
