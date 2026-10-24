// BalancePanel.cs —— 余额查询界面
//
// 列表视图：每个账号一张卡片，显示余额、状态、上次更新时间。
// 编辑视图：填 URL / Key / 取值路径 / 阈值，可「测试」立即查询一次。
// 定时刷新在后台线程跑，不阻塞界面。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MiMoSkin
{
    public sealed class BalancePanel : Panel
    {
        BalanceSource.Store _store;
        System.Windows.Forms.Timer _auto;

        Panel _listView, _editView;
        FlowLayoutPanel _cards;
        Label _listTitle;
        FlatButton _btnAdd, _btnRefreshAll;

        // 编辑控件
        int _editing = -1;
        FlatText _eName, _eUrl, _eKey, _eHeader, _ePrefix, _ePath, _eCurrency, _eLow, _eNote, _eBody;
        Label _eBodyLabel;
        ComboBox _eMethod;
        Label _eTestResult;
        BalanceAccount _draft;

        public BalancePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Dock = DockStyle.Fill;

            _store = BalanceSource.Load();

            BuildListView();
            BuildEditView();
            ShowList();
            RebuildCards();

            _auto = new System.Windows.Forms.Timer();
            _auto.Tick += delegate { RefreshAll(false); };
            ApplyInterval();
        }

        /* ============================== 列表视图 ============================== */

        void BuildListView()
        {
            _listView = new Panel();
            _listView.Dock = DockStyle.Fill;
            _listView.BackColor = Theme.Surface;
            Controls.Add(_listView);

            _listTitle = new Label();
            _listTitle.Text = "余额查询";
            _listTitle.Font = Theme.FontH2;
            _listTitle.ForeColor = Theme.Ink;
            _listTitle.SetBounds(14, 12, 200, 24);
            _listView.Controls.Add(_listTitle);

            _btnAdd = new FlatButton();
            _btnAdd.Kind = ButtonKind.Primary;
            _btnAdd.Text = "＋ 添加账号";
            _btnAdd.Font = Theme.FontTiny;
            _btnAdd.SetBounds(_listView.Width - 116, 10, 102, 28);
            _btnAdd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnAdd.Click += delegate { NewAccount(); };
            _listView.Controls.Add(_btnAdd);

            _btnRefreshAll = new FlatButton();
            _btnRefreshAll.Text = "刷新全部";
            _btnRefreshAll.Font = Theme.FontTiny;
            _btnRefreshAll.SetBounds(_listView.Width - 226, 10, 102, 28);
            _btnRefreshAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefreshAll.Click += delegate { RefreshAll(true); };
            _listView.Controls.Add(_btnRefreshAll);

            ScrollHost scroll = new ScrollHost();
            scroll.SetBounds(0, 48, _listView.Width, _listView.Height - 48 - 30);
            scroll.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            scroll.BackColor = Theme.Surface;
            _listView.Controls.Add(scroll);

            _cards = new FlowLayoutPanel();
            _cards.Dock = DockStyle.Top;
            _cards.FlowDirection = FlowDirection.TopDown;
            _cards.WrapContents = false;
            // 不用 AutoSize：卡片位置由 LayoutCards 手动排，高度自己算
            _cards.AutoSize = false;
            _cards.BackColor = Theme.Surface;
            _cards.Padding = new Padding(0);
            _cards.Resize += delegate { LayoutCards(); };
            scroll.Controls.Add(_cards);

            // 底部：刷新间隔
            Panel foot = new Panel();
            foot.SetBounds(0, _listView.Height - 30, _listView.Width, 30);
            foot.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            foot.BackColor = Theme.Surface;

            Label lh = new Label();
            lh.Text = "自动刷新";
            lh.Font = Theme.FontTiny;
            lh.ForeColor = Theme.Muted;
            lh.SetBounds(14, 6, 62, 18);

            _eInterval = new ComboBox();
            _eInterval.DropDownStyle = ComboBoxStyle.DropDownList;
            _eInterval.Font = Theme.FontTiny;
            _eInterval.SetBounds(78, 3, 90, 24);
            _eInterval.Items.AddRange(new object[] { "关闭", "1 分钟", "5 分钟", "10 分钟", "30 分钟", "1 小时" });
            _eInterval.SelectedIndexChanged += delegate { ApplyInterval(); Save(); };
            foot.Controls.Add(lh);
            foot.Controls.Add(_eInterval);
            _listView.Controls.Add(foot);

            SetIntervalIndex(_store.IntervalMin);
        }

        ComboBox _eInterval;

        void SetIntervalIndex(int minutes)
        {
            int idx = 0;
            if (minutes == 1) idx = 1;
            else if (minutes == 5) idx = 2;
            else if (minutes == 10) idx = 3;
            else if (minutes == 30) idx = 4;
            else if (minutes == 60) idx = 5;
            _eInterval.SelectedIndex = idx;
        }

        void ApplyInterval()
        {
            int[] map = new int[] { 0, 1, 5, 10, 30, 60 };
            int idx = _eInterval.SelectedIndex;
            if (idx < 0) idx = 3;
            _store.IntervalMin = map[idx];

            // _auto 在构造函数末尾才创建，界面构建期间的 SelectedIndexChanged 会先跑这里
            if (_auto == null) return;

            if (_store.IntervalMin <= 0) _auto.Stop();
            else
            {
                _auto.Interval = _store.IntervalMin * 60000;
                _auto.Start();
            }
        }

        // 手动排卡片：宽度跟随容器，纵向堆叠。
        // 不用 Dock=Top —— FlowLayoutPanel(AutoSize) 与子控件 Dock 混用会把高度塌成 0。
        void LayoutCards()
        {
            int w = _cards.ClientSize.Width;
            if (w < 10) w = _listView.ClientSize.Width - 28;
            if (w < 10) return;

            int y = 0;
            foreach (Control k in _cards.Controls)
            {
                BalanceCard bc = k as BalanceCard;
                if (bc != null)
                {
                    k.SetBounds(0, y, w, 84);
                    y += 92;
                }
                else
                {
                    // 空状态提示：保持自身高度
                    k.SetBounds(0, y, w, k.Height);
                    y += k.Height + 8;
                }
            }
            _cards.Height = Math.Max(y, 100);
        }

        void RebuildCards()
        {
            _cards.Controls.Clear();

            if (_store.Accounts.Count == 0)
            {
                Label empty = new Label();
                empty.Text = "还没有账号。点右上角「＋ 添加账号」，填入平台的余额查询接口地址和 API Key。";
                empty.Font = Theme.FontSmall;
                empty.ForeColor = Theme.Muted;
                empty.AutoSize = false;
                empty.Dock = DockStyle.Top;
                empty.Height = 60;
                empty.Padding = new Padding(4, 20, 4, 0);
                _cards.Controls.Add(empty);
                return;
            }

            foreach (BalanceAccount a in _store.Accounts)
            {
                BalanceCard card = new BalanceCard();
                card.Account = a;

                int captured = _store.Accounts.IndexOf(a);
                card.EditClicked += delegate { EditAccount(captured); };
                card.DeleteClicked += delegate { DeleteAccount(captured); };
                card.RefreshClicked += delegate { RefreshOne(captured, true); };

                _cards.Controls.Add(card);
            }
            LayoutCards();
        }

        /* ============================== 编辑视图 ============================== */

        void BuildEditView()
        {
            _editView = new Panel();
            _editView.Dock = DockStyle.Fill;
            _editView.BackColor = Theme.Surface;
            _editView.Visible = false;
            Controls.Add(_editView);

            Label t = new Label();
            t.Name = "editTitle";
            t.Text = "编辑账号";
            t.Font = Theme.FontH2;
            t.ForeColor = Theme.Ink;
            t.SetBounds(14, 12, 200, 24);
            _editView.Controls.Add(t);

            FlatButton bBack = new FlatButton();
            bBack.Text = "← 返回";
            bBack.Font = Theme.FontTiny;
            bBack.SetBounds(_editView.Width - 86, 10, 72, 28);
            bBack.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bBack.Click += delegate { ShowList(); };
            _editView.Controls.Add(bBack);

            ScrollHost scroll = new ScrollHost();
            scroll.SetBounds(0, 46, _editView.Width, _editView.Height - 46 - 44);
            scroll.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            scroll.BackColor = Theme.Surface;
            _editView.Controls.Add(scroll);

            FlowLayoutPanel form = new FlowLayoutPanel();
            form.Dock = DockStyle.Top;
            form.FlowDirection = FlowDirection.TopDown;
            form.WrapContents = false;
            form.AutoSize = true;
            form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            form.BackColor = Theme.Surface;
            form.Padding = new Padding(14, 4, 14, 4);
            scroll.Controls.Add(form);

            int fw = _editView.Width - 28;

            form.Controls.Add(EdLabel("名称", fw));
            _eName = EdText(fw, false); form.Controls.Add(_eName);

            form.Controls.Add(EdLabel("查询接口 URL", fw));
            _eUrl = EdText(fw, false); _eUrl.Placeholder = "https://api.example.com/v1/balance"; form.Controls.Add(_eUrl);

            form.Controls.Add(EdLabel("请求方法", fw));
            _eMethod = new ComboBox();
            _eMethod.DropDownStyle = ComboBoxStyle.DropDownList;
            _eMethod.Font = Theme.FontBody;
            _eMethod.SetBounds(0, 0, 120, 26);
            _eMethod.Items.AddRange(new object[] { "GET", "POST" });
            _eMethod.SelectedIndexChanged += delegate
            {
                if (_draft != null) _draft.Method = _eMethod.SelectedIndex == 1 ? "POST" : "GET";
                bool post = _eMethod.SelectedIndex == 1;
                _eBody.Visible = post;
                if (_eBodyLabel != null) _eBodyLabel.Visible = post;
            };
            Panel mp = EdRow(fw); mp.Controls.Add(_eMethod);
            form.Controls.Add(mp);

            _eBodyLabel = EdLabel("POST 请求体（JSON，可空）", fw);
            form.Controls.Add(_eBodyLabel);
            _eBody = EdText(fw, false); _eBody.Height = 54; _eBody.Multiline2 = true;
            _eBody.Placeholder = "{\"model\":\"gpt\"}";
            form.Controls.Add(_eBody);

            form.Controls.Add(EdLabel("API Key", fw));
            _eKey = EdText(fw, true); form.Controls.Add(_eKey);

            Panel hp = EdRow(fw);
            Panel half = new Panel();
            half.BackColor = Theme.Surface;
            half.SetBounds(0, 0, (fw - 8) / 2, 52);

            Label lh2 = EdLabel("认证头", half.Width);
            _eHeader = EdText(half.Width, false); _eHeader.SetBounds(0, 22, half.Width, 28);
            half.Controls.Add(lh2); half.Controls.Add(_eHeader);

            Panel half2 = new Panel();
            half2.BackColor = Theme.Surface;
            half2.SetBounds((fw - 8) / 2 + 8, 0, (fw - 8) / 2, 52);
            Label lp2 = EdLabel("前缀", half2.Width);
            _ePrefix = EdText(half2.Width, false); _ePrefix.SetBounds(0, 22, half2.Width, 28);
            half2.Controls.Add(lp2); half2.Controls.Add(_ePrefix);

            hp.Controls.Add(half); hp.Controls.Add(half2);
            form.Controls.Add(hp);

            form.Controls.Add(EdLabel("取值路径（留空 = 自动探测）", fw));
            _ePath = EdText(fw, false); _ePath.Placeholder = "data.balance  或  info.items[0].total_balance";
            form.Controls.Add(_ePath);

            Panel cp = EdRow(fw);
            Panel c1 = new Panel(); c1.BackColor = Theme.Surface; c1.SetBounds(0, 0, (fw - 8) / 2, 52);
            Label lc1 = EdLabel("货币符号", c1.Width);
            _eCurrency = EdText(c1.Width, false); _eCurrency.SetBounds(0, 22, c1.Width, 28);
            c1.Controls.Add(lc1); c1.Controls.Add(_eCurrency);

            Panel c2 = new Panel(); c2.BackColor = Theme.Surface; c2.SetBounds((fw - 8) / 2 + 8, 0, (fw - 8) / 2, 52);
            Label lc2 = EdLabel("低额提醒", c2.Width);
            _eLow = EdText(c2.Width, false); _eLow.SetBounds(0, 22, c2.Width, 28); _eLow.Placeholder = "留空不提醒";
            c2.Controls.Add(lc2); c2.Controls.Add(_eLow);

            cp.Controls.Add(c1); cp.Controls.Add(c2);
            form.Controls.Add(cp);

            form.Controls.Add(EdLabel("备注", fw));
            _eNote = EdText(fw, false); form.Controls.Add(_eNote);

            // 测试按钮 + 结果
            Panel tp = EdRow(fw);
            FlatButton bTest = new FlatButton();
            bTest.Text = "测试连接";
            bTest.Width = 90;
            bTest.Height = 30;
            bTest.Font = Theme.FontTiny;
            bTest.Click += delegate { TestNow(); };
            tp.Controls.Add(bTest);

            _eTestResult = new Label();
            _eTestResult.Text = "";
            _eTestResult.Font = Theme.FontTiny;
            _eTestResult.ForeColor = Theme.Muted;
            _eTestResult.AutoSize = false;
            _eTestResult.SetBounds(100, 6, fw - 100, 22);
            tp.Controls.Add(_eTestResult);
            form.Controls.Add(tp);

            // 底部保存
            Panel bottom = new Panel();
            bottom.SetBounds(0, _editView.Height - 44, _editView.Width, 44);
            bottom.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            bottom.BackColor = Theme.Surface;

            FlatButton bSave = new FlatButton();
            bSave.Kind = ButtonKind.Primary;
            bSave.Text = "保存";
            bSave.Width = 110;
            bSave.Height = 32;
            bSave.SetBounds(14, 6, 110, 32);
            bSave.Click += delegate { SaveAccount(); };

            FlatButton bCancel = new FlatButton();
            bCancel.Text = "取消";
            bCancel.Width = 80;
            bCancel.Height = 32;
            bCancel.SetBounds(132, 6, 80, 32);
            bCancel.Click += delegate { ShowList(); };

            bottom.Controls.Add(bSave);
            bottom.Controls.Add(bCancel);
            _editView.Controls.Add(bottom);
        }

        static Label EdLabel(string text, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.FontTiny;
            l.ForeColor = Theme.Muted;
            l.AutoSize = false;
            l.SetBounds(0, 8, w, 16);
            l.Margin = new Padding(0, 0, 0, 2);
            return l;
        }

        static FlowLayoutPanel EdRow(int w)
        {
            FlowLayoutPanel r = new FlowLayoutPanel();
            r.FlowDirection = FlowDirection.LeftToRight;
            r.WrapContents = false;
            r.AutoSize = false;
            r.BackColor = Theme.Surface;
            r.SetBounds(0, 0, w, 56);
            r.Margin = new Padding(0, 0, 0, 4);
            return r;
        }

        FlatText EdText(int w, bool password)
        {
            FlatText t = new FlatText();
            t.SetBounds(0, 0, w, 30);
            t.Password = password;
            t.Font = Theme.FontBody;
            t.Margin = new Padding(0, 0, 0, 6);
            return t;
        }

        // 供测试截图用：直接打开编辑页
        public void OpenEditorForTest() { NewAccount(); }

        void ShowList()
        {
            _editing = -1;
            _draft = null;
            _editView.Visible = false;
            _listView.Visible = true;
            RebuildCards();
        }

        void ShowEdit()
        {
            _listView.Visible = false;
            _editView.Visible = true;
        }

        void NewAccount()
        {
            _editing = -1;
            _draft = new BalanceAccount();
            _draft.Name = "账号 " + (_store.Accounts.Count + 1);
            LoadDraft();
            ShowEdit();
        }

        void EditAccount(int index)
        {
            if (index < 0 || index >= _store.Accounts.Count) return;
            _editing = index;
            BalanceAccount src = _store.Accounts[index];
            _draft = new BalanceAccount();
            _draft.Name = src.Name;
            _draft.Url = src.Url;
            _draft.Method = src.Method;
            _draft.ApiKey = src.ApiKey;
            _draft.AuthHeader = src.AuthHeader;
            _draft.AuthPrefix = src.AuthPrefix;
            _draft.Body = src.Body;
            _draft.Path = src.Path;
            _draft.Label = src.Label;
            _draft.Currency = src.Currency;
            _draft.Low = src.Low;
            _draft.Note = src.Note;
            LoadDraft();
            ShowEdit();
        }

        void LoadDraft()
        {
            _eName.Text = _draft.Name;
            _eUrl.Text = _draft.Url;
            _eMethod.SelectedIndex = _draft.Method == "POST" ? 1 : 0;
            _eBody.Text = _draft.Body;
            _eBody.Visible = _draft.Method == "POST";
            _eKey.Text = _draft.ApiKey;
            _eHeader.Text = _draft.AuthHeader;
            _ePrefix.Text = _draft.AuthPrefix;
            _ePath.Text = _draft.Path;
            _eCurrency.Text = _draft.Currency;
            _eLow.Text = _draft.Low < 0 ? "" : _draft.Low.ToString(CultureInfo.InvariantCulture);
            _eNote.Text = _draft.Note;
            _eTestResult.Text = "";

            Label t = (Label)_editView.Controls["editTitle"];
            t.Text = _editing < 0 ? "添加账号" : "编辑账号";
        }

        void CollectDraft()
        {
            _draft.Name = _eName.Text.Trim();
            _draft.Url = _eUrl.Text.Trim();
            _draft.Method = _eMethod.SelectedIndex == 1 ? "POST" : "GET";
            _draft.Body = _eBody.Text;
            _draft.ApiKey = _eKey.Text.Trim();
            _draft.AuthHeader = _eHeader.Text.Trim();
            _draft.AuthPrefix = _ePrefix.Text;
            _draft.Path = _ePath.Text.Trim();
            _draft.Currency = _eCurrency.Text.Trim();
            _draft.Note = _eNote.Text.Trim();

            double low;
            string lt = _eLow.Text.Trim();
            if (lt.Length == 0) _draft.Low = -1;
            else if (double.TryParse(lt, NumberStyles.Float, CultureInfo.InvariantCulture, out low)) _draft.Low = low;
            else _draft.Low = -1;
        }

        void SaveAccount()
        {
            CollectDraft();
            if (_draft.Url.Length == 0)
            {
                _eTestResult.Text = "请填写查询接口 URL";
                _eTestResult.ForeColor = Theme.Err;
                return;
            }
            if (_draft.Name.Length == 0) _draft.Name = "未命名";

            if (_editing < 0) _store.Accounts.Add(_draft);
            else _store.Accounts[_editing] = _draft;

            Save();
            ShowList();
            RefreshOne(_editing < 0 ? _store.Accounts.Count - 1 : _editing, true);
        }

        void DeleteAccount(int index)
        {
            if (index < 0 || index >= _store.Accounts.Count) return;
            if (MessageBox.Show("删除账号「" + _store.Accounts[index].Name + "」？",
                "余额查询", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _store.Accounts.RemoveAt(index);
            Save();
            RebuildCards();
        }

        void Save()
        {
            ApplyInterval();
            BalanceSource.Save(_store);
        }

        /* ============================== 刷新 ============================== */

        void TestNow()
        {
            CollectDraft();
            if (_draft.Url.Length == 0)
            {
                _eTestResult.Text = "先填 URL";
                _eTestResult.ForeColor = Theme.Err;
                return;
            }

            _eTestResult.Text = "查询中…";
            _eTestResult.ForeColor = Theme.Muted;

            BalanceAccount probe = _draft;
            Thread t = new Thread(delegate()
            {
                BalanceSource.QueryResult r = BalanceSource.Query(probe);
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (r.Ok)
                        {
                            _eTestResult.Text = "✓ 取到 " + r.PathUsed + " = " + r.Value;
                            _eTestResult.ForeColor = Theme.Ok;
                        }
                        else
                        {
                            _eTestResult.Text = "✗ " + Shorten(r.Error, 60);
                            _eTestResult.ForeColor = Theme.Err;
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void RefreshAll(bool manual)
        {
            for (int i = 0; i < _store.Accounts.Count; i++) RefreshOne(i, manual);
        }

        void RefreshOne(int index, bool manual)
        {
            if (index < 0 || index >= _store.Accounts.Count) return;
            BalanceAccount a = _store.Accounts[index];
            if (a.Url.Trim().Length == 0) return;

            Thread t = new Thread(delegate()
            {
                BalanceSource.QueryResult r = BalanceSource.Query(a);
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (r.Ok)
                        {
                            a.HasResult = true;
                            a.LastValue = r.Value;
                            a.LastError = "";
                            a.LastTime = DateTime.Now.Ticks;
                            // 自动探测到的路径回填，方便下次直接用
                            if (a.Path.Trim().Length == 0) a.Path = r.PathUsed;
                        }
                        else
                        {
                            a.LastError = r.Error;
                            a.LastTime = DateTime.Now.Ticks;
                        }
                        RebuildCards();
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static string Shorten(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _auto != null) { _auto.Stop(); _auto.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /* ============================== 账号卡片 ============================== */

    public sealed class BalanceCard : Control
    {
        public BalanceAccount Account;
        public event EventHandler EditClicked;
        public event EventHandler DeleteClicked;
        public event EventHandler RefreshClicked;

        bool _hoverEdit, _hoverDel, _hoverRefresh;

        public BalanceCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 84;
        }

        Rectangle EditRect { get { return new Rectangle(Width - 150, 12, 60, 24); } }
        Rectangle DelRect { get { return new Rectangle(Width - 40, 12, 28, 24); } }
        Rectangle RefreshRect { get { return new Rectangle(Width - 84, 12, 40, 24); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool a = EditRect.Contains(e.Location);
            bool b = DelRect.Contains(e.Location);
            bool c = RefreshRect.Contains(e.Location);
            if (a != _hoverEdit || b != _hoverDel || c != _hoverRefresh)
            {
                _hoverEdit = a; _hoverDel = b; _hoverRefresh = c;
                Cursor = (a || b || c) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverEdit = false; _hoverDel = false; _hoverRefresh = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (EditRect.Contains(e.Location) && EditClicked != null) EditClicked(this, EventArgs.Empty);
            else if (DelRect.Contains(e.Location) && DeleteClicked != null) DeleteClicked(this, EventArgs.Empty);
            else if (RefreshRect.Contains(e.Location) && RefreshClicked != null) RefreshClicked(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            bool low = IsLow();
            bool err = Account != null && Account.LastError.Length > 0 && !Account.HasResult;

            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, 10f, Theme.Surface);
            Theme.StrokeRound(g, r, 10f, low ? Theme.Err : Theme.Line, low ? 1.4f : 1f);

            if (Account == null) return;

            // 名称
            Theme.Text(g, Account.Name, Theme.FontH2, Theme.Ink,
                new RectangleF(14, 12, Width - 200, 20), ContentAlignment.MiddleLeft);

            // 余额 / 状态
            string big;
            Color bigColor;
            if (Account.HasResult)
            {
                big = (Account.Currency.Length > 0 ? Account.Currency : "") + FormatNum(Account.LastValue);
                bigColor = low ? Theme.Err : Theme.Ink;
            }
            else if (Account.LastError.Length > 0)
            {
                big = "查询失败";
                bigColor = Theme.Err;
            }
            else
            {
                big = "未查询";
                bigColor = Theme.Muted;
            }
            Theme.Text(g, big, new Font("Microsoft YaHei UI", 16f, FontStyle.Bold), bigColor,
                new RectangleF(14, 34, Width - 200, 28), ContentAlignment.MiddleLeft);

            // 副信息：路径 + 时间
            StringBuilder sub = new StringBuilder();
            if (Account.HasResult && Account.Path.Length > 0) sub.Append(Account.Path);
            if (Account.LastTime != 0)
            {
                if (sub.Length > 0) sub.Append("  ·  ");
                sub.Append("更新于 ").Append(new DateTime(Account.LastTime).ToString("HH:mm:ss"));
            }
            if (Account.HasResult && Account.LastError.Length > 0)
            {
                if (sub.Length > 0) sub.Append("  ·  ");
                sub.Append("（上次失败：").Append(Clip(Account.LastError, 24)).Append("）");
            }
            Theme.Text(g, sub.ToString(), Theme.FontTiny, Theme.Muted,
                new RectangleF(14, 64, Width - 200, 16), ContentAlignment.MiddleLeft);

            // 按钮
            DrawMini(g, EditRect, "编辑", _hoverEdit);
            DrawMini(g, RefreshRect, "刷新", _hoverRefresh);
            DrawMini(g, DelRect, "✕", _hoverDel, _hoverDel ? Theme.Err : Theme.Muted);
        }

        void DrawMini(Graphics g, Rectangle rect, string text, bool hot)
        {
            DrawMini(g, rect, text, hot, Theme.InkSoft);
        }

        void DrawMini(Graphics g, Rectangle rect, string text, bool hot, Color ink)
        {
            RectangleF rf = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
            if (hot) Theme.FillRound(g, rf, 6f, Theme.Rail);
            Theme.Text(g, text, Theme.FontTiny, hot ? Theme.Ink : ink, rf, ContentAlignment.MiddleCenter);
        }

        bool IsLow()
        {
            if (Account == null || !Account.HasResult || Account.Low < 0) return false;
            double v;
            if (double.TryParse(Account.LastValue, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v < Account.Low;
            return false;
        }

        static string FormatNum(string raw)
        {
            double v;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                if (v >= 100000000) return (v / 100000000.0).ToString("0.##") + " 亿";
                if (v >= 10000) return (v / 10000.0).ToString("0.##") + " 万";
                if (v == Math.Floor(v)) return ((long)v).ToString("#,##0");
                return v.ToString("0.##");
            }
            return raw;
        }

        static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }
}
