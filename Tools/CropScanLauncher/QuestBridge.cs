using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CropScanLauncher
{
    /// <summary>Only sends the validated CropScan link to one USB-connected Quest using an explicitly configured ADB.</summary>
    static class QuestBridge
    {
        public const string ConfigFileName = "QuestAdbPath.txt";
        const int TimeoutMs = 20000;

        public static string ConfiguredAdb
        {
            get
            {
                var config = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
                return File.Exists(config) ? File.ReadAllText(config).Trim() : null;
            }
        }

        public static string Open(string adb, string link)
        {
            if (!Path.IsPathRooted(adb) || !File.Exists(adb))
                return "The CropScan Quest launcher cannot find ADB. Run Install-CropScanQuestBridge.ps1 again.";

            var listed = Run(adb, "devices -l", out var devices);
            if (!listed)
                return "ADB could not check the USB connection. Reconnect the Quest and try again.";

            var headsets = Regex.Matches(devices, @"^([a-zA-Z0-9_-]+)[ \t]+device[ \t]+[^\r\n]*\bmodel:Quest(?:[A-Za-z0-9_-]*)\b",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (headsets.Count != 1)
                return headsets.Count > 1 ? "Connect just one Quest and try again."
                    : "Connect your Quest via USB, allow USB debugging on the headset, and try again.";

            var serial = headsets[0].Groups[1].Value;
            // 'serial' and 'link' contain only validated characters. Use the package to limit resolution to CropScan.
            var arguments = $"-s {serial} shell am start -W -a android.intent.action.VIEW -c android.intent.category.BROWSABLE -d {link} com.btp.plantdiseasevr";
            var launched = Run(adb, arguments, out var response);
            if (!launched || !Regex.IsMatch(response, @"^Status: ok\s*$", RegexOptions.Multiline))
                return "The Quest could not open this crop scan link. Install the updated Plant Disease VR APK and try again.";
            return null;
        }

        static bool Run(string adb, string args, out string output)
        {
            output = "";
            using (var process = Process.Start(new ProcessStartInfo(adb, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(TimeoutMs))
                {
                    process.Kill();
                    return false;
                }

                if (!Task.WaitAll(new Task[] { stdout, stderr }, 1000))
                    return false;
                output = stdout.Result;
                return process.ExitCode == 0;
            }
        }
    }
}
