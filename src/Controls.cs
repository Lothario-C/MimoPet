// Controls.cs —— 自绘控件库
//
// WinForms 默认控件是 Windows 95 观感，与产品气质冲突。
// 这里全部重绘：圆角、细描边、悬停过渡、无系统高光。
// 所有控件都自带 DoubleBuffered，避免拖动滑块时闪烁。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiMoSkin
{
    /* ============================== 按钮 ============================== */

    public enum ButtonKind { Primary, Secondary, Ghost }

    public sealed class FlatButton : Control
    {
        public ButtonKind Kind = ButtonKind.Secondary;
        bool _hover, _down;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.FontBody;
            Height = 32;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            RectangleF r = new RectangleF(0, 0, Width, Height);
            float radius = 7f;
            bool en = Enabled;

            Color fill, stroke, ink;
            if (Kind == ButtonKind.Primary)
            {
                fill = _down ? Theme.AccentActive : (_hover ? Theme.AccentHover : Theme.Accent);
                stroke = fill;
                ink = Color.White;
                if (!en) { fill = Theme.Mix(Theme.Accent, Theme.Surface, 0.62); stroke = fill; ink = Theme.Alpha(Color.White, 190); }
            }
            else if (Kind == ButtonKind.Ghost)
            {
                fill = _down ? Theme.Mix(Theme.Surface, Theme.Line, 0.9)
                     : (_hover ? Theme.Mix(Theme.Surface, Theme.Line, 0.55) : Color.Transparent);
                stroke = Color.Transparent;
                ink = en ? Theme.InkSoft : Theme.Muted;
            }
            else
            {
                fill = _down ? Theme.Mix(Theme.Surface, Theme.Line, 0.85)
                     : (_hover ? Theme.Mix(Theme.Surface, Theme.AccentSoft, 0.55) : Theme.Surface);
                stroke = _hover ? Theme.AccentLine : Theme.Line;
                ink = en ? Theme.Ink : Theme.Muted;
            }

            if (fill.A > 0) Theme.FillRound(g, r, radius, fill);
            if (stroke.A > 0) Theme.StrokeRound(g, r, radius, stroke, 1f);

            Theme.Text(g, Text, Font, ink, r, ContentAlignment.MiddleCenter);
        }
    }

    /* ============================== 分段控件 ============================== */

    // 用于「壁纸覆盖」「填充方式」这类少量互斥选项。
    // 比下拉框更直观：所有选项一目了然，一次点击到位。
    public sealed class SegmentedControl : Control
    {
        string[] _items = new string[0];
        int _selected;
        int _hover = -1;

        public event EventHandler SelectedIndexChanged;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.FontSmall;
            Height = 32;
        }

        public string[] Items
        {
            get { return _items; }
            set { _items = value ?? new string[0]; if (_selected >= _items.Length) _selected = 0; Invalidate(); }
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
            float w = (float)Width / _items.Length;
            int i = (int)(p.X / w);
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
            if (h >= 0 && e.Button == MouseButtons.Left) SelectedIndex = h;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            if (_items.Length == 0) return;

            RectangleF r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, r, 8f, Theme.Rail);

            float segW = (float)Width / _items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                RectangleF seg = new RectangleF(i * segW, 2, segW, Height - 4);

                if (i == _selected)
                {
                    Theme.SoftShadow(g, seg, 6f, 26);
                    Theme.FillRound(g, seg, 6f, Theme.Surface);
                    Theme.StrokeRound(g, seg, 6f, Theme.Line, 1f);
                }
                else if (i == _hover)
                {
                    Theme.FillRound(g, seg, 6f, Theme.Alpha(Theme.Surface, 130));
                }

                Color ink = (i == _selected) ? Theme.Ink : Theme.InkSoft;
                if (!Enabled) ink = Theme.Muted;
                Theme.Text(g, _items[i], (i == _selected ? Theme.FontSmall : Theme.FontSmall),
                    ink, seg, ContentAlignment.MiddleCenter);
            }
        }
    }

    /* ============================== 滑块 ============================== */

    public sealed class FlatSlider : Control
    {
        int _min, _max = 100, _value = 50;
        bool _drag, _hover;
        int _hoverX = -1;

        public event EventHandler ValueChanged;

        const int TrackH = 4;
        const int ThumbR = 8;

        public FlatSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 30;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return _min; } set { _min = value; if (_value < _min) Value = _min; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = Math.Max(value, _min + 1); if (_value > _max) Value = _max; Invalidate(); } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        float TrackLeft { get { return ThumbR + 2; } }
        float TrackRight { get { return Width - ThumbR - 2; } }
        float TrackW { get { return Math.Max(1, TrackRight - TrackLeft); } }

        float ValueToX(int v)
        {
            double t = (double)(v - _min) / Math.Max(1, _max - _min);
            return TrackLeft + (float)(t * TrackW);
        }

        int XToValue(float x)
        {
            double t = (x - TrackLeft) / TrackW;
            t = Math.Max(0, Math.Min(1, t));
            return _min + (int)Math.Round(t * (_max - _min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _drag = true;
                Value = XToValue(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _hoverX = e.X;
            if (_drag) Value = XToValue(e.X);
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _drag = false; _hoverX = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            float cy = Height / 2f;
            float x = ValueToX(_value);

            // 轨道
            RectangleF bg = new RectangleF(TrackLeft, cy - TrackH / 2f, TrackW, TrackH);
            Theme.FillRound(g, bg, TrackH / 2f, Theme.Mix(Theme.Line, Theme.Rail, 0.5));

            // 已填充部分
            if (x > TrackLeft + 0.5f)
            {
                RectangleF fg = new RectangleF(TrackLeft, cy - TrackH / 2f, x - TrackLeft, TrackH);
                Theme.FillRound(g, fg, TrackH / 2f, Enabled ? Theme.Accent : Theme.Muted);
            }

            // 滑块
            float r = _hover || _drag ? ThumbR + 1f : ThumbR;
            RectangleF thumb = new RectangleF(x - r, cy - r, r * 2, r * 2);

            if (_hover || _drag)
                Theme.SoftShadow(g, thumb, r, 40);

            Theme.FillRound(g, thumb, r, Theme.Surface);
            Theme.StrokeRound(g, thumb, r, Enabled ? Theme.Accent : Theme.Muted, 2f);

            // 拖动时在上方显示数值气泡
            if (_drag)
            {
                string s = _value.ToString();
                SizeF ts = Theme.Measure(g, s, Theme.FontNum);
                float bw = ts.Width + 14, bh = 20;
                float bx = Math.Max(0, Math.Min(Width - bw, x - bw / 2f));
                RectangleF bubble = new RectangleF(bx, cy - r - bh - 6, bw, bh);
                Theme.FillRound(g, bubble, 5f, Theme.Ink);
                Theme.Text(g, s, Theme.FontNum, Color.White, bubble, ContentAlignment.MiddleCenter);
            }
        }
    }

    /* ============================== 数字框 ============================== */

    public sealed class NumBox : Control
    {
        int _min, _max = 100, _value;
        bool _hoverUp, _hoverDown, _focus;
        TextBox _edit;

        public event EventHandler ValueChanged;

        public NumBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 30;
            Font = Theme.FontBody;

            _edit = new TextBox();
            _edit.BorderStyle = BorderStyle.None;
            _edit.Font = Theme.FontBody;
            _edit.TextAlign = HorizontalAlignment.Left;
            _edit.BackColor = Theme.SurfaceAlt;
            _edit.ForeColor = Theme.Ink;
            _edit.GotFocus += delegate { _focus = true; Invalidate(); };
            _edit.LostFocus += delegate { _focus = false; Commit(); Invalidate(); };
            _edit.KeyDown += OnEditKey;
            Controls.Add(_edit);
        }

        public int Minimum { get { return _min; } set { _min = value; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = Math.Max(value, _min + 1); Invalidate(); } }
        public int Step = 1;

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _value) { SyncEdit(); return; }
                _value = v;
                SyncEdit();
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        void SyncEdit()
        {
            if (_edit != null && _edit.Text != _value.ToString()) _edit.Text = _value.ToString();
        }

        void Commit()
        {
            int v;
            if (int.TryParse(_edit.Text, out v)) Value = v;
            else SyncEdit();
        }

        void OnEditKey(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Up) { Value = _value + Step; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { Value = _value - Step; e.SuppressKeyPress = true; }
        }

        RectangleF UpRect { get { return new RectangleF(Width - 20, 2, 18, Height / 2f - 2); } }
        RectangleF DownRect { get { return new RectangleF(Width - 20, Height / 2f, 18, Height / 2f - 2); } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_edit != null)
            {
                _edit.SetBounds(9, (Height - _edit.PreferredHeight) / 2, Width - 32, _edit.PreferredHeight);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool u = UpRect.Contains(e.Location), d = DownRect.Contains(e.Location);
            if (u != _hoverUp || d != _hoverDown) { _hoverUp = u; _hoverDown = d; Cursor = (u || d) ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverUp = _hoverDown = false; Cursor = Cursors.IBeam; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (UpRect.Contains(e.Location)) { Value = _value + Step; _edit.Focus(); }
            else if (DownRect.Contains(e.Location)) { Value = _value - Step; _edit.Focus(); }
            else _edit.Focus();
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            RectangleF r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, r, 7f, Theme.SurfaceAlt);
            Theme.StrokeRound(g, r, 7f, _focus ? Theme.Accent : (_hoverUp || _hoverDown ? Theme.LineStrong : Theme.Line), _focus ? 1.5f : 1f);

            // 上下箭头
            DrawArrow(g, UpRect, true, _hoverUp);
            DrawArrow(g, DownRect, false, _hoverDown);
        }

        void DrawArrow(Graphics g, RectangleF r, bool up, bool hot)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float s = 3.2f;
            PointF[] pts = up
                ? new PointF[] { new PointF(cx - s, cy + s * 0.5f), new PointF(cx + s, cy + s * 0.5f), new PointF(cx, cy - s * 0.7f) }
                : new PointF[] { new PointF(cx - s, cy - s * 0.5f), new PointF(cx + s, cy - s * 0.5f), new PointF(cx, cy + s * 0.7f) };
            using (SolidBrush b = new SolidBrush(hot ? Theme.Accent : Theme.Muted))
                g.FillPolygon(b, pts);
        }
    }

    /* ============================== 文本输入 ============================== */

    public sealed class FlatText : Control
    {
        TextBox _edit;
        bool _focus;
        string _placeholder = "";

        public FlatText()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 30;
            Font = Theme.FontBody;

            _edit = new TextBox();
            _edit.BorderStyle = BorderStyle.None;
            _edit.Font = Theme.FontBody;
            _edit.BackColor = Theme.SurfaceAlt;
            _edit.ForeColor = Theme.Ink;
            _edit.GotFocus += delegate { _focus = true; Invalidate(); };
            _edit.LostFocus += delegate { _focus = false; Invalidate(); };
            _edit.TextChanged += delegate { Invalidate(); OnTextChanged(EventArgs.Empty); };
            Controls.Add(_edit);
        }

        public string Placeholder { get { return _placeholder; } set { _placeholder = value ?? ""; Invalidate(); } }

        public override string Text
        {
            get { return _edit == null ? "" : _edit.Text; }
            set { if (_edit != null) _edit.Text = value ?? ""; }
        }

        public bool ReadOnlyText
        {
            get { return _edit.ReadOnly; }
            set { _edit.ReadOnly = value; _edit.BackColor = Theme.SurfaceAlt; }
        }

        // 密码模式：API key 输入时打点显示
        public bool Password
        {
            get { return _edit.UseSystemPasswordChar; }
            set { _edit.UseSystemPasswordChar = value; }
        }

        // 多行输入（POST 请求体这类长内容）
        public bool Multiline2
        {
            get { return _edit.Multiline; }
            set
            {
                _edit.Multiline = value;
                _edit.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_edit != null) _edit.SetBounds(10, (Height - _edit.PreferredHeight) / 2, Width - 20, _edit.PreferredHeight);
        }

        protected override void OnMouseDown(MouseEventArgs e) { _edit.Focus(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            RectangleF r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, r, 7f, Theme.SurfaceAlt);
            Theme.StrokeRound(g, r, 7f, _focus ? Theme.Accent : Theme.Line, _focus ? 1.5f : 1f);

            if (_edit.Text.Length == 0 && _placeholder.Length > 0)
            {
                RectangleF pr = new RectangleF(10, 0, Width - 20, Height);
                Theme.Text(g, _placeholder, Theme.FontBody, Theme.Muted, pr, ContentAlignment.MiddleLeft);
            }
        }
    }

    /* ============================== 开关 ============================== */

    public sealed class FlatToggle : Control
    {
        bool _checked, _hover;

        public event EventHandler CheckedChanged;

        public FlatToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 22;
            Width = 40;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Checked = !_checked; base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            float h = Height;
            RectangleF r = new RectangleF(0, (Height - h) / 2f, Width, h);

            Color track = _checked
                ? (_hover ? Theme.AccentHover : Theme.Accent)
                : (_hover ? Theme.LineStrong : Theme.Mix(Theme.Line, Theme.Rail, 0.4));
            Theme.FillRound(g, r, h / 2f, track);

            float kr = h - 6;
            float kx = _checked ? r.Right - kr - 3 : r.X + 3;
            RectangleF knob = new RectangleF(kx, r.Y + 3, kr, kr);
            Theme.FillRound(g, knob, kr / 2f, Color.White);
        }
    }

    /* ============================== 卡片 ============================== */

    public sealed class Card : Panel
    {
        public string Title = "";

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Padding = new Padding(16, 40, 16, 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);

            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.SoftShadow(g, r, 10f, 18);
            Theme.FillRound(g, r, 10f, Theme.Surface);
            Theme.StrokeRound(g, r, 10f, Theme.Line, 1f);

            if (Title.Length > 0)
            {
                // 标题左侧的橙色短竖条
                Theme.FillRound(g, new RectangleF(16, 17, 3, 13), 1.5f, Theme.Accent);
                Theme.Text(g, Title, Theme.FontH2, Theme.Ink, new RectangleF(26, 14, Width - 42, 20), ContentAlignment.MiddleLeft);
            }

            base.OnPaint(e);
        }
    }

    /* ============================== 滚动宿主 ============================== */

    // 自带细滚动条的容器。子控件按顺序垂直排列，超出可视高度时可滚动。
    public sealed class ScrollHost : Panel
    {
        int _scroll, _hoverBar;
        bool _drag;
        int _dragStartY, _dragStartScroll;

        const int BarW = 7;

        public ScrollHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        int ContentHeight
        {
            get
            {
                int h = 0;
                foreach (Control c in Controls) h += c.Height + c.Margin.Top + c.Margin.Bottom;
                return h;
            }
        }

        int MaxScroll { get { return Math.Max(0, ContentHeight - Height); } }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - e.Delta / 2));
            Layout2();
            Invalidate();
            base.OnMouseWheel(e);
        }

        void Layout2()
        {
            int avail = Width - (MaxScroll > 0 ? BarW + 6 : 0) - Padding.Right - Padding.Left;
            if (avail < 10) avail = 10;

            int y = -_scroll;
            foreach (Control c in Controls)
            {
                if (c.Width != avail) c.Width = avail;
                c.Left = Padding.Left;
                c.Top = Math.Max(0, y + c.Margin.Top + Padding.Top);
                y += c.Height + c.Margin.Top + c.Margin.Bottom;
            }
        }

        // 子 FlowLayoutPanel 按内容撑开高度：它 Dock=None，不会自己长高
        void SyncContentHeight()
        {
            foreach (Control c in Controls)
            {
                FlowLayoutPanel fl = c as FlowLayoutPanel;
                if (fl == null) continue;
                int need = fl.Padding.Top + fl.Padding.Bottom;
                foreach (Control k in fl.Controls) need += k.Height + k.Margin.Top + k.Margin.Bottom;
                if (fl.Height != need) fl.Height = Math.Max(need, 1);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SyncContentHeight();
            _scroll = Math.Min(_scroll, MaxScroll);
            Layout2();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            SyncContentHeight();
            _scroll = Math.Min(_scroll, MaxScroll);
            Layout2();
        }

        Rectangle BarRect()
        {
            int max = MaxScroll;
            if (max <= 0) return Rectangle.Empty;
            int trackH = Height - 8;
            int barH = Math.Max(30, (int)((double)Height / Math.Max(1, ContentHeight) * trackH));
            int y = 4 + (int)((double)_scroll / max * (trackH - barH));
            return new Rectangle(Width - BarW - 3, y, BarW, barH);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle bar = BarRect();
            if (!bar.IsEmpty && new Rectangle(bar.X - 4, bar.Y, bar.Width + 8, bar.Height).Contains(e.Location))
            {
                _drag = true;
                _dragStartY = e.Y;
                _dragStartScroll = _scroll;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Rectangle bar = BarRect();
            _hoverBar = (!bar.IsEmpty && new Rectangle(bar.X - 6, 0, bar.Width + 12, Height).Contains(e.Location)) ? 1 : 0;

            if (_drag && !bar.IsEmpty)
            {
                int trackH = Height - 8;
                int range = Math.Max(1, trackH - bar.Height);
                int dy = e.Y - _dragStartY;
                _scroll = Math.Max(0, Math.Min(MaxScroll, _dragStartScroll + (int)((double)dy / range * MaxScroll)));
                Layout2();
            }
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { _hoverBar = 0; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bar = BarRect();
            if (bar.IsEmpty) return;

            Graphics g = e.Graphics;
            Theme.SetupGraphics(g);
            Color c = _hoverBar == 1 || _drag ? Theme.LineStrong : Theme.Mix(Theme.Line, Theme.Bg, 0.15);
            Theme.FillRound(g, new RectangleF(bar.X, bar.Y, bar.Width, bar.Height), BarW / 2f, c);
        }
    }

    /* ============================== 预览面板 ============================== */

    public sealed class PreviewBox : Panel
    {
        public PreviewBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }
    }
}
