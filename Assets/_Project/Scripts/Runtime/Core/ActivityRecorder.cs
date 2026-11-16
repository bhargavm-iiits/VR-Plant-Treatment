using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BTP.XR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BTP.Core
{
    /// <summary>
    /// The study's activity log for the app: what the learner did, sent to the CropScan API's POST /api/activity (the
    /// same table the website writes to, with source "unity").
    ///
    /// Recorded on its own: scene loads, the environment entered or refused, the plant and disease selected, every
    /// XR grab and trigger press on an interactable, UI selections, where the head is looking (once a second, only
    /// when it has moved), and the app gaining or losing focus. Other scripts can add their own line with
    /// <see cref="Log(string,string,string)"/>.
    ///
    /// It creates itself when the app starts; nothing has to be placed in a scene. It never blocks play: events wait
    /// in memory, go out in small batches, and are retried if the API is unreachable. Each run has one visit ID and
    /// every event a sequence number, so a batch sent twice is stored once. No name, photo or free text is recorded.
    ///
    /// The API address is "-xr-api &lt;url&gt;" from the command line when present, else the PlayerPrefs key
    /// "btp.activity.api", else the laptop demo's http://127.0.0.1:8000. Set that key to "off" to record nothing.
    /// </summary>
    public sealed class ActivityRecorder : MonoBehaviour
    {
        const string ApiPref = "btp.activity.api";
        const string DefaultApi = "http://127.0.0.1:8000";
        const int MaxQueued = 2000;
        const int BatchSize = 60;
        const float FlushSeconds = 3f;
        const float PoseSeconds = 1f;
        const float RescanSeconds = 5f;

        static ActivityRecorder instance;

        readonly List<string> queue = new List<string>();
        readonly HashSet<XRBaseInteractable> watched = new HashSet<XRBaseInteractable>();
        string visitId;
        string endpoint;
        int nextSeq;
        bool sending;
        float retryAt;
        GameObject lastUiSelection;
        Vector3 lastPosePosition;
        Vector3 lastPoseForward;
        SelectionState boundSelection;
        EnvironmentController boundEnvironment;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null || Application.isBatchMode)
                return;
            var api = XrLaunchArgs.Parse(Environment.GetCommandLineArgs()).ApiBaseUrl;
            if (string.IsNullOrEmpty(api))
                api = PlayerPrefs.GetString(ApiPref, DefaultApi);
            if (string.IsNullOrEmpty(api) || api == "off")
                return;
            var host = new GameObject("[ActivityRecorder]");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ActivityRecorder>();
            instance.endpoint = api.TrimEnd('/') + "/api/activity";
            instance.visitId = Guid.NewGuid().ToString("D");
        }

        /// <summary>Adds one line to the log. Safe to call from anywhere on the main thread; a no-op when recording
        /// is off. <paramref name="detail"/> is a short label, never free text typed by a person.</summary>
        public static void Log(string kind, string target = null, string detail = null)
        {
            if (instance != null)
                instance.Add(kind, target, detail == null ? null : "{\"detail\":" + Quote(detail) + "}");
        }

        void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.focusChanged += OnFocusChanged;
            Add("app_started", Application.productName,
                "{\"version\":" + Quote(Application.version) + ",\"platform\":" + Quote(Application.platform.ToString()) + "}");
            Add("scene_loaded", SceneManager.GetActiveScene().name, null);
            StartCoroutine(Flusher());
            StartCoroutine(Sampler());
            StartCoroutine(Scanner());
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.focusChanged -= OnFocusChanged;
            Unbind();
            foreach (var interactable in watched)
            {
                if (interactable == null)
                    continue;
                interactable.selectEntered.RemoveListener(OnSelect);
                interactable.activated.RemoveListener(OnActivate);
            }

            watched.Clear();
            if (instance == this)
                instance = null;
        }

        void OnApplicationQuit()
        {
            Add("app_quit", null, null);
            // Best effort: the request is started, but the process may end before it completes.
            Send(TakeBatch());
        }

        // --- what is recorded ---------------------------------------------------------------------------------------

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Add("scene_loaded", scene.name, "{\"mode\":" + Quote(mode.ToString()) + "}");
            Watch();
        }

        void OnFocusChanged(bool focused) => Add(focused ? "app_focus" : "app_blur", null, null);

        void OnEnvironmentLoaded(EnvironmentId id) => Add("environment_loaded", id.ToString(), null);
        void OnEnvironmentFocused(EnvironmentId id) => Add("environment_entered", id.ToString(), null);
        void OnEntryRefused(EnvironmentId id, string reason) => Add("environment_refused", id.ToString(), "{\"reason\":" + Quote(reason) + "}");
        void OnCaseChanged(string plantId, string diseaseId) => Add("case_selected", plantId, "{\"disease\":" + Quote(diseaseId) + "}");
        void OnSelectionRefused(string reason) => Add("selection_refused", null, "{\"reason\":" + Quote(reason) + "}");

        void OnSelect(SelectEnterEventArgs args) => Add("xr_select", NameOf(args.interactableObject), InteractorJson(args.interactorObject));
        void OnActivate(ActivateEventArgs args) => Add("xr_activate", NameOf(args.interactableObject), InteractorJson(args.interactorObject));

        static string NameOf(object interaction)
        {
            var component = interaction as Component;
            return component != null ? PathOf(component.transform) : null;
        }

        static string InteractorJson(object interactor)
        {
            var component = interactor as Component;
            return component != null ? "{\"interactor\":" + Quote(component.name) + "}" : null;
        }

        /// <summary>The object's name with up to two parents, enough to tell "Tomato/Leaf" from "Potato/Leaf".</summary>
        static string PathOf(Transform transform)
        {
            var path = transform.name;
            var parent = transform.parent;
            for (var depth = 0; depth < 2 && parent != null; depth++, parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        /// <summary>The singletons live in XRBootstrap and the interactables in each environment, so both are looked
        /// for again every few seconds instead of once.</summary>
        IEnumerator Scanner()
        {
            var wait = new WaitForSecondsRealtime(RescanSeconds);
            while (true)
            {
                Bind();
                Watch();
                yield return wait;
            }
        }

        void Bind()
        {
            var selection = SelectionState.Instance;
            if (selection != boundSelection)
            {
                if (boundSelection != null)
                {
                    boundSelection.CaseChanged -= OnCaseChanged;
                    boundSelection.SelectionRefused -= OnSelectionRefused;
                }

                boundSelection = selection;
                if (selection != null)
                {
                    selection.CaseChanged += OnCaseChanged;
                    selection.SelectionRefused += OnSelectionRefused;
                }
            }

            var environment = EnvironmentController.Instance;
            if (environment != boundEnvironment)
            {
                if (boundEnvironment != null)
                {
                    boundEnvironment.EnvironmentLoaded -= OnEnvironmentLoaded;
                    boundEnvironment.EnvironmentFocused -= OnEnvironmentFocused;
                    boundEnvironment.EntryRefused -= OnEntryRefused;
                }

                boundEnvironment = environment;
                if (environment != null)
                {
                    environment.EnvironmentLoaded += OnEnvironmentLoaded;
                    environment.EnvironmentFocused += OnEnvironmentFocused;
                    environment.EntryRefused += OnEntryRefused;
                }
            }
        }

        void Unbind()
        {
            if (boundSelection != null)
            {
                boundSelection.CaseChanged -= OnCaseChanged;
                boundSelection.SelectionRefused -= OnSelectionRefused;
                boundSelection = null;
            }

            if (boundEnvironment != null)
            {
                boundEnvironment.EnvironmentLoaded -= OnEnvironmentLoaded;
                boundEnvironment.EnvironmentFocused -= OnEnvironmentFocused;
                boundEnvironment.EntryRefused -= OnEntryRefused;
                boundEnvironment = null;
            }
        }

        void Watch()
        {
            watched.RemoveWhere(interactable => interactable == null);
            foreach (var interactable in FindObjectsByType<XRBaseInteractable>())
            {
                if (!watched.Add(interactable))
                    continue;
                interactable.selectEntered.AddListener(OnSelect);
                interactable.activated.AddListener(OnActivate);
            }
        }

        /// <summary>Once a second: a UI selection that changed, and the head pose when it has moved or turned.</summary>
        IEnumerator Sampler()
        {
            var wait = new WaitForSecondsRealtime(PoseSeconds);
            while (true)
            {
                yield return wait;
                var system = EventSystem.current;
                var selected = system != null ? system.currentSelectedGameObject : null;
                if (selected != lastUiSelection)
                {
                    lastUiSelection = selected;
                    if (selected != null)
                        Add("ui_select", PathOf(selected.transform), null);
                }

                var view = Camera.main;
                if (view == null)
                    continue;
                var position = view.transform.position;
                var forward = view.transform.forward;
                if ((position - lastPosePosition).sqrMagnitude < 0.01f && Vector3.Angle(forward, lastPoseForward) < 5f)
                    continue;
                lastPosePosition = position;
                lastPoseForward = forward;
                var angles = view.transform.eulerAngles;
                Add("head_pose", null,
                    "{\"x\":" + Number(position.x) + ",\"y\":" + Number(position.y) + ",\"z\":" + Number(position.z)
                    + ",\"yaw\":" + Number(angles.y) + ",\"pitch\":" + Number(angles.x) + "}");
            }
        }

        // --- queue and delivery -------------------------------------------------------------------------------------

        void Add(string kind, string target, string dataJson)
        {
            if (queue.Count >= MaxQueued)
                return;     // the API has been unreachable for a long time; keep the oldest part of the run
            var line = new StringBuilder(160);
            line.Append("{\"seq\":").Append(nextSeq++)
                .Append(",\"kind\":").Append(Quote(kind))
                .Append(",\"ts\":").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .Append(",\"path\":").Append(Quote(SceneManager.GetActiveScene().name));
            if (!string.IsNullOrEmpty(target))
                line.Append(",\"target\":").Append(Quote(target));
            if (!string.IsNullOrEmpty(dataJson))
                line.Append(",\"data\":").Append(dataJson);
            line.Append('}');
            queue.Add(line.ToString());
        }

        IEnumerator Flusher()
        {
            var wait = new WaitForSecondsRealtime(FlushSeconds);
            while (true)
            {
                yield return wait;
                if (sending || queue.Count == 0 || Time.realtimeSinceStartup < retryAt)
                    continue;
                yield return Deliver();
            }
        }

        List<string> TakeBatch() => queue.GetRange(0, Mathf.Min(BatchSize, queue.Count));

        UnityWebRequest Send(List<string> batch)
        {
            if (batch.Count == 0)
                return null;
            var body = new StringBuilder(batch.Count * 160);
            body.Append("{\"source\":\"unity\",\"visitId\":").Append(Quote(visitId))
                .Append(",\"viewport\":").Append(Quote(Screen.width + "x" + Screen.height))
                .Append(",\"events\":[").Append(string.Join(",", batch)).Append("]}");
            var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString())),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 8,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SendWebRequest();
            return request;
        }

        IEnumerator Deliver()
        {
            sending = true;
            var batch = TakeBatch();
            var request = Send(batch);
            if (request != null)
            {
                yield return new WaitUntil(() => request.isDone);
                if (request.result == UnityWebRequest.Result.Success)
                    queue.RemoveRange(0, Mathf.Min(batch.Count, queue.Count));
                else if (request.responseCode >= 400 && request.responseCode < 500)
                    queue.RemoveRange(0, Mathf.Min(batch.Count, queue.Count));   // refused for good: retrying cannot help
                else
                    retryAt = Time.realtimeSinceStartup + 15f;                    // unreachable: the same events go again
                request.Dispose();
            }

            sending = false;
        }

        // --- JSON, written by hand so the event's data can be any small object ---------------------------------------

        static string Number(float value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

        static string Quote(string text)
        {
            if (text == null)
                return "null";
            if (text.Length > 160)
                text = text.Substring(0, 160);
            var quoted = new StringBuilder(text.Length + 2);
            quoted.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            quoted.Append(c);
                        break;
                }
            }

            return quoted.Append('"').ToString();
        }
    }
}
