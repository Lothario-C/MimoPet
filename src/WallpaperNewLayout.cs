// WallpaperNewLayout.cs —— 新版客户端（26.1023+）的壁纸注入
//
// 新架构：应用代码不再在 app.asar 里，而在
//   %APPDATA%/Xiaomi MiMo/runtime-res/releases/
//     renderer/static/index.html     ← 入口 HTML
//     renderer/assets/...            ← 散装资源
//
// 注入方式：把皮肤 CSS 和壁纸 JPG 作为**新文件**放进 assets/（不在资源清单里，
// 客户端的完整性校验只针对清单内文件），再在 index.html 尾部加一行 <link>。
//
// ⚠️ 已知风险（实测）：index.html 是清单内文件，改动会在客户端资源校验后
//    被还原。所以本模块每次启动桌宠时检测 link 是否还在，不在就补一次
//    （补 link 是幂等的小操作）。若发现补了又被还原导致壁纸失效，
//    请告诉我，需要换「CSS 注入到已有 CSS 文件末尾」之外的思路。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MiMoSkin
{
    public static class WallpaperNewLayout
    {
        public static bool Available
        {
            get { return File.Exists(IndexHtmlPath); }
        }

        public static string ReleasesDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Xiaomi MiMo", "runtime-res", "releases");
            }
        }

        public static string AssetsDir
        {
            get { return Path.Combine(ReleasesDir, "renderer", "assets"); }
        }

        public static string IndexHtmlPath
        {
            get { return Path.Combine(ReleasesDir, "renderer", "static", "index.html"); }
        }

        public const string CssName = "mimo-dream-skin.css";
        public const string WallName = "mimo-wallpaper.jpg";
        public const string Mark = "data-mimo-dream-skin";

        // 主 CSS 文件（index~*.css）。@import 到它尾部 —— 不碰 index.html：
        // index.html 在资源清单里，改它会在启动时触发完整性告警弹窗（实测）。
        // @import 必须出现在 CSS 文件头部才有效 —— 但这里是「我们自己的 CSS」
        // import 主 CSS，反过来不行。所以改为：把皮肤 CSS 的内容**追加**到
        // 主 CSS 文件尾部，并打标记注释。追加内容全是我们生成的，末尾拼接安全。
        public static string MainCssPath
        {
            get
            {
                string dir = AssetsDir;
                if (!Directory.Exists(dir)) return null;
                foreach (string f in Directory.GetFiles(dir, "index~*.css"))
                {
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    if (text.Contains(Mark)) return f;          // 已打过
                    // 主 CSS 应该非常大（>100KB），避免抓到别的
                    if (new FileInfo(f).Length > 100000) return f;
                }
                // 没有打过标记 → 找最大的
                string best = null; long bestLen = 0;
                foreach (string f in Directory.GetFiles(dir, "index~*.css"))
                {
                    long len = new FileInfo(f).Length;
                    if (len > bestLen) { bestLen = len; best = f; }
                }
                return best;
            }
        }

        // 是否已注入（皮肤文件存在 + 主 CSS 含标记）
        public static bool IsPatched
        {
            get
            {
                try
                {
                    if (!File.Exists(Path.Combine(AssetsDir, CssName))) return false;
                    if (!File.Exists(Path.Combine(AssetsDir, WallName))) return false;
                    string main = MainCssPath;
                    if (main == null) return false;
                    return File.ReadAllText(main, Encoding.UTF8)
                        .IndexOf(Mark, StringComparison.Ordinal) >= 0;
                }
                catch { return false; }
            }
        }

        // 写入皮肤文件 + 追加到主 CSS。返回错误，null = 成功。
        public static string Install(byte[] css, byte[] wallpaper)
        {
            try
            {
                if (!Directory.Exists(AssetsDir)) return "找不到 renderer/assets 目录";
                File.WriteAllBytes(Path.Combine(AssetsDir, CssName), css);
                File.WriteAllBytes(Path.Combine(AssetsDir, WallName), wallpaper);

                string main = MainCssPath;
                if (main == null) return "找不到主 CSS 文件";
                AppendToMainCss(main, css, false);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // 主 CSS 尾部追加皮肤内容（幂等）：先移除旧标记段，再追加新内容。
        // 皮肤内容直接内联（而非 @import），因为 @import 必须在文件头部。
        static void AppendToMainCss(string mainPath, byte[] css, bool _)
        {
            string text = File.Exists(mainPath)
                ? File.ReadAllText(mainPath, Encoding.UTF8)
                : "";

            // 移除旧的皮肤段（标记到标记尾）
            int start = text.IndexOf(MarkBegin, StringComparison.Ordinal);
            if (start >= 0)
            {
                int end = text.IndexOf(MarkEnd, start, StringComparison.Ordinal);
                if (end >= 0) text = text.Substring(0, start) + text.Substring(end + MarkEnd.Length);
                else text = text.Substring(0, start);
            }
            else
            {
                // 兼容旧版 link 标记残留（如果之前用过 index.html 方案）
                // 不处理 index.html —— 由 Uninstall 清理
            }

            text = text.TrimEnd() + "\n" + MarkBegin + "\n" +
                Encoding.UTF8.GetString(css) + "\n" + MarkEnd + "\n";
            File.WriteAllText(mainPath, text, new UTF8Encoding(false));
        }

        public const string MarkBegin = "/* == mimo-dream-skin:begin == */";
        public const string MarkEnd = "/* == mimo-dream-skin:end == */";

        // 还原：移除皮肤文件与主 CSS 里的皮肤段
        public static string Uninstall()
        {
            try
            {
                foreach (string f in new string[] { CssName, WallName })
                {
                    string p = Path.Combine(AssetsDir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
                string main = MainCssPath;
                if (main != null)
                {
                    string text = File.ReadAllText(main, Encoding.UTF8);
                    int start = text.IndexOf(MarkBegin, StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        int end = text.IndexOf(MarkEnd, start, StringComparison.Ordinal);
                        if (end >= 0) text = text.Substring(0, start) + text.Substring(end + MarkEnd.Length);
                        else text = text.Substring(0, start);
                        File.WriteAllText(main, text, new UTF8Encoding(false));
                    }
                }
                // 顺手清理 index.html 里旧方案可能留下的 link
                if (File.Exists(IndexHtmlPath))
                {
                    string html = File.ReadAllText(IndexHtmlPath, Encoding.UTF8);
                    int ls = html.IndexOf("<link", StringComparison.Ordinal);
                    bool changed = false;
                    while (ls >= 0)
                    {
                        int le = html.IndexOf('>', ls);
                        if (le < 0) break;
                        string tag = html.Substring(ls, le - ls + 1);
                        if (tag.Contains(Mark)) { html = html.Remove(ls, le - ls + 1); changed = true; }
                        ls = html.IndexOf("<link", ls);
                    }
                    if (changed) File.WriteAllText(IndexHtmlPath, html, new UTF8Encoding(false));
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
