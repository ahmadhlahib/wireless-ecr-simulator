// Wireless ECR Simulator - by Ahmad El Lahib
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WirelessEcrSim
{
    /// <summary>One integration server environment (Local, SIT, Iraq PROD, ...).</summary>
    public class EnvProfile
    {
        public string Name { get; set; }
        public string Server { get; set; }
        public string WsUrl { get; set; }
        public string TerminalId { get; set; }
        public string Pass { get; set; }
        public string SecretId { get; set; }
        public string Token { get; set; }
        public string Currency { get; set; }
        public bool IsProduction { get; set; }

        public EnvProfile Copy()
        {
            return (EnvProfile)MemberwiseClone();
        }
    }

    /// <summary>
    /// Everything the window remembers between runs. Saved next to the program as JSON; passwords,
    /// client secrets and tokens are encrypted with Windows DPAPI for the current Windows user.
    /// </summary>
    public class AppSettings
    {
        public List<EnvProfile> Profiles { get; set; }
        public string SelectedProfile { get; set; }
        public long NextEcrRefNo { get; set; }
        public string Amount { get; set; }
        public int WaitingTimeSec { get; set; }
        public bool AutoIncrementRef { get; set; }

        public bool TerminalModern { get; set; }
        public bool TerminalAutoReconnect { get; set; }
        public string TerminalCountry { get; set; }
        public string TerminalMode { get; set; }
        public string DeclineCode { get; set; }
        public string DeclineDesc { get; set; }
        public double AnswerDelaySec { get; set; }

        public bool BoonzAutoPreauth { get; set; }
        public bool PretendDoorOpened { get; set; }
        public string BoonzPreauthAmount { get; set; }
        public double BoonzPreauthDelaySec { get; set; }
        public int LockNum { get; set; }
        public int MockMerchantPort { get; set; }
        public string MockMerchantReply { get; set; }
        public string MockMerchantSecret { get; set; }

        public bool RetryEnabled { get; set; }
        public string RetryCode { get; set; }
        public string RetryStep { get; set; }
        public string RetryMinAmount { get; set; }

        public static string FilePath
        {
            get { return Path.Combine(Program.AppDir, "wireless-ecr-simulator.settings.json"); }
        }

        /// <summary>
        /// Default clientIdSecretKey for SIT and Lebanon PROD, read from defaults.local.json next to the program
        /// ({"clientSecret": "..."}). That file is git-ignored: the secret also works on production, so it never
        /// goes into the public repository. Empty when the file is missing; paste the secret in the app instead.
        /// </summary>
        public static string AreebaClientSecret
        {
            get
            {
                try
                {
                    string file = Path.Combine(Program.AppDir, "defaults.local.json");
                    if (!File.Exists(file)) return "";
                    return Json.Str(Json.TryParseObject(File.ReadAllText(file, Encoding.UTF8)), "clientSecret") ?? "";
                }
                catch
                {
                    return "";
                }
            }
        }

        /// <summary>Names of the built-in profiles already offered, so a profile the user deleted is not added back.</summary>
        public List<string> SeenDefaults { get; set; }

        public static AppSettings Defaults()
        {
            AppSettings s = new AppSettings();
            s.Profiles = new List<EnvProfile>();
            s.Profiles.Add(new EnvProfile
            {
                Name = "Local",
                Server = "http://localhost:8099",
                WsUrl = "ws://localhost:8099/websocket",
                TerminalId = "T0012345",
                Pass = "1234",
                SecretId = "bG9jYWwtY2xpZW50OmxvY2FsLXNlY3JldA==",
                Token = "",
                Currency = "LBP",
            });
            s.Profiles.Add(new EnvProfile
            {
                Name = "SIT",
                Server = "https://socket-mpos.areeba.com:5002",
                WsUrl = "wss://socket-mpos.areeba.com:5002",
                TerminalId = "LB000046",
                Pass = "",
                SecretId = AreebaClientSecret,
                Token = "",
                Currency = "LBP",
            });
            s.Profiles.Add(new EnvProfile
            {
                Name = "Lebanon PROD",
                Server = "https://socket-mpos.areeba.com:5000",
                WsUrl = "wss://socket-mpos.areeba.com:5000",
                TerminalId = "",
                Pass = "",
                SecretId = AreebaClientSecret,
                Token = "",
                Currency = "USD",
                IsProduction = true,
            });
            s.Profiles.Add(new EnvProfile
            {
                Name = "Iraq PROD",
                Server = "https://api.areeba.iq/ecr-websocket",
                WsUrl = "wss://api.areeba.iq/ecr-websocket",
                TerminalId = "IQ001100",
                Pass = "",
                SecretId = "",
                Token = "",
                Currency = "IQD",
                IsProduction = true,
            });
            s.SeenDefaults = new List<string>();
            foreach (EnvProfile p in s.Profiles) s.SeenDefaults.Add(p.Name);
            s.SelectedProfile = "Local";
            s.NextEcrRefNo = 1001;
            s.Amount = "1000";
            s.WaitingTimeSec = 60;
            s.AutoIncrementRef = true;
            s.TerminalModern = true;
            s.TerminalAutoReconnect = true;
            s.TerminalCountry = "";
            s.TerminalMode = "Approve";
            s.DeclineCode = "-1";
            s.DeclineDesc = "Declined";
            s.AnswerDelaySec = 2;
            s.BoonzAutoPreauth = true;
            s.PretendDoorOpened = false;
            s.BoonzPreauthAmount = "100";
            s.BoonzPreauthDelaySec = 1;
            s.LockNum = 1;
            s.MockMerchantPort = 8790;
            s.MockMerchantReply = "200 success";
            s.MockMerchantSecret = "";
            s.RetryEnabled = false;
            s.RetryCode = "1116";
            s.RetryStep = "1000";
            s.RetryMinAmount = "1000";
            return s;
        }

        public static AppSettings Load()
        {
            AppSettings defaults = Defaults();
            try
            {
                if (!File.Exists(FilePath)) return defaults;
                AppSettings s = new System.Web.Script.Serialization.JavaScriptSerializer()
                    .Deserialize<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (s == null || s.Profiles == null || s.Profiles.Count == 0) return defaults;
                foreach (EnvProfile p in s.Profiles)
                {
                    p.Pass = Unprotect(p.Pass);
                    p.SecretId = Unprotect(p.SecretId);
                    p.Token = Unprotect(p.Token);
                }
                s.MockMerchantSecret = Unprotect(s.MockMerchantSecret);
                AddNewDefaults(s, defaults);
                if (s.WaitingTimeSec <= 0) s.WaitingTimeSec = defaults.WaitingTimeSec;
                if (s.MockMerchantPort <= 0) s.MockMerchantPort = defaults.MockMerchantPort;
                if (s.LockNum <= 0) s.LockNum = 1;
                if (s.NextEcrRefNo <= 0) s.NextEcrRefNo = defaults.NextEcrRefNo;
                return s;
            }
            catch
            {
                return defaults;
            }
        }

        /// <summary>Adds built-in profiles that came with a newer version and fills an empty client secret.</summary>
        private static void AddNewDefaults(AppSettings s, AppSettings defaults)
        {
            if (s.SeenDefaults == null) s.SeenDefaults = new List<string>();
            foreach (EnvProfile d in defaults.Profiles)
            {
                EnvProfile existing = s.Profiles.Find(p => p.Name == d.Name);
                if (existing == null && !s.SeenDefaults.Contains(d.Name)) s.Profiles.Add(d);
                if (existing != null && string.IsNullOrEmpty(existing.SecretId) && !string.IsNullOrEmpty(d.SecretId)) existing.SecretId = d.SecretId;
                if (!s.SeenDefaults.Contains(d.Name)) s.SeenDefaults.Add(d.Name);
            }
        }

        public void Save()
        {
            AppSettings copy = (AppSettings)MemberwiseClone();
            copy.Profiles = new List<EnvProfile>();
            foreach (EnvProfile p in Profiles)
            {
                EnvProfile c = p.Copy();
                c.Pass = Protect(c.Pass);
                c.SecretId = Protect(c.SecretId);
                c.Token = Protect(c.Token);
                copy.Profiles.Add(c);
            }
            copy.MockMerchantSecret = Protect(MockMerchantSecret);
            File.WriteAllText(FilePath, Json.Pretty(Json.Serialize(copy)), Encoding.UTF8);
        }

        private const string Prefix = "dpapi:";

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return plain;
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(data);
        }

        private static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix)) return stored;
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return ""; // saved by another Windows user
            }
        }
    }
}
