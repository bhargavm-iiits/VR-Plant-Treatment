using System;
using UnityEngine;
using UnityEngine.XR.Management;

namespace BTP.XR
{
    /// <summary>
    /// Starts OpenXR after the player is already running. When the app link starts the player before Quest Link is active,
    /// XR Management's start-up initialisation fails and the player runs in the desktop view; once the learner activates
    /// Link, this initialises the loader and starts its subsystems (DesktopDemoControls hands the camera back to head
    /// tracking by itself). Safe to call repeatedly; it does nothing while XR is running.
    /// Both callers (<see cref="XrAutoStart"/> and the CropScan <see cref="XrSessionCoordinator"/>) share one attempt
    /// clock here, so the blocking loader initialisation never runs twice in the same frame.
    /// </summary>
    public static class XrHeadset
    {
        static int lastAttemptFrame = -1;
        static bool attempting;
        static string lastWarning;

        /// <summary>Realtime (seconds since start-up) of the last start attempt by any caller; negative infinity before the first.</summary>
        public static float LastAttemptRealtime { get; private set; } = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lastAttemptFrame = -1;
            attempting = false;
            lastWarning = null;
            LastAttemptRealtime = float.NegativeInfinity;
        }

        /// <summary>True when any caller attempted a start within the last <paramref name="seconds"/>.</summary>
        public static bool AttemptedWithin(float seconds) => Time.realtimeSinceStartup - LastAttemptRealtime < seconds;

        public static bool TryStart(out string message)
        {
            message = null;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null)
            {
                message = "XR is not configured in this build.";
                return false;
            }

            // One attempt per frame across all callers (InitializeLoaderSync blocks for a few hundred milliseconds).
            if (attempting || lastAttemptFrame == Time.frameCount)
            {
                message = "A headset start was already attempted this frame.";
                return false;
            }

            attempting = true;
            lastAttemptFrame = Time.frameCount;
            LastAttemptRealtime = Time.realtimeSinceStartup;
            try
            {
                if (manager.activeLoader == null)
                {
                    if (manager.isInitializationComplete)
                        manager.DeinitializeLoader();
                    manager.InitializeLoaderSync();
                }

                if (manager.activeLoader == null)
                {
                    message = "Quest Link is not active yet: put on the headset and turn on Link.";
                    return false;
                }

                manager.StartSubsystems();
                lastWarning = null;
                return true;
            }
            catch (Exception exception)
            {
                message = "The headset could not be started: " + exception.Message;
                // Repeated retries must not repeat the same warning.
                if (message != lastWarning)
                    Debug.LogWarning("[BTP][XR] " + message);
                lastWarning = message;
                return false;
            }
            finally
            {
                attempting = false;
            }
        }
    }
}
