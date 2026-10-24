// CssBuilder.cs —— 根据设置生成注入用的皮肤 CSS
//
// 布局依据（从 app.asar 渲染层提取的真实结构）：
//   #app.relative.flex.h-screen
//     ├─ aside[data-sidebar-unified]      ← 左侧边栏（含 rail + 会话列表）
//     └─ main[data-main-surface]          ← 主工作区
//          └─ .info-panel-host            ← 聊天内容区
//
// 壁纸挂在目标元素的 ::before 上，用两层渐变叠出「遮罩 + 半透明面板 + 壁纸」，
// 这样面板的半透明质感才在壁纸之上，不会变成裸图直出。
using System;
using System.Globalization;
using System.Text;

namespace MiMoSkin
{
    public static class CssBuilder
    {
        static string F(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string Build(SkinConfig c)
        {
            StringBuilder sb = new StringBuilder(8192);

            string sel = c.RegionSelector();
            bool wholeWindow = c.Region == "window";

            double scrim = Math.Max(0, Math.Min(100, c.Scrim)) / 100.0;
            double panelLight = Math.Max(0, Math.Min(100, c.PanelLight)) / 100.0;
            double panelDark = Math.Max(0, Math.Min(100, c.PanelDark)) / 100.0;

            string fitMode;
            switch (c.Fit)
            {
                case "contain": fitMode = "contain"; break;
                case "fill": fitMode = "100% 100%"; break;
                case "scale":
                    fitMode = F(c.ScalePercent) + "% auto";
                    break;
                default: fitMode = "cover"; break;
            }

            string pos = F(c.PosX) + "% " + F(c.PosY) + "%";

            sb.AppendLine("/* ==========================================================================");
            sb.AppendLine("   MimoPet · 由 MimoPet 生成");
            sb.AppendLine("   区域：" + c.RegionLabel() + "   取景：" + fitMode + " @ " + pos);
            sb.AppendLine("   ========================================================================== */");
            sb.AppendLine();

            /* [1] 变量 ------------------------------------------------------- */
            sb.AppendLine(":root {");
            sb.AppendLine("  --skin-scrim: " + F(scrim) + ";");
            sb.AppendLine("  --skin-scrim-color: rgba(255, 255, 255, var(--skin-scrim));");
            sb.AppendLine("  --skin-content-light: " + F(panelLight) + ";");
            sb.AppendLine("  --skin-content-dark: " + F(panelDark) + ";");
            sb.AppendLine("  --skin-blur: " + c.Blur + "px;");
            sb.AppendLine("}");
            sb.AppendLine();

            /* [2] 壁纸层 ----------------------------------------------------- */
            sb.AppendLine("/* [2] 壁纸层：挂在目标元素的 ::before 上 */");
            sb.AppendLine(sel + "::before {");
            sb.AppendLine("  content: \"\";");
            sb.AppendLine("  position: absolute;");
            sb.AppendLine("  inset: 0;");
            if (c.InsetTop != 0 || c.InsetRight != 0 || c.InsetBottom != 0 || c.InsetLeft != 0)
            {
                sb.AppendLine("  /* 内缩（百分比）：让壁纸避开标题栏 / 边栏等区域 */");
                sb.AppendLine("  inset: " + c.InsetTop + "% " + c.InsetRight + "% " + c.InsetBottom + "% " + c.InsetLeft + "%;");
            }
            // 负 z-index 让壁纸沉到内容之下。
            // 配合目标元素的 isolation:isolate，负值不会逃到父级背景后面。
            sb.AppendLine("  z-index: -1;");
            sb.AppendLine("  pointer-events: none;");
            sb.AppendLine("  background-image:");
            sb.AppendLine("    linear-gradient(var(--skin-scrim-color), var(--skin-scrim-color)),");
            sb.AppendLine("    linear-gradient(var(--color-panel), var(--color-panel)),");
            sb.AppendLine("    url(\"./mimo-wallpaper.jpg\");");
            sb.AppendLine("  background-size: cover, cover, " + fitMode + ";");
            sb.AppendLine("  background-position: center center, center center, " + pos + ";");
            sb.AppendLine("  background-repeat: no-repeat, no-repeat, no-repeat;");
            if (c.Blur > 0)
            {
                sb.AppendLine("  filter: blur(var(--skin-blur));");
                // 模糊会把边缘透出底色，放大一点让边缘不留白
                sb.AppendLine("  transform: scale(1.06);");
            }
            sb.AppendLine("}");
            sb.AppendLine();

            // 目标元素本身要能作为定位容器。
            // isolation 建立层叠上下文，让壁纸的负 z-index 停在内部，
            // 不会跑到父级背景之下而消失。
            sb.AppendLine(sel + " {");
            sb.AppendLine("  position: relative;");
            sb.AppendLine("  isolation: isolate;");
            sb.AppendLine("  background-color: transparent !important;");
            sb.AppendLine("}");
            sb.AppendLine();

            // 注意：绝对不要给目标元素的子元素设 position/z-index。
            //
            // MiMo 的顶栏（data-topbar-left / data-topbar-right / data-brand-actions）
            // 是 #app 的直接子元素且用 position:absolute 定位。一旦改成 relative，
            // 它们会掉进正常文档流 —— 顶栏错位、侧边栏被挤没、窗口拖拽区失效。
            //
            // 壁纸已用负 z-index 沉到内容之下，子元素保持原样即可。
            sb.AppendLine();

            sb.AppendLine("html, body { background-color: transparent !important; }");
            sb.AppendLine();

            /* [3] 浅色主题 --------------------------------------------------- */
            sb.AppendLine("/* [3] 浅色主题：面板半透明，让壁纸透出来 */");
            sb.AppendLine(":root, :host {");
            sb.AppendLine("  --color-bg:                  rgba(255, 254, 253, calc(var(--skin-content-light) * 1.00));");
            sb.AppendLine("  --color-main-bg:             rgba(255, 255, 255, calc(var(--skin-content-light) * 1.00));");
            sb.AppendLine("  --color-panel:               rgba(255, 255, 255, calc(var(--skin-content-light) * 1.00));");
            sb.AppendLine("  --color-sunken:              rgba(248, 247, 245, calc(var(--skin-content-light) * 0.90));");
            if (c.SidebarTranslucent && wholeWindow)
            {
                sb.AppendLine("  --color-side:                rgba(255, 255, 255, calc(var(--skin-content-light) * 1.02));");
                sb.AppendLine("  --color-side-glass:          rgba(255, 255, 255, calc(var(--skin-content-light) * 1.02));");
                sb.AppendLine("  --color-settings-nav:        rgba(248, 247, 245, calc(var(--skin-content-light) * 1.00));");
            }
            else
            {
                sb.AppendLine("  --color-side:                rgba(255, 255, 255, 1.00);");
                sb.AppendLine("  --color-side-glass:          rgba(255, 255, 255, 1.00);");
                sb.AppendLine("  --color-settings-nav:        rgba(248, 247, 245, 1.00);");
            }
            sb.AppendLine("  --color-settings-content:    rgba(255, 255, 255, calc(var(--skin-content-light) * 1.00));");
            sb.AppendLine("  --color-settings-card:       rgba(255, 255, 255, calc(var(--skin-content-light) * 1.00));");
            sb.AppendLine("  --color-set-card:            rgba(255, 255, 255, calc(var(--skin-content-light) * 1.00));");
            sb.AppendLine("  --color-elevated-card:       rgba(255, 255, 255, calc(var(--skin-content-light) * 1.06));");
            sb.AppendLine("  --color-input-bg:            rgba(255, 255, 255, calc(var(--skin-content-light) * 1.06));");
            sb.AppendLine("  --color-composer-surface:    rgba(255, 255, 255, calc(var(--skin-content-light) * 1.08));");
            sb.AppendLine("  --color-composer-tray-bg:    rgba(248, 247, 245, calc(var(--skin-content-light) * 0.92));");
            sb.AppendLine("  --color-right-panel-page-bg: rgba(255, 255, 255, calc(var(--skin-content-light) * 1.00));");
            sb.AppendLine("  --color-popover:             rgba(255, 255, 255, calc(var(--skin-content-light) * 1.14));");
            sb.AppendLine("  --color-bubble:              rgba(248, 247, 245, calc(var(--skin-content-light) * 0.94));");
            sb.AppendLine("  --color-code-bg:             rgba(240, 240, 242, calc(var(--skin-content-light) * 1.04));");
            sb.AppendLine("  --color-diff-viewer-bg:      rgba(255, 255, 255, calc(var(--skin-content-light) * 1.00));");
            sb.AppendLine("  --color-thumbnail-bg:        rgba(250, 250, 250, calc(var(--skin-content-light) * 0.96));");
            sb.AppendLine("  --color-menu-glass:          rgba(255, 255, 255, calc(var(--skin-content-light) * 1.16));");
            sb.AppendLine("  --color-notice-bg:           rgba(255, 255, 255, calc(var(--skin-content-light) * 1.16));");
            sb.AppendLine("}");
            sb.AppendLine();

            /* [4] 深色主题 --------------------------------------------------- */
            sb.AppendLine("/* [4] 深色主题 */");
            sb.AppendLine(":root[data-theme=\"dark\"],");
            sb.AppendLine("[data-theme=\"dark\"] {");
            sb.AppendLine("  --skin-scrim-color: rgba(0, 0, 0, var(--skin-scrim));");
            sb.AppendLine("  --color-bg:                  rgba(24, 24, 24, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-main-bg:             rgba(24, 24, 24, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-panel:               rgba(24, 24, 24, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-sunken:              rgba(46, 46, 46, calc(var(--skin-content-dark) * 0.90));");
            if (c.SidebarTranslucent && wholeWindow)
            {
                sb.AppendLine("  --color-side:                rgba(18, 18, 18, calc(var(--skin-content-dark) * 1.02));");
                sb.AppendLine("  --color-side-glass:          rgba(18, 18, 18, calc(var(--skin-content-dark) * 1.02));");
                sb.AppendLine("  --color-settings-nav:        rgba(18, 18, 18, calc(var(--skin-content-dark) * 1.00));");
            }
            else
            {
                sb.AppendLine("  --color-side:                rgba(18, 18, 18, 1.00);");
                sb.AppendLine("  --color-side-glass:          rgba(18, 18, 18, 1.00);");
                sb.AppendLine("  --color-settings-nav:        rgba(18, 18, 18, 1.00);");
            }
            sb.AppendLine("  --color-settings-content:    rgba(31, 31, 31, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-settings-card:       rgba(40, 40, 40, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-set-card:            rgba(26, 26, 26, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-elevated-card:       rgba(26, 26, 26, calc(var(--skin-content-dark) * 1.04));");
            sb.AppendLine("  --color-input-bg:            rgba(31, 31, 31, calc(var(--skin-content-dark) * 1.06));");
            sb.AppendLine("  --color-composer-surface:    rgba(31, 31, 31, calc(var(--skin-content-dark) * 1.10));");
            sb.AppendLine("  --color-composer-tray-bg:    rgba(21, 21, 21, calc(var(--skin-content-dark) * 0.92));");
            sb.AppendLine("  --color-right-panel-page-bg: rgba(24, 24, 24, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-popover:             rgba(26, 26, 26, calc(var(--skin-content-dark) * 1.24));");
            sb.AppendLine("  --color-bubble:              rgba(46, 46, 46, calc(var(--skin-content-dark) * 0.94));");
            sb.AppendLine("  --color-code-bg:             rgba(46, 46, 46, calc(var(--skin-content-dark) * 1.12));");
            sb.AppendLine("  --color-diff-viewer-bg:      rgba(30, 30, 30, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-thumbnail-bg:        rgba(36, 36, 36, calc(var(--skin-content-dark) * 1.00));");
            sb.AppendLine("  --color-menu-glass:          rgba(22, 22, 22, calc(var(--skin-content-dark) * 1.20));");
            sb.AppendLine("  --color-notice-bg:           rgba(46, 46, 46, calc(var(--skin-content-dark) * 1.20));");
            sb.AppendLine("}");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}
