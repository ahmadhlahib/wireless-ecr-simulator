// Wireless ECR Simulator - by Ahmad El Lahib
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace WirelessEcrSim
{
    /// <summary>Small JSON helper on top of JavaScriptSerializer (ships with .NET Framework).</summary>
    public static class Json
    {
        private static JavaScriptSerializer NewSerializer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        public static string Serialize(object value)
        {
            return NewSerializer().Serialize(value);
        }

        /// <summary>Parses JSON; returns null when the text is not JSON.</summary>
        public static object TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return NewSerializer().DeserializeObject(text); }
            catch { return null; }
        }

        public static Dictionary<string, object> TryParseObject(string text)
        {
            return TryParse(text) as Dictionary<string, object>;
        }

        public static string Str(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            object v;
            if (!obj.TryGetValue(key, out v) || v == null) return null;
            if (v is string) return (string)v;
            if (v is IDictionary || (v is IEnumerable && !(v is string))) return Serialize(v);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>Indents JSON for display; returns the text unchanged when it is not JSON.</summary>
        public static string Pretty(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? "";
            object parsed = TryParse(text);
            if (parsed == null) return text;
            StringBuilder sb = new StringBuilder();
            Write(sb, parsed, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v, int indent)
        {
            IDictionary<string, object> dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    sb.Append(' ', (indent + 1) * 2).Append(Quote(kv.Key)).Append(": ");
                    Write(sb, kv.Value, indent + 1);
                    if (++i < dict.Count) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(' ', indent * 2).Append('}');
                return;
            }
            if (v is string) { sb.Append(Quote((string)v)); return; }
            IEnumerable list = v as IEnumerable;
            if (list != null)
            {
                List<object> items = new List<object>();
                foreach (object o in list) items.Add(o);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(' ', (indent + 1) * 2);
                    Write(sb, items[i], indent + 1);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(' ', indent * 2).Append(']');
                return;
            }
            sb.Append(NewSerializer().Serialize(v));
        }

        private static string Quote(string s)
        {
            return NewSerializer().Serialize(s);
        }

        /// <summary>Hides passwords, secrets and tokens before a payload is shown or logged.</summary>
        public static string Mask(string json)
        {
            Dictionary<string, object> obj = TryParseObject(json);
            if (obj == null) return json;
            MaskInPlace(obj);
            return Serialize(obj);
        }

        private static void MaskInPlace(Dictionary<string, object> obj)
        {
            List<string> keys = new List<string>(obj.Keys);
            foreach (string k in keys)
            {
                string lower = k.ToLowerInvariant();
                if (lower == "pass" || lower == "password" || lower.Contains("secret") || lower == "authorization")
                {
                    string s = Convert.ToString(obj[k]);
                    obj[k] = string.IsNullOrEmpty(s) ? s : "****";
                }
                else if (obj[k] is Dictionary<string, object>)
                {
                    MaskInPlace((Dictionary<string, object>)obj[k]);
                }
            }
        }
    }
}
