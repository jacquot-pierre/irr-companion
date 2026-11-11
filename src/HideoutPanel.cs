using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>One line of a card: [marker] name · detail ......... value [GIVE].</summary>
    public sealed class InfoRow : Panel
    {
        private readonly Label _name, _detail, _value;
        private readonly Button _btn;
        private readonly Color? _marker;

        public InfoRow(string name, string detail, string value, Color nameColor, Color? marker, Button btn, ToolTip tip, string tooltip)
        {
            Dock = DockStyle.Top;
            Height = Theme.S(28);
            BackColor = Theme.Row;
            _marker = marker;
            _name = new Label { Text = name, Font = Theme.Body, ForeColor = nameColor, BackColor = Theme.Row, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            _detail = new Label { Text = detail ?? "", Font = Theme.Small, ForeColor = Theme.TextFaint, BackColor = Theme.Row, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            _value = new Label { Text = value ?? "", Font = Theme.Bold, ForeColor = nameColor, BackColor = Theme.Row, TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false };
            Controls.Add(_name);
            Controls.Add(_detail);
            Controls.Add(_value);
            if (btn != null) { _btn = btn; Controls.Add(btn); }
            if (tooltip != null) { tip.SetToolTip(_name, tooltip); tip.SetToolTip(_detail, tooltip); }
        }

        private Image _icon;
        private bool _iconSlot;
        private Label _extra;
        private int _extraWidth;

        /// <summary>Adds a column before the value (e.g. a price), right-aligned, of the given width (at 100 %).</summary>
        public void SetExtra(string text, Color color, int width)
        {
            if (_extra == null)
            {
                _extra = new Label { Font = Theme.Body, BackColor = Theme.Row, TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false };
                Controls.Add(_extra);
            }
            _extra.Text = text ?? "";
            _extra.ForeColor = color;
            _extraWidth = Theme.S(width);
            PerformLayout();
        }

        public Label Extra { get { return _extra; } }

        /// <summary>Shows a game icon before the name (null keeps an empty slot so names stay aligned).</summary>
        public void SetIcon(Image icon)
        {
            _icon = icon;
            _iconSlot = true;
            Height = Theme.S(48);
            PerformLayout();
            Invalidate();
        }

        /// <summary>Makes the name clickable (link to another unit).</summary>
        public void MakeLink(Action click)
        {
            _name.Cursor = Cursors.Hand;
            _name.Click += (s, e) => click();
            _name.MouseEnter += (s, e) => _name.Font = new Font(Theme.Body, FontStyle.Underline);
            _name.MouseLeave += (s, e) => _name.Font = Theme.Body;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_iconSlot)
            {
                // inventory-like slot, so dark icons stay visible
                int s = Height - Theme.S(6);
                var slot = new Rectangle(Theme.S(12), (Height - s) / 2, s, s);
                using (var b = new SolidBrush(Color.FromArgb(62, 58, 53))) e.Graphics.FillRectangle(b, slot);
                using (var p = new Pen(Theme.Track)) e.Graphics.DrawRectangle(p, slot.X, slot.Y, slot.Width - 1, slot.Height - 1);
                if (_icon != null)
                {
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(_icon, Rectangle.Inflate(slot, -Theme.S(2), -Theme.S(2)));
                }
                else
                    TextRenderer.DrawText(e.Graphics, "?", Theme.Bold, slot, Theme.TextFaint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else if (_marker.HasValue)
                using (var b = new SolidBrush(_marker.Value))
                    e.Graphics.FillRectangle(b, Theme.S(14), Height / 2 - Theme.S(5), Theme.S(10), Theme.S(10));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_name == null || _detail == null || _value == null) return;
            int pad = Theme.S(14), right = Width - pad;
            int x = _iconSlot ? Theme.S(12) + Height - Theme.S(6) + Theme.S(10) : pad + (_marker.HasValue ? Theme.S(18) : 0);
            if (_btn != null)
            {
                int bw = Theme.S(64), bh = Math.Min(Height - Theme.S(4), Theme.S(26));
                _btn.SetBounds(right - bw, (Height - bh) / 2, bw, bh);
                right -= bw + Theme.S(10);
            }
            int vw = Math.Max(Theme.S(60), TextRenderer.MeasureText(_value.Text, _value.Font).Width + Theme.S(6));
            _value.SetBounds(right - vw, 0, vw, Height);
            right -= vw + Theme.S(8);
            if (_extra != null)
            {
                _extra.SetBounds(right - _extraWidth, 0, _extraWidth, Height);
                right -= _extraWidth + Theme.S(8);
            }
            int nw = Math.Min(Math.Max(0, right - x), TextRenderer.MeasureText(_name.Text, _name.Font).Width + Theme.S(8));
            _name.SetBounds(x, 0, nw, Height);
            _detail.SetBounds(x + nw + Theme.S(4), 0, Math.Max(0, right - x - nw - Theme.S(4)), Height);
        }
    }

    /// <summary>A word-wrapped label that sizes its height to its text.</summary>
    public sealed class WrapLabel : Label
    {
        public WrapLabel()
        {
            AutoSize = false;
            Dock = DockStyle.Top;
            UseMnemonic = false;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Fit();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Fit();
        }

        private void Fit()
        {
            if (Width <= 0) return;
            int h = string.IsNullOrEmpty(Text) ? 0
                : TextRenderer.MeasureText(Text, Font, new Size(Width - Padding.Horizontal, 0), TextFormatFlags.WordBreak).Height + Padding.Vertical;
            if (h != Height) Height = h;
        }
    }

    /// <summary>Bordered card holding rows stacked from the top.</summary>
    public sealed class Card : Panel
    {
        private readonly List<Control> _rows = new List<Control>();
        public Color BorderColor = Theme.Border;

        public Card()
        {
            Dock = DockStyle.Top;
            BackColor = Theme.Row;
            Padding = new Padding(1, 1, 1, Theme.S(8));
        }

        public void Add(Control c) { _rows.Add(c); }

        /// <summary>Adds the rows (Dock=Top stacks in reverse order) and sizes the card.</summary>
        public void Done()
        {
            for (int i = _rows.Count - 1; i >= 0; i--) Controls.Add(_rows[i]);
            foreach (var c in _rows) c.SizeChanged += (s, e) => FitHeight();
            FitHeight();
        }

        private void FitHeight()
        {
            int h = Padding.Vertical + _rows.Sum(c => c.Height);
            if (h != Height) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(BorderColor)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            int c = Theme.S(12);
            using (var p = new Pen(BorderColor)) e.Graphics.DrawLine(p, Width - 1 - c, 0, Width - 1, c);
        }
    }

    /// <summary>The HIDEOUT view: upgrade costs of every hideout unit, the player's progress and a shopping list.</summary>
    public sealed class HideoutPanel : Panel
    {
        private const string ShoppingList = "SHOPPING LIST";

        private readonly ToolTip _tip;
        private readonly Action<string> _status;
        private readonly HideoutProgress _p;
        private readonly ListBox _units;
        private readonly Panel _content;
        private readonly NumericUpDown _playerLevel;
        private readonly Dictionary<string, NumericUpDown> _loyalty = new Dictionary<string, NumericUpDown>();
        private bool _nextOnly = true, _loading;
        private object _shown;

        public static readonly Dictionary<string, Color> ResourceColors = new Dictionary<string, Color>
        {
            { "Concrete", Color.FromArgb(170, 166, 156) },
            { "Food", Color.FromArgb(139, 195, 74) },
            { "Metal", Color.FromArgb(93, 169, 233) },
            { "Oil", Color.FromArgb(240, 160, 64) },
        };

        public HideoutPanel(ToolTip tip, Action<string> status)
        {
            _tip = tip;
            _status = status;
            _loading = true; // no save while the fields get their initial values
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            HideoutData.Load();
            try { _p = HideoutProgress.Load(); }
            catch (Exception ex)
            {
                _p = new HideoutProgress();
                MessageBox.Show("Could not read your hideout progress:\n" + ex.Message + "\n\nFile: " + HideoutProgress.FilePath,
                    "IRR Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // ---- left: my progress + units
            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(250), BackColor = Theme.Panel, Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(12), Theme.S(12)) };
            var progHeader = new SectionHeader("My progress") { BackColor = Theme.Panel, Height = Theme.S(36) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Panel, Height = Theme.S(6 * 30 + 4) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(70)));
            _playerLevel = Num(1, 100, _p.PlayerLevel);
            _playerLevel.ValueChanged += (s, e) => { if (_loading) return; _p.PlayerLevel = (int)_playerLevel.Value; ProgressChanged(); };
            AddGridRow(grid, 0, "Player level", _playerLevel, "Your player level (some upgrades need a minimum level).");
            var loyLabel = Theme.Lbl("Faction loyalty", Theme.Small, Theme.TextDim);
            loyLabel.Margin = new Padding(0, Theme.S(8), 0, 0);
            grid.Controls.Add(loyLabel, 0, 1);
            grid.SetColumnSpan(loyLabel, 2);
            int row = 2;
            foreach (var f in HideoutData.Factions)
            {
                var n = Num(0, 10, _p.LoyaltyOf(f));
                string faction = f;
                n.ValueChanged += (s, e) => { if (_loading) return; _p.Loyalty[faction] = (int)n.Value; ProgressChanged(); };
                _loyalty[f] = n;
                AddGridRow(grid, row++, "   " + f, n, HideoutData.FactionNames[f] + ": your loyalty level with this faction.");
            }
            for (int i = 0; i < row; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(i == 1 ? 26 : 30)));
            grid.Height = Theme.S(26) + (row - 1) * Theme.S(30) + Theme.S(4);
            var unitsHeader = new SectionHeader("Hideout") { BackColor = Theme.Panel, Height = Theme.S(40) };
            _units = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(40), IntegralHeight = false, Font = Theme.Bold,
            };
            _units.Items.Add(ShoppingList);
            foreach (var u in HideoutData.Units) _units.Items.Add(u);
            _units.DrawItem += DrawUnit;
            _units.SelectedIndexChanged += (s, e) => Show(_units.SelectedItem, true);
            Theme.DarkScrollbars(_units);
            var hint = Theme.Lbl("Set the level of each unit to follow your progress. GIVE copies a console command (cheat).", Theme.Small, Theme.TextDim);
            hint.AutoSize = false;
            hint.Dock = DockStyle.Bottom;
            hint.Height = Theme.S(62);
            left.Controls.Add(_units);
            left.Controls.Add(unitsHeader);
            left.Controls.Add(grid);
            left.Controls.Add(progHeader);
            left.Controls.Add(hint);

            // ---- content
            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(Theme.S(20), Theme.S(8), Theme.S(20), Theme.S(20)) };
            Theme.DarkScrollbars(_content);

            Controls.Add(_content);
            Controls.Add(left);
            _units.SelectedIndex = 0;
            _loading = false;
        }

        public ListBox List { get { return _units; } }

        private static NumericUpDown Num(int min, int max, int value)
        {
            return new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), BackColor = Theme.Row, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle, Font = Theme.Bold, TextAlign = HorizontalAlignment.Right, Dock = DockStyle.Fill,
                Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)),
            };
        }

        private void AddGridRow(TableLayoutPanel g, int row, string label, Control c, string tooltip)
        {
            var l = Theme.Lbl(label, Theme.Body, Theme.Text);
            l.Anchor = AnchorStyles.Left;
            g.Controls.Add(l, 0, row);
            g.Controls.Add(c, 1, row);
            _tip.SetToolTip(l, tooltip);
            _tip.SetToolTip(c, tooltip);
        }

        private void ProgressChanged()
        {
            try { _p.Save(); }
            catch (Exception ex) { _status("Could not save the hideout progress: " + ex.Message); }
            _units.Invalidate();
            Show(_shown, false);
        }

        // ================================================================== unit list

        private enum State { Max, Ready, Locked }

        private State StateOf(HideoutUnit u)
        {
            var next = _p.NextLevel(u);
            if (next == null) return State.Max;
            return next.Conditions.All(_p.IsMet) ? State.Ready : State.Locked;
        }

        private void DrawUnit(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            var item = _units.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Theme.S(6), Theme.S(3), e.Bounds.Height - Theme.S(12));
            var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(16), e.Bounds.Height);
            var u = item as HideoutUnit;
            if (u == null)
            {
                TextRenderer.DrawText(g, "★  " + ShoppingList, Theme.Bold, r, Theme.Changed, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                using (var p = new Pen(Theme.Track)) g.DrawLine(p, e.Bounds.X + Theme.S(8), e.Bounds.Bottom - 1, e.Bounds.Right - Theme.S(8), e.Bounds.Bottom - 1);
                return;
            }
            var top = new Rectangle(r.X, r.Y + Theme.S(3), r.Width, Theme.S(20));
            var bot = new Rectangle(r.X, r.Y + Theme.S(21), r.Width, Theme.S(16));
            TextRenderer.DrawText(g, u.Name.ToUpperInvariant(), Theme.Bold, top, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            int lvl = _p.LevelOf(u.Name);
            TextRenderer.DrawText(g, "Level " + lvl + " / " + u.MaxLevel, Theme.Small, top, Theme.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            var st = StateOf(u);
            string s = st == State.Max ? "Fully upgraded" : st == State.Ready ? "Can upgrade to level " + (lvl + 1) : "Level " + (lvl + 1) + " locked";
            var col = st == State.Max ? Theme.TextFaint : st == State.Ready ? Theme.Higher : Theme.Lower;
            TextRenderer.DrawText(g, s, Theme.Small, bot, col, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        // ================================================================== content

        private void Show(object item, bool resetScroll)
        {
            _shown = item;
            var scroll = _content.AutoScrollPosition;
            _content.SuspendLayout();
            var old = _content.Controls.Cast<Control>().ToList();
            _content.Controls.Clear();
            foreach (var c in old) c.Dispose();
            var items = new List<Control>();
            var u = item as HideoutUnit;
            if (u != null) BuildUnit(u, items);
            else if (item != null) BuildShoppingList(items);
            for (int i = items.Count - 1; i >= 0; i--) _content.Controls.Add(items[i]);
            _content.ResumeLayout();
            _content.AutoScrollPosition = resetScroll ? Point.Empty : new Point(-scroll.X, -scroll.Y);
        }

        private static Control Gap(int h)
        {
            return new Panel { Dock = DockStyle.Top, Height = Theme.S(h), BackColor = Theme.Back };
        }

        private static Label Title(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Top, Height = Theme.S(40), Font = Theme.Title, ForeColor = Theme.Text, BackColor = Theme.Back, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Top, Height = Theme.S(26), Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Row, Padding = new Padding(Theme.S(12), 0, 0, 0), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
        }

        private Button GiveButton(string command)
        {
            var b = Theme.Btn("GIVE", (s, e) => Copy(command));
            b.Font = Theme.Small;
            _tip.SetToolTip(b, "Copies \"" + command + "\" - paste it in the game console (cheat).");
            return b;
        }

        private void Copy(string command)
        {
            try
            {
                Clipboard.SetText(command);
                _status("Copied: " + command + "   ·   paste it in the game console (² / ~, ^ or F10) with Ctrl+V.");
            }
            catch (Exception ex) { _status("Could not access the clipboard: " + ex.Message); }
        }

        private void BuildUnit(HideoutUnit u, List<Control> items)
        {
            int cur = _p.LevelOf(u.Name);
            items.Add(Title(u.Name.ToUpperInvariant()));

            // current level selector
            var bar = new Panel { Dock = DockStyle.Top, Height = Theme.S(44), BackColor = Theme.Back };
            var lbl = Theme.Lbl("MY LEVEL", Theme.Bold, Theme.TextDim);
            lbl.Location = new Point(0, Theme.S(12));
            var opts = Enumerable.Range(0, u.MaxLevel + 1).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
            var seg = new Segmented(opts) { Selected = cur, BackColor = Theme.Row };
            seg.SetBounds(Theme.S(90), Theme.S(4), Theme.S(60) * opts.Length, Theme.S(34));
            seg.OptionClicked += (s, e) =>
            {
                _p.UnitLevels[u.Name] = seg.ClickedIndex;
                _status(u.Name + " set to level " + seg.ClickedIndex + ".");
                ProgressChanged();
            };
            _tip.SetToolTip(seg, "The level of this unit in your hideout. 0 = not built yet.");
            var note = Theme.Lbl(u.FreeLevels > 0 ? "Built from the start. Level 1 = first upgrade (level " + (u.FreeLevels + 1) + " in the game files)." : "", Theme.Small, Theme.TextFaint);
            _tip.SetToolTip(note, "The game files have a free level 1 for this unit (its starting state). It is not shown, so the levels start at the first upgrade you pay for.");
            note.Location = new Point(Theme.S(100) + Theme.S(60) * opts.Length, Theme.S(14));
            bar.Controls.Add(lbl);
            bar.Controls.Add(seg);
            bar.Controls.Add(note);
            items.Add(bar);
            items.Add(Gap(6));

            foreach (var lv in u.Levels)
            {
                items.Add(LevelCard(u, lv, cur));
                items.Add(Gap(12));
            }
        }

        private Card LevelCard(HideoutUnit u, HideoutLevel lv, int cur)
        {
            var card = new Card();
            bool built = lv.Level <= cur, next = lv.Level == cur + 1;
            bool ready = lv.Conditions.All(_p.IsMet);
            string state = built ? "BUILT ✓" : next ? (ready ? "NEXT UPGRADE - REQUIREMENTS MET" : "NEXT UPGRADE - LOCKED") : "LATER";
            var stateColor = built ? Theme.Higher : next ? (ready ? Theme.Changed : Theme.Lower) : Theme.TextFaint;
            if (next) card.BorderColor = ready ? Theme.Changed : Theme.Lower;

            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(38), BackColor = Theme.Row };
            var t = new Label { Text = "LEVEL " + lv.Level, Font = Theme.Header, ForeColor = built ? Theme.TextDim : Theme.Text, BackColor = Theme.Row, AutoSize = true, Location = new Point(Theme.S(12), Theme.S(9)) };
            var st = new Label { Text = state, Font = Theme.Bold, ForeColor = stateColor, BackColor = Theme.Row, AutoSize = true };
            head.Controls.Add(t);
            head.Controls.Add(st);
            head.Resize += (s, e) => st.Location = new Point(head.Width - st.Width - Theme.S(14), Theme.S(11));
            card.Add(head);
            if (!string.IsNullOrEmpty(lv.Description))
                card.Add(new WrapLabel { Text = lv.Description, Font = Theme.Body, ForeColor = Theme.TextDim, BackColor = Theme.Row, Padding = new Padding(Theme.S(12), 0, Theme.S(12), Theme.S(6)) });

            if (lv.IsFree)
            {
                card.Add(new InfoRow("No cost, no requirement.", null, null, Theme.TextDim, null, null, _tip, null));
                card.Done();
                return card;
            }

            if (lv.Conditions.Count > 0)
            {
                card.Add(Caption("REQUIREMENTS"));
                foreach (var c in lv.Conditions)
                {
                    bool ok = _p.IsMet(c);
                    string have = c.Type == "unit" ? "yours: " + _p.LevelOf(c.Unit)
                        : c.Type == "faction" ? "yours: " + _p.LoyaltyOf(c.Faction)
                        : "yours: " + _p.PlayerLevel;
                    string tip = c.Type == "faction" ? HideoutData.FactionNames[c.Faction] : null;
                    var row = new InfoRow((ok ? "✓  " : "✗  ") + c.Text, have, null, ok ? Theme.Higher : Theme.Lower, null, null, _tip, tip);
                    if (c.Type == "unit")
                    {
                        var target = HideoutData.Find(c.Unit);
                        if (target != null) row.MakeLink(() => _units.SelectedItem = target);
                    }
                    card.Add(row);
                }
            }
            if (lv.Items.Count > 0)
            {
                card.Add(Caption("ITEMS"));
                foreach (var it in lv.Items)
                    card.Add(WithIcon(new InfoRow(it.Name, null, "× " + it.Count, Theme.Text, null, GiveButton(HideoutData.GiveItem(it, it.Count)), _tip, it.Tag), Icons.Item(it.Id)));
            }
            card.Add(Caption("MONEY AND MATERIALS"));
            if (lv.Cash > 0)
                card.Add(WithIcon(new InfoRow("Cash", null, Fmt(lv.Cash), Theme.Changed, Theme.Changed, GiveButton(HideoutData.GiveCash(lv.Cash)), _tip, null), Icons.Item("Cash")));
            foreach (var r in HideoutData.Resources)
            {
                int v;
                if (!lv.Resources.TryGetValue(r, out v)) continue;
                card.Add(WithIcon(new InfoRow(r, null, Fmt(v), Theme.Text, ResourceColors[r], GiveButton(HideoutData.GiveResource(r, v)), _tip, null), Icons.Material(r, ResourceColors[r])));
            }
            card.Done();
            return card;
        }

        private static InfoRow WithIcon(InfoRow row, Image icon)
        {
            row.SetIcon(icon);
            return row;
        }

        private static string Fmt(int v) { return v.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " "); }

        private void BuildShoppingList(List<Control> items)
        {
            var t = HideoutTotals.Compute(_p, _nextOnly);
            items.Add(Title("SHOPPING LIST"));
            var bar = new Panel { Dock = DockStyle.Top, Height = Theme.S(44), BackColor = Theme.Back };
            var seg = new Segmented("NEXT UPGRADES", "EVERYTHING LEFT") { Selected = _nextOnly ? 0 : 1, BackColor = Theme.Row };
            seg.SetBounds(0, Theme.S(4), Theme.S(340), Theme.S(34));
            seg.OptionClicked += (s, e) => { _nextOnly = seg.ClickedIndex == 0; Show(_shown, true); };
            _tip.SetToolTip(seg, "NEXT UPGRADES: the next level of every unit. EVERYTHING LEFT: all the levels you have not built yet.");
            var sum = Theme.Lbl(t.Upgrades == 0 ? "Nothing left: your hideout is fully upgraded."
                : t.Upgrades + " upgrade" + (t.Upgrades > 1 ? "s" : "") + " · based on the levels set in each unit", Theme.Small, Theme.TextDim);
            sum.Location = new Point(Theme.S(356), Theme.S(14));
            bar.Controls.Add(seg);
            bar.Controls.Add(sum);
            items.Add(bar);
            items.Add(Gap(6));
            if (t.Upgrades == 0) return;

            var money = new Card();
            money.Add(Caption("MONEY AND MATERIALS"));
            money.Add(WithIcon(new InfoRow("Cash", null, Fmt(t.Cash), Theme.Changed, Theme.Changed, t.Cash > 0 ? GiveButton(HideoutData.GiveCash(t.Cash)) : null, _tip, null), Icons.Item("Cash")));
            foreach (var r in HideoutData.Resources)
            {
                int v;
                t.Resources.TryGetValue(r, out v);
                money.Add(WithIcon(new InfoRow(r, null, Fmt(v), Theme.Text, ResourceColors[r], v > 0 ? GiveButton(HideoutData.GiveResource(r, v)) : null, _tip, null), Icons.Material(r, ResourceColors[r])));
            }
            money.Done();
            items.Add(money);
            items.Add(Gap(12));

            var list = new Card();
            list.Add(Caption("ITEMS (" + t.Items.Count + ")"));
            foreach (var line in t.Items)
                list.Add(WithIcon(new InfoRow(line.Item.Name, "for " + string.Join(", ", line.For), "× " + line.Count, Theme.Text, null,
                    GiveButton(HideoutData.GiveItem(line.Item, line.Count)), _tip, line.Item.Tag + "\nfor " + string.Join(", ", line.For)), Icons.Item(line.Item.Id)));
            list.Done();
            items.Add(list);
        }

#if DEBUG
        /// <summary>Test helper: shows a unit (or the shopping list) with an example progress, without saving.</summary>
        public void TestShow(string unit, bool nextOnly)
        {
            _loading = true;
            _p.PlayerLevel = 12;
            _playerLevel.Value = 12;
            foreach (var kv in new Dictionary<string, int> { { "IGC", 2 }, { "UICS", 1 }, { "VLF", 2 }, { "FANG", 0 } })
            {
                _p.Loyalty[kv.Key] = kv.Value;
                _loyalty[kv.Key].Value = kv.Value;
            }
            foreach (var kv in new Dictionary<string, int> { { "Structure", 0 }, { "Generator", 1 }, { "Stash", 1 }, { "Gunsmith", 1 }, { "Repair Station", 1 } })
                _p.UnitLevels[kv.Key] = kv.Value;
            _loading = false;
            _nextOnly = nextOnly;
            object target = unit == null ? (object)ShoppingList : HideoutData.Find(unit);
            _units.SelectedItem = target;
            Show(target, true);
            _units.Invalidate();
        }
#endif
    }
}
