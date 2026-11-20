using System.Collections.Generic;
using BTP.Core;
using BTP.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;

namespace BTP.Treatment
{
    /// <summary>
    /// The chamber's front glass door as a real hinged door for the user (Play mode; added by <see cref="TreatmentChamber"/>).
    /// When the head (the main camera) is within <see cref="ChamberDoorLogic.Range"/> of the door front and in front of it, a
    /// GTA-style interaction pop-up (<see cref="ChamberDoorPopup"/>: a round "A" button glyph and the word "Open" / "Close";
    /// an "O" keycap in the desktop demo) pops in beside the handle at about eye height and the handle glows faintly. Then
    /// the RIGHT controller's A button (<see cref="ChamberDoorPrompt.ToggleHand"/>; O on the keyboard without a headset)
    /// swings the leaf open or closed about its left hinge line: <see cref="ChamberDoorLogic.OpenAngle"/> outwards over
    /// <see cref="ChamberDoorLogic.SwingSeconds"/> with an ease-in-out, less when a station base or the hall wall is in the
    /// way (<see cref="SetSurroundings"/>). The handle itself is pressable too (controller ray trigger or grip, poke, hand
    /// pinch, the desktop mouse). A is only read while the user stands at the door (the rig's jump on A is paused
    /// meanwhile); X, Y (menu) and B (back) are never touched. The door collider is solid only while the leaf is fully closed,
    /// and nothing on the swinging leaf can block the rig. A hands-on station that retracts the whole front
    /// (<see cref="TreatmentChamber.SetFrontOpen"/>) overrides the user; the user's choice returns when it releases.
    /// </summary>
    public sealed class ChamberDoor : MonoBehaviour
    {
        const float PromptOut = 0.14f;
        const float FadeSeconds = 0.25f, ToggleCooldown = 0.3f, HapticAmplitude = 0.3f, HapticDuration = 0.07f;

        TreatmentChamber chamber;
        readonly ChamberDoorState state = new ChamberDoorState();
        float openAngle = ChamberDoorLogic.OpenAngle;
        readonly List<Vector2[]> obstacles = new List<Vector2[]>();
        readonly List<Vector2> hall = new List<Vector2>();

        Camera head;
        DesktopDemoControls desktop;
        VrControllerRig controllerRig;
        float lookupTimer;
        float lastToggle = -10f;

        ChamberDoorPopup popup;

        // Handle.
        Transform handle;
        BoxCollider handleCollider;
        XRSimpleInteractable handleInteractable;
        MeshRenderer handleRenderer;
        MaterialPropertyBlock handleBlock;
        Color handleBase = Color.grey;
        float glow;

        readonly List<CharacterController> rigs = new List<CharacterController>();
        float ignoreTimer;
        readonly List<Behaviour> pausedJumps = new List<Behaviour>();

        /// <summary>True while the user stands at the door (in range and in front of it).</summary>
        public bool UserInRange { get; private set; }
        /// <summary>The user's choice (kept while a station retracts the front).</summary>
        public bool UserOpen => state.UserOpen;
        /// <summary>True while the leaf swings.</summary>
        public bool Moving => state.Moving;
        /// <summary>How far the leaf opens here (less than <see cref="ChamberDoorLogic.OpenAngle"/> when something is in the way).</summary>
        public float OpenAngle => openAngle;
        /// <summary>True while the interaction pop-up is on screen (also while it pops in or out).</summary>
        public bool PromptShowing => popup != null && popup.Visible;
        /// <summary>The pop-up's action label ("Open" / "Close"); null before it first shows.</summary>
        public string PromptLabel => popup?.Label;
        /// <summary>The pop-up's button glyph ("A" / "O"); null before it first shows.</summary>
        public string PromptGlyph => popup?.Glyph;
        /// <summary>False when no useful swing fits between the chamber and the stations (the door then stays shut).</summary>
        public bool Available => openAngle >= ChamberDoorLogic.MinUsefulAngle;

        internal void Bind(TreatmentChamber owner)
        {
            chamber = owner;
            SetupHandle();
        }

        void OnDisable()
        {
            PauseJumps(false);
            popup?.Hide();
            glow = 0f;
            ApplyGlow();
        }

        void OnDestroy()
        {
            if (handleInteractable != null)
                handleInteractable.selectEntered.RemoveListener(OnHandleSelected);
            popup?.Destroy();
            popup = null;
        }

        // ------------------------------------------------------------------ surroundings

        /// <summary>
        /// The station bases (convex floor polygons, world X/Z) and the hall floor (convex, world X/Z) around the chamber: the
        /// outward swing is cut to the largest angle that keeps the leaf <see cref="ChamberDoorLogic.SwingMargin"/> clear of all.
        /// </summary>
        public void SetSurroundings(IReadOnlyList<Vector2[]> bases, IReadOnlyList<Vector2> hallFloor)
        {
            obstacles.Clear();
            if (bases != null)
                obstacles.AddRange(bases);
            hall.Clear();
            if (hallFloor != null)
                hall.AddRange(hallFloor);
            RefreshClearance();
        }

