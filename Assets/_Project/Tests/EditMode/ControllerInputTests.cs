using BTP.Core;
using BTP.Lab;
using BTP.Treatment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace BTP.Tests
{
    public class ControllerInputTests
    {
        [Test]
        public void AimDelta_TurningRight_IsPositiveYaw_AndAimingUp_IsPositivePitch()
        {
            LeafMouseRotation.AimDelta(Vector3.forward, Vector3.right, out var yaw, out var pitch);
            Assert.That(yaw, Is.EqualTo(90f).Within(0.001f));
            Assert.That(pitch, Is.EqualTo(0f).Within(0.001f));

            LeafMouseRotation.AimDelta(Vector3.forward, (Vector3.forward + Vector3.up).normalized, out yaw, out pitch);
            Assert.That(yaw, Is.EqualTo(0f).Within(0.001f));
            Assert.That(pitch, Is.EqualTo(45f).Within(0.001f));
        }

        [Test]
        public void AimDelta_VerticalAim_HasNoYaw()
        {
            LeafMouseRotation.AimDelta(Vector3.up, Vector3.forward, out var yaw, out var pitch);
            Assert.That(yaw, Is.EqualTo(0f));
            Assert.That(pitch, Is.EqualTo(-90f).Within(0.001f));
        }

        [Test]
        public void AccumulateRotation_AppliesGain_MatchesMouseSign_AndClampsPitch()
        {
            float yaw = 10f, pitch = 0f;
            LeafMouseRotation.AccumulateRotation(ref yaw, ref pitch, 5f, 10f, 3f);
            Assert.That(yaw, Is.EqualTo(355f).Within(0.001f), "Turning right lowers the yaw like a mouse drag to the right (wrapped to 0..360).");
            Assert.That(pitch, Is.EqualTo(30f).Within(0.001f));

            LeafMouseRotation.AccumulateRotation(ref yaw, ref pitch, 0f, 100f, 3f);
            Assert.That(pitch, Is.EqualTo(65f), "Pitch clamps at 65 degrees.");
            LeafMouseRotation.AccumulateRotation(ref yaw, ref pitch, 0f, -1000f, 3f);
            Assert.That(pitch, Is.EqualTo(-65f));
        }

        [Test]
        public void GripFilter_AllowsNonControllerInteractors_WhateverTheGrip()
        {
            var previous = ControllerGripSelectFilter.GripReader;
            var holder = new GameObject("Filter hand interactor");
            try
            {
                ControllerGripSelectFilter.GripReader = _ => false;
                var hand = holder.AddComponent<XRDirectInteractor>();
                hand.handedness = InteractorHandedness.Right;
                Assert.That(ControllerGripSelectFilter.Instance.canProcess, Is.True);
                Assert.That(ControllerGripSelectFilter.Instance.Process(hand, null), Is.True, "An unregistered (hand / mouse) interactor is never blocked.");
                Assert.That(ControllerGripSelectFilter.Instance.Process(null, null), Is.True);
            }
            finally
            {
                ControllerGripSelectFilter.GripReader = previous;
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void GripFilter_ControllerInteractor_NeedsItsOwnHandsGrip()
        {
            var previous = ControllerGripSelectFilter.GripReader;
            var holder = new GameObject("Filter controller interactor");
            try
            {
                var controller = holder.AddComponent<XRDirectInteractor>();
                controller.handedness = InteractorHandedness.Left;
                VrControllerRig.RegisterControllerInteractor(controller);
                ControllerGripSelectFilter.GripReader = hand => hand == InteractorHandedness.Left;
                Assert.That(ControllerGripSelectFilter.Instance.Process(controller, null), Is.True, "Left grip down allows the left controller.");
                ControllerGripSelectFilter.GripReader = hand => hand == InteractorHandedness.Right;
                Assert.That(ControllerGripSelectFilter.Instance.Process(controller, null), Is.False, "Trigger alone (no left grip) cannot hold a tool the trigger uses.");
            }
            finally
            {
                VrControllerRig.UnregisterControllerInteractor(holder.GetComponent<XRDirectInteractor>());
                ControllerGripSelectFilter.GripReader = previous;
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void GripOrTriggerFilter_ControllerHoldsWithEitherButton_OfItsOwnHand()
        {
            var previousGrip = ControllerGripSelectFilter.GripReader;
            var previousTrigger = ControllerGripSelectFilter.TriggerReader;
            var holder = new GameObject("Filter right controller");
            try
            {
                var controller = holder.AddComponent<XRDirectInteractor>();
                controller.handedness = InteractorHandedness.Right;
                VrControllerRig.RegisterControllerInteractor(controller);
                var scoop = ControllerGripSelectFilter.GripOrTrigger;
                var sprayer = ControllerGripSelectFilter.Instance;

                ControllerGripSelectFilter.GripReader = _ => false;
                ControllerGripSelectFilter.TriggerReader = hand => hand == InteractorHandedness.Right;
                Assert.That(scoop.Process(controller, null), Is.True, "the right trigger alone picks up and holds the scoop");
                Assert.That(sprayer.Process(controller, null), Is.False, "the right trigger alone never holds the sprayer (it sprays)");

                ControllerGripSelectFilter.TriggerReader = _ => false;
                ControllerGripSelectFilter.GripReader = hand => hand == InteractorHandedness.Right;
                Assert.That(scoop.Process(controller, null), Is.True, "the right grip holds the scoop");
                Assert.That(sprayer.Process(controller, null), Is.True, "the right grip holds the sprayer");

                ControllerGripSelectFilter.GripReader = hand => hand == InteractorHandedness.Left;
                ControllerGripSelectFilter.TriggerReader = hand => hand == InteractorHandedness.Left;
                Assert.That(scoop.Process(controller, null), Is.False, "the other hand's buttons never hold it");
                ControllerGripSelectFilter.GripReader = _ => false;
                ControllerGripSelectFilter.TriggerReader = _ => false;
                Assert.That(scoop.Process(controller, null), Is.False, "releasing both drops it");
            }
            finally
            {
                VrControllerRig.UnregisterControllerInteractor(holder.GetComponent<XRDirectInteractor>());
                ControllerGripSelectFilter.GripReader = previousGrip;
                ControllerGripSelectFilter.TriggerReader = previousTrigger;
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void TreatmentTools_ComeToTheHand_ControllersSelectThemOnlyByGripToggle()
        {
            var scoop = new GameObject("GrainScoop (test)");
            var sprayer = new GameObject("Sprayer (test)");
            var right = new GameObject("Right controller (test)");
            var left = new GameObject("Left controller (test)");
            var hand = new GameObject("Tracked hand (test)");
            try
            {
                scoop.AddComponent<BoxCollider>().size = new Vector3(0.25f, 0.04f, 0.075f);
                var grip = new GameObject("Grip").transform;
                grip.SetParent(scoop.transform, false);
                var item = TreatmentGrabbable.Attach(scoop, "Grain scoop", grip, 0f);
                Assert.That(item.Grab.farAttachMode, Is.EqualTo(InteractableFarAttachMode.Near), "a ray pick-up brings the scoop into the hand");
                Assert.That(item.Grab.attachTransform, Is.SameAs(grip), "held by its handle");
                Assert.That(Filters(item), Has.Member(TreatmentGrabbable.ToggleSelectFilter.Instance));

                // The sprayer arrives with its own grip-hold filter: it would drop the tool once the grip is let go.
                sprayer.AddComponent<BoxCollider>();
                var bottleGrab = sprayer.AddComponent<XRGrabInteractable>();
                ControllerGripSelectFilter.AddTo(bottleGrab);
                var bottle = TreatmentGrabbable.Attach(sprayer, "Spray bottle", null, 0f);
                Assert.That(Filters(bottle), Has.No.Member(ControllerGripSelectFilter.Instance), "no hold-to-keep filter on a toggled tool");
                Assert.That(Filters(bottle), Has.No.Member(ControllerGripSelectFilter.GripOrTrigger));
                Assert.That(Filters(bottle).FindAll(f => f is TreatmentGrabbable.ToggleSelectFilter), Has.Count.EqualTo(1));

                var rightController = right.AddComponent<XRDirectInteractor>();
                rightController.handedness = InteractorHandedness.Right;
                var leftController = left.AddComponent<XRDirectInteractor>();
                leftController.handedness = InteractorHandedness.Left;
                VrControllerRig.RegisterControllerInteractor(rightController);
                VrControllerRig.RegisterControllerInteractor(leftController);
                var trackedHand = hand.AddComponent<XRDirectInteractor>();
                var filter = TreatmentGrabbable.ToggleSelectFilter.Instance;

                Assert.That(filter.Process(rightController, item.Grab), Is.False, "a trigger or grip press never selects a tool by itself");
                item.ToggleHolder = rightController;
                Assert.That(filter.Process(rightController, item.Grab), Is.True, "the grip toggle's controller holds it, no button needed");
                Assert.That(filter.Process(leftController, item.Grab), Is.False, "the other controller cannot take it");
                Assert.That(filter.Process(trackedHand, item.Grab), Is.True, "tracked hands keep their own pinch / grasp hold");
                item.ToggleHolder = null;

                Assert.That(TreatmentGrabbable.ReleaseReturnsHome(GuidedInputGate.Right), Is.True, "a controller release puts the tool back");
                Assert.That(TreatmentGrabbable.ReleaseReturnsHome(GuidedInputGate.Left), Is.True);
                Assert.That(TreatmentGrabbable.ReleaseReturnsHome(GuidedInputGate.Mouse), Is.False, "the desktop keeps its drop");
            }
            finally
            {
                VrControllerRig.UnregisterControllerInteractor(right.GetComponent<XRDirectInteractor>());
                VrControllerRig.UnregisterControllerInteractor(left.GetComponent<XRDirectInteractor>());
                foreach (var go in new[] { scoop, sprayer, right, left, hand })
                    Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GripToggle_PicksTheToolAtTheHand_ElseTheOnePointedAt()
        {
            var scoop = new GameObject("GrainScoop (test)");
            var sack = new GameObject("Sack (test)");
            try
            {
                scoop.transform.position = new Vector3(20f, 0.9f, 0f);
                scoop.AddComponent<BoxCollider>().size = new Vector3(0.25f, 0.04f, 0.075f);
                var scoopItem = TreatmentGrabbable.Attach(scoop, "Grain scoop", null, 0f);
                sack.transform.position = new Vector3(20f, 0.9f, 0.6f);
                sack.AddComponent<BoxCollider>().size = new Vector3(0.2f, 0.3f, 0.2f);
                var sackItem = TreatmentGrabbable.Attach(sack, "Sack", null, 0f);
                Physics.SyncTransforms();
                var items = new System.Collections.Generic.List<TreatmentGrabbable> { scoopItem, sackItem };

                var handle = scoop.transform.position + new Vector3(-0.09f, 0.06f, 0f);
                Assert.That(ToolGripToggle.Choose(items, null, handle, null), Is.SameAs(scoopItem), "the controller resting on the handle picks up the scoop");
                var away = new Vector3(20f, 1.6f, -1.2f);
                Assert.That(ToolGripToggle.Choose(items, null, away, null), Is.Null, "nothing within reach, nothing pointed at");
                var atSack = new Ray(away, (sack.transform.position - away).normalized);
                Assert.That(ToolGripToggle.Choose(items, null, away, atSack), Is.SameAs(sackItem), "pointing at a sack picks it up");
                var atScoop = new Ray(away, (scoop.transform.position - away).normalized);
                Assert.That(ToolGripToggle.Choose(items, null, away, atScoop), Is.SameAs(scoopItem));
            }
            finally
            {
                Object.DestroyImmediate(scoop);
                Object.DestroyImmediate(sack);
            }
        }

        static System.Collections.Generic.List<IXRSelectFilter> Filters(TreatmentGrabbable item)
        {
            var filters = new System.Collections.Generic.List<IXRSelectFilter>();
            item.Grab.selectFilters.GetAll(filters);
            return filters;
        }

        /// <summary>Stand-in for the XRI sample's ControllerInputActionManager, which the rig finds by type name.</summary>
        sealed class ControllerInputActionManager : MonoBehaviour
        {
            [SerializeField] InputActionReference m_TeleportMode;
            public bool smoothMotionEnabled { get; set; }
            public bool smoothTurnEnabled { get; set; }
            public InputActionReference TeleportMode { get => m_TeleportMode; set => m_TeleportMode = value; }
        }

        [Test]
        public void Locomotion_LeftStickWalks_RightStickOnlyTurnsSmoothly_AtComfortableSpeeds()
        {
            var rig = new GameObject("XR Origin (locomotion test)");
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                ControllerInputActionManager Manager(string name, string hand)
                {
                    var controller = new GameObject(name);
                    controller.transform.SetParent(rig.transform, false);
                    var manager = controller.AddComponent<ControllerInputActionManager>();
                    var teleport = asset.AddActionMap($"XRI {hand} Locomotion").AddAction("Teleport Mode", binding: $"<XRController>{{{hand}Hand}}/{{Primary2DAxis}}");
                    manager.TeleportMode = InputActionReference.Create(teleport);
                    return manager;
                }

                var left = Manager("Left Controller", "Left");
                var right = Manager("Right Controller", "Right");
                right.smoothMotionEnabled = true; // whatever a scene saved: the right stick must not walk
                var body = rig.AddComponent<CharacterController>();
                body.stepOffset = 0.5f;
                var move = rig.AddComponent<ContinuousMoveProvider>();
                var turn = rig.AddComponent<ContinuousTurnProvider>();
                turn.enableTurnAround = true;

                rig.AddComponent<VrControllerRig>().ConfigureLocomotion();

                Assert.That(left.smoothMotionEnabled, Is.True, "the left stick walks smoothly");
                Assert.That(left.smoothTurnEnabled, Is.True, "with walking off, the left stick turns smoothly too (no snap)");
                Assert.That(left.TeleportMode.action.bindings[0].effectivePath, Is.EqualTo("<XRController>{LeftHand}/{Primary2DAxis}"),
                    "the left stick can still teleport when smooth walking is turned off in the menu");
                Assert.That(right.smoothMotionEnabled, Is.False, "the right stick does not walk");
                Assert.That(right.smoothTurnEnabled, Is.True, "the right stick turns smoothly, no snap turn");
                Assert.That(right.TeleportMode.action.bindings[0].effectivePath, Is.Empty, "the right stick never shows a teleport arc or jumps");
                Assert.That(move.moveSpeed, Is.EqualTo(VrControllerRig.MoveSpeed));
                Assert.That(move.moveSpeed, Is.InRange(1.5f, 2f), "a comfortable walking speed (m/s)");
                Assert.That(move.enableStrafe, Is.True);
                Assert.That(move.enableFly, Is.False);
                Assert.That(turn.turnSpeed, Is.InRange(60f, 90f), "a comfortable turn speed (deg/s)");
                Assert.That(turn.enableTurnAround, Is.False, "pulling the stick back never jumps 180 degrees");
                Assert.That(body.stepOffset, Is.LessThanOrEqualTo(VrControllerRig.StepOffset), "no gravity: walking never climbs onto things");
                Assert.That(VrControllerRig.ApplyStickDefaults(rig), Is.EqualTo(2), "exactly the two controller managers");
            }
            finally
            {
                foreach (var manager in rig.GetComponentsInChildren<ControllerInputActionManager>())
                    if (manager.TeleportMode != null)
                        Object.DestroyImmediate(manager.TeleportMode);
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(asset);
            }
        }
    }
}
