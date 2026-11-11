using System;
using System.IO;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Runs a setup step and records START / DONE / FAILED lines in Tools/logs/automation.log,
    /// so long steps triggered from the command line can be followed to completion.
    /// </summary>
    public static class Automation
    {
        const string LogPath = "Tools/logs/automation.log";

        public static string Run(string name, Action step)
        {
            Write($"START {name}");
            try
            {
                step();
                Write($"DONE {name}");
                return "done";
            }
            catch (Exception exception)
            {
                Write($"FAILED {name}: {exception}");
                Debug.LogException(exception);
                return $"failed: {exception.Message}";
            }
        }

        static void Write(string line)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? ".");
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
    }
}
