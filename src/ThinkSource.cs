// ThinkSource.cs —— 思考挡位读取
//
// 数据源（全部只读，与 MiMo 的真实机制一一对应）：
//   · 全局默认挡位：preferences.json 的 "think" 字段（渲染层 Br("think", e) 保存）
//   · 当前模型：    preferences.json 的 "model" 字段
//   · 模型支持挡位：渲染层 __MIMO_EFFORTS__ 表（已提取为 efforts.txt 嵌入资源）
//   · 挡位枚举：    low / medium / high / xhigh / max / disabled / none / light / extra_high
//
// 「提前查看挡位」：给任意模型名查它支持的挡位，不用等切过去。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace MiMoSkin
{
    public static class ThinkSource
    {
        public const string Levels = "low,medium,high,xhigh,max";

        static Dictionary<string, string[]> _efforts;

        static string PrefsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Xiaomi MiMo", "preferences.json");
            }
        }

        public static string ReadCurrentThink()
        {
            try
            {
                string raw = File.ReadAllText(PrefsPath, Encoding.UTF8);
                return ExtractStringField(raw, "think");
            }
            catch { return ""; }
        }

        public static string ReadCurrentModel()
        {
            try
            {
                string raw = File.ReadAllText(PrefsPath, Encoding.UTF8);
                return ExtractStringField(raw, "model");
            }
            catch { return ""; }
        }

        // 写挡位到 preferences.json 的 "think" 字段。
        // 必须在 MiMo 关闭时调用：运行中改文件会被内存态覆盖。
        // 返回错误信息，null = 成功。
        public static string WriteThink(string level)
        {
            try
            {
                string p = PrefsPath;
                if (!File.Exists(p)) return "找不到 preferences.json";
                JObject o = Json.Parse(File.ReadAllText(p, Encoding.UTF8)) as JObject;
                if (o == null) return "preferences.json 解析失败";
                o.Set("think", new JString(level));
                // tab 缩进、无末尾换行（与 MiMo 写出的原文件逐字节一致）
                File.WriteAllText(p, Json.StringifyTabbed(o), new UTF8Encoding(false));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static string ExtractStringField(string json, string key)
        {
            // preferences.json 由 JSON.stringify 风格写出，冒号后有空格（"think": "max"），
            // 不能假设紧贴。定位键名后跳过冒号与空白再找引号。
            int i = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return "";
            int c = json.IndexOf(':', i);
            if (c < 0) return "";
            int s = c + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t')) s++;
            if (s >= json.Length || json[s] != '"') return "";
            s++;
            int e = json.IndexOf('"', s);
            return e > s ? json.Substring(s, e - s) : "";
        }

        // 挡位表。优先读运行时释放的 efforts.json（gen 重抓后是最新的），
        // 读不到再退回 exe 内嵌资源。格式为 JSON：{"model":["low","high","max"]}
        public static Dictionary<string, string[]> Efforts
        {
            get
            {
                if (_efforts != null) return _efforts;
                _efforts = new Dictionary<string, string[]>(StringComparer.Ordinal);

                string text = null;
                try
                {
                    string live = Path.Combine(ThinkPatch.WorkDir, "efforts.json");
                    if (File.Exists(live)) text = File.ReadAllText(live, Encoding.UTF8);
                }
                catch { }

                if (text == null)
                {
                    try
                    {
                        Assembly asm = Assembly.GetExecutingAssembly();
                        using (Stream s = asm.GetManifestResourceStream("MiMoSkin.efforts.json"))
                        using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                            text = r.ReadToEnd();
                    }
                    catch { }
                }

                if (text != null)
                {
                    try { ParseEfforts(text); }
                    catch { }
                }
                return _efforts;
            }
        }

        static void ParseEfforts(string json)
        {
            JObject root = Json.Parse(json) as JObject;
            if (root == null) return;
            foreach (string key in root.Keys)
            {
                JArray arr = root.Map[key] as JArray;
                if (arr == null) continue;
                string[] lv = new string[arr.Items.Count];
                for (int i = 0; i < arr.Items.Count; i++)
                {
                    JString js = arr.Items[i] as JString;
                    lv[i] = js == null ? "" : js.Value;
                }
                _efforts[key] = lv;
            }
        }

        // 查某模型的挡位。模型名可带 xiaomi/ofm/openrouter 前缀，也接受裸名（自动匹配）。
        public static string[] EffortsFor(string model)
        {
            if (string.IsNullOrEmpty(model)) return new string[0];
            Dictionary<string, string[]> t = Efforts;
            string m = model.Trim();

            if (t.ContainsKey(m)) return t[m];

            // 裸名 → 尝试补常见前缀
            string[] prefixes = new string[] { "", "ofm/", "xiaomi/", "openrouter/" };
            foreach (string p in prefixes)
            {
                string k = p + m;
                if (t.ContainsKey(k)) return t[k];
            }

            // 模糊匹配：查询词先剥 provider 前缀（xiaomi/ ofm/ openrouter/），
            // 再与表键剥掉第一段后的名字比对（z-ai/glm-5.3-flash → glm-5.3-flash）
            string lower = m.ToLowerInvariant();
            foreach (string p in new string[] { "xiaomi/", "ofm/", "openrouter/" })
                if (lower.StartsWith(p, StringComparison.Ordinal)) { lower = lower.Substring(p.Length); break; }

            foreach (KeyValuePair<string, string[]> kv in t)
            {
                string bare = kv.Key.ToLowerInvariant();
                int slash = bare.IndexOf('/');
                // 剥第一段（最多剥一层；cline/cline-free/x 这种三层键剥第一层后仍含 /，正好能区分）
                if (slash >= 0) bare = bare.Substring(slash + 1);
                if (bare == lower) return kv.Value;
            }

            // 最后再放宽：包含匹配（如输 deepseek 匹配到唯一一个就返回）
            List<string[]> contains = new List<string[]>();
            foreach (KeyValuePair<string, string[]> kv in t)
                if (kv.Key.ToLowerInvariant().Contains(lower)) contains.Add(kv.Value);
            if (contains.Count == 1) return contains[0];

            return new string[0];
        }

        // 挡位中文显示
        public static string LevelLabel(string level)
        {
            switch (level)
            {
                case "low": return "低";
                case "light": return "轻";
                case "medium": return "中";
                case "high": return "高";
                case "xhigh": return "超高";
                case "extra_high": return "超高";
                case "max": return "最大";
                case "disabled": return "关闭";
                case "none": return "无";
                default: return level;
            }
        }

        // 挡位序（用于「当前挡位在支持列表的位置」）
        static readonly string[] Order = new string[] { "none", "disabled", "light", "low", "medium", "high", "xhigh", "extra_high", "max" };

        public static int LevelOrder(string level)
        {
            for (int i = 0; i < Order.Length; i++)
                if (Order[i] == level) return i;
            return -1;
        }

        // 列出所有唯一模型（去掉 provider 前缀），供「提前查看」下拉选择
        public static List<string> AllModels()
        {
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            List<string> list = new List<string>();
            foreach (string key in Efforts.Keys)
            {
                string bare = key;
                foreach (string p in new string[] { "xiaomi/", "ofm/", "openrouter/" })
                    if (bare.StartsWith(p, StringComparison.Ordinal)) { bare = bare.Substring(p.Length); break; }
                if (!seen.ContainsKey(bare))
                {
                    seen[bare] = true;
                    list.Add(bare);
                }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }
}
