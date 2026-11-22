using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using BTP.Core;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Lab
{
    /// <summary>
    /// Drag the leaf with the left mouse button, or point a controller at it and hold the trigger (turning the wrist spins it;
    /// the dragging hand's thumbstick also spins it). The controls and the specimen centre stay fixed.
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
        const float StickYawDegreesPerSecond = 120f, StickPitchDegreesPerSecond = 90f, StickDeadzone = 0.2f;
        const int InteractorRefreshFrames = 90;
        [SerializeField, Min(0.1f)] float controllerGain = 3f;
        readonly List<NearFarInteractor> controllerInteractors = new();
        NearFarInteractor controllerOwner;
        Vector3 previousAim;
        int nextInteractorRefresh;

        /// <summary>True while a mouse drag or a controller drag is turning the leaf.</summary>
        public bool IsDragging => mouseDragging || controllerOwner != null;

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
            pivot.SetParent(transform, false);
            pivot.localPosition = bounds.center;
            foreach (var child in children) child.SetParent(pivot, true);
        }

        void OnDisable()
        {
            mouseDragging = false;
            controllerOwner = null;
        }

        void Update()
        {
            UpdateControllers(Time.deltaTime);
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
                mouseDragging = !overUI && HitsLeaf(camera, position);
                previousPointer = position;
                return;
            }
            if (!mouseDragging) return;
            var delta = position - previousPointer;
            previousPointer = position;
            yaw = Mathf.Repeat(yaw - delta.x * degreesPerPixel, 360f);
            pitch = Mathf.Clamp(pitch + delta.y * degreesPerPixel, -PitchLimit, PitchLimit);
            ApplyRotation();
        }

        void ApplyRotation() =>
            pivot.localRotation = Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);

        // ------------------------------------------------------------------ controllers

        void RefreshControllerInteractors()
        {
            controllerInteractors.Clear();
            foreach (var interactor in FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None))
                if (VrControllerRig.IsControllerInteractor(interactor))
                    controllerInteractors.Add(interactor);
        }

        void UpdateControllers(float deltaTime)
        {
            if (pivot == null) return;
            if (Time.frameCount >= nextInteractorRefresh)
            {
                nextInteractorRefresh = Time.frameCount + InteractorRefreshFrames;
                RefreshControllerInteractors();
            }

            if (controllerOwner != null)
            {
                var held = ControllerButtons.Find(controllerOwner.handedness, ControllerButtons.Trigger);
                if (!controllerOwner.isActiveAndEnabled || held == null || !held.isPressed)
                {
                    controllerOwner = null;
                    return;
                }

                var aim = controllerOwner.transform.forward;
                AimDelta(previousAim, aim, out var aimYaw, out var aimPitch);
                previousAim = aim;
                AccumulateRotation(ref yaw, ref pitch, aimYaw, aimPitch, controllerGain);
                var stick = ControllerButtons.Stick(controllerOwner.handedness);
                if (Mathf.Abs(stick.x) > StickDeadzone)
                    yaw = Mathf.Repeat(yaw - stick.x * StickYawDegreesPerSecond * deltaTime, 360f);
                if (Mathf.Abs(stick.y) > StickDeadzone)
                    pitch = Mathf.Clamp(pitch + stick.y * StickPitchDegreesPerSecond * deltaTime, -PitchLimit, PitchLimit);
                ApplyRotation();
                return;
            }

            foreach (var interactor in controllerInteractors)
            {
                if (interactor == null || !interactor.isActiveAndEnabled) continue;
                var trigger = ControllerButtons.Find(interactor.handedness, ControllerButtons.Trigger);
                if (trigger == null || !trigger.wasPressedThisFrame) continue;
                if (interactor.TryGetCurrentUIRaycastResult(out _)) continue;
                var origin = interactor.transform;
                if (!HitsLeaf(new Ray(origin.position, origin.forward))) continue;
                controllerOwner = interactor;
                previousAim = origin.forward;
                return;
            }
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
