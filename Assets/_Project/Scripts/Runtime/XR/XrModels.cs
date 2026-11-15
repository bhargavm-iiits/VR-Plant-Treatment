using System;
using BTP.Treatment;

namespace BTP.XR
{
    // Wire shapes for the proposed CropScan XR API (see Docs/CropScan_XR_Implementation_Guide.md, section 3).
    // Field names are camelCase so JsonUtility reads the agreed fixture verbatim. The routes and payloads are
    // PROPOSED: confirm them with the backend owner before relying on a field that is not in the fixture.

    /// <summary>
    /// One learner's run for one backend scan. <see cref="xrSessionId"/> identifies the run, <see cref="scanId"/> the
    /// backend scan, <see cref="specimenId"/> the Farm plant to focus and update, and (plantId, diseaseId) the reusable
    /// Unity teaching case. An AR-only session carries the diagnosis with empty Unity fields.
    /// </summary>
    [Serializable]
    public sealed class XrSessionData
    {
        public string xrSessionId;
        public string scanId;
        public string backendDiseaseId;
        public string plantId;
        public string diseaseId;
        public string specimenId;
        public string recipeId;
        public int recipeVersion;
        /// <summary>Revision of the latest confirmed result; 0 before the first save.</summary>
        public int revision;
        public XrTreatmentResult treatmentResult;

        public CaseKey Case => new CaseKey(plantId, diseaseId);

        /// <summary>JsonUtility turns a JSON null into an empty object, so "no result" is an empty outcome.</summary>
        public bool HasResult => treatmentResult != null && !string.IsNullOrEmpty(treatmentResult.outcome);

        public bool HasUnityCase => !string.IsNullOrEmpty(plantId) && !string.IsNullOrEmpty(diseaseId);

        public XrSessionData Clone() => UnityEngine.JsonUtility.FromJson<XrSessionData>(UnityEngine.JsonUtility.ToJson(this));
    }

    /// <summary>The fields of the published ScanOut (GET /api/scans/{scan_id}) that Unity reads. Names match the API.</summary>
    [Serializable]
    public sealed class XrScan
    {
        public string id;
        public string status;
        public string disease_id;
        // confidence can be JSON null, which JsonUtility cannot put in a float; Unity does not need it.
    }

    [Serializable]
    public sealed class XrCreateSessionBody
    {
        public string scanId;
    }

    [Serializable]
    public sealed class XrCreateLaunchBody
    {
        public string scanId;
        /// <summary>"app-link": the same-laptop Enter the field button.</summary>
        public string source;
    }

    /// <summary>FastAPI's error body when "detail" is a string.</summary>
    [Serializable]
    public sealed class XrErrorBody
    {
        public string detail;
    }

    /// <summary>The confirmed (server-saved) outcome of a treatment assessment.</summary>
    [Serializable]
    public sealed class XrTreatmentResult
    {
        /// <summary>Stable outcome key from <see cref="TreatmentResultManager.OutcomeKey"/> ("Managed", "PartlyManaged", ...).</summary>
        public string outcome;
        /// <summary>"improved", "partial" or "none" (<see cref="XrImprovement"/>).</summary>
        public string improvement;
        public float initialSeverity;
        public float finalSeverity;
        public float initialHealth;
        public float finalHealth;
        public float score;
        public bool valid;
        public int recipeVersion;
        public int revision;
        public string savedUtc;
    }

    [Serializable]
    public sealed class XrControlValue
    {
        public string controlId;
        public float value;
    }

    /// <summary>Body of POST /api/xr/sessions/{xrSessionId}/treatment-result (sent with an Idempotency-Key header).</summary>
    [Serializable]
    public sealed class XrTreatmentSubmission
    {
        public string xrSessionId;
        public string recipeId;
        public int recipeVersion;
        public int expectedRevision;
        /// <summary>The local manual session the assessment came from; part of the idempotency key.</summary>
        public long manualSessionId;
        public string outcome;
        public string improvement;
        public float initialSeverity;
        public float finalSeverity;
        public float initialHealth;
        public float finalHealth;
        public float score;
        public bool valid;
        public int applyCount;
        public int healthChecks;
        public float elapsedSeconds;
        public XrControlValue[] appliedValues = Array.Empty<XrControlValue>();
        public string completedUtc;

        /// <summary>
        /// Deterministic, so a retry after a lost response (or after restarting the player) reuses the same key and the
        /// server writes the outcome once.
        /// </summary>
        public string IdempotencyKey => $"xr:{xrSessionId}:m{manualSessionId}:r{expectedRevision}";
    }

    public enum XrSubmitStatus
    {
        /// <summary>The server wrote (or had already written) this assessment; <see cref="XrSubmitResponse.Session"/> is canonical.</summary>
        Saved,
        /// <summary>Another result was saved first; reload <see cref="XrSubmitResponse.Session"/> before saving again.</summary>
        Conflict,
        /// <summary>The server refused the assessment (validation against the pinned recipe failed).</summary>
        Rejected,
        /// <summary>No usable answer (network, timeout, 5xx): retry with the same idempotency key.</summary>
        Failed
    }

