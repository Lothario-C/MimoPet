// Preview.cs —— 界面里的模拟窗口预览
//
// 用简化线框模拟 MiMo 的布局，把壁纸按当前设置画在对应区域上，
// 让用户在下发前就能看出「壁纸会出现在哪、怎么取景」。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MiMoSkin
{
    public sealed class PreviewPainter
    {
        public Image Wallpaper;
        public SkinConfig Cfg = new SkinConfig();

        /* 布局比例（按 MiMo 实际界面估算） */
        const float RailWidth = 0.052f;     // 左侧图标栏
        const float SideWidth = 0.168f;     // 会话列表
        const float TopBarH = 0.062f;

        public void Paint(Graphics g, RectangleF r)
        {
            if (r.Width < 20 || r.Height < 20) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            bool dark = Cfg.PanelDark > 0 && Cfg.PanelLight < 0;
            // 预览底色跟主题走：面板越不透，底色越实
            Color baseBg = Color.FromArgb(255, 250, 250, 249);

            using (SolidBrush b = new SolidBrush(baseBg)) g.FillRectangle(b, r);

            RectangleF rail = new RectangleF(r.X, r.Y, r.Width * RailWidth, r.Height);
            RectangleF side = new RectangleF(rail.Right, r.Y, r.Width * SideWidth, r.Height);
            RectangleF main = new RectangleF(side.Right, r.Y, r.Right - side.Right, r.Height);

            float sideTotal = r.Width * (RailWidth + SideWidth);

            /* 按区域决定壁纸画在哪 */
            string region = Cfg.Region;
            RectangleF target;
            if (region == "window") target = r;
            else if (region == "sidebar") target = new RectangleF(r.X, r.Y, sideTotal, r.Height);
            else if (region == "content") target = new RectangleF(main.X, main.Y, main.Width, main.Height);
            else target = main;   // main / custom

            // 内缩
            float ix = target.Width * Cfg.InsetLeft / 100f;
            float ir = target.Width * Cfg.InsetRight / 100f;
            float it = target.Height * Cfg.InsetTop / 100f;
            float ib = target.Height * Cfg.InsetBottom / 100f;
            RectangleF wp = new RectangleF(
                target.X + ix, target.Y + it,
                Math.Max(1, target.Width - ix - ir),
                Math.Max(1, target.Height - it - ib));

            /* 画壁纸 */
            if (Wallpaper != null)
            {
                RectangleF srcRect, dstRect;
                ComputeFit(Wallpaper.Size, wp, out srcRect, out dstRect);

                // 裁剪到目标区域，避免超出
                Region old = g.Clip;
                g.SetClip(wp, CombineMode.Intersect);
                try
                {
                    g.DrawImage(Wallpaper, dstRect, srcRect, GraphicsUnit.Pixel);
                }
                finally { g.Clip = old; }

                // 遮罩
                int scrimA = (int)Math.Round(255.0 * Cfg.Scrim / 100.0);
                if (scrimA > 0)
                {
                    Color sc = Color.FromArgb(scrimA, 255, 255, 255);
                    using (SolidBrush sb = new SolidBrush(sc)) g.FillRectangle(sb, wp);
                }
            }
            else
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(255, 236, 236, 234))) g.FillRectangle(sb, wp);
                using (Pen p = new Pen(Color.FromArgb(255, 200, 200, 198)))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawRectangle(p, wp.X, wp.Y, wp.Width, wp.Height);
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(255, 140, 140, 138)))
                        g.DrawString("未选择壁纸", new Font("Microsoft YaHei UI", 9f), tb, wp, sf);
                }
            }

            /* 画界面骨架（半透明面板） */
            float panelA = Math.Max(0.30f, Cfg.PanelLight / 100f);

            // 侧栏
            if (!(region == "window" && Cfg.SidebarTranslucent))
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1f, panelA)), 250, 250, 249)))
                    g.FillRectangle(sb, new RectangleF(r.X, r.Y, sideTotal, r.Height));
            }
            else
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(255 * panelA), 250, 250, 249)))
                    g.FillRectangle(sb, new RectangleF(r.X, r.Y, sideTotal, r.Height));
            }

            // rail
            using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1f, panelA * 0.96)), 246, 246, 244)))
                g.FillRectangle(sb, rail);

            // 顶栏
            using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(255 * panelA), 252, 252, 251)))
                g.FillRectangle(sb, new RectangleF(main.X, main.Y, main.Width, r.Height * TopBarH));

            // 主工作区内容卡片
            float pad = main.Width * 0.06f;
            RectangleF card = new RectangleF(
                main.X + pad, main.Y + r.Height * TopBarH + pad * 0.8f,
                main.Width - pad * 2, r.Height * 0.34f);
            using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(255 * panelA), 255, 255, 255)))
                g.FillRectangle(sb, card);

            // 输入框
            RectangleF input = new RectangleF(
                card.X, card.Bottom + pad * 0.8f, card.Width, r.Height * 0.11f);
            using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1f, panelA * 1.06)), 255, 255, 255)))
                g.FillRectangle(sb, input);

            /* 线框与标注 */
            using (Pen line = new Pen(Color.FromArgb(255, 226, 226, 224), 1f))
            {
                g.DrawRectangle(line, r.X, r.Y, r.Width - 1, r.Height - 1);
                g.DrawLine(line, rail.Right, r.Y, rail.Right, r.Bottom);
                g.DrawLine(line, side.Right, r.Y, side.Right, r.Bottom);
                g.DrawRectangle(line, card.X, card.Y, card.Width, card.Height);
                g.DrawRectangle(line, input.X, input.Y, input.Width, input.Height);
            }

            // 侧栏里的会话条目示意
            using (Pen dot = new Pen(Color.FromArgb(255, 208, 208, 206), 1f))
            using (SolidBrush db = new SolidBrush(Color.FromArgb(255, 232, 232, 230)))
            {
                float y = r.Y + r.Height * 0.16f;
                for (int i = 0; i < 7 && y < r.Bottom - 10; i++)
                {
                    RectangleF item = new RectangleF(side.X + side.Width * 0.08f, y, side.Width * 0.84f, r.Height * 0.035f);
                    g.FillRectangle(db, item);
                    y += r.Height * 0.055f;
                }
                // rail 图标
                float ry = r.Y + r.Height * 0.10f;
                for (int i = 0; i < 5 && ry < r.Bottom - 10; i++)
                {
                    RectangleF ic = new RectangleF(rail.X + rail.Width * 0.3f, ry, rail.Width * 0.4f, rail.Width * 0.4f);
                    g.DrawEllipse(dot, ic);
                    ry += rail.Width * 0.9f;
                }
            }

            // 目标区域高亮边框，让「渲染区域」一目了然
            using (Pen hi = new Pen(Color.FromArgb(255, 255, 106, 0), 2f))
            {
                hi.DashStyle = DashStyle.Dash;
                hi.DashPattern = new float[] { 5f, 3f };
                g.DrawRectangle(hi, target.X + 1, target.Y + 1, target.Width - 2, target.Height - 2);
            }
        }

        // 按 fit / 缩放 / 位置算出源矩形和目标矩形
        void ComputeFit(Size img, RectangleF dst, out RectangleF srcRect, out RectangleF dstRect)
        {
            float iw = img.Width, ih = img.Height;
            srcRect = new RectangleF(0, 0, iw, ih);

            string fit = Cfg.Fit;

            if (fit == "fill")
            {
                // 拉伸铺满，不保持比例
                dstRect = dst;
                return;
            }

            float scale;
            if (fit == "contain")
            {
                scale = Math.Min(dst.Width / iw, dst.Height / ih);
                float w = iw * scale, h = ih * scale;
                float x = dst.X + (dst.Width - w) * Cfg.PosX / 100f;
                float y = dst.Y + (dst.Height - h) * Cfg.PosY / 100f;
                dstRect = new RectangleF(x, y, w, h);
                return;
            }

            if (fit == "scale")
            {
                scale = (dst.Width / iw) * (Cfg.ScalePercent / 100f);
                float w = iw * scale, h = ih * scale;
                float x = dst.X + (dst.Width - w) * Cfg.PosX / 100f;
                float y = dst.Y + (dst.Height - h) * Cfg.PosY / 100f;
                dstRect = new RectangleF(x, y, w, h);
                return;
            }

            // cover：等比填满后裁切
            scale = Math.Max(dst.Width / iw, dst.Height / ih);
            float sw = dst.Width / scale, sh = dst.Height / scale;
            float sx = (iw - sw) * Cfg.PosX / 100f;
            float sy = (ih - sh) * Cfg.PosY / 100f;
            srcRect = new RectangleF(sx, sy, sw, sh);
            dstRect = dst;
        }
    }
}
