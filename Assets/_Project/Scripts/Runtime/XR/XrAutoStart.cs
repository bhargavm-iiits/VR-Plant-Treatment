using System;
using System.Collections;
using System.Collections.Generic;
using BTP.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace BTP.XR
{
    /// <summary>
    /// Starts VR by itself. When the player (or Editor Play mode) starts before Quest Link is active, OpenXR's start-up
    /// initialisation fails and the app runs in the desktop view. This always-on component keeps retrying
    /// <see cref="XrHeadset.TryStart"/> with a backoff (1 s after start, then 3 s growing to at most 10 s) until an XR
    /// display runs. Then head tracking takes over (DesktopDemoControls steps aside by itself), the XR Origin's floor
    /// tracking is set up again, and once the first tracked head pose arrives the user is placed at the current
    /// environment's spawn point facing forward, behind a short fade. If XR stops (Link disconnected) the desktop view
    /// returns and the retries resume. Installs itself; pass <c>-desktop</c> on the command line to turn it off.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class XrAutoStart : MonoBehaviour
    {
        public const float FirstRetrySeconds = 1f;
        public const float MinRetrySeconds = 3f;
        public const float MaxRetrySeconds = 10f;
        public const float BackoffFactor = 1.5f;
        public const string HintText = "VR: put on the Quest and enable Quest Link — VR starts automatically";
        public const string DisableArgument = "-desktop";

        const float TrackingTimeoutSeconds = 10f;
        const float FadeInSeconds = 0.5f;

        enum State { Unknown, Waiting, Running }

        static XrAutoStart instance;
        static readonly List<InputDevice> Heads = new();

        State state = State.Unknown;
        int failures;
        int attempts;
        int maxAttempts = int.MaxValue;
        float nextAttemptAt;
        Coroutine settling;
        FadeOverlay heldFade;
        GUIStyle hintStyle;

        // Frames on which the environment last placed the rig and the head first reported tracking.
        EnvironmentController subscribedEnvironment;
        int lastPlacementFrame = -1;
        int trackedFrame = -1;

        /// <summary>True while an XR display is running.</summary>
        public static bool IsRunning => instance != null && instance.state == State.Running;

        /// <summary>
        /// Seconds to wait before the next start attempt after <paramref name="failedAttempts"/> failed ones: the first
        /// retry comes after <see cref="FirstRetrySeconds"/>, then <see cref="MinRetrySeconds"/> growing by
        /// <see cref="BackoffFactor"/> per failure up to <see cref="MaxRetrySeconds"/>.
        /// </summary>
        public static float RetryDelaySeconds(int failedAttempts)
        {
            if (failedAttempts <= 0)
                return FirstRetrySeconds;
            var delay = MinRetrySeconds * Math.Pow(BackoffFactor, Math.Min(failedAttempts - 1, 64));
            return (float)Math.Min(delay, MaxRetrySeconds);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (instance != null || Application.isBatchMode)
                return;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), DisableArgument) >= 0)
            {
                Debug.Log($"[BTP][XR] {DisableArgument}: automatic headset start is off.");
                return;
            }

            var go = new GameObject("XR Auto Start");
            DontDestroyOnLoad(go);
            go.AddComponent<XrAutoStart>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            // On Quest (Android) XR is native and starts with the app; one retry is enough there.
            if (Application.platform == RuntimePlatform.Android)
                maxAttempts = 1;
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
            Unsubscribe();
            ReleaseFade();
        }

        void Update()
        {
            TrackEnvironment();

            if (XrReadiness.DisplayRunning(out _))
            {
                if (state != State.Running)
                    OnStarted(lateStart: state == State.Waiting);
                if (trackedFrame < 0 && HeadTracked())
                    trackedFrame = Time.frameCount;
                return;
            }

            if (state != State.Waiting)
                OnStopped(wasRunning: state == State.Running);

            if (attempts >= maxAttempts)
                return;
            var now = Time.realtimeSinceStartup;
            if (now < nextAttemptAt)
                return;

            // The CropScan coordinator shares the attempt clock: never two blocking initialisations close together.
            if (XrHeadset.AttemptedWithin(MinRetrySeconds))
            {
                nextAttemptAt = XrHeadset.LastAttemptRealtime + MinRetrySeconds;
                return;
            }

            attempts++;
            if (XrHeadset.TryStart(out _))
            {
                // The display starts with the OpenXR session, usually a few frames later; check again shortly.
                nextAttemptAt = Time.realtimeSinceStartup + MinRetrySeconds;
                return;
            }

            failures++;
            nextAttemptAt = Time.realtimeSinceStartup + RetryDelaySeconds(failures);
        }

        void OnStarted(bool lateStart)
        {
            state = State.Running;
            failures = 0;
            attempts = 0;
            trackedFrame = HeadTracked() ? Time.frameCount : -1;
            Debug.Log(lateStart
                ? "[BTP][XR] Headset started: head tracking on, placing you at the spawn point."
                : "[BTP][XR] Headset running: head tracking on.");
            if (settling != null)
                StopCoroutine(settling);
            settling = StartCoroutine(Settle(lateStart));
        }

        void OnStopped(bool wasRunning)
        {
            state = State.Waiting;
            attempts = 0;
            // After a disconnect OpenXR may still be shutting down; leave it a moment before the first retry.
            failures = wasRunning ? 1 : 0;
            nextAttemptAt = Time.realtimeSinceStartup + RetryDelaySeconds(failures);
            if (settling != null)
            {
                StopCoroutine(settling);
                settling = null;
            }

            ReleaseFade();
            Debug.Log(wasRunning
                ? "[BTP][XR] Headset stopped (Quest Link disconnected?): desktop view; VR restarts automatically."
                : "[BTP][XR] No XR headset running: desktop view; VR starts automatically once Quest Link is active.");
        }

        /// <summary>Head tracking has taken over: re-place the user once a real head pose arrives.</summary>
        IEnumerator Settle(bool lateStart)
        {
            if (lateStart)
            {
                // XROrigin set up its floor offset at Start, before any input subsystem existed; run that setup again.
                var origin = FindAnyObjectByType<XROrigin>();
                if (origin != null)
                    origin.RequestedTrackingOriginMode = origin.RequestedTrackingOriginMode;
            }

            EnvironmentController environment;
            while ((environment = EnvironmentController.Instance) == null || environment.IsSwitching || !environment.Current.HasValue)
                yield return null;

            // A scan launch waiting for the headset re-places the rig at its specimen itself.
            var coordinator = XrSessionCoordinator.Instance;
            if (coordinator != null && coordinator.WaitingForHeadset)
            {
                settling = null;
                yield break;
            }

            if (lateStart)
                HoldFade();

            var deadline = Time.realtimeSinceStartup + TrackingTimeoutSeconds;
            while (trackedFrame < 0 && Time.realtimeSinceStartup < deadline)
                yield return null;
            // A few frames for the TrackedPoseDriver to apply the pose.
            for (var i = 0; i < 3; i++)
                yield return null;

            while ((environment = EnvironmentController.Instance) == null || environment.IsSwitching)
                yield return null;

            // At start-up the environment often placed the rig after the head was already tracked: nothing to fix then.
            var placedWhileTracked = trackedFrame >= 0 && lastPlacementFrame > trackedFrame + 2;
            if (lateStart || !placedWhileTracked)
                environment.Recenter();

            if (heldFade != null)
            {
                yield return null;
                var fade = heldFade;
                heldFade = null;
                yield return fade.FadeTo(0f, FadeInSeconds);
            }

            settling = null;
        }

        void HoldFade()
        {
            heldFade = FindAnyObjectByType<FadeOverlay>();
            if (heldFade != null)
                heldFade.SetAlpha(1f);
        }

        /// <summary>Never leave the view black: the desktop view must stay visible when XR stops mid-fade.</summary>
        void ReleaseFade()
        {
            if (heldFade != null)
                heldFade.SetAlpha(0f);
            heldFade = null;
        }

        static bool HeadTracked()
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, Heads);
            foreach (var device in Heads)
            {
                if (device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) && tracked)
                    return true;
            }

            return false;
        }

        void TrackEnvironment()
        {
            var environment = EnvironmentController.Instance;
            if (environment == subscribedEnvironment)
                return;
            Unsubscribe();
            subscribedEnvironment = environment;
            if (environment == null)
                return;
            environment.EnvironmentLoaded += OnRigPlaced;
            environment.EnvironmentFocused += OnRigPlaced;
        }

        void Unsubscribe()
        {
            if (subscribedEnvironment == null)
                return;
            subscribedEnvironment.EnvironmentLoaded -= OnRigPlaced;
            subscribedEnvironment.EnvironmentFocused -= OnRigPlaced;
            subscribedEnvironment = null;
        }

        void OnRigPlaced(EnvironmentId _) => lastPlacementFrame = Time.frameCount;

        void OnGUI()
        {
            if (state != State.Waiting)
                return;

            hintStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                padding = new RectOffset(12, 12, 8, 8),
                normal = { textColor = Color.white },
            };

            var content = new GUIContent(HintText);
            var size = hintStyle.CalcSize(content);
            var width = Mathf.Min(size.x, Screen.width - 24f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height - size.y - 12f, width, size.y), content, hintStyle);
        }
    }
}
