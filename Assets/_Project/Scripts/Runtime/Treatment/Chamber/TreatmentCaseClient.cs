using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTP.XR;
using UnityEngine;
using UnityEngine.Networking;

namespace BTP.Treatment
{
    /// <summary>The outcome of one treatment API request. Never throws to callers: failures carry an error message.</summary>
    public sealed class TreatmentApiResult<T> where T : class
    {
        public T Value;
        /// <summary>HTTP status (0 when the server could not be reached).</summary>
        public int Status;
        public string Error;
        /// <summary>Request path, e.g. "/api/treatment/cases/tomato/early_blight?scenario=standard".</summary>
        public string Path;

        public bool Ok => Value != null && Error == null;
        /// <summary>404: no treatment case (or scenario) for this plant and disease.</summary>
        public bool NotFound => Status == 404;
        /// <summary>409: the profile version is stale; reload the profile.</summary>
        public bool Conflict => Status == 409;
    }

    /// <summary>
    /// The four-control treatment API: GET /api/treatment/cases/{plantId}/{diseaseId}?scenario= for the profile and
    /// POST .../evaluate?scenario= for Check Health. The base URL follows the XR session rule (-xr-api, else
    /// CropScanXR.config.json) and must be HTTPS or a local http://localhost / 127.0.0.1 address. Requests use
    /// UnityWebRequest (start them on the main thread); the async calls never throw.
    /// </summary>
    public sealed class TreatmentCaseClient
    {
        public const int TimeoutSeconds = 10;
        public const string CasesRoute = "/api/treatment/cases";

        readonly string baseUrl;

        public string BaseUrl => baseUrl;

        public TreatmentCaseClient(string apiBaseUrl) => baseUrl = (apiBaseUrl ?? "").Trim().TrimEnd('/');

        /// <summary>The configured API: -xr-api on the command line, else CropScanXR.config.json's apiBaseUrl.</summary>
        public static string ConfiguredBaseUrl()
        {
            var args = XrLaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (!string.IsNullOrEmpty(args.ApiBaseUrl))
                return args.ApiBaseUrl;
            var config = XrPlayerConfig.Load(out _);
            return config?.apiBaseUrl;
        }

        public static TreatmentCaseClient FromConfiguration() => new TreatmentCaseClient(ConfiguredBaseUrl());

        static string Scenario(string scenario) => string.IsNullOrEmpty(scenario) ? "standard" : scenario;

        /// <summary>"/api/treatment/cases/tomato/early_blight?scenario=standard" with escaped path segments.</summary>
        public static string CasePath(string plantId, string diseaseId, string scenario) =>
            $"{CasesRoute}/{Uri.EscapeDataString(plantId ?? "")}/{Uri.EscapeDataString(diseaseId ?? "")}?scenario={Uri.EscapeDataString(Scenario(scenario))}";

        public static string EvaluatePath(string plantId, string diseaseId, string scenario) =>
            $"{CasesRoute}/{Uri.EscapeDataString(plantId ?? "")}/{Uri.EscapeDataString(diseaseId ?? "")}/evaluate?scenario={Uri.EscapeDataString(Scenario(scenario))}";

        /// <summary>Why <paramref name="url"/> may not be used (not HTTPS and not this computer), or null.</summary>
        public static string RefuseUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return "No treatment API address is configured (CropScanXR.config.json apiBaseUrl or -xr-api).";
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://127.0.0.1", StringComparison.Ordinal))
                return null;
            return "The treatment API address must use HTTPS.";
        }

        public Task<TreatmentApiResult<TreatmentProfile>> GetCase(string plantId, string diseaseId, string scenario) =>
            Send(CasePath(plantId, diseaseId, scenario), null, ParseProfile);

        public Task<TreatmentApiResult<TreatmentEvaluation>> Evaluate(TreatmentProfile profile, TreatmentSettings settings) =>
            Send(EvaluatePath(profile.PlantId, profile.DiseaseId, profile.Scenario), settings.ToEvaluationJson(profile), ParseEvaluation);

