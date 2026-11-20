using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace BTP.Treatment
{
    /// <summary>
    /// Reads the OpenXR hand-tracking subsystem (Quest native hand tracking) for the treatment station: tracking loss of a
    /// hand stops spraying and pouring that frame and returns the held tool to its dock; a lasting loss shows "Hands out of
    /// view" on the touchscreen; the holding hand's pinch (thumb tip to index tip) drives the sprayer. No hand visuals.
    /// Without a running hand subsystem (controllers, desktop, simulator) it does nothing.
    /// </summary>
    public sealed class HandTrackingMonitor : MonoBehaviour
    {
        public const float PinchClose = 0.022f, PinchOpen = 0.04f;

        [SerializeField] ChamberStation station;

        readonly HandLossTracker tracker = new HandLossTracker();
        readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        readonly bool[] pinch = new bool[2];
        bool messageShown;

        public HandLossTracker Tracker => tracker;

        internal void Bind(ChamberStation owner) => station = owner;

        void OnEnable() => tracker.HandLost += OnHandLost;

        void OnDisable() => tracker.HandLost -= OnHandLost;

        void OnHandLost(int hand)
        {
            if (station != null)
                station.HandleTrackingLost(HandLossTracker.HandName(hand));
        }

        void Update()
        {
            SubsystemManager.GetSubsystems(subsystems);
            XRHandSubsystem hands = null;
            foreach (var subsystem in subsystems)
                if (subsystem.running)
                    hands = subsystem;
            if (hands == null)
                return;
            tracker.Update(Time.deltaTime, hands.leftHand.isTracked, hands.rightHand.isTracked);
            if (tracker.ShowMessage != messageShown)
            {
                messageShown = tracker.ShowMessage;
                if (station != null)
                    station.ShowHandsOutOfView(messageShown);
                HandTestLog.Log("tracking", "both", "hands", messageShown ? "out of view" : "back");
            }

            UpdatePinch(0, hands.leftHand);
            UpdatePinch(1, hands.rightHand);
        }

        void UpdatePinch(int index, XRHand hand)
        {
            if (!hand.isTracked)
            {
                pinch[index] = false;
                return;
            }

            if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb) || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index2))
                return;
            var distance = Vector3.Distance(thumb.position, index2.position);
            var now = pinch[index] ? distance < PinchOpen : distance < PinchClose;
            if (now == pinch[index])
                return;
            pinch[index] = now;
            if (station != null)
                station.HandlePinch(HandLossTracker.HandName(index), now);
        }
    }
}