        void RefreshClearance()
        {
            if (chamber == null || !chamber.HasLayout)
                return;
            openAngle = ChamberDoorLogic.ClearAngle(Flat(chamber.DoorHingeWorld), Flat(chamber.DoorEdgeWorld), ChamberDoorLogic.OpenAngle, obstacles, hall);
            if (!Available)
            {
                state.SetUserOpen(false);
                Debug.LogWarning($"[BTP] Chamber door: only {openAngle:0} deg of swing is clear of the stations and walls; the door stays shut.");
            }
            else if (openAngle < ChamberDoorLogic.OpenAngle)
                Debug.Log($"[BTP] Chamber door: swing limited to {openAngle:0} deg (a station base or wall is in the way of {ChamberDoorLogic.OpenAngle:0}).");
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (chamber == null || !chamber.HasLayout)
                return;
            var deltaTime = Time.deltaTime;
            Lookups(deltaTime);

            state.SetRetracted(chamber.FrontOpen);
            var desktopMode = desktop != null && desktop.IsActive;
            var menuOpen = controllerRig != null && controllerRig.IsMenuOpen;
            UserInRange = Available && !state.Retracted && !menuOpen && head != null && HeadInRange(head.transform.position);

            // The right A belongs to the door only while the user stands at it (the rig's jump on A pauses meanwhile).
            PauseJumps(UserInRange && !desktopMode);
            if (UserInRange)
            {
                if (desktopMode)
                {
                    var keyboard = Keyboard.current;
                    if (keyboard != null && keyboard.oKey.wasPressedThisFrame)
                        Toggle(InteractorHandedness.None, "keyboard O");
                }
                else if (PrimaryPressed(ChamberDoorPrompt.ToggleHand))
                    Toggle(ChamberDoorPrompt.ToggleHand, "right A");
            }

            state.Tick(deltaTime);
            chamber.SetDoorSwing(ChamberDoorLogic.Angle(state.Progress, openAngle));
            if (chamber.GlassPhysics != null)
                chamber.GlassPhysics.SetDoorSwung(!state.FullyClosed);

            UpdatePrompt(deltaTime, desktopMode);
            glow = Mathf.MoveTowards(glow, UserInRange ? 1f : 0f, deltaTime / FadeSeconds);
            ApplyGlow();
            KeepHandleOffRig(deltaTime);
        }

        void Lookups(float deltaTime)
        {
            if (head == null || !head.isActiveAndEnabled)
                head = Camera.main;
            lookupTimer -= deltaTime;
            if (lookupTimer > 0f && desktop != null && controllerRig != null)
                return;
            lookupTimer = 2f;
            if (desktop == null)
                desktop = FindAnyObjectByType<DesktopDemoControls>();
            if (controllerRig == null)
                controllerRig = FindAnyObjectByType<VrControllerRig>();
        }

        bool HeadInRange(Vector3 eye) =>
            ChamberDoorLogic.InRange(Flat(eye), Flat(chamber.DoorHingeWorld), Flat(chamber.DoorEdgeWorld), Flat(chamber.DoorOutwardWorld));

        static bool PrimaryPressed(InteractorHandedness hand)
        {
            var button = ControllerButtons.Find(hand, ControllerButtons.Primary);
            return button != null && button.wasPressedThisFrame;
        }

        /// <summary>Opens a closed door or closes an open one (also mid-swing). False while a station holds the front or right after a toggle.</summary>
        public bool Toggle() => Toggle(InteractorHandedness.None, "script");

        bool Toggle(InteractorHandedness hand, string source)
        {
            if (!Available || Time.unscaledTime - lastToggle < ToggleCooldown || !state.Toggle())
                return false;
            lastToggle = Time.unscaledTime;
            Haptic(hand);
            // The pop-up's press feedback (only while it is up), then it hides for the swing.
            popup?.Press();
            Debug.Log($"[BTP] Chamber door {(state.UserOpen ? "opening" : "closing")} ({source}).");
            return true;
        }

        /// <summary>A short pulse on the controller of <paramref name="hand"/> (nothing for the keyboard or without controllers).</summary>
        static void Haptic(InteractorHandedness hand)
        {
            if (hand != InteractorHandedness.Left && hand != InteractorHandedness.Right)
                return;
            foreach (var interactor in FindObjectsByType<NearFarInteractor>())
            {
                if (interactor.handedness == hand && VrControllerRig.IsControllerInteractor(interactor))
                {
                    interactor.SendHapticImpulse(HapticAmplitude, HapticDuration);
                    return;
                }
            }
        }

        /// <summary>The rig's jump (XRI starter rig: right A) pauses while A opens the door; it is restored as it was.</summary>
        void PauseJumps(bool pause)
        {
            if (pause == pausedJumps.Count > 0)
                return;
            if (pause)
            {
                foreach (var jump in FindObjectsByType<JumpProvider>())
                {
                    if (!jump.enabled)
                        continue;
                    jump.enabled = false;
                    pausedJumps.Add(jump);
                }

                return;
            }

            foreach (var jump in pausedJumps)
                if (jump != null)
                    jump.enabled = true;
            pausedJumps.Clear();
        }