        /// <summary>Editor tools and batch verification: the same request, waiting on this thread (never in a running scene).</summary>
        public TreatmentApiResult<TreatmentProfile> GetCaseBlocking(string plantId, string diseaseId, string scenario) =>
            SendBlocking(CasePath(plantId, diseaseId, scenario), null, ParseProfile);

        public TreatmentApiResult<TreatmentEvaluation> EvaluateBlocking(TreatmentProfile profile, TreatmentSettings settings) =>
            SendBlocking(EvaluatePath(profile.PlantId, profile.DiseaseId, profile.Scenario), settings.ToEvaluationJson(profile), ParseEvaluation);

        static TreatmentProfile ParseProfile(string body, out string error) => TreatmentProfile.Parse(body, out error);

        static TreatmentEvaluation ParseEvaluation(string body, out string error) => TreatmentEvaluation.Parse(body, out error);

        delegate T Parser<out T>(string body, out string error);

        UnityWebRequest Create(string path, string json)
        {
            var request = new UnityWebRequest(baseUrl + path, json == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.SetRequestHeader("Accept", "application/json");
            return request;
        }

        async Task<TreatmentApiResult<T>> Send<T>(string path, string json, Parser<T> parse) where T : class
        {
            var refusal = RefuseUrl(baseUrl);
            if (refusal != null)
                return new TreatmentApiResult<T> { Error = refusal, Path = path };
            try
            {
                using (var request = Create(path, json))
                {
                    var done = new TaskCompletionSource<bool>();
                    request.SendWebRequest().completed += _ => done.TrySetResult(true);
                    await done.Task;
                    return Interpret(request, path, parse);
                }
            }
            catch (Exception exception)
            {
                return new TreatmentApiResult<T> { Error = "The treatment request failed: " + exception.Message, Path = path };
            }
        }

        TreatmentApiResult<T> SendBlocking<T>(string path, string json, Parser<T> parse) where T : class
        {
            var refusal = RefuseUrl(baseUrl);
            if (refusal != null)
                return new TreatmentApiResult<T> { Error = refusal, Path = path };
            try
            {
                using (var request = Create(path, json))
                {
                    var operation = request.SendWebRequest();
                    var deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds + 5);
                    while (!operation.isDone && DateTime.UtcNow < deadline)
                        Thread.Sleep(5);
                    if (!operation.isDone)
                        return new TreatmentApiResult<T> { Error = "The treatment API did not answer in time.", Path = path };
                    return Interpret(request, path, parse);
                }
            }
            catch (Exception exception)
            {
                return new TreatmentApiResult<T> { Error = "The treatment request failed: " + exception.Message, Path = path };
            }
        }

        static TreatmentApiResult<T> Interpret<T>(UnityWebRequest request, string path, Parser<T> parse) where T : class
        {
            var status = (int)request.responseCode;
            var body = request.downloadHandler?.text;
            var result = new TreatmentApiResult<T> { Status = status, Path = path };
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                result.Error = $"The treatment API could not be reached ({request.error}).";
                return result;
            }

            if (status == 404)
            {
                result.Error = "No treatment profile exists for this case.";
                return result;
            }

            if (status == 409)
            {
                result.Error = "The treatment profile changed on the server.";
                return result;
            }

            if (status < 200 || status >= 300)
            {
                var detail = string.IsNullOrEmpty(body) ? "" : ": " + (body.Length > 160 ? body.Substring(0, 160) + "..." : body);
                result.Error = $"The treatment API returned {status}{detail}";
                return result;
            }

            var value = parse(body, out var error);
            if (value == null)
            {
                result.Error = error ?? "The treatment response could not be read.";
                return result;
            }

            result.Value = value;
            return result;
        }

        public override string ToString() => $"TreatmentCaseClient({baseUrl})";

        /// <summary>Logs with the project prefix (kept here so callers need not repeat the URL).</summary>
        public void Log(string message) => Debug.Log($"[BTP] Treatment API {baseUrl}: {message}");
    }
}
