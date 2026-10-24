// BubbleForm.cs —— 桌宠悬停气泡菜单（思考气泡造型）
//
// 造型：圆角气泡 + 底部一条弯尾巴指向桌宠（对话气泡轮廓），
// 内部是 2×2 网格的像素风图标按钮 + 底部退出条。卡通配色贴合 MiMo 橙主题。
//
// 按钮绘制全部程序化（无外部资源）：像素方块图标 + 粗描边 + 悬停抬升。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiMoSkin
{
    public sealed class BubbleForm : Form
    {
        public sealed class BubbleItem
        {
            public string Text;
            public int ToolIndex = -1;
            public bool IsExit;
            public PixelIcon Icon;      // 像素图标种类
        }

        public enum PixelIcon { Wallpaper, Chart, Coin, Brain, Exit }

        readonly BubbleItem[] _items;
        int _hover = -1;            // 网格按钮 hover
        bool _hoverExit;

        public event EventHandler<BubbleItem> ItemClicked;

        /* 布局：2 列网格按钮 + 底部退出条 + 尾巴 */
        const int GridCols = 2;
        const int CellW = 82, CellH = 70;     // 每格尺寸（加大：给中文标签留出清晰字号）
        const int PadX = 10, PadTop = 10, PadMid = 6, PadBottom = 10;
        const int ExitH = 28;
        const int TailH = 12;
        static int GridRows { get { return 2; } }
        static int BubbleW { get { return PadX * 2 + CellW * GridCols; } }
        static int BodyH { get { return PadTop + CellH * GridRows + PadMid + ExitH + PadBottom; } }
        static int TotalH { get { return BodyH + TailH; } }

        // 弯尾巴（指向桌宠）：贝塞尔轮廓
        static int TailX { get { return BubbleW / 2 - 7; } }   // 尾巴相对气泡的水平位置

        System.Windows.Forms.Timer _closeWatch;

        public BubbleForm(BubbleItem[] items)
        {
            _items = items ?? new BubbleItem[0];

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Text = "MiMoBubble";

            ClientSize = new Size(BubbleW, TotalH);

            _closeWatch = new System.Windows.Forms.Timer();
            _closeWatch.Interval = 60;
            _closeWatch.Tick += delegate { CheckAutoClose(); };
            _closeWatch.Start();
        }

        public void ShowAbove(Rectangle petBounds)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = petBounds.Left + petBounds.Width / 2 - BubbleW / 2;
            int y = petBounds.Top - TotalH - 2;
            if (y < wa.Top + 4) { y = petBounds.Bottom + 2; _showArrowBelow = true; }
            else _showArrowBelow = false;
            x = Math.Max(wa.Left + 4, Math.Min(wa.Right - Width - 4, x));
            Location = new Point(x, y);
            Show();
            Render();
        }

        bool _showArrowBelow;

        /* ------------------------------ 命中与交互 ------------------------------ */

        DateTime _leaveSince = DateTime.MaxValue;

        void CheckAutoClose()
        {
            Point cur = Cursor.Position;
            bool onBubble = Bounds.Contains(cur);
            bool onPet = _petBounds.Contains(cur);

            if (onBubble || onPet) _leaveSince = DateTime.MaxValue;
            else if (_leaveSince == DateTime.MaxValue) _leaveSince = DateTime.Now;
            else if ((DateTime.Now - _leaveSince).TotalMilliseconds > 300) Close();
        }

        Rectangle _petBounds = Rectangle.Empty;
        public Rectangle PetBounds
        {
            get { return _petBounds; }
            set { _petBounds = value; }
        }

        // 气泡体（不含尾巴）区域
        RectangleF BodyRect
        {
            get
            {
                float top = _showArrowBelow ? 0 : 0;
                return new RectangleF(0.5f, top, BubbleW - 1, BodyH - 1);
            }
        }

        // 网格按钮矩形（客户区坐标）
        Rectangle CellRect(int index)
        {
            int col = index % GridCols, row = index / GridCols;
            int x = PadX + col * CellW;
            int y = PadTop + row * CellH;
            return new Rectangle(x, y, CellW - 4, CellH - 4);
        }

        Rectangle ExitRect
        {
            get
            {
                int y = PadTop + CellH * GridRows + PadMid;
                return new Rectangle(PadX, y, BubbleW - PadX * 2, ExitH - 2);
            }
        }

        int HitCell(Point p)
        {
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i].IsExit) continue;
                Rectangle r = CellRect(i);
                r.Inflate(2, 2);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        bool HitExit(Point p)
        {
            Rectangle r = ExitRect;
            r.Inflate(2, 2);
            return r.Contains(p);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int cell = HitCell(e.Location);
            bool ex = HitExit(e.Location);
            if (cell != _hover || ex != _hoverExit)
            {
                _hover = cell;
                _hoverExit = ex;
                Cursor = (cell >= 0 || ex) ? Cursors.Hand : Cursors.Default;
                Render();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; _hoverExit = false;
            Render();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int cell = HitCell(e.Location);
            if (cell >= 0)
            {
                BubbleItem it = _items[cell];
                Close();
                if (ItemClicked != null) ItemClicked(this, it);
                base.OnMouseDown(e);
                return;
            }
            if (HitExit(e.Location))
            {
                BubbleItem it = _items[_items.Length - 1];   // 退出是最后一个
                Close();
                if (ItemClicked != null) ItemClicked(this, it);
            }
            base.OnMouseDown(e);
        }

        /* ------------------------------ 绘制 ------------------------------ */

        void Render()
        {
            if (!IsHandleCreated) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    Theme.SetupGraphics(g);

                    float bodyY = _showArrowBelow ? 0f : 0f;
                    RectangleF body = new RectangleF(0.5f, bodyY, w - 1, BodyH - 1);

                    DrawBubbleShape(g, body);

                    // 网格按钮
                    for (int i = 0; i < _items.Length; i++)
                    {
                        if (_items[i].IsExit) continue;
                        DrawCellButton(g, i);
                    }

                    // 退出条
                    DrawExitBar(g);

                    // 尾巴
                    DrawTail(g, body);
                }

                LayeredWindowUtil.Present(Handle, bmp, Left, Top);
            }
        }

        // 气泡主体：卡通圆角矩形 + 粗轮廓 + 内高光
        void DrawBubbleShape(Graphics g, RectangleF body)
        {
            // 投影
            Theme.SoftShadow(g, body, 14f, 40);

            // 主体
            Theme.FillRound(g, body, 16f, Color.FromArgb(253, 252, 250));
            Theme.StrokeRound(g, body, 16f, Color.FromArgb(255, 232, 96, 0), 3f);

            // 顶部内高光（卡通感）
            using (GraphicsPath hi = Theme.Round(new RectangleF(body.X + 6, body.Y + 4, body.Width - 12, 12), 6f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                g.FillPath(b, hi);

            // 高光：紧贴描边内侧的白色粗弧线 —— 卡通气泡标准画法，靠深色描边衬托
            using (Pen hl = new Pen(Color.FromArgb(255, 255, 205, 170), 4.5f))
            {
                g.DrawArc(hl, body.X + 7, body.Y + 7, body.Width - 30, body.Height - 34, 200, 65);
            }
        }

        // 尾巴：弯钩形对话气泡尾巴，指向桌宠
        void DrawTail(Graphics g, RectangleF body)
        {
            float cx = TailX + 7;
            Color fill = Color.FromArgb(253, 252, 250);
            Color stroke = Color.FromArgb(255, 232, 96, 0);

            if (!_showArrowBelow)
            {
                // 尾巴在气泡底部，向下弯
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddBezier(
                        new PointF(cx - 9, body.Bottom - 2),
                        new PointF(cx - 5, body.Bottom + 6),
                        new PointF(cx + 2, body.Bottom + TailH - 2),
                        new PointF(cx + 8, body.Bottom + TailH));
                    p.AddLine(new PointF(cx + 8, body.Bottom + TailH), new PointF(cx + 10, body.Bottom - 2));
                    p.CloseFigure();

                    // 只填尾巴下缘与气泡不相交的部分：先填色再描边
                    Region old = g.Clip;
                    using (Region clip = new Region(new RectangleF(0, body.Bottom - 2, w_, TailH + 4)))
                    {
                        g.Clip = clip;
                        g.FillPath(new SolidBrush(fill), p);
                        using (Pen pen = new Pen(stroke, 2.5f))
                        {
                            // 只描外侧两条曲线，不描与气泡相接的横线
                            g.DrawPath(pen, p);
                        }
                    }
                    g.Clip = old;

                    // 重画气泡底边（被尾巴覆盖的部分恢复轮廓连接感）
                    using (Pen pen = new Pen(stroke, 2.5f))
                        g.DrawLine(pen, cx - 9, body.Bottom - 1, cx + 10, body.Bottom - 1);
                }
            }
            else
            {
                // 尾巴在顶部，向上弯（镜像）
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddBezier(
                        new PointF(cx - 9, body.Top + 2),
                        new PointF(cx - 5, body.Top - 6),
                        new PointF(cx + 2, body.Top - TailH + 2),
                        new PointF(cx + 8, body.Top - TailH));
                    p.AddLine(new PointF(cx + 8, body.Top - TailH), new PointF(cx + 10, body.Top + 2));
                    p.CloseFigure();

                    Region old = g.Clip;
                    using (Region clip = new Region(new RectangleF(0, body.Top - TailH - 2, w_, TailH + 4)))
                    {
                        g.Clip = clip;
                        g.FillPath(new SolidBrush(fill), p);
                        using (Pen pen = new Pen(stroke, 2.5f))
                            g.DrawPath(pen, p);
                    }
                    g.Clip = old;

                    using (Pen pen = new Pen(stroke, 2.5f))
                        g.DrawLine(pen, cx - 9, body.Top + 1, cx + 10, body.Top + 1);
                }
            }
        }

        int w_ { get { return ClientSize.Width; } }

        // 像素风图标：粗描边圆角方块底 + 简单像素图形
        void DrawCellButton(Graphics g, int index)
        {
            BubbleItem it = _items[index];
            Rectangle r = CellRect(index);
            bool hot = index == _hover;

            // 悬停抬升
            Rectangle draw = r;
            if (hot) draw.Offset(0, -2);

            // 底板：白底 + 粗描边，悬停变橙
            Color fill = hot ? Theme.AccentSoft : Color.White;
            Color stroke = hot ? Theme.Accent : Color.FromArgb(255, 170, 150, 140);
            Theme.FillRound(g, new RectangleF(draw.X, draw.Y, draw.Width, draw.Height), 10f, fill);
            Theme.StrokeRound(g, new RectangleF(draw.X, draw.Y, draw.Width, draw.Height), 10f, stroke, 2f);

            // 像素图标（居中）
            int iconSize = 26;
            PointF ic = new PointF(
                draw.X + (draw.Width - iconSize) / 2f,
                draw.Y + (draw.Height - iconSize) / 2f - 3);
            Color iconColor = hot ? Theme.Accent : Color.FromArgb(255, 90, 80, 75);
            DrawPixelIcon(g, it.Icon, ic, iconSize, iconColor);

            // 文字（中文在 8px 会糊，用 9.5f 加粗并占足宽度）
            using (Font f = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold))
                Theme.Text(g, it.Text, f, hot ? Theme.Ink : Theme.InkSoft,
                    new RectangleF(draw.X - 4, draw.Bottom - 21, draw.Width + 8, 18), ContentAlignment.MiddleCenter);
        }

        // 像素图形：8×8 逻辑网格放大绘制。'1' = 主色，'2' = 主色 55% 透明（层次）
        void DrawPixelIcon(Graphics g, PixelIcon kind, PointF origin, float size, Color color)
        {
            // 壁纸用矢量画（像素网格画不出山形）
            if (kind == PixelIcon.Wallpaper)
            {
                DrawWallpaperIcon(g, origin, size, color);
                return;
            }
            string[] pattern = PixelPattern(kind);
            float px = size / 8f;
            Color soft = Color.FromArgb(color.A * 55 / 100, color);
            using (SolidBrush b1 = new SolidBrush(color))
            using (SolidBrush b2 = new SolidBrush(soft))
            {
                for (int row = 0; row < 8; row++)
                {
                    if (row >= pattern.Length) break;
                    string line = pattern[row];
                    for (int col = 0; col < 8 && col < line.Length; col++)
                    {
                        char ch = line[col];
                        if (ch == '1')
                            g.FillRectangle(b1, origin.X + col * px, origin.Y + row * px, px + 0.5f, px + 0.5f);
                        else if (ch == '2')
                            g.FillRectangle(b2, origin.X + col * px, origin.Y + row * px, px + 0.5f, px + 0.5f);
                    }
                }
            }
        }

        // 壁纸图标：矢量绘制（相框 + 双三角山 + 太阳）
        void DrawWallpaperIcon(Graphics g, PointF origin, float size, Color color)
        {
            float px = size / 8f;
            Color soft = Color.FromArgb(color.A * 55 / 100, color);

            // 相框
            using (Pen frame = new Pen(color, px * 0.9f))
                g.DrawRectangle(frame, origin.X + px * 0.5f, origin.Y + px * 0.5f, size - px, size - px);

            // 后山（浅色）
            using (SolidBrush b = new SolidBrush(soft))
                g.FillPolygon(b, new PointF[] {
                    new PointF(origin.X + size * 0.32f, origin.Y + size * 0.78f),
                    new PointF(origin.X + size * 0.58f, origin.Y + size * 0.40f),
                    new PointF(origin.X + size * 0.84f, origin.Y + size * 0.78f) });

            // 前山（深色）
            using (SolidBrush b = new SolidBrush(color))
                g.FillPolygon(b, new PointF[] {
                    new PointF(origin.X + size * 0.16f, origin.Y + size * 0.78f),
                    new PointF(origin.X + size * 0.42f, origin.Y + size * 0.34f),
                    new PointF(origin.X + size * 0.68f, origin.Y + size * 0.78f) });

            // 太阳
            g.FillEllipse(new SolidBrush(color), origin.X + size * 0.66f, origin.Y + size * 0.16f, size * 0.18f, size * 0.18f);
        }

        static string[] PixelPattern(PixelIcon kind)
        {
            switch (kind)
            {
                case PixelIcon.Wallpaper:   // 相框：两座山
                    return new string[]
                    {
                        "00000000",
                        "01000010",
                        "01100110",
                        "01111110",
                        "00011000",
                        "01111110",
                        "11111111",
                        "00000000",
                    };
                case PixelIcon.Chart:       // 柱状图
                    return new string[]
                    {
                        "00000000",
                        "00010000",
                        "00010010",
                        "00010110",
                        "00110110",
                        "00110110",
                        "01110111",
                        "00000000",
                    };
                case PixelIcon.Coin:        // 铜钱：圆 + 方孔
                    return new string[]
                    {
                        "00000000",
                        "00111100",
                        "01111110",
                        "01101110",
                        "01101110",
                        "01111110",
                        "00111100",
                        "00000000",
                    };
                case PixelIcon.Brain:       // 灯泡：圆泡体 + 灯座
                    return new string[]
                    {
                        "00111100",
                        "01111110",
                        "01111110",
                        "01111110",
                        "00111100",
                        "00011000",
                        "00011000",
                        "00111100",
                    };
                case PixelIcon.Exit:        // 电源
                    return new string[]
                    {
                        "00000000",
                        "00011000",
                        "00011000",
                        "00111100",
                        "01100110",
                        "01100110",
                        "00111100",
                        "00000000",
                    };
                default:
                    return new string[] { "00000000", "00000000", "00000000", "00000000",
                                          "00000000", "00000000", "00000000", "00000000" };
            }
        }

        // 底部退出条
        void DrawExitBar(Graphics g)
        {
            Rectangle r = ExitRect;
            bool hot = _hoverExit;
            BubbleItem exit = null;
            foreach (BubbleItem it in _items) if (it.IsExit) { exit = it; break; }
            if (exit == null) return;

            Color fill = hot ? Color.FromArgb(255, 253, 235, 232) : Color.FromArgb(255, 248, 246, 243);
            Color stroke = hot ? Theme.Err : Color.FromArgb(255, 225, 220, 215);
            Theme.FillRound(g, new RectangleF(r.X, r.Y, r.Width, r.Height), 8f, fill);
            Theme.StrokeRound(g, new RectangleF(r.X, r.Y, r.Width, r.Height), 8f, stroke, hot ? 1.6f : 1f);

            // 小图标 + 文字
            float iconSize = 12;
            DrawPixelIcon(g, PixelIcon.Exit, new PointF(r.X + 10, r.Y + (r.Height - iconSize) / 2f), iconSize,
                hot ? Theme.Err : Color.FromArgb(255, 150, 130, 125));
            using (Font f = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
                Theme.Text(g, exit.Text, f, hot ? Theme.Err : Theme.Muted,
                    new RectangleF(r.X + 26, r.Y, r.Width - 30, r.Height), ContentAlignment.MiddleLeft);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Render();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            Render();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _closeWatch != null) { _closeWatch.Stop(); _closeWatch.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /* 分层窗口呈现（与桌宠同一套已验证的 DIB 方案） */
    internal static class LayeredWindowUtil
    {
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int CX, CY; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr h);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr h, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi,
            uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        static extern void CopyMemory(IntPtr d, IntPtr s, uint n);

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        public static void Present(IntPtr handle, Bitmap bmp, int x, int y)
        {
            int w = bmp.Width, h = bmp.Height;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);

            BITMAPINFO bmi = new BITMAPINFO();
            bmi.biSize = Marshal.SizeOf(typeof(BITMAPINFO));
            bmi.biWidth = w;
            bmi.biHeight = -h;
            bmi.biPlanes = 1;
            bmi.biBitCount = 32;

            IntPtr bits = IntPtr.Zero;
            IntPtr hBitmap = CreateDIBSection(memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);

            try
            {
                if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero) return;

                Rectangle rect = new Rectangle(0, 0, w, h);
                BitmapData bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = w * 4;
                    for (int yy = 0; yy < h; yy++)
                        CopyMemory(IntPtr.Add(bits, yy * rowBytes), IntPtr.Add(bd.Scan0, yy * bd.Stride), (uint)rowBytes);
                }
                finally { bmp.UnlockBits(bd); }

                IntPtr old = SelectObject(memDc, hBitmap);

                POINT dst = new POINT(); dst.X = x; dst.Y = y;
                SIZE sz = new SIZE(); sz.CX = w; sz.CY = h;
                POINT src = new POINT();
                BLENDFUNCTION bf = new BLENDFUNCTION();
                bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;

                UpdateLayeredWindow(handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref bf, 2);

                SelectObject(memDc, old);
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}

