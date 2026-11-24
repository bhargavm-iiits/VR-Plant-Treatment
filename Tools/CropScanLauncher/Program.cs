using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BTP.XR;

namespace CropScanLauncher
{
    /// <summary>
    /// Windows runs this for a cropscanxr://open-field?scan=&lt;uuid&gt; link (per-user registration by
    /// Install-CropScanXR.ps1). It accepts exactly one such link, then either forwards the scan to the running player over
    /// the current user's named pipe, or starts PlantDiseaseVR.exe from this folder with --crop-scan-id &lt;uuid&gt;.
    /// Activations are serialised by a per-user mutex that is held until a newly started player's receiver answers, so a
    /// second click while the player starts is forwarded to it instead of starting a second player. The link supplies only
    /// the scan ID; the player and backend decide whether this device may open it.
    /// </summary>
    static class Program
    {
        const string PlayerExe = "PlantDiseaseVR.exe";
        const int ConnectTimeoutMs = 1500;
        const int StartupWaitMs = 120000;
        const int ActivationWaitMs = 180000;
        const long MaxLogBytes = 256 * 1024;

        static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CropScanXR", "launcher.log");

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception exception)
            {
                Log("Unexpected failure: " + exception);
                Show("CropScan XR could not open the scan.\n\n" + exception.Message);
                return 1;
            }
        }

        static int Run(string[] args)
        {
            if (args.Length != 1)
            {
                Log($"Refused: expected one link, got {args.Length} arguments.");
                Show("CropScan XR opens from the Enter the field button on a CropScan result.");
                return 2;
            }

            var questAdb = QuestBridge.ConfiguredAdb;
            if (questAdb != null && XrScanLink.TryParseQuestPairUri(args[0], XrScanLink.Scheme, out var pairingCode))
                return SendToQuest(questAdb, $"{XrScanLink.QuestScheme}://{XrScanLink.QuestPairHost}/?code={pairingCode}");

            if (!XrScanLink.TryParseOpenFieldUri(args[0], out var scanId, out var error))
            {
                Log("Refused link: " + error);
                Show("CropScan XR did not open this link.\n\n" + error);
                return 2;
            }

            if (questAdb != null)
                return SendToQuest(questAdb, $"{XrScanLink.QuestScheme}://{XrScanLink.Host}/?scan={scanId}");

            var pipe = XrScanLink.CurrentUserPipeName();
            var player = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PlayerExe);
            using (var gate = new Mutex(false, @"Local\CropScanXR.Launcher." + pipe))
            {
                bool owned;
                try
                {
                    owned = gate.WaitOne(ActivationWaitMs);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }

                if (!owned)
                {
                    Log("Refused: another activation is still running.");
                    Show("CropScan XR is still opening the previous scan. Try again in a moment.");
                    return 3;
                }

                try
                {
                    return Activate(scanId, pipe, player);
                }
                finally
                {
                    gate.ReleaseMutex();
                }
            }
        }

        static int SendToQuest(string adb, string link)
        {
            var problem = QuestBridge.Open(adb, link);
            if (problem == null)
            {
                Log("Activation sent to the Quest.");
                return 0;
            }

            Log("Quest activation failed: " + problem);
            Show(problem);
            return 8;
        }

        static int Activate(string scanId, string pipe, string player)
        {
            var open = XrScanLink.OpenPrefix + scanId;
            if (TrySend(pipe, open, ConnectTimeoutMs, out var answer))
                return Finish(answer, scanId, "forwarded to the running player");

            if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(PlayerExe)).Length > 0)
            {
                // The player is starting (or its receiver is busy): wait for it rather than starting a second one.
                if (WaitForReceiver(pipe, open, StartupWaitMs, null, out answer))
                    return Finish(answer, scanId, "forwarded to the starting player");
                Log("The running player did not accept the scan in time.");
                Show("CropScan XR is running but did not accept the scan. Close CropScan XR and press Enter the field again.");
                return 4;
            }

            if (!File.Exists(player))
            {
                Log("Missing player: " + player);
                Show($"CropScan XR is not installed correctly: {player} is missing. Run Install-CropScanXR.ps1 again.");
                return 5;
            }

            // Direct execution from the fixed install folder; the only variable argument is the validated UUID.
            var start = new ProcessStartInfo(player)
            {
                Arguments = $"{XrScanLink.ScanArgument} {scanId} -single-instance",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(player),
            };
            var process = Process.Start(start);
            Log($"Started {player} (pid {process?.Id}) for scan {scanId}.");

            // Keep the activation lock until the new player's receiver answers, so later clicks reach this player.
            if (WaitForReceiver(pipe, XrScanLink.Ping, StartupWaitMs, process, out answer))
                return 0;
            if (process != null && process.HasExited)
            {
                Log($"The player exited while starting (code {process.ExitCode}).");
                Show("CropScan XR closed while starting. Check that the Windows build is installed completely.");
                return 6;
            }

            Log("The player started, but its receiver did not answer within the start-up wait.");
            return 0;
        }

        static bool WaitForReceiver(string pipe, string line, int timeoutMs, Process starting, out string answer)
        {
            answer = null;
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount - deadline < 0)
            {
                if (starting != null && starting.HasExited)
                    return false;
                if (TrySend(pipe, line, 1000, out answer))
                    return true;
                Thread.Sleep(500);
            }

            return false;
        }

        static int Finish(string answer, string scanId, string how)
        {
            if (answer == XrScanLink.Ok)
            {
                Log($"Scan {scanId} {how}.");
                return 0;
            }

            Log($"The player refused scan {scanId}: {answer}");
            Show("CropScan XR refused the scan: " + answer);
            return 7;
        }

        static bool TrySend(string pipe, string line, int timeoutMs, out string answer)
        {
            answer = null;
            try
            {
                using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut))
                {
                    client.Connect(timeoutMs);
                    var utf8 = new UTF8Encoding(false);
                    var writer = new StreamWriter(client, utf8) { AutoFlush = true, NewLine = "\n" };
                    var reader = new StreamReader(client, utf8);
                    writer.WriteLine(line);
                    answer = reader.ReadLine();
                    return answer != null;
                }
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                    File.Delete(LogPath);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Logging must never stop an activation.
            }
        }

        /// <summary>Tells the learner what went wrong. CROPSCANXR_LAUNCHER_QUIET=1 (scripted tests) only logs it.</summary>
        static void Show(string message)
        {
            if (Environment.GetEnvironmentVariable("CROPSCANXR_LAUNCHER_QUIET") == "1")
            {
                Log("(dialog) " + message.Replace('\n', ' '));
                return;
            }

            MessageBoxW(IntPtr.Zero, message, "CropScan XR", 0x00000040 /* MB_ICONINFORMATION */);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
    }
}
