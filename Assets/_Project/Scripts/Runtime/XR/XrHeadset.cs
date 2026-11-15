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
    /// </summary>
    public static class XrHeadset
    {
        public static bool TryStart(out string message)
        {
            message = null;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null)
            {
                message = "XR is not configured in this build.";
                return false;
            }

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
                return true;
            }
            catch (Exception exception)
            {
                message = "The headset could not be started: " + exception.Message;
                Debug.LogWarning("[BTP][XR] " + message);
                return false;
            }
        }
    }
}
