using System;
using System.Security.Cryptography;
using System.Text;

namespace BTP.XR
{
    /// <summary>
    /// The same-laptop "Enter the field" activation: the website opens cropscanxr://open-field?scan=&lt;uuid&gt;, Windows runs
    /// CropScanLauncher.exe with that link, and the launcher either starts the player with --crop-scan-id &lt;uuid&gt; or
    /// forwards the ID to the running player over a per-user named pipe. This file has no Unity dependency: the launcher
    /// (Tools/CropScanLauncher) compiles the very same source, so both sides validate the link and speak the pipe
    /// protocol identically. The link only ever carries a scan ID; access is decided by the backend, never by the link.
    /// </summary>
    public static class XrScanLink
    {
        public const string Scheme = "cropscanxr";
        public const string Host = "open-field";
        public const string ScanQueryKey = "scan";
        public const string ScanArgument = "--crop-scan-id";
        public const int MaxUriLength = 256;

        // Pipe protocol: one line per connection, answered with one line.
        public const string Ping = "PING";
        public const string OpenPrefix = "OPEN ";
        public const string Ok = "OK";
        public const string ErrorPrefix = "ERR ";
        public const int MaxLineLength = 128;

        /// <summary>A scan ID in canonical form (lower-case 8-4-4-4-12 UUID); false for anything else.</summary>
        public static bool TryParseScanId(string text, out string scanId)
        {
            scanId = null;
            if (string.IsNullOrEmpty(text) || text.Length != 36)
                return false;
            if (!Guid.TryParseExact(text, "D", out var guid))
                return false;
            scanId = guid.ToString("D");
            return true;
        }

        /// <summary>
        /// Accepts exactly cropscanxr://open-field?scan=&lt;uuid&gt; (an optional trailing slash after the host is allowed,
        /// because browsers add one). No user info, port, other path, other or repeated query keys, or fragment.
        /// </summary>
        public static bool TryParseOpenFieldUri(string text, out string scanId, out string error)
        {
            scanId = null;
            error = null;
            if (string.IsNullOrEmpty(text) || text.Length > MaxUriLength)
                return Fail("The link is empty or too long.", out error);
            if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
                return Fail("The link is not a valid URI.", out error);
            if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
                return Fail($"Only {Scheme}: links are accepted.", out error);
            if (!string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
                return Fail($"Only {Scheme}://{Host} links are accepted.", out error);
            if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort && uri.Port != -1)
                return Fail("The link must not carry user information or a port.", out error);
            if (uri.AbsolutePath != "/" && uri.AbsolutePath != "")
                return Fail("The link has an unexpected path.", out error);
            if (!string.IsNullOrEmpty(uri.Fragment))
                return Fail("The link must not carry a fragment.", out error);

            var query = uri.Query.StartsWith("?", StringComparison.Ordinal) ? uri.Query.Substring(1) : uri.Query;
            if (string.IsNullOrEmpty(query) || query.IndexOf('&') >= 0 || query.IndexOf(';') >= 0)
                return Fail("The link must carry exactly one scan.", out error);
            var equals = query.IndexOf('=');
            if (equals <= 0 || query.Substring(0, equals) != ScanQueryKey)
                return Fail("The link must carry exactly one scan.", out error);
            var value = Uri.UnescapeDataString(query.Substring(equals + 1));
            if (!TryParseScanId(value, out scanId))
                return Fail("The scan ID is not a UUID.", out error);
            return true;
        }

        /// <summary>Finds "--crop-scan-id &lt;uuid&gt;" in a command line. False with no error when the flag is absent.</summary>
        public static bool TryFindScanArgument(string[] args, out string scanId, out string error)
        {
            scanId = null;
            error = null;
            if (args == null)
                return false;
            var found = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], ScanArgument, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (found)
                    return Fail($"{ScanArgument} was given twice.", out error);
                found = true;
                if (i + 1 >= args.Length || !TryParseScanId(args[i + 1], out scanId))
                    return Fail($"{ScanArgument} must be followed by a scan UUID.", out error);
                i++;
            }

            return found;
        }

        /// <summary>
        /// The receiver's pipe name for a Windows user. Every sender computes the same name: the launcher, the player and
        /// the local CropScan backend (Python: sha256 of the lower-case USERNAME). Only the user name is used, because
        /// Mono, .NET and Python report the logon domain differently.
        /// </summary>
        public static string PipeName(string userName)
        {
            var identity = (userName ?? "").ToLowerInvariant();
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                var hex = new StringBuilder("CropScanXR.OpenField.");
                for (var i = 0; i < 8; i++)
                    hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        public static string CurrentUserPipeName() => PipeName(Environment.UserName);

        /// <summary>
        /// The Unity Editor's own receiver (XrEditorBridge), listening while the Editor is open but not playing: it opens
        /// XRBootstrap and presses Play with the scan. A separate name, so it never clashes with the in-game receiver.
        /// </summary>
        public static string EditorPipeName(string userName) => "CropScanXR.Editor." + PipeName(userName).Substring("CropScanXR.OpenField.".Length);

        public static string CurrentUserEditorPipeName() => EditorPipeName(Environment.UserName);

        /// <summary>A pipe line: PING (is the receiver ready?) or OPEN &lt;uuid&gt;.</summary>
        public static bool TryParsePipeLine(string line, out bool ping, out string scanId)
        {
            ping = false;
            scanId = null;
            if (string.IsNullOrEmpty(line) || line.Length > MaxLineLength)
                return false;
            if (line == Ping)
            {
                ping = true;
                return true;
            }

            return line.StartsWith(OpenPrefix, StringComparison.Ordinal) && TryParseScanId(line.Substring(OpenPrefix.Length), out scanId);
        }

        static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
