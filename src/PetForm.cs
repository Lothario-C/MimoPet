// PetForm.cs —— 桌宠窗口
//
// 形态：桌面常驻的透明角色贴图。可拖动换位置（位置记忆），悬停轻微跳动，
// 双击打开设置面板，右键菜单可打开面板 / 退出。
//
// 透明实现用 UpdateLayeredWindow（逐像素 alpha），不用 TransparencyKey：
//   · TransparencyKey 只能抠掉单一颜色，贴图边缘的抗锯齿半透明像素会留下洋红毛边
//   · TransparencyKey 对 GDI+ 的半透明绘制不可靠，实测会把整张贴图一起抠掉
//   UpdateLayeredWindow 直接把 32 位 ARGB 位图交给系统合成，边缘平滑、无毛边。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiMoSkin
{
    public sealed class PetForm : Form
    {
        const int TargetHeight = 150;   // 桌宠显示高度（贴图按比例缩放）
        const int HoverLift = 4;        // 悬停时向上浮起的像素，随尺寸等比减小
        const int TickMs = 33;

        Image _pet;
        bool _hover;
        bool _dragging;
        Point _dragOrigin;
        Point _formOrigin;

        double _lift;
        double _liftTarget;

        Timer _anim;
        ToolTip _tip;
        Timer _hoverWatch;          // 悬停计时：够久才弹气泡，避免路过就弹
        BubbleForm _bubble;
        bool _bubbleOpen;

        public event EventHandler OpenPanelRequested;
        public event EventHandler ExitRequested;
        public event EventHandler<int> OpenToolRequested;

        public PetForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Text = "MiMo 桌宠";

            _pet = LoadPetImage();

            int h = TargetHeight;
            int w = _pet != null ? (int)Math.Round(h * (double)_pet.Width / _pet.Height) : h;
            // 留出悬停浮起的空间，避免跳起时被窗口边界裁掉
            ClientSize = new Size(w, h + HoverLift);

            BuildMenu();

            _tip = new ToolTip();
            _tip.SetToolTip(this, "双击打开设置 · 拖动换位置 · 右键更多");

            _anim = new Timer();
            _anim.Interval = TickMs;
            _anim.Tick += OnTick;
            _anim.Start();

            _hoverWatch = new Timer();
            _hoverWatch.Interval = 400;
            _hoverWatch.Tick += OnHoverTick;
        }

        static Image LoadPetImage()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("MiMoSkin.pet.png"))
                {
                    if (s == null) return null;
                    // 复制到内存流：Image.FromStream 会持有流，资源流关闭后图会失效
                    byte[] buf = new byte[s.Length];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = s.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return Image.FromStream(new MemoryStream(buf));
                }
            }
            catch { return null; }
        }

        void BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = Theme.FontBody;
            m.ShowImageMargin = false;

            ToolStripMenuItem open = new ToolStripMenuItem("打开设置");
            open.Click += delegate { if (OpenPanelRequested != null) OpenPanelRequested(this, EventArgs.Empty); };

            ToolStripMenuItem reset = new ToolStripMenuItem("回到屏幕右下角");
            reset.Click += delegate { MoveToDefaultCorner(); };

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { if (ExitRequested != null) ExitRequested(this, EventArgs.Empty); };

            m.Items.Add(open);
            m.Items.Add(reset);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(exit);
            ContextMenuStrip = m;
        }

        /* ------------------------------ 动画 ------------------------------ */

        void OnTick(object sender, EventArgs e)
        {
            _liftTarget = _hover ? HoverLift : 0;

            double d = _liftTarget - _lift;
            if (Math.Abs(d) < 0.15) _lift = _liftTarget;
            else _lift += d * 0.22;

            Render();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            // 400ms 后仍悬停才弹气泡
            _hoverWatch.Stop();
            _hoverWatch.Start();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _hoverWatch.Stop();
            base.OnMouseLeave(e);
        }

        void OnHoverTick(object sender, EventArgs e)
        {
            _hoverWatch.Stop();
            if (_hover && !_bubbleOpen) OpenBubble();
        }

        void OpenBubble()
        {
            if (_bubbleOpen) return;

            BubbleForm.BubbleItem[] items = new BubbleForm.BubbleItem[]
            {
                new BubbleForm.BubbleItem { Text = "壁纸", ToolIndex = 0, Icon = BubbleForm.PixelIcon.Wallpaper },
                new BubbleForm.BubbleItem { Text = "Token", ToolIndex = 1, Icon = BubbleForm.PixelIcon.Chart },
                new BubbleForm.BubbleItem { Text = "余额", ToolIndex = 2, Icon = BubbleForm.PixelIcon.Coin },
                new BubbleForm.BubbleItem { Text = "思考", ToolIndex = 3, Icon = BubbleForm.PixelIcon.Brain },
                new BubbleForm.BubbleItem { Text = "退出", IsExit = true, Icon = BubbleForm.PixelIcon.Exit },
            };

            _bubble = new BubbleForm(items);
            _bubble.PetBounds = Bounds;
            _bubble.ItemClicked += delegate(object s, BubbleForm.BubbleItem it)
            {
                _bubbleOpen = false;
                if (it.IsExit) { if (ExitRequested != null) ExitRequested(this, EventArgs.Empty); }
                else if (OpenToolRequested != null) OpenToolRequested(this, it.ToolIndex);
            };
            _bubble.FormClosed += delegate { _bubbleOpen = false; };
            _bubble.ShowAbove(Bounds);
            _bubbleOpen = true;
        }

        /* ------------------------------ 拖动 ------------------------------ */

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 按下桌宠时收起气泡（若已弹出）
            if (_bubbleOpen && _bubble != null) { try { _bubble.Close(); } catch { } }

            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOrigin = Cursor.Position;
                _formOrigin = Location;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Point now = Cursor.Position;
                Location = new Point(
                    _formOrigin.X + (now.X - _dragOrigin.X),
                    _formOrigin.Y + (now.Y - _dragOrigin.Y));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            if (e.Button == MouseButtons.Left) SavePosition();
            base.OnMouseUp(e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            if (OpenPanelRequested != null) OpenPanelRequested(this, EventArgs.Empty);
            base.OnDoubleClick(e);
        }

        /* ------------------------------ 绘制 ------------------------------ */

        // 用 UpdateLayeredWindow 逐像素合成：把贴图按当前浮起量画到 ARGB 位图，
        // 再整张交给系统。透明区域完全交给 alpha 通道，边缘平滑无毛边。
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
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    if (_pet != null)
                    {
                        int drawH = h - HoverLift;
                        int drawW = (int)Math.Round(drawH * (double)_pet.Width / _pet.Height);

                        float x = (w - drawW) / 2f;
                        float y = (h - drawH) - (float)_lift;

                        g.DrawImage(_pet, new RectangleF(x, y, drawW, drawH));
                    }
                }


                // 关键：不能用 Bitmap.GetHbitmap —— 它产生的 GDI 位图不保留 alpha，
                // 交给 UpdateLayeredWindow 会得到全透明窗口（实测窗口表面全黑/全透明）。
                // 必须用 CreateDIBSection 建一张 32 位 top-down DIB，再自己把 ARGB 拷进去。
                IntPtr screenDc = GetDC(IntPtr.Zero);
                IntPtr memDc = CreateCompatibleDC(screenDc);

                BITMAPINFO bmi = new BITMAPINFO();
                bmi.biSize = Marshal.SizeOf(typeof(BITMAPINFO));
                bmi.biWidth = w;
                bmi.biHeight = -h;          // 负数 = top-down，与 Bitmap 的行序一致
                bmi.biPlanes = 1;
                bmi.biBitCount = 32;
                bmi.biCompression = 0;      // BI_RGB
                bmi.biSizeImage = 0;

                IntPtr bits = IntPtr.Zero;
                IntPtr hBitmap = CreateDIBSection(memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);

                try
                {
                    if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero) return;

                    BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        // 逐行拷贝：DIB 的 stride 可能大于图像宽度
                        int rowBytes = w * 4;
                        for (int y = 0; y < h; y++)
                        {
                            IntPtr src = IntPtr.Add(bd.Scan0, y * bd.Stride);
                            IntPtr dst = IntPtr.Add(bits, y * rowBytes);
                            CopyMemory(dst, src, (uint)rowBytes);
                        }
                    }
                    finally { bmp.UnlockBits(bd); }

                    IntPtr oldBitmap = SelectObject(memDc, hBitmap);

                    POINT topPos = new POINT(Left, Top);
                    SIZE size = new SIZE(w, h);
                    POINT srcPos = new POINT(0, 0);
                    BLENDFUNCTION blend = new BLENDFUNCTION();
                    blend.BlendOp = AC_SRC_OVER;
                    blend.BlendFlags = 0;
                    blend.SourceConstantAlpha = 255;
                    blend.AlphaFormat = AC_SRC_ALPHA;

                    UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size,
                        memDc, ref srcPos, 0, ref blend, ULW_ALPHA);

                    SelectObject(memDc, oldBitmap);
                }
                finally
                {
                    if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                    DeleteDC(memDc);
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 句柄创建时窗口还没定位（Left/Top 为 0），分层窗口的位置参数会错。
            // 显示后必须重绘一次，把正确的坐标交给 UpdateLayeredWindow。
            Render();
            // 再补一次：有些情况下首次 UpdateLayeredWindow 后窗口尚未完成合成
            BeginInvoke(new MethodInvoker(delegate
            {
                Render();
                // 强制置顶：TopMost 属性在某些时机不生效，直接调 SetWindowPos 更可靠
                SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height,
                    SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
            }));
        }

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        // 分层窗口的 ExStyle 必须带 WS_EX_LAYERED
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不出现在 Alt+Tab
                return cp;
            }
        }

        /* ------------------------------ 位置 ------------------------------ */

        public void MoveToDefaultCorner()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 6);
            SavePosition();
        }

        public void RestorePosition(int x, int y)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (x < wa.Left - Width || x > wa.Right || y < wa.Top - Height || y > wa.Bottom)
            {
                MoveToDefaultCorner();
                return;
            }
            Location = new Point(x, y);
        }

        void SavePosition()
        {
            try
            {
                string p = SkinConfig.DefaultPath(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                PetState s = PetState.Load(p);
                s.PetX = Location.X;
                s.PetY = Location.Y;
                s.Save(p);
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_anim != null) { _anim.Stop(); _anim.Dispose(); _anim = null; }
                if (_hoverWatch != null) { _hoverWatch.Stop(); _hoverWatch.Dispose(); _hoverWatch = null; }
                if (_bubble != null) { try { _bubble.Close(); } catch { } _bubble = null; }
                if (_tip != null) { _tip.Dispose(); _tip = null; }
                if (_pet != null) { _pet.Dispose(); _pet = null; }
            }
            base.Dispose(disposing);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        /* ------------------------------ P/Invoke ------------------------------ */

        const int AC_SRC_OVER = 0x00;
        const int AC_SRC_ALPHA = 0x01;
        const int ULW_ALPHA = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
            uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);
    }
}
