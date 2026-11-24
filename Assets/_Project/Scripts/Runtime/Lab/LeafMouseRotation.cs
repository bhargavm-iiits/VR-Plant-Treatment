using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using BTP.Core;
using BTP.Treatment;

namespace BTP.Lab
{
    /// <summary>
    /// The Lab leaf's handling. Desktop: drag it with the left mouse button (yaw and a clamped pitch).
    /// <para>
    /// VR: the leaf is picked up exactly like the Treatment lab's scoop and spray bottle. Its centred pivot carries a collider
    /// fitted to the leaf and a <see cref="TreatmentGrabbable"/>, so the same <see cref="ToolGripToggle"/> (created in the
    /// Lab by the leaf's registration) handles it: point at the leaf or reach it, press the GRIP once and it comes into
    /// the hand, held by its stem, and stays there with the grip let go; it moves and turns freely with the hand (turn
    /// the wrist to see the back, bring it close to inspect). The next GRIP press puts it back: it eases home to the stand
    /// in its original orientation. A tracked hand's pinch holds it the same way. Hover shows a cyan ring and a label
    /// (<see cref="LeafGrabCue"/>); grab and release pulse the controller. The weekly recovery keeps rendering on the
    /// held leaf; a new leaf on the stand (another disease or plant) sends the held one home first.
    /// "[BTP] LeafTurn:" and "[BTP] Grip toggle:" lines trace it in the device log.
    /// </para>
    /// </summary>
    public sealed class LeafMouseRotation : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float degreesPerPixel = 0.35f;
        Transform pivot;
        Renderer[] targets;
        Vector2 previousPointer;
        float yaw;
        float pitch;
        readonly List<RaycastResult> uiHits = new();
        bool mouseDragging;
        const float PitchLimit = 65f;
        /// <summary>Thinnest the grab collider gets (m), so an edge-on leaf stays easy to point at, and its padding (m).</summary>
        public const float MinThickness = 0.04f, ColliderPadding = 0.01f;
        BoxCollider grabCollider;
        Transform grip;
        TreatmentGrabbable grabbable;
        LeafGrabCue cue;
        float cueRadius;
        Vector3 localSize;

        /// <summary>True while a mouse drag is turning the leaf.</summary>
        public bool IsDragging => mouseDragging;

        /// <summary>True while the leaf is away from its stand (held, or easing back).</summary>
        public bool IsHeld => grabbable != null && !grabbable.AtHome;

        /// <summary>True while a controller, hand or the mouse ray points at the leaf on its stand.</summary>
        public bool IsHovered => grabbable != null && grabbable.Grab != null && grabbable.Grab.isHovered && grabbable.AtHome;

        internal BoxCollider GrabCollider => grabCollider;
        internal TreatmentGrabbable Grabbable => grabbable;
        internal Transform Grip => grip;
        internal LeafGrabCue Cue => cue;
        internal Transform Pivot => pivot;

        void Awake() => Initialize();