        // ------------------------------------------------------------------ handle

        /// <summary>The handle becomes a pressable XR interactable: ray (trigger or grip), poke, hand pinch and the desktop mouse toggle the door.</summary>
        void SetupHandle()
        {
            if (handle != null || chamber == null)
                return;
            handle = chamber.DoorHandle;
            if (handle == null)
                return;
            handleRenderer = handle.GetComponent<MeshRenderer>();
            if (handleRenderer != null && handleRenderer.sharedMaterial != null && handleRenderer.sharedMaterial.HasProperty("_BaseColor"))
                handleBase = handleRenderer.sharedMaterial.GetColor("_BaseColor");

            handleCollider = handle.GetComponent<BoxCollider>();
            if (handleCollider == null)
                handleCollider = handle.gameObject.AddComponent<BoxCollider>();
            // The bar is 22 mm thick (unit cylinder: 1 wide, 2 high): a 66 mm grab volume over its length makes it easy to hit.
            handleCollider.size = new Vector3(3f, 2.1f, 3f);
            handleCollider.isTrigger = false;

            handleInteractable = handle.GetComponent<XRSimpleInteractable>();
            if (handleInteractable == null)
                handleInteractable = handle.gameObject.AddComponent<XRSimpleInteractable>();
            if (!handleInteractable.colliders.Contains(handleCollider))
                handleInteractable.colliders.Add(handleCollider);
            handleInteractable.selectEntered.AddListener(OnHandleSelected);

            // Poke: the default configuration (push along the handle's +Z, which points into the leaf also when it is open,
            // within 45 degrees) is what pushing a door handle feels like.
            var poke = handle.GetComponent<XRPokeFilter>();
            if (poke == null)
                poke = handle.gameObject.AddComponent<XRPokeFilter>();
            poke.pokeInteractable = handleInteractable;
            poke.pokeCollider = handleCollider;
            FindRigs();
        }

        void OnHandleSelected(SelectEnterEventArgs args)
        {
            var interactor = args.interactorObject;
            var hand = interactor != null ? interactor.handedness : InteractorHandedness.None;
            if (!Toggle(InteractorHandedness.None, "handle") || interactor == null)
                return;
            if (interactor is XRBaseInputInteractor input && VrControllerRig.IsControllerInteractor(input))
                input.SendHapticImpulse(HapticAmplitude, HapticDuration);
            else if (interactor is XRPokeInteractor)
                Haptic(hand);
        }

        void FindRigs()
        {
            rigs.Clear();
            rigs.AddRange(FindObjectsByType<CharacterController>(FindObjectsInactive.Include));
        }

        /// <summary>The handle's collider never blocks the rig (re-applied: re-activating a collider resets the ignore).</summary>
        void KeepHandleOffRig(float deltaTime)
        {
            ignoreTimer -= deltaTime;
            if (ignoreTimer > 0f || handleCollider == null)
                return;
            ignoreTimer = 1f;
            if (!handleCollider.enabled || !handleCollider.gameObject.activeInHierarchy)
                return;
            foreach (var rig in rigs)
                if (rig != null && rig.enabled && rig.gameObject.activeInHierarchy)
                    Physics.IgnoreCollision(rig, handleCollider, true);
        }

        void ApplyGlow()
        {
            if (handleRenderer == null)
                return;
            if (glow <= 0.001f)
            {
                handleRenderer.SetPropertyBlock(null);
                return;
            }

            handleBlock ??= new MaterialPropertyBlock();
            var pulse = 0.3f + 0.1f * Mathf.Sin(Time.time * 3f);
            handleBlock.SetColor("_BaseColor", Color.Lerp(handleBase, UIPalette.Cyan, glow * pulse));
            handleRenderer.SetPropertyBlock(handleBlock);
        }

        // ------------------------------------------------------------------ prompt

        void UpdatePrompt(float deltaTime, bool desktopMode)
        {
            var show = state.PromptVisible(UserInRange) && head != null;
            if (popup == null)
            {
                if (!show)
                    return;
                popup = new ChamberDoorPopup(transform);
            }
            else if (!show && !popup.Visible && !popup.Pressing)
                return;

            var eye = head != null ? head.transform.position : Vector3.zero;
            var outward = chamber.DoorOutwardWorld;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.back;
            popup.Tick(show, state.UserOpen, desktopMode, PromptAnchor(eye, outward), eye, -outward, deltaTime);
        }

        /// <summary>Beside the closed door's handle, a little in front of the glass towards the user, at about eye height.</summary>
        Vector3 PromptAnchor(Vector3 eye, Vector3 outward)
        {
            var anchor = chamber.DoorHandleClosedWorld + outward * PromptOut;
            var top = chamber.DoorHeightRangeWorld.y + 0.15f;
            var low = anchor.y + 0.12f;
            anchor.y = Mathf.Clamp(eye.y - 0.12f, low, Mathf.Max(low, top));
            return anchor;
        }
    }
}
