using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The on-device hand-test trail: one "[BTP][HandTest]" line per interaction (name, hand, modality, result), so a person's
    /// headset run can be checked afterwards with `adb logcat -s Unity | findstr HandTest`.
    /// </summary>
    public static class HandTestLog
    {
        public const string Prefix = "[BTP][HandTest]";

        /// <summary>The last line written (tests and the verification read it).</summary>
        public static string Last { get; private set; }

        public static int Count { get; private set; }

        public static void Log(string interaction, string hand, string modality, string result)
        {
            Last = $"{Prefix} interaction={interaction} hand={(string.IsNullOrEmpty(hand) ? "-" : hand)} modality={(string.IsNullOrEmpty(modality) ? "-" : modality)} result={result}";
            Count++;
            Debug.Log(Last);
        }
    }
}
