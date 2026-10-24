// ToolboxForm.cs —— 工具箱主窗口
//
// 左侧图标栏切换工具页，右侧显示对应面板。
// 单击桌宠弹出，替代原来的设置面板。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MiMoSkin
{
    public sealed class ToolboxForm : Form
    {
        const int BoxW = 520;
        const int BoxH = 580;
        const int SidebarW = 56;
        const int CornerRadius = 15;

        /* 工具定义 */
        static readonly string[] ToolNames = {
            "壁纸", "Token", "余额", "思考"
        };

        Panel _content;
        Panel _sidebar;
        int _currentTool;
        readonly List<Panel> _pages = new List<Panel>();
        readonly List<SidebarBtn> _buttons = new List<SidebarBtn>();

        /* 各工具页 */
        TokenMonitorPanel _tokenPage;
        BalancePanel _balancePage;
        ThinkPanel _thinkPage;
        Panel _wallPage;

        public ToolboxForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(BoxW, BoxH);
            BackColor = Theme.Surface;
            Font = Theme.FontBody;
            DoubleBuffered = true;
            ShowInTaskbar = true;

            BuildUi();
            ApplyRoundedCorners();
        }

        void BuildUi()
        {
            // 顶部标题栏
            TitleBar tb = new TitleBar();
            tb.Dock = DockStyle.Top;
            tb.Height = 56;
            tb.Avatar = LoadAvatar();
            tb.CloseClicked += delegate { Hide(); };
            Controls.Add(tb);

            // 侧边栏
            _sidebar = new Panel();
            _sidebar.Dock = DockStyle.Left;
            _sidebar.Width = SidebarW;
            _sidebar.BackColor = Theme.Rail;
            _sidebar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, SidebarW - 1, 0, SidebarW - 1, Height);
            };
            Controls.Add(_sidebar);

            // 内容区
            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.BackColor = Theme.Surface;
            _content.Padding = new Padding(0);
            Controls.Add(_content);
            _content.BringToFront();

            // 构建各工具页
            BuildPages();

            // 侧边栏按钮
            BuildSidebar();

            // 默认显示第一个
            SwitchTool(0);
        }

        void BuildPages()
        {
            // 壁纸页：复用 PanelForm 的核心内容
            _wallPage = BuildWallpaperPage();
            _pages.Add(_wallPage);

            _tokenPage = new TokenMonitorPanel();
            _pages.Add(_tokenPage);

            _balancePage = new BalancePanel();
            _pages.Add(_balancePage);

            _thinkPage = new ThinkPanel();
            _pages.Add(_thinkPage);
        }

        // 壁纸设置页（简化版，功能完整）
        Panel BuildWallpaperPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Surface;
            page.Dock = DockStyle.Fill;

            Label title = new Label();
            title.Text = "壁纸设置";
            title.Font = Theme.FontH2;
            title.ForeColor = Theme.Ink;
            title.SetBounds(14, 14, 160, 24);
            page.Controls.Add(title);

            // 用原始 PanelForm 的逻辑：直接嵌入一个隐藏的 PanelForm 实例的控件
            // 但为了简单，这里重新构建核心控件
            SkinConfig cfg = SkinConfig.Load(
                SkinConfig.DefaultPath(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)));

            int y = 50;

            // 图片选择
            Label l1 = new Label();
            l1.Text = "图片";
            l1.Font = Theme.FontSmall;
            l1.ForeColor = Theme.InkSoft;
            l1.SetBounds(14, y, 50, 24);
            page.Controls.Add(l1);

            FlatText txtImg = new FlatText();
            txtImg.SetBounds(66, y + 1, 220, 28);
            txtImg.Placeholder = "未选择";
            txtImg.ReadOnlyText = true;
            txtImg.Text = cfg.WallpaperPath.Length > 20
                ? cfg.WallpaperPath.Substring(0, 18) + "…" : cfg.WallpaperPath;
            page.Controls.Add(txtImg);

            FlatButton bPick = new FlatButton();
            bPick.Text = "选择";
            bPick.Width = 52;
            bPick.Height = 28;
            bPick.Font = Theme.FontTiny;
            bPick.SetBounds(294, y + 1, 52, 28);
            bPick.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "图片|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif";
                    if (d.ShowDialog() != DialogResult.OK) return;
                    cfg.WallpaperPath = d.FileName;
                    txtImg.Text = d.FileName.Length > 20 ? d.FileName.Substring(0, 18) + "…" : d.FileName;
                }
            };
            page.Controls.Add(bPick);

            // 渲染区域
            y += 40;
            Label l2 = new Label();
            l2.Text = "壁纸覆盖";
            l2.Font = Theme.FontSmall;
            l2.ForeColor = Theme.InkSoft;
            l2.SetBounds(14, y, 60, 24);
            page.Controls.Add(l2);

            SegmentedControl seg = new SegmentedControl();
            seg.Items = new string[] { "主工作区", "整窗", "侧栏", "内容", "自定义" };
            seg.SetBounds(78, y, 340, 30);
            page.Controls.Add(seg);

            // 填充方式
            y += 40;
            Label l3 = new Label();
            l3.Text = "填充方式";
            l3.Font = Theme.FontSmall;
            l3.ForeColor = Theme.InkSoft;
            l3.SetBounds(14, y, 60, 24);
            page.Controls.Add(l3);

            SegmentedControl segFit = new SegmentedControl();
            segFit.Items = new string[] { "裁切", "完整", "拉伸", "缩放" };
            segFit.SetBounds(78, y, 240, 30);
            page.Controls.Add(segFit);

            // 遮罩
            y += 40;
            Label l4 = new Label();
            l4.Text = "遮罩";
            l4.Font = Theme.FontSmall;
            l4.ForeColor = Theme.InkSoft;
            l4.SetBounds(14, y, 60, 24);
            page.Controls.Add(l4);

            TrackBar trkScrim = new TrackBar();
            trkScrim.Minimum = 0; trkScrim.Maximum = 100; trkScrim.Value = cfg.Scrim;
            trkScrim.TickStyle = TickStyle.None;
            trkScrim.SetBounds(78, y, 280, 28);
            page.Controls.Add(trkScrim);

            // 透明度
            y += 36;
            Label l5 = new Label();
            l5.Text = "面板透明";
            l5.Font = Theme.FontSmall;
            l5.ForeColor = Theme.InkSoft;
            l5.SetBounds(14, y, 60, 24);
            page.Controls.Add(l5);

            TrackBar trkAlpha = new TrackBar();
            trkAlpha.Minimum = 0; trkAlpha.Maximum = 100; trkAlpha.Value = cfg.PanelLight;
            trkAlpha.TickStyle = TickStyle.None;
            trkAlpha.SetBounds(78, y, 280, 28);
            page.Controls.Add(trkAlpha);

            // 应用按钮
            y += 50;
            FlatButton bApply = new FlatButton();
            bApply.Kind = ButtonKind.Primary;
            bApply.Text = "应用并重启 MiMo";
            bApply.Width = 160;
            bApply.Height = 36;
            bApply.SetBounds(14, y, 160, 36);
            bApply.Click += delegate
            {
                SyncAndApplyWallpaper(cfg, seg, segFit, trkScrim, trkAlpha);
            };
            page.Controls.Add(bApply);

            return page;
        }

        // 壁纸「应用并重启」完整流程：同步配置 → 关 MiMo → 注入 asar → 重启。
        // 之前工具箱里这个按钮只保存配置不注入，壁纸当然不出现。
        void SyncAndApplyWallpaper(SkinConfig cfg, SegmentedControl seg,
            SegmentedControl segFit, TrackBar trkScrim, TrackBar trkAlpha)
        {
            Form host = FindForm();

            // 1. 同步全部配置（区域 / 填充 / 观感）
            if (seg.SelectedIndex == 0) cfg.Region = "main";
            else if (seg.SelectedIndex == 1) cfg.Region = "window";
            else if (seg.SelectedIndex == 2) cfg.Region = "sidebar";
            else if (seg.SelectedIndex == 3) cfg.Region = "content";
            else cfg.Region = "custom";

            if (segFit.SelectedIndex == 0) cfg.Fit = "cover";
            else if (segFit.SelectedIndex == 1) cfg.Fit = "contain";
            else if (segFit.SelectedIndex == 2) cfg.Fit = "fill";
            else cfg.Fit = "scale";

            cfg.Scrim = trkScrim.Value;
            cfg.PanelLight = trkAlpha.Value;
            cfg.Save(SkinConfig.DefaultPath(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)));

            // 2. 前置检查
            if (string.IsNullOrEmpty(cfg.WallpaperPath) || !File.Exists(cfg.WallpaperPath))
            {
                MessageBox.Show(host, "请先选择一张壁纸图片。", "壁纸",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool newLayout = WallpaperNewLayout.Available;
            string exeDir = null, asar = null;

            if (newLayout)
            {
                // 新架构：直接写 runtime-res/releases 的散装文件，无需走 asar
            }
            else
            {
                exeDir = MiMoApp.FindExeDir();
                if (string.IsNullOrEmpty(exeDir)) { MessageBox.Show(host, "找不到 MiMo 安装位置。", "壁纸", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                asar = MiMoApp.AsarPath(exeDir);
                if (!File.Exists(asar)) { MessageBox.Show(host, "找不到 app.asar。", "壁纸", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }

            if (MiMoApp.RunningCount() > 0 && MiMoApp.IsInsideMiMo())
            {
                DialogResult dr = MessageBox.Show(host,
                    "本程序正运行在 MiMo 内部。\n应用壁纸需先关闭 MiMo，会中断当前会话。\n\n仍要继续吗？",
                    "壁纸", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                // 3. 处理图片 + 生成 CSS
                Wallpaper.Result wr = Wallpaper.Process(cfg.WallpaperPath, cfg.MaxWidth);
                if (wr.Error != null)
                {
                    MessageBox.Show(host, "图片处理失败：\n" + wr.Error, "壁纸",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                byte[] cssBytes = new System.Text.UTF8Encoding(false).GetBytes(CssBuilder.Build(cfg));

                // 4. 关 MiMo（含托盘与僵尸处理）
                if (MiMoApp.IsZombieState()) MiMoApp.CleanupZombies(delegate { });
                string err;
                if (MiMoApp.RunningCount() > 0 && !MiMoApp.Stop(delegate { }, out err))
                {
                    MessageBox.Show(host, err, "壁纸", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 5. 注入（双架构）
                if (newLayout)
                {
                    string werr = WallpaperNewLayout.Install(cssBytes, wr.Jpeg);
                    if (werr != null)
                    {
                        MessageBox.Show(host, "应用失败：\n" + werr, "壁纸",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                else
                {
                    SkinPatcher.PatchInput inp = new SkinPatcher.PatchInput();
                    inp.AsarPath = asar;
                    inp.Css = cssBytes;
                    inp.Wallpaper = wr.Jpeg;
                    inp.Log = delegate { };
                    try { SkinPatcher.Patch(inp); }
                    catch (Exception ex)
                    {
                        if (File.Exists(SkinPatcher.BackupOf(asar)))
                        {
                            try { SkinPatcher.Restore(asar, delegate { }); } catch { }
                        }
                        MessageBox.Show(host, "应用失败：\n" + ex.Message, "壁纸",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                // 6. 重启 —— 壁纸立刻可见
                if (exeDir != null) MiMoApp.Start(exeDir, delegate { });
                else MiMoApp.Start(MiMoApp.FindExeDir(), delegate { });
            }
            finally { Cursor = Cursors.Default; }
        }

        void BuildSidebar()
        {
            for (int i = 0; i < ToolNames.Length; i++)
            {
                int idx = i;
                SidebarBtn b = new SidebarBtn();
                b.Label = ToolNames[i];
                b.SetBounds(0, 12 + i * 72, SidebarW, 64);
                b.Click += delegate { SwitchTool(idx); };
                _sidebar.Controls.Add(b);
                _buttons.Add(b);
            }
        }

        public void SelectTool(int index) { SwitchTool(index); }

        public void OpenEditorForTest() { _balancePage.OpenEditorForTest(); }

        void SwitchTool(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            _currentTool = index;

            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].Active = (i == index);

            _content.Controls.Clear();
            _content.Controls.Add(_pages[index]);
            _pages[index].Dock = DockStyle.Fill;
            _pages[index].Visible = true;
        }

        Image LoadAvatar()
        {
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("MiMoSkin.pet.png"))
                {
                    if (s == null) return null;
                    byte[] buf = new byte[s.Length];
                    int r = 0;
                    while (r < buf.Length) { int n = s.Read(buf, r, buf.Length - r); if (n <= 0) break; r += n; }
                    return Image.FromStream(new MemoryStream(buf));
                }
            }
            catch { return null; }
        }

        void ApplyRoundedCorners()
        {
            using (GraphicsPath p = Theme.Round(new RectangleF(0, 0, Width, Height), CornerRadius))
            {
                Region old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundedCorners();
        }

        // 无边框拖动
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCAPTION = 2;
            const int HTCLIENT = 1;

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT)
            {
                Point p = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF,
                    (m.LParam.ToInt32() >> 16) & 0xFFFF));
                if (p.Y < 56) m.Result = (IntPtr)HTCAPTION;
            }
        }
    }

    /* ============================== 标题栏 ============================== */

    public sealed class TitleBar : Panel
    {
        public Image Avatar;
        public event EventHandler CloseClicked;

        bool _hoverClose;

        public TitleBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        Rectangle CloseRect { get { return new Rectangle(Width - 38, 14, 26, 26); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = CloseRect.Contains(e.Location);
            if (h != _hoverClose) { _hoverClose = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverClose = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (CloseRect.Contains(e.Location) && CloseClicked != null)
                CloseClicked(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            using (SolidBrush b = new SolidBrush(Theme.Surface))
                g.FillRectangle(b, ClientRectangle);

            // 头像
            int av = 36;
            RectangleF ar = new RectangleF(14, 10, av, av);
            if (Avatar != null)
            {
                using (GraphicsPath clip = new GraphicsPath())
                {
                    clip.AddEllipse(ar);
                    Region old = g.Clip;
                    g.SetClip(clip);
                    float sw = Avatar.Width, sh = Avatar.Height * 0.62f;
                    g.DrawImage(Avatar, ar, new RectangleF(0, 0, sw, sh), GraphicsUnit.Pixel);
                    g.Clip = old;
                }
            }

            // 标题
            Theme.Text(g, "MiMo Toolbox", Theme.FontH2, Theme.Ink,
                new RectangleF(60, 12, 200, 20), ContentAlignment.MiddleLeft);
            Theme.Text(g, "工具箱", Theme.FontTiny, Theme.Muted,
                new RectangleF(60, 32, 200, 18), ContentAlignment.MiddleLeft);

            // 关闭按钮
            Rectangle cr = CloseRect;
            if (_hoverClose)
                Theme.FillRound(g, new RectangleF(cr.X, cr.Y, cr.Width, cr.Height), 6f, Theme.Rail);
            using (Pen p = new Pen(_hoverClose ? Theme.Ink : Theme.Muted, 1.6f))
            {
                int cx = cr.X + cr.Width / 2, cy = cr.Y + cr.Height / 2;
                g.DrawLine(p, cx - 4, cy - 4, cx + 4, cy + 4);
                g.DrawLine(p, cx + 4, cy - 4, cx - 4, cy + 4);
            }

            using (Pen p = new Pen(Theme.Line))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    /* ============================== 侧边栏按钮 ============================== */

    public sealed class SidebarBtn : Control
    {
        public string Label = "";
        bool _active;
        bool _hover;

        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        public SidebarBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            RectangleF r = new RectangleF(0, 0, Width, Height);

            if (_active)
                Theme.FillRound(g, new RectangleF(8, 6, Width - 16, Height - 12), 8f, Theme.AccentSoft);
            else if (_hover)
                Theme.FillRound(g, new RectangleF(8, 6, Width - 16, Height - 12), 8f, Theme.Rail);

            // 图标圆点（用文字代替图标，保持轻量）
            string icon = Label.Substring(0, 1);
            RectangleF ir = new RectangleF(Width / 2f - 14, 8, 28, 28);
            Theme.FillRound(g, ir, 14f, _active ? Theme.Accent : Theme.Mix(Theme.Line, Theme.Rail, 0.5));
            Theme.Text(g, icon, Theme.FontTiny, _active ? Color.White : Theme.Muted,
                ir, ContentAlignment.MiddleCenter);

            // 标签
            Theme.Text(g, Label, Theme.FontTiny, _active ? Theme.Accent : Theme.Muted,
                new RectangleF(0, 38, Width, 16), ContentAlignment.MiddleCenter);
        }
    }
}
