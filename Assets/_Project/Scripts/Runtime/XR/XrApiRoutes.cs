using System;

namespace BTP.XR
{
    /// <summary>
    /// PROPOSED CropScan XR routes (implementation guide, section 3). They are not on the published API yet; confirm the
    /// names and payloads with the backend owner and change them only here.
    /// </summary>
    public static class XrApiRoutes
    {
        public const string Health = "/healthz";
        public const string PairingRedeem = "/api/xr/pairings/redeem";
        /// <summary>Laptop demo only: the app pairs itself for a scan's owner when the API runs on this computer.</summary>
        public const string LocalPair = "/api/xr/local-pair";
        public const string PendingLaunches = "/api/xr/launches/pending";
        public const string Socket = "/ws/xr";
        public const string Sessions = "/api/xr/sessions";

        /// <summary>The existing, published scan route (used by the staging preview).</summary>
        public static string Scan(string scanId) => "/api/scans/" + Escape(scanId);
        public static string Launches(string xrSessionId) => Session(xrSessionId) + "/launches";

        public static string Session(string xrSessionId) => "/api/xr/sessions/" + Escape(xrSessionId);
        public static string Recipe(string xrSessionId) => Session(xrSessionId) + "/recipe";
        public static string TreatmentResult(string xrSessionId) => Session(xrSessionId) + "/treatment-result";
        public static string LaunchStatus(string launchId) => "/api/xr/launches/" + Escape(launchId) + "/status";

        public const string IdempotencyHeader = "Idempotency-Key";

        static string Escape(string segment) => Uri.EscapeDataString(segment ?? "");
    }
}
