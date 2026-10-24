// PanelForm.cs —— 紧凑设置面板
//
// 从桌宠双击打开的小面板（约 360×470）。
// 空间有限，所以用两个标签页分组：「壁纸」「观感」，而不是把四张卡片竖着堆。
// 顶部是角色缩略图 + 当前状态，底部是操作按钮。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MiMoSkin
{
    public sealed class PanelForm : Form
    {
        const int PanelW = 368;
        const int PanelH = 478;
        const int CornerRadius = 15;   // 窗口四角圆角半径

        SkinConfig _cfg;
        string _cfgPath;
        Image _preview;
        Image _avatar;
        string _pendingWallpaper;
        bool _busy;
        bool _loading;

        /* 控件 */
        FlatText _txtWallpaper, _txtFolder, _txtSelector;
        SegmentedControl _segRegion, _segFit;
        NumBox _numScale, _numMaxW;
        FlatSlider _sldPosX, _sldPosY, _sldScrim, _sldPanelLight, _sldPanelDark, _sldBlur;
        Label _lblPosX, _lblPosY, _lblScrim, _lblPanelLight, _lblPanelDark, _lblBlur;
        FlatToggle _tglSidebar;
        PreviewBox _previewBox;
        FlatButton _btnApply, _btnRestore, _btnVerify;
        Panel _selectorRow, _sidebarRow;
        TabStrip _tabs;
        Panel _pageWall, _pageLook;
        StatusDot _status;

        public PanelForm()
        {
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _cfgPath = SkinConfig.DefaultPath(exeDir);
            _cfg = SkinConfig.Load(_cfgPath);

            BuildUi();
            LoadIntoUi();
            RefreshPreview();
            UpdateStatus();
        }

        /* ============================== 界面 ============================== */

        void BuildUi()
        {
            Text = "MimoPet";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(PanelW, PanelH);
            BackColor = Theme.Surface;
            ApplyRoundedCorners();
            Font = Theme.FontBody;
            DoubleBuffered = true;
            ShowInTaskbar = true;

            // 顶部：角色缩略图 + 标题 + 状态 + 关闭
            Header head = new Header();
            head.Dock = DockStyle.Top;
            head.Height = 78;
            head.Avatar = LoadAvatar();
            _status = new StatusDot();
            head.Status = _status;
            head.CloseClicked += delegate { Hide(); };

            // 底部按钮
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 56;
            bottom.BackColor = Theme.Surface;
            bottom.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, 14, 0, bottom.Width - 14, 0);
            };

            _btnApply = new FlatButton();
            _btnApply.Kind = ButtonKind.Primary;
            _btnApply.Text = "应用";
            _btnApply.Height = 34;
            _btnApply.Click += delegate { DoApply(true); };

            _btnRestore = new FlatButton();
            _btnRestore.Text = "还原";
            _btnRestore.Height = 34;
            _btnRestore.Click += delegate { DoRestore(); };

            _btnVerify = new FlatButton();
            _btnVerify.Kind = ButtonKind.Ghost;
            _btnVerify.Text = "自检";
            _btnVerify.Height = 34;
            _btnVerify.Click += delegate { DoVerify(); };

            FlowLayoutPanel btnRow = new FlowLayoutPanel();
            btnRow.Dock = DockStyle.Fill;
            btnRow.FlowDirection = FlowDirection.LeftToRight;
            btnRow.WrapContents = false;
            btnRow.Padding = new Padding(14, 11, 14, 0);
            btnRow.BackColor = Theme.Surface;
            _btnApply.Width = 148;
            _btnRestore.Width = 92;
            _btnVerify.Width = 76;
            btnRow.Controls.Add(_btnApply);
            btnRow.Controls.Add(_btnRestore);
            btnRow.Controls.Add(_btnVerify);
            bottom.Controls.Add(btnRow);
            Controls.Add(bottom);

            // 标签页
            _tabs = new TabStrip();
            _tabs.Dock = DockStyle.Top;
            _tabs.Height = 40;
            _tabs.Items = new string[] { "壁纸", "观感" };
            _tabs.SelectedIndexChanged += delegate { SwitchPage(); };
            Controls.Add(_tabs);
            // Dock=Top 的控件后添加者停靠在上方，所以 head 必须在 _tabs 之后加入
            Controls.Add(head);

            // 页面容器
            Panel pages = new Panel();
            pages.Dock = DockStyle.Fill;
            pages.BackColor = Theme.Surface;
            Controls.Add(pages);
            pages.BringToFront();

            _pageWall = new Panel();
            _pageWall.Dock = DockStyle.Fill;
            _pageWall.BackColor = Theme.Surface;
            _pageWall.AutoScroll = false;
            pages.Controls.Add(_pageWall);

            _pageLook = new Panel();
            _pageLook.Dock = DockStyle.Fill;
            _pageLook.BackColor = Theme.Surface;
            _pageLook.Visible = false;
            pages.Controls.Add(_pageLook);

            BuildWallPage();
            BuildLookPage();
            SwitchPage();
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
                    s.Read(buf, 0, buf.Length);
                    return Image.FromStream(new MemoryStream(buf));
                }
            }
            catch { return null; }
        }

        /* ---------- 页 1：壁纸 ---------- */
        void BuildWallPage()
        {
            int y = 8;

            // 预览
            _previewBox = new PreviewBox();
            _previewBox.SetBounds(14, y, PanelW - 28, 168);
            _previewBox.Paint += delegate(object s, PaintEventArgs e) { DrawPreview(e.Graphics); };
            _pageWall.Controls.Add(_previewBox);
            y += 176;

            y = AddRow(_pageWall, y, "图片", out _txtWallpaper, 118, "未选择", true,
                new string[] { "选择", "随机" },
                new EventHandler[] { delegate { PickFile(); }, delegate { PickRandom(); } });

            y = AddRow(_pageWall, y, "文件夹", out _txtFolder, 118, "未指定", true,
                new string[] { "选择", "清除" },
                new EventHandler[] { delegate { PickFolder(); }, delegate { _cfg.WallpaperFolder = ""; _txtFolder.Text = ""; } });

            // 最大宽度
            Panel r3 = new Panel();
            r3.SetBounds(14, y, PanelW - 28, 32);
            r3.BackColor = Theme.Surface;
            Label l3 = MakeLabel("宽度", 40);
            l3.SetBounds(0, 0, 40, 32);
            _numMaxW = new NumBox();
            _numMaxW.Minimum = 300;
            _numMaxW.Maximum = 7680;
            _numMaxW.Step = 100;
            _numMaxW.SetBounds(46, 1, 80, 30);
            _numMaxW.ValueChanged += delegate { SyncCfg(); };
            Label l3b = MakeLabel("px", 30);
            l3b.SetBounds(132, 0, 30, 32);
            r3.Controls.Add(l3);
            r3.Controls.Add(_numMaxW);
            r3.Controls.Add(l3b);
            _pageWall.Controls.Add(r3);
            y += 40;

            // 渲染区域
            Label lr = MakeLabel("壁纸覆盖", 62);
            lr.SetBounds(14, y + 6, 62, 20);
            _pageWall.Controls.Add(lr);

            _segRegion = new SegmentedControl();
            _segRegion.Items = new string[] { "主工作区", "整窗", "侧栏", "内容", "自定义" };
            _segRegion.SetBounds(14, y + 28, PanelW - 28, 32);
            _segRegion.SelectedIndexChanged += delegate { OnRegionChanged(); };
            _pageWall.Controls.Add(_segRegion);
            y += 68;

            _selectorRow = new Panel();
            _selectorRow.SetBounds(14, y, PanelW - 28, 32);
            _selectorRow.BackColor = Theme.Surface;
            _txtSelector = new FlatText();
            _txtSelector.SetBounds(0, 1, PanelW - 28, 30);
            _txtSelector.Placeholder = "CSS 选择器";
            _txtSelector.TextChanged += delegate { _cfg.CustomSelector = _txtSelector.Text; RefreshPreview(); };
            _selectorRow.Controls.Add(_txtSelector);
            _pageWall.Controls.Add(_selectorRow);
            y += 36;

            _sidebarRow = new Panel();
            _sidebarRow.SetBounds(14, y, PanelW - 28, 26);
            _sidebarRow.BackColor = Theme.Surface;
            _tglSidebar = new FlatToggle();
            _tglSidebar.SetBounds(0, 2, 40, 22);
            _tglSidebar.CheckedChanged += delegate { SyncCfg(); RefreshPreview(); };
            Label ls = MakeLabel("侧边栏也透出壁纸", 150);
            ls.SetBounds(48, 3, 150, 20);
            _sidebarRow.Controls.Add(_tglSidebar);
            _sidebarRow.Controls.Add(ls);
            _pageWall.Controls.Add(_sidebarRow);
        }

        /* ---------- 页 2：观感 ---------- */
        void BuildLookPage()
        {
            int y = 10;

            // 填充方式
            Label lf = MakeLabel("填充方式", 62);
            lf.SetBounds(14, y, 62, 20);
            _pageLook.Controls.Add(lf);
            y += 22;

            _segFit = new SegmentedControl();
            _segFit.Items = new string[] { "裁切", "完整", "拉伸", "缩放" };
            _segFit.SetBounds(14, y, PanelW - 28 - 74, 32);
            _segFit.SelectedIndexChanged += delegate { OnFitChanged(); };
            _pageLook.Controls.Add(_segFit);

            _numScale = new NumBox();
            _numScale.Minimum = 50;
            _numScale.Maximum = 300;
            _numScale.Step = 5;
            _numScale.SetBounds(PanelW - 28 - 68, y + 1, 68, 30);
            _numScale.ValueChanged += delegate { SyncCfg(); RefreshPreview(); };
            _pageLook.Controls.Add(_numScale);
            y += 42;

            _sldPosX = new FlatSlider();
            _sldPosX.Minimum = 0; _sldPosX.Maximum = 100;
            _sldPosX.ValueChanged += delegate { SyncCfg(); RefreshPreview(); };
            _lblPosX = MakeValue("50%");
            y = AddSlider(_pageLook, y, "水平", _sldPosX, _lblPosX);

            _sldPosY = new FlatSlider();
            _sldPosY.Minimum = 0; _sldPosY.Maximum = 100;
            _sldPosY.ValueChanged += delegate { SyncCfg(); RefreshPreview(); };
            _lblPosY = MakeValue("50%");
            y = AddSlider(_pageLook, y, "垂直", _sldPosY, _lblPosY);

            // 快捷位置
            Panel qp = new Panel();
            qp.SetBounds(14, y, PanelW - 28, 32);
            qp.BackColor = Theme.Surface;
            string[] qs = new string[] { "居中", "顶部", "底部", "左侧", "右侧" };
            int[] qx = new int[] { 50, 50, 50, 0, 100 };
            int[] qy = new int[] { 50, 0, 100, 50, 50 };
            int bw = (PanelW - 28 - 24) / 5;
            for (int i = 0; i < qs.Length; i++)
            {
                int k = i;
                FlatButton b = new FlatButton();
                b.Text = qs[i];
                b.Font = Theme.FontTiny;
                b.SetBounds(i * (bw + 6), 1, bw, 28);
                b.Click += delegate { _sldPosX.Value = qx[k]; _sldPosY.Value = qy[k]; };
                qp.Controls.Add(b);
            }
            _pageLook.Controls.Add(qp);
            y += 40;

            Label sep = MakeLabel("观感", 40);
            sep.SetBounds(14, y, 40, 16);
            sep.ForeColor = Theme.Muted;
            _pageLook.Controls.Add(sep);
            y += 16;

            _sldScrim = new FlatSlider();
            _sldScrim.Minimum = 0; _sldScrim.Maximum = 100;
            _sldScrim.ValueChanged += delegate { SyncCfg(); RefreshPreview(); };
            _lblScrim = MakeValue("10%");
            y = AddSlider(_pageLook, y, "遮罩", _sldScrim, _lblScrim);

            _sldPanelLight = new FlatSlider();
            _sldPanelLight.Minimum = 0; _sldPanelLight.Maximum = 100;
            _sldPanelLight.ValueChanged += delegate { SyncCfg(); RefreshPreview(); };
            _lblPanelLight = MakeValue("55%");
            y = AddSlider(_pageLook, y, "浅色", _sldPanelLight, _lblPanelLight);

            _sldPanelDark = new FlatSlider();
            _sldPanelDark.Minimum = 0; _sldPanelDark.Maximum = 100;
            _sldPanelDark.ValueChanged += delegate { SyncCfg(); RefreshPreview(); };
            _lblPanelDark = MakeValue("65%");
            y = AddSlider(_pageLook, y, "深色", _sldPanelDark, _lblPanelDark);

            _sldBlur = new FlatSlider();
            _sldBlur.Minimum = 0; _sldBlur.Maximum = 40;
            _sldBlur.ValueChanged += delegate { SyncCfg(); RefreshPreview(); };
            _lblBlur = MakeValue("0px");
            y = AddSlider(_pageLook, y, "模糊", _sldBlur, _lblBlur);
        }

        /* ---------- 布局辅助 ---------- */

        int AddRow(Panel host, int y, string label, out FlatText field, int fieldW,
            string placeholder, bool readOnly, string[] buttons, EventHandler[] handlers)
        {
            Panel r = new Panel();
            r.SetBounds(14, y, PanelW - 28, 32);
            r.BackColor = Theme.Surface;

            Label l = MakeLabel(label, 40);
            l.SetBounds(0, 0, 40, 32);
            r.Controls.Add(l);

            FlatText t = new FlatText();
            t.SetBounds(46, 1, fieldW, 30);
            t.Placeholder = placeholder;
            t.ReadOnlyText = readOnly;
            r.Controls.Add(t);
            field = t;

            int bx = 46 + fieldW + 6;
            for (int i = 0; i < buttons.Length; i++)
            {
                FlatButton b = new FlatButton();
                b.Text = buttons[i];
                b.Font = Theme.FontTiny;
                b.SetBounds(bx, 1, 54, 28);
                b.Click += handlers[i];
                r.Controls.Add(b);
                bx += 58;
            }

            host.Controls.Add(r);
            return y + 40;
        }

        int AddSlider(Panel host, int y, string label, FlatSlider s, Label val)
        {
            Panel r = new Panel();
            r.SetBounds(14, y, PanelW - 28, 30);
            r.BackColor = Theme.Surface;

            Label l = MakeLabel(label, 38);
            l.SetBounds(0, 0, 38, 30);
            r.Controls.Add(l);

            s.SetBounds(42, 2, PanelW - 28 - 42 - 50, 26);
            r.Controls.Add(s);

            val.SetBounds(PanelW - 28 - 46, 0, 46, 30);
            r.Controls.Add(val);

            host.Controls.Add(r);
            return y + 30;
        }

        Label MakeLabel(string text, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Width = w;
            l.Height = 30;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.ForeColor = Theme.InkSoft;
            l.Font = Theme.FontTiny;
            l.BackColor = Color.Transparent;
            return l;
        }

        Label MakeValue(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleRight;
            l.ForeColor = Theme.Ink;
            l.Font = Theme.FontTiny;
            l.BackColor = Color.Transparent;
            return l;
        }

        // 供外部切换标签页（测试与命令行用）
        public void SelectTab(int index)
        {
            _tabs.SelectedIndex = index;
            SwitchPage();
        }

        void SwitchPage()
        {
            bool wall = _tabs.SelectedIndex == 0;
            _pageWall.Visible = wall;
            _pageLook.Visible = !wall;
        }

        /* ============================== 状态同步 ============================== */

        void LoadIntoUi()
        {
            _loading = true;
            try
            {
                _txtWallpaper.Text = ShortPath(_cfg.WallpaperPath);
                _txtFolder.Text = ShortPath(_cfg.WallpaperFolder);

                int ri = 0;
                if (_cfg.Region == "window") ri = 1;
                else if (_cfg.Region == "sidebar") ri = 2;
                else if (_cfg.Region == "content") ri = 3;
                else if (_cfg.Region == "custom") ri = 4;
                _segRegion.SelectedIndex = ri;

                _txtSelector.Text = _cfg.CustomSelector;

                int fi = 0;
                if (_cfg.Fit == "contain") fi = 1;
                else if (_cfg.Fit == "fill") fi = 2;
                else if (_cfg.Fit == "scale") fi = 3;
                _segFit.SelectedIndex = fi;

                _numScale.Value = Clamp(_cfg.ScalePercent, 50, 300);
                _sldPosX.Value = Clamp(_cfg.PosX, 0, 100);
                _sldPosY.Value = Clamp(_cfg.PosY, 0, 100);
                _sldScrim.Value = Clamp(_cfg.Scrim, 0, 100);
                _sldPanelLight.Value = Clamp(_cfg.PanelLight, 0, 100);
                _sldPanelDark.Value = Clamp(_cfg.PanelDark, 0, 100);
                _sldBlur.Value = Clamp(_cfg.Blur, 0, 40);
                _numMaxW.Value = Clamp(_cfg.MaxWidth, 300, 7680);
                _tglSidebar.Checked = _cfg.SidebarTranslucent;

                ApplyVisibility();
                UpdateValueLabels();
            }
            finally { _loading = false; }

            LoadPreviewImage();
        }

        static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }

        void ApplyVisibility()
        {
            if (_selectorRow != null) _selectorRow.Visible = _segRegion.SelectedIndex == 4;
            if (_sidebarRow != null) _sidebarRow.Visible = _segRegion.SelectedIndex == 1;
            _numScale.Enabled = _segFit.SelectedIndex == 3;
        }

        void SyncCfg()
        {
            if (_loading) return;

            if (_segRegion.SelectedIndex == 0) _cfg.Region = "main";
            else if (_segRegion.SelectedIndex == 1) _cfg.Region = "window";
            else if (_segRegion.SelectedIndex == 2) _cfg.Region = "sidebar";
            else if (_segRegion.SelectedIndex == 3) _cfg.Region = "content";
            else _cfg.Region = "custom";

            _cfg.CustomSelector = _txtSelector.Text;

            if (_segFit.SelectedIndex == 0) _cfg.Fit = "cover";
            else if (_segFit.SelectedIndex == 1) _cfg.Fit = "contain";
            else if (_segFit.SelectedIndex == 2) _cfg.Fit = "fill";
            else _cfg.Fit = "scale";

            _cfg.ScalePercent = _numScale.Value;
            _cfg.PosX = _sldPosX.Value;
            _cfg.PosY = _sldPosY.Value;
            _cfg.Scrim = _sldScrim.Value;
            _cfg.PanelLight = _sldPanelLight.Value;
            _cfg.PanelDark = _sldPanelDark.Value;
            _cfg.Blur = _sldBlur.Value;
            _cfg.MaxWidth = _numMaxW.Value;
            _cfg.SidebarTranslucent = _tglSidebar.Checked;

            UpdateValueLabels();
        }

        void UpdateValueLabels()
        {
            if (_lblPosX != null) _lblPosX.Text = _sldPosX.Value + "%";
            if (_lblPosY != null) _lblPosY.Text = _sldPosY.Value + "%";
            if (_lblScrim != null) _lblScrim.Text = _sldScrim.Value + "%";
            if (_lblPanelLight != null) _lblPanelLight.Text = _sldPanelLight.Value + "%";
            if (_lblPanelDark != null) _lblPanelDark.Text = _sldPanelDark.Value + "%";
            if (_lblBlur != null) _lblBlur.Text = _sldBlur.Value + "px";
        }

        void OnRegionChanged()
        {
            if (_loading) return;
            ApplyVisibility();
            SyncCfg();
            RefreshPreview();
        }

        void OnFitChanged()
        {
            if (_loading) return;
            ApplyVisibility();
            SyncCfg();
            RefreshPreview();
        }

        /* ============================== 预览 ============================== */

        void LoadPreviewImage()
        {
            if (_preview != null) { _preview.Dispose(); _preview = null; }

            string p = _pendingWallpaper;
            if (string.IsNullOrEmpty(p)) p = _cfg.WallpaperPath;
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return;

            try
            {
                Wallpaper.Result r = Wallpaper.Process(p, 1200);
                if (r.Error != null) return;
                using (MemoryStream ms = new MemoryStream(r.Jpeg))
                {
                    _preview = Image.FromStream(ms);
                }
            }
            catch { }
        }

        void RefreshPreview()
        {
            if (_previewBox != null) _previewBox.Invalidate();
        }

        void DrawPreview(Graphics g)
        {
            PreviewBox box = _previewBox;
            if (box == null || box.Width < 40 || box.Height < 40) return;

            Theme.SetupGraphics(g);

            RectangleF r = new RectangleF(0, 0, box.Width - 1, box.Height - 1);
            Theme.FillRound(g, r, 9f, Theme.Rail);

            float availW = box.Width - 16;
            float availH = box.Height - 16;
            float ratio = Math.Min(availW / 16f, availH / 10f);
            float w = 16 * ratio, h = 10 * ratio;
            RectangleF win = new RectangleF((box.Width - w) / 2f, (box.Height - h) / 2f, w, h);

            PreviewPainter pp = new PreviewPainter();
            pp.Wallpaper = _preview;
            pp.Cfg = _cfg;
            pp.Paint(g, win);
        }

        /* ============================== 选择 ============================== */

        void PickFile()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "选择壁纸";
                d.Filter = "图片|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*";
                if (!string.IsNullOrEmpty(_cfg.WallpaperPath) && File.Exists(_cfg.WallpaperPath))
                    d.InitialDirectory = Path.GetDirectoryName(_cfg.WallpaperPath);
                if (d.ShowDialog(this) != DialogResult.OK) return;

                _pendingWallpaper = d.FileName;
                _cfg.WallpaperPath = d.FileName;
                _txtWallpaper.Text = ShortPath(d.FileName);
                LoadPreviewImage();
                RefreshPreview();
            }
        }

        void PickFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择壁纸文件夹（每次随机挑一张）";
                if (d.ShowDialog(this) != DialogResult.OK) return;

                _cfg.WallpaperFolder = d.SelectedPath;
                _txtFolder.Text = ShortPath(d.SelectedPath);
                PickRandom();
            }
        }

        void PickRandom()
        {
            string folder = _cfg.WallpaperFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            List<string> imgs = Wallpaper.ScanFolder(folder, 4);
            if (imgs.Count == 0) return;

            Random rnd = new Random();
            string pick = imgs[rnd.Next(imgs.Count)];
            _pendingWallpaper = pick;
            _cfg.WallpaperPath = pick;
            _txtWallpaper.Text = ShortPath(pick);
            LoadPreviewImage();
            RefreshPreview();
        }

        /* ============================== 操作 ============================== */

        void DoApply(bool restart)
        {
            if (_busy) return;

            string src = _pendingWallpaper;
            if (string.IsNullOrEmpty(src)) src = _cfg.WallpaperPath;
            if (string.IsNullOrEmpty(src) || !File.Exists(src))
            {
                MessageBox.Show(this, "请先选择一张壁纸。", "MimoPet",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SyncCfg();

            string exeDir = MiMoApp.FindExeDir();
            if (string.IsNullOrEmpty(exeDir)) { Warn("找不到 MiMo Desktop 的安装位置。"); return; }
            string asar = MiMoApp.AsarPath(exeDir);
            if (!File.Exists(asar)) { Warn("找不到 app.asar：\n" + asar); return; }

            if (MiMoApp.RunningCount() > 0 && MiMoApp.IsInsideMiMo())
            {
                DialogResult dr = MessageBox.Show(this,
                    "检测到本程序正运行在 MiMo 内部。\n\n" +
                    "写入壁纸必须先关闭 MiMo，这会同时关掉调用本程序的会话。\n" +
                    "建议改为在资源管理器里双击 MimoPet.exe 运行。\n\n仍要继续吗？",
                    "MimoPet", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return;
            }

            SetBusy(true);
            try
            {
                Wallpaper.Result wr = Wallpaper.Process(src, _cfg.MaxWidth);
                if (wr.Error != null) { Warn("图片处理失败：\n" + wr.Error); return; }

                byte[] cssBytes = new System.Text.UTF8Encoding(false).GetBytes(CssBuilder.Build(_cfg));

                bool wasRunning = MiMoApp.RunningCount() > 0;
                if (MiMoApp.IsZombieState()) MiMoApp.CleanupZombies(delegate { });

                string err;
                if (MiMoApp.RunningCount() > 0 && !MiMoApp.Stop(delegate { }, out err))
                {
                    Warn(err);
                    return;
                }

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
                    if (wasRunning) MiMoApp.Start(exeDir, delegate { });
                    Warn("补丁失败：\n" + ex.Message);
                    return;
                }

                _cfg.Save(_cfgPath);
                if (restart || wasRunning) MiMoApp.Start(exeDir, delegate { });
                UpdateStatus();
            }
            finally { SetBusy(false); }
        }

        void DoRestore()
        {
            if (_busy) return;

            string exeDir = MiMoApp.FindExeDir();
            if (string.IsNullOrEmpty(exeDir)) { Warn("找不到 MiMo 安装位置。"); return; }
            string asar = MiMoApp.AsarPath(exeDir);

            if (!File.Exists(SkinPatcher.BackupOf(asar))) { Warn("找不到原始备份，无法还原。"); return; }
            if (MessageBox.Show(this, "确定要还原 MiMo 的原始外观吗？", "MimoPet",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            SetBusy(true);
            try
            {
                bool wasRunning = MiMoApp.RunningCount() > 0;
                if (MiMoApp.IsZombieState()) MiMoApp.CleanupZombies(delegate { });

                string err;
                if (MiMoApp.RunningCount() > 0 && !MiMoApp.Stop(delegate { }, out err)) { Warn(err); return; }

                SkinPatcher.Restore(asar, delegate { });
                if (wasRunning) MiMoApp.Start(exeDir, delegate { });
                UpdateStatus();
            }
            catch (Exception ex) { Warn("还原失败：\n" + ex.Message); }
            finally { SetBusy(false); }
        }

        void DoVerify()
        {
            if (_busy) return;

            string exeDir = MiMoApp.FindExeDir();
            if (string.IsNullOrEmpty(exeDir)) { Warn("找不到 MiMo 安装位置。"); return; }
            string asar = MiMoApp.AsarPath(exeDir);

            SetBusy(true);
            try
            {
                PatchResult r = SkinPatcher.Verify(asar, delegate { });
                bool ok = r.HashFailures == 0 && r.SizeMatches;
                MessageBox.Show(this,
                    (ok ? "自检通过。" : "自检发现问题。") + "\n\n" +
                    "打包文件 " + r.PackedCount + " 个\n" +
                    "哈希校验失败 " + r.HashFailures + " 个\n" +
                    "皮肤已注入：" + (SkinPatcher.IsPatched(asar) ? "是" : "否"),
                    "MimoPet", MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex) { Warn("自检失败：\n" + ex.Message); }
            finally { SetBusy(false); }
        }

        void Warn(string msg)
        {
            MessageBox.Show(this, msg, "MimoPet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            _btnApply.Enabled = !busy;
            _btnRestore.Enabled = !busy;
            _btnVerify.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            Application.DoEvents();
        }

        void UpdateStatus()
        {
            try
            {
                string exeDir = MiMoApp.FindExeDir();
                bool running = MiMoApp.IsRunning();
                bool patched = false;
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string asar = MiMoApp.AsarPath(exeDir);
                    if (File.Exists(asar)) { try { patched = SkinPatcher.IsPatched(asar); } catch { } }
                }
                if (_status != null)
                {
                    _status.Running = running;
                    _status.Patched = patched;
                    _status.Invalidate();
                }
            }
            catch { }
        }

        static string ShortPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            if (p.Length <= 22) return p;
            return p.Substring(0, 9) + "…" + p.Substring(p.Length - 10);
        }

        /* ---------- 圆角 ---------- */

        // 无边框窗口做圆角用 Region 裁切：系统按区域裁剪整个窗口，
        // 四角之外的点既不可见也不接收鼠标，拖拽和点击不会漏到圆角外。
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

        /* ---------- 面板拖动（无边框窗口需要自己实现） ---------- */

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
                // 只有顶部标题区可以拖动，避免影响下方控件交互
                if (p.Y < 78) m.Result = (IntPtr)HTCAPTION;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 关闭面板不退出程序，只是藏起来（桌宠还在）
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            if (_preview != null) { _preview.Dispose(); _preview = null; }
            if (_avatar != null) { _avatar.Dispose(); _avatar = null; }
            base.OnFormClosing(e);
        }
    }

    /* ============================== 顶部栏 ============================== */

    public sealed class Header : Panel
    {
        public Image Avatar;
        public StatusDot Status;
        public event EventHandler CloseClicked;

        bool _hoverClose;

        public Header()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        Rectangle CloseRect { get { return new Rectangle(Width - 38, 12, 26, 26); } }

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

            using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, ClientRectangle);

            // 角色缩略图（圆形裁切）
            int av = 52;
            RectangleF ar = new RectangleF(14, 13, av, av);
            if (Avatar != null)
            {
                using (GraphicsPath clip = new GraphicsPath())
                {
                    clip.AddEllipse(ar);
                    Region old = g.Clip;
                    g.SetClip(clip);
                    // 取贴图上部（头部），更像个头像
                    float sw = Avatar.Width, sh = Avatar.Height * 0.62f;
                    g.DrawImage(Avatar, ar, new RectangleF(0, 0, sw, sh), GraphicsUnit.Pixel);
                    g.Clip = old;
                }
            }
            else Theme.FillRound(g, ar, av / 2f, Theme.AccentSoft);

            using (Pen p = new Pen(Theme.AccentLine, 1.5f))
            using (GraphicsPath cp = new GraphicsPath())
            {
                cp.AddEllipse(ar);
                g.DrawPath(p, cp);
            }

            // 标题
            Theme.Text(g, "MimoPet", Theme.FontH2, Theme.Ink,
                new RectangleF(78, 18, 200, 20), ContentAlignment.MiddleLeft);

            // 状态
            string st = Status == null ? "" :
                (Status.Running ? "MiMo 运行中" : "MiMo 未运行") + " · " +
                (Status.Patched ? "壁纸已生效" : "原始外观");
            Theme.Text(g, st, Theme.FontTiny, Theme.Muted,
                new RectangleF(78, 38, 220, 18), ContentAlignment.MiddleLeft);

            if (Status != null)
            {
                Color dot = Status.Patched ? Theme.Ok : Theme.Muted;
                using (SolidBrush b = new SolidBrush(dot))
                    g.FillEllipse(b, 78, 40, 5, 5);
            }

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
                g.DrawLine(p, 14, Height - 1, Width - 14, Height - 1);

            base.OnPaint(e);
        }
    }

    public sealed class StatusDot : Control
    {
        public bool Running, Patched;

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
        }
    }

    /* ============================== 标签条 ============================== */

    public sealed class TabStrip : Control
    {
        string[] _items = new string[0];
        int _selected, _hover = -1;

        public event EventHandler SelectedIndexChanged;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Font = Theme.FontBody;
        }

        public string[] Items
        {
            get { return _items; }
            set { _items = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = Math.Max(0, Math.Min(_items.Length - 1, value));
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        int HitTest(Point p)
        {
            if (_items.Length == 0) return -1;
            int w = Width / _items.Length;
            int i = p.X / Math.Max(1, w);
            return (i < 0 || i >= _items.Length) ? -1 : i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0) SelectedIndex = h;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, ClientRectangle);
            if (_items.Length == 0) return;

            int w = Width / _items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                bool sel = i == _selected;
                RectangleF r = new RectangleF(i * w, 0, w, Height - 1);

                if (sel)
                {
                    using (Pen p = new Pen(Theme.Accent, 2.4f))
                        g.DrawLine(p, r.X + 14, Height - 2, r.Right - 14, Height - 2);
                }
                else if (i == _hover)
                {
                    using (Pen p = new Pen(Theme.Line, 2.4f))
                        g.DrawLine(p, r.X + 14, Height - 2, r.Right - 14, Height - 2);
                }

                Theme.Text(g, _items[i], sel ? Theme.FontH2 : Theme.FontBody,
                    sel ? Theme.Ink : Theme.Muted, r, ContentAlignment.MiddleCenter);
            }

            using (Pen p = new Pen(Theme.Line))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }
}
