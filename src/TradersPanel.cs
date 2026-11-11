using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>The TRADERS view: what each trader can have in stock, with the chance of every item.</summary>
    public sealed class TradersPanel : Panel
    {
        private readonly ToolTip _tip;
        private readonly ListBox _list;
        private readonly Panel _content;
        private readonly TextBox _search;
        private readonly Timer _searchDelay = new Timer { Interval = 450 };
        private string _applied = "";
        private Trader _shown;

        public TradersPanel(ToolTip tip)
        {
            _tip = tip;
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            TraderData.Load();

            // ---- left: traders
            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(260), BackColor = Theme.Panel, Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(12), Theme.S(12)) };
            var head = new SectionHeader("Traders") { BackColor = Theme.Panel, Height = Theme.S(36) };
            _list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(68), IntegralHeight = false, Font = Theme.Bold,
            };
            foreach (var t in TraderData.Traders) _list.Items.Add(t);
            _list.DrawItem += DrawTrader;
            _list.SelectedIndexChanged += (s, e) => Show(_list.SelectedItem as Trader, true);
            Theme.DarkScrollbars(_list);
            var gap = new Panel { Dock = DockStyle.Top, Height = Theme.S(6), BackColor = Theme.Panel };
            _search = Theme.Box();
            _search.Dock = DockStyle.Top;
            // search when typing stops for a moment, or at once with Enter (no rebuild on every key)
            _search.TextChanged += (s, e) => { _searchDelay.Stop(); _searchDelay.Start(); };
            _searchDelay.Tick += (s, e) => ApplySearch();
            _search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplySearch(); }
                else if (e.KeyCode == Keys.Escape && _search.Text.Length > 0) { e.SuppressKeyPress = true; _search.Text = ""; ApplySearch(); }
            };
            SetCue(_search, "Find an item...");
            _tip.SetToolTip(_search, "Show only the items whose name contains this text (Enter to search at once, Esc to clear).");
            var hint = Theme.Lbl("Read from the game's trader presets. Weapons are not listed. The chances of the random loot are an estimate.", Theme.Small, Theme.TextDim);
            hint.AutoSize = false;
            hint.Dock = DockStyle.Bottom;
            hint.Height = Theme.S(62);
            left.Controls.Add(_list);
            left.Controls.Add(gap);
            left.Controls.Add(_search);
            left.Controls.Add(head);
            left.Controls.Add(hint);

            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(Theme.S(20), Theme.S(8), Theme.S(20), Theme.S(20)) };
            Theme.DarkScrollbars(_content);
            Controls.Add(_content);
            Controls.Add(left);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        public ListBox List { get { return _list; } }

        private static void SetCue(TextBox box, string cue)
        {
            box.HandleCreated += (s, e) => NativeCue(box, cue);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void NativeCue(TextBox box, string cue) { SendMessage(box.Handle, 0x1501, (IntPtr)1, cue); }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETREDRAW = 0x000B;

        private void ApplySearch()
        {
            _searchDelay.Stop();
            string q = _search.Text.Trim();
            if (q == _applied) return;
            _applied = q;
            Show(_shown, true);
        }

        private static string Abbr(Trader t) { return t.Id; }

        private void DrawTrader(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            var t = (Trader)_list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Theme.S(6), Theme.S(3), e.Bounds.Height - Theme.S(12));
            var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(16), e.Bounds.Height);
            var top = new Rectangle(r.X, r.Y + Theme.S(5), r.Width, Theme.S(22));
            var mid = new Rectangle(r.X, r.Y + Theme.S(26), r.Width, Theme.S(18));
            var bot = new Rectangle(r.X, r.Y + Theme.S(44), r.Width, Theme.S(18));
            const TextFormatFlags f = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, t.People.Count > 0 ? t.PeopleNames : Abbr(t) + " trader", Theme.Bold, top, Theme.Text, f);
            TextRenderer.DrawText(g, Abbr(t) + " · " + t.Name, Theme.Small, mid, Theme.TextDim, f);
            TextRenderer.DrawText(g, t.People.Count > 0 ? string.Join(", ", t.People.Select(p => p.MapText)) : "", Theme.Small, bot, Theme.Accent, f);
        }

        // ================================================================== content

        private static string Pct(double p)
        {
            if (p >= 0.995) return "100 %";
            if (p >= 0.095) return Math.Round(p * 100).ToString(CultureInfo.InvariantCulture) + " %";
            if (p >= 0.0095) return (p * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %";
            return "< 1 %";
        }

        private static Color ChanceColor(double p)
        {
            return p >= 0.995 ? Theme.Higher : p >= 0.4 ? Theme.Text : p >= 0.1 ? Theme.TextDim : Theme.TextFaint;
        }

        private bool Match(TraderItem it)
        {
            string q = _applied;
            return q.Length == 0 || it.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || it.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Show(Trader t, bool resetScroll)
        {
            _shown = t;
            var scroll = _content.AutoScrollPosition;
            // no drawing while the rows are rebuilt, then one repaint: no flicker
            bool frozen = _content.IsHandleCreated;
            if (frozen) SendMessage(_content.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                _content.SuspendLayout();
                var old = _content.Controls.Cast<Control>().ToList();
                _content.Controls.Clear();
                foreach (var c in old) c.Dispose();
                var items = new List<Control>();
                if (t != null) Build(t, items);
                for (int i = items.Count - 1; i >= 0; i--) _content.Controls.Add(items[i]);
                _content.ResumeLayout();
                _content.AutoScrollPosition = resetScroll ? Point.Empty : new Point(-scroll.X, -scroll.Y);
            }
            finally
            {
                if (frozen)
                {
                    SendMessage(_content.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    _content.Invalidate(true);
                    _content.Update();
                }
            }
        }

        private static Control Gap(int h) { return new Panel { Dock = DockStyle.Top, Height = Theme.S(h), BackColor = Theme.Back }; }

        private static Label Caption(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Top, Height = Theme.S(26), Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Row, Padding = new Padding(Theme.S(12), 0, 0, 0), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
        }

        private const int PriceWidth = 96, ChanceWidth = 60;

        private Control CardHead(string title, string state, string tooltip)
        {
            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(56), BackColor = Theme.Row };
            var l = new Label { Text = title, Font = Theme.Header, ForeColor = Theme.Text, BackColor = Theme.Row, AutoSize = true, Location = new Point(Theme.S(12), Theme.S(9)), UseMnemonic = false };
            var s = new Label { Text = state, Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Row, AutoSize = true, UseMnemonic = false };
            var price = new Label { Text = "PRICE", Font = Theme.Small, ForeColor = Theme.TextFaint, BackColor = Theme.Row, TextAlign = ContentAlignment.MiddleRight };
            var chance = new Label { Text = "CHANCE", Font = Theme.Small, ForeColor = Theme.TextFaint, BackColor = Theme.Row, TextAlign = ContentAlignment.MiddleRight };
            head.Controls.Add(l);
            head.Controls.Add(s);
            head.Controls.Add(price);
            head.Controls.Add(chance);
            head.Resize += (o, e) =>
            {
                s.Location = new Point(head.Width - s.Width - Theme.S(14), Theme.S(13));
                // same columns as the rows: [price][8][chance] with 14 px on the right
                int right = head.Width - 1 - Theme.S(14);
                int cw = Theme.S(ChanceWidth);
                chance.SetBounds(right - cw, Theme.S(36), cw, Theme.S(18));
                right -= cw + Theme.S(8);
                price.SetBounds(right - Theme.S(PriceWidth), Theme.S(36), Theme.S(PriceWidth), Theme.S(18));
            };
            _tip.SetToolTip(price, "Market price of one unit, in cash (vendor table of the game). A trader in a raid can apply its own multiplier.");
            _tip.SetToolTip(chance, "Chance that the item is in the trader's stock.");
            if (tooltip != null) { _tip.SetToolTip(l, tooltip); _tip.SetToolTip(s, tooltip); }
            return head;
        }

        private static string Money(double v)
        {
            if (v <= 0) return "?";
            string s = v >= 10 || v == Math.Floor(v) ? Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ")
                : v.ToString("0.##", CultureInfo.InvariantCulture);
            return s;
        }

        private void AddPrice(InfoRow row, TraderItem it)
        {
            row.SetExtra(Money(it.Price), it.Price > 0 ? Theme.Changed : Theme.TextFaint, PriceWidth);
            _tip.SetToolTip(row.Extra, it.Price > 0 ? it.Name + ": " + Money(it.Price) + " cash per unit" + (it.Count > 1 ? " (× " + it.Count + " = " + Money(it.Price * it.Count) + ")" : "")
                : "No market price in the game files.");
        }

        private InfoRow Row(TraderItem it, string detail, double chance, string tooltip)
        {
            var row = new InfoRow(it.Name, detail, Pct(chance), ChanceColor(chance), null, null, _tip, tooltip ?? it.Id);
            row.SetIcon(Icons.Item(it.Id));
            AddPrice(row, it);
            return row;
        }

        private void Build(Trader t, List<Control> items)
        {
            string title = t.People.Count > 0 ? t.PeopleNames.ToUpperInvariant() + "  ·  " + Abbr(t) : Abbr(t) + " TRADER";
            items.Add(new Label { Text = title, Dock = DockStyle.Top, Height = Theme.S(40), Font = Theme.Title, ForeColor = Theme.Text, BackColor = Theme.Back, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
            string who = t.People.Count == 0 ? t.Name + " trader. "
                : t.People.Count == 1 ? t.People[0].Name + ", " + t.Name + " trader, on " + t.People[0].MapText + ". "
                : string.Join(" and ", t.People.Select(p => p.Name + " (" + p.MapText + ")")) + ": the " + t.Name + " traders, same stock. ";
            var sub = new WrapLabel
            {
                Text = who + "Each time the stock is made: random loot, a random pick in a list, and the items always in stock." +
                       (t.WeaponChance > 0 ? " Plus a " + Pct(t.WeaponChance) + " chance of a weapon (not listed)." : ""),
                Font = Theme.Body, ForeColor = Theme.TextDim, BackColor = Theme.Back, Padding = new Padding(0, 0, 0, Theme.S(8)),
            };
            items.Add(sub);
            bool any = false;

            // random loot
            foreach (var g in t.Random)
            {
                var cats = g.Categories.Select(c => new { c, list = c.Items.Where(Match).ToList() }).Where(x => x.list.Count > 0).ToList();
                if (cats.Count == 0) continue;
                var card = new Card();
                string n = g.Min == g.Max ? g.Min.ToString(CultureInfo.InvariantCulture) : g.Min + " to " + g.Max;
                string tiers = string.Join(", ", g.Tiers.Select(x => "tier " + x.Key + ": " + x.Value.ToString("0", CultureInfo.InvariantCulture)));
                card.Add(CardHead("RANDOM LOOT", n + " items · estimated",
                    "The game draws " + n + " items: each draw picks a kind of item by its weight, then (for a family of items) a rarity tier by its weight (" +
                    tiers + "), then an item of that tier. The percent is our estimate of the chance to get the item at least once."));
                foreach (var x in cats)
                {
                    bool single = x.c.Item != null && x.c.Items.Count == 1;
                    if (!single)
                        card.Add(Caption(x.c.Label.ToUpperInvariant() + "  ·  " + Pct(x.c.Share) + " of the draws  ·  " + x.c.Items.Count + " items"));
                    foreach (var it in x.list)
                        card.Add(Row(it, single ? Pct(x.c.Share) + " of the draws" : it.Tier != null ? "tier " + it.Tier : null, it.Chance, null));
                }
                card.Done();
                items.Add(card);
                items.Add(Gap(12));
                any = true;
            }
            // weighted pick
            foreach (var g in t.Choices)
            {
                var list = g.Items.Where(Match).ToList();
                if (list.Count == 0) continue;
                var card = new Card();
                string n = g.Min == g.Max ? g.Min.ToString(CultureInfo.InvariantCulture) : g.Min + " to " + g.Max;
                card.Add(CardHead("RANDOM PICK", n + " of these " + g.Items.Count + " items" + (g.Chance < 0.995 ? " · " + Pct(g.Chance) + " of the time" : ""),
                    "The game draws " + n + " different items from this list; an item with a higher weight comes out more often. The percent is the chance the item is in the stock (exact)."));
                double total = g.Items.Sum(i => i.Weight);
                foreach (var it in list)
                    card.Add(Row(it, "weight " + it.Weight.ToString("0.#", CultureInfo.InvariantCulture) + " / " + total.ToString("0.#", CultureInfo.InvariantCulture), it.Chance, null));
                card.Done();
                items.Add(card);
                items.Add(Gap(12));
                any = true;
            }

            // always in stock
            var fixedItems = t.Fixed.Where(Match).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (fixedItems.Count > 0)
            {
                var card = new Card();
                card.Add(CardHead("ALWAYS IN STOCK", t.Fixed.Count + " items · " + t.Fixed.Sum(i => i.Count) + " units",
                    "These items are always added to the stock (100 %), in the quantity shown."));
                foreach (var it in fixedItems)
                {
                    var row = new InfoRow(it.Name, it.Count > 1 ? "× " + it.Count : null, "100 %", Theme.Higher, null, null, _tip, it.Id);
                    row.SetIcon(Icons.Item(it.Id));
                    AddPrice(row, it);
                    card.Add(row);
                }
                card.Done();
                items.Add(card);
                items.Add(Gap(12));
                any = true;
            }

            if (!any)
                items.Add(new WrapLabel { Text = "No item of this trader matches \"" + _applied + "\".", Font = Theme.Body, ForeColor = Theme.TextDim, BackColor = Theme.Back });
        }

#if DEBUG
        /// <summary>Test helper: shows a trader, with an optional search.</summary>
        public void TestShow(int index, string search)
        {
            _search.Text = search ?? "";
            _applied = _search.Text.Trim();
            _list.SelectedIndex = index;
            Show(_list.SelectedItem as Trader, true);
            Application.DoEvents();
        }
#endif
    }
}
