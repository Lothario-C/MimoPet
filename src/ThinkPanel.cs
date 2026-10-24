// ThinkPanel.cs —— 思考强度面板
//
// 核心：一键给 MiMo 打「思考强度补丁」（think-patch.mjs，来自已验证成功的
// mimo-think-effort 项目）。打完补丁并重启后，MiMo 模型选择器里会原生出现
// 每个模型按真实声明的思考挡位子菜单 —— 用户直接在桌面端里选，无需桌宠介入。
//
// 补丁改的是 releases 下的 renderer/引擎/校验文件（启动时不重算哈希，安全），
// 不碰 index.html（那个会触发完整性告警）。
//
// 面板还提供：补丁状态、重放（MiMo 更新后）、卸载、当前模型挡位、挡位预查询。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MiMoSkin
{
    public sealed class ThinkPanel : Panel
    {
        Label _lblStatus, _lblModel, _lblHint;
        LevelScale _currentScale;
        FlatText _txtProbe;
        LevelScale _probeScale;
        Label _lblProbe;
        FlatButton _btnInstall, _btnReplay, _btnUndo;
        bool _busy;
        bool? _appliedCache;

        public ThinkPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Dock = DockStyle.Fill;

            BuildUi();
            RefreshModel();
            CheckStatusAsync(false);
        }

        /* ============================== 界面 ============================== */

        void BuildUi()
        {
            int y = 14;

            Label title = new Label();
            title.Text = "思考强度";
            title.Font = Theme.FontH2;
            title.ForeColor = Theme.Ink;
            title.SetBounds(14, y, 200, 24);
            Controls.Add(title);
            y += 30;

            // 状态徽章
            _lblStatus = new Label();
            _lblStatus.Font = Theme.FontSmall;
            _lblStatus.AutoSize = false;
            _lblStatus.SetBounds(14, y, Width - 28, 22);
            _lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblStatus.Text = "检测中…";
            _lblStatus.ForeColor = Theme.Muted;
            Controls.Add(_lblStatus);
            y += 30;

            // 说明
            Label desc = new Label();
            desc.Text = "一键启用后，在 MiMo 模型选择器里会原生显示每个模型的思考挡位，直接点选即可。";
            desc.Font = Theme.FontTiny;
            desc.ForeColor = Theme.InkSoft;
            desc.AutoSize = false;
            desc.SetBounds(14, y, Width - 28, 34);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(desc);
            y += 40;

            // 三个按钮
            _btnInstall = new FlatButton();
            _btnInstall.Kind = ButtonKind.Primary;
            _btnInstall.Text = "启用思考强度";
            _btnInstall.Width = 120;
            _btnInstall.Height = 34;
            _btnInstall.SetBounds(14, y, 120, 34);
            _btnInstall.Click += delegate { DoInstall(false); };

            _btnReplay = new FlatButton();
            _btnReplay.Text = "重放补丁";
            _btnReplay.Width = 92;
            _btnReplay.Height = 34;
            _btnReplay.SetBounds(142, y, 92, 34);
            _btnReplay.Click += delegate { DoInstall(true); };

            _btnUndo = new FlatButton();
            _btnUndo.Kind = ButtonKind.Ghost;
            _btnUndo.Text = "卸载";
            _btnUndo.Width = 68;
            _btnUndo.Height = 34;
            _btnUndo.SetBounds(242, y, 68, 34);
            _btnUndo.Click += delegate { DoUndo(); };

            Controls.Add(_btnInstall);
            Controls.Add(_btnReplay);
            Controls.Add(_btnUndo);
            y += 48;

            // 分隔线
            Panel sep = new Panel();
            sep.SetBounds(14, y, Width - 28, 1);
            sep.BackColor = Theme.Line;
            sep.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(sep);
            y += 16;

            // 当前模型
            Label cur = new Label();
            cur.Text = "当前模型";
            cur.Font = Theme.FontTiny;
            cur.ForeColor = Theme.Muted;
            cur.SetBounds(14, y, 120, 18);
            Controls.Add(cur);
            y += 20;

            _lblModel = new Label();
            _lblModel.Font = Theme.FontBody;
            _lblModel.ForeColor = Theme.Ink;
            _lblModel.AutoSize = false;
            _lblModel.SetBounds(14, y, Width - 28, 20);
            _lblModel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_lblModel);
            y += 24;

            _currentScale = new LevelScale();
            _currentScale.SetBounds(14, y, Width - 28, 36);
            _currentScale.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_currentScale);
            y += 42;

            _lblHint = new Label();
            _lblHint.Font = Theme.FontTiny;
            _lblHint.ForeColor = Theme.Muted;
            _lblHint.AutoSize = false;
            _lblHint.SetBounds(14, y, Width - 28, 18);
            _lblHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_lblHint);
            y += 30;

            // 提前查看
            Label t2 = new Label();
            t2.Text = "提前查看";
            t2.Font = Theme.FontH2;
            t2.ForeColor = Theme.Ink;
            t2.SetBounds(14, y, 200, 24);
            Controls.Add(t2);
            y += 30;

            _txtProbe = new FlatText();
            _txtProbe.SetBounds(14, y, Width - 28, 30);
            _txtProbe.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtProbe.Placeholder = "输入模型名，看它支持哪些挡位";
            _txtProbe.TextChanged += delegate { ProbeChanged(); };
            Controls.Add(_txtProbe);
            y += 38;

            _lblProbe = new Label();
            _lblProbe.Font = Theme.FontTiny;
            _lblProbe.ForeColor = Theme.Muted;
            _lblProbe.AutoSize = false;
            _lblProbe.SetBounds(14, y, Width - 28, 18);
            _lblProbe.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_lblProbe);
            y += 24;

            _probeScale = new LevelScale();
            _probeScale.SetBounds(14, y, Width - 28, 36);
            _probeScale.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_probeScale);
        }

        /* ============================== 状态检测 ============================== */

        void CheckStatusAsync(bool showOutput)
        {
            if (_busy) return;
            SetStatus("检测补丁状态…", Theme.Muted);

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                ThinkPatch.RunResult r = ThinkPatch.Run("verify", 40000);
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        _appliedCache = r.Ok;
                        if (r.Ok) SetStatus("● 已启用 —— 在 MiMo 里选模型即可看到挡位菜单", Theme.Ok);
                        else SetStatus("○ 未启用 —— 点「启用思考强度」安装", Theme.Muted);
                        if (showOutput) Log(r.Output);
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void SetStatus(string text, Color c)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = c;
        }

        /* ============================== 安装 / 重放 / 卸载 ============================== */

        void DoInstall(bool replay)
        {
            if (_busy) return;

            Form host = FindForm();
            string head = replay ? "重放补丁" : "启用思考强度";

            // 前置：node 可用性
            if (ThinkPatch.FindNode() == null)
            {
                MessageBox.Show(host,
                    "找不到 Node.js。补丁器需要 Node 18+ 运行。\n" +
                    "请安装 Node.js，或确认 MiMo 自带运行时可用。",
                    head, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string note = replay
                ? "重放会先剥离旧注入再按最新档位表重新注入（MiMo 更新后需要）。\n\n需要重启 MiMo 生效。继续吗？"
                : "将在 releases 资源里注入思考强度支持（不碰 index.html，不触发完整性告警）。\n\n需要重启 MiMo 生效。继续吗？";

            if (MessageBox.Show(host, note, head,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            SetBusy(true);
            try
            {
                // 关 MiMo（补丁写引擎文件，运行中可能被占用；且需要重启生效）
                if (MiMoApp.IsZombieState()) MiMoApp.CleanupZombies(Log);
                string stopErr;
                bool wasRunning = MiMoApp.RunningCount() > 0;
                if (wasRunning && !MiMoApp.Stop(Log, out stopErr))
                {
                    MessageBox.Show(host, stopErr, head, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // gen（重抓档位表）+ apply
                Log("抓取网关档位表…");
                ThinkPatch.RunResult gen = ThinkPatch.Run("gen", 60000);
                Log(gen.Output);

                Log("打补丁…");
                ThinkPatch.RunResult ap = ThinkPatch.Run("apply", 60000);
                Log(ap.Output);

                if (!ap.Ok)
                {
                    MessageBox.Show(host, "补丁失败：\n" + ap.Output, head,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    // 重启
                    MiMoApp.Start(MiMoApp.FindExeDir(), Log);
                    _appliedCache = true;
                    SetStatus("● 已启用 —— 重启完成后，在 MiMo 里选模型即可看到挡位菜单", Theme.Ok);
                    MessageBox.Show(host,
                        "已启用。MiMo 重启完成后，在模型选择器里选模型，\n右侧会出现该模型的思考挡位菜单。",
                        head, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally { SetBusy(false); }
        }

        void DoUndo()
        {
            if (_busy) return;
            Form host = FindForm();

            if (MessageBox.Show(host,
                "卸载补丁，把 releases 文件还原为官方原始版本。\n\n需要重启 MiMo。继续吗？",
                "卸载思考强度", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            SetBusy(true);
            try
            {
                if (MiMoApp.IsZombieState()) MiMoApp.CleanupZombies(Log);
                string stopErr;
                bool wasRunning = MiMoApp.RunningCount() > 0;
                if (wasRunning && !MiMoApp.Stop(Log, out stopErr))
                {
                    MessageBox.Show(host, stopErr, "卸载", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                ThinkPatch.RunResult un = ThinkPatch.Run("undo", 60000);
                Log(un.Output);
                if (!un.Ok)
                    MessageBox.Show(host, "卸载失败：\n" + un.Output, "卸载",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);

                MiMoApp.Start(MiMoApp.FindExeDir(), Log);
                _appliedCache = false;
                SetStatus("○ 已卸载", Theme.Muted);
            }
            finally { SetBusy(false); }
        }

        /* ============================== 当前模型 / 预查询 ============================== */

        void RefreshModel()
        {
            string model = ThinkSource.ReadCurrentModel();
            _lblModel.Text = string.IsNullOrEmpty(model) ? "（未读取到）" : model;

            string[] levels = ThinkSource.EffortsFor(model);
            _currentScale.SetLevels(levels, null);

            if (levels.Length > 0)
                _lblHint.Text = "该模型支持 " + levels.Length + " 挡：" + JoinLabels(levels) + "（在 MiMo 里点选）";
            else
                _lblHint.Text = "该模型无多档强度（可能是开关型思考，或不在档位表里）";
        }

        void ProbeChanged()
        {
            string q = _txtProbe.Text.Trim();
            if (q.Length == 0)
            {
                _probeScale.SetLevels(new string[0], null);
                _lblProbe.Text = "";
                return;
            }

            string[] levels = ThinkSource.EffortsFor(q);
            if (levels.Length > 0)
            {
                _lblProbe.Text = "支持 " + levels.Length + " 挡：" + JoinLabels(levels);
                _lblProbe.ForeColor = Theme.Ok;
                _probeScale.SetLevels(levels, null);
            }
            else
            {
                _lblProbe.Text = "没查到。试试完整名（含 workbuddy/ 等前缀）";
                _lblProbe.ForeColor = Theme.Muted;
                _probeScale.SetLevels(new string[0], null);
            }
        }

        static string JoinLabels(string[] levels)
        {
            string[] parts = new string[levels.Length];
            for (int i = 0; i < levels.Length; i++) parts[i] = ThinkSource.LevelLabel(levels[i]);
            return string.Join(" / ", parts);
        }

        /* ============================== 杂项 ============================== */

        void SetBusy(bool busy)
        {
            _busy = busy;
            _btnInstall.Enabled = !busy;
            _btnReplay.Enabled = !busy;
            _btnUndo.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            Application.DoEvents();
        }

        void Log(string msg)
        {
            if (msg == null) return;
            // 输出到最近窗口的标题栏下方状态；这里简单刷新状态行末段
            if (msg.Length > 0)
            {
                // 不弹窗刷屏，只在状态行显示最后一句
                string[] ls = msg.Split('\n');
                if (ls.Length > 0) SetStatus(("● " + ls[ls.Length - 1]).Trim(), Theme.InkSoft);
            }
        }


    }

    // 挡位刻度条：横向一排格子
    public sealed class LevelScale : Control
    {
        string[] _levels = new string[0];
        string _active;
        int _hover = -1;

        public bool Clickable;
        public event EventHandler<string> LevelClicked;

        public void SetLevels(string[] levels, string active)
        {
            _levels = levels ?? new string[0];
            _active = active;
            _hover = -1;
            Invalidate();
        }

        public LevelScale()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 36;
            Cursor = Cursors.Hand;
        }

        int HitTest(Point p)
        {
            if (_levels.Length == 0 || !Clickable) return -1;
            int gap = 6;
            float w = (Width - gap * (_levels.Length - 1)) / (float)_levels.Length;
            int i = (int)(p.X / (w + gap));
            if (i < 0 || i >= _levels.Length) return -1;
            float cellX = i * (w + gap);
            return p.X >= cellX && p.X <= cellX + w ? i : -1;
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
            if (h >= 0 && LevelClicked != null) LevelClicked(this, _levels[h]);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            if (_levels.Length == 0)
            {
                Theme.Text(g, "—", Theme.FontTiny, Theme.Muted,
                    new RectangleF(0, 0, Width, Height), ContentAlignment.MiddleLeft);
                return;
            }

            int gap = 6;
            float w = (Width - gap * (_levels.Length - 1)) / (float)_levels.Length;

            for (int i = 0; i < _levels.Length; i++)
            {
                float x = i * (w + gap);
                bool on = _levels[i] == _active;

                RectangleF r = new RectangleF(x, 2, w, Height - 4);
                bool hov = i == _hover;
                Color fill = on ? Theme.Accent : (hov ? Theme.Mix(Theme.Rail, Theme.Accent, 0.25) : Theme.Rail);
                Theme.FillRound(g, r, 7f, fill);
                if (on) Theme.StrokeRound(g, r, 7f, Theme.Accent, 1.4f);

                Theme.Text(g, ThinkSource.LevelLabel(_levels[i]), Theme.FontTiny,
                    on ? Color.White : Theme.InkSoft, r, ContentAlignment.MiddleCenter);
            }
        }
    }
}
