using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BTP.XR
{
    /// <summary>
    /// The CropScan XR API over HTTPS (session, recipe, treatment result and launch status). Every request carries the
    /// paired device's scoped credential; the server checks it on each lookup and write. Requests run through
    /// UnityWebRequest, so they must start on the main thread and their awaits resume there.
    /// </summary>
    public sealed class HttpXrBackend : IXrBackend
    {
        const int TimeoutSeconds = 20;
        readonly string baseUrl;
        readonly Func<string> token;

        public string Label => "Connected";
        public bool IsFixture => false;
        public string BaseUrl => baseUrl;

        /// <param name="deviceToken">Reads the current device token (it changes when the player is paired again).</param>
        public HttpXrBackend(string apiBaseUrl, Func<string> deviceToken)
        {
            baseUrl = (apiBaseUrl ?? "").TrimEnd('/');
            token = deviceToken;
        }

        public async Task<XrResult<XrSessionData>> OpenSessionForScan(string scanId)
        {
            var body = JsonUtility.ToJson(new XrCreateSessionBody { scanId = scanId });
            var (status, text, error) = await Send("POST", XrApiRoutes.Sessions, body, "session:" + scanId);
            if (error == null)
                return Parse<XrSessionData>(text, status);
            switch (status)
            {
                case 403:
                case 404:
                    return XrResult<XrSessionData>.Fail("This PC is paired with a CropScan session that cannot open this scan " +
                                                        "(it belongs to another account, or it does not exist).", status);
                case 409:
                case 422:
                    return XrResult<XrSessionData>.Fail("This scan cannot open in VR: " + (ErrorDetail(text) ?? error), status);
                default:
                    return XrResult<XrSessionData>.Fail(error, status);
            }
        }

        public async Task<XrResult<XrLaunch>> CreateLaunch(string xrSessionId, string scanId, string idempotencyKey)
        {
            var body = JsonUtility.ToJson(new XrCreateLaunchBody { scanId = scanId, source = "app-link" });
            var (status, text, error) = await Send("POST", XrApiRoutes.Launches(xrSessionId), body, idempotencyKey);
            return error == null ? Parse<XrLaunch>(text, status) : XrResult<XrLaunch>.Fail(error, status);
        }

        /// <summary>The published scan record (no device credential; used by the staging preview).</summary>
        public async Task<XrResult<XrScan>> GetScan(string scanId)
        {
            var (status, text, error) = await Send("GET", XrApiRoutes.Scan(scanId), null, null, false);
            if (status == 404)
                return XrResult<XrScan>.Fail("CropScan has no scan with this ID.", status);
            return error == null ? Parse<XrScan>(text, status) : XrResult<XrScan>.Fail(error, status);
        }

        static string ErrorDetail(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;
            try
            {
                return JsonUtility.FromJson<XrErrorBody>(body)?.detail;
            }
            catch
            {
                return null;
            }
        }

        public async Task<XrResult<XrSessionData>> GetSession(string xrSessionId)
        {
            var (status, body, error) = await Send("GET", XrApiRoutes.Session(xrSessionId), null, null);
            return error == null ? Parse<XrSessionData>(body, status) : XrResult<XrSessionData>.Fail(error, status);
        }

        public async Task<XrResult<XrRecipe>> GetRecipe(string xrSessionId)
        {
            var (status, body, error) = await Send("GET", XrApiRoutes.Recipe(xrSessionId), null, null);
            return error == null ? Parse<XrRecipe>(body, status) : XrResult<XrRecipe>.Fail(error, status);
        }

        public async Task<XrSubmitResponse> SubmitTreatmentResult(XrTreatmentSubmission submission)
        {
            var (status, body, error) = await Send("POST", XrApiRoutes.TreatmentResult(submission.xrSessionId), JsonUtility.ToJson(submission),
                submission.IdempotencyKey);
            if (status == 200 || status == 201)
            {
                var saved = Parse<XrSessionData>(body, status);
                return saved.Ok
                    ? new XrSubmitResponse { Status = XrSubmitStatus.Saved, Session = saved.Value, Message = "Saved." }
                    : new XrSubmitResponse { Status = XrSubmitStatus.Failed, Message = "The save response could not be read: " + saved.Error };
            }

            if (status == 409)
            {
                // The conflict body may carry the canonical session; otherwise reload it before allowing another save.
                var current = Parse<XrSessionData>(body, status);
                if (!current.Ok || string.IsNullOrEmpty(current.Value?.xrSessionId))
                    current = await GetSession(submission.xrSessionId);
                return new XrSubmitResponse { Status = XrSubmitStatus.Conflict, Session = current.Ok ? current.Value : null, Message = error ?? "Revision conflict." };
            }

            if (status == 400 || status == 403 || status == 404 || status == 422)
                return new XrSubmitResponse { Status = XrSubmitStatus.Rejected, Message = error ?? $"Refused ({status})." };
            return new XrSubmitResponse { Status = XrSubmitStatus.Failed, Message = error ?? $"Unexpected response ({status})." };
        }

        public async Task<XrResult<List<XrLaunch>>> GetPendingLaunches()
        {
            var (status, body, error) = await Send("GET", XrApiRoutes.PendingLaunches, null, null);
            if (error != null)
                return XrResult<List<XrLaunch>>.Fail(error, status);
            var list = Parse<XrLaunchList>(body, status);
            return list.Ok
                ? XrResult<List<XrLaunch>>.Success(new List<XrLaunch>(list.Value.items ?? Array.Empty<XrLaunch>()), status)
                : XrResult<List<XrLaunch>>.Fail(list.Error, status);
        }

        public async Task<XrResult<bool>> ReportLaunchStatus(string launchId, string launchStatus, string detail, bool xrReady)
        {
            var body = JsonUtility.ToJson(new XrLaunchStatusBody { status = launchStatus, detail = detail, xrReady = xrReady });
            var (status, _, error) = await Send("POST", XrApiRoutes.LaunchStatus(launchId), body, null);
            return error == null ? XrResult<bool>.Success(true, status) : XrResult<bool>.Fail(error, status);
        }

        /// <summary>Redeems a one-time pairing code shown on the website. No device token is needed yet.</summary>
        public async Task<XrResult<XrDeviceCredential>> RedeemPairing(string code)
        {
            var body = JsonUtility.ToJson(new XrPairingRedeemBody
            {
                code = code?.Trim(), deviceName = SystemInfo.deviceName, platform = "windows-openxr", appVersion = Application.version,
            });
            var (status, text, error) = await Send("POST", XrApiRoutes.PairingRedeem, body, null, false);
            if (error != null)
                return XrResult<XrDeviceCredential>.Fail(status == 429 ? "Too many attempts: wait a minute and try a new code." : error, status);
            var credential = Parse<XrDeviceCredential>(text, status);
            if (credential.Ok && string.IsNullOrEmpty(credential.Value.deviceToken))
                return XrResult<XrDeviceCredential>.Fail("The pairing response had no device credential.", status);
            return credential;
        }

        /// <summary>True when the API runs on this computer (the laptop demo).</summary>
        public bool IsLocalApi => IsLocal(baseUrl);

        /// <summary>
        /// Laptop demo: pairs this app for the browser session that owns <paramref name="scanId"/>, so Enter the field
        /// works without typing a code. Only a local API (dev_local.py) answers it.
        /// </summary>
        public async Task<XrResult<XrDeviceCredential>> LocalPair(string scanId)
        {
            var body = JsonUtility.ToJson(new XrLocalPairBody
            {
                scanId = scanId, deviceName = SystemInfo.deviceName, platform = "windows-local", appVersion = Application.version,
            });
            var (status, text, error) = await Send("POST", XrApiRoutes.LocalPair, body, null, false);
            if (error != null)
                return XrResult<XrDeviceCredential>.Fail(ErrorDetail(text) ?? error, status);
            var credential = Parse<XrDeviceCredential>(text, status);
            if (credential.Ok && string.IsNullOrEmpty(credential.Value.deviceToken))
                return XrResult<XrDeviceCredential>.Fail("The pairing response had no device credential.", status);
            return credential;
        }

        public async Task<XrResult<bool>> CheckHealth()
        {
            var (status, _, error) = await Send("GET", XrApiRoutes.Health, null, null, false);
            return error == null ? XrResult<bool>.Success(true, status) : XrResult<bool>.Fail(error, status);
        }

        async Task<(int status, string body, string error)> Send(string method, string path, string json, string idempotencyKey, bool authorised = true)
        {
            if (string.IsNullOrEmpty(baseUrl))
                return (0, null, "No API address is configured (-xr-api).");
            if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !IsLocal(baseUrl))
                return (0, null, "The API address must use HTTPS.");
            var bearer = authorised ? token?.Invoke() : null;
            if (authorised && string.IsNullOrEmpty(bearer))
                return (401, null, "This player is not paired: pair it with the website first.");

            using (var request = new UnityWebRequest(baseUrl + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                request.SetRequestHeader("Accept", "application/json");
                if (!string.IsNullOrEmpty(bearer))
                    request.SetRequestHeader("Authorization", "Bearer " + bearer);
                if (!string.IsNullOrEmpty(idempotencyKey))
                    request.SetRequestHeader(XrApiRoutes.IdempotencyHeader, idempotencyKey);
                request.timeout = TimeoutSeconds;

                var done = new TaskCompletionSource<bool>();
                request.SendWebRequest().completed += _ => done.TrySetResult(true);
                await done.Task;

                var status = (int)request.responseCode;
                var body = request.downloadHandler?.text;
                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
                    return (status, body, $"The CropScan API could not be reached ({request.error}). It may be starting up: try again shortly.");
                if (status >= 200 && status < 300)
                    return (status, body, null);
                if (status == 401)
                    return (status, body, "The device credential was refused: pair this player again.");
                return (status, body, $"{method} {path} returned {status}{Detail(body)}.");
            }
        }

        static bool IsLocal(string url) =>
            url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://127.0.0.1", StringComparison.Ordinal);

        static string Detail(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "";
            var trimmed = body.Length > 160 ? body.Substring(0, 160) + "..." : body;
            return ": " + trimmed;
        }

        static XrResult<T> Parse<T>(string body, int status) where T : class
        {
            if (string.IsNullOrWhiteSpace(body))
                return XrResult<T>.Fail("The response was empty.", status);
            try
            {
                var value = JsonUtility.FromJson<T>(body);
                return value != null ? XrResult<T>.Success(value, status) : XrResult<T>.Fail("The response was empty.", status);
            }
            catch (Exception exception)
            {
                return XrResult<T>.Fail("The response was not valid JSON: " + exception.Message, status);
            }
        }
    }
}
