using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using CommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace BTP.Treatment
{
    /// <summary>
    /// The Treatment lab's hand-held "Training spray" bottle. The rig selects with the index trigger, so a select only
    /// picks the bottle up: it then stays in that hand (no need to keep squeezing) and the trigger sprays while held.
    /// The grip button puts it back on the bench. Spraying needs a release after the pick-up press, so taking the
    /// bottle never fires a burst, and it stops at once on trigger release, tracking loss, a case change, a hand
    /// change, or when the bottle is disabled (scene exit).
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class TrainingSprayer : MonoBehaviour
    {
        /// <summary>Shared action label for targeted spraying (the lab sign and the controller prompt use it).</summary>
        public const string ActionLabel = "Apply targeted spray";

        [SerializeField] ParticleSystem mist;
        [SerializeField] TreatmentController controller;
        [Tooltip("Point of the bottle (local) that sits in the hand: the neck, just under the trigger head.")]
        [SerializeField] Vector3 gripPoint = new Vector3(-0.008f, 0.18f, 0f);
        [Tooltip("Bottle rotation in the hand: the nozzle (+X) points along the controller's forward.")]
        [SerializeField] Vector3 heldEuler = new Vector3(0f, -90f, 0f);
        [Tooltip("Where a hand-less holder (the desktop mouse ray) carries the bottle, relative to the ray origin.")]
        [SerializeField] Vector3 handlessOffset = new Vector3(0.16f, -0.2f, 0.42f);
        [SerializeField, Range(0f, 1f)] float hapticAmplitude = 0.16f;
        [SerializeField, Min(0.02f)] float hapticInterval = 0.12f;

        XRSimpleInteractable interactable;
        IXRSelectInteractor holder;
        Transform homeParent;
        Vector3 homePosition;
        Quaternion homeRotation;
        bool armed;
        float nextHaptic;

        public bool IsHeld => holder != null;
        public bool IsSpraying { get; private set; }

        /// <summary>Raised when spraying starts (true) or stops (false).</summary>
        public event Action<bool> SprayingChanged;

        internal void Bind(ParticleSystem mistSystem, TreatmentController owner)
        {
            mist = mistSystem;
            controller = owner;
        }

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            homeParent = transform.parent;
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            if (mist != null)
                mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelected);
            if (controller != null)
                controller.CaseLoaded += ReturnHome;
        }

        void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelected);
            if (controller != null)
                controller.CaseLoaded -= ReturnHome;
            ReturnHome();
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            var interactor = args.interactorObject;
            // A select from the hand that already holds the bottle is the spray press itself.
            if (interactor == null || interactor == holder)
                return;
            SetSpraying(false);
            holder = interactor;
            armed = false;
            Pulse(0.3f, 0.05f);
        }

        /// <summary>Stops spraying and puts the bottle back on the bench.</summary>
        public void ReturnHome()
        {
            SetSpraying(false);
            holder = null;
            armed = false;
            transform.SetParent(homeParent, false);
            transform.SetLocalPositionAndRotation(homePosition, homeRotation);
        }

        void LateUpdate()
        {
            if (holder == null)
                return;
            var behaviour = holder as MonoBehaviour;
            var attach = behaviour != null && behaviour.isActiveAndEnabled ? holder.GetAttachTransform(interactable) : null;
            if (attach == null)
            {
                ReturnHome();
                return;
            }

            var handless = holder.handedness == InteractorHandedness.None;
            if (handless)
            {
                // Desktop mouse ray (no hand): carry the bottle low and right of the view, nozzle towards the cursor.
                var aim = behaviour.transform;
                var rotation = aim.rotation * Quaternion.Euler(heldEuler);
                transform.SetPositionAndRotation(aim.position + aim.rotation * handlessOffset - rotation * gripPoint, rotation);
            }
            else
            {
                var rotation = attach.rotation * Quaternion.Euler(heldEuler);
                transform.SetPositionAndRotation(attach.position - rotation * gripPoint, rotation);
            }

            var device = DeviceFor(holder);
            if (Pressed(device, CommonUsages.gripButton) || handless && Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
            {
                Pulse(0.25f, 0.04f);
                ReturnHome();
                return;
            }

            // Spray on the trigger: activate (controller trigger) or select (the same trigger on this rig, a pinch, or the
            // desktop left click), so every input path that can hold the bottle can also spray it.
            var trigger = holder is XRBaseInputInteractor input && (input.activateInput.ReadIsPerformed() || input.selectInput.ReadIsPerformed());
            if (!trigger)
                armed = true;
            SetSpraying(armed && trigger && IsTracked(device));
            if (IsSpraying && Time.unscaledTime >= nextHaptic)
            {
                nextHaptic = Time.unscaledTime + hapticInterval;
                Pulse(hapticAmplitude, hapticInterval);
            }
        }

        void SetSpraying(bool on)
        {
            if (on == IsSpraying)
                return;
            IsSpraying = on;
            if (mist != null)
            {
                if (on)
                    mist.Play(true);
                else
                    mist.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            nextHaptic = 0f;
            SprayingChanged?.Invoke(on);
        }

        void Pulse(float amplitude, float duration)
        {
            if (holder is XRBaseInputInteractor input)
                input.SendHapticImpulse(amplitude, duration);
        }

        static XRInputDevice DeviceFor(IXRSelectInteractor interactor)
        {
            switch (interactor.handedness)
            {
                case InteractorHandedness.Left: return InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                case InteractorHandedness.Right: return InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                default: return default;
            }
        }

        static bool Pressed(XRInputDevice device, InputFeatureUsage<bool> usage) =>
            device.isValid && device.TryGetFeatureValue(usage, out var value) && value;

        /// <summary>No controller device (desktop, simulator, hand tracking) counts as tracked; a lost controller does not.</summary>
        static bool IsTracked(XRInputDevice device) =>
            !device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || tracked;
    }
}
