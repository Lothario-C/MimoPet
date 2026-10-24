// TokenMonitor.cs —— Token 用量监控（多网关）
//
// 顶部：网关下拉切换 + 管理（添加本地路径 / 远程 URL+key）。
// 下方：日期 → 模型 → 详情 + 当日占比条，结构沿用。
//
// 数据源见 GatewaySource.cs：
//   local  —— OFM stats.json（支持自定义路径，多个本地网关/多机同步目录都能接）
//   remote —— one-api / new-api 系 /api/data（远程中转站）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MiMoSkin
{
    public sealed class TokenMonitorPanel : Panel
    {
        System.Windows.Forms.Timer _refresh;

        ComboBox _cboGateway, _cboDay;
        Label _lblSource, _lblTotal, _lblRemoteNote;
        FlatButton _btnManage, _btnRefresh;
        ListBox _listModels;
        readonly Dictionary<string, Label> _detail = new Dictionary<string, Label>();
        MiniBar _bar;

        List<GatewayConfig> _gateways;
        SortedDictionary<string, Dictionary<string, ModelDayStats>> _data;
        string _lastGatewayKey = "";

        public TokenMonitorPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Dock = DockStyle.Fill;

            _gateways = GatewayStore.Load();
            BuildUi();
            LoadGatewayData();

            _refresh = new System.Windows.Forms.Timer();
            _refresh.Interval = 10000;
            _refresh.Tick += delegate { LoadGatewayData(); };
            _refresh.Start();
        }

        /* ============================== 界面 ============================== */

        void BuildUi()
        {
            int y = 10;

            Label title = new Label();
            title.Text = "Token 用量";
            title.Font = Theme.FontH2;
            title.ForeColor = Theme.Ink;
            title.SetBounds(14, y, 130, 24);
            Controls.Add(title);

            _btnRefresh = new FlatButton();
            _btnRefresh.Text = "刷新";
            _btnRefresh.Font = Theme.FontTiny;
            _btnRefresh.Width = 56;
            _btnRefresh.Height = 26;
            _btnRefresh.SetBounds(Width - 70, y, 56, 26);
            _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefresh.Click += delegate { LoadGatewayData(); };
            Controls.Add(_btnRefresh);

            _btnManage = new FlatButton();
            _btnManage.Text = "管理";
            _btnManage.Font = Theme.FontTiny;
            _btnManage.Width = 56;
            _btnManage.Height = 26;
            _btnManage.SetBounds(Width - 134, y, 56, 26);
            _btnManage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnManage.Click += delegate { ManageGateways(); };
            Controls.Add(_btnManage);
            y += 34;

            // 网关选择
            Label lg = new Label();
            lg.Text = "网关";
            lg.Font = Theme.FontTiny;
            lg.ForeColor = Theme.Muted;
            lg.SetBounds(14, y + 5, 34, 18);
            Controls.Add(lg);

            _cboGateway = new ComboBox();
            _cboGateway.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboGateway.Font = Theme.FontSmall;
            _cboGateway.SetBounds(52, y, Width - 66, 26);
            _cboGateway.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _cboGateway.SelectedIndexChanged += delegate
            {
                _lastGatewayKey = "";
                LoadGatewayData();
            };
            Controls.Add(_cboGateway);
            y += 36;

            _lblSource = new Label();
            _lblSource.Font = Theme.FontTiny;
            _lblSource.ForeColor = Theme.Muted;
            _lblSource.SetBounds(14, y, Width - 28, 16);
            _lblSource.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_lblSource);
            y += 22;

            // 日期
            Label ld = new Label();
            ld.Text = "日期";
            ld.Font = Theme.FontTiny;
            ld.ForeColor = Theme.Muted;
            ld.SetBounds(14, y + 4, 34, 18);
            Controls.Add(ld);

            _cboDay = new ComboBox();
            _cboDay.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboDay.Font = Theme.FontSmall;
            _cboDay.SetBounds(52, y, 150, 26);
            _cboDay.SelectedIndexChanged += delegate { RefreshModelList(); };
            Controls.Add(_cboDay);

            _lblTotal = new Label();
            _lblTotal.Font = Theme.FontNum;
            _lblTotal.ForeColor = Theme.Accent;
            _lblTotal.TextAlign = ContentAlignment.MiddleRight;
            _lblTotal.SetBounds(Width - 200, y, 186, 26);
            _lblTotal.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(_lblTotal);
            y += 34;

            // 远程数据源说明（one-api 不分输入输出）
            _lblRemoteNote = new Label();
            _lblRemoteNote.Font = Theme.FontTiny;
            _lblRemoteNote.ForeColor = Theme.Warn;
            _lblRemoteNote.SetBounds(14, y, Width - 28, 16);
            _lblRemoteNote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblRemoteNote.Visible = false;
            Controls.Add(_lblRemoteNote);
            y += 20;

            // 模型列表
            Label lm = new Label();
            lm.Text = "模型";
            lm.Font = Theme.FontTiny;
            lm.ForeColor = Theme.Muted;
            lm.SetBounds(14, y, 120, 16);
            Controls.Add(lm);
            y += 20;

            _listModels = new ListBox();
            _listModels.Font = Theme.FontSmall;
            _listModels.BorderStyle = BorderStyle.None;
            _listModels.BackColor = Theme.SurfaceAlt;
            _listModels.ForeColor = Theme.Ink;
            _listModels.SetBounds(8, y, 190, 170);
            _listModels.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _listModels.SelectedIndexChanged += delegate { ShowDetail(); };
            Controls.Add(_listModels);

            int dx = 210, dy = y;
            AddDetail("tokens", "Token（入/出）", dx, ref dy);
            AddDetail("reasoning", "思考 token", dx, ref dy);
            AddDetail("cache", "缓存命中", dx, ref dy);
            AddDetail("calls", "调用 / 失败", dx, ref dy);
            AddDetail("ttft", "平均首 Token", dx, ref dy);
            AddDetail("speed", "生成速度", dx, ref dy);
            dy += 6;

            _bar = new MiniBar();
            _bar.SetBounds(dx, dy, Width - dx - 14, 60);
            _bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_bar);
        }

        void AddDetail(string key, string label, int x, ref int y)
        {
            Label l = new Label();
            l.Text = label;
            l.Font = Theme.FontTiny;
            l.ForeColor = Theme.Muted;
            l.SetBounds(x, y, 110, 18);

            Label v = new Label();
            v.Font = Theme.FontBody;
            v.ForeColor = Theme.Ink;
            v.SetBounds(x, y + 17, Width - x - 14, 20);
            v.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Controls.Add(l);
            Controls.Add(v);
            _detail[key] = v;
            y += 40;
        }

        /* ============================== 网关管理 ============================== */

        void ManageGateways()
        {
            using (GatewayManageForm f = new GatewayManageForm(_gateways))
            {
                if (f.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    _gateways = f.Gateways;
                    GatewayStore.Save(_gateways);
                    ReloadGatewayCombo();
                    LoadGatewayData();
                }
            }
        }

        void ReloadGatewayCombo()
        {
            _cboGateway.Items.Clear();
            foreach (GatewayConfig g in _gateways)
                _cboGateway.Items.Add(g.Name + (g.Kind == "remote" ? "（远程）" : (g.Kind == "openrouter" ? "（OpenRouter）" : "")));
            int sel = Math.Max(0, Math.Min(_gateways.Count - 1, _cboGateway.Items.Count - 1));
            if (_cboGateway.Items.Count > 0) _cboGateway.SelectedIndex = sel;
        }

        GatewayConfig CurrentGateway
        {
            get
            {
                int i = _cboGateway.SelectedIndex;
                return (i >= 0 && i < _gateways.Count) ? _gateways[i] : null;
            }
        }

        /* ============================== 数据加载 ============================== */

        void LoadGatewayData()
        {
            GatewayConfig g = CurrentGateway;
            if (g == null) return;

            // 同一网关且非 remote 时，靠定时器读文件即可；remote 每次都发请求
            string key = g.Kind + "|" + g.Path;
            if (g.Kind == "local" && key == _lastGatewayKey && _data != null)
            {
                RefreshModelList();
                return;
            }

            if (g.Kind == "remote")
            {
                SetSource("查询远程网关…", Theme.Muted);
                GatewayConfig captured = g;
                Thread t = new Thread(delegate()
                {
                    string err;
                    var data = GatewayReader.Read(captured, out err);
                    try
                    {
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            _data = data;
                            _lastGatewayKey = key;
                            ApplyLoaded(captured, err);
                        }));
                    }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
                return;
            }

            // local
            string lerr;
            _data = GatewayReader.Read(g, out lerr);
            _lastGatewayKey = key;
            ApplyLoaded(g, lerr);
        }

        void ApplyLoaded(GatewayConfig g, string err)
        {
            if (err != null && err.Length > 0)
            {
                SetSource("✗ " + err, Theme.Err);
                _cboDay.Items.Clear();
                _listModels.Items.Clear();
                _bar.Data = null;
                _bar.Invalidate();
                return;
            }

            bool remote = g.Kind == "remote";
            _lblRemoteNote.Visible = remote;
            if (remote)
                _lblRemoteNote.Text = "远程中转站只记录输出 token 与调用数，输入/TTFT/速度不可得";

            SetSource((remote ? "远程: " : "本地: ") +
                (string.IsNullOrEmpty(g.Path) ? GatewayStore.DefaultLocalStats : g.Path) +
                "（10 秒自动刷新）", Theme.Muted);

            if (_data == null || _data.Count == 0)
            {
                SetSource((remote ? "远程: " : "本地: ") + g.Path + " —— 暂无数据", Theme.Muted);
                _cboDay.Items.Clear();
                _listModels.Items.Clear();
                _lblTotal.Text = "";
                _bar.Data = null;
                _bar.Invalidate();
                return;
            }

            string[] days = _data.Keys.Reverse().ToArray();
            string prev = _cboDay.SelectedIndex >= 0 && _cboDay.SelectedIndex < _cboDay.Items.Count
                ? (string)_cboDay.Items[_cboDay.SelectedIndex] : null;

            _cboDay.Items.Clear();
            foreach (string d in days) _cboDay.Items.Add(d);
            if (days.Length > 0)
            {
                int idx = prev != null ? Array.IndexOf(days, prev) : 0;
                _cboDay.SelectedIndex = idx >= 0 ? idx : 0;
            }
            RefreshModelList();
        }

        void SetSource(string text, Color c)
        {
            _lblSource.Text = text;
            _lblSource.ForeColor = c;
        }

        /* ============================== 模型列表 / 详情 ============================== */

        void RefreshModelList()
        {
            _listModels.Items.Clear();
            if (_cboDay.SelectedIndex < 0 || _data == null) return;
            string day = (string)_cboDay.SelectedItem;

            Dictionary<string, ModelDayStats> models;
            if (!_data.TryGetValue(day, out models)) return;

            List<KeyValuePair<string, ModelDayStats>> list = models.ToList();
            list.Sort(delegate(KeyValuePair<string, ModelDayStats> a, KeyValuePair<string, ModelDayStats> b)
            {
                return (b.Value.Input + b.Value.Output).CompareTo(a.Value.Input + a.Value.Output);
            });

            foreach (KeyValuePair<string, ModelDayStats> kv in list)
            {
                string shortName = kv.Key.Length > 26 ? "…" + kv.Key.Substring(kv.Key.Length - 25) : kv.Key;
                _listModels.Items.Add(shortName + "  (" + kv.Value.Calls + ")");
            }

            ModelDayStats total = new ModelDayStats();
            foreach (ModelDayStats s in models.Values)
            {
                total.Input += s.Input; total.Output += s.Output;
                total.Reasoning += s.Reasoning; total.CacheRead += s.CacheRead;
            }
            _lblTotal.Text = FormatNum(total.Input) + " / " + FormatNum(total.Output);

            if (_listModels.Items.Count > 0) _listModels.SelectedIndex = 0;
            else { _bar.Data = null; _bar.Invalidate(); }
        }

        void ShowDetail()
        {
            int idx = _listModels.SelectedIndex;
            if (idx < 0 || _cboDay.SelectedIndex < 0 || _data == null) return;
            string day = (string)_cboDay.SelectedItem;

            Dictionary<string, ModelDayStats> models;
            if (!_data.TryGetValue(day, out models)) return;

            List<KeyValuePair<string, ModelDayStats>> list = models.ToList();
            list.Sort(delegate(KeyValuePair<string, ModelDayStats> a, KeyValuePair<string, ModelDayStats> b)
            {
                return (b.Value.Input + b.Value.Output).CompareTo(a.Value.Input + a.Value.Output);
            });
            if (idx >= list.Count) return;

            ModelDayStats s = list[idx].Value;

            bool remoteOnly = s.Input == 0 && s.Output > 0;   // one-api 系数据源无输入记录（openrouter 有完整数据不受影响）
            _detail["tokens"].Text = remoteOnly
                ? "— / " + FormatNum(s.Output)
                : FormatNum(s.Input) + " / " + FormatNum(s.Output);
            _detail["reasoning"].Text = s.Reasoning > 0 ? FormatNum(s.Reasoning) : "—";
            _detail["cache"].Text = s.CacheRead > 0 ? FormatNum(s.CacheRead) : "—";
            _detail["calls"].Text = s.Calls + " 次" + (s.Failed > 0 ? " / 失败 " + s.Failed : "");
            _detail["ttft"].Text = s.TtftSamples > 0 ? FormatMs(s.AvgTtft) : "—";
            _detail["speed"].Text = s.SpeedTps > 0 ? s.SpeedTps.ToString("0.0") + " tok/s" : "—";

            _bar.Day = day;
            _bar.Data = _data;
            _bar.Invalidate();
        }

        static string FormatNum(long v)
        {
            if (v >= 100000000) return (v / 100000000.0).ToString("0.##") + "亿";
            if (v >= 10000) return (v / 10000.0).ToString("0.#") + "万";
            if (v >= 1000) return (v / 1000.0).ToString("0.#") + "K";
            return v.ToString();
        }

        static string FormatMs(double ms)
        {
            if (ms >= 60000) return (ms / 60000.0).ToString("0.0") + " 分";
            if (ms >= 1000) return (ms / 1000.0).ToString("0.0") + " 秒";
            return ms.ToString("0") + " ms";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _refresh != null) { _refresh.Stop(); _refresh.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /* ============================== 网关管理对话框 ============================== */

    public sealed class GatewayManageForm : Form
    {
        public List<GatewayConfig> Gateways;
        ListBox _list;
        FlatText _eName, _ePath, _eKey, _eUser;
        ComboBox _eKind;
        int _editing = -1;

        public GatewayManageForm(List<GatewayConfig> source)
        {
            Text = "网关管理";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 380);
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontBody;
            BackColor = Theme.Surface;

            Gateways = new List<GatewayConfig>();
            foreach (GatewayConfig g in source) Gateways.Add(g.Clone());

            int y = 12;
            Label t = new Label();
            t.Text = "用量数据源";
            t.Font = Theme.FontH2;
            t.ForeColor = Theme.Ink;
            t.SetBounds(14, y, 200, 24);
            Controls.Add(t);
            y += 32;

            _list = new ListBox();
            _list.Font = Theme.FontSmall;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.SetBounds(14, y, 412 - 28, 110);
            _list.SelectedIndexChanged += delegate { LoadSelected(); };
            Controls.Add(_list);
            y += 118;

            Label lk = new Label();
            lk.Text = "类型";
            lk.Font = Theme.FontTiny;
            lk.ForeColor = Theme.Muted;
            lk.SetBounds(14, y + 4, 40, 18);
            Controls.Add(lk);

            _eKind = new ComboBox();
            _eKind.DropDownStyle = ComboBoxStyle.DropDownList;
            _eKind.Font = Theme.FontSmall;
            _eKind.SetBounds(58, y, 130, 26);
            _eKind.Items.AddRange(new object[] { "本地 stats.json", "远程中转站 (one-api/new-api)", "OpenRouter 官方" });
            _eKind.SelectedIndexChanged += delegate { OnKindChanged(); };
            Controls.Add(_eKind);
            y += 34;

            Label ln = new Label();
            ln.Text = "名称";
            ln.Font = Theme.FontTiny;
            ln.ForeColor = Theme.Muted;
            ln.SetBounds(14, y + 4, 40, 18);
            Controls.Add(ln);

            _eName = new FlatText();
            _eName.SetBounds(58, y, 300, 28);
            Controls.Add(_eName);
            y += 36;

            Label lp = new Label();
            lp.Text = "路径/URL";
            lp.Font = Theme.FontTiny;
            lp.ForeColor = Theme.Muted;
            lp.SetBounds(14, y + 4, 60, 18);
            Controls.Add(lp);

            _ePath = new FlatText();
            _ePath.SetBounds(78, y, 280, 28);
            Controls.Add(_ePath);

            FlatButton bBrowse = new FlatButton();
            bBrowse.Text = "…";
            bBrowse.Font = Theme.FontTiny;
            bBrowse.Width = 32;
            bBrowse.Height = 28;
            bBrowse.SetBounds(364, y, 32, 28);
            bBrowse.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "stats.json|stats.json|所有文件|*.*";
                    d.Title = "选择网关的 stats.json";
                    if (d.ShowDialog(this) == DialogResult.OK) _ePath.Text = d.FileName;
                }
            };
            Controls.Add(bBrowse);
            y += 36;

            Label la = new Label();
            la.Text = "API Key";
            la.Font = Theme.FontTiny;
            la.ForeColor = Theme.Muted;
            la.SetBounds(14, y + 4, 60, 18);
            Controls.Add(la);

            _eKey = new FlatText();
            _eKey.SetBounds(78, y, 280, 28);
            _eKey.Password = true;
            Controls.Add(_eKey);
            y += 36;

            Label lu = new Label();
            lu.Text = "用户 ID";
            lu.Font = Theme.FontTiny;
            lu.ForeColor = Theme.Muted;
            lu.SetBounds(14, y + 4, 60, 18);
            Controls.Add(lu);

            _eUser = new FlatText();
            _eUser.SetBounds(78, y, 280, 28);
            Controls.Add(_eUser);
            y += 42;

            // 底部按钮
            FlatButton bAdd = new FlatButton();
            bAdd.Text = "＋ 新增";
            bAdd.Font = Theme.FontTiny;
            bAdd.Width = 70;
            bAdd.Height = 30;
            bAdd.SetBounds(14, y, 70, 30);
            bAdd.Click += delegate { NewItem(); };
            Controls.Add(bAdd);

            FlatButton bDel = new FlatButton();
            bDel.Text = "删除";
            bDel.Font = Theme.FontTiny;
            bDel.Width = 60;
            bDel.Height = 30;
            bDel.SetBounds(90, y, 60, 30);
            bDel.Click += delegate { DeleteItem(); };
            Controls.Add(bDel);

            FlatButton bTest = new FlatButton();
            bTest.Text = "测试";
            bTest.Font = Theme.FontTiny;
            bTest.Width = 60;
            bTest.Height = 30;
            bTest.SetBounds(156, y, 60, 30);
            bTest.Click += delegate { TestItem(); };
            Controls.Add(bTest);

            FlatButton bOk = new FlatButton();
            bOk.Kind = ButtonKind.Primary;
            bOk.Text = "确定";
            bOk.Width = 76;
            bOk.Height = 30;
            bOk.SetBounds(426 - 90, y, 76, 30);
            bOk.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bOk.Click += delegate { SaveAndClose(); };
            Controls.Add(bOk);

            // 测试结果行
            Label res = new Label();
            res.Name = "testResult";
            res.Font = Theme.FontTiny;
            res.ForeColor = Theme.Muted;
            res.SetBounds(14, y + 36, 412 - 28, 18);
            Controls.Add(res);

            ReloadList();
        }

        Label TestResult
        {
            get { return (Label)Controls["testResult"]; }
        }

        void ReloadList()
        {
            _list.Items.Clear();
            foreach (GatewayConfig g in Gateways)
                _list.Items.Add(g.Name + (g.Kind == "remote" ? "（远程）" : (g.Kind == "openrouter" ? "（OpenRouter）" : "（本地）")));
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        void LoadSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= Gateways.Count) return;
            _editing = i;
            GatewayConfig g = Gateways[i];
            _eKind.SelectedIndex = g.Kind == "remote" ? 1 : (g.Kind == "openrouter" ? 2 : 0);
            _eName.Text = g.Name;
            _ePath.Text = g.Path;
            _eKey.Text = g.ApiKey;
            _eUser.Text = g.Username;
            OnKindChanged();
        }

        void OnKindChanged()
        {
            int kind = _eKind.SelectedIndex;
            bool remote = kind == 1;
            bool openrouter = kind == 2;
            _eKey.Visible = remote || openrouter;
            _eUser.Visible = remote;
            bool pathVisible = !openrouter;
            _ePath.Visible = pathVisible;
            // 找 path 的浏览按钮（在 _ePath 旁边）一并隐藏
            foreach (Control ctl in Controls)
                if (ctl is FlatButton && ctl.Text == "…") ctl.Visible = pathVisible;

            if (openrouter) _eKey.Placeholder = "Management Key（设置里的管理密钥）";
            else if (_eKey.Placeholder.Length > 0) _eKey.Placeholder = "";

            if (remote && _ePath.Text.Length == 0)
                _ePath.Placeholder = "https://your-relay.example.com";
            else if (!remote && !openrouter && _ePath.Text.Length == 0)
                _ePath.Placeholder = GatewayStore.DefaultLocalStats;
        }

        void Collect()
        {
            if (_editing < 0) return;
            GatewayConfig g = Gateways[_editing];
            g.Name = _eName.Text.Trim();
            g.Kind = _eKind.SelectedIndex == 1 ? "remote" : (_eKind.SelectedIndex == 2 ? "openrouter" : "local");
            g.Path = _ePath.Text.Trim();
            g.ApiKey = _eKey.Text.Trim();
            g.Username = _eUser.Text.Trim();
            if (g.Kind == "local" && g.Path.Length == 0) g.Path = GatewayStore.DefaultLocalStats;
        }

        void NewItem()
        {
            Collect();
            GatewayConfig g = new GatewayConfig();
            g.Name = "网关 " + (Gateways.Count + 1);
            g.Kind = _eKind.SelectedIndex == 1 ? "remote" : (_eKind.SelectedIndex == 2 ? "openrouter" : "local");
            g.Path = GatewayStore.DefaultLocalStats;
            Gateways.Add(g);
            ReloadList();
            _list.SelectedIndex = Gateways.Count - 1;
        }

        void DeleteItem()
        {
            if (_editing < 0) return;
            if (Gateways.Count <= 1)
            {
                MessageBox.Show(this, "至少保留一个网关。", "网关管理",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Gateways.RemoveAt(_editing);
            _editing = -1;
            ReloadList();
        }

        void TestItem()
        {
            Collect();
            if (_editing < 0) return;
            GatewayConfig probe = Gateways[_editing].Clone();

            TestResult.Text = "查询中…";
            TestResult.ForeColor = Theme.Muted;

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                string err;
                var data = GatewayReader.Read(probe, out err);
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (err != null && err.Length > 0)
                        {
                            TestResult.Text = "✗ " + (err.Length > 60 ? err.Substring(0, 60) + "…" : err);
                            TestResult.ForeColor = Theme.Err;
                        }
                        else
                        {
                            int days = data == null ? 0 : data.Count;
                            TestResult.Text = "✓ 连接成功，" + days + " 天数据";
                            TestResult.ForeColor = Theme.Ok;
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void SaveAndClose()
        {
            Collect();
            foreach (GatewayConfig g in Gateways)
            {
                if (g.Name.Length == 0) g.Name = "未命名";
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // 当日各模型 token 占比横条（沿用）
    public sealed class MiniBar : Control
    {
        public string Day;
        public SortedDictionary<string, Dictionary<string, ModelDayStats>> Data;

        public MiniBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.SurfaceAlt;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);
            Theme.FillRound(g, new RectangleF(0, 0, Width, Height), 6f, Theme.SurfaceAlt);

            if (Data == null || Day == null) return;
            Dictionary<string, ModelDayStats> models;
            if (!Data.TryGetValue(Day, out models) || models.Count == 0) return;

            List<ModelDayStats> list = models.Values.ToList();
            list.Sort(delegate(ModelDayStats a, ModelDayStats b)
            {
                return (b.Input + b.Output).CompareTo(a.Input + a.Output);
            });

            long total = 0;
            foreach (ModelDayStats s in list) total += s.Input + s.Output;
            if (total <= 0) return;

            float x = 8, w = Width - 16, y = 6, h = 18;
            Color[] palette = new Color[] {
                Theme.Accent, Color.FromArgb(255, 90, 140, 200), Color.FromArgb(255, 120, 180, 120),
                Color.FromArgb(255, 190, 150, 80), Color.FromArgb(255, 160, 120, 190),
            };

            int n = Math.Min(5, list.Count);
            for (int i = 0; i < n; i++)
            {
                float seg = (float)((list[i].Input + list[i].Output) / (double)total) * w;
                if (seg < 1) continue;
                Theme.FillRound(g, new RectangleF(x, y, seg, h), 4f, palette[i % palette.Length]);
                x += seg;
            }

            float ly = y + h + 8, lx = 8;
            for (int i = 0; i < n; i++)
            {
                string name = list[i].Model;
                int slash = name.LastIndexOf('/');
                if (slash >= 0 && slash < name.Length - 1) name = name.Substring(slash + 1);
                string text = name + " " +
                    ((list[i].Input + list[i].Output) * 100.0 / total).ToString("0") + "%";

                SizeF sz = Theme.Measure(g, text, Theme.FontTiny);
                if (lx + sz.Width + 18 > Width) break;

                using (SolidBrush b = new SolidBrush(palette[i % palette.Length]))
                    g.FillRectangle(b, lx, ly + 3, 8, 8);
                Theme.Text(g, text, Theme.FontTiny, Theme.InkSoft,
                    new RectangleF(lx + 12, ly, sz.Width + 4, 16), ContentAlignment.MiddleLeft);
                lx += sz.Width + 26;
            }
        }
    }
}
