using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BTP.Core;
using BTP.Farm;
using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BTP.XR
{
    /// <summary>
    /// The active CropScan XR session and its launch coordinator, living in XRBootstrap beside SelectionState,
    /// EnvironmentController and TreatmentServices.
    ///
    /// Scans arrive from "Enter the field" on the website: Windows runs CropScanLauncher with the cropscanxr: link, which
    /// starts the player with --crop-scan-id or forwards the ID over the per-user pipe (<see cref="XrScanPipeServer"/>);
    /// both end in <see cref="RequestScan"/>. A scan is turned into a session (paired: POST /api/xr/sessions, where the
    /// backend checks access; staging preview: GET /api/scans plus the case map; or the labelled fixture), validated
    /// against the shared case map, bound (SelectionState.LockCase) and focused at its Farm specimen. Treatment waits for
    /// the pinned recipe; Update Treatment in Lab saves the assessment; Lab and the specimen show the confirmed result.
    /// "Opened in headset" is reported only once XR and the Farm target are ready; before Quest Link is active the case
    /// stays open in the desktop view with an Activate Quest Link prompt.
    ///
    /// Without a scan, -xr-fixture or a configured API it stays invisible, so the offline demo is unchanged. All Unity work
    /// runs on the main thread.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class XrSessionCoordinator : MonoBehaviour, ITreatmentResultSink
    {
        public enum Mode { Offline, Fixture, Preview, Connected }

        enum RecipeState { None, Loading, Ready, Failed }

        sealed class PendingOpen
        {
            /// <summary>A scan from the app link (startup argument or pipe), or null for a launch event.</summary>
            public string ScanId;
            /// <summary>A launch event (socket, fixture), or null for a scan.</summary>
            public XrLaunch Launch;
        }

        const string ActiveSessionSetting = "xr_active_session";
        const string HandledLaunchesSetting = "xr_handled_launches";
        const string PendingSubmitPrefix = "xr_pending_submit:";
        const string FixtureStoreFile = "xr_fixture_store.json";
        const string PreviewStoreFile = "xr_preview_store.json";
        const string DefaultFixtureResource = "XR/Fixtures/xr-test-001";
        /// <summary>Editor only: a PlayerPrefs key set by BTP/XR/Fixture Mode so Play uses the bundled fixture.</summary>
        public const string EditorFixturePref = "btp.xr.editorFixture";
        /// <summary>Editor only: a scan XrEditorBridge received while the Editor was not playing, opened when Play starts.</summary>
        public const string EditorPendingScanPref = "btp.xr.pendingScan";
        const int AutomaticSaveRetries = 2;
        const float HeadsetRetrySeconds = 15f;

        public static XrSessionCoordinator Instance { get; private set; }

        public Mode CurrentMode { get; private set; }

        /// <summary>True while a scan launch waits for the headset; it re-places the rig itself once XR runs.</summary>
        public bool WaitingForHeadset => headsetLaunch != null;
        /// <summary>The bound session, or null.</summary>
        public XrSessionData Session { get; private set; }
        public bool IsPaired => credential != null;
        public bool IsPairing { get; private set; }
        public string PairingMessage { get; private set; }
        public string DeviceId => credential?.deviceId;
        public string ApiBaseUrl => apiBaseUrl;
        public string SocketState { get; private set; } = "off";
        public string ReadinessText { get; private set; } = "unknown";
        /// <summary>The local store in fixture or preview mode (tests and diagnostics), else null.</summary>
        public FixtureXrBackend LocalStore => localStore;
        /// <summary>Why the scan receiver is not listening (another player owns it), or null.</summary>
        public string ReceiverError => pipe?.Error;

        public event Action Changed;

        /// <summary>
        /// Raised when a different scan arrives while the current assessment is unsaved, and again when that question is
        /// answered. The Treatment lab's touchscreen shows it as a modal (<see cref="SwitchPromptPending"/>); the floating
        /// status panel shows the same choice everywhere else.
        /// </summary>
        public event Action SwitchPromptChanged;

        /// <summary>A different scan is waiting for "Open New Scan" / "Keep Current Scan".</summary>
        public bool SwitchPromptPending => confirmLaunch != null;

        /// <summary>The modal's text for the pending switch, or null.</summary>
        public string SwitchPromptText => confirmLaunch == null || confirmSession == null
            ? null
            : $"Scan {confirmSession.scanId} ({CaseTitle(confirmSession)}) was sent to this headset. The assessment of scan {Session?.scanId} " +
              "is not saved yet. It stays on this PC and can be retried when you reopen that scan.";

        /// <summary>The touchscreen's "Open New Scan": the coordinator's existing switch path.</summary>
        public void OpenNewScan() => ConfirmSwitch();

        /// <summary>The touchscreen's "Keep Current Scan": the coordinator's existing keep path.</summary>
        public void KeepCurrentScan() => KeepCurrent();

        /// <summary>
        /// While true (the Treatment lab's touchscreen is up) the floating status panel is hidden and receives no input:
        /// the touchscreen carries the connection state and the scan-switch modal.
        /// </summary>
        public bool StatusPanelSuppressed
        {
            get => statusPanelSuppressed;
            set
            {
                statusPanelSuppressed = value;
                RefreshPanel();
            }
        }

        bool statusPanelSuppressed;

        /// <summary>Test hook: asks the switch question for <paramref name="next"/> as an incoming launch would.</summary>
        internal void PromptSwitchForTests(XrLaunch launch, XrSessionData next)
        {
            confirmLaunch = launch;
            confirmSession = next;
            RefreshPanel();
        }

        XrLaunchArgs args;
        XrPlayerConfig config;
        string apiBaseUrl;
        bool active;
        IXrBackend backend;
        HttpXrBackend http;
        FixtureXrBackend localStore;
        XrSessionData fixtureSeed;
        XrCaseMap map;
        XrSpecimenCatalog specimens;
        XrLaunchDeduper deduper = new XrLaunchDeduper();
        XrLaunchSocket socket;
        XrScanPipeServer pipe;
        XrDeviceCredential credential;
        XrStatusPanel panel;
        TreatmentServices services;

        readonly Queue<PendingOpen> opens = new Queue<PendingOpen>();
        XrLaunch lastLaunch;
        bool handlingOpen;
        string openingScanId;
        string scanAfterPairing;
        string questPairingCode;
        string questPairingFeedback;
        bool questPairingKeypad;
        static bool IsQuest => Application.platform == RuntimePlatform.Android;
        XrLaunch confirmLaunch;
        XrSessionData confirmSession;
        /// <summary>A launch focused in the desktop view, waiting for Quest Link before it may report "opened".</summary>
        XrLaunch headsetLaunch;
        float headsetRetryTimer;
        bool refocusing;

        RecipeState recipeState;
        string recipeMessage;
        string notice;

        TreatmentSubmitState submitState = TreatmentSubmitState.Ready;
        long submitManualSessionId;
        string submitStatus = "";
        XrTreatmentSubmission pendingSubmission;

        Transform specimenAnchor;
        Scene anchorScene;
        string locateError;
        int shownRevision = -1;
        float readinessTimer;
        bool xrReady;

        // ------------------------------------------------------------------ install

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>
        /// Adds the coordinator to XRBootstrap in every run, so a running player can always receive an Enter the field
        /// click. It stays invisible until a scan, -xr-fixture or a configured API needs it. A coordinator already in the
        /// scene (BootstrapSceneBuilder) is used instead.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!TryInstall())
                SceneManager.sceneLoaded += InstallWhenBootstrapLoads;
        }

        static void InstallWhenBootstrapLoads(Scene scene, LoadSceneMode mode)
        {
            if (TryInstall())
                SceneManager.sceneLoaded -= InstallWhenBootstrapLoads;
        }

        static bool TryInstall()
        {
            if (Instance != null)
                return true;
            var selection = SelectionState.Instance;
            if (selection == null)
                return false;
            var go = new GameObject("XR Session Coordinator");
            SceneManager.MoveGameObjectToScene(go, selection.gameObject.scene);
            go.AddComponent<XrSessionCoordinator>();
            return true;
        }

        static XrLaunchArgs ReadArgs()
        {
            var parsed = XrLaunchArgs.Parse(Environment.GetCommandLineArgs());
#if UNITY_EDITOR
            if (!parsed.IsFixture && !parsed.IsConnected)
            {
                var pref = PlayerPrefs.GetString(EditorFixturePref, "");
                if (!string.IsNullOrEmpty(pref))
                    parsed.FixturePath = pref;
            }
#endif
            return parsed;
        }

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            args = ReadArgs();
            config = XrPlayerConfig.Load(out var configError);
            if (configError != null)
                Debug.LogError("[BTP][XR] " + configError);
            apiBaseUrl = config.ResolveApiBaseUrl(args);
            CurrentMode = args.IsFixture ? Mode.Fixture
                : string.IsNullOrEmpty(apiBaseUrl) ? Mode.Offline
                : config.IsPreview && string.IsNullOrEmpty(args.ApiBaseUrl) ? Mode.Preview
                : Mode.Connected;
            services = TreatmentServices.Instance;

            // Windows forwards a link over the local pipe. Quest receives it from the Android activity.
            if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
            {
                pipe = new XrScanPipeServer(XrScanLink.CurrentUserPipeName());
                pipe.Start();
            }

            if (CurrentMode != Mode.Offline)
                Activate();

            if (IsQuest)
            {
                Application.deepLinkActivated += ReceiveQuestLink;
                ReceiveQuestLink(Application.absoluteURL);
            }

            if (XrScanLink.TryFindScanArgument(Environment.GetCommandLineArgs(), out var scanId, out var scanError))
                RequestScan(scanId, "start-up");
            else if (scanError != null)
                ShowProblem(scanError);
#if UNITY_EDITOR
            // Enter the field while the Editor was not playing: XrEditorBridge saved the scan and pressed Play.
            var pending = PlayerPrefs.GetString(EditorPendingScanPref, "");
            if (!string.IsNullOrEmpty(pending))
            {
                PlayerPrefs.DeleteKey(EditorPendingScanPref);
                PlayerPrefs.Save();
                RequestScan(pending, "Unity Editor");
            }
#endif
        }

        void ReceiveQuestLink(string link)
        {
            var opensScan = XrScanLink.TryParseQuestScanUri(link, out var scanId, out _);
            var pairs = XrScanLink.TryParseQuestPairUri(link, XrScanLink.QuestScheme, out var code);
            if (!opensScan && !pairs)
                return;

            if (CurrentMode == Mode.Offline)
            {
                apiBaseUrl = config.ResolveApiBaseUrl(args, XrPlayerConfig.PlatformApiBaseUrl);
                CurrentMode = Mode.Connected;
                Activate();
            }

            if (opensScan)
                RequestScan(scanId, "Quest app link");
            else if (CurrentMode == Mode.Connected && !IsPairing)
            {
                questPairingKeypad = false;
                questPairingCode = code;
                RefreshPanel();
            }
        }

        void SetQuestPairingKeypad(bool open)
        {
            questPairingCode = null;
            questPairingFeedback = null;
            questPairingKeypad = open;
            RefreshPanel();
        }

        void RespondToQuestPairing(bool approve)
        {
            var code = questPairingCode;
            questPairingCode = null;
            if (approve)
                Pair(code);
            else
                RefreshPanel();
        }

        /// <summary>Hooks the coordinator into the shared systems and starts its backend. Idempotent.</summary>
        void Activate()
        {
            if (active)
                return;
            active = true;
            map = XrCaseMap.LoadDefault(out var mapProblems);
            foreach (var problem in mapProblems)
                Debug.LogError("[BTP][XR] Case map: " + problem);
            specimens = XrSpecimenCatalog.LoadDefault(out var specimenProblems);
            foreach (var problem in specimenProblems)
                Debug.LogError("[BTP][XR] Specimens: " + problem);
            deduper = XrLaunchDeduper.Deserialize(GetSetting(HandledLaunchesSetting));

            panel = XrStatusPanel.Create(transform);
            if (Camera.main != null)
                panel.PlaceAbove(Camera.main.transform);
            if (services != null)
                services.ResultSink = this;
            var environment = EnvironmentController.Instance;
            if (environment != null)
            {
                environment.SpawnResolver = ResolveSpawn;
                environment.EntryGate = GateEntry;
                environment.EnvironmentLoaded += OnEnvironmentShown;
                environment.EnvironmentFocused += OnEnvironmentShown;
                environment.EntryRefused += OnEntryRefused;
            }
            else
            {
                Debug.LogError("[BTP][XR] No EnvironmentController: start the player from XRBootstrap.", this);
            }

            if (SelectionState.Instance != null)
                SelectionState.Instance.SelectionRefused += OnSelectionRefused;

            switch (CurrentMode)
            {
                case Mode.Fixture:
                    StartFixture();
                    break;
                case Mode.Preview:
                    StartPreview();
                    break;
                case Mode.Connected:
                    StartConnected();
                    break;
            }

            RefreshPanel();
        }

        void OnDestroy()
        {
            if (Instance != this)
                return;
            Instance = null;
            if (IsQuest)
                Application.deepLinkActivated -= ReceiveQuestLink;
            socket?.Dispose();
            pipe?.Dispose();
            if (!active)
                return;
            if (services != null && ReferenceEquals(services.ResultSink, this))
                services.ResultSink = null;
            var environment = EnvironmentController.Instance;
            if (environment != null)
            {
                environment.SpawnResolver = null;
                environment.EntryGate = null;
                environment.EnvironmentLoaded -= OnEnvironmentShown;
                environment.EnvironmentFocused -= OnEnvironmentShown;
                environment.EntryRefused -= OnEntryRefused;
            }

            if (SelectionState.Instance != null)
                SelectionState.Instance.SelectionRefused -= OnSelectionRefused;
        }

        void OnApplicationQuit() => pipe?.Dispose();

        Func<CaseKey, TreatmentCase> LocalCase => key => services != null ? services.GetBundledCase(key, out _) : null;

        void StartFixture()
        {
            string json;
            string source;
            if (args.FixturePath == XrLaunchArgs.DefaultFixture)
            {
                json = Resources.Load<TextAsset>(DefaultFixtureResource)?.text;
                source = "Resources/" + DefaultFixtureResource + ".json";
            }
            else
            {
                source = args.FixturePath;
                json = File.Exists(source) ? File.ReadAllText(source) : null;
            }

            fixtureSeed = json != null ? FixtureXrBackend.ParseFixture(json, out _) : null;
            if (fixtureSeed == null)
            {
                ShowProblem($"The fixture could not be read ({source}).");
                return;
            }

            localStore = new FixtureXrBackend(fixtureSeed, LocalCase, Path.Combine(Application.persistentDataPath, FixtureStoreFile));
            if (args.ResetFixture)
                localStore.Reset(fixtureSeed);
            localStore.DropNextResponse = args.DropFirstResponse;
            backend = localStore;
            Debug.Log($"[BTP][XR][FIXTURE] Fixture mode: {source} (session {fixtureSeed.xrSessionId}). F9 replays the last launch, F10 sends a new one.");
            LaunchFixture();
        }

        /// <summary>Fixture mode: a fresh launch of the fixture session (what one web click would send).</summary>
        public void LaunchFixture()
        {
            if (fixtureSeed == null)
                return;
            EnqueueLaunch(new XrLaunch { launchId = $"fixture-{fixtureSeed.xrSessionId}-{DateTime.UtcNow.Ticks}", xrSessionId = fixtureSeed.xrSessionId });
        }

        void StartPreview()
        {
            http = new HttpXrBackend(apiBaseUrl, () => null);
            localStore = new FixtureXrBackend(null, LocalCase, Path.Combine(Application.persistentDataPath, PreviewStoreFile));
            localStore.DropNextResponse = args.DropFirstResponse;
            backend = new ScanPreviewXrBackend(http, localStore, map);
            Debug.Log($"[BTP][XR][PREVIEW] Staging preview against {apiBaseUrl}: scans resolve through GET /api/scans; results stay on this PC.");
        }

        void StartConnected()
        {
            credential = XrDeviceCredentialStore.Load();
            http = new HttpXrBackend(apiBaseUrl, () => credential?.deviceToken);
            backend = http;
            if (!IsQuest)
                gameObject.AddComponent<XrPairingWindow>().Bind(this);
            if (credential != null)
                StartSocket();
            else
                PairingMessage = "Not paired yet.";
        }

        void Start()
        {
            if (CurrentMode != Mode.Connected || !IsPaired)
                return;
            // Rebind after a restart: the last session's case, recipe and confirmed result come back without moving the
            // user, unless a scan from the app link is already on its way.
            var last = GetSetting(ActiveSessionSetting);
            if (!string.IsNullOrEmpty(last) && Session == null && opens.Count == 0)
                StartCoroutine(Restore(last));
        }

        IEnumerator Restore(string xrSessionId)
        {
            var task = backend.GetSession(xrSessionId);
            yield return Wait(task);
            var result = Read(task);
            if (!result.Ok || Session != null || handlingOpen || opens.Count > 0)
                yield break;
            if (XrSessionValidator.Validate(result.Value, map, specimens, RecipeSupported).Count == 0)
            {
                Bind(result.Value);
                notice = "Restored your last scan.";
                ShowCurrentEnvironment();
            }
        }

        void Update()
        {
            DrainPipe();
            if (!active)
                return;
            DrainSocket();
            readinessTimer -= Time.unscaledDeltaTime;
            if (readinessTimer <= 0f)
            {
                readinessTimer = 1f;
                var ready = XrReadiness.DisplayRunning(out var state);
                if (ready != xrReady || state != ReadinessText)
                {
                    xrReady = ready;
                    ReadinessText = state;
                    RefreshPanel();
                }

                socket?.SetReadiness(xrReady, ReadinessText);
            }

            if (CurrentMode == Mode.Fixture)
                HandleFixtureKeys();
            UpdateHeadsetWait();

            var environment = EnvironmentController.Instance;
            if (!handlingOpen && !refocusing && confirmLaunch == null && opens.Count > 0 && submitState != TreatmentSubmitState.Saving &&
                environment != null && !environment.IsSwitching && environment.Current.HasValue)
                StartCoroutine(HandleOpen(opens.Dequeue()));
        }

        void HandleFixtureKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f9Key.wasPressedThisFrame && lastLaunch != null)
            {
                Debug.Log($"[BTP][XR][FIXTURE] Replaying launch {lastLaunch.launchId} (it must not move the user again).");
                EnqueueLaunch(lastLaunch);
            }

            if (keyboard.f10Key.wasPressedThisFrame)
                LaunchFixture();
        }

        void ShowProblem(string message)
        {
            Debug.LogWarning("[BTP][XR] " + message);
            Activate();
            notice = message;
            RefreshPanel();
        }

        // ------------------------------------------------------------------ app-link scans

        void DrainPipe()
        {
            if (pipe == null)
                return;
            while (pipe.TryDequeue(out var scanId))
                RequestScan(scanId, "app link");
        }

        /// <summary>
        /// Opens a scan from Enter the field (start-up argument or a click forwarded to the running player). Repeated clicks
        /// for a scan that is already opening or queued are coalesced.
        /// </summary>
        public void RequestScan(string scanId, string source)
        {
            if (!XrScanLink.TryParseScanId(scanId, out scanId))
                return;
            Activate();
            Debug.Log($"[BTP][XR] Scan {scanId} received ({source}).");
            if (CurrentMode == Mode.Offline)
            {
                ShowProblem("This PC has no CropScan address configured. Reinstall CropScan XR with the staging API address " +
                            $"({XrPlayerConfig.FileName}).");
                return;
            }

            if (CurrentMode == Mode.Fixture)
            {
                ShowProblem("Fixture mode ignores scans from the website. Start the player without -xr-fixture to open real scans.");
                return;
            }

            if (scanId == openingScanId || scanId == scanAfterPairing)
            {
                if (IsQuest && scanId == scanAfterPairing && !IsPairing && !questPairingKeypad)
                    SetQuestPairingKeypad(true);
                Debug.Log($"[BTP][XR] Scan {scanId} is already opening; the repeated click is ignored.");
                return;
            }

            foreach (var queued in opens)
                if (queued.ScanId == scanId)
                    return;

            if (CurrentMode == Mode.Connected && !IsPaired)
            {
                scanAfterPairing = scanId;
                if (http != null && http.IsLocalApi)
                {
                    // The API runs on this laptop: pair automatically for the scan's owner, then open it.
                    notice = "Opening your scan...";
                    RefreshPanel();
                    StartCoroutine(PairLocallyThenOpen(scanId));
                    return;
                }

                if (IsQuest)
                    SetQuestPairingKeypad(true);
                else
                {
                    notice = "Pair this PC to open the scan: press Pair VR on the CropScan website and type the code in the window on the PC screen.";
                    RefreshPanel();
                }
                return;
            }

            opens.Enqueue(new PendingOpen { ScanId = scanId });
            notice = "Opening your scan...";
            RefreshPanel();
        }

        void EnqueueLaunch(XrLaunch launch)
        {
            if (launch == null || string.IsNullOrEmpty(launch.launchId))
                return;
            foreach (var queued in opens)
                if (queued.Launch != null && queued.Launch.launchId == launch.launchId)
                    return;
            opens.Enqueue(new PendingOpen { Launch = launch });
        }

        IEnumerator HandleOpen(PendingOpen open)
        {
            handlingOpen = true;
            try
            {
                if (open.Launch != null)
                    yield return HandleLaunch(open.Launch);
                else
                    yield return HandleScan(open.ScanId);
            }
            finally
            {
                handlingOpen = false;
                openingScanId = null;
            }
        }

        IEnumerator HandleScan(string scanId)
        {
            openingScanId = scanId;
            var task = backend.OpenSessionForScan(scanId);
            yield return Wait(task);
            var result = Read(task);
            // Laptop demo: this PC is paired to another browser (or its credential was retired); pair for this scan's
            // owner and try once more.
            if (!result.Ok && (result.Status == 401 || result.Status == 403) && http != null && http.IsLocalApi && CurrentMode == Mode.Connected)
            {
                var pair = http.LocalPair(scanId);
                yield return Wait(pair);
                var paired = Read(pair);
                if (paired.Ok)
                {
                    UseCredential(paired.Value);
                    task = backend.OpenSessionForScan(scanId);
                    yield return Wait(task);
                    result = Read(task);
                }
            }

            if (!result.Ok)
            {
                if (IsQuest && (result.Status == 401 || result.Status == 403))
                {
                    scanAfterPairing = scanId;
                    SetQuestPairingKeypad(true);
                    questPairingFeedback = "Choose Pair VR in the browser that made this scan.";
                    RefreshPanel();
                    yield break;
                }
                // Before a session exists there is no launch record for the web page; the reason shows here.
                ShowProblem("The scan could not be opened: " + result.Error);
                yield break;
            }

            var next = result.Value;
            var problems = XrSessionValidator.Validate(next, map, specimens, RecipeSupported);
            if (problems.Count > 0)
            {
                ShowProblem(problems[0]);
                yield break;
            }

            // A launch record lets the result page show Opening / Opened in headset / the failure reason.
            var launchTask = backend.CreateLaunch(next.xrSessionId, scanId, $"app-link:{scanId}:{Guid.NewGuid():N}");
            yield return Wait(launchTask);
            var created = Read(launchTask);
            XrLaunch launch;
            if (created.Ok && !string.IsNullOrEmpty(created.Value?.launchId))
            {
                launch = created.Value;
            }
            else
            {
                Debug.LogWarning("[BTP][XR] No launch record, so the web page will not show live status: " + created.Error);
                launch = new XrLaunch { launchId = "unrecorded-" + Guid.NewGuid().ToString("N"), xrSessionId = next.xrSessionId };
            }

            deduper.TryBegin(launch.launchId);
            SetSetting(HandledLaunchesSetting, deduper.Serialize());
            lastLaunch = launch;
            Report(launch, XrLaunchStatus.Opening, null);
            yield return OpenOrConfirm(launch, next);
        }

        // ------------------------------------------------------------------ launch events (socket, fixture)

        IEnumerator HandleLaunch(XrLaunch launch)
        {
            // A replayed event, a reconnect that lists the same job or a retried click must not move the user twice.
            if (!deduper.TryBegin(launch.launchId))
            {
                Debug.Log($"[BTP][XR] Launch {launch.launchId} was already handled; ignored.");
                yield break;
            }

            SetSetting(HandledLaunchesSetting, deduper.Serialize());
            lastLaunch = launch;
            if (IsExpired(launch))
            {
                Report(launch, XrLaunchStatus.Failed, "The launch expired before the headset could open it.");
                yield break;
            }

            Report(launch, XrLaunchStatus.Opening, null);
            var task = backend.GetSession(launch.xrSessionId);
            yield return Wait(task);
            var result = Read(task);
            if (!result.Ok)
            {
                Fail(launch, "The scan could not be opened: " + result.Error);
                yield break;
            }

            var next = result.Value;
            var problems = XrSessionValidator.Validate(next, map, specimens, RecipeSupported);
            if (problems.Count > 0)
            {
                Fail(launch, problems[0]);
                yield break;
            }

            yield return OpenOrConfirm(launch, next);
        }

        IEnumerator OpenOrConfirm(XrLaunch launch, XrSessionData next)
        {
            // Switching away from a scan whose assessment is not saved yet needs the learner's confirmation.
            if (Session != null && Session.xrSessionId != next.xrSessionId && HasUnsavedResult)
            {
                confirmLaunch = launch;
                confirmSession = next;
                notice = null;
                RefreshPanel();
                yield break;
            }

            yield return Open(launch, next);
        }

        IEnumerator Open(XrLaunch launch, XrSessionData next)
        {
            if (headsetLaunch != null && headsetLaunch != launch)
                Report(headsetLaunch, XrLaunchStatus.Failed, "Replaced by a newer launch before Quest Link was active.");
            headsetLaunch = null;
            Bind(next);
            var environment = EnvironmentController.Instance;
            yield return environment.FocusRoutine(EnvironmentId.Farm);

            var focused = environment.Current == EnvironmentId.Farm && string.IsNullOrEmpty(environment.LastError) && specimenAnchor != null;
            if (!focused)
            {
                Fail(launch, "The specimen could not be shown: " + (environment.LastError ?? locateError ?? "unknown error"));
                yield break;
            }

            if (xrReady || CurrentMode == Mode.Fixture)
            {
                notice = xrReady ? "Opened in headset." : $"Opened scan {next.scanId} (desktop view, fixture).";
                RefreshPanel();
                Report(launch, XrLaunchStatus.Opened, xrReady ? null : "Opened in the desktop view (fixture, no headset).");
                yield break;
            }

            // The case stays open in the desktop view; "opened" is reported once the headset shows the Farm.
            headsetLaunch = launch;
            headsetRetryTimer = 0f;
            notice = "Activate Quest Link: put on the headset and turn on Link. Your scan stays open.";
            RefreshPanel();
            Report(launch, XrLaunchStatus.Opening, "Waiting for Quest Link.");
        }

        /// <summary>While a launch waits for the headset: retries XR start-up, then refocuses the specimen and reports "opened".</summary>
        void UpdateHeadsetWait()
        {
            if (headsetLaunch == null || refocusing)
                return;
            if (!xrReady)
            {
                headsetRetryTimer -= Time.unscaledDeltaTime;
                if (headsetRetryTimer > 0f)
                    return;
                headsetRetryTimer = HeadsetRetrySeconds;
                // XrAutoStart retries on its own schedule; skip when it (or anyone) just tried.
                if (XrHeadset.AttemptedWithin(XrAutoStart.MinRetrySeconds))
                    return;
                if (!XrHeadset.TryStart(out var message) && message != null)
                {
                    notice = "Activate Quest Link: " + message;
                    RefreshPanel();
                }

                return;
            }

            var environment = EnvironmentController.Instance;
            if (environment == null || environment.IsSwitching || handlingOpen)
                return;
            StartCoroutine(FinishInHeadset(headsetLaunch));
        }

        IEnumerator FinishInHeadset(XrLaunch launch)
        {
            refocusing = true;
            try
            {
                // The rig was placed for the desktop camera; place it again now that the head is tracked.
                var environment = EnvironmentController.Instance;
                yield return environment.FocusRoutine(EnvironmentId.Farm);
                if (headsetLaunch != launch)
                    yield break;
                headsetLaunch = null;
                if (environment.Current == EnvironmentId.Farm && specimenAnchor != null && xrReady)
                {
                    notice = "Opened in headset.";
                    Report(launch, XrLaunchStatus.Opened, null);
                }
                else
                {
                    Fail(launch, "The headset started, but the specimen could not be shown: " + (environment.LastError ?? locateError ?? "unknown error"));
                }

                RefreshPanel();
            }
            finally
            {
                refocusing = false;
            }
        }

        void RetryHeadset()
        {
            headsetRetryTimer = HeadsetRetrySeconds;
            notice = XrHeadset.TryStart(out var message) ? "Headset started." : "Activate Quest Link: " + message;
            RefreshPanel();
        }

        void ConfirmSwitch()
        {
            var launch = confirmLaunch;
            var next = confirmSession;
            confirmLaunch = null;
            confirmSession = null;
            if (launch == null)
                return;
            handlingOpen = true;
            StartCoroutine(RunThenRelease(Open(launch, next)));
        }

        IEnumerator RunThenRelease(IEnumerator routine)
        {
            try
            {
                yield return routine;
            }
            finally
            {
                handlingOpen = false;
            }
        }

        void KeepCurrent()
        {
            if (confirmLaunch != null)
                Report(confirmLaunch, XrLaunchStatus.Failed, "The learner kept the current scan open.");
            confirmLaunch = null;
            confirmSession = null;
            notice = "Kept the current scan.";
            RefreshPanel();
        }

        void Fail(XrLaunch launch, string reason)
        {
            notice = reason;
            Debug.LogWarning($"[BTP][XR] Launch {launch.launchId} failed: {reason}");
            Report(launch, XrLaunchStatus.Failed, reason);
            RefreshPanel();
        }

        void Report(XrLaunch launch, string status, string detail)
        {
            if (backend == null || launch == null || launch.launchId.StartsWith("unrecorded-", StringComparison.Ordinal))
                return;
            var task = backend.ReportLaunchStatus(launch.launchId, status, detail, xrReady);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted || !t.Result.Ok)
                    Debug.LogWarning($"[BTP][XR] Could not report '{status}' for launch {launch.launchId}: " +
                                     (t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result.Error));
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static bool IsExpired(XrLaunch launch) =>
            !string.IsNullOrEmpty(launch.expiresUtc) &&
            DateTime.TryParse(launch.expiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expires) &&
            expires < DateTime.UtcNow;

        bool RecipeSupported(XrCaseMap.Row row)
        {
            if (services == null)
                return false;
            var local = services.GetBundledCase(new CaseKey(row.plantId, row.diseaseId), out _);
            return local != null && TreatmentCaseValidator.ValidateManual(local).Count == 0;
        }

        // ------------------------------------------------------------------ binding

        void Bind(XrSessionData next)
        {
            var previous = Session;
            var sameSession = previous != null && previous.xrSessionId == next.xrSessionId;
            if (previous != null && services != null && previous.Case != next.Case)
                services.ClearCaseOverride(previous.Case);

            Session = next;
            SetSetting(ActiveSessionSetting, next.xrSessionId);
            SelectionState.Instance?.LockCase(next.plantId, next.diseaseId,
                $"Scan {next.scanId} is open. Release it on the scan panel before choosing another case.");

            specimenAnchor = null;
            anchorScene = default;
            if (!sameSession)
            {
                shownRevision = next.revision;
                submitState = TreatmentSubmitState.Ready;
                submitManualSessionId = 0;
                submitStatus = "";
                pendingSubmission = LoadPendingSubmission(next.xrSessionId);
                if (pendingSubmission != null)
                {
                    submitState = TreatmentSubmitState.Failed;
                    submitManualSessionId = pendingSubmission.manualSessionId;
                    submitStatus = "An assessment of this scan was not saved yet. Press Retry save.";
                }

                StartCoroutine(LoadRecipe(next));
            }
            else if (recipeState != RecipeState.Ready)
            {
                StartCoroutine(LoadRecipe(next));
            }

            Changed?.Invoke();
            RefreshPanel();
        }

        /// <summary>Ends the connected binding: the case is unlocked, the recipe override dropped and the Farm entrance used again.</summary>
        public void Release()
        {
            if (Session == null)
                return;
            if (services != null)
                services.ClearCaseOverride(Session.Case);
            SelectionState.Instance?.UnlockCase();
            if (headsetLaunch != null)
                Report(headsetLaunch, XrLaunchStatus.Failed, "The learner released the scan before Quest Link was active.");
            headsetLaunch = null;
            Session = null;
            recipeState = RecipeState.None;
            pendingSubmission = null;
            submitState = TreatmentSubmitState.Ready;
            submitStatus = "";
            specimenAnchor = null;
            SetSetting(ActiveSessionSetting, "");
            notice = "Scan released: the Farm and Lab are free to explore.";
            Changed?.Invoke();
            RefreshPanel();
        }

        IEnumerator LoadRecipe(XrSessionData session)
        {
            recipeState = RecipeState.Loading;
            recipeMessage = "Loading the treatment recipe...";
            RefreshPanel();
            var task = backend.GetRecipe(session.xrSessionId);
            yield return Wait(task);
            if (Session == null || Session.xrSessionId != session.xrSessionId)
                yield break;
            var result = Read(task);
            if (!result.Ok)
            {
                recipeState = RecipeState.Failed;
                recipeMessage = "The treatment recipe could not be loaded: " + result.Error;
                RefreshPanel();
                yield break;
            }

            var local = services?.GetBundledCase(session.Case, out _);
            var problems = XrRecipeAdapter.Validate(result.Value, session, local,
                services != null && services.Catalog != null ? (Func<string, bool>)services.Catalog.Has : null);
            if (problems.Count > 0)
            {
                foreach (var problem in problems)
                    Debug.LogWarning($"[BTP][XR] Recipe {result.Value.recipeId} v{result.Value.recipeVersion}: {problem}");
                recipeState = RecipeState.Failed;
                recipeMessage = "This build cannot run the scan's treatment recipe: " + problems[0];
                RefreshPanel();
                yield break;
            }

            services.SetCaseOverride(session.Case, XrRecipeAdapter.Apply(result.Value, local));
            recipeState = RecipeState.Ready;
            recipeMessage = $"Recipe {result.Value.recipeId} v{result.Value.recipeVersion} ({result.Value.source ?? "backend"}) is ready.";
            Changed?.Invoke();
            RefreshPanel();
        }

        string GateEntry(EnvironmentId id)
        {
            if (id != EnvironmentId.Treatment || Session == null)
                return null;
            switch (recipeState)
            {
                case RecipeState.Ready: return null;
                case RecipeState.Failed: return recipeMessage;
                default: return "The treatment recipe for this scan is still loading.";
            }
        }

        void OnEntryRefused(EnvironmentId id, string reason)
        {
            notice = reason;
            RefreshPanel();
        }

        void OnSelectionRefused(string reason)
        {
            notice = reason;
            RefreshPanel();
        }

        // ------------------------------------------------------------------ Farm specimen

        Transform ResolveSpawn(EnvironmentId id, Scene scene)
        {
            if (id != EnvironmentId.Farm || Session == null)
                return null;
            if (specimenAnchor != null && anchorScene == scene)
                return specimenAnchor;
            specimenAnchor = null;
            if (!specimens.TryGet(Session.specimenId, out var specimen))
            {
                locateError = $"Specimen '{Session.specimenId}' is not authored.";
                return null;
            }

            if (!FarmSpecimenLocator.TryLocate(scene, specimen, out var row, out var plant, out locateError))
            {
                Debug.LogError("[BTP][XR] " + locateError);
                return null;
            }

            if (!FarmSpecimenLocator.TryStandingPose(row, plant, specimen.standDistance, out var position, out var rotation, specimen.standSide))
            {
                locateError = $"No clear ground to stand on beside specimen '{specimen.specimenId}'.";
                Debug.LogError("[BTP][XR] " + locateError);
                return null;
            }

            var anchor = new GameObject($"XR Specimen Anchor ({specimen.specimenId})");
            SceneManager.MoveGameObjectToScene(anchor, scene);
            anchor.transform.SetPositionAndRotation(position, rotation);
            specimenAnchor = anchor.transform;
            anchorScene = scene;
            locateError = null;
            return specimenAnchor;
        }

        void OnEnvironmentShown(EnvironmentId id)
        {
            if (Camera.main != null)
                panel.PlaceAbove(Camera.main.transform);
            if (id == EnvironmentId.Farm)
                ShowSpecimen();
            RefreshPanel();
        }

        void ShowCurrentEnvironment()
        {
            var environment = EnvironmentController.Instance;
            if (environment != null && environment.Current.HasValue && !environment.IsSwitching)
                OnEnvironmentShown(environment.Current.Value);
        }

        /// <summary>Puts the ring and card on the specimen and shows its confirmed result on that plant only.</summary>
        void ShowSpecimen()
        {
            if (Session == null || !specimens.TryGet(Session.specimenId, out var specimen))
                return;
            var scene = SceneManager.GetActiveScene();
            if (!FarmSpecimenLocator.TryLocate(scene, specimen, out var row, out var plant, out var error))
            {
                locateError = error;
                return;
            }

            row.FocusPlant(specimen.plantIndex);
            row.SetSelectedDetail(Session.HasResult
                ? $"Your scan: {XrImprovement.Label(Session.treatmentResult.improvement)} (saved)"
                : "Your scan. Press Enter Lab on the scan panel.");
            if (!Session.HasResult)
                return;
            // Unity's fake-null objects break '??', so test with == null.
            var visual = plant.GetComponent<FarmSpecimenVisual>();
            if (visual == null)
                visual = plant.gameObject.AddComponent<FarmSpecimenVisual>();
            // A result confirmed since the specimen was last shown is played as a change; otherwise it is shown as it is.
            var animate = Session.revision > shownRevision;
            visual.Show(XrImprovement.FarmDiseaseAmount(Session.treatmentResult), animate);
            shownRevision = Session.revision;
        }

        // ------------------------------------------------------------------ ITreatmentResultSink

        public bool Binds(CaseKey key) => active && CurrentMode != Mode.Offline && Session != null && Session.Case == key && recipeState == RecipeState.Ready;

        public string LearnerName => Session != null ? "XR " + Session.xrSessionId : null;

        public string SessionLabel => Session == null ? "" :
            $"{(CurrentMode == Mode.Fixture ? "FIXTURE " : CurrentMode == Mode.Preview ? "PREVIEW " : "")}scan {Session.scanId} (session {Session.xrSessionId})";

        public string StatusText => submitStatus;

        public TreatmentSubmitState StateFor(ManualSessionRecord record)
        {
            if (record == null)
                return TreatmentSubmitState.Ready;
            return record.SessionId == submitManualSessionId ? submitState : TreatmentSubmitState.Ready;
        }

        public bool HasUnsavedResult => pendingSubmission != null && submitState != TreatmentSubmitState.Saved;

        public void Submit(TreatmentCase content, ManualSessionRecord record)
        {
            if (Session == null || record == null || content == null || submitState == TreatmentSubmitState.Saving)
                return;
            // This assessment is already confirmed: never write it a second time, just show it in the Lab.
            if (StateFor(record) == TreatmentSubmitState.Saved)
            {
                EnvironmentController.Instance?.ReturnToLab();
                return;
            }

            if (record.SessionId <= 0)
            {
                submitStatus = "The assessment has not been stored on this PC yet; try again.";
                Changed?.Invoke();
                return;
            }

            StartSubmit(BuildSubmission(content, record));
        }

        void StartSubmit(XrTreatmentSubmission submission)
        {
            pendingSubmission = submission;
            SetSetting(PendingSubmitPrefix + submission.xrSessionId, JsonUtility.ToJson(submission));
            submitManualSessionId = submission.manualSessionId;
            submitState = TreatmentSubmitState.Saving;
            submitStatus = "Saving treatment...";
            Changed?.Invoke();
            RefreshPanel();
            StartCoroutine(SubmitRoutine(submission));
        }

        XrTreatmentSubmission BuildSubmission(TreatmentCase content, ManualSessionRecord record)
        {
            var applied = record.AppliedScore.HasValue;
            var outcome = record.Outcome ?? TreatmentResultManager.OutcomeKey(content.Outcome, applied, record.AppliedValid, record.AppliedScore ?? 0f);
            var values = new List<XrControlValue>();
            foreach (var pair in record.AppliedValues)
                values.Add(new XrControlValue { controlId = pair.Key, value = pair.Value });
            values.Sort((a, b) => string.CompareOrdinal(a.controlId, b.controlId));
            return new XrTreatmentSubmission
            {
                xrSessionId = Session.xrSessionId,
                recipeId = Session.recipeId,
                recipeVersion = Session.recipeVersion,
                expectedRevision = Session.revision,
                manualSessionId = record.SessionId,
                outcome = outcome,
                improvement = XrImprovement.Of(outcome),
                initialSeverity = record.InitialSeverity,
                finalSeverity = record.FinalSeverity ?? record.CurrentSeverity,
                initialHealth = record.InitialHealth,
                finalHealth = record.FinalHealth ?? record.CurrentHealth,
                score = record.AppliedScore ?? 0f,
                valid = record.AppliedValid,
                applyCount = record.ApplyCount,
                healthChecks = record.HealthChecks,
                elapsedSeconds = record.ElapsedSeconds,
                appliedValues = values.ToArray(),
                completedUtc = (record.CompletedUtc ?? record.UpdatedUtc).ToString("o", CultureInfo.InvariantCulture),
            };
        }

        IEnumerator SubmitRoutine(XrTreatmentSubmission submission)
        {
            XrSubmitResponse response = null;
            for (var attempt = 0; attempt <= AutomaticSaveRetries; attempt++)
            {
                if (attempt > 0)
                {
                    submitStatus = $"Saving treatment... (retry {attempt}, same request)";
                    Changed?.Invoke();
                    yield return new WaitForSecondsRealtime(attempt);
                }

                var task = backend.SubmitTreatmentResult(submission);
                yield return Wait(task);
                response = task.IsFaulted
                    ? new XrSubmitResponse { Status = XrSubmitStatus.Failed, Message = task.Exception?.GetBaseException().Message }
                    : task.Result;
                if (response.Status != XrSubmitStatus.Failed)
                    break;
                Debug.LogWarning($"[BTP][XR] Save attempt {attempt + 1} for {submission.IdempotencyKey} got no answer: {response.Message}");
            }

            if (Session == null || Session.xrSessionId != submission.xrSessionId)
                yield break;

            switch (response.Status)
            {
                case XrSubmitStatus.Saved:
                    Session = response.Session ?? Session;
                    pendingSubmission = null;
                    SetSetting(PendingSubmitPrefix + submission.xrSessionId, "");
                    submitState = TreatmentSubmitState.Saved;
                    submitStatus = $"Saved: {TreatmentResultManager.OutcomeLabel(Session.treatmentResult?.outcome)} (revision {Session.revision}).";
                    notice = null;
                    Changed?.Invoke();
                    RefreshPanel();
                    // After the confirmation the learner reviews the saved assessment in the Lab.
                    if (EnvironmentController.Instance != null && EnvironmentController.Instance.Current == EnvironmentId.Treatment)
                        EnvironmentController.Instance.ReturnToLab();
                    break;
                case XrSubmitStatus.Conflict:
                    if (response.Session != null)
                        Session = response.Session;
                    submitState = TreatmentSubmitState.Conflict;
                    submitStatus = $"A newer result (revision {Session.revision}) was saved for this scan and has been loaded. " +
                                   "Review it in the Lab, or save again to replace it.";
                    Changed?.Invoke();
                    RefreshPanel();
                    break;
                case XrSubmitStatus.Rejected:
                    pendingSubmission = null;
                    SetSetting(PendingSubmitPrefix + submission.xrSessionId, "");
                    submitState = TreatmentSubmitState.Rejected;
                    submitStatus = "The assessment was refused: " + response.Message;
                    Changed?.Invoke();
                    RefreshPanel();
                    break;
                default:
                    submitState = TreatmentSubmitState.Failed;
                    submitStatus = "Not saved yet: " + response.Message + " Your assessment is kept; press Retry.";
                    Changed?.Invoke();
                    RefreshPanel();
                    break;
            }
        }

        /// <summary>Retries a kept assessment (after a failure, or one kept across a restart) with its original idempotency key.</summary>
        void RetryPending()
        {
            if (pendingSubmission == null || Session == null || submitState == TreatmentSubmitState.Saving)
                return;
            if (submitState == TreatmentSubmitState.Conflict)
                pendingSubmission.expectedRevision = Session.revision;
            StartSubmit(pendingSubmission);
        }

        XrTreatmentSubmission LoadPendingSubmission(string xrSessionId)
        {
            var json = GetSetting(PendingSubmitPrefix + xrSessionId);
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                var submission = JsonUtility.FromJson<XrTreatmentSubmission>(json);
                return submission != null && submission.xrSessionId == xrSessionId ? submission : null;
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ connected mode: pairing and socket

        public async void Pair(string code)
        {
            if (http == null || CurrentMode != Mode.Connected || IsPairing || string.IsNullOrWhiteSpace(code))
                return;
            IsPairing = true;
            PairingMessage = "Pairing...";
            questPairingFeedback = "Checking the code...";
            RefreshPanel();
            XrResult<XrDeviceCredential> result;
            try
            {
                result = await http.RedeemPairing(code);
            }
            catch (Exception exception)
            {
                result = XrResult<XrDeviceCredential>.Fail(exception.Message);
            }

            IsPairing = false;
            if (!result.Ok)
            {
                PairingMessage = "Pairing failed: " + result.Error;
                if (IsQuest)
                {
                    questPairingFeedback = result.Error;
                    if (!questPairingKeypad)
                        notice = PairingMessage;
                }
                RefreshPanel();
                return;
            }

            credential = result.Value;
            XrDeviceCredentialStore.Save(credential);
            PairingMessage = $"Paired as device {credential.deviceId}.";
            if (IsQuest)
            {
                questPairingKeypad = false;
                questPairingFeedback = null;
                notice = "Quest paired. Open a scan from the CropScan website.";
            }
            StartSocket();
            // A scan clicked before pairing is opened now.
            var waiting = scanAfterPairing;
            scanAfterPairing = null;
            if (waiting != null)
                RequestScan(waiting, "after pairing");
            RefreshPanel();
        }

        IEnumerator PairLocallyThenOpen(string scanId)
        {
            var pair = http.LocalPair(scanId);
            yield return Wait(pair);
            var result = Read(pair);
            if (!result.Ok)
            {
                PairingMessage = "Automatic pairing failed: " + result.Error;
                notice = "This PC could not pair itself (" + result.Error + "). Press Pair VR on the website and type the code here.";
                RefreshPanel();
                yield break;
            }

            UseCredential(result.Value);
            var waiting = scanAfterPairing;
            scanAfterPairing = null;
            if (waiting != null)
                RequestScan(waiting, "after automatic pairing");
        }

        void UseCredential(XrDeviceCredential paired)
        {
            credential = paired;
            XrDeviceCredentialStore.Save(credential);
            PairingMessage = $"Paired automatically as device {credential.deviceId}.";
            StartSocket();
            RefreshPanel();
        }

        public void Unpair()
        {
            socket?.Dispose();
            socket = null;
            credential = null;
            XrDeviceCredentialStore.Clear();
            SocketState = "off";
            PairingMessage = "Unpaired. Pair again to open scans.";
            RefreshPanel();
        }

        void StartSocket()
        {
            socket?.Dispose();
            var url = !string.IsNullOrEmpty(args.SocketUrl) ? args.SocketUrl
                : !string.IsNullOrEmpty(config.socketUrl) ? config.socketUrl
                : XrLaunchArgs.SocketUrlFor(apiBaseUrl);
            if (string.IsNullOrEmpty(url))
            {
                SocketState = "no socket URL";
                return;
            }

            socket = new XrLaunchSocket(url, credential?.deviceId, () => credential?.deviceToken);
            socket.SetReadiness(xrReady, ReadinessText);
            socket.Start();
        }

        void DrainSocket()
        {
            if (socket == null)
                return;
            while (socket.TryDequeue(out var e))
            {
                if (e.StateChanged.HasValue)
                {
                    SocketState = e.StateChanged.Value == XrLaunchSocket.State.Connected ? "connected" :
                        e.StateChanged.Value == XrLaunchSocket.State.Connecting ? "connecting" :
                        string.IsNullOrEmpty(e.Note) ? "disconnected" : "disconnected (" + e.Note + ")";
                    if (e.StateChanged.Value == XrLaunchSocket.State.Connected)
                        StartCoroutine(RecoverPending());
                    RefreshPanel();
                }
                else if (e.Message != null && e.Message.type == "vr.launch")
                {
                    // A remote launch to this paired device (a later flow); the same-laptop button uses the app link.
                    EnqueueLaunch(new XrLaunch { launchId = e.Message.launchId, xrSessionId = e.Message.xrSessionId });
                }
            }
        }

        /// <summary>After (re)connecting, reads this device's pending launches and queues those not handled yet.</summary>
        IEnumerator RecoverPending()
        {
            var task = backend.GetPendingLaunches();
            yield return Wait(task);
            var result = Read(task);
            if (!result.Ok)
            {
                Debug.LogWarning("[BTP][XR] Pending launches could not be read: " + result.Error);
                yield break;
            }

            foreach (var launch in result.Value)
                if (!deduper.Contains(launch.launchId))
                    EnqueueLaunch(launch);
        }

        // ------------------------------------------------------------------ panel

        void RefreshPanel()
        {
            SwitchPromptChanged?.Invoke();
            if (panel == null)
                return;
            var suppressed = statusPanelSuppressed;
            panel.gameObject.SetActive(!suppressed);
            foreach (var raycaster in panel.GetComponentsInChildren<UnityEngine.EventSystems.BaseRaycaster>(true))
                raycaster.enabled = !suppressed;
            if (suppressed)
                return;
            if (questPairingKeypad)
            {
                panel.ShowPairingKeypad(Pair, () => SetQuestPairingKeypad(false), questPairingFeedback, IsPairing);
                return;
            }
            panel.HidePairingKeypad();
            var environment = EnvironmentController.Instance;
            var current = environment != null ? environment.Current : null;
            var actions = new List<XrStatusPanel.Action>();
            var text = new StringBuilder();

            string modeText;
            Color modeColor;
            switch (CurrentMode)
            {
                case Mode.Fixture:
                    modeText = "FIXTURE MODE: test data, not a real scan";
                    modeColor = UIPalette.Warning;
                    break;
                case Mode.Preview:
                    modeText = $"STAGING PREVIEW: real scan, results saved on this PC only | headset {(xrReady ? "ready" : "not ready")}";
                    modeColor = UIPalette.Warning;
                    break;
                case Mode.Connected:
                    modeText = $"CONNECTED: {(IsPaired ? SocketState : "not paired")} | headset {(xrReady ? "ready" : "not ready")}";
                    modeColor = IsPaired && SocketState == "connected" && xrReady ? UIPalette.Cyan : UIPalette.ButtonNormal;
                    break;
                default:
                    modeText = "CropScan XR is not configured on this PC";
                    modeColor = UIPalette.ButtonNormal;
                    break;
            }

            string title;
            if (questPairingCode != null)
            {
                title = "Pair this Quest?";
                text.AppendLine("Approve only if you just chose Pair VR in the CropScan browser you want to connect.");
                actions.Add(new XrStatusPanel.Action("Approve pairing", () => RespondToQuestPairing(true)));
                actions.Add(new XrStatusPanel.Action("Cancel", () => RespondToQuestPairing(false)));
            }
            else if (confirmLaunch != null)
            {
                title = "Open a different scan?";
                text.AppendLine($"Scan {confirmSession.scanId} ({CaseTitle(confirmSession)}) was sent to this headset.");
                text.AppendLine($"The assessment of scan {Session?.scanId} is not saved yet. It stays on this PC and can be retried when you reopen that scan.");
                actions.Add(new XrStatusPanel.Action("Open new scan", ConfirmSwitch));
                actions.Add(new XrStatusPanel.Action("Keep current scan", KeepCurrent));
            }
            else if (Session == null)
            {
                title = handlingOpen || opens.Count > 0 ? "Opening your scan..." : "No scan open";
                if (CurrentMode == Mode.Connected && !IsPaired)
                {
                    if (IsQuest)
                    {
                        text.AppendLine("Choose Pair VR on the website and enter the code here.");
                        actions.Add(new XrStatusPanel.Action("Enter pairing code", () => SetQuestPairingKeypad(true), !IsPairing));
                    }
                    else
                        text.AppendLine("Pair this PC: press Pair VR on the CropScan website and type the code in the window on the PC screen.");
                }
                else if (!handlingOpen && opens.Count == 0)
                    text.AppendLine("Press Enter the field on a CropScan result to open it here.");
                if (CurrentMode == Mode.Fixture)
                    actions.Add(new XrStatusPanel.Action("Open fixture scan", LaunchFixture));
            }
            else
            {
                title = CaseTitle(Session);
                text.AppendLine($"<color=#B8C7D4>{SessionLabel}</color>");
                if (headsetLaunch != null)
                    text.AppendLine("<b>Activate Quest Link</b>: put on the headset and turn on Link. Your scan stays open.");
                text.AppendLine(SavedResultText(Session));
                if (recipeState != RecipeState.Ready && !string.IsNullOrEmpty(recipeMessage))
                    text.AppendLine(recipeMessage);
                if (!string.IsNullOrEmpty(submitStatus))
                    text.AppendLine(submitStatus);

                if (headsetLaunch != null)
                    actions.Add(new XrStatusPanel.Action("Retry headset", RetryHeadset));
                switch (current)
                {
                    case EnvironmentId.Farm:
                        actions.Add(new XrStatusPanel.Action("Enter Lab", () => EnvironmentController.Instance.EnterLab()));
                        break;
                    case EnvironmentId.Lab:
                        actions.Add(new XrStatusPanel.Action("Enter Treatment", () => EnvironmentController.Instance.EnterTreatment(),
                            GateEntry(EnvironmentId.Treatment) == null));
                        actions.Add(new XrStatusPanel.Action("Return to Farm", () => StartCoroutine(EnvironmentController.Instance.FocusRoutine(EnvironmentId.Farm))));
                        break;
                    case EnvironmentId.Treatment:
                        actions.Add(new XrStatusPanel.Action("Return to Lab", () => EnvironmentController.Instance.ReturnToLab()));
                        break;
                }

                if (pendingSubmission != null && (submitState == TreatmentSubmitState.Failed || submitState == TreatmentSubmitState.Conflict))
                    actions.Add(new XrStatusPanel.Action("Retry save", RetryPending));
                if (CurrentMode == Mode.Fixture)
                    actions.Add(new XrStatusPanel.Action("Send launch again", LaunchFixture));
                actions.Add(new XrStatusPanel.Action("Release scan", Release, submitState != TreatmentSubmitState.Saving));
            }

            if (!string.IsNullOrEmpty(notice))
                text.AppendLine($"<color=#FBBF24>{notice}</color>");
            if (pipe != null && pipe.Error != null)
                text.AppendLine($"<size=80%>{pipe.Error}</size>");
            panel.Show(modeText, modeColor, title, text.ToString(), actions);
        }

        static string CaseTitle(XrSessionData session)
        {
            var plantName = session.plantId;
            if (SelectionState.Instance != null && SelectionState.Instance.Catalog != null && SelectionState.Instance.Catalog.TryGet(session.plantId, out var plant))
                plantName = plant.DisplayName;
            foreach (var info in DiseaseClasses.For(session.plantId))
                if (info.DiseaseId == session.diseaseId)
                    return $"{plantName} • {info.DisplayName}";
            return $"{plantName} • {session.diseaseId}";
        }

        /// <summary>The confirmed (server-saved) assessment, as the Lab shows it.</summary>
        public static string SavedResultText(XrSessionData session)
        {
            if (session == null || !session.HasResult)
                return "No treatment saved for this scan yet.";
            var r = session.treatmentResult;
            return $"<b>Saved assessment (revision {session.revision})</b>: {TreatmentResultManager.OutcomeLabel(r.outcome)}\n" +
                   $"{XrImprovement.Label(r.improvement)}. Leaf health {TreatmentSessionManager.Percent(r.initialHealth)} to " +
                   $"{TreatmentSessionManager.Percent(r.finalHealth)}, active severity {TreatmentSessionManager.Percent(r.initialSeverity)} to " +
                   $"{TreatmentSessionManager.Percent(r.finalSeverity)}, match {TreatmentSessionManager.Percent(r.score)}.\n" +
                   $"<size=85%><color=#FBBF24>{TreatmentResultManager.Disclaimer}</color></size>";
        }

        // ------------------------------------------------------------------ helpers

        string GetSetting(string key) => services != null && services.Progress != null && services.Progress.IsReady ? services.Progress.GetSetting(key) : null;

        void SetSetting(string key, string value)
        {
            if (services != null && services.Progress != null && services.Progress.IsReady)
                services.Progress.SetSetting(key, value ?? "");
        }

        static IEnumerator Wait(Task task)
        {
            while (!task.IsCompleted)
                yield return null;
        }

        static XrResult<T> Read<T>(Task<XrResult<T>> task) =>
            task.IsFaulted ? XrResult<T>.Fail(task.Exception?.GetBaseException().Message) : task.Result;
    }
}
