using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BTP.XR
{
    /// <summary>
    /// Unity's outbound connection to WSS /ws/xr. It announces the device and its headset readiness, sends a heartbeat,
    /// receives targeted "vr.launch" events and reconnects with backoff. The socket runs on a worker thread; every
    /// message and state change is queued and handed to Unity in <see cref="Drain"/> on the main thread. Session and
    /// recipe data never travel here, only the launch to open (they are fetched over HTTPS).
    /// </summary>
    public sealed class XrLaunchSocket : IDisposable
    {
        public enum State { Disconnected, Connecting, Connected }

        public readonly struct Event
        {
            public readonly State? StateChanged;
            public readonly XrSocketMessage Message;
            public readonly string Note;

            public Event(State? state, XrSocketMessage message, string note)
            {
                StateChanged = state;
                Message = message;
                Note = note;
            }
        }

        public const float HeartbeatSeconds = 15f;
        const int ReceiveBufferBytes = 16 * 1024;

        readonly string url;
        readonly Func<string> token;
        readonly string deviceId;
        readonly string appVersion;
        readonly ConcurrentQueue<Event> inbox = new ConcurrentQueue<Event>();
        readonly System.Random random = new System.Random();
        CancellationTokenSource stop;
        volatile bool xrReady;
        volatile string display = "unknown";

        public XrLaunchSocket(string socketUrl, string device, Func<string> deviceToken)
        {
            url = socketUrl;
            deviceId = device;
            token = deviceToken;
            // Unity APIs are main-thread only; the worker thread reuses this copy.
            appVersion = Application.version;
        }

        public bool IsRunning => stop != null && !stop.IsCancellationRequested;

        public void Start()
        {
            if (IsRunning)
                return;
            stop = new CancellationTokenSource();
            var cancel = stop.Token;
            Task.Run(() => Run(cancel), cancel);
        }

        public void Stop()
        {
            if (stop == null)
                return;
            stop.Cancel();
            stop.Dispose();
            stop = null;
        }

        public void Dispose() => Stop();

        /// <summary>Main thread: the readiness the next heartbeat reports.</summary>
        public void SetReadiness(bool ready, string displayState)
        {
            xrReady = ready;
            display = displayState ?? "unknown";
        }

        /// <summary>Main thread: takes the queued events.</summary>
        public bool TryDequeue(out Event e) => inbox.TryDequeue(out e);

        async Task Run(CancellationToken cancel)
        {
            var attempt = 0;
            while (!cancel.IsCancellationRequested)
            {
                inbox.Enqueue(new Event(State.Connecting, null, null));
                string note = null;
                using (var socket = new ClientWebSocket())
                {
                    try
                    {
                        var bearer = token?.Invoke();
                        if (string.IsNullOrEmpty(bearer))
                            throw new InvalidOperationException("not paired");
                        socket.Options.SetRequestHeader("Authorization", "Bearer " + bearer);
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                        {
                            connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                            await socket.ConnectAsync(new Uri(url), connectTimeout.Token);
                        }

                        attempt = 0;
                        inbox.Enqueue(new Event(State.Connected, null, null));
                        await Send(socket, Hello("device.hello"), cancel);
                        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                        {
                            var heartbeat = Heartbeat(socket, linked.Token);
                            await Receive(socket, cancel);
                            linked.Cancel();
                            try { await heartbeat; } catch (OperationCanceledException) { }
                        }
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        note = exception.Message;
                    }
                }

                inbox.Enqueue(new Event(State.Disconnected, null, note));
                if (cancel.IsCancellationRequested)
                    break;
                double jitter;
                lock (random)
                    jitter = random.NextDouble();
                try
                {
                    await Task.Delay(XrBackoff.Delay(attempt++, jitter), cancel);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        async Task Heartbeat(ClientWebSocket socket, CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(HeartbeatSeconds), cancel);
                await Send(socket, Hello("device.heartbeat"), cancel);
            }
        }

        async Task Receive(ClientWebSocket socket, CancellationToken cancel)
        {
            var buffer = new byte[ReceiveBufferBytes];
            var text = new StringBuilder();
            while (!cancel.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
                    return;
                }

                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    if (text.Length > 256 * 1024)
                        throw new InvalidOperationException("A socket message was too large.");
                    continue;
                }

                var json = text.ToString();
                text.Clear();
                XrSocketMessage message = null;
                try { message = JsonUtility.FromJson<XrSocketMessage>(json); } catch { }
                if (message == null || string.IsNullOrEmpty(message.type))
                    continue;
                if (message.type == "ping")
                {
                    await Send(socket, Hello("pong"), cancel);
                    continue;
                }

                inbox.Enqueue(new Event(null, message, null));
            }
        }

        string Hello(string type) => JsonUtility.ToJson(new XrSocketMessage
        {
            type = type, deviceId = deviceId, xrReady = xrReady, display = display, appVersion = appVersion,
        });

        static Task Send(ClientWebSocket socket, string json, CancellationToken cancel)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancel);
        }
    }
}
