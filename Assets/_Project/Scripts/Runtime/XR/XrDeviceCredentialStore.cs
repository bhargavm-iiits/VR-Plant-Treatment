using System;
using System.IO;
using UnityEngine;

namespace BTP.XR
{
    /// <summary>
    /// Keeps the paired device's scoped credential in persistentDataPath (per Windows user). It is a revocable device
    /// token, never the learner's password; unpairing deletes the file. A production build should move it to the
    /// Windows credential store (DPAPI); this is a staging-demo store.
    /// </summary>
    public static class XrDeviceCredentialStore
    {
        public const string FileName = "xr_device.json";

        static string PathFor(string directory) => System.IO.Path.Combine(directory ?? Application.persistentDataPath, FileName);

        public static XrDeviceCredential Load(string directory = null)
        {
            var path = PathFor(directory);
            if (!File.Exists(path))
                return null;
            try
            {
                var credential = JsonUtility.FromJson<XrDeviceCredential>(File.ReadAllText(path));
                return credential != null && !string.IsNullOrEmpty(credential.deviceToken) ? credential : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BTP][XR] The saved pairing could not be read; pair again. " + exception.Message);
                return null;
            }
        }

        public static void Save(XrDeviceCredential credential, string directory = null) =>
            File.WriteAllText(PathFor(directory), JsonUtility.ToJson(credential));

        public static void Clear(string directory = null)
        {
            var path = PathFor(directory);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
