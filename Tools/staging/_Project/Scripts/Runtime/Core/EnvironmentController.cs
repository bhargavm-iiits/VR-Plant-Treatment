using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Core
{
    /// <summary>
    /// Keeps exactly one environment scene (Farm or Lab) loaded next to the persistent
    /// XRBootstrap scene. A switch fades out, unloads the old environment, loads the new one,
    /// moves the XR rig to its SpawnAnchor and fades in. The single XR rig in XRBootstrap is
    /// kept for the whole session, so cameras and input modules are never duplicated.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class EnvironmentController : MonoBehaviour
    {
        [SerializeField] string farmSceneName = "Envirornment";
        [SerializeField] string labSceneName = "Lab";
        [SerializeField] EnvironmentId startEnvironment = EnvironmentId.Farm;

        [Tooltip("Root of the XR rig (the XR Origin).")]
        [SerializeField] Transform rigRoot;
        [Tooltip("The tracked head camera inside the rig.")]
        [SerializeField] Transform head;
        [SerializeField] FadeOverlay fade;
        [SerializeField, Min(0f)] float fadeDuration = 0.35f;

        Scene environmentScene;

        public static EnvironmentController Instance { get; private set; }

        /// <summary>The loaded environment, or null while the first one is loading.</summary>
        public EnvironmentId? Current { get; private set; }

        public bool IsSwitching { get; private set; }

        /// <summary>Raised after an environment has loaded and the rig is at its spawn point.</summary>
        public event Action<EnvironmentId> EnvironmentLoaded;

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

            // Pressing Play in Farm.unity or Lab.unity loads XRBootstrap next to it (see
            // BootstrapGuard); adopt that environment instead of loading the default one.
            if (TryGetLoadedEnvironment(out var id, out var scene))
            {
                environmentScene = scene;
                Activate(id);
                if (fade != null)
                    yield return fade.FadeTo(0f, fadeDuration);
                IsSwitching = false;
                yield break;
            }

            IsSwitching = false;
            yield return SwitchRoutine(startEnvironment);
        }

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

        public void ReturnToFarm() => SwitchEnvironment(EnvironmentId.Farm);

        /// <summary>Moves the user back to the current environment's spawn point, facing forward.</summary>
        public void Recenter()
        {
            if (IsSwitching || !environmentScene.IsValid())
                return;
            var anchor = FindSpawnAnchor(environmentScene);
            if (anchor != null)
                PlaceRig(anchor.transform);
        }

        IEnumerator SwitchRoutine(EnvironmentId target)
        {
            IsSwitching = true;
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
                Debug.LogError($"[BTP] Scene '{sceneName}' is not in the build profile's scene list.");
                IsSwitching = false;
                yield break;
            }

            yield return load;
            environmentScene = SceneManager.GetSceneByName(sceneName);
            Activate(target);

            // One frame for the new scene's Start methods before it becomes visible.
            yield return null;
            if (fade != null)
                yield return fade.FadeTo(0f, fadeDuration);
            IsSwitching = false;
        }

        void Activate(EnvironmentId target)
        {
            // The active scene supplies the skybox, fog and ambient lighting.
            SceneManager.SetActiveScene(environmentScene);
            DynamicGI.UpdateEnvironment();

            var anchor = FindSpawnAnchor(environmentScene);
            if (anchor != null)
                PlaceRig(anchor.transform);
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

        string SceneNameFor(EnvironmentId id) => id == EnvironmentId.Farm ? farmSceneName : labSceneName;

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
