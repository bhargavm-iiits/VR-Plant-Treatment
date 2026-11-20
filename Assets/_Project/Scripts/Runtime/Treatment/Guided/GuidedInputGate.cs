using BTP.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Treatment
{
    /// <summary>
    /// The session's <see cref="TriggerConsumeGate"/> plus its input sources: the left and right controller triggers
    /// ("left", "right"), the desktop left mouse button ("mouse") and the desktop use key ("key"). The guided treatment
    /// observes them every frame (<see cref="ObserveDevices"/>); tools ask <see cref="Allows"/> before a use.
    /// </summary>
    public static class GuidedInputGate
    {
        public const string Left = "left", Right = "right", Mouse = "mouse", Key = "key", Script = "script";

        public static readonly TriggerConsumeGate Gate = new TriggerConsumeGate();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Gate.Clear();

        /// <summary>Whether a use from <paramref name="source"/> may happen now (no popup up, its OK press released).</summary>
        public static bool Allows(string source) => source == Script || Gate.Allows(source);

        /// <summary>The source name of an interactor: "left"/"right" by handedness, "mouse" for the desktop ray.</summary>
        public static string SourceOf(IXRInteractor interactor)
        {
            if (interactor == null)
                return Script;
            if (DesktopDemoControls.IsMouseRay(interactor))
                return Mouse;
            return interactor.handedness switch
            {
                InteractorHandedness.Left => Left,
                InteractorHandedness.Right => Right,
                _ => Script,
            };
        }

        /// <summary>Records which triggers are down this frame (a released trigger ends its consumed press).</summary>
        public static void ObserveDevices(bool useKeyDown)
        {
            ObserveController(XRController.leftHand, Left);
            ObserveController(XRController.rightHand, Right);
            var mouse = UnityEngine.InputSystem.Mouse.current;
            Gate.Observe(Mouse, mouse != null && mouse.leftButton.isPressed);
            Gate.Observe(Key, useKeyDown);
        }

        static void ObserveController(XRController device, string source)
        {
            // Without that controller (hand tracking) the press is ended by its own release event instead.
            if (device == null || !device.added)
                return;
            var trigger = ControllerButtons.Find(source == Left ? InteractorHandedness.Left : InteractorHandedness.Right, ControllerButtons.Trigger);
            if (trigger != null)
                Gate.Observe(source, trigger.isPressed);
        }
    }
}
