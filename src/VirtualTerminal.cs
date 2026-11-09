// Wireless ECR Simulator - by Ahmad El Lahib
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WirelessEcrSim
{
    public enum TerminalState { Disconnected, Connecting, Connected, Registered }

    /// <summary>A transaction the server pushed to the virtual terminal.</summary>
    public class IncomingRequest
    {
        public string RequestId { get; set; }
        public string Type { get; set; }
        public string Amount { get; set; }
        public string Currency { get; set; }
        public string EcrRefNo { get; set; }
        public string Stan { get; set; }
        public DateTime ReceivedAt { get; set; }
        public string Summary
        {
            get
            {
                string s = Type;
                if (!string.IsNullOrEmpty(Amount)) s += "  " + Amount + " " + (Currency ?? "");
                if (!string.IsNullOrEmpty(EcrRefNo)) s += "  ref " + EcrRefNo;
                return s;
            }
        }
        public string Received { get { return ReceivedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture); } }
    }

    /// <summary>
    /// Plays a POS terminal on the integration server WebSocket, like the Android app's WirelessEcrConnection:
    /// Login (with heartbeat for the modern protocol), answers transactions with TRXResult, re-sends a result
    /// after a reconnect until CONFIRMATION, sends OpenDoor (the START button) and reads DoorResult.
    /// </summary>
    public class VirtualTerminal
    {
        public const int HeartbeatSec = 30;

        public event Action<string, string> Log;           // (tag, text)
        public event Action<TerminalState> StateChanged;
        public event Action<IncomingRequest> RequestReceived;
        public event Action<string, string, string> DoorResult;  // (requestID, code, desc)

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, string> outbox = new ConcurrentDictionary<string, string>();
        private readonly Random random = new Random();
        private volatile bool wantConnected;
        private int stan = new Random().Next(1000, 9000);

        public string Url { get; set; }
        public string TerminalId { get; set; }
        public string Pass { get; set; }
        public string Country { get; set; }
        public bool Modern { get; set; }
        public bool AutoReconnect { get; set; }
        public TerminalState State { get; private set; }

        public int OutboxCount { get { return outbox.Count; } }

        public void Connect()
        {
            wantConnected = true;
            if (cts != null) cts.Cancel();
            cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            Task.Run(() => RunAsync(token));
        }

        public void Disconnect()
        {
            wantConnected = false;
            if (cts != null) cts.Cancel();
            ClientWebSocket s = socket;
            if (s != null)
            {
                try { s.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(1500); }
                catch { }
                try { s.Abort(); } catch { }
            }
            SetState(TerminalState.Disconnected);
        }

        /// <summary>Drops the link without a close frame, like a terminal losing Wi-Fi.</summary>
        public void DropLink()
        {
            ClientWebSocket s = socket;
            if (s != null) { try { s.Abort(); } catch { } }
        }

        private async Task RunAsync(CancellationToken token)
        {
            int backoffSec = 1;
            while (!token.IsCancellationRequested && wantConnected)
            {
                bool loggedIn = false;
                ClientWebSocket s = new ClientWebSocket();
                socket = s;
                try
                {
                    // KeepAlive frames keep the server's idle timer alive (it allows 3 heartbeats of silence)
                    s.Options.KeepAliveInterval = TimeSpan.FromSeconds(Modern ? 20 : 30);
                    SetState(TerminalState.Connecting);
                    Emit("WS", "connecting to " + Url);
                    using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        connectCts.CancelAfter(15000);
                        await s.ConnectAsync(new Uri(Url), connectCts.Token);
                    }
                    SetState(TerminalState.Connected);
                    Dictionary<string, object> login = new Dictionary<string, object>();
                    login["type"] = "Login";
                    login["terminalId"] = TerminalId;
                    login["pass"] = Pass;
                    if (Modern) login["heartbeat"] = HeartbeatSec;
                    if (!string.IsNullOrWhiteSpace(Country)) login["country"] = Country.Trim().ToUpperInvariant();
                    await SendRawAsync(Json.Serialize(login), "Login");
                    Task ping = Modern ? PingLoop(s, token) : null;
                    loggedIn = await ReceiveLoop(s, token);
                }
                catch (Exception e)
                {
                    if (!token.IsCancellationRequested) Emit("ERR", "connection failed: " + Describe(e));
                }
                finally
                {
                    try { s.Abort(); } catch { }
                    s.Dispose();
                }
                if (token.IsCancellationRequested || !wantConnected) break;
                SetState(TerminalState.Disconnected);
                if (!AutoReconnect)
                {
                    Emit("WS", "disconnected (auto reconnect is off)");
                    wantConnected = false;
                    break;
                }
                if (loggedIn) backoffSec = 1;
                Emit("WS", "reconnecting in " + backoffSec + " s");
                try { await Task.Delay(backoffSec * 1000, token); } catch { break; }
                backoffSec = Math.Min(backoffSec * 2, 30);
            }
            SetState(TerminalState.Disconnected);
        }

        private async Task PingLoop(ClientWebSocket s, CancellationToken token)
        {
            while (!token.IsCancellationRequested && s.State == WebSocketState.Open)
            {
                try { await Task.Delay(HeartbeatSec * 1000, token); } catch { return; }
                if (s != socket || s.State != WebSocketState.Open) return;
                // the server ignores {"type":"Ping"} but counts it as activity
                Dictionary<string, object> ping = new Dictionary<string, object>();
                ping["type"] = "Ping";
                ping["terminalId"] = TerminalId;
                try { await SendRawAsync(Json.Serialize(ping), null); } catch { return; }
            }
        }

        private async Task<bool> ReceiveLoop(ClientWebSocket s, CancellationToken token)
        {
            bool loggedIn = false;
            byte[] buffer = new byte[16 * 1024];
            while (s.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                MemoryStream ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await s.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        Emit("WS", "server closed the connection: " + r.CloseStatus + " " + r.CloseStatusDescription);
                        return loggedIn;
                    }
                    ms.Write(buffer, 0, r.Count);
                } while (!r.EndOfMessage);
                string text = Encoding.UTF8.GetString(ms.ToArray());
                if (Handle(text)) loggedIn = true;
            }
            return loggedIn;
        }

        /// <summary>Returns true when the message was a successful Login reply.</summary>
        private bool Handle(string text)
        {
            Emit("IN", Json.Mask(text));
            Dictionary<string, object> msg = Json.TryParseObject(text);
            if (msg == null) return false;
            string type = Json.Str(msg, "type") ?? "";
            string code = Json.Str(msg, "responseCode");
            string requestId = Json.Str(msg, "requestID");

            if (type.Equals("Login", StringComparison.OrdinalIgnoreCase))
            {
                if (code == "0")
                {
                    SetState(TerminalState.Registered);
                    Emit("OK", "registered as " + TerminalId + (Modern ? " (modern protocol, heartbeat " + HeartbeatSec + " s)" : " (legacy protocol)"));
                    FlushOutbox();
                    return true;
                }
                return false;
            }
            if (type.Length == 0 && code == "-2")
            {
                Emit("ERR", "login rejected: " + Json.Str(msg, "responseDesc"));
                return false;
            }
            if (type.Equals("CONFIRMATION", StringComparison.OrdinalIgnoreCase))
            {
                string removed;
                if (requestId != null && outbox.TryRemove(requestId, out removed))
                    Emit("OK", "server confirmed the result of " + Short(requestId));
                return false;
            }
            if (type.Equals("SocketState", StringComparison.OrdinalIgnoreCase))
            {
                Emit("WARN", "server says: " + code + " " + Json.Str(msg, "responseDesc"));
                return false;
            }
            if (type.Equals("DoorResult", StringComparison.OrdinalIgnoreCase))
            {
                Action<string, string, string> h = DoorResult;
                if (h != null) h(requestId, code, Json.Str(msg, "responseDesc"));
                return false;
            }
            if (!string.IsNullOrEmpty(requestId) && !string.IsNullOrEmpty(type) && code == null)
            {
                IncomingRequest req = new IncomingRequest
                {
                    RequestId = requestId,
                    Type = type.ToUpperInvariant(),
                    Amount = Json.Str(msg, "amount"),
                    Currency = Json.Str(msg, "currency"),
                    EcrRefNo = Json.Str(msg, "ecrRefNo"),
                    Stan = Json.Str(msg, "systemTraceNumber"),
                    ReceivedAt = DateTime.Now,
                };
                Action<IncomingRequest> h = RequestReceived;
                if (h != null) h(req);
            }
            else if (code != null && code != "0" && code != "1")
            {
                Emit("WARN", "server replied " + code + " " + Json.Str(msg, "responseDesc"));
            }
            return false;
        }

        /// <summary>Approved TRXResult in the shape the Android app sends.</summary>
        public Task Approve(IncomingRequest req, bool duplicate)
        {
            int myStan = Interlocked.Increment(ref stan);
            string stanText = myStan.ToString("000000", CultureInfo.InvariantCulture);
            DateTime now = DateTime.Now;
            Dictionary<string, object> result = new Dictionary<string, object>();
            if (req.Type == "SETTLE")
            {
                result["batchNumber"] = "1";
                result["transactionCount"] = 3;
                result["message"] = "Settlement successful";
            }
            else
            {
                result["responseCode"] = "0000";
                result["responseMessage"] = "Approved";
                result["transactionType"] = req.Type;
                result["amount"] = req.Amount;
                result["currency"] = req.Currency;
                result["systemTraceNumber"] = stanText;
                result["rrn"] = now.ToString("yyMMddHH", CultureInfo.InvariantCulture) + myStan.ToString("0000", CultureInfo.InvariantCulture).Substring(0, 4);
                result["authCode"] = random.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
                result["maskedPan"] = "476173******0119";
                result["cardType"] = "VISA";
                result["entryMode"] = "CONTACTLESS";
                result["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                result["time"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }
            return SendResult(req, "0", req.Type == "SETTLE" ? "Success" : "Approved", stanText, result, duplicate);
        }

        public Task Decline(IncomingRequest req, string code, string desc, bool duplicate)
        {
            return SendResult(req, string.IsNullOrWhiteSpace(code) ? "-1" : code.Trim(), string.IsNullOrWhiteSpace(desc) ? "Declined" : desc, null, null, duplicate);
        }

        public Task Busy(IncomingRequest req)
        {
            return SendResult(req, "-1", "Pos is busy with other request", null, null, false);
        }

        private async Task SendResult(IncomingRequest req, string code, string desc, string stanText, object result, bool duplicate)
        {
            Dictionary<string, object> json = new Dictionary<string, object>();
            json["type"] = "TRXResult";
            json["responseCode"] = code;
            json["terminalId"] = TerminalId;
            json["responseDesc"] = desc;
            if (!string.IsNullOrEmpty(req.EcrRefNo)) json["ecrRefNo"] = req.EcrRefNo;
            json["requestID"] = req.RequestId;
            if (stanText != null) json["systemTraceNumber"] = stanText;
            json["batchNumber"] = "1";
            json["pass"] = Pass;
            json["result"] = result;
            string text = Json.Serialize(json);
            // modern terminals keep the result until the server confirms it (WirelessEcrOutbox)
            if (Modern) outbox[req.RequestId] = text;
            await SendRawAsync(text, "TRXResult " + code);
            if (duplicate) await SendRawAsync(text, "TRXResult " + code + " (duplicate)");
        }

        public async Task<string> SendOpenDoor(int lockNum, string ecrRefNo)
        {
            string requestId = Guid.NewGuid().ToString();
            Dictionary<string, object> json = new Dictionary<string, object>();
            json["type"] = "OpenDoor";
            json["terminalId"] = TerminalId;
            json["pass"] = Pass;
            json["requestID"] = requestId;
            json["lockNum"] = lockNum;
            if (!string.IsNullOrEmpty(ecrRefNo)) json["ecrRefNo"] = ecrRefNo;
            await SendRawAsync(Json.Serialize(json), "OpenDoor");
            return requestId;
        }

        public Task SendCustom(string text)
        {
            return SendRawAsync(text, "custom");
        }

        private void FlushOutbox()
        {
            foreach (KeyValuePair<string, string> kv in outbox)
            {
                string text = kv.Value;
                Task.Run(async () =>
                {
                    try { await SendRawAsync(text, "re-sent unconfirmed result " + Short(kv.Key)); } catch { }
                });
            }
        }

        private async Task SendRawAsync(string text, string label)
        {
            ClientWebSocket s = socket;
            if (s == null || s.State != WebSocketState.Open) throw new InvalidOperationException("terminal is not connected");
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await sendLock.WaitAsync();
            try
            {
                await s.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                sendLock.Release();
            }
            if (label != null) Emit("OUT", Json.Mask(text));
        }

        private void SetState(TerminalState state)
        {
            if (State == state) return;
            State = state;
            Action<TerminalState> h = StateChanged;
            if (h != null) h(state);
        }

        private void Emit(string tag, string text)
        {
            Action<string, string> h = Log;
            if (h != null) h(tag, text);
        }

        private static string Short(string id)
        {
            return id != null && id.Length > 8 ? id.Substring(0, 8) : id;
        }

        private static string Describe(Exception e)
        {
            while (e.InnerException != null && (e is AggregateException || e is WebSocketException)) e = e.InnerException;
            return e.Message;
        }
    }
}
