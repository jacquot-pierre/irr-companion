using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>Flat slider drawn like the in-game menu.</summary>
    public sealed class Slider : Control
    {
        public double Min = 0, Max = 1, Increment = 0.01;
        public int Precision = 2;
        private double _value;
        private bool _drag;
        public bool ReadOnly;
        public event EventHandler ValueChanged;

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Row;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public double Value
        {
            get { return _value; }
            set { _value = value; Invalidate(); }
        }

        private Rectangle TrackRect
        {
            get
            {
                int pad = Theme.S(6);
                return new Rectangle(pad, Height / 2 - Theme.S(2), Math.Max(1, Width - 2 * pad), Theme.S(4));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var r = TrackRect;
            using (var b = new SolidBrush(Theme.Track)) g.FillRectangle(b, r);
            double t = Max > Min ? (_value - Min) / (Max - Min) : 0;
            t = Math.Max(0, Math.Min(1, t));
            int w = (int)Math.Round(r.Width * t);
            var fill = ReadOnly ? Theme.TextDim : (Focused || _drag ? Color.White : Theme.Accent);
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r.X, r.Y, w, r.Height);
            if (!ReadOnly)
            {
                int x = r.X + w;
                using (var b = new SolidBrush(fill))
                    g.FillRectangle(b, x - Theme.S(2), r.Y - Theme.S(5), Theme.S(4), r.Height + Theme.S(10));
            }
            // out-of-range marker (value beyond the recommended slider range)
            if (_value > Max + 1e-9 || _value < Min - 1e-9)
            {
                using (var b = new SolidBrush(Theme.Warn))
                {
                    int x = _value > Max ? r.Right - Theme.S(3) : r.X;
                    g.FillRectangle(b, x, r.Y - Theme.S(5), Theme.S(3), r.Height + Theme.S(10));
                }
            }
        }

        private void SetFromMouse(int x)
        {
            var r = TrackRect;
            double t = Math.Max(0, Math.Min(1, (x - r.X) / (double)r.Width));
            double v = Min + t * (Max - Min);
            v = Math.Round((v - Min) / Increment) * Increment + Min;
            v = Math.Round(v, Math.Max(Precision, 0));
            v = Math.Max(Min, Math.Min(Max, v));
            if (Math.Abs(v - _value) > 1e-12)
            {
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (ReadOnly || e.Button != MouseButtons.Left) return;
            Focus();
            _drag = true;
            SetFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) SetFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Invalidate();
        }

        private void Nudge(int dir)
        {
            if (ReadOnly) return;
            double v = Math.Round(_value + dir * Increment, Math.Max(Precision, 0));
            v = Math.Max(Min, Math.Min(Max, v));
            if (Math.Abs(v - _value) < 1e-12) return;
            _value = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { Nudge(-1); e.Handled = true; }
            if (e.KeyCode == Keys.Right) { Nudge(1); e.Handled = true; }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>Segmented selector (OFF/ON, POTATO/LOW/...), like the game's toggles.</summary>
    public sealed class Segmented : Control
    {
        private readonly List<string> _options = new List<string>();
        private int _selected = -1, _hover = -1;
        public bool ReadOnly;
        /// <summary>Raised when the user clicks an option.</summary>
        public event EventHandler OptionClicked;
        public int ClickedIndex { get; private set; }

        public Segmented(params string[] options)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _options.AddRange(options);
            BackColor = Theme.Row;
            Font = Theme.Bold;
            Cursor = Cursors.Hand;
        }

        public int Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public IList<string> Options { get { return _options; } }

        private Rectangle Seg(int i)
        {
            int n = Math.Max(1, _options.Count);
            int x0 = i * Width / n, x1 = (i + 1) * Width / n;
            return new Rectangle(x0, 0, x1 - x0, Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            for (int i = 0; i < _options.Count; i++)
            {
                var r = Seg(i);
                bool sel = i == _selected;
                if (sel)
                {
                    using (var b = new SolidBrush(ReadOnly ? Theme.PanelAlt : Theme.AccentDark)) g.FillRectangle(b, r);
                    using (var p = new Pen(ReadOnly ? Theme.TextDim : Theme.Accent, Theme.S(2)))
                        g.DrawLine(p, r.X + 1, r.Bottom - Theme.S(1), r.Right - 1, r.Bottom - Theme.S(1));
                }
                else if (i == _hover && !ReadOnly)
                {
                    using (var b = new SolidBrush(Theme.RowHover)) g.FillRectangle(b, r);
                }
                if (i > 0)
                    using (var p = new Pen(Theme.Track)) g.DrawLine(p, r.X, r.Y + Theme.S(6), r.X, r.Bottom - Theme.S(6));
                var col = sel ? Theme.Text : (ReadOnly ? Theme.TextFaint : Theme.TextDim);
                TextRenderer.DrawText(g, _options[i], Font, r, col,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private int HitTest(int x)
        {
            for (int i = 0; i < _options.Count; i++) if (Seg(i).Contains(x, 1)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.X);
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (ReadOnly) return;
            int h = HitTest(e.X);
            if (h < 0) return;
            ClickedIndex = h;
            if (OptionClicked != null) OptionClicked(this, EventArgs.Empty);
        }
    }

    /// <summary>Section title with a horizontal rule ("VISION ————").</summary>
    public sealed class SectionHeader : Control
    {
        public SectionHeader(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text.ToUpperInvariant();
            Font = Theme.Header;
            BackColor = Theme.Back;
            Height = Theme.S(40);
            Dock = DockStyle.Top;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var sz = TextRenderer.MeasureText(g, Text, Font);
            int y = Height - Theme.S(8) - sz.Height;
            TextRenderer.DrawText(g, Text, Font, new Point(Theme.S(4), y), Theme.Text);
            int ly = y + sz.Height / 2;
            using (var p = new Pen(Theme.Accent)) g.DrawLine(p, Theme.S(16) + sz.Width, ly, Width - Theme.S(8), ly);
        }
    }

    /// <summary>Clickable section title that shows / hides a group ("▶ ADVANCED ————  hint").</summary>
    public sealed class ToggleHeader : Control
    {
        private bool _expanded, _hot;
        public string Hint;
        public event EventHandler Toggled;

        public ToggleHeader(string text, string hint)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text.ToUpperInvariant();
            Hint = hint;
            Font = Theme.Header;
            BackColor = Theme.Back;
            Height = Theme.S(56);
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
        }

        public bool Expanded
        {
            get { return _expanded; }
            set { if (_expanded == value) return; _expanded = value; Invalidate(); if (Toggled != null) Toggled(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Expanded = !Expanded; }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var r = new Rectangle(0, Theme.S(12), Width - 1, Height - Theme.S(16));
            using (var b = new SolidBrush(_hot ? Theme.Row : Theme.Panel)) g.FillRectangle(b, r);
            using (var p = new Pen(_expanded ? Theme.Accent : Theme.Border)) g.DrawRectangle(p, r);
            string title = (_expanded ? "▼  " : "▶  ") + Text;
            var sz = TextRenderer.MeasureText(g, title, Font);
            int y = r.Y + (r.Height - sz.Height) / 2;
            TextRenderer.DrawText(g, title, Font, new Point(r.X + Theme.S(12), y), Theme.Accent, TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(Hint))
            {
                var hr = new Rectangle(r.X + Theme.S(24) + sz.Width, r.Y, r.Width - sz.Width - Theme.S(36), r.Height);
                TextRenderer.DrawText(g, Hint, Theme.Small, hr, Theme.TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Owner-drawn dark combo box.</summary>
    public sealed class DarkCombo : ComboBox
    {
        public DarkCombo()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Row;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ItemHeight = Theme.S(22);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Row)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
            {
                var item = Items[e.Index];
                var p = item as Preset;
                var col = p != null && p.BuiltIn ? Theme.Accent : Theme.Text;
                TextRenderer.DrawText(e.Graphics, ItemText(item), Font, e.Bounds, col,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }

        public static string ItemText(object item)
        {
            var p = item as Preset;
            if (p == null) return item == null ? "" : item.ToString();
            return (p.BuiltIn ? "Game · " : "") + p.Name + (p.Dirty ? " *" : "");
        }
    }

    /// <summary>One editable parameter row: NAME · reference · slider/toggle · value.</summary>
    public sealed class ParamRow : Panel
    {
        public readonly Param P;
        public readonly Label NameLabel;
        public readonly Label RefLabel;
        public readonly Slider Slider;
        public readonly Segmented Toggle;
        public readonly TextBox ValueBox;
        public event EventHandler UserChanged;
        private bool _readOnly;
        private object _value;

        public ParamRow(Param p, ToolTip tip)
        {
            P = p;
            Dock = DockStyle.Top;
            Height = Theme.S(46);
            BackColor = Theme.Back;
            Padding = new Padding(0, Theme.S(3), 0, Theme.S(3));

            NameLabel = new Label
            {
                Text = p.Name.ToUpperInvariant(),
                Font = Theme.Bold,
                ForeColor = Theme.Text,
                BackColor = Theme.Row,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };
            RefLabel = new Label
            {
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Theme.Row,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
            };
            Controls.Add(NameLabel);
            Controls.Add(RefLabel);
            tip.SetToolTip(NameLabel, p.Tooltip);

            if (p.IsBool)
            {
                Toggle = new Segmented("OFF", "ON");
                Toggle.OptionClicked += (s, e) =>
                {
                    SetValue(Toggle.ClickedIndex == 1);
                    Raise();
                };
                Controls.Add(Toggle);
            }
            else
            {
                Slider = new Slider
                {
                    Min = p.ViewMin, Max = p.ViewMax, Increment = p.Increment, Precision = p.Precision,
                };
                Slider.ValueChanged += (s, e) =>
                {
                    SetValue(Slider.Value);
                    Raise();
                };
                ValueBox = new TextBox
                {
                    BackColor = Theme.Row,
                    ForeColor = Theme.Text,
                    BorderStyle = BorderStyle.None,
                    Font = Theme.Bold,
                    TextAlign = HorizontalAlignment.Right,
                };
                ValueBox.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; }
                    if (e.KeyCode == Keys.Escape) { ShowValue(); e.SuppressKeyPress = true; }
                };
                ValueBox.Leave += (s, e) => CommitText();
                tip.SetToolTip(ValueBox, "Type an exact value and press Enter.");
                Controls.Add(Slider);
                Controls.Add(ValueBox);
            }
        }

        private void Raise() { if (UserChanged != null) UserChanged(this, EventArgs.Empty); }

        public object Value { get { return _value; } }

        public bool ReadOnly
        {
            get { return _readOnly; }
            set
            {
                _readOnly = value;
                if (Slider != null) { Slider.ReadOnly = value; Slider.TabStop = !value; Slider.Invalidate(); }
                if (ValueBox != null) ValueBox.ReadOnly = value;
                if (Toggle != null) { Toggle.ReadOnly = value; Toggle.Invalidate(); }
            }
        }

        private void CommitText()
        {
            if (_readOnly || ValueBox == null) return;
            double d;
            string t = ValueBox.Text.Trim().Replace(',', '.').Replace(" ", "");
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                ShowValue();
                return;
            }
            d = Math.Max(P.HardMin, Math.Min(P.HardMax, d));
            if (_value is double && Math.Abs((double)_value - d) < 1e-12) { ShowValue(); return; }
            SetValue(d);
            Raise();
        }

        /// <summary>Sets the displayed value (does not raise UserChanged).</summary>
        public void SetValue(object v)
        {
            _value = v;
            ShowValue();
        }

        private void ShowValue()
        {
            if (Toggle != null)
            {
                Toggle.Selected = _value is bool ? ((bool)_value ? 1 : 0) : -1;
                return;
            }
            double d = _value is double ? (double)_value : 0;
            Slider.Value = d;
            if (!ValueBox.Focused || _readOnly) ValueBox.Text = P.Format(d);
            bool warn = P.OutsideRecommended(d);
            ValueBox.ForeColor = warn ? Theme.Warn : (_readOnly ? Theme.TextDim : Theme.Text);
        }

        /// <summary>Shows the reference preset's value next to the control.</summary>
        public void SetReference(string refName, object refValue)
        {
            if (refValue == null) { RefLabel.Text = ""; return; }
            string txt = refName + " " + P.Format(refValue);
            Color col = Theme.TextFaint;
            if (!GameData.ValuesEqual(refValue, _value))
            {
                if (_value is double && refValue is double)
                {
                    bool up = (double)_value > (double)refValue;
                    txt = (up ? "▲ above " : "▼ below ") + txt;
                    col = up ? Theme.Higher : Theme.Lower;
                }
                else
                {
                    txt = "● " + txt;
                    col = Theme.Changed;
                }
            }
            else txt = "= " + txt;
            RefLabel.Text = txt;
            RefLabel.ForeColor = col;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var r = new Rectangle(0, Padding.Top, Width - 1, Height - Padding.Vertical - 1);
            using (var b = new SolidBrush(Theme.Row)) e.Graphics.FillRectangle(b, r);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, r);
            // small cut corner, like the game's panels
            int c = Theme.S(10);
            using (var p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, r.Right - c, r.Top, r.Right, r.Top + c);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (NameLabel == null || RefLabel == null || (Toggle == null && (Slider == null || ValueBox == null))) return;
            int top = Padding.Top + 1, h = Height - Padding.Vertical - 2;
            int pad = Theme.S(14);
            int ctrlW = Math.Max(Theme.S(160), (int)(Width * 0.36));
            int valW = Theme.S(80);
            int refW = Theme.S(190);
            int right = Width - pad - Theme.S(8);
            if (Toggle != null)
            {
                Toggle.SetBounds(right - ctrlW - valW, top + Theme.S(6), ctrlW + valW, h - Theme.S(12));
                right = right - ctrlW - valW;
            }
            else
            {
                ValueBox.SetBounds(right - valW, top + (h - ValueBox.PreferredHeight) / 2, valW, ValueBox.PreferredHeight);
                Slider.SetBounds(right - valW - ctrlW - Theme.S(10), top + Theme.S(4), ctrlW, h - Theme.S(8));
                right = right - valW - ctrlW - Theme.S(10);
            }
            RefLabel.SetBounds(right - refW - Theme.S(10), top, refW, h);
            NameLabel.SetBounds(pad, top, Math.Max(10, right - refW - Theme.S(20) - pad), h);
        }
    }
}
