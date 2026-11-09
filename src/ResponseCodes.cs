// Wireless ECR Simulator - by Ahmad El Lahib
using System.Collections.Generic;

namespace WirelessEcrSim
{
    public enum Outcome { Approved, Declined, Pending, Error }

    /// <summary>What each integration server responseCode means (CommonConstants + POS codes).</summary>
    public static class ResponseCodes
    {
        private static readonly Dictionary<string, string> Meanings = new Dictionary<string, string>
        {
            { "0", "Approved / success" },
            { "1", "Sent to the terminal, but no result came back within waitingTime. Check the result with trx-info or pos-trx-info." },
            { "-1", "Declined, cancelled or failed on the terminal (or the server could not send it to the POS)." },
            { "-2", "Missing parameter in the request (terminalId, pass, amount, currency, ecrRefNo, lockNum ...)." },
            { "-3", "Transaction check service (ISO / POS microservice) unavailable or returned an error." },
            { "-4", "Transaction ignored: invalid or unknown requestID." },
            { "-5", "The merchant ECR (Tap Start) refused or could not open the door." },
            { "404", "Terminal is not registered: it has not logged in on the WebSocket." },
            { "450", "Connection with the POS is lost." },
            { "500", "Wrong password: does not match the password the terminal logged in with." },
            { "5003", "Transaction not found." },
            { "1116", "Insufficient funds (host code, only when TMS ecr.mirrorResponseCode is on)." },
            { "1100", "Do not honour (host code, mirrored)." },
            { "1101", "Expired card (host code, mirrored)." },
            { "1117", "Incorrect PIN (host code, mirrored)." },
            { "9112", "Issuer or switch is inoperative (host code, mirrored)." },
        };

        public static string Explain(string code)
        {
            if (code == null) return "No responseCode in the reply.";
            string m;
            if (Meanings.TryGetValue(code.Trim(), out m)) return m;
            if (code.Length == 4) return "Host response code mirrored by the terminal (TMS ecr.mirrorResponseCode).";
            return "Unknown code.";
        }

        public static Outcome Classify(ApiResult r)
        {
            if (r == null || r.Error != null) return Outcome.Error;
            string code = r.Code;
            if (code == null) return r.Ok ? Outcome.Approved : Outcome.Error;
            if (code == "0") return Outcome.Approved;
            if (code == "1") return Outcome.Pending;
            if (code == "-2" || code == "-3" || code == "404" || code == "450" || code == "500") return Outcome.Error;
            return Outcome.Declined;
        }
    }
}
