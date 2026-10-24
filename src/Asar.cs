// Asar.cs —— asar 归档读写引擎
//
// asar 头部布局：[u32 外层载荷=4][u32 内层总长][u32 内层载荷][u32 JSON长度][JSON][填充][数据...]
// 填充把 JSON 补齐到 4 字节边界，数据段起点 = 16 + JSON长度 + 填充长度。
// 漏算填充会整体错位，表现为「哈希全不符」。
//
// 这里自己实现 JSON，不用 JavaScriptSerializer：需要保证键顺序不变、数字原样保留
// （offset 是字符串、size 是数字、blockSize 是数字），序列化后才能与原始字节尽量一致。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MiMoSkin
{
    /* ============================== 极简 JSON ============================== */

    public abstract class JNode { }

    public sealed class JObject : JNode
    {
        public readonly List<string> Keys = new List<string>();
        public readonly Dictionary<string, JNode> Map = new Dictionary<string, JNode>(StringComparer.Ordinal);

        public JNode Get(string key)
        {
            JNode v;
            return Map.TryGetValue(key, out v) ? v : null;
        }

        public void Set(string key, JNode value)
        {
            if (!Map.ContainsKey(key)) Keys.Add(key);
            Map[key] = value;
        }

        public bool Remove(string key)
        {
            if (!Map.ContainsKey(key)) return false;
            Map.Remove(key);
            Keys.Remove(key);
            return true;
        }

        public JObject Child(string key)
        {
            JNode n = Get(key);
            JObject o = n as JObject;
            if (o == null) { o = new JObject(); Set(key, o); }
            return o;
        }
    }

    public sealed class JArray : JNode
    {
        public readonly List<JNode> Items = new List<JNode>();
    }

    public sealed class JString : JNode
    {
        public string Value;
        public JString(string v) { Value = v; }
    }

    // 数字保留原始文本：asar 里 offset 是十进制字符串、size 是整数，
    // 转成 double 再回写会变成 88758276.0 之类，破坏格式。
    public sealed class JNumber : JNode
    {
        public string Raw;
        public JNumber(string raw) { Raw = raw; }
        public static JNumber FromLong(long v) { return new JNumber(v.ToString(CultureInfo.InvariantCulture)); }
        public long ToLong() { return long.Parse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture); }
    }

    public sealed class JBool : JNode
    {
        public bool Value;
        public JBool(bool v) { Value = v; }
    }

    public sealed class JNull : JNode { }

    public static class Json
    {
        public static JNode Parse(string s)
        {
            int i = 0;
            JNode n = ParseValue(s, ref i);
            SkipWs(s, ref i);
            return n;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }

        static JNode ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return new JString(ParseString(s, ref i));
            if (c == 't') { Expect(s, ref i, "true"); return new JBool(true); }
            if (c == 'f') { Expect(s, ref i, "false"); return new JBool(false); }
            if (c == 'n') { Expect(s, ref i, "null"); return new JNull(); }
            return ParseNumber(s, ref i);
        }

        static void Expect(string s, ref int i, string lit)
        {
            if (i + lit.Length > s.Length || string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0)
                throw new FormatException("JSON 字面量不匹配 @" + i);
            i += lit.Length;
        }

        static JObject ParseObject(string s, ref int i)
        {
            JObject o = new JObject();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 对象键必须是字符串 @" + i);
                string k = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 缺少冒号 @" + i);
                i++;
                JNode v = ParseValue(s, ref i);
                o.Set(k, v);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("JSON 对象内出现意外字符 @" + i);
            }
            return o;
        }

        static JArray ParseArray(string s, ref int i)
        {
            JArray a = new JArray();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Items.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("JSON 数组内出现意外字符 @" + i);
            }
            return a;
        }

        static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // "
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字符串未闭合");
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("JSON 转义未完成");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("JSON 未知转义 \\" + e);
                }
            }
            return sb.ToString();
        }

        static JNumber ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') { i++; continue; }
                break;
            }
            if (i == start) throw new FormatException("JSON 数字为空 @" + start);
            return new JNumber(s.Substring(start, i - start));
        }

        /* ------------------------------ 序列化 ------------------------------ */

        public static string Stringify(JNode n)
        {
            StringBuilder sb = new StringBuilder(1 << 20);
            Write(sb, n);
            return sb.ToString();
        }

        // tab 缩进序列化：MiMo 的 preferences.json / window-state.json 都是 tab 缩进。
        // 用紧凑格式写回虽也能被解析，但下次 MiMo 覆写时 diff 会整个翻掉。
        public static string StringifyTabbed(JNode n)
        {
            StringBuilder sb = new StringBuilder(1 << 16);
            WriteTabbed(sb, n, 0);
            return sb.ToString();
        }

        static void WriteTabbed(StringBuilder sb, JNode n, int depth)
        {
            string tab = new string('\t', depth);
            string tab2 = new string('\t', depth + 1);

            if (n == null) { sb.Append("null"); return; }

            JObject o = n as JObject;
            if (o != null)
            {
                if (o.Keys.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                for (int k = 0; k < o.Keys.Count; k++)
                {
                    if (k > 0) sb.Append(",\n");
                    sb.Append(tab2);
                    WriteString(sb, o.Keys[k]);
                    sb.Append(": ");
                    WriteTabbed(sb, o.Map[o.Keys[k]], depth + 1);
                }
                sb.Append('\n').Append(tab).Append('}');
                return;
            }

            JArray a = n as JArray;
            if (a != null)
            {
                if (a.Items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int k = 0; k < a.Items.Count; k++)
                {
                    if (k > 0) sb.Append(",\n");
                    sb.Append(tab2);
                    WriteTabbed(sb, a.Items[k], depth + 1);
                }
                sb.Append('\n').Append(tab).Append(']');
                return;
            }

            Write(sb, n);
        }

        static void Write(StringBuilder sb, JNode n)
        {
            if (n == null) { sb.Append("null"); return; }

            JObject o = n as JObject;
            if (o != null)
            {
                sb.Append('{');
                for (int k = 0; k < o.Keys.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    WriteString(sb, o.Keys[k]);
                    sb.Append(':');
                    Write(sb, o.Map[o.Keys[k]]);
                }
                sb.Append('}');
                return;
            }

            JArray a = n as JArray;
            if (a != null)
            {
                sb.Append('[');
                for (int k = 0; k < a.Items.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    Write(sb, a.Items[k]);
                }
                sb.Append(']');
                return;
            }

            JString js = n as JString;
            if (js != null) { WriteString(sb, js.Value); return; }

            JNumber jn = n as JNumber;
            if (jn != null) { sb.Append(jn.Raw); return; }

            JBool jb = n as JBool;
            if (jb != null) { sb.Append(jb.Value ? "true" : "false"); return; }

            sb.Append("null");
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /* ============================== asar 归档 ============================== */

    public sealed class AsarFile
    {
        public string Path;
        public JObject Node;
        public long Offset;
        public long Size;
        public bool Unpacked;
    }

    public sealed class AsarArchive : IDisposable
    {
        public const int BlockSize = 4194304;

        public string FilePath;
        public JObject Root;
        public long JsonLength;
        public long DataStart;
        public long FileSize;

        FileStream _fs;

        public static AsarArchive Open(string path)
        {
            AsarArchive a = new AsarArchive();
            a.FilePath = path;
            a._fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[] h = new byte[16];
            a._fs.Position = 0;
            a._fs.Read(h, 0, 16);
            uint jsonLen = BitConverter.ToUInt32(h, 12);
            if (jsonLen == 0 || jsonLen > a._fs.Length)
                throw new InvalidDataException("asar 头部长度异常：" + jsonLen);

            byte[] jb = new byte[jsonLen];
            a._fs.Read(jb, 0, (int)jsonLen);

            string txt = Encoding.UTF8.GetString(jb);
            JNode n = Json.Parse(txt);
            a.Root = n as JObject;
            if (a.Root == null) throw new InvalidDataException("asar 头部不是 JSON 对象");

            a.JsonLength = jsonLen;
            a.DataStart = 16 + jsonLen + Pad4(jsonLen);
            a.FileSize = a._fs.Length;
            return a;
        }

        public static int Pad4(long n)
        {
            return (int)((4 - (n % 4)) % 4);
        }

        // 按 offset 排序收集所有「打包在数据段里」的文件。unpacked 条目没有 offset，跳过。
        public List<AsarFile> CollectPacked()
        {
            List<AsarFile> list = new List<AsarFile>();
            Walk(Root, "", list);
            list.Sort(delegate(AsarFile a, AsarFile b) { return a.Offset.CompareTo(b.Offset); });
            return list;
        }

        static void Walk(JObject node, string prefix, List<AsarFile> outList)
        {
            JObject files = node.Get("files") as JObject;
            if (files == null) return;
            for (int i = 0; i < files.Keys.Count; i++)
            {
                string name = files.Keys[i];
                JObject f = files.Map[name] as JObject;
                if (f == null) continue;
                string q = prefix + "/" + name;
                if (f.Get("files") is JObject) { Walk(f, q, outList); continue; }
                long off;
                if (!TryLong(f.Get("offset"), out off)) continue;   // 没有 offset = unpacked
                AsarFile af = new AsarFile();
                af.Path = q;
                af.Node = f;
                af.Offset = off;
                af.Size = ToLong(f.Get("size"));
                af.Unpacked = false;
                outList.Add(af);
            }
        }

        // offset / size 在 asar 里可能是数字也可能是字符串（不同打包器写法不同），
        // 两种都要认，否则整包条目会被当成 unpacked 全部跳过。
        public static bool TryLong(JNode n, out long value)
        {
            value = 0;
            JNumber num = n as JNumber;
            if (num != null) return long.TryParse(num.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            JString s = n as JString;
            if (s != null) return long.TryParse(s.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            return false;
        }

        public static long ToLong(JNode n)
        {
            long v;
            return TryLong(n, out v) ? v : 0;
        }

        public JObject Find(string path)
        {
            string[] parts = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            JObject node = Root;
            for (int i = 0; i < parts.Length; i++)
            {
                JObject files = node.Get("files") as JObject;
                if (files == null) return null;
                JObject nx = files.Get(parts[i]) as JObject;
                if (nx == null) return null;
                node = nx;
            }
            return node;
        }

        public JObject EnsureDir(string path)
        {
            string[] parts = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            JObject node = Root;
            for (int i = 0; i < parts.Length; i++)
            {
                JObject files = node.Get("files") as JObject;
                if (files == null) { files = new JObject(); node.Set("files", files); }
                JObject nx = files.Get(parts[i]) as JObject;
                if (nx == null) { nx = new JObject(); files.Set(parts[i], nx); }
                node = nx;
            }
            return node;
        }

        public byte[] ReadFile(AsarFile f)
        {
            byte[] buf = new byte[f.Size];
            if (f.Size == 0) return buf;
            _fs.Position = DataStart + f.Offset;
            int got = 0;
            while (got < buf.Length)
            {
                int n = _fs.Read(buf, got, buf.Length - got);
                if (n <= 0) throw new EndOfStreamException("读取 " + f.Path + " 时数据段提前结束");
                got += n;
            }
            return buf;
        }

        public void Dispose()
        {
            if (_fs != null) { _fs.Dispose(); _fs = null; }
        }
    }

    /* ============================== 打包写出 ============================== */

    public static class AsarWriter
    {
        // 单块文件的 hash 与 blocks[0] 相同；多块文件 hash 是整文件摘要，blocks 是每 4MB 一块。
        public static JObject IntegrityFor(byte[] buf)
        {
            JObject o = new JObject();
            o.Set("algorithm", new JString("SHA256"));

            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                o.Set("hash", new JString(Hex(sha.ComputeHash(buf))));
            }

            JArray blocks = new JArray();
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                if (buf.Length == 0)
                {
                    blocks.Items.Add(new JString(Hex(sha.ComputeHash(new byte[0]))));
                }
                else
                {
                    for (int i = 0; i < buf.Length; i += AsarArchive.BlockSize)
                    {
                        int len = Math.Min(AsarArchive.BlockSize, buf.Length - i);
                        byte[] chunk = new byte[len];
                        Buffer.BlockCopy(buf, i, chunk, 0, len);
                        blocks.Items.Add(new JString(Hex(sha.ComputeHash(chunk))));
                    }
                }
            }

            o.Set("blockSize", JNumber.FromLong(AsarArchive.BlockSize));
            o.Set("blocks", blocks);
            return o;
        }

        public static string Hex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static void Write(string dstPath, JObject root, List<byte[]> chunks)
        {
            string json = Json.Stringify(root);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int pad = AsarArchive.Pad4(jsonBytes.Length);

            long innerLen = jsonBytes.Length + pad + 8;
            byte[] h = new byte[16];
            Array.Copy(BitConverter.GetBytes((uint)4), 0, h, 0, 4);
            Array.Copy(BitConverter.GetBytes((uint)innerLen), 0, h, 4, 4);
            Array.Copy(BitConverter.GetBytes((uint)(jsonBytes.Length + pad + 4)), 0, h, 8, 4);
            Array.Copy(BitConverter.GetBytes((uint)jsonBytes.Length), 0, h, 12, 4);

            using (FileStream fs = new FileStream(dstPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                fs.Write(h, 0, 16);
                fs.Write(jsonBytes, 0, jsonBytes.Length);
                if (pad > 0) fs.Write(new byte[pad], 0, pad);
                for (int i = 0; i < chunks.Count; i++)
                {
                    if (chunks[i] == null || chunks[i].Length == 0) continue;
                    fs.Write(chunks[i], 0, chunks[i].Length);
                }
                fs.Flush(true);
            }
        }
    }
}
