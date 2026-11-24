using System;
using System.Collections.Generic;
using BTP.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace BTP.Treatment
{
    /// <summary>
    /// Grip toggle for every hand-held item (<see cref="TreatmentGrabbable"/>: the treatment tools, and the visiting Lab's
    /// leaf, BTP.Lab.LeafMouseRotation) on the controllers: one GRIP press near an item (or pointing at it) picks it up into
    /// that hand, where it stays with the grip let go; the next GRIP press of that hand puts it back (it eases home to the
    /// bench or stand). The trigger keeps using a held tool (spray).
    /// It does not depend on the interactor's hover: the tool is the one the controller hovers, else the nearest tool collider
    /// within <see cref="NearReach"/> of the controller, else the first tool the controller points at within
    /// <see cref="RayReach"/>. The press comes from Input System actions on the grip button (the binding XRI's own Select
    /// uses), the selection is entered and ended through the interaction manager, and while held the controller's select
    /// input reads as pressed (<see cref="VrControllerRig.SetHoldingTool"/>), so XRI keeps the selection. The hand's poke
    /// interactor pauses while it holds a tool, so a UI canvas at the hand can never take the hand's interaction group away
    /// from the held tool. Tracked hands keep their natural pinch/grasp hold (see <see cref="TreatmentGrabbable"/>).
    /// One instance runs per loaded scene that has an item, created by the first item that registers (in the Treatment lab
    /// a tool, in the visiting Lab the leaf).
    /// </summary>
    public sealed class ToolGripToggle : MonoBehaviour
    {
        /// <summary>Reach (m) from the controller's near-cast point to a tool's collider for a near pick-up.</summary>
        public const float NearReach = 0.15f;
        /// <summary>Longest pointing pick-up (m).</summary>
        public const float RayReach = 5f;
        /// <summary>Radius (m) of the pointing sweep.</summary>
        public const float PointRadius = 0.05f;
        /// <summary>How far (m) behind the first surface the pointing sweep meets a tool still counts (the bench under it).</summary>
        public const float PointSlack = 0.25f;

        static readonly List<TreatmentGrabbable> Items = new List<TreatmentGrabbable>();
        static ToolGripToggle instance;

        /// <summary>Replaces the grip press reading (tests, batch verification): true when that hand's grip went down this frame.</summary>
        internal static Func<InteractorHandedness, bool> PressOverride;

        InputAction leftGrip, rightGrip;
        readonly Dictionary<IXRInteractor, List<Behaviour>> pausedPokes = new Dictionary<IXRInteractor, List<Behaviour>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Items.Clear();
            instance = null;
            PressOverride = null;
        }

        internal static IReadOnlyList<TreatmentGrabbable> Registered => Items;

        internal static void Register(TreatmentGrabbable item)
        {
            if (item == null || Items.Contains(item))
                return;
            Items.Add(item);
            if (instance != null || !Application.isPlaying)
                return;
            var go = new GameObject("ToolGripToggle");
            if (item.gameObject.scene.IsValid())
                SceneManager.MoveGameObjectToScene(go, item.gameObject.scene);
            instance = go.AddComponent<ToolGripToggle>();
            Debug.Log($"[BTP] Grip toggle: active in scene '{go.scene.name}' (first item: {item.DisplayName}).");
        }

        /// <summary>True while a grip toggle runs (one per scene with hand-held items).</summary>
        internal static bool Active => instance != null && instance.isActiveAndEnabled;

        internal static void Unregister(TreatmentGrabbable item) => Items.Remove(item);

        void OnEnable()
        {
            leftGrip = GripAction("Left");
            rightGrip = GripAction("Right");
        }

        void OnDisable()
        {
            Dispose(ref leftGrip);
            Dispose(ref rightGrip);
            foreach (var controller in new List<IXRInteractor>(pausedPokes.Keys))
                ResumePokes(controller);
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        static InputAction GripAction(string hand)
        {
            var action = new InputAction(hand + " tool grip", InputActionType.Button);
            action.AddBinding($"<XRController>{{{hand}Hand}}/{{GripButton}}");
            action.AddBinding($"<XRController>{{{hand}Hand}}/gripPressed");
            action.Enable();
            return action;
        }

        static void Dispose(ref InputAction action)
        {
            if (action == null)
                return;
            action.Disable();
            action.Dispose();
            action = null;
        }

        bool PressedThisFrame(InteractorHandedness hand)
        {
            if (PressOverride != null)
                return PressOverride(hand);
            var action = hand == InteractorHandedness.Left ? leftGrip : rightGrip;
            return action != null && action.WasPressedThisFrame();
        }

        void Update()
        {
            foreach (var hand in new[] { InteractorHandedness.Left, InteractorHandedness.Right })
                if (PressedThisFrame(hand))
                    Toggle(VrControllerRig.ControllerFor(hand));
        }

        /// <summary>A grip press of <paramref name="controller"/>: puts back the tool it holds, else picks one up. Returns the tool.</summary>
        internal TreatmentGrabbable Toggle(XRBaseInputInteractor controller)
        {
            if (controller == null || controller.interactionManager == null)
                return null;
            var held = HeldBy(controller);
            if (held != null)
            {
                Release(held);
                return held;
            }

            if (controller.hasSelection)
                return null;
            var item = Choose(Items, controller, NearPoint(controller), PointingRay(controller));
            if (item == null)
            {
                Debug.Log($"[BTP] Grip toggle: {controller.handedness} grip pressed, no item in reach or pointed at ({Items.Count} registered).");
                return null;
            }
            Pick(item, controller);
            return item;
        }

        void Pick(TreatmentGrabbable item, XRBaseInputInteractor controller)
        {
            var grab = item.Grab;
            item.ToggleHolder = controller;
            VrControllerRig.SetHoldingTool(controller, true);
            controller.interactionManager.SelectEnter((IXRSelectInteractor)controller, (IXRSelectInteractable)grab);
            Debug.Log($"[BTP] Grip toggle: {controller.handedness} controller picks up {item.DisplayName} (selected {grab.isSelected}).");
            if (!grab.isSelected)
            {
                item.ToggleHolder = null;
                VrControllerRig.SetHoldingTool(controller, false);
                return;
            }

            PausePokes(controller);
        }

        /// <summary>Ends a toggled hold: the tool leaves the hand and goes home (<see cref="TreatmentGrabbable"/> on select exit).</summary>
        internal static void Release(TreatmentGrabbable item)
        {
            var controller = item != null ? item.ToggleHolder : null;
            if (controller == null)
                return;
            Debug.Log($"[BTP] Grip toggle: {controller.handedness} controller puts back {item.DisplayName}.");
            item.ToggleHolder = null;
            VrControllerRig.SetHoldingTool(controller, false);
            var grab = item.Grab;
            if (grab != null && controller.interactionManager != null && controller.IsSelecting((IXRSelectInteractable)grab))
                controller.interactionManager.SelectExit((IXRSelectInteractor)controller, (IXRSelectInteractable)grab);
            if (instance != null)
                instance.ResumePokes(controller);
        }

        /// <summary>The selection ended some other way (case change, lost tracking): forget the toggle.</summary>
        internal static void Forget(TreatmentGrabbable item)
        {
            Debug.Log($"[BTP] Grip toggle: {item.DisplayName} left the hand another way.");
            var controller = item.ToggleHolder;
            item.ToggleHolder = null;
            VrControllerRig.SetHoldingTool(controller, false);
            if (instance != null && controller != null)
                instance.ResumePokes(controller);
        }

        static TreatmentGrabbable HeldBy(XRBaseInputInteractor controller)
        {
            foreach (var item in Items)
                if (item != null && ReferenceEquals(item.ToggleHolder, controller))
                    return item;
            return null;
        }

        /// <summary>
        /// The tool a grip press picks up: one the controller hovers, else the nearest tool whose collider is within
        /// <see cref="NearReach"/> of <paramref name="near"/>, else the first tool a <see cref="PointRadius"/> sweep along the ray
        /// meets (within <see cref="PointSlack"/> of the first surface it meets).
        /// Tools already in a hand (or the mouse's) are skipped.
        /// </summary>
        internal static TreatmentGrabbable Choose(IReadOnlyList<TreatmentGrabbable> items, IXRHoverInteractor hoverer, Vector3 near, Ray? ray)
        {
            if (hoverer != null)
                foreach (var hovered in hoverer.interactablesHovered)
                    foreach (var item in items)
                        if (Available(item) && ReferenceEquals(item.Grab, hovered))
                            return item;

            TreatmentGrabbable best = null;
            var bestDistance = NearReach;
            foreach (var item in items)
            {
                if (!Available(item))
                    continue;
                foreach (var collider in item.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.isTrigger)
                        continue;
                    var distance = Vector3.Distance(collider.ClosestPoint(near), near);
                    if (distance <= bestDistance)
                    {
                        bestDistance = distance;
                        best = item;
                    }
                }
            }

            if (best != null || !ray.HasValue)
                return best;
            // Pointing: a forgiving sweep (a flat 4 cm scoop is a thin target at arm's length). The bench a tool lies on is
            // usually hit just before the tool itself, so a tool counts when it is hit within PointSlack of the first hit.
            var hits = Physics.SphereCastAll(ray.Value, PointRadius, RayReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var first = -1f;
            foreach (var hit in hits)
            {
                if (hit.distance <= 0f && hit.point == Vector3.zero)
                    continue; // overlapping at the start of the sweep (the controller's own surroundings)
                if (first < 0f)
                    first = hit.distance;
                if (hit.distance > first + PointSlack)
                    break;
                var pointed = hit.collider.GetComponentInParent<TreatmentGrabbable>();
                if (pointed == null || !Available(pointed))
                    continue;
                foreach (var item in items)
                    if (ReferenceEquals(item, pointed))
                        return pointed;
            }

            return null;
        }

        static bool Available(TreatmentGrabbable item) =>
            item != null && item.isActiveAndEnabled && item.Grab != null && item.Grab.isActiveAndEnabled && !item.IsHeld;

        /// <summary>The controller's near-grab point (its near caster's origin).</summary>
        static Vector3 NearPoint(XRBaseInputInteractor controller)
        {
            if (controller is NearFarInteractor nearFar && nearFar.nearInteractionCaster is InteractionCasterBase caster && caster.effectiveCastOrigin != null)
                return caster.effectiveCastOrigin.position;
            return controller.transform.position;
        }

        /// <summary>Where the controller points (its far caster's origin and forward).</summary>
        static Ray? PointingRay(XRBaseInputInteractor controller)
        {
            var origin = controller.transform;
            if (controller is NearFarInteractor nearFar && nearFar.farInteractionCaster is InteractionCasterBase caster && caster.effectiveCastOrigin != null)
                origin = caster.effectiveCastOrigin;
            return new Ray(origin.position, origin.forward);
        }

        void PausePokes(XRBaseInputInteractor controller)
        {
            var root = controller.transform.parent != null ? controller.transform.parent : controller.transform;
            var paused = new List<Behaviour>();
            foreach (var poke in root.GetComponentsInChildren<XRPokeInteractor>())
                if (poke.enabled)
                {
                    poke.enabled = false;
                    paused.Add(poke);
                }

            pausedPokes[controller] = paused;
        }

        void ResumePokes(IXRInteractor controller)
        {
            if (!pausedPokes.TryGetValue(controller, out var paused))
                return;
            foreach (var poke in paused)
                if (poke != null)
                    poke.enabled = true;
            pausedPokes.Remove(controller);
        }
    }
}
