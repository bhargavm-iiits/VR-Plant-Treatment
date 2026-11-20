using System;
using System.Collections.Generic;
using BTP.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Treatment
{
    /// <summary>
    /// A portable treatment item that can be picked up, inspected, used and put back (added at runtime; no scene change).
    /// Controllers hold it with the GRIP (<see cref="ControllerGripSelectFilter"/>; the trigger stays free to use it),
    /// tracked hands with a grasp, the desktop with a click (<see cref="DesktopToolHand"/>). It follows the hand through an
    /// XRGrabInteractable (instantaneous with position/rotation smoothing: crisp, no physics jitter) from its own grip
    /// attach pose, highlights on hover and pulses the controller on grab and release.
    /// Return rule (documented choice): released within <see cref="GuidedTreatmentConfig.homeSnapDistance"/> (25 cm) of its
    /// holder it eases back into the original pose over 0.25 s; released elsewhere it drops physically (rigidbody,
    /// gravity) onto whatever has a collider and goes home by itself, eased, after lying still for
    /// <see cref="GuidedTreatmentConfig.idleReturnSeconds"/> (8 s), or at once when it falls below the floor or out of
    /// reach. <see cref="ReturnHome"/> sends it home on demand (case change, step reset, desktop G).
    /// </summary>
    public sealed class TreatmentGrabbable : MonoBehaviour
    {
        public enum State { Home, Held, Settling, Dropped, Returning }

        const float HoverTint = 0.35f, ReturnSeconds = 0.55f, MaxReach = 6f, StillSpeed = 0.03f;

        [SerializeField] string displayName;
        [SerializeField] Transform grip;

        Vector3 homePosition;
        Quaternion homeRotation = Quaternion.identity;
        bool homeSet;
        Rigidbody body;
        XRGrabInteractable grab;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly Dictionary<Renderer, Color> baseColors = new Dictionary<Renderer, Color>();
        MaterialPropertyBlock block;
        int hovers;
        bool desktopHover;
        float highlight;
        Vector3 fromPosition;
        Quaternion fromRotation;
        float moveTime, moveDuration;
        float idle;
        float floorY;
        IXRSelectInteractor holder;
        Transform desktopAnchor;
        GuidedTreatmentConfig config;

        public State Current { get; private set; } = State.Home;
        public bool IsHeld => Current == State.Held;
        public bool AtHome => Current == State.Home;
        /// <summary>"left", "right", "mouse" (desktop) or null.</summary>
        public string HeldBy { get; private set; }
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Vector3 HomePosition => homePosition;
        public Quaternion HomeRotation => homeRotation;
        public XRGrabInteractable Grab => grab;
        public Transform Grip => grip;
        public bool DesktopHeld => desktopAnchor != null;

        public event Action<TreatmentGrabbable> Grabbed;
        public event Action<TreatmentGrabbable> Released;
        /// <summary>The holder pressed (true) or released (false) its use input: (source, down).</summary>
        public event Action<TreatmentGrabbable, string, bool> Used;
        /// <summary>The item is back in its holder.</summary>
        public event Action<TreatmentGrabbable> ReturnedHome;

        /// <summary>Makes <paramref name="target"/> grabbable (re-using its XRGrabInteractable and rigidbody when it has them).</summary>
        public static TreatmentGrabbable Attach(GameObject target, string label, Transform gripPose, float floorHeight)
        {
            var item = target.GetComponent<TreatmentGrabbable>();
            if (item == null)
                item = target.AddComponent<TreatmentGrabbable>();
            item.displayName = label;
            item.grip = gripPose;
            item.floorY = floorHeight;
            item.Initialise();
            return item;
        }

        void Initialise()
        {
            config = GuidedTreatmentConfig.Load();
            body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.mass = 0.4f;
            if (GetComponentInChildren<Collider>() == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                var bounds = LocalBounds();
                box.center = bounds.center;
                box.size = Vector3.Max(bounds.size, Vector3.one * 0.02f);
            }

            grab = GetComponent<XRGrabInteractable>();
            if (grab == null)
                grab = gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.smoothPosition = true;
            grab.smoothPositionAmount = 22f;
            grab.tightenPosition = 0.6f;
            grab.smoothRotation = true;
            grab.smoothRotationAmount = 22f;
            grab.tightenRotation = 0.6f;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = false;
            grab.retainTransformParent = false;
            if (grip != null)
                grab.attachTransform = grip;
            ControllerGripSelectFilter.AddTo(grab);
            grab.selectFilters.Remove(DesktopRayRejectFilter.Instance);
            grab.selectFilters.Add(DesktopRayRejectFilter.Instance);
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
            grab.hoverEntered.AddListener(_ => hovers++);
            grab.hoverExited.AddListener(_ => hovers = Mathf.Max(0, hovers - 1));
            grab.activated.AddListener(args => Used?.Invoke(this, GuidedInputGate.SourceOf(args.interactorObject), true));
            grab.deactivated.AddListener(args => Used?.Invoke(this, GuidedInputGate.SourceOf(args.interactorObject), false));

            renderers.Clear();
            renderers.AddRange(GetComponentsInChildren<Renderer>(true));
            renderers.RemoveAll(r => r is ParticleSystemRenderer);
            foreach (var r in renderers)
            {
                var material = r.sharedMaterial;
                baseColors[r] = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            }

            IgnoreRigs();
            if (!homeSet)
                SetHome(transform.position, transform.rotation);
        }

        Bounds LocalBounds()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var first = true;
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var b = filter.sharedMesh.bounds;
                for (var c = 0; c < 8; c++)
                {
                    var p = transform.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z)));
                    if (first)
                        bounds = new Bounds(p, Vector3.zero);
                    else
                        bounds.Encapsulate(p);
                    first = false;
                }
            }

            return bounds;
        }

        void IgnoreRigs()
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            foreach (var rig in FindObjectsByType<CharacterController>(FindObjectsInactive.Include))
                foreach (var c in colliders)
                    Physics.IgnoreCollision(rig, c, true);
        }

        /// <summary>The holder pose (world). When the item is home it moves there now.</summary>
        public void SetHome(Vector3 position, Quaternion rotation)
        {
            homePosition = position;
            homeRotation = rotation;
            homeSet = true;
            if (Current == State.Home)
                transform.SetPositionAndRotation(position, rotation);
        }

        public void SetFloor(float y) => floorY = y;

        // ------------------------------------------------------------------ XR

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            holder = args.interactorObject;
            BeginHold(GuidedInputGate.SourceOf(holder));
            Haptic(0.3f, 0.07f);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.isCanceled && Current != State.Held)
                return;
            Haptic(0.18f, 0.05f);
            holder = null;
            EndHold();
        }

        void BeginHold(string source)
        {
            Current = State.Held;
            HeldBy = source;
            body.isKinematic = true;
            body.useGravity = false;
            Grabbed?.Invoke(this);
        }

        void EndHold()
        {
            HeldBy = null;
            desktopAnchor = null;
            if (GuidedGeometry.ReleaseSettlesHome(transform.position, homePosition, config.homeSnapDistance))
                MoveHome(config.homeSettleSeconds, State.Settling);
            else
                Drop();
            Released?.Invoke(this);
        }

        void Drop()
        {
            Current = State.Dropped;
            idle = 0f;
            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>A short pulse on the holding controller (nothing for hands, the mouse or tests).</summary>
        public void Haptic(float amplitude, float duration)
        {
            if (holder is XRBaseInputInteractor input && VrControllerRig.IsControllerInteractor(input))
                input.SendHapticImpulse(amplitude, duration);
        }

        /// <summary>Drops the item from the XR hand that holds it (if any) and sends it home.</summary>
        public void ReturnHome(bool instant = false)
        {
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            holder = null;
            HeldBy = null;
            desktopAnchor = null;
            if (instant)
            {
                ArriveHome();
                return;
            }

            MoveHome(ReturnSeconds, State.Returning);
        }

        void MoveHome(float duration, State state)
        {
            body.isKinematic = true;
            body.useGravity = false;
            fromPosition = transform.position;
            fromRotation = transform.rotation;
            moveTime = 0f;
            moveDuration = duration;
            Current = state;
        }

        void ArriveHome()
        {
            body.isKinematic = true;
            body.useGravity = false;
            transform.SetPositionAndRotation(homePosition, homeRotation);
            Current = State.Home;
            ReturnedHome?.Invoke(this);
        }

        // ------------------------------------------------------------------ desktop

        /// <summary>Desktop: the item follows <paramref name="anchor"/> (the mouse hand) until <see cref="DesktopRelease"/>.</summary>
        public void DesktopHold(Transform anchor)
        {
            if (anchor == null)
                return;
            ReturnHomeCancelXr();
            desktopAnchor = anchor;
            BeginHold(GuidedInputGate.Mouse);
        }

        void ReturnHomeCancelXr()
        {
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
        }

        public void DesktopRelease()
        {
            if (desktopAnchor == null)
                return;
            EndHold();
        }

        /// <summary>Desktop use key (Space) while held: down / up.</summary>
        public void DesktopUse(bool down) => Used?.Invoke(this, GuidedInputGate.Key, down);

        public void SetDesktopHover(bool on) => desktopHover = on;

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            var dt = Time.deltaTime;
            switch (Current)
            {
                case State.Held when desktopAnchor != null:
                {
                    var blend = 1f - Mathf.Exp(-20f * dt);
                    transform.SetPositionAndRotation(Vector3.Lerp(transform.position, desktopAnchor.position, blend),
                        Quaternion.Slerp(transform.rotation, desktopAnchor.rotation, blend));
                    break;
                }
                case State.Settling:
                case State.Returning:
                {
                    moveTime += dt;
                    GuidedGeometry.SettlePose(fromPosition, fromRotation, homePosition, homeRotation, moveTime, moveDuration, out var p, out var r);
                    transform.SetPositionAndRotation(p, r);
                    if (moveTime >= moveDuration)
                        ArriveHome();
                    break;
                }
                case State.Dropped:
                {
                    var moving = body.linearVelocity.magnitude > StillSpeed || body.angularVelocity.magnitude > 0.2f;
                    idle = moving ? 0f : idle + dt;
                    if (GuidedGeometry.ShouldAutoReturn(idle, config.idleReturnSeconds, transform.position, floorY, homePosition, MaxReach))
                        MoveHome(ReturnSeconds, State.Returning);
                    break;
                }
            }

            UpdateHighlight(dt);
        }

        void UpdateHighlight(float dt)
        {
            var target = (hovers > 0 || desktopHover) && Current != State.Held ? 1f : 0f;
            if (Mathf.Approximately(target, highlight) && (target == 0f))
                return;
            highlight = Mathf.MoveTowards(highlight, target, dt / 0.15f);
            block ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                if (highlight <= 0.001f)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }

                r.GetPropertyBlock(block);
                var baseColor = baseColors.TryGetValue(r, out var c) ? c : Color.white;
                var tint = Color.Lerp(baseColor, new Color(0.7f, 1f, 1f, baseColor.a), HoverTint * highlight);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_EmissionColor", UIPalette() * (0.25f * highlight));
                r.SetPropertyBlock(block);
            }
        }

        static Color UIPalette() => BTP.UI.UIPalette.Cyan;

        /// <summary>Hand-held tools use their own desktop hold (<see cref="DesktopToolHand"/>): the desktop mouse ray never selects them.</summary>
        sealed class DesktopRayRejectFilter : IXRSelectFilter
        {
            public static readonly DesktopRayRejectFilter Instance = new DesktopRayRejectFilter();
            public bool canProcess => true;
            public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => !DesktopDemoControls.IsMouseRay(interactor);
        }
    }
}