    public sealed class XrSubmitResponse
    {
        public XrSubmitStatus Status;
        public XrSessionData Session;
        public string Message;
    }

    /// <summary>Pinned, structured treatment recipe (GET /api/xr/sessions/{xrSessionId}/recipe).</summary>
    [Serializable]
    public sealed class XrRecipe
    {
        public string recipeId;
        public int recipeVersion;
        public string plantId;
        public string diseaseId;
        /// <summary>Version of the Unity content the recipe was seeded from (treatment_content.sql cases.content_version).</summary>
        public int contentVersion;
        /// <summary>"backend" or "fixture"; shown in fixture mode.</summary>
        public string source;
        public float initialSeverity;
        public float initialHealth;
        public float maxSeverityDrop;
        public float maxHealthGain;
        public float maxWorsen;
        public XrRecipeControl[] controls = Array.Empty<XrRecipeControl>();
        public XrRecipeRule[] rules = Array.Empty<XrRecipeRule>();
        /// <summary>Unity asset keys the lesson needs; each must resolve in the local TreatmentAssetCatalog.</summary>
        public string[] assetKeys = Array.Empty<string>();

        public CaseKey Case => new CaseKey(plantId, diseaseId);
    }

    [Serializable]
    public sealed class XrRecipeControl
    {
        public string controlId;
        public int order;
        public float min;
        public float max;
        public float step;
        public float start;
        public float recommendedMin;
        public float recommendedMax;
        public float weight = 1f;
        public bool required = true;
    }

    [Serializable]
    public sealed class XrRecipeRule
    {
        public string ruleId;
        public string controlA;
        public float aMin;
        public float aMax;
        public string controlB;
        public float bMin;
        public float bMax;
        public float penalty;
        public bool blocks;
    }

    /// <summary>A persisted launch job (POST /api/xr/sessions/{id}/launches, GET /api/xr/launches/pending).</summary>
    [Serializable]
    public sealed class XrLaunch
    {
        public string launchId;
        public string xrSessionId;
        public string status;
        public string expiresUtc;
    }

    [Serializable]
    public sealed class XrLaunchList
    {
        public XrLaunch[] items = Array.Empty<XrLaunch>();
    }

    /// <summary>Launch states Unity reports (POST /api/xr/launches/{launchId}/status).</summary>
    public static class XrLaunchStatus
    {
        public const string Opening = "opening";
        public const string Opened = "opened";
        public const string Failed = "failed";
    }

    [Serializable]
    public sealed class XrLaunchStatusBody
    {
        public string status;
        public string detail;
        public bool xrReady;
    }

    /// <summary>A message on WSS /ws/xr. Server to Unity: "vr.launch", "ping". Unity to server: "device.hello", "device.heartbeat", "pong".</summary>
    [Serializable]
    public sealed class XrSocketMessage
    {
        public string type;
        public string launchId;
        public string xrSessionId;
        public string deviceId;
        public bool xrReady;
        public string display;
        public string appVersion;
    }

    [Serializable]
    public sealed class XrPairingRedeemBody
    {
        public string code;
        public string deviceName;
        public string platform;
        public string appVersion;
    }

    [Serializable]
    public sealed class XrLocalPairBody
    {
        public string scanId;
        public string deviceName;
        public string platform;
        public string appVersion;
    }

    /// <summary>The scoped device credential returned by POST /api/xr/pairings/redeem.</summary>
    [Serializable]
    public sealed class XrDeviceCredential
    {
        public string deviceId;
        public string deviceToken;
        public string apiBaseUrl;
        public string expiresUtc;
    }

    /// <summary>Whether a saved assessment improved the plant, using the outcome the recipe produced.</summary>
    public static class XrImprovement
    {
        public const string Improved = "improved";
        public const string Partial = "partial";
        public const string None = "none";

        public static string Of(string outcomeKey)
        {
            switch (outcomeKey)
            {
                case "Managed":
                case "PestSuppressed":
                case "Contained":
                    return Improved;
                case "PartlyManaged":
                case "PartlySuppressed":
                case "PoorlyContained":
                    return Partial;
                default:
                    return None;
            }
        }

        public static string Label(string improvement)
        {
            switch (improvement)
            {
                case Improved: return "Simulated improvement";
                case Partial: return "Partial improvement (simulated)";
                default: return "No improvement";
            }
        }

        /// <summary>
        /// The Farm shader's _DiseaseAmount (1 diseased, 0 healthy) for a saved result. The band keeps the three
        /// categories visibly distinct; inside a band the simulated severity change sets the amount.
        /// </summary>
        public static float FarmDiseaseAmount(XrTreatmentResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.outcome))
                return 1f;
            var ratio = result.initialSeverity > 1e-3f ? result.finalSeverity / result.initialSeverity : 1f;
            if (float.IsNaN(ratio))
                ratio = 1f;
            switch (string.IsNullOrEmpty(result.improvement) ? Of(result.outcome) : result.improvement)
            {
                case Improved: return Clamp(ratio, 0.05f, 0.35f);
                case Partial: return Clamp(ratio, 0.45f, 0.75f);
                default: return 1f;
            }
        }

        static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
