using System;

namespace BTP.XR
{
    /// <summary>
    /// How the Windows player was started. With no flags the app is the unchanged offline demo.
    ///   -xr-fixture default|&lt;path.json&gt;   Fixture mode: open the agreed session from the bundled or a local fixture.
    ///   -xr-fixture-drop-response          Fixture mode: lose the response of the first save (tests the retry path).
    ///   -xr-fixture-reset                  Fixture mode: forget saved fixture results before starting.
    ///   -xr-api https://host               Connected mode against a staging API (pairing is done in the player).
    ///   -xr-ws wss://host/ws/xr            Optional socket URL; derived from -xr-api when left out.
    /// </summary>
    public sealed class XrLaunchArgs
    {
        public const string DefaultFixture = "default";

        public string FixturePath;
        public bool DropFirstResponse;
        public bool ResetFixture;
        public string ApiBaseUrl;
        public string SocketUrl;

        public bool IsFixture => !string.IsNullOrEmpty(FixturePath);
        public bool IsConnected => !IsFixture && !string.IsNullOrEmpty(ApiBaseUrl);

        public static XrLaunchArgs Parse(string[] args)
        {
            var result = new XrLaunchArgs();
            if (args == null)
                return result;
            for (var i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[++i] : null;
                switch (args[i].ToLowerInvariant())
                {
                    case "-xr-fixture":
                        result.FixturePath = Next() ?? DefaultFixture;
                        break;
                    case "-xr-fixture-drop-response":
                        result.DropFirstResponse = true;
                        break;
                    case "-xr-fixture-reset":
                        result.ResetFixture = true;
                        break;
                    case "-xr-api":
                        result.ApiBaseUrl = TrimSlash(Next());
                        break;
                    case "-xr-ws":
                        result.SocketUrl = Next();
                        break;
                }
            }

            if (string.IsNullOrEmpty(result.SocketUrl) && !string.IsNullOrEmpty(result.ApiBaseUrl))
                result.SocketUrl = SocketUrlFor(result.ApiBaseUrl);
            return result;
        }

        /// <summary>https://host → wss://host/ws/xr (http → ws for a local test server).</summary>
        public static string SocketUrlFor(string apiBaseUrl)
        {
            if (string.IsNullOrEmpty(apiBaseUrl))
                return null;
            var url = TrimSlash(apiBaseUrl);
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "wss://" + url.Substring("https://".Length);
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                url = "ws://" + url.Substring("http://".Length);
            return url + XrApiRoutes.Socket;
        }

        static string TrimSlash(string url) => string.IsNullOrEmpty(url) ? url : url.TrimEnd('/');
    }
}
