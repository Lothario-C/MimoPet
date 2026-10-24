// BalanceSource.cs —— 余额查询核心
//
// 全部自定义：用户填 URL + Key（+ 可选取值路径），程序发 HTTP 请求、解析 JSON、
// 提取余额。没填路径时自动探测常见字段名（balance / remaining / quota / credits…）。
//
// 取值路径语法：data.balance、info.items[0].total_balance、usage.remaining
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace MiMoSkin
{
    public sealed class BalanceAccount
    {
        public string Name = "新账号";
        public string Url = "";
        public string Method = "GET";        // GET | POST
        public string ApiKey = "";
        public string AuthHeader = "Authorization";
        public string AuthPrefix = "Bearer ";
        public string Body = "";             // POST 时的请求体（JSON），可空
        public string Path = "";             // 取值路径，空 = 自动探测
        public string Label = "";            // 显示名，如「余额」
        public string Currency = "";         // 前缀，如 ¥ $
        public double Low = -1;              // 低额阈值，<0 = 不提醒
        public string Note = "";

        // 运行时结果（不持久化）
        public string LastValue = "";
        public string LastError = "";
        public long LastTime;                // ticks
        public bool HasResult;
    }

    public static class BalanceSource
    {
        /* ------------------------------ 持久化 ------------------------------ */

        public sealed class Store
        {
            public int IntervalMin = 10;
            public List<BalanceAccount> Accounts = new List<BalanceAccount>();
        }

        static string ConfigPath
        {
            get
            {
                return Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "mimoskin-balance.json");
            }
        }

        public static Store Load()
        {
            Store s = new Store();
            try
            {
                if (!File.Exists(ConfigPath)) return s;
                JObject root = Json.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8)) as JObject;
                if (root == null) return s;

                JNumber iv = root.Get("interval") as JNumber;
                if (iv != null) s.IntervalMin = (int)iv.ToLong();

                JArray arr = root.Get("accounts") as JArray;
                if (arr != null)
                {
                    foreach (JNode n in arr.Items)
                    {
                        JObject o = n as JObject;
                        if (o == null) continue;
                        BalanceAccount a = new BalanceAccount();
                        a.Name = Str(o, "name", a.Name);
                        a.Url = Str(o, "url", a.Url);
                        a.Method = Str(o, "method", a.Method);
                        a.ApiKey = Str(o, "key", a.ApiKey);
                        a.AuthHeader = Str(o, "header", a.AuthHeader);
                        a.AuthPrefix = Str(o, "prefix", a.AuthPrefix);
                        a.Body = Str(o, "body", a.Body);
                        a.Path = Str(o, "path", a.Path);
                        a.Label = Str(o, "label", a.Label);
                        a.Currency = Str(o, "currency", a.Currency);
                        JNumber low = o.Get("low") as JNumber;
                        if (low != null) { try { a.Low = double.Parse(low.Raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture); } catch { } }
                        a.Note = Str(o, "note", a.Note);
                        s.Accounts.Add(a);
                    }
                }
            }
            catch { }
            return s;
        }

        static string Str(JObject o, string k, string def)
        {
            JString s = o.Get(k) as JString;
            return s == null ? def : s.Value;
        }

        public static void Save(Store s)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"interval\":").Append(s.IntervalMin).Append(",\"accounts\":[");
                for (int i = 0; i < s.Accounts.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    BalanceAccount a = s.Accounts[i];
                    sb.Append("{");
                    sb.Append("\"name\":\"").Append(E(a.Name)).Append("\",");
                    sb.Append("\"url\":\"").Append(E(a.Url)).Append("\",");
                    sb.Append("\"method\":\"").Append(E(a.Method)).Append("\",");
                    sb.Append("\"key\":\"").Append(E(a.ApiKey)).Append("\",");
                    sb.Append("\"header\":\"").Append(E(a.AuthHeader)).Append("\",");
                    sb.Append("\"prefix\":\"").Append(E(a.AuthPrefix)).Append("\",");
                    sb.Append("\"body\":\"").Append(E(a.Body)).Append("\",");
                    sb.Append("\"path\":\"").Append(E(a.Path)).Append("\",");
                    sb.Append("\"label\":\"").Append(E(a.Label)).Append("\",");
                    sb.Append("\"currency\":\"").Append(E(a.Currency)).Append("\",");
                    sb.Append("\"low\":").Append(a.Low.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"note\":\"").Append(E(a.Note)).Append("\"");
                    sb.Append("}");
                }
                sb.Append("]}");
                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static string E(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r");
        }

        /* ------------------------------ HTTP ------------------------------ */

        public static string Fetch(BalanceAccount a, out string error)
        {
            error = null;
            try
            {
                if (a.Url.Trim().Length == 0) { error = "未填写 URL"; return null; }

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(a.Url.Trim());
                req.Method = a.Method == "POST" ? "POST" : "GET";
                req.Timeout = 15000;
                req.UserAgent = "MiMoSkin/1.3";
                req.Accept = "application/json";

                if (a.ApiKey.Trim().Length > 0)
                {
                    string header = a.AuthHeader.Trim().Length > 0 ? a.AuthHeader.Trim() : "Authorization";
                    if (header.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                        req.Headers[header] = a.AuthPrefix + a.ApiKey.Trim();
                    else
                        req.Headers[header] = a.ApiKey.Trim();
                }

                if (req.Method == "POST")
                {
                    byte[] body = Encoding.UTF8.GetBytes(a.Body.Length > 0 ? a.Body : "{}");
                    req.ContentType = "application/json";
                    req.ContentLength = body.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                }

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
            }
            catch (WebException we)
            {
                string detail = we.Message;
                try
                {
                    if (we.Response != null)
                    {
                        using (StreamReader r = new StreamReader(we.Response.GetResponseStream(), Encoding.UTF8))
                        {
                            string b = r.ReadToEnd();
                            if (b.Length > 0) detail += " — " + (b.Length > 200 ? b.Substring(0, 200) + "…" : b);
                        }
                    }
                }
                catch { }
                error = detail;
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /* ------------------------------ 提取 ------------------------------ */

        // 按路径取值：data.balance / info.items[0].total
        public static string ExtractByPath(string json, string path, out string error)
        {
            error = null;
            JNode root;
            try { root = Json.Parse(json); }
            catch (Exception ex) { error = "响应不是合法 JSON：" + ex.Message; return null; }

            JNode cur = root;
            string[] parts = path.Split('.');
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;

                int idx = -1;
                string key = p;
                int br = p.IndexOf('[');
                if (br >= 0)
                {
                    key = p.Substring(0, br);
                    int close = p.IndexOf(']', br);
                    if (close > br) int.TryParse(p.Substring(br + 1, close - br - 1), out idx);
                }

                if (key.Length > 0)
                {
                    JObject o = cur as JObject;
                    if (o == null) { error = "路径在 \"" + key + "\" 处不是对象"; return null; }
                    cur = o.Get(key);
                    if (cur == null) { error = "找不到字段 \"" + key + "\""; return null; }
                }

                if (idx >= 0)
                {
                    JArray arr = cur as JArray;
                    if (arr == null) { error = "路径在 \"" + p + "\" 处不是数组"; return null; }
                    if (idx >= arr.Items.Count) { error = "数组下标越界：" + p; return null; }
                    cur = arr.Items[idx];
                }
            }

            return NodeToValue(cur, out error);
        }

        static string NodeToValue(JNode n, out string error)
        {
            error = null;
            if (n is JNumber) return ((JNumber)n).Raw;
            if (n is JString) return ((JString)n).Value;
            if (n is JBool) return ((JBool)n).Value ? "true" : "false";
            error = "路径指向的不是数值/字符串";
            return null;
        }

        // 自动探测：递归找名字含余额关键词的数值字段
        static readonly string[] Priority = {
            "total_balance", "available_balance", "remaining_balance", "balance",
            "remaining_quota", "remaining", "total_quota", "quota", "credits",
            "credit", "balance_cny", "amount"
        };

        public static string AutoDetect(string json, out string error)
        {
            error = null;
            JNode root;
            try { root = Json.Parse(json); }
            catch (Exception ex) { error = "响应不是合法 JSON：" + ex.Message; return null; }

            foreach (string kw in Priority)
            {
                List<string> hits = new List<string>();
                List<string> vals = new List<string>();
                Scan(root, "", kw, hits, vals, 0);
                if (hits.Count > 0) return hits[0] + "=" + vals[0];
            }

            error = "没找到余额字段。请手动填写取值路径（如 data.balance），或检查响应内容。";
            return null;
        }

        static void Scan(JNode n, string path, string kw, List<string> hits, List<string> vals, int depth)
        {
            if (depth > 6) return;

            JObject o = n as JObject;
            if (o != null)
            {
                foreach (string key in o.Keys)
                {
                    if (key.ToLowerInvariant().Contains(kw))
                    {
                        JNode v = o.Get(key);
                        string val = ValueText(v);
                        if (val != null)
                        {
                            hits.Add(path.Length == 0 ? key : path + "." + key);
                            vals.Add(val);
                        }
                    }
                    Scan(o.Get(key), path.Length == 0 ? key : path + "." + key, kw, hits, vals, depth + 1);
                }
                return;
            }

            JArray arr = n as JArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Items.Count && i < 20; i++)
                    Scan(arr.Items[i], path + "[" + i + "]", kw, hits, vals, depth + 1);
            }
        }

        // 只要数值或「看起来是数字」的字符串
        static string ValueText(JNode v)
        {
            if (v is JNumber) return ((JNumber)v).Raw;
            if (v is JString)
            {
                string s = ((JString)v).Value.Trim();
                double d;
                if (s.Length > 0 && s.Length < 24 &&
                    double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out d))
                    return s;
            }
            return null;
        }

        /* ------------------------------ 查询一个账号 ------------------------------ */

        public sealed class QueryResult
        {
            public string Value;
            public string PathUsed;
            public string Error;
            public bool Ok { get { return Error == null; } }
        }

        public static QueryResult Query(BalanceAccount a)
        {
            QueryResult r = new QueryResult();

            string json = Fetch(a, out r.Error);
            if (json == null) return r;

            string value, pathUsed;
            if (a.Path.Trim().Length > 0)
            {
                value = ExtractByPath(json, a.Path.Trim(), out r.Error);
                if (value == null) return r;
                pathUsed = a.Path.Trim();
            }
            else
            {
                // 自动探测返回 "路径=值"
                string det = AutoDetect(json, out r.Error);
                if (det == null) return r;
                int eq = det.IndexOf('=');
                pathUsed = det.Substring(0, eq);
                value = det.Substring(eq + 1);
            }

            r.Value = value;
            r.PathUsed = pathUsed;
            return r;
        }
    }
}
