// GatewaySource.cs —— 多网关用量数据源
//
// 两类数据源，可自由添加切换：
//   1. local  —— 本地 OFM 网关的 stats.json（路径可自定义，默认 ~/.our-free-model/stats.json）
//   2. remote —— HTTP 中转站的用量接口（one-api / new-api 系，GET /api/data 自带 token）\n//   3. openrouter —— OpenRouter 官方 /activity 接口（需要 Management Key）
//
// remote 返回的格式与本地统一成 ModelDayStats，界面无感切换。
//
// one-api /api/data 响应（实测格式）：
//   { data: { quota_data: [ { timestamp, model_name, token_used, count, quota }, ... ],
//             quota: number(剩余), used_quota: number } }
//   timestamp 是「当天零点」的 unix 秒；token_used 是输出 token。
//   注意 one-api 不单独记录输入 token —— input 用 (quota/token_used) 比率折算不可靠，
//   显示时 input 标 "—"，只展示输出与调用数。这是数据源侧的客观限制。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace MiMoSkin
{
    /* ------------------------------ 数据模型 ------------------------------ */

    // 单个模型在某天的统计（本地与远程数据源统一用这个结构）
    public sealed class ModelDayStats
    {
        public string Model = "";
        public string Day = "";
        public long Input, Output, Reasoning, CacheRead;
        public long Calls, Failed;
        public long TtftMs, TtftSamples;       // TTFT 总和与样本数（本地数据源才有）
        public long DecodeMs, DecodeTokens;    // 解码时间总和与 token 数（本地才有）

        public double AvgTtft
        {
            get { return TtftSamples > 0 ? (double)TtftMs / TtftSamples : 0; }
        }

        // 生成速度：decodeTokens / decodeMs（网关用同一口径算 tok/s，见 store.js decodeWindow）
        public double SpeedTps
        {
            get { return DecodeMs > 0 ? DecodeTokens * 1000.0 / DecodeMs : 0; }
        }
    }

    /* ------------------------------ 网关配置 ------------------------------ */

    public sealed class GatewayConfig
    {
        public string Name = "本地网关";
        public string Kind = "local";        // local | remote
        public string Path = "";             // local: stats.json 路径；remote: base URL
        public string ApiKey = "";           // remote 用
        public string Username = "";         // remote（one-api 需要登录态时用 access token header）

        public GatewayConfig Clone()
        {
            GatewayConfig g = new GatewayConfig();
            g.Name = Name; g.Kind = Kind; g.Path = Path;
            g.ApiKey = ApiKey; g.Username = Username;
            return g;
        }
    }

    public static class GatewayStore
    {
        static string ConfigPath
        {
            get
            {
                return Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "mimoskin-gateways.json");
            }
        }

        public static List<GatewayConfig> Load()
        {
            List<GatewayConfig> list = new List<GatewayConfig>();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    JObject root = Json.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8)) as JObject;
                    JArray arr = root == null ? null : root.Get("gateways") as JArray;
                    if (arr != null)
                    {
                        foreach (JNode n in arr.Items)
                        {
                            JObject o = n as JObject;
                            if (o == null) continue;
                            GatewayConfig g = new GatewayConfig();
                            g.Name = Str(o, "name", g.Name);
                            g.Kind = Str(o, "kind", g.Kind);
                            g.Path = Str(o, "path", g.Path);
                            g.ApiKey = Str(o, "key", g.ApiKey);
                            g.Username = Str(o, "user", g.Username);
                            list.Add(g);
                        }
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                // 默认：本地网关
                GatewayConfig def = new GatewayConfig();
                def.Name = "本地网关";
                def.Kind = "local";
                def.Path = DefaultLocalStats;
                list.Add(def);
            }
            return list;
        }

        public static void Save(List<GatewayConfig> list)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"gateways\":[");
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    GatewayConfig g = list[i];
                    sb.Append("{");
                    sb.Append("\"name\":\"").Append(Esc(g.Name)).Append("\",");
                    sb.Append("\"kind\":\"").Append(Esc(g.Kind)).Append("\",");
                    sb.Append("\"path\":\"").Append(Esc(g.Path)).Append("\",");
                    sb.Append("\"key\":\"").Append(Esc(g.ApiKey)).Append("\",");
                    sb.Append("\"user\":\"").Append(Esc(g.Username)).Append("\"");
                    sb.Append("}");
                }
                sb.Append("]}");
                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static string Str(JObject o, string k, string def)
        {
            JString s = o.Get(k) as JString;
            return s == null ? def : s.Value;
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public static string DefaultLocalStats
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".our-free-model", "stats.json");
            }
        }
    }

    /* ------------------------------ 读取 ------------------------------ */

    public static class GatewayReader
    {
        // 统一入口：按 kind 分发，返回「日期 → 模型 → 统计」
        public static SortedDictionary<string, Dictionary<string, ModelDayStats>> Read(GatewayConfig g, out string error)
        {
            error = null;
            if (g == null) { error = "未选择网关"; return null; }
            if (g.Kind == "remote") return ReadRemote(g, out error);
            if (g.Kind == "openrouter") return ReadOpenRouter(g, out error);
            return ReadLocal(g.Path, out error);
        }

        /* -------- openrouter: /activity（Management key） --------
         * GET https://openrouter.ai/api/v1/activity?date=YYYY-MM-DD
         * 返回近 30 天逐日逐模型：prompt/completion/reasoning/cached tokens、
         * requests、usage(USD)。数据比 one-api 系完整（含输入与思考 token）。
         * 需要 Management key（普通推理 key 会 403）。 */

        public static SortedDictionary<string, Dictionary<string, ModelDayStats>> ReadOpenRouter(GatewayConfig g, out string error)
        {
            error = null;
            SortedDictionary<string, Dictionary<string, ModelDayStats>> result =
                new SortedDictionary<string, Dictionary<string, ModelDayStats>>();

            if (string.IsNullOrEmpty(g.ApiKey))
            {
                error = "需要 Management Key（OpenRouter 设置里的管理密钥，普通推理 key 权限不够）";
                return result;
            }

            // 近 30 天逐日拉取（该接口支持 date 过滤）
            string[] dates = new string[30];
            DateTime today = DateTime.UtcNow.Date;
            for (int i = 0; i < 30; i++) dates[i] = today.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            int cursor = 0;
            Exception lastEx = null;
            foreach (string day in dates)
            {
                string url = "https://openrouter.ai/api/v1/activity?date=" + day;
                string json;
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 12000;
                    req.Accept = "application/json";
                    req.Headers["Authorization"] = "Bearer " + g.ApiKey;

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        json = r.ReadToEnd();
                }
                catch (WebException we)
                {
                    string detail = we.Message;
                    HttpWebResponse resp = we.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        int code = (int)resp.StatusCode;
                        detail = "HTTP " + code + (code == 403 ? "（403：Management key 不对或权限不足）" : "");
                        try
                        {
                            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                            {
                                string b = r.ReadToEnd();
                                if (b.Length > 0) detail += " " + (b.Length > 120 ? b.Substring(0, 120) : b);
                            }
                        }
                        catch { }
                    }
                    error = detail;
                    return result;
                }
                catch (Exception ex) { lastEx = ex; break; }

                ParseOpenRouterDay(json, result);
                cursor++;
            }

            if (cursor == 0 && lastEx != null) error = lastEx.Message;
            return result;
        }

        static void ParseOpenRouterDay(string json, SortedDictionary<string, Dictionary<string, ModelDayStats>> result)
        {
            JObject root = Json.Parse(json) as JObject;
            if (root == null) return;
            JArray data = root.Get("data") as JArray;
            if (data == null) return;

            foreach (JNode n in data.Items)
            {
                JObject o = n as JObject;
                if (o == null) continue;

                string day = GetString(o, "date");
                string model = GetString(o, "model");
                if (day.Length == 0 || model.Length == 0) continue;

                Dictionary<string, ModelDayStats> models;
                if (!result.TryGetValue(day, out models))
                {
                    models = new Dictionary<string, ModelDayStats>(StringComparer.Ordinal);
                    result[day] = models;
                }

                ModelDayStats s;
                if (!models.TryGetValue(model, out s))
                {
                    s = new ModelDayStats();
                    s.Model = model;
                    s.Day = day;
                    models[model] = s;
                }

                s.Input += GetLong(o, "prompt_tokens");
                s.Output += GetLong(o, "completion_tokens");
                s.Reasoning += GetLong(o, "reasoning_tokens");
                s.CacheRead += GetLong(o, "cached_tokens");
                s.Calls += GetLong(o, "requests");
            }
        }

        // 供测试：单日解析
        public static SortedDictionary<string, Dictionary<string, ModelDayStats>> ParseOpenRouterPublic(string json)
        {
            SortedDictionary<string, Dictionary<string, ModelDayStats>> result =
                new SortedDictionary<string, Dictionary<string, ModelDayStats>>();
            ParseOpenRouterDay(json, result);
            return result;
        }

        /* -------- local: OFM stats.json -------- */

        public static SortedDictionary<string, Dictionary<string, ModelDayStats>> ReadLocal(string statsPath, out string error)
        {
            error = null;
            SortedDictionary<string, Dictionary<string, ModelDayStats>> result =
                new SortedDictionary<string, Dictionary<string, ModelDayStats>>();

            string p = string.IsNullOrEmpty(statsPath) ? GatewayStore.DefaultLocalStats : statsPath;
            if (!File.Exists(p)) { error = "stats.json 不存在：" + p; return result; }

            try
            {
                JObject root = Json.Parse(File.ReadAllText(p, Encoding.UTF8)) as JObject;
                JObject days = root == null ? null : root.Get("days") as JObject;
                if (days == null) return result;

                foreach (string day in days.Keys)
                {
                    JObject dayObj = days.Map[day] as JObject;
                    if (dayObj == null) continue;

                    Dictionary<string, ModelDayStats> models =
                        new Dictionary<string, ModelDayStats>(StringComparer.Ordinal);

                    JObject modelMap = dayObj.Get("models") as JObject;
                    if (modelMap != null)
                    {
                        foreach (string mk in modelMap.Keys)
                        {
                            JObject m = modelMap.Map[mk] as JObject;
                            if (m == null) continue;
                            ModelDayStats s = ParseLocalModel(mk, day, m);
                            models[mk] = s;
                        }
                    }
                    result[day] = models;
                }
            }
            catch (Exception ex) { error = ex.Message; }
            return result;
        }

        static ModelDayStats ParseLocalModel(string model, string day, JObject m)
        {
            ModelDayStats s = new ModelDayStats();
            s.Model = model;
            s.Day = day;
            s.Input = GetLong(m, "input");
            s.Output = GetLong(m, "output");
            s.Reasoning = GetLong(m, "reasoning");
            s.CacheRead = GetLong(m, "cacheRead");
            s.Calls = GetLong(m, "calls");
            s.Failed = GetLong(m, "failed");
            s.TtftMs = GetLong(m, "ttftMs");
            s.TtftSamples = GetLong(m, "ttftSamples");
            s.DecodeMs = GetLong(m, "decodeMs");
            s.DecodeTokens = GetLong(m, "decodeTokens");
            return s;
        }

        static long GetLong(JObject o, string key)
        {
            JNumber n = o.Get(key) as JNumber;
            return n == null ? 0 : n.ToLong();
        }

        /* -------- remote: one-api / new-api 系 -------- */

        public static SortedDictionary<string, Dictionary<string, ModelDayStats>> ReadRemote(GatewayConfig g, out string error)
        {
            error = null;
            SortedDictionary<string, Dictionary<string, ModelDayStats>> result =
                new SortedDictionary<string, Dictionary<string, ModelDayStats>>();

            if (string.IsNullOrEmpty(g.Path)) { error = "未填写网关地址"; return result; }

            string url = g.Path.Trim().TrimEnd('/') + "/api/data";
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 15000;
                req.Accept = "application/json";
                if (!string.IsNullOrEmpty(g.ApiKey))
                    req.Headers["Authorization"] = "Bearer " + g.ApiKey;
                if (!string.IsNullOrEmpty(g.Username))
                    req.Headers["New-Api-User"] = g.Username;   // new-api 需要用户 id

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = r.ReadToEnd();
                    return ParseRemote(json, out error);
                }
            }
            catch (WebException we)
            {
                error = "请求失败：" + we.Message;
                try
                {
                    if (we.Response != null)
                    {
                        using (StreamReader r = new StreamReader(we.Response.GetResponseStream(), Encoding.UTF8))
                        {
                            string b = r.ReadToEnd();
                            if (b.Length > 0) error += " — " + (b.Length > 160 ? b.Substring(0, 160) + "…" : b);
                        }
                    }
                }
                catch { }
                return result;
            }
            catch (Exception ex) { error = ex.Message; return result; }
        }

        internal static SortedDictionary<string, Dictionary<string, ModelDayStats>> ParseRemote(string json, out string error)
        {
            error = null;
            SortedDictionary<string, Dictionary<string, ModelDayStats>> result =
                new SortedDictionary<string, Dictionary<string, ModelDayStats>>();

            JObject root;
            try { root = Json.Parse(json) as JObject; }
            catch (Exception ex) { error = "响应不是 JSON: " + ex.Message; return result; }
            if (root == null) { error = "响应为空"; return result; }

            // 成功判定：new-api 返回 {"success":true,...}；失败 {"message":"无权进行此操作..."}
            JBool ok = root.Get("success") as JBool;
            if (ok != null && !ok.Value)
            {
                JString msg = root.Get("message") as JString;
                error = "网关拒绝: " + (msg == null ? "未知错误" : msg.Value);
                return result;
            }

            JObject data = root.Get("data") as JObject;
            if (data == null) { error = "响应里没有 data 字段"; return result; }

            JArray quotaData = data.Get("quota_data") as JArray;
            if (quotaData == null || quotaData.Items.Count == 0)
            {
                // 有 data 但没有明细 → 可能是老版 one-api；仍尝试读总量
                error = "响应里没有 quota_data 明细";
                return result;
            }

            foreach (JNode n in quotaData.Items)
            {
                JObject o = n as JObject;
                if (o == null) continue;

                long ts = GetLong(o, "timestamp");
                string model = GetString(o, "model_name");
                if (model.Length == 0 || ts <= 0) continue;

                // timestamp 是当天零点（unix 秒）→ 日期
                string day = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.ToString("yyyy-MM-dd");

                Dictionary<string, ModelDayStats> models;
                if (!result.TryGetValue(day, out models))
                {
                    models = new Dictionary<string, ModelDayStats>(StringComparer.Ordinal);
                    result[day] = models;
                }

                ModelDayStats s;
                if (!models.TryGetValue(model, out s))
                {
                    s = new ModelDayStats();
                    s.Model = model;
                    s.Day = day;
                    models[model] = s;
                }

                s.Output += GetLong(o, "token_used");
                s.Calls += GetLong(o, "count");
                // quota 是计费额度不是 token，无法反推 input —— 留 0，界面显示 "—"
            }

            return result;
        }

        static string GetString(JObject o, string key)
        {
            JString s = o.Get(key) as JString;
            return s == null ? "" : s.Value;
        }
    }
}