        internal void Initialize()
        {
            if (pivot != null) return;
            targets = GetComponentsInChildren<Renderer>(true);
            if (targets.Length == 0) return;
            var bounds = new Bounds();
            bool first = true;
            foreach (var renderer in targets)
            {
                var local = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var point = transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            var children = new Transform[transform.childCount];
            for (int i = 0; i < children.Length; i++) children[i] = transform.GetChild(i);
            pivot = new GameObject("Leaf Spin Pivot").transform;
            pivot.gameObject.layer = 0; // Default: the layer the controller rays, near casts and the grip toggle's sweep test.
            pivot.SetParent(transform, false);
            pivot.localPosition = bounds.center;
            foreach (var child in children) child.SetParent(pivot, true);
            localSize = bounds.size;
            AddCollider();
            if (Application.isPlaying)
                AttachGrab();
        }

        /// <summary>
        /// The collider round the leaf (on the pivot, so it moves with it) and the grip: the stem end (the bottom of the leaf),
        /// facing so the held leaf points forward from the hand with its front up towards the eyes.
        /// </summary>
        void AddCollider()
        {
            var scale = Mathf.Max(Mathf.Abs(pivot.lossyScale.x), 1e-4f);
            grabCollider = pivot.gameObject.AddComponent<BoxCollider>();
            grabCollider.size = FittedColliderSize(localSize, scale);
            cueRadius = 0.5f * Mathf.Max(localSize.x, localSize.y) * scale * 1.08f + 0.03f;
            grip = new GameObject("Leaf Grip").transform;
            grip.SetParent(pivot, false);
            grip.localPosition = new Vector3(0f, -0.5f * localSize.y, 0f);
            grip.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
        }

        /// <summary>The grab collider's local size: the leaf's bounds plus padding, never thinner than <see cref="MinThickness"/> (world metres / <paramref name="scale"/>).</summary>
        internal static Vector3 FittedColliderSize(Vector3 localSize, float scale)
        {
            scale = Mathf.Max(scale, 1e-4f);
            var pad = 2f * ColliderPadding / scale;
            var min = MinThickness / scale;
            return new Vector3(Mathf.Max(localSize.x + pad, min), Mathf.Max(localSize.y + pad, min), Mathf.Max(localSize.z + pad, min));
        }

        /// <summary>
        /// Makes the leaf a hand-held item of the grip toggle (as the scoop and the spray bottle). Unlike a tool it stays under
        /// its model while held, so a new leaf on the stand takes the old one (and its toggle) away with it.
        /// </summary>
        internal void AttachGrab()
        {
            if (pivot == null || grabbable != null)
                return;
            grabbable = TreatmentGrabbable.Attach(pivot.gameObject, "Lab leaf " + name, grip, 0f);
            grabbable.Grab.unparentTransformOnGrab = false;
            grabbable.Grab.retainTransformParent = true;
            grabbable.Grabbed += OnGrabbed;
            grabbable.Released += OnReleased;
            grabbable.ReturnedHome += OnReturnedHome;
            if (Application.isPlaying)
            {
                cue = new LeafGrabCue(transform, cueRadius);
                Debug.Log($"[BTP] LeafTurn: grip-toggle leaf ready on '{name}' (collider {grabCollider.size * Mathf.Abs(pivot.lossyScale.x)} m, " +
                          $"centre {pivot.position}); grip toggle in scene '{gameObject.scene.name}'.");
            }
        }

        void OnGrabbed(TreatmentGrabbable item)
        {
            mouseDragging = false;
            Debug.Log($"[BTP] LeafTurn: picked up by {item.HeldBy ?? "a hand"} (grip toggle).");
        }

        void OnReleased(TreatmentGrabbable item) => Debug.Log("[BTP] LeafTurn: let go; easing back to the stand.");

        /// <summary>Back on the stand in its original orientation: the mouse drag starts again from there.</summary>
        void OnReturnedHome(TreatmentGrabbable item)
        {
            yaw = 0f;
            pitch = 0f;
            Debug.Log("[BTP] LeafTurn: back on the stand.");
        }

        void OnDisable()
        {
            mouseDragging = false;
            // A new leaf replaces this one (another disease or plant): put this one back first, out of the hand.
            if (grabbable != null && !grabbable.AtHome)
                grabbable.ReturnHome(true);
        }

        void OnDestroy() => cue?.Destroy();

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || mouse.rightButton.isPressed || !Application.isFocused)
            {
                mouseDragging = false;
                return;
            }
            var position = mouse.position.ReadValue();
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            ProcessPointer(Camera.main, position, pressed, mouse.leftButton.isPressed, pressed && OverUI(position));
        }

        void LateUpdate()
        {
            if (cue == null || pivot == null)
                return;
            var eye = Camera.main != null ? Camera.main.transform.position : pivot.position + Vector3.back;
            cue.Tick(IsHovered, false, MouseHovering(), pivot.position, eye, Time.unscaledDeltaTime);
        }

        bool MouseHovering()
        {
            if (grabbable == null || grabbable.Grab == null)
                return false;
            foreach (var hovering in grabbable.Grab.interactorsHovering)
                if (DesktopDemoControls.IsMouseRay(hovering))
                    return true;
            return false;
        }

        bool OverUI(Vector2 position)
        {
            var events = EventSystem.current;
            if (events == null) return false;
            uiHits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = position }, uiHits);
            foreach (var hit in uiHits)
                if (hit.gameObject != null && hit.gameObject.GetComponentInParent<Canvas>() != null) return true;
            return false;
        }

        internal bool HitsLeaf(Camera camera, Vector2 position)
        {
            if (camera == null || targets == null) return false;
            return HitsLeaf(camera.ScreenPointToRay(position));
        }

        /// <summary>True when the world-space ray passes through any visible leaf renderer's bounds.</summary>
        internal bool HitsLeaf(Ray ray)
        {
            if (targets == null) return false;
            foreach (var renderer in targets)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var localRay = new Ray(renderer.transform.InverseTransformPoint(ray.origin),
                    renderer.transform.InverseTransformVector(ray.direction));
                if (renderer.localBounds.IntersectRay(localRay)) return true;
            }
            return false;
        }

        internal void ProcessPointer(Camera camera, Vector2 position, bool pressed, bool held, bool overUI)
        {
            if (pivot == null || camera == null || !held)
            {
                mouseDragging = false;
                return;
            }
            if (pressed)
            {
                // A leaf in a hand (or easing back) is not dragged.
                mouseDragging = !overUI && !IsHeld && HitsLeaf(camera, position);
                previousPointer = position;
                return;
            }
            if (!mouseDragging) return;
            var delta = position - previousPointer;
            previousPointer = position;
            yaw = Mathf.Repeat(yaw - delta.x * degreesPerPixel, 360f);
            pitch = Mathf.Clamp(pitch + delta.y * degreesPerPixel, -PitchLimit, PitchLimit);
            pivot.localRotation = Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
        }

        /// <summary>
        /// How the aim direction turned between two frames: the yaw about world up (positive = turned right) and the change of
        /// elevation (positive = aimed up), both in degrees. A vertical aim has no yaw.
        /// </summary>
        internal static void AimDelta(Vector3 previousAim, Vector3 currentAim, out float yawDegrees, out float pitchDegrees)
        {
            var previousFlat = Vector3.ProjectOnPlane(previousAim, Vector3.up);
            var currentFlat = Vector3.ProjectOnPlane(currentAim, Vector3.up);
            const float MinFlat = 1e-4f;
            yawDegrees = previousFlat.sqrMagnitude < MinFlat || currentFlat.sqrMagnitude < MinFlat
                ? 0f
                : Vector3.SignedAngle(previousFlat, currentFlat, Vector3.up);
            pitchDegrees = Elevation(currentAim) - Elevation(previousAim);
        }

        static float Elevation(Vector3 direction) =>
            Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;

        /// <summary>The mouse mapping for an aim change: turning right spins the leaf's front to the right; the pitch is clamped.</summary>
        internal static void AccumulateRotation(ref float yaw, ref float pitch, float aimYaw, float aimPitch, float gain)
        {
            yaw = Mathf.Repeat(yaw - aimYaw * gain, 360f);
            pitch = Mathf.Clamp(pitch + aimPitch * gain, -PitchLimit, PitchLimit);
        }
    }
}
