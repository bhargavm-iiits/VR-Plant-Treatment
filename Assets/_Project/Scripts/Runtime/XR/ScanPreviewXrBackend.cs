using System.Collections.Generic;
using System.Threading.Tasks;

namespace BTP.XR
{
    /// <summary>
    /// Staging proof of concept (guide 5.3.2): resolves a scan through the published GET /api/scans/{scan_id} and the
    /// shared case map, and keeps the session, recipe and saved results on this PC (the fixture store rules: pinned recipe,
    /// revisions, one write per idempotency key). It needs no XR API or pairing, so it works before the backend routes
    /// exist; it is labelled STAGING PREVIEW, and nothing it saves reaches CropScan. The integrated flow is
    /// <see cref="HttpXrBackend"/>, where the backend checks that the paired device may open the scan.
    /// </summary>
    public sealed class ScanPreviewXrBackend : IXrBackend
    {
        public const string SessionPrefix = "preview-";
        public const int RecipeVersion = 1;

        readonly HttpXrBackend api;
        readonly FixtureXrBackend local;
        readonly XrCaseMap map;

        public string Label => "Staging preview";
        public bool IsFixture => true;

        public ScanPreviewXrBackend(HttpXrBackend publishedApi, FixtureXrBackend localStore, XrCaseMap caseMap)
        {
            api = publishedApi;
            local = localStore;
            map = caseMap;
        }

        public async Task<XrResult<XrSessionData>> OpenSessionForScan(string scanId)
        {
            var scan = await api.GetScan(scanId);
            if (!scan.Ok)
                return XrResult<XrSessionData>.Fail(scan.Error, scan.Status);
            var session = SessionFor(scan.Value, map, out var reason);
            return session != null
                ? XrResult<XrSessionData>.Success(local.AddSessionIfMissing(session))
                : XrResult<XrSessionData>.Fail(reason, 422);
        }

        /// <summary>The local session for a classified, mapped scan; null with a learner-facing reason otherwise.</summary>
        public static XrSessionData SessionFor(XrScan scan, XrCaseMap map, out string reason)
        {
            reason = null;
            if (scan == null || string.IsNullOrEmpty(scan.id))
            {
                reason = "The scan record could not be read.";
                return null;
            }

            if (string.IsNullOrEmpty(scan.disease_id))
            {
                reason = $"The scan has no diagnosis yet (status '{scan.status}').";
                return null;
            }

            if (map == null || !map.TryResolve(scan.disease_id, out var row, out reason))
                return null;
            return new XrSessionData
            {
                xrSessionId = SessionPrefix + scan.id,
                scanId = scan.id,
                backendDiseaseId = scan.disease_id,
                plantId = row.plantId,
                diseaseId = row.diseaseId,
                specimenId = row.specimenId,
                recipeId = row.recipeId,
                recipeVersion = RecipeVersion,
                revision = 0,
            };
        }

        public Task<XrResult<XrLaunch>> CreateLaunch(string xrSessionId, string scanId, string idempotencyKey) =>
            local.CreateLaunch(xrSessionId, scanId, idempotencyKey);

        public Task<XrResult<XrSessionData>> GetSession(string xrSessionId) => local.GetSession(xrSessionId);
        public Task<XrResult<XrRecipe>> GetRecipe(string xrSessionId) => local.GetRecipe(xrSessionId);
        public Task<XrSubmitResponse> SubmitTreatmentResult(XrTreatmentSubmission submission) => local.SubmitTreatmentResult(submission);
        public Task<XrResult<List<XrLaunch>>> GetPendingLaunches() => local.GetPendingLaunches();

        public Task<XrResult<bool>> ReportLaunchStatus(string launchId, string status, string detail, bool xrReady) =>
            local.ReportLaunchStatus(launchId, status, detail, xrReady);
    }
}
