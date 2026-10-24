// SkinConfig.cs —— 皮肤设置模型与持久化
//
// 配置文件与 exe 同目录（mimoskin.json）。用自带的 Json 解析器读写，
// 不引入 JavaScriptSerializer，避免额外的程序集依赖。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MiMoSkin
{
    public sealed class SkinConfig
    {
        /* 壁纸来源 */
        public string WallpaperPath = "";
        public string WallpaperFolder = "";
        public int MaxWidth = 2560;

        /* 渲染区域 */
        public string Region = "main";          // main | window | sidebar | content | custom
        public string CustomSelector = "";
        public int InsetTop = 0;                // 百分比 0..40
        public int InsetRight = 0;
        public int InsetBottom = 0;
        public int InsetLeft = 0;

        /* 取景 */
        public string Fit = "cover";            // cover | contain | fill | scale
        public int ScalePercent = 100;          // 50..300
        public int PosX = 50;                   // 0..100
        public int PosY = 50;

        /* 观感 */
        public int Scrim = 10;                  // 遮罩强度 0..100
        public int Blur = 0;                    // 模糊 0..40 px
        public int PanelLight = 55;             // 面板不透明度（浅色主题）
        public int PanelDark = 65;              // 面板不透明度（深色主题）
        public bool SidebarTranslucent = true;  // 整窗壁纸时让侧栏也透出来

        public string RegionSelector()
        {
            switch (Region)
            {
                case "window": return "#app";
                case "sidebar": return "aside[data-sidebar-unified]";
                case "content": return ".info-panel-host";
                case "custom":
                    return string.IsNullOrEmpty(CustomSelector) ? "main[data-main-surface]" : CustomSelector.Trim();
                default: return "main[data-main-surface]";
            }
        }

        public string RegionLabel()
        {
            switch (Region)
            {
                case "window": return "整个窗口";
                case "sidebar": return "仅左侧边栏";
                case "content": return "仅聊天内容区";
                case "custom": return "自定义选择器";
                default: return "主工作区";
            }
        }

        public SkinConfig Clone()
        {
            SkinConfig c = new SkinConfig();
            c.WallpaperPath = WallpaperPath;
            c.WallpaperFolder = WallpaperFolder;
            c.MaxWidth = MaxWidth;
            c.Region = Region;
            c.CustomSelector = CustomSelector;
            c.InsetTop = InsetTop;
            c.InsetRight = InsetRight;
            c.InsetBottom = InsetBottom;
            c.InsetLeft = InsetLeft;
            c.Fit = Fit;
            c.ScalePercent = ScalePercent;
            c.PosX = PosX;
            c.PosY = PosY;
            c.Scrim = Scrim;
            c.Blur = Blur;
            c.PanelLight = PanelLight;
            c.PanelDark = PanelDark;
            c.SidebarTranslucent = SidebarTranslucent;
            return c;
        }

        /* ------------------------------ 持久化 ------------------------------ */

        public static string DefaultPath(string exeDir)
        {
            return Path.Combine(exeDir, "mimoskin.json");
        }

        public static SkinConfig Load(string path)
        {
            SkinConfig c = new SkinConfig();
            if (!File.Exists(path)) return c;
            try
            {
                JObject o = Json.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8)) as JObject;
                if (o == null) return c;

                c.WallpaperPath = Str(o, "wallpaperPath", c.WallpaperPath);
                c.WallpaperFolder = Str(o, "wallpaperFolder", c.WallpaperFolder);
                c.MaxWidth = Int(o, "maxWidth", c.MaxWidth);
                c.Region = Str(o, "region", c.Region);
                c.CustomSelector = Str(o, "customSelector", c.CustomSelector);
                c.InsetTop = Int(o, "insetTop", c.InsetTop);
                c.InsetRight = Int(o, "insetRight", c.InsetRight);
                c.InsetBottom = Int(o, "insetBottom", c.InsetBottom);
                c.InsetLeft = Int(o, "insetLeft", c.InsetLeft);
                c.Fit = Str(o, "fit", c.Fit);
                c.ScalePercent = Int(o, "scalePercent", c.ScalePercent);
                c.PosX = Int(o, "posX", c.PosX);
                c.PosY = Int(o, "posY", c.PosY);
                c.Scrim = Int(o, "scrim", c.Scrim);
                c.Blur = Int(o, "blur", c.Blur);
                c.PanelLight = Int(o, "panelLight", c.PanelLight);
                c.PanelDark = Int(o, "panelDark", c.PanelDark);
                c.SidebarTranslucent = Bool(o, "sidebarTranslucent", c.SidebarTranslucent);
            }
            catch { /* 配置损坏时用默认值，不影响启动 */ }
            return c;
        }

        public void Save(string path)
        {
            JObject o = new JObject();
            o.Set("wallpaperPath", new JString(WallpaperPath));
            o.Set("wallpaperFolder", new JString(WallpaperFolder));
            o.Set("maxWidth", JNumber.FromLong(MaxWidth));
            o.Set("region", new JString(Region));
            o.Set("customSelector", new JString(CustomSelector));
            o.Set("insetTop", JNumber.FromLong(InsetTop));
            o.Set("insetRight", JNumber.FromLong(InsetRight));
            o.Set("insetBottom", JNumber.FromLong(InsetBottom));
            o.Set("insetLeft", JNumber.FromLong(InsetLeft));
            o.Set("fit", new JString(Fit));
            o.Set("scalePercent", JNumber.FromLong(ScalePercent));
            o.Set("posX", JNumber.FromLong(PosX));
            o.Set("posY", JNumber.FromLong(PosY));
            o.Set("scrim", JNumber.FromLong(Scrim));
            o.Set("blur", JNumber.FromLong(Blur));
            o.Set("panelLight", JNumber.FromLong(PanelLight));
            o.Set("panelDark", JNumber.FromLong(PanelDark));
            o.Set("sidebarTranslucent", new JBool(SidebarTranslucent));

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Stringify(o), new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static string Str(JObject o, string k, string def)
        {
            JString s = o.Get(k) as JString;
            return s == null ? def : s.Value;
        }

        static int Int(JObject o, string k, int def)
        {
            JNumber n = o.Get(k) as JNumber;
            if (n == null) return def;
            try { return (int)n.ToLong(); } catch { return def; }
        }

        static bool Bool(JObject o, string k, bool def)
        {
            JBool b = o.Get(k) as JBool;
            return b == null ? def : b.Value;
        }
    }
}
