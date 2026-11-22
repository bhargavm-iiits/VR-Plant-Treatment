using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Core
{
    /// <summary>
    /// Keeps exactly one environment scene (Farm, visiting Lab or Treatment lab) loaded next to
    /// the persistent XRBootstrap scene. A switch saves the current environment, fades out,
    /// unloads it, loads the new one, waits until it is configured, moves the XR rig to its
    /// SpawnAnchor and fades in. The single XR rig in XRBootstrap is kept for the whole session,
    /// so cameras, AudioListeners and input modules are never duplicated.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class EnvironmentController : MonoBehaviour
    {
        [SerializeField] string farmSceneName = "Envirornment";
        [SerializeField] string labSceneName = "Lab";
        [SerializeField] string treatmentSceneName = "Treatment";
        [SerializeField] EnvironmentId startEnvironment = EnvironmentId.Farm;

        [Tooltip("Root of the XR rig (the XR Origin).")]
        [SerializeField] Transform rigRoot;
        [Tooltip("The tracked head camera inside the rig.")]
        [SerializeField] Transform head;
        [SerializeField] FadeOverlay fade;
        [SerializeField, Min(0f)] float fadeDuration = 0.35f;
        [Tooltip("Longest wait for a loaded environment to finish configuring itself.")]
        [SerializeField, Min(1f)] float readyTimeout = 15f;

        Scene environmentScene;

        public static EnvironmentController Instance { get; private set; }

        /// <summary>The loaded environment, or null while the first one is loading.</summary>
        public EnvironmentId? Current { get; private set; }

        public bool IsSwitching { get; private set; }

        /// <summary>Why the last switch failed, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>Raised after an environment has loaded and the rig is at its spawn point.</summary>
        public event Action<EnvironmentId> EnvironmentLoaded;

        /// <summary>
        /// Raised while the current environment is still loaded, just before it is torn down for a switch: (leaving, its
        /// scene). The Treatment lab uses it to snapshot the selected Farm specimen before the Farm unloads.
        /// </summary>
        public event Action<EnvironmentId, Scene> Exiting;

        /// <summary>Raised after <see cref="FocusRoutine"/> re-placed the rig inside the already loaded environment.</summary>
        public event Action<EnvironmentId> EnvironmentFocused;

        /// <summary>
        /// Optional standing point for an environment, used instead of its SpawnAnchor when it returns a transform (a
        /// connected XR session uses it to place the user at the selected Farm specimen). Null keeps the SpawnAnchor.
        /// </summary>
        public Func<EnvironmentId, Scene, Transform> SpawnResolver { get; set; }

        /// <summary>Optional check before entering an environment: returns why entry is refused, or null to allow it.</summary>
        public Func<EnvironmentId, string> EntryGate { get; set; }

        /// <summary>Raised with the reason when <see cref="EntryGate"/> refused an entry.</summary>
        public event Action<EnvironmentId, string> EntryRefused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        internal void Bind(string farmScene, string labScene, Transform rig, Transform headCamera, FadeOverlay fadeOverlay)
        {
            farmSceneName = farmScene;
            labSceneName = labScene;
            rigRoot = rig;
            head = headCamera;
            fade = fadeOverlay;
        }

        internal void BindTreatment(string treatmentScene) => treatmentSceneName = treatmentScene;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[BTP] A second EnvironmentController was created; only XRBootstrap should contain one.", this);
                Destroy(this);
                return;
            }

            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        IEnumerator Start()
        {
            IsSwitching = true;

            // Give head tracking a couple of frames to report a real pose before placing the rig.
            yield return null;
            yield return null;

            // Pressing Play in an environment scene loads XRBootstrap next to it (see
            // BootstrapGuard); adopt that environment instead of loading the default one.
            if (TryGetLoadedEnvironment(out var id, out var scene))
            {
                environmentScene = scene;
                yield return WaitUntilReady(scene);
                Activate(id);
                if (fade != null)
                    yield return fade.FadeTo(0f, fadeDuration);
                IsSwitching = false;
                yield break;
            }

            IsSwitching = false;
            yield return SwitchRoutine(startEnvironment);
        }

        /// <summary>Starts a switch. Ignored while another switch is running, so repeated presses never overlap.</summary>
        public void SwitchEnvironment(EnvironmentId target)
        {
            if (IsSwitching || Current == target)
                return;
            StartCoroutine(SwitchRoutine(target));
        }

        /// <summary>Enter Lab: selects Tomato first if the user has not selected a plant.</summary>
        public void EnterLab()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.EnsureSelection();
            SwitchEnvironment(EnvironmentId.Lab);
        }

        /// <summary>Enter the Treatment lab with the case selected in the visiting Lab.</summary>
        public void EnterTreatment()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.EnsureSelection();
            var refusal = EntryGate?.Invoke(EnvironmentId.Treatment);
            if (refusal != null)
            {
                LastError = refusal;
                Debug.Log("[BTP] Enter Treatment refused: " + refusal);
                EntryRefused?.Invoke(EnvironmentId.Treatment, refusal);
                return;
            }

            SwitchEnvironment(EnvironmentId.Treatment);
        }

        /// <summary>
        /// Brings the user to <paramref name="target"/>'s standing point (see <see cref="SpawnResolver"/>). From another
        /// environment this is the normal additive switch. In the environment already loaded, where
        /// <see cref="SwitchEnvironment"/> returns early, it fades out, moves the rig and fades in. Waits for a running
        /// switch first; the routine ends when the user can see the result.
        /// </summary>
        public IEnumerator FocusRoutine(EnvironmentId target)
        {
            while (IsSwitching)
                yield return null;
            if (Current != target)
            {
                yield return SwitchRoutine(target);
                yield break;
            }

            IsSwitching = true;
            LastError = null;
            if (fade != null)
                yield return fade.FadeTo(1f, fadeDuration);
            var anchor = ResolveAnchor(target, environmentScene);
            if (anchor != null)
                PlaceRig(anchor);
            EnvironmentFocused?.Invoke(target);
            yield return null;
            if (fade != null)
                yield return fade.FadeTo(0f, fadeDuration);
            IsSwitching = false;
        }

        public void ReturnToFarm() => SwitchEnvironment(EnvironmentId.Farm);

        public void ReturnToLab() => SwitchEnvironment(EnvironmentId.Lab);

        /// <summary>Moves the user back to the current environment's spawn point, facing forward.</summary>
        public void Recenter()
        {
            if (IsSwitching || !environmentScene.IsValid())
                return;
            var anchor = ResolveAnchor(Current ?? startEnvironment, environmentScene);
            if (anchor != null)
                PlaceRig(anchor);
        }

        /// <summary>Moves the user to a standing point in the current environment (e.g. a Treatment station's anchor), facing its forward.</summary>
        public void MoveTo(Transform anchor)
        {
            if (IsSwitching || anchor == null)
                return;
            PlaceRig(anchor);
        }

        IEnumerator SwitchRoutine(EnvironmentId target)
        {
            IsSwitching = true;
            LastError = null;
            var previous = Current;

            // Save lesson state and release held tools before anything is torn down.
            if (environmentScene.IsValid() && environmentScene.isLoaded)
            {
                if (previous.HasValue)
                {
                    try
                    {
                        Exiting?.Invoke(previous.Value, environmentScene);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }

                NotifyExit(environmentScene);
            }

            if (fade != null)
                yield return fade.FadeTo(1f, fadeDuration);

            if (environmentScene.IsValid() && environmentScene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(environmentScene);
                yield return Resources.UnloadUnusedAssets();
            }

            var sceneName = SceneNameFor(target);
            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                LastError = $"Scene '{sceneName}' is not in the build profile's scene list.";
                Debug.LogError("[BTP] " + LastError);
                // Leave the user somewhere usable: reload the environment they came from.
                IsSwitching = false;
                if (previous.HasValue && previous.Value != target)
                {
                    Current = null;
                    yield return SwitchRoutine(previous.Value);
                }
                else if (fade != null)
                {
                    yield return fade.FadeTo(0f, fadeDuration);
                }

                yield break;
            }

            yield return load;
            environmentScene = SceneManager.GetSceneByName(sceneName);
            SceneManager.SetActiveScene(environmentScene);

            // Wait until database-backed scenes have configured themselves, then place the rig.
            yield return WaitUntilReady(environmentScene);
            Activate(target);

            // One frame for the new scene's late setup before it becomes visible.
            yield return null;
            if (fade != null)
                yield return fade.FadeTo(0f, fadeDuration);
            IsSwitching = false;
        }

        IEnumerator WaitUntilReady(Scene scene)
        {
            var waiting = new List<IEnvironmentReadiness>();
            var deadline = Time.realtimeSinceStartup + readyTimeout;
            // Start methods run on the first frame after loading.
            yield return null;
            while (Time.realtimeSinceStartup < deadline)
            {
                waiting.Clear();
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var component in root.GetComponentsInChildren<IEnvironmentReadiness>(true))
                        if (!component.IsEnvironmentReady)
                            waiting.Add(component);
                if (waiting.Count == 0)
                    yield break;
                yield return null;
            }

            LastError = $"'{scene.name}' did not finish loading in time.";
            Debug.LogError("[BTP] " + LastError);
        }

        static void NotifyExit(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<IEnvironmentExit>(true))
                {
                    try
                    {
                        component.PrepareForExit();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
        }

        void Activate(EnvironmentId target)
        {
            // The active scene supplies the skybox, fog and ambient lighting.
            SceneManager.SetActiveScene(environmentScene);
            DynamicGI.UpdateEnvironment();

            var anchor = ResolveAnchor(target, environmentScene);
            if (anchor != null)
                PlaceRig(anchor);
            else
                Debug.LogError($"[BTP] Scene '{environmentScene.name}' has no SpawnAnchor.");

            Current = target;
            EnvironmentLoaded?.Invoke(target);
        }

        /// <summary>
        /// Turns the rig about the head so the user faces the anchor's forward direction, then
        /// moves it so the head is above the anchor and the rig's floor is at the anchor's height.
        /// </summary>
        void PlaceRig(Transform anchor)
        {
            if (rigRoot == null || head == null)
            {
                Debug.LogError("[BTP] EnvironmentController needs the XR rig root and head camera.", this);
                return;
            }

            var headForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var targetForward = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
            if (headForward.sqrMagnitude > 1e-4f && targetForward.sqrMagnitude > 1e-4f)
                rigRoot.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(headForward, targetForward, Vector3.up));

            var offset = anchor.position - head.position;
            offset.y = anchor.position.y - rigRoot.position.y;
            rigRoot.position += offset;
            Physics.SyncTransforms();
        }

        /// <summary>Explicit scene for every environment.</summary>
        string SceneNameFor(EnvironmentId id)
        {
            switch (id)
            {
                case EnvironmentId.Farm: return farmSceneName;
                case EnvironmentId.Lab: return labSceneName;
                case EnvironmentId.Treatment: return treatmentSceneName;
                default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        bool TryGetLoadedEnvironment(out EnvironmentId id, out Scene scene)
        {
            foreach (EnvironmentId candidate in Enum.GetValues(typeof(EnvironmentId)))
            {
                scene = SceneManager.GetSceneByName(SceneNameFor(candidate));
                if (scene.IsValid() && scene.isLoaded)
                {
                    id = candidate;
                    return true;
                }
            }

            id = default;
            scene = default;
            return false;
        }

        /// <summary>The resolver's standing point when it has one, else the scene's SpawnAnchor.</summary>
        Transform ResolveAnchor(EnvironmentId id, Scene scene)
        {
            Transform resolved = null;
            try
            {
                resolved = SpawnResolver?.Invoke(id, scene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (resolved != null)
                return resolved;
            var anchor = FindSpawnAnchor(scene);
            return anchor != null ? anchor.transform : null;
        }

        static SpawnAnchor FindSpawnAnchor(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var anchor = root.GetComponentInChildren<SpawnAnchor>(true);
                if (anchor != null)
                    return anchor;
            }

            return null;
        }
    }
}