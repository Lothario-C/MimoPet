// SkinPatcher.cs —— 皮肤注入 / 还原 / 校验
//
// 只改三处：往 asar 里加 2 个文件（皮肤 CSS + 壁纸），并在 index.html 的 </head> 前插一行 <link>。
// 其余 1900+ 个文件（含 113 个 unpacked、13 个可执行标记）原样保留。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MiMoSkin
{
    public sealed class PatchResult
    {
        public int PackedCount;
        public long DataBytes;
        public bool SizeMatches;
        public int HashFailures;
        public List<string> Messages = new List<string>();
    }

    public static class SkinPatcher
    {
        public const string AssetsDir = "/out/renderer/assets";
        public const string IndexHtml = "/out/renderer/index.html";
        public const string CssName = "mimo-dream-skin.css";
        public const string WallName = "mimo-wallpaper.jpg";
        public const string Mark = "data-mimo-dream-skin";

        public static readonly string[] NewFileNames = new string[] { CssName, WallName };

        public static string BackupOf(string asarPath) { return asarPath + ".mimo-skin-backup"; }

        /* ------------------------------ 校验 ------------------------------ */

        public static PatchResult Verify(string asarPath, Action<string> log)
        {
            PatchResult r = new PatchResult();
            using (AsarArchive a = AsarArchive.Open(asarPath))
            {
                List<AsarFile> packed = a.CollectPacked();
                long bytes = 0;
                int bad = 0;

                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                {
                    for (int i = 0; i < packed.Count; i++)
                    {
                        AsarFile f = packed[i];
                        byte[] buf = a.ReadFile(f);
                        bytes += buf.Length;

                        JObject integ = f.Node.Get("integrity") as JObject;
                        if (integ == null) continue;

                        JString expectHash = integ.Get("hash") as JString;
                        if (expectHash != null)
                        {
                            string got = AsarWriter.Hex(sha.ComputeHash(buf));
                            if (!string.Equals(got, expectHash.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                if (bad < 10 && log != null) log("  ✗ 整文件哈希不符: " + f.Path);
                                bad++;
                                continue;
                            }
                        }

                        JArray blocks = integ.Get("blocks") as JArray;
                        if (blocks == null) continue;
                        JNumber bs = integ.Get("blockSize") as JNumber;
                        int blockSize = bs == null ? AsarArchive.BlockSize : (int)bs.ToLong();
                        if (blockSize <= 0) blockSize = AsarArchive.BlockSize;

                        bool broke = false;
                        for (int k = 0, off = 0; off < buf.Length && k < blocks.Items.Count; off += blockSize, k++)
                        {
                            int len = Math.Min(blockSize, buf.Length - off);
                            byte[] chunk = new byte[len];
                            Buffer.BlockCopy(buf, off, chunk, 0, len);
                            string got = AsarWriter.Hex(sha.ComputeHash(chunk));
                            JString eb = blocks.Items[k] as JString;
                            if (eb != null && !string.Equals(got, eb.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                if (bad < 10 && log != null) log("  ✗ 分块哈希不符: " + f.Path + " block#" + k);
                                bad++;
                                broke = true;
                                break;
                            }
                        }
                        if (broke) continue;
                    }
                }

                long expectEnd = a.DataStart + bytes;
                r.PackedCount = packed.Count;
                r.DataBytes = bytes;
                r.SizeMatches = (expectEnd == a.FileSize);
                r.HashFailures = bad;

                if (log != null)
                {
                    log("  打包文件 " + packed.Count + " 个，数据 " + bytes + " 字节");
                    log("  数据段结束于 " + expectEnd + "，文件实际大小 " + a.FileSize + (r.SizeMatches ? " ✓" : " ✗ 不匹配"));
                    log("  SHA256 校验失败 " + bad + " 个" + (bad == 0 ? " ✓" : ""));
                }
            }
            return r;
        }

        public static bool IsPatched(string asarPath)
        {
            using (AsarArchive a = AsarArchive.Open(asarPath))
            {
                for (int i = 0; i < NewFileNames.Length; i++)
                {
                    if (a.Find(AssetsDir + "/" + NewFileNames[i]) != null) return true;
                }
                return false;
            }
        }

        /* ------------------------------ 打补丁 ------------------------------ */

        public sealed class PatchInput
        {
            public string AsarPath;
            public byte[] Css;
            public byte[] Wallpaper;
            public Action<string> Log;

            // 额外替换/新增的文件（asar 内路径 → 内容）。
            // 思考桥用：/launch.cjs、/out/renderer/assets/index-*.js。
            // 路径同时登记进替换集，防止旧文件残留。
            public Dictionary<string, byte[]> ExtraFiles;
        }

        public static void Patch(PatchInput inp)
        {
            Action<string> log = inp.Log ?? delegate { };
            string src = inp.AsarPath;
            string bk = BackupOf(src);

            // 备份必须是「未打补丁的原始包」。
            // 应用更新会整包替换 app.asar，有时连备份一起覆盖成新版，
            // 所以不能只看「备份是否存在」。规则：
            //   当前包无皮肤文件 → 当前包就是原始包 → 用它刷新备份（永远正确）
            //   当前包有皮肤文件 → 已打过补丁 → 保留现有备份
            //   已打过补丁且无备份   → 拒绝操作。此时再打一次会把「已注入的原始内容」
            //                          当原始包备份下来，等于永久失去还原能力。
            bool skinInSrc = IsPatched(src);
            if (!skinInSrc)
            {
                File.Copy(src, bk, true);
                log("✓ 当前包为原始状态，已备份 → " + Path.GetFileName(bk));
            }
            else if (!File.Exists(bk))
            {
                throw new InvalidOperationException(
                    "当前 app.asar 已打过皮肤补丁，但找不到原始备份，继续操作会永久失去还原能力。\n" +
                    "请先重装或更新一次 MiMo 以获得干净的 app.asar，再重新应用壁纸。");
            }
            else
            {
                log("· 当前包已含皮肤，保留现有备份");
            }

            JObject root;
            long dataStart;
            List<AsarFile> packed;
            List<byte[]> chunks = new List<byte[]>();

            using (AsarArchive a = AsarArchive.Open(src))
            {
                root = a.Root;
                dataStart = a.DataStart;
                packed = a.CollectPacked();

                for (int i = 0; i < packed.Count; i++) chunks.Add(a.ReadFile(packed[i]));
            }

            // 0) 剔除上一次留下的皮肤文件 —— 换壁纸必须「替换」而不是「追加」，
            //    否则反复换图会在包里不断堆积旧字节，asar 越滚越大。
            HashSet<string> skinPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < NewFileNames.Length; i++) skinPaths.Add(AssetsDir + "/" + NewFileNames[i]);
            if (inp.ExtraFiles != null)
                foreach (string k in inp.ExtraFiles.Keys) skinPaths.Add(k);

            int removed = 0;
            long removedBytes = 0;
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                if (!skinPaths.Contains(packed[i].Path)) continue;
                removedBytes += chunks[i].Length;
                chunks.RemoveAt(i);
                packed.RemoveAt(i);
                removed++;
            }

            if (removed > 0)
            {
                JObject dir = root.Get("files") as JObject;
                dir = a2(root, AssetsDir);
                JObject files = dir.Get("files") as JObject;
                if (files != null)
                {
                    for (int i = 0; i < NewFileNames.Length; i++) files.Remove(NewFileNames[i]);
                }
                log("· 已移除旧皮肤文件 " + removed + " 个（" + removedBytes + " 字节）");
            }

            // 1) 改 index.html：插入 <link>
            int htmlIdx = -1;
            for (int i = 0; i < packed.Count; i++)
            {
                if (packed[i].Path == IndexHtml) { htmlIdx = i; break; }
            }
            if (htmlIdx < 0) throw new InvalidDataException("找不到 " + IndexHtml);

            string html = Encoding.UTF8.GetString(chunks[htmlIdx]);
            if (html.IndexOf(Mark, StringComparison.Ordinal) >= 0)
            {
                log("· index.html 已含皮肤标记，跳过插入");
            }
            else
            {
                int at = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                if (at < 0) throw new InvalidDataException("index.html 里找不到 </head>");

                string link = "    <link rel=\"stylesheet\" href=\"./assets/" + CssName + "\" " + Mark + "=\"1\">\n";
                html = html.Substring(0, at) + link + html.Substring(at);

                byte[] nb = Encoding.UTF8.GetBytes(html);
                chunks[htmlIdx] = nb;
                packed[htmlIdx].Node.Set("size", JNumber.FromLong(nb.Length));
                packed[htmlIdx].Node.Set("integrity", AsarWriter.IntegrityFor(nb));
                log("✓ 已插入 <link>，index.html " + nb.Length + " 字节");
            }

            // 2) 重排数据段
            long cursor = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                packed[i].Node.Set("offset", new JString(cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                cursor += chunks[i].Length;
            }

            // 3) 追加新文件
            JObject assetsNode = a2(root, AssetsDir);
            JObject af = assetsNode.Get("files") as JObject;
            if (af == null) { af = new JObject(); assetsNode.Set("files", af); }

            AddFile(af, CssName, inp.Css, ref cursor, chunks, log);
            AddFile(af, WallName, inp.Wallpaper, ref cursor, chunks, log);

            // 额外文件：在 asar 目录树里定位并替换。
            // 已存在的（如 launch.cjs）条目还在 dirNode 里，只需重写其 size/integrity/offset；
            // 不存在的（如新的 bundle 文件名）按路径建条目。
            if (inp.ExtraFiles != null)
            {
                foreach (KeyValuePair<string, byte[]> kv in inp.ExtraFiles)
                {
                    string asarPath = kv.Key;
                    byte[] content = kv.Value;

                    // 定位父目录节点
                    string dirPart = asarPath.Substring(0, asarPath.LastIndexOf('/'));
                    string fileName = asarPath.Substring(asarPath.LastIndexOf('/') + 1);
                    JObject dirNode = a2(root, dirPart);
                    JObject filesMap = dirNode.Get("files") as JObject;
                    if (filesMap == null) { filesMap = new JObject(); dirNode.Set("files", filesMap); }

                    JObject entry = new JObject();
                    entry.Set("size", JNumber.FromLong(content.Length));
                    entry.Set("offset", new JString(cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    entry.Set("integrity", AsarWriter.IntegrityFor(content));
                    filesMap.Set(fileName, entry);

                    chunks.Add(content);
                    cursor += content.Length;
                    log("✓ 已替换 " + asarPath + "（" + content.Length + " 字节）");
                }
            }

            // 4) 先写临时文件再覆盖，避免中途失败把原包写坏
            string tmp = src + ".mimo-skin-new";
            AsarWriter.Write(tmp, root, chunks);
            File.Copy(tmp, src, true);
            File.Delete(tmp);

            log("✓ 已写入 " + src);
            log("");
            log("—— 自检 ——");
            PatchResult vr = Verify(src, log);
            if (vr.HashFailures != 0 || !vr.SizeMatches)
                throw new InvalidDataException("自检未通过，请立刻用「还原」恢复！");
        }

        static JObject a2(JObject root, string path)
        {
            string[] parts = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            JObject node = root;
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

        static void AddFile(JObject filesNode, string name, byte[] buf, ref long cursor, List<byte[]> chunks, Action<string> log)
        {
            JObject e = new JObject();
            e.Set("size", JNumber.FromLong(buf.Length));
            e.Set("offset", new JString(cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            e.Set("integrity", AsarWriter.IntegrityFor(buf));
            filesNode.Set(name, e);

            chunks.Add(buf);
            cursor += buf.Length;
            log("✓ 已加入 " + name + "（" + buf.Length + " 字节）");
        }

        /* ------------------------------ 还原 ------------------------------ */

        public static void Restore(string asarPath, Action<string> log)
        {
            Action<string> L = log ?? delegate { };
            string bk = BackupOf(asarPath);
            if (!File.Exists(bk)) throw new FileNotFoundException("找不到备份：" + bk);
            File.Copy(bk, asarPath, true);
            L("✓ 已从备份还原");
            Verify(asarPath, L);
        }

        /* ---------------------------- 预检演练 ---------------------------- */
        // 在临时副本上完整跑一遍补丁并校验，完全不碰真实安装。
        public static PatchResult Preflight(string asarPath, byte[] css, byte[] wall, Action<string> log)
        {
            Action<string> L = log ?? delegate { };
            string tmp = asarPath + ".preflight";
            string tmpBk = BackupOf(tmp);
            try
            {
                File.Copy(asarPath, tmp, true);
                PatchInput inp = new PatchInput();
                inp.AsarPath = tmp;
                inp.Css = css;
                inp.Wallpaper = wall;
                inp.Log = L;
                Patch(inp);
                return Verify(tmp, null);
            }
            finally
            {
                TryDelete(tmp);
                TryDelete(tmpBk);
            }
        }

        static void TryDelete(string p)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }
}
