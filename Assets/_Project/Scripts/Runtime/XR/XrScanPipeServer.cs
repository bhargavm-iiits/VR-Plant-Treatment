using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace BTP.XR
{
    /// <summary>
    /// The running player's receiver for "Enter the field" clicks. CropScanLauncher connects to a named pipe whose name is
    /// derived from the Windows user (<see cref="XrScanLink.CurrentUserPipeName"/>) and sends one line: PING or
    /// OPEN &lt;uuid&gt;. One connection at a time, so clicks are serialised. Valid scan IDs are queued for Unity's main
    /// thread (<see cref="TryDequeue"/>); everything else is refused. The pipe keeps Windows' default access (full
    /// control for its owner, read-only for others), and the line can only ever name a scan UUID, whose access the
    /// backend checks.
    /// </summary>
    public sealed class XrScanPipeServer : IDisposable
    {
        const int ReadTimeoutMs = 2000;

        readonly string pipeName;
        readonly ConcurrentQueue<string> received = new ConcurrentQueue<string>();
        Thread thread;
        volatile bool stopping;

        public XrScanPipeServer(string name)
        {
            pipeName = name;
        }

        public string PipeName => pipeName;

        /// <summary>Why the receiver stopped (for example another player already owns the pipe), or null.</summary>
        public string Error { get; private set; }

        public bool IsRunning => thread != null && thread.IsAlive;

        public void Start()
        {
            if (thread != null)
                return;
            stopping = false;
            thread = new Thread(Run) { IsBackground = true, Name = "CropScan XR scan receiver" };
            thread.Start();
        }

        /// <summary>Main thread: the next received scan ID.</summary>
        public bool TryDequeue(out string scanId) => received.TryDequeue(out scanId);

        void Run()
        {
            while (!stopping)
            {
                NamedPipeServerStream server;
                try
                {
                    server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                }
                catch (Exception exception)
                {
                    // Usually another player instance (or a second Editor) already listens on this user's pipe.
                    Error = "The scan receiver could not start: " + exception.Message;
                    return;
                }

                using (server)
                {
                    try
                    {
                        server.WaitForConnection();
                        if (stopping)
                            break;
                        Serve(server);
                    }
                    catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException || exception is InvalidOperationException)
                    {
                        // A client that vanished mid-message; listen again.
                    }
                }
            }
        }

        void Serve(NamedPipeServerStream server)
        {
            var reader = new StreamReader(server, new UTF8Encoding(false), false, 256, true);
            var writer = new StreamWriter(server, new UTF8Encoding(false), 256, true) { AutoFlush = true, NewLine = "\n" };
            var read = reader.ReadLineAsync();
            if (!read.Wait(ReadTimeoutMs))
                return;
            var line = read.Result?.Trim();
            string answer;
            if (!XrScanLink.TryParsePipeLine(line, out var ping, out var scanId))
                answer = XrScanLink.ErrorPrefix + "expected PING or OPEN <scan uuid>";
            else
            {
                if (!ping)
                    received.Enqueue(scanId);
                answer = XrScanLink.Ok;
            }

            writer.WriteLine(answer);
            try
            {
                server.WaitForPipeDrain();
            }
            catch (Exception)
            {
                // The client may already have closed after reading the answer.
            }
        }

        public void Dispose()
        {
            if (thread == null)
                return;
            stopping = true;
            // Unblock WaitForConnection with a throwaway client.
            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                    client.Connect(200);
            }
            catch (Exception)
            {
                // Already stopped.
            }

            thread.Join(1000);
            thread = null;
        }
    }
}
