using System.Collections.Generic;
using System.Threading.Tasks;

namespace BTP.XR
{
    /// <summary>
    /// What Unity needs from the CropScan XR API. <see cref="HttpXrBackend"/> talks to the (proposed) HTTPS routes;
    /// <see cref="FixtureXrBackend"/> serves the agreed fixture locally while staging is being set up. Every method is
    /// called and completes on Unity's main thread.
    /// </summary>
    public interface IXrBackend
    {
        /// <summary>"Fixture" or "Connected"; shown on the status panel.</summary>
        string Label { get; }
        bool IsFixture { get; }

        /// <summary>
        /// Creates or reopens this learner's session for a scan received from the app link (POST /api/xr/sessions). The
        /// backend checks that the paired device may access the scan; an unpaired or foreign device gets an access error.
        /// </summary>
        Task<XrResult<XrSessionData>> OpenSessionForScan(string scanId);

        /// <summary>Records a launch for the web page's status (POST /api/xr/sessions/{id}/launches, idempotent by key).</summary>
        Task<XrResult<XrLaunch>> CreateLaunch(string xrSessionId, string scanId, string idempotencyKey);

        Task<XrResult<XrSessionData>> GetSession(string xrSessionId);
        Task<XrResult<XrRecipe>> GetRecipe(string xrSessionId);
        Task<XrSubmitResponse> SubmitTreatmentResult(XrTreatmentSubmission submission);
        Task<XrResult<List<XrLaunch>>> GetPendingLaunches();
        Task<XrResult<bool>> ReportLaunchStatus(string launchId, string status, string detail, bool xrReady);
    }

    public readonly struct XrResult<T>
    {
        public readonly T Value;
        public readonly string Error;
        /// <summary>HTTP status, or 0 when no response arrived.</summary>
        public readonly int Status;

        public XrResult(T value, string error, int status)
        {
            Value = value;
            Error = error;
            Status = status;
        }

        public bool Ok => Error == null;

        public static XrResult<T> Success(T value, int status = 200) => new XrResult<T>(value, null, status);
        public static XrResult<T> Fail(string error, int status = 0) => new XrResult<T>(default, error ?? "Unknown error", status);
    }
}
