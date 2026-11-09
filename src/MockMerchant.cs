// Wireless ECR Simulator - by Ahmad El Lahib
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WirelessEcrSim
{
    /// <summary>
    /// Stands in for the merchant's (Boonz / weimi24) "Tap Start" API when there is no real door:
    /// POST /order-center-web/third/order/areeba {terminalId, timestamp, lockNum} with header X-Signature.
    /// Point the local integration server at it with merchant.ecr.mock-enabled=false and
    /// merchant.ecr.url=http://localhost:PORT. A successful answer raises DoorOpened, and the window then
    /// sends the PREAUTH the way Boonz does.
    /// </summary>
    public class MockMerchant
    {
        public const string DefaultPath = "/order-center-web/third/order/areeba";

        public event Action<string, string> Log;                 // (tag, text)
        public event Action<string, int> DoorOpened;             // (terminalId, lockNum)

        private HttpListener listener;

        /// <summary>"200 success", "-1 signature error", "7002 timestamp expired", "HTTP 500", "No answer (timeout)"</summary>
        public string ReplyMode { get; set; }
        public string Secret { get; set; }
        public int Port { get; private set; }
        public bool Running { get { return listener != null && listener.IsListening; } }

        public void Start(int port)
        {
            Stop();
            HttpListener l = new HttpListener();
            l.Prefixes.Add("http://localhost:" + port + "/");
            l.Prefixes.Add("http://127.0.0.1:" + port + "/");
            l.Start();
            listener = l;
            Port = port;
            Emit("DOOR", "mock Tap Start listening on http://localhost:" + port + DefaultPath);
            Task.Run(() => Loop(l));
        }

        public void Stop()
        {
            HttpListener l = listener;
            listener = null;
            if (l != null)
            {
                try { l.Stop(); l.Close(); } catch { }
                Emit("DOOR", "mock Tap Start stopped");
            }
        }

        private async Task Loop(HttpListener l)
        {
            while (l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await l.GetContextAsync(); }
                catch { return; }
                HttpListenerContext c = ctx;
                Task t = Task.Run(() => Serve(c));
            }
        }

        private void Serve(HttpListenerContext ctx)
        {
            try
            {
                string body;
                using (StreamReader r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                string signature = ctx.Request.Headers["X-Signature"];
                Emit("DOOR", "Tap Start called: " + ctx.Request.HttpMethod + " " + ctx.Request.Url.AbsolutePath + " " + body);

                Dictionary<string, object> req = Json.TryParseObject(body);
                string terminalId = Json.Str(req, "terminalId");
                string lockText = Json.Str(req, "lockNum");
                string timestamp = Json.Str(req, "timestamp");
                int lockNum;
                if (!int.TryParse(lockText, out lockNum)) lockNum = 1;

                if (!string.IsNullOrEmpty(Secret))
                {
                    string expected = Sign(SignString(terminalId, timestamp, lockText), Secret);
                    bool match = string.Equals(expected, signature, StringComparison.OrdinalIgnoreCase);
                    Emit(match ? "OK" : "WARN", "X-Signature " + (match ? "matches" : "does NOT match (expected " + expected + ")"));
                }

                string mode = ReplyMode ?? "200 success";
                if (mode.StartsWith("No answer"))
                {
                    Emit("DOOR", "not answering (the server gives up after merchant.ecr.timeout-seconds, default 10 s)");
                    Thread.Sleep(15000);
                    ctx.Response.Abort();
                    return;
                }
                if (mode.StartsWith("HTTP 500"))
                {
                    Reply(ctx, 500, "{\"error\":\"simulated server error\"}");
                    return;
                }
                if (mode.StartsWith("-1"))
                {
                    Reply(ctx, 200, "{\"code\":-1,\"msg\":\"signature error\",\"enMsg\":\"signature error\",\"data\":null}");
                    return;
                }
                if (mode.StartsWith("7002"))
                {
                    Reply(ctx, 200, "{\"code\":7002,\"msg\":\"timestamp expired\",\"enMsg\":\"timestamp expired\",\"data\":null}");
                    return;
                }
                Reply(ctx, 200, "{\"code\":200,\"msg\":\"success\",\"enMsg\":\"success\",\"data\":{\"orderNo\":\"SIM" + DateTime.Now.ToString("yyMMddHHmmss") + "\"}}");
                Action<string, int> h = DoorOpened;
                if (h != null) h(terminalId, lockNum);
            }
            catch (Exception e)
            {
                Emit("ERR", "mock Tap Start: " + e.Message);
            }
        }

        private void Reply(HttpListenerContext ctx, int status, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
            Emit("DOOR", "answered HTTP " + status + " " + json);
        }

        /// <summary>Same plain string as MerchantDoorRequestDto.toSignString: sorted keys, non-empty values.</summary>
        public static string SignString(string terminalId, string timestamp, string lockNum)
        {
            SortedDictionary<string, string> fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            fields["terminalId"] = terminalId;
            fields["timestamp"] = timestamp;
            fields["lockNum"] = lockNum;
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> kv in fields)
                if (!string.IsNullOrEmpty(kv.Value)) parts.Add(kv.Key + "=" + kv.Value);
            return string.Join("&", parts);
        }

        public static string Sign(string plain, string secret)
        {
            using (HMACSHA256 mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                byte[] hash = mac.ComputeHash(Encoding.UTF8.GetBytes(plain));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private void Emit(string tag, string text)
        {
            Action<string, string> h = Log;
            if (h != null) h(tag, text);
        }
    }
}
