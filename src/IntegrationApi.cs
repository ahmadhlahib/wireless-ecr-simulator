// Wireless ECR Simulator - by Ahmad El Lahib
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace WirelessEcrSim
{
    /// <summary>One HTTP exchange with the integration server, kept for display.</summary>
    public class ApiResult
    {
        public string Method;
        public string Url;
        public string RequestBody;
        public int HttpStatus;
        public string ResponseBody;
        public long ElapsedMs;
        public string Error;
        public Dictionary<string, object> Json;

        public string Code { get { return WirelessEcrSim.Json.Str(Json, "responseCode"); } }
        public string Desc { get { return WirelessEcrSim.Json.Str(Json, "responseDesc"); } }
        public bool Ok { get { return Error == null && HttpStatus >= 200 && HttpStatus < 300; } }
    }

    /// <summary>
    /// Merchant-side calls of the integration server (same requests as the Postman collection):
    /// POST /access-token/application-token, POST /integration/transaction, GET /integration/trx-info,
    /// GET /integration/pos-trx-info, POST /integration/door/open.
    /// </summary>
    public class IntegrationApi
    {
        static IntegrationApi()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = 20;
        }

        private readonly EnvProfile env;

        public IntegrationApi(EnvProfile env)
        {
            this.env = env;
        }

        public Task<ApiResult> ApplicationToken()
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["clientIdSecretKey"] = env.SecretId ?? "";
            return Send("POST", "/access-token/application-token", body, false, 30000);
        }

        /// <summary>Body of POST /integration/transaction, exactly the fields the server reads.</summary>
        public static Dictionary<string, object> TransactionBody(EnvProfile env, string type, string amount, string currency,
            string ecrRefNo, string stan, string tips, int waitingTimeMs)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["terminalId"] = env.TerminalId;
            body["pass"] = env.Pass;
            body["transactionType"] = type;
            if (type != "SETTLE")
            {
                if (type != "VOID")
                {
                    body["amount"] = amount;
                    body["currency"] = currency;
                    body["tips"] = tips ?? "";
                }
                body["ecrRefNo"] = ecrRefNo;
            }
            if (type == "VOID") body["systemTraceNumber"] = stan;
            body["waitingTime"] = waitingTimeMs;
            return body;
        }

        public Task<ApiResult> Transaction(Dictionary<string, object> body, int waitingTimeMs)
        {
            // the server holds the call until the terminal answers or waitingTime runs out
            return Send("POST", "/integration/transaction", body, true, waitingTimeMs + 30000);
        }

        public Task<ApiResult> TrxInfo(string ecrRefNo, string batchNumber)
        {
            string q = "?terminalId=" + Uri.EscapeDataString(env.TerminalId ?? "") + "&ecrRefNo=" + Uri.EscapeDataString(ecrRefNo ?? "");
            if (!string.IsNullOrEmpty(batchNumber)) q += "&batchNumber=" + Uri.EscapeDataString(batchNumber);
            return Send("GET", "/integration/trx-info" + q, null, true, 90000);
        }

        public Task<ApiResult> PosTrxInfo(string ecrRefNo)
        {
            string q = "?terminalId=" + Uri.EscapeDataString(env.TerminalId ?? "") + "&ecrRefNo=" + Uri.EscapeDataString(ecrRefNo ?? "");
            return Send("GET", "/integration/pos-trx-info" + q, null, true, 90000);
        }

        public Task<ApiResult> OpenDoor(int lockNum, string ecrRefNo)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["terminalId"] = env.TerminalId;
            body["pass"] = env.Pass;
            body["lockNum"] = lockNum;
            if (!string.IsNullOrEmpty(ecrRefNo)) body["ecrRefNo"] = ecrRefNo;
            return Send("POST", "/integration/door/open", body, true, 60000);
        }

        /// <summary>The same request as a curl command line (password and token hidden).</summary>
        public string Curl(string method, string path, Dictionary<string, object> body)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("curl -X ").Append(method).Append(" \"").Append(Base()).Append(path).Append("\"");
            sb.Append(" -H \"Content-Type: application/json\" -H \"Authorization: Bearer <token>\"");
            if (body != null) sb.Append(" -d '").Append(Json.Mask(Json.Serialize(body))).Append("'");
            return sb.ToString();
        }

        private string Base()
        {
            return (env.Server ?? "").TrimEnd('/');
        }

        private Task<ApiResult> Send(string method, string path, Dictionary<string, object> body, bool auth, int timeoutMs)
        {
            return Task.Run(() =>
            {
                ApiResult r = new ApiResult();
                r.Method = method;
                r.Url = Base() + path;
                r.RequestBody = body == null ? null : Json.Serialize(body);
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(r.Url);
                    req.Method = method;
                    req.Accept = "*/*";
                    req.UserAgent = "WirelessEcrSimulator/1.0";
                    req.Timeout = timeoutMs;
                    req.ReadWriteTimeout = timeoutMs;
                    req.KeepAlive = false;
                    if (auth && !string.IsNullOrEmpty(env.Token))
                    {
                        string token = env.Token.Trim();
                        req.Headers["Authorization"] = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
                    }
                    if (r.RequestBody != null)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(r.RequestBody);
                        req.ContentType = "application/json";
                        req.ContentLength = bytes.Length;
                        using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                    }
                    HttpWebResponse resp;
                    try { resp = (HttpWebResponse)req.GetResponse(); }
                    catch (WebException we)
                    {
                        resp = we.Response as HttpWebResponse;
                        if (resp == null) throw;
                    }
                    using (resp)
                    using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        r.HttpStatus = (int)resp.StatusCode;
                        r.ResponseBody = reader.ReadToEnd();
                    }
                    r.Json = Json.TryParseObject(r.ResponseBody);
                }
                catch (WebException e)
                {
                    r.Error = e.Status == WebExceptionStatus.Timeout ? "no answer within " + (timeoutMs / 1000) + " s" : e.Message;
                }
                catch (Exception e)
                {
                    r.Error = e.Message;
                }
                r.ElapsedMs = watch.ElapsedMilliseconds;
                return r;
            });
        }
    }
}
