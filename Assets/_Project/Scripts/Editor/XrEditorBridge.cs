using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using BTP.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BTP.Editor
{
    /// <summary>
    /// Lets Enter the field open Unity on the laptop demo. While the Editor is open but not playing, it listens on the
    /// current user's CropScan XR Editor pipe (<see cref="XrScanLink.CurrentUserEditorPipeName"/>); the local backend
    /// sends OPEN &lt;scan id&gt;, and the Editor saves the scan, opens XRBootstrap and presses Play. The in-game coordinator
    /// then opens that scan's Farm plant (XrSessionCoordinator.EditorPendingScanPref). When Unity is closed, the backend
    /// starts the Editor with -executeMethod BTP.Editor.XrEditorBridge.PlayFromCommandLine --crop-scan-id &lt;uuid&gt;.
    /// Only a validated scan UUID ever crosses the pipe or the command line.
    /// </summary>
    [InitializeOnLoad]
    static class XrEditorBridge
    {
        const string CommandLineHandledKey = "btp.xr.commandLineScanHandled";
        static XrScanPipeServer server;
        // A scan that arrived while Unity was busy (starting, compiling, importing); opened once it is idle.
        static string waitingScan;

        // See VR Demo: the same hand-over with no scan. The backend either starts the Editor with this flag, or, when
        // the Editor is already open, drops an empty request file in the project's Temp folder. Neither carries data.
        const string DemoArgument = "--crop-demo";
        const string DemoRequestFile = "Temp/CropScanDemo.request";
        const double DemoRequestMaxAgeSeconds = 300;
        static bool waitingDemo;
        static double nextDemoCheck;

        static XrEditorBridge()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor || Application.isBatchMode)
                return;
            server = new XrScanPipeServer(XrScanLink.CurrentUserEditorPipeName());
            server.Start();
            TakeCommandLineScan();
            EditorApplication.update += Poll;
            // The listener thread must not survive a domain reload (entering Play reloads scripts).
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        static void Stop()
        {
            EditorApplication.update -= Poll;
            server?.Dispose();
            server = null;
        }

        static void Poll()
        {
            if (server == null)
                return;
            while (server.TryDequeue(out var scanId))
                waitingScan = scanId;
            CheckDemoRequest();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (waitingScan != null)
            {
                // A scan outranks a demo: it already opens the app, at a specific plant.
                var next = waitingScan;
                waitingScan = null;
                waitingDemo = false;
                OpenScan(next);
                return;
            }

            if (waitingDemo)
            {
                waitingDemo = false;
                PlayDemo();
            }
        }

        /// <summary>Twice a second, look for the backend's request file. A request older than a few minutes was
        /// meant for an Editor session that has since closed, so it is removed without acting on it.</summary>
        static void CheckDemoRequest()
        {
            if (EditorApplication.timeSinceStartup < nextDemoCheck)
                return;
            nextDemoCheck = EditorApplication.timeSinceStartup + 0.5;
            try
            {
                if (!File.Exists(DemoRequestFile))
                    return;
                var age = (DateTime.UtcNow - File.GetLastWriteTimeUtc(DemoRequestFile)).TotalSeconds;
                File.Delete(DemoRequestFile);
                if (age <= DemoRequestMaxAgeSeconds)
                    waitingDemo = true;
            }
            catch (Exception)
            {
                // The backend may still be writing it; the next check takes it.
            }
        }

        /// <summary>Opens the app with no scan: opens XRBootstrap and presses Play, or just comes to the front when
        /// it is already playing.</summary>
        public static void PlayDemo()
        {
            BringToFront();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            // Unsaved scene edits are never discarded: Unity asks first, and Cancel keeps the Editor as it is.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[BTP][XR] See VR Demo: cancelled because the open scenes were not saved.");
                return;
            }

            if (EditorSceneManager.GetActiveScene().path != ProjectPaths.BootstrapScene)
                EditorSceneManager.OpenScene(ProjectPaths.BootstrapScene, OpenSceneMode.Single);
            Debug.Log("[BTP][XR] See VR Demo: entering Play mode.");
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Entry point when the backend starts a closed Editor for the demo (-executeMethod ... --crop-demo).</summary>
        public static void PlayDemoFromCommandLine() => TakeCommandLineScan();   // a no-op when the bridge already took it

        /// <summary>
        /// Started by the backend with --crop-scan-id &lt;uuid&gt; (Unity was closed): open that scan once the Editor has
        /// loaded. Once per Editor session, so entering Play (a script reload) never opens it again.
        /// </summary>
        static void TakeCommandLineScan()
        {
            if (SessionState.GetBool(CommandLineHandledKey, false))
                return;
            SessionState.SetBool(CommandLineHandledKey, true);
            var args = Environment.GetCommandLineArgs();
            if (XrScanLink.TryFindScanArgument(args, out var scanId, out var error))
                waitingScan = scanId;
            else if (error != null)
                Debug.LogError("[BTP][XR] " + error);
            else if (Array.IndexOf(args, DemoArgument) >= 0)
                waitingDemo = true;
        }

        /// <summary>Opens a scan in Play mode: saves it for the coordinator, opens XRBootstrap and presses Play.</summary>
        public static void OpenScan(string scanId)
        {
            if (!XrScanLink.TryParseScanId(scanId, out scanId))
                return;
            BringToFront();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Already playing: the in-game receiver opens it.
                Forward(scanId);
                return;
            }

            PlayerPrefs.SetString(XrSessionCoordinator.EditorPendingScanPref, scanId);
            PlayerPrefs.Save();
            // Unsaved scene edits are never discarded: Unity asks first, and Cancel keeps the Editor as it is.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                PlayerPrefs.DeleteKey(XrSessionCoordinator.EditorPendingScanPref);
                Debug.Log("[BTP][XR] Enter the field: cancelled because the open scenes were not saved.");
                return;
            }

            if (EditorSceneManager.GetActiveScene().path != ProjectPaths.BootstrapScene)
                EditorSceneManager.OpenScene(ProjectPaths.BootstrapScene, OpenSceneMode.Single);
            Debug.Log($"[BTP][XR] Enter the field: opening scan {scanId} in Play mode.");
            // Show the Game tab, so the Farm is what appears when Unity comes to the front.
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Entry point when the backend starts a closed Editor (-executeMethod ... --crop-scan-id &lt;uuid&gt;).</summary>
        public static void PlayFromCommandLine() => TakeCommandLineScan();   // a no-op when the bridge already took it

        static void Forward(string scanId)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", XrScanLink.CurrentUserPipeName(), PipeDirection.InOut))
                {
                    client.Connect(2000);
                    var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    writer.WriteLine(XrScanLink.OpenPrefix + scanId);
                    new StreamReader(client).ReadLine();
                }
            }
            catch (Exception exception)
            {
                // Entering Play: the scan waits for the coordinator instead.
                PlayerPrefs.SetString(XrSessionCoordinator.EditorPendingScanPref, scanId);
                PlayerPrefs.Save();
                Debug.Log("[BTP][XR] Enter the field: the game is still starting; the scan opens when it is ready. " + exception.Message);
            }
        }

        /// <summary>Best effort: Windows may only flash the taskbar button when another app has the focus.</summary>
        static void BringToFront()
        {
            try
            {
                var window = Process.GetCurrentProcess().MainWindowHandle;
                if (window == IntPtr.Zero)
                    return;
                if (IsIconic(window))
                    ShowWindow(window, 9 /* SW_RESTORE */);
                SetForegroundWindow(window);
            }
            catch (Exception)
            {
                // Focus is a convenience.
            }
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    }
}
