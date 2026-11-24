using System;
using System.IO;
using UnityEngine;

namespace BTP.XR
{
    /// <summary>
    /// The installed player's CropScan settings, written by Install-CropScanXR.ps1 next to PlantDiseaseVR.exe
    /// (CropScanXR.config.json; in the Editor, the project folder). The app link only carries a scan ID, so the API
    /// address comes from here; -xr-api on the command line overrides it.
    /// </summary>
    [Serializable]
    public sealed class XrPlayerConfig
    {
        public const string FileName = "CropScanXR.config.json";
        public const string QuestApiBaseUrl = "https://cropscan-api-hzln.onrender.com";
        /// <summary>The paired player creates or reopens sessions over HTTPS (POST /api/xr/sessions). The default.</summary>
        public const string ModePaired = "paired";
        /// <summary>Staging proof of concept: GET /api/scans/{id} plus the case map; results stay on this PC.</summary>
        public const string ModePreview = "preview";

        public string apiBaseUrl;
        public string sessionMode = ModePaired;
        public string socketUrl;

        public bool IsPreview => string.Equals(sessionMode, ModePreview, StringComparison.OrdinalIgnoreCase);

        public static string PlatformApiBaseUrl =>
            Application.platform == RuntimePlatform.Android ? QuestApiBaseUrl : null;

        public string ResolveApiBaseUrl(XrLaunchArgs args, string fallback = null)
        {
            if (!string.IsNullOrEmpty(args.ApiBaseUrl))
                return args.ApiBaseUrl;
            return !string.IsNullOrEmpty(apiBaseUrl) ? apiBaseUrl : fallback;
        }

        /// <summary>The folder holding PlantDiseaseVR.exe (the project folder in the Editor).</summary>
        public static string InstallFolder => Path.GetDirectoryName(Application.dataPath);

        public static XrPlayerConfig Load(out string error)
        {
            error = null;
            var path = Path.Combine(InstallFolder, FileName);
            if (!File.Exists(path))
                return new XrPlayerConfig();
            try
            {
                var config = JsonUtility.FromJson<XrPlayerConfig>(File.ReadAllText(path)) ?? new XrPlayerConfig();
                if (!string.IsNullOrEmpty(config.apiBaseUrl))
                    config.apiBaseUrl = config.apiBaseUrl.Trim().TrimEnd('/');
                return config;
            }
            catch (Exception exception)
            {
                error = $"{path} could not be read: {exception.Message}";
                return new XrPlayerConfig();
            }
        }
    }
}
