using System;
using System.Collections.Generic;
using BTP.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BTP.Treatment
{
    /// <summary>
    /// The desktop demo's hand for the treatment tools (only while <see cref="DesktopDemoControls"/> drives the rig): a
    /// left click on a tool picks it up (the XR mouse ray never grabs tools), then it follows the mouse. Pouring tools
    /// (packets, scoop) hover just above whatever the cursor points at (the chamber soil plane when the cursor is over the
    /// tray), the sprayer is held in front of the eye with its nozzle along the cursor ray. Space uses the tool (spray /
    /// tip the scoop to pour), Z / X tilt it, the wheel changes the reach, G puts it back in its holder. A click is never
    /// taken while the cursor is on a world UI (the guide's OK, the kiosk) or while the popup consumes the press.
    /// </summary>
    public sealed class DesktopToolHand : MonoBehaviour
    {
        const float HoverAbove = 0.06f, MinReach = 0.3f, MaxReach = 2.2f, TiltSpeed = 120f, PourRoll = 115f;

        DesktopDemoControls desktop;
        Camera eye;
        Transform anchor;
        TreatmentGrabbable held;
        TreatmentGrabbable hovered;
        readonly List<TreatmentGrabbable> items = new List<TreatmentGrabbable>();
        float reach = 0.7f;
        float roll;
        bool useDown;

        /// <summary>For a pouring tool, an optional plane point under the cursor (the chamber soil): (ray, out point) → true when it applies.</summary>
        public Func<Ray, Vector3?> SurfaceOverride;
        /// <summary>Items aimed along the cursor ray from the eye (the sprayer), with the local forward to align.</summary>
        readonly Dictionary<TreatmentGrabbable, Transform> aimers = new Dictionary<TreatmentGrabbable, Transform>();

        public TreatmentGrabbable Held => held;
        /// <summary>Verification: the real mouse and keyboard are ignored; <see cref="Hold"/> and <see cref="AimAlong"/> drive the hand.</summary>
        public bool Scripted { get; set; }
        public bool Active => desktop != null && desktop.IsActive;
        public bool UseDown => useDown;
        public Transform Anchor => anchor;

        public void Register(TreatmentGrabbable item, Transform aimForward = null)
        {
            if (item == null || items.Contains(item))
                return;
            items.Add(item);
            if (aimForward != null)
                aimers[item] = aimForward;
        }

        void Awake()
        {
            anchor = new GameObject("DesktopToolAnchor").transform;
            anchor.SetParent(transform, false);
        }

        void Update()
        {
            if (desktop == null)
                desktop = FindAnyObjectByType<DesktopDemoControls>();
            if (eye == null || !eye.isActiveAndEnabled)
                eye = Camera.main;
            if (!Active || eye == null)
            {
                if (held != null)
                    Drop(true);
                SetHover(null);
                return;
            }

            // Verification drives the hand itself (Hold / AimAlong) instead of the real mouse.
            if (Scripted)
            {
                if (held != null && held.Current != TreatmentGrabbable.State.Held)
                    held = null;
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null)
                return;
            var ray = eye.ScreenPointToRay(mouse.position.ReadValue());
            useDown = keyboard != null && keyboard.spaceKey.isPressed;

            if (held == null)
            {
                var target = Pick(ray);
                SetHover(target);
                if (target != null && mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.isPressed && !OverUi() &&
                    GuidedInputGate.Allows(GuidedInputGate.Mouse))
                    Hold(target);
                return;
            }

            SetHover(null);
            if (held.Current != TreatmentGrabbable.State.Held)
            {
                held = null;
                return;
            }

            if (mouse.scroll.ReadValue().y != 0f)
                reach = Mathf.Clamp(reach + Mathf.Sign(mouse.scroll.ReadValue().y) * 0.08f, MinReach, MaxReach);
            if (keyboard != null)
            {
                if (keyboard.zKey.isPressed)
                    roll -= TiltSpeed * Time.deltaTime;
                if (keyboard.xKey.isPressed)
                    roll += TiltSpeed * Time.deltaTime;
                if (keyboard.spaceKey.wasPressedThisFrame)
                    held.DesktopUse(true);
                if (keyboard.spaceKey.wasReleasedThisFrame)
                    held.DesktopUse(false);
                if (keyboard.gKey.wasPressedThisFrame)
                {
                    Drop(true);
                    return;
                }
            }

            PoseAnchor(ray);
        }

        void PoseAnchor(Ray ray)
        {
            if (aimers.TryGetValue(held, out var forward) && forward != null)
            {
                // Held in front of the eye, a little low and right; the nozzle points along the cursor ray.
                var position = eye.transform.position + ray.direction * Mathf.Clamp(reach, MinReach, 0.55f) - eye.transform.up * 0.1f + eye.transform.right * 0.08f;
                var local = Quaternion.Inverse(held.transform.rotation) * forward.rotation;
                var rotation = Quaternion.LookRotation(ray.direction, Vector3.up) * Quaternion.Inverse(local);
                anchor.SetPositionAndRotation(position, rotation);
                return;
            }

            Vector3 point;
            var surface = SurfaceOverride?.Invoke(ray);
            if (surface.HasValue)
                point = surface.Value + Vector3.up * HoverAbove;
            else if (Physics.Raycast(ray, out var hit, MaxReach, ~(1 << 2), QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(held.transform))
                point = hit.point + Vector3.up * HoverAbove;
            else
                point = ray.origin + ray.direction * reach;
            // The scoop tips along its length (bowl down) when Space is held or with Z / X; packets just tilt.
            var target = useDown ? PourRoll : 0f;
            var tilt = Mathf.Clamp(roll + target, -150f, 150f);
            var yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(ray.direction, Vector3.up).sqrMagnitude > 1e-6f
                ? Vector3.ProjectOnPlane(ray.direction, Vector3.up).normalized
                : Vector3.forward, Vector3.up);
            // The item's +X (scoop bowl) points away from the viewer; tilting rolls it about the view's right axis.
            var basePose = yaw * Quaternion.Euler(0f, -90f, 0f);
            anchor.SetPositionAndRotation(point, Quaternion.AngleAxis(-tilt, yaw * Vector3.right) * basePose);
        }

        TreatmentGrabbable Pick(Ray ray)
        {
            if (!Physics.Raycast(ray, out var hit, MaxReach + 1f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                return null;
            var item = hit.collider.GetComponentInParent<TreatmentGrabbable>();
            return item != null && items.Contains(item) && item.isActiveAndEnabled ? item : null;
        }

        bool OverUi()
        {
            var ray = desktop != null ? desktop.MouseRay : null;
            return ray != null && ray.TryGetCurrentUIRaycastResult(out _);
        }

        void SetHover(TreatmentGrabbable item)
        {
            if (hovered == item)
                return;
            if (hovered != null)
                hovered.SetDesktopHover(false);
            hovered = item;
            if (hovered != null)
                hovered.SetDesktopHover(true);
        }

        /// <summary>Picks <paramref name="item"/> up (verification calls this to simulate the click).</summary>
        public void Hold(TreatmentGrabbable item)
        {
            if (item == null)
                return;
            if (held != null)
                Drop(true);
            roll = 0f;
            reach = Mathf.Clamp(Vector3.Distance(eye != null ? eye.transform.position : item.transform.position, item.transform.position), MinReach, 1.2f);
            anchor.SetPositionAndRotation(item.transform.position, item.transform.rotation);
            held = item;
            item.DesktopHold(anchor);
        }

        /// <summary>Lets go: home when <paramref name="putBack"/> (G), else released where it is (it settles or drops).</summary>
        public void Drop(bool putBack)
        {
            if (held == null)
                return;
            var item = held;
            held = null;
            if (useDown)
                item.DesktopUse(false);
            item.DesktopRelease();
            if (putBack)
                item.ReturnHome();
        }

        /// <summary>Verification: places the desktop hand (and the held tool) at a pose.</summary>
        public void SetAnchor(Vector3 position, Quaternion rotation) => anchor.SetPositionAndRotation(position, rotation);

        /// <summary>Verification: points the hand like the mouse would along <paramref name="ray"/> with Space up/down.</summary>
        public void AimAlong(Ray ray, bool use)
        {
            if (held == null || eye == null)
                return;
            if (use != useDown)
                held.DesktopUse(use);
            useDown = use;
            PoseAnchor(ray);
        }
    }
}
