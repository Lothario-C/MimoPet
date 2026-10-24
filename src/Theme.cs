// Theme.cs —— 设计令牌与绘制工具
//
// 设计方向：暖灰底 + 纯白卡片 + 细描边，橙色作为唯一强调色。
// 参考现代桌面工具的克制质感，不追求花哨，但每个细节都打磨过。
//
// 全部控件自绘：WinForms 默认控件是 Windows 95 风格，与产品气质严重不符。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiMoSkin
{
    public static class Theme
    {
        /* ------------------------------ 色板 ------------------------------ */

        // 背景层次：从最底层到最上层逐级提亮
        public static readonly Color Bg = Color.FromArgb(255, 247, 246, 244);       // 窗口底
        public static readonly Color Surface = Color.FromArgb(255, 253, 253, 252);  // 卡片
        public static readonly Color SurfaceAlt = Color.FromArgb(255, 250, 249, 247); // 输入框底
        public static readonly Color Rail = Color.FromArgb(255, 241, 240, 237);     // 侧栏

        // 文字
        public static readonly Color Ink = Color.FromArgb(255, 26, 25, 23);         // 主文字
        public static readonly Color InkSoft = Color.FromArgb(255, 88, 86, 82);     // 次级
        public static readonly Color Muted = Color.FromArgb(255, 138, 135, 129);    // 辅助

        // 强调色（MiMo 橙）
        public static readonly Color Accent = Color.FromArgb(255, 255, 106, 0);
        public static readonly Color AccentHover = Color.FromArgb(255, 232, 96, 0);
        public static readonly Color AccentActive = Color.FromArgb(255, 208, 86, 0);
        public static readonly Color AccentSoft = Color.FromArgb(255, 255, 243, 233);
        public static readonly Color AccentLine = Color.FromArgb(255, 255, 214, 178);

        // 描边
        public static readonly Color Line = Color.FromArgb(255, 232, 230, 226);
        public static readonly Color LineSoft = Color.FromArgb(255, 240, 238, 234);
        public static readonly Color LineStrong = Color.FromArgb(255, 214, 211, 206);

        // 状态
        public static readonly Color Ok = Color.FromArgb(255, 34, 160, 92);
        public static readonly Color Warn = Color.FromArgb(255, 214, 140, 20);
        public static readonly Color Err = Color.FromArgb(255, 210, 60, 60);

        /* ------------------------------ 字体 ------------------------------ */

        const string UiFamily = "Microsoft YaHei UI";
        const string MonoFamily = "Consolas";

        public static Font FontTitle = new Font(UiFamily, 15f, FontStyle.Bold);
        public static Font FontH2 = new Font(UiFamily, 10.5f, FontStyle.Bold);
        public static Font FontBody = new Font(UiFamily, 9f);
        public static Font FontSmall = new Font(UiFamily, 8.5f);
        public static Font FontTiny = new Font(UiFamily, 8f);
        public static Font FontNum = new Font(UiFamily, 9f, FontStyle.Bold);
        public static Font FontMono = new Font(MonoFamily, 8.5f);
        public static Font FontMonoSm = new Font(MonoFamily, 8f);

        /* ------------------------------ 绘制 ------------------------------ */

        // 圆角矩形路径。radius 会自动收敛到不超过半边长，避免细长控件画出畸形。
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
            GraphicsPath p = new GraphicsPath();
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }

            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c, float w)
        {
            // 内缩半个线宽，让描边落在边界内侧而不是被裁掉一半
            RectangleF rr = new RectangleF(r.X + w / 2f, r.Y + w / 2f, r.Width - w, r.Height - w);
            using (GraphicsPath p = Round(rr, radius))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        public static void FillStrokeRound(Graphics g, RectangleF r, float radius, Color fill, Color stroke, float w)
        {
            FillRound(g, r, radius, fill);
            if (w > 0) StrokeRound(g, r, radius, stroke, w);
        }

        // 高质量文字绘制：ClearType + 抗锯齿
        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r,
            ContentAlignment align)
        {
            TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c,
                TextFormatFlags.NoPadding | FlagsFor(align));
        }

        public static void Text(Graphics g, string s, Font f, Color c, PointF pt)
        {
            TextRenderer.DrawText(g, s, f, Point.Round(pt), c, TextFormatFlags.NoPadding);
        }

        public static SizeF Measure(Graphics g, string s, Font f)
        {
            Size sz = TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
            return new SizeF(sz.Width, sz.Height);
        }

        static TextFormatFlags FlagsFor(ContentAlignment a)
        {
            TextFormatFlags f = TextFormatFlags.NoPadding;
            switch (a)
            {
                case ContentAlignment.MiddleLeft: f |= TextFormatFlags.Left | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.MiddleCenter: f |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.MiddleRight: f |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.TopLeft: f |= TextFormatFlags.Left | TextFormatFlags.Top; break;
                case ContentAlignment.TopCenter: f |= TextFormatFlags.HorizontalCenter | TextFormatFlags.Top; break;
                case ContentAlignment.BottomLeft: f |= TextFormatFlags.Left | TextFormatFlags.Bottom; break;
                case ContentAlignment.BottomCenter: f |= TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom; break;
                case ContentAlignment.BottomRight: f |= TextFormatFlags.Right | TextFormatFlags.Bottom; break;
                default: f |= TextFormatFlags.Left | TextFormatFlags.VerticalCenter; break;
            }
            return f;
        }

        public static void SetupGraphics(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        // 颜色混合，用于 hover / pressed 过渡
        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
        }

        // 极淡的投影：三层同心圆角矩形，透明度递减
        public static void SoftShadow(Graphics g, RectangleF r, float radius, int strength)
        {
            for (int i = 3; i >= 1; i--)
            {
                RectangleF rr = new RectangleF(r.X - i, r.Y - i + 1, r.Width + i * 2, r.Height + i * 2);
                int a = (int)(strength * (1.0 / (i * 2.2)));
                if (a <= 0) continue;
                using (GraphicsPath p = Round(rr, radius + i))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 60, 55, 48)))
                    g.FillPath(b, p);
            }
        }
    }
}
