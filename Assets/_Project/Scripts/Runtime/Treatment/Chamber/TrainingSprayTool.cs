using System;
using System.Collections.Generic;
using BTP.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Treatment
{
    /// <summary>
    /// The handheld training sprayer for spray-indicated cases (Addendum 5; the project's own spray-bottle model, no hand
    /// model). Picked up from its dock with a tracked grasp (either hand) it follows the grip pose. Spraying needs a SEPARATE
    /// pinch after the pickup (a pinch held while grabbing does not spray until it has been released once) and stops the
    /// moment the pinch is released. Twelve rays in a 10-degree cone from the nozzle test the marked foliage target only:
    /// coverage grows with the share of rays that hit it (spray elsewhere earns nothing); the spray amount grows with spraying
    /// time. Release or tracking loss stops the spray and returns the bottle to its dock. Monitoring cases never need it.
    /// The visible spray is a dense, bright droplet cloud (<see cref="MistParticles"/> at <see cref="MistRate"/> per second in a
    /// <see cref="MistConeAngle"/> cone) and the droplets that reach the specimen bead up ON its leaves
    /// (<see cref="SetFoliage"/>: the chamber specimen's own surface points; the chamber strips its colliders, so neither
    /// particle collisions nor raycasts could find the leaves): up to <see cref="MaxBeads"/> small wet beads stay on the leaf
    /// surface for about <see cref="BeadSeconds"/> and then shrink away. One particle system each, no per-frame allocation.
    /// </summary>
    public sealed class TrainingSprayTool : MonoBehaviour
    {
        public const float UnitsPerSecond = 1.5f, CoveragePerSecond = 45f, ConeAngle = 10f, Range = 0.75f, ReturnDelay = 0.4f;
        const int Rays = 12;

        [SerializeField] Transform nozzle;
        [SerializeField] MeshFilter mistFilter;
        [SerializeField] MeshRenderer mistRenderer;

        Collider target;
        Vector3 dockPosition;
        Quaternion dockRotation = Quaternion.identity;
        bool held;
        Transform grip;
        string holdingHand;
        bool pinched;
        bool armed;
        float returnTimer = -1f;
        float time;
        Mesh mistMesh;
        XRGrabInteractable grab;

        public bool IsHeld => held;
        public bool IsSpraying => held && armed && pinched && !blockedPress && (BurstSeconds <= 0f || burstLeft > 0f);

        /// <summary>
        /// Guided treatment: a <see cref="TreatmentGrabbable"/> owns the bottle's holder (eased return, drop physics), so the
        /// tool never snaps itself back to its dock.
        /// </summary>
        public bool ExternalHome { get; set; }
        /// <summary>Above zero, each press sprays one burst of this many seconds (one spray per trigger press).</summary>
        public float BurstSeconds { get; set; }
        /// <summary>Asked with the holding hand ("left", "right", "key") before a press sprays; false swallows that press.</summary>
        public static Func<string, bool> UseGate { get; set; }
        /// <summary>A press started spraying (held, armed, allowed): the holding hand.</summary>
        public event Action<string> SprayPressed;

        bool blockedPress;
        float burstLeft;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => UseGate = null;
        public string HoldingHand => holdingHand;
        public float SprayedUnits { get; private set; }
        /// <summary>Coverage of the marked foliage target (0-100 %).</summary>
        public float Coverage { get; private set; }
        /// <summary>Share of the last frame's rays that hit the marked target.</summary>
        public float LastHitFraction { get; private set; }
        public Transform Nozzle => nozzle;
        public Vector3 DockPosition => dockPosition;

        /// <summary>Raised while spraying changes the amount or the coverage.</summary>
        public event Action Changed;

        internal void Bind(Transform nozzleTip, MeshFilter mist, MeshRenderer mistMeshRenderer)
        {
            nozzle = nozzleTip;
            mistFilter = mist;
            mistRenderer = mistMeshRenderer;
        }

        void Awake()
        {
            if (!Application.isPlaying)
                return;
            var body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            if (GetComponent<Collider>() == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.135f, 0f);
                box.size = new Vector3(0.1f, 0.28f, 0.085f);
            }

            grab = GetComponent<XRGrabInteractable>();
            if (grab == null)
                grab = gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;
            // A ray grab brings the sprayer into the hand (the controllers' default holds it at the end of the ray).
            grab.farAttachMode = UnityEngine.XR.Interaction.Toolkit.Attachment.InteractableFarAttachMode.Near;
            // Controllers hold with the GRIP (the filter), leaving the trigger to spray.
            ControllerGripSelectFilter.AddTo(grab);
            grab.selectEntered.AddListener(args =>
            {
                Grab(null, args.interactorObject is IXRSelectInteractor s && s.handedness == InteractorHandedness.Left ? "left" : "right");
                // A trigger that is already down at pickup does not spray: it must be released once first.
                if (VrControllerRig.IsActivateHeldOnController(args.interactorObject))
                {
                    pinched = true;
                    armed = false;
                }
            });
            grab.selectExited.AddListener(_ => Release());
            // Controllers: the trigger (activate) sprays; tracked hands: the HandTrackingMonitor reports the holding hand's pinch.
            grab.activated.AddListener(_ => SetPinch(true));
            grab.deactivated.AddListener(_ => SetPinch(false));

            // Real droplets beside the mist cone: they fly, slow in the air and stick to the chamber glass and soil. Brighter and
            // more opaque than the mist mesh so they read against the hall in the headset.
            if (mistRenderer != null && mistRenderer.sharedMaterial != null)
            {
                var drops = Glowing(ChamberGlassPhysics.Tinted(mistRenderer.sharedMaterial, new Color(0.9f, 0.97f, 1f, 0.85f), "mist drops", 0.95f),
                    new Color(0.22f, 0.3f, 0.36f));
                mistParticles = ChamberGlassPhysics.CreateParticles("SprayMistParticles", transform, drops, MistParticles, true, 0.35f);
                var drag = mistParticles.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.limit = 20f;
                drag.drag = 2.2f;

                // The wet beads on the leaves: world space, no gravity or collision; they pop in, sit, then shrink away.
                var beads = Glowing(ChamberGlassPhysics.Tinted(mistRenderer.sharedMaterial, new Color(0.93f, 0.98f, 1f, 0.92f), "leaf beads", 0.98f),
                    new Color(0.3f, 0.38f, 0.45f));
                beadParticles = ChamberGlassPhysics.CreateParticles("SprayLeafBeads", transform, beads, MaxBeads, false, 0f);
                var size = beadParticles.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.2f), new Keyframe(0.04f, 1f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0f)));
            }
        }

        /// <summary>Mist droplets alive at once (the chamber's particle budget: mist 420 + beads 360, water 110 + 30, sparkle 40).</summary>
        public const int MistParticles = 420;
        /// <summary>Mist droplets per second while spraying.</summary>
        public const float MistRate = 360f;
        /// <summary>Half-angle of the visible mist cone (wider than the 10-degree coverage cone of the scoring rays).</summary>
        public const float MistConeAngle = 16f;
        /// <summary>Wet beads on the leaves at most, beads added per second while the mist reaches leaves, and their life.</summary>
        public const int MaxBeads = 360;
        public const float BeadRate = 170f, BeadSeconds = 5f;
        /// <summary>How far the mist wets leaves (m).</summary>
        public const float BeadRange = Range * 1.3f;
        ParticleSystem mistParticles;
        ParticleSystem beadParticles;
        IReadOnlyList<Vector3> foliage;
        float beadCarry;
        float mistCarry;
        uint mistSeed = 0x2545F491u;

        public ParticleSystem MistParticleSystem => mistParticles;

        float NextRandom()
        {
            mistSeed ^= mistSeed << 13;
            mistSeed ^= mistSeed >> 17;
            mistSeed ^= mistSeed << 5;
            return (mistSeed & 0xFFFFFF) / (float)0x1000000;
        }

        static Material Glowing(Material material, Color emission)
        {
            if (material != null && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }

            return material;
        }

        /// <summary>The chamber specimen's surface points (world): mist that reaches them leaves wet beads there. Null: none.</summary>
        public void SetFoliage(IReadOnlyList<Vector3> points) => foliage = points;

        /// <summary>Wet beads alive on the leaves now.</summary>
        public int BeadCount => beadParticles != null ? beadParticles.particleCount : 0;

        /// <summary>Emits this frame's mist droplets from the nozzle into the <see cref="MistConeAngle"/> cone (no allocation).</summary>
        void EmitMist(float deltaTime)
        {
            if (mistParticles == null || nozzle == null)
                return;
            mistCarry += deltaTime * MistRate;
            var count = Mathf.FloorToInt(mistCarry);
            mistCarry -= count;
            for (var i = 0; i < count; i++)
            {
                var angle = NextRandom() * Mathf.PI * 2f;
                var spread = MistConeAngle * Mathf.Sqrt(NextRandom());
                var direction = Quaternion.AngleAxis(spread, nozzle.rotation * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f)) * nozzle.forward;
                mistParticles.Emit(new ParticleSystem.EmitParams
                {
                    position = nozzle.position + direction * 0.005f,
                    velocity = direction * Mathf.Lerp(2f, 3.2f, NextRandom()),
                    startSize = Mathf.Lerp(0.0035f, 0.007f, NextRandom()),
                    startLifetime = Mathf.Lerp(0.55f, 0.95f, NextRandom()),
                    applyShapeToPosition = false,
                }, 1);
            }

            EmitBeads(deltaTime);
        }

        /// <summary>Wets the leaves the mist reaches: beads at foliage points inside the mist cone and range (sampled, capped).</summary>
        void EmitBeads(float deltaTime)
        {
            if (beadParticles == null || foliage == null || foliage.Count == 0)
                return;
            beadCarry += deltaTime * BeadRate;
            var count = Mathf.FloorToInt(beadCarry);
            beadCarry -= count;
            var origin = nozzle.position;
            var forward = nozzle.forward;
            for (var i = 0; i < count; i++)
            {
                // A few random tries per bead: the cone covers only part of the plant.
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    var point = foliage[(int)(NextRandom() * foliage.Count) % foliage.Count];
                    if (!InSprayCone(origin, forward, point, MistConeAngle, BeadRange))
                        continue;
                    var toNozzle = (origin - point).normalized;
                    beadParticles.Emit(new ParticleSystem.EmitParams
                    {
                        // On the sprayed (near) side of the leaf, just off its surface.
                        position = point + toNozzle * 0.003f,
                        velocity = Vector3.zero,
                        startSize = Mathf.Lerp(0.004f, 0.0075f, NextRandom()),
                        startLifetime = BeadSeconds * Mathf.Lerp(0.8f, 1.1f, NextRandom()),
                        applyShapeToPosition = false,
                    }, 1);
                    break;
                }
            }
        }

        /// <summary>True when <paramref name="point"/> is within <paramref name="range"/> of the nozzle and inside its cone.</summary>
        internal static bool InSprayCone(Vector3 nozzlePosition, Vector3 nozzleForward, Vector3 point, float coneAngle, float range)
        {
            var offset = point - nozzlePosition;
            var distance = offset.magnitude;
            if (distance < 1e-4f || distance > range)
                return false;
            return Vector3.Angle(nozzleForward, offset) <= coneAngle;
        }

        void OnDestroy()
        {
            if (mistMesh == null)
                return;
            if (Application.isPlaying)
                Destroy(mistMesh);
            else
                DestroyImmediate(mistMesh);
        }

        /// <summary>The marked foliage target (the spray marker's collider). Null: no target, coverage never grows.</summary>
        public void SetTarget(Collider marked) => target = marked;

        public void Dock(Vector3 position, Quaternion rotation)
        {
            dockPosition = position;
            dockRotation = rotation;
            if (!held && !ExternalHome)
                transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>A new case or specimen: no spray yet.</summary>
        public void ResetSpray()
        {
            ForceRelease(null);
            SprayedUnits = 0f;
            Coverage = 0f;
        }

        public void Grab(Transform gripPose, string hand)
        {
            held = true;
            grip = gripPose;
            holdingHand = hand;
            // A pinch that is already closed at pickup does not spray: it must open first (a separate pinch).
            armed = !pinched;
            returnTimer = -1f;
            HandTestLog.Log("sprayer-grab", hand, "hand-grasp", "held");
        }

        public void Release()
        {
            if (!held)
                return;
            var wasSpraying = IsSpraying;
            held = false;
            grip = null;
            HandTestLog.Log("sprayer-release", holdingHand, "hand-grasp", wasSpraying ? "spray stopped, returning to dock" : "returning to dock");
            holdingHand = null;
            returnTimer = ExternalHome ? -1f : ReturnDelay;
            blockedPress = false;
            ShowMist(false);
        }

        public void ForceRelease(string reason)
        {
            var wasHeld = held;
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            held = false;
            grip = null;
            pinched = false;
            armed = false;
            if (wasHeld && reason != null)
                HandTestLog.Log("sprayer-force-release", holdingHand, "hand-grasp", reason);
            holdingHand = null;
            returnTimer = -1f;
            blockedPress = false;
            if (!ExternalHome)
                transform.SetPositionAndRotation(dockPosition, dockRotation);
            ShowMist(false);
        }

        /// <summary>The holding hand's pinch (or the controller trigger): spraying needs a pinch that started after the pickup.</summary>
        public void SetPinch(bool down)
        {
            if (down == pinched)
                return;
            pinched = down;
            if (!down && held)
                armed = true;
            if (down && held && armed)
            {
                // One press = one spray: refused presses (a popup is up, or this is the press that dismissed it) stay dry.
                blockedPress = UseGate != null && !UseGate(holdingHand ?? "");
                if (!blockedPress)
                {
                    burstLeft = BurstSeconds;
                    SprayPressed?.Invoke(holdingHand);
                }
            }

            if (!down)
                blockedPress = false;
            if (held && armed)
                HandTestLog.Log("sprayer-pinch", holdingHand, "pinch", down ? "spray on" : "spray off");
            if (!down)
                ShowMist(false);
        }

        void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            time += deltaTime;
            if (held && grip != null)
                transform.SetPositionAndRotation(grip.position, grip.rotation);
            if (!held && returnTimer >= 0f)
            {
                returnTimer -= deltaTime;
                if (returnTimer < 0f)
                    transform.SetPositionAndRotation(dockPosition, dockRotation);
            }

            if (!IsSpraying || nozzle == null)
            {
                LastHitFraction = 0f;
                if (BurstSeconds > 0f && burstLeft <= 0f && pinched)
                    ShowMist(false);
                return;
            }

            if (BurstSeconds > 0f)
                burstLeft -= deltaTime;

            var hits = 0;
            for (var i = 0; i < Rays; i++)
            {
                var angle = i * 2.39996f;
                var spread = ConeAngle * Mathf.Sqrt((i + 0.5f) / Rays);
                var direction = Quaternion.AngleAxis(spread, nozzle.rotation * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f)) * nozzle.forward;
                if (target != null && target.Raycast(new Ray(nozzle.position, direction), out _, Range))
                    hits++;
            }

            LastHitFraction = hits / (float)Rays;
            SprayedUnits = Mathf.Min(10f, SprayedUnits + UnitsPerSecond * deltaTime);
            Coverage = Mathf.Min(100f, Coverage + CoveragePerSecond * LastHitFraction * deltaTime);
            ShowMist(true);
            EmitMist(deltaTime);
            Changed?.Invoke();
        }

        void ShowMist(bool visible)
        {
            if (mistRenderer == null || mistFilter == null)
                return;
            mistRenderer.enabled = visible;
            if (!visible || nozzle == null)
                return;
            if (mistMesh == null)
            {
                mistMesh = new Mesh { name = "SprayMist" };
                mistMesh.MarkDynamic();
                mistFilter.sharedMesh = mistMesh;
            }

            var space = mistFilter.transform;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] faces = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            for (var i = 0; i < 60; i++)
            {
                var phase = Mathf.Repeat(time * 2.5f + i * 0.0167f, 1f);
                var angle = i * 2.39996f;
                var radius = Mathf.Tan(ConeAngle * Mathf.Deg2Rad) * phase * Range * Mathf.Sqrt((i % 7 + 0.5f) / 7f);
                var p = nozzle.position + nozzle.forward * (phase * Range) + nozzle.rotation * new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                var centre = space.InverseTransformPoint(p);
                var start = vertices.Count;
                var size = 0.0015f + phase * 0.003f;
                foreach (var axis in axes)
                {
                    vertices.Add(centre + axis * size);
                    normals.Add(axis);
                }

                foreach (var index in faces)
                    triangles.Add(start + index);
            }

            mistMesh.Clear();
            mistMesh.SetVertices(vertices);
            mistMesh.SetNormals(normals);
            mistMesh.SetTriangles(triangles, 0);
            mistMesh.RecalculateBounds();
        }
    }
}
