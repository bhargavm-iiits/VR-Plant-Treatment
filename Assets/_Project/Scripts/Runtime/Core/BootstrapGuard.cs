using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Core
{
    /// <summary>
    /// Lets Farm.unity or Lab.unity be played on their own in the Editor. When the persistent
    /// XRBootstrap scene is not loaded, this environment's other root objects wait inactive
    /// while XRBootstrap loads next to it (and adopts it), so their scripts always find the
    /// shared selection and environment controller. Place it on its own root object.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class BootstrapGuard : MonoBehaviour
    {
        [SerializeField] string bootstrapSceneName = "XRBootstrap";

        void Awake()
        {
            if (EnvironmentController.Instance != null || SceneManager.GetSceneByName(bootstrapSceneName).isLoaded)
                return;

            var waiting = new List<GameObject>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root != gameObject && root.activeSelf)
                {
                    root.SetActive(false);
                    waiting.Add(root);
                }
            }

            StartCoroutine(LoadBootstrap(waiting));
        }

        IEnumerator LoadBootstrap(List<GameObject> waiting)
        {
            yield return SceneManager.LoadSceneAsync(bootstrapSceneName, LoadSceneMode.Additive);
            foreach (var root in waiting)
            {
                if (root != null)
                    root.SetActive(true);
            }
        }
    }
}
