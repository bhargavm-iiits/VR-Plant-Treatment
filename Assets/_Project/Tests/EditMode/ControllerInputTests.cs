using BTP.Core;
using BTP.Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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
                Assert.That(ControllerGripSelectFilter.Instance.Process(controller, null), Is.False, "Trigger alone (no left grip) cannot hold a tool.");
            }
            finally
            {
                VrControllerRig.UnregisterControllerInteractor(holder.GetComponent<XRDirectInteractor>());
                ControllerGripSelectFilter.GripReader = previous;
                Object.DestroyImmediate(holder);
            }
        }
    }
}
