using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>Price table of the ITEMS view: a header with sortable columns, then the items grouped by sub-category.
    /// Owner-drawn (hundreds of rows), only the visible rows are painted.</summary>
    public sealed class ItemTable : Panel
    {
        public sealed class Column
        {
            public string Title, Tip;
            public Func<GameItem, double> Key;
        }

        private sealed class Line
        {
            public string Caption;
            public GameItem Item;
        }

        public static readonly Column[] Columns =
        {
            new Column { Title = "MARKET", Tip = "Buy at the market (cash). Small text: the faction loyalty level the market asks for.", Key = i => i.MarketBuy ? i.Price : 0 },
            new Column { Title = "BLACK MKT", Tip = "Buy at the black market, in Marked Coins.", Key = i => i.Coins },
            new Column { Title = "TRADER", Tip = "Buy from a raid trader (cash): the market price. Small text: which traders can have it in stock (weapons are not listed).", Key = i => i.TraderPrice },
            new Column { Title = "SELL", Tip = "What the market pays you (cash): the market price x the share of the item's category (weapons 35 %, parts 45 %, gear and ammo 40 %, barter 80 %, keys 20 %, others 60 %). Grey with \"?\": the game files say it cannot be sold at the market.", Key = i => i.Sell },
            new Column { Title = "TRADER PAYS", Tip = "What a raid trader pays you for it (cash): only the quest crates.", Key = i => i.HandIn },
        };

        private readonly List<Line> _lines = new List<Line>();
        private readonly ToolTip _tip;
        private int _hot = -1, _sort = -1;
        private string _tipText;

        public ItemTable(ToolTip tip)
        {
            _tip = tip;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Theme.Back;
            Theme.DarkScrollbars(this);
        }

        public static int ColW { get { return Theme.S(112); } }
        private static int RowH { get { return Theme.S(44); } }
        private static int CapH { get { return Theme.S(30); } }

        /// <summary>Column of the sort (-1: by name), descending for prices.</summary>
        public int Sort { get { return _sort; } set { _sort = value; } }

        public void SetItems(IEnumerable<GameItem> items, bool groups)
        {
            _lines.Clear();
            var list = items.ToList();
            Func<IEnumerable<GameItem>, IEnumerable<GameItem>> order = l => _sort < 0
                ? l.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : l.OrderByDescending(i => Columns[_sort].Key(i)).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
            if (groups)
            {
                foreach (var g in list.GroupBy(i => i.Sub ?? ""))
                {
                    if (g.Key.Length > 0) _lines.Add(new Line { Caption = g.Key.ToUpperInvariant() + "  ·  " + g.Count() });
                    foreach (var i in order(g)) _lines.Add(new Line { Item = i });
                }
            }
            else
                foreach (var i in order(list)) _lines.Add(new Line { Item = i });
            _hot = -1;
            AutoScrollPosition = Point.Empty;
            AutoScrollMinSize = new Size(0, _lines.Sum(l => l.Item != null ? RowH : CapH) + Theme.S(8));
            Invalidate();
        }

        private int Top(int index)
        {
            int y = 0;
            for (int i = 0; i < index; i++) y += _lines[i].Item != null ? RowH : CapH;
            return y;
        }

        private int IndexAt(int y)
        {
            int top = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                int h = _lines[i].Item != null ? RowH : CapH;
                if (y < top + h) return i;
                top += h;
            }
            return -1;
        }

        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int oy = AutoScrollPosition.Y, y = 0, w = ClientSize.Width;
            bool flat = _lines.All(z => z.Item != null);  // no group captions: show the sub-category under the name
            const TextFormatFlags left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            const TextFormatFlags right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            for (int i = 0; i < _lines.Count; i++)
            {
                var l = _lines[i];
                int h = l.Item != null ? RowH : CapH;
                var r = new Rectangle(0, y + oy, w, h);
                y += h;
                if (r.Bottom < 0) continue;
                if (r.Top > ClientSize.Height) break;
                if (l.Item == null)
                {
                    TextRenderer.DrawText(g, l.Caption, Theme.Small, new Rectangle(r.X + Theme.S(12), r.Y + Theme.S(8), r.Width, r.Height - Theme.S(8)), Theme.TextDim, left);
                    using (var p = new Pen(Theme.Track)) g.DrawLine(p, r.X + Theme.S(8), r.Bottom - 1, r.Right - Theme.S(8), r.Bottom - 1);
                    continue;
                }
                var it = l.Item;
                using (var b = new SolidBrush(i == _hot ? Theme.RowHover : Theme.Row)) g.FillRectangle(b, r.X, r.Y + 1, r.Width, r.Height - 2);
                // icon slot
                int s = h - Theme.S(8);
                var slot = new Rectangle(r.X + Theme.S(10), r.Y + Theme.S(4), s, s);
                using (var b = new SolidBrush(Color.FromArgb(62, 58, 53))) g.FillRectangle(b, slot);
                var icon = it.Icon ? Icons.Item(it.Id) : null;
                if (icon != null)
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(icon, Rectangle.Inflate(slot, -Theme.S(2), -Theme.S(2)));
                }
                // columns from the right
                int x = r.Right - Theme.S(12);
                var cells = new List<Rectangle>();
                for (int c = Columns.Length - 1; c >= 0; c--)
                {
                    x -= ColW;
                    cells.Insert(0, new Rectangle(x, r.Y, ColW - Theme.S(8), r.Height));
                }
                var nameR = new Rectangle(slot.Right + Theme.S(10), r.Y, x - slot.Right - Theme.S(16), r.Height);
                bool tier = it.Tier > 0;
                TextRenderer.DrawText(g, it.Name, Theme.Body, new Rectangle(nameR.X, nameR.Y + (tier ? Theme.S(3) : 0), nameR.Width, tier ? Theme.S(22) : nameR.Height), Theme.Text, left);
                if (tier)
                    TextRenderer.DrawText(g, "tier " + it.Tier + (it.Sub != null && flat ? "  ·  " + it.Sub : ""), Theme.Small,
                        new Rectangle(nameR.X, nameR.Y + Theme.S(22), nameR.Width, Theme.S(18)), Theme.TextFaint, left);
                Cell(g, cells[0], it.MarketBuy ? ItemData.Money(it.Price) : null, it.MarketBuy && it.LoyaltyFaction != null ? it.LoyaltyFaction + " loyalty " + it.LoyaltyLevel : null, Theme.Changed, right);
                Cell(g, cells[1], it.Coins > 0 ? ItemData.Money(it.Coins) + " MC" : null, null, Theme.ColorA, right);
                Cell(g, cells[2], it.TraderPrice > 0 ? ItemData.Money(it.TraderPrice) : null,
                    it.Traders.Count == 1 ? it.Traders[0].Who : it.Traders.Count > 1 ? it.Traders.Count + " traders" : null, Theme.Changed, right);
                Cell(g, cells[3], it.Sell > 0 ? ItemData.Money(it.Sell) + (it.NoSell ? " ?" : "") : null, null, it.NoSell ? Theme.TextFaint : Theme.Higher, right);
                Cell(g, cells[4], it.HandIn > 0 ? ItemData.Money(it.HandIn) : null, null, Theme.Higher, right);
            }
        }

        private static void Cell(Graphics g, Rectangle r, string value, string small, Color color, TextFormatFlags f)
        {
            if (value == null)
            {
                TextRenderer.DrawText(g, "—", Theme.Body, r, Theme.TextFaint, f);
                return;
            }
            if (small == null)
            {
                TextRenderer.DrawText(g, value, Theme.Bold, r, color, f);
                return;
            }
            TextRenderer.DrawText(g, value, Theme.Bold, new Rectangle(r.X, r.Y + Theme.S(3), r.Width, Theme.S(22)), color, f);
            TextRenderer.DrawText(g, small, Theme.Small, new Rectangle(r.X - Theme.S(30), r.Y + Theme.S(22), r.Width + Theme.S(30), Theme.S(18)), Theme.TextFaint,
                f | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Y - AutoScrollPosition.Y);
            if (i >= 0 && _lines[i].Item == null) i = -1;
            if (i == _hot) return;
            _hot = i;
            Invalidate();
            string t = i < 0 ? null : Describe(_lines[i].Item);
            if (t != _tipText) { _tipText = t; _tip.SetToolTip(this, t); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot >= 0) { _hot = -1; Invalidate(); }
        }

        public static string Describe(GameItem it)
        {
            var lines = new List<string> { it.Name + (it.Tier > 0 ? "  (tier " + it.Tier + ")" : "") };
            if (it.Price <= 0) lines.Add("No price in the game files.");
            if (it.MarketBuy)
                lines.Add("Market: " + ItemData.Money(it.Price) + " cash" + (it.LoyaltyFaction != null ? ", needs " + it.LoyaltyFaction + " loyalty " + it.LoyaltyLevel : ""));
            else if (it.Price > 0) lines.Add("Not sold at the market (market price " + ItemData.Money(it.Price) + ").");
            if (it.Coins > 0) lines.Add("Black market: " + ItemData.Money(it.Coins) + " Marked Coins");
            foreach (var t in it.Traders)
                lines.Add("Trader " + t.Who + " (" + t.Trader + "): " + ItemData.Money(it.Price) + " cash, in stock " + Math.Round(t.Chance * 100) + " % of the time");
            if (it.Sell > 0)
                lines.Add("Sell at the market: " + ItemData.Money(it.Sell) + " cash (" + Math.Round(ItemData.SellMultiplier(it.Category) * 100) + " % of the price)" +
                          (it.NoSell ? " - the game files say it cannot be sold there: to check in game" : ""));
            if (it.HandIn > 0) lines.Add("A raid trader pays " + ItemData.Money(it.HandIn) + " cash for it.");
            lines.Add(it.Id);
            return string.Join("\n", lines);
        }

#if DEBUG
        public int TestCount { get { return _lines.Count(l => l.Item != null); } }
#endif
    }

    /// <summary>Header of the price table: the column titles, click one to sort by it.</summary>
    public sealed class ItemTableHeader : Control
    {
        private readonly ItemTable _table;
        public event EventHandler SortChanged;

        public ItemTableHeader(ItemTable table, ToolTip tip)
        {
            _table = table;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = Theme.S(30);
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
            tip.SetToolTip(this, "Click a column to sort by it (highest first), click ITEM to sort by name. Hover a row for the details.");
        }

        private int ColumnAt(int x)
        {
            int right = Width - SystemInformation.VerticalScrollBarWidth - Theme.S(12);
            for (int c = ItemTable.Columns.Length - 1; c >= 0; c--)
            {
                right -= ItemTable.ColW;
                if (x >= right) return c;
            }
            return -1;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            _table.Sort = ColumnAt(e.X);
            Invalidate();
            if (SortChanged != null) SortChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int x = Width - SystemInformation.VerticalScrollBarWidth - Theme.S(12);
            const TextFormatFlags f = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            for (int c = ItemTable.Columns.Length - 1; c >= 0; c--)
            {
                x -= ItemTable.ColW;
                TextRenderer.DrawText(g, ItemTable.Columns[c].Title + (_table.Sort == c ? " ▼" : ""), Theme.Small,
                    new Rectangle(x, 0, ItemTable.ColW - Theme.S(8), Height), _table.Sort == c ? Theme.Accent : Theme.TextDim, f);
            }
            TextRenderer.DrawText(g, "ITEM" + (_table.Sort < 0 ? " ▲" : ""), Theme.Small, new Rectangle(Theme.S(12), 0, Theme.S(200), Height),
                _table.Sort < 0 ? Theme.Accent : Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using (var p = new Pen(Theme.Accent)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    /// <summary>The ITEMS view: every item of the game by category, with its buy and sell prices.</summary>
    public sealed class ItemsPanel : Panel
    {
        private readonly ListBox _cats;
        private readonly TextBox _search;
        private readonly ItemTable _table;
        private readonly Label _title, _sub;
        private readonly Timer _searchDelay = new Timer { Interval = 350 };

        public ItemsPanel(ToolTip tip)
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            ItemData.Load();

            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(250), BackColor = Theme.Panel, Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(12), Theme.S(12)) };
            var head = new SectionHeader("Categories") { BackColor = Theme.Panel, Height = Theme.S(36) };
            _search = Theme.Box();
            _search.Dock = DockStyle.Top;
            _search.TextChanged += (s, e) => { _searchDelay.Stop(); _searchDelay.Start(); };
            _searchDelay.Tick += (s, e) => { _searchDelay.Stop(); Refresh2(); };
            _search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && _search.Text.Length > 0) { e.SuppressKeyPress = true; _search.Text = ""; } };
            _search.HandleCreated += (s, e) => SendMessage(_search.Handle, 0x1501, (IntPtr)1, "Find an item...");
            tip.SetToolTip(_search, "Items whose name contains this text, in every category (Esc to clear).");
            var gap = new Panel { Dock = DockStyle.Top, Height = Theme.S(6), BackColor = Theme.Panel };
            _cats = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(30), IntegralHeight = false, Font = Theme.Bold,
            };
            _cats.Items.Add("All items");
            foreach (var c in ItemData.Categories) _cats.Items.Add(c);
            _cats.DrawItem += DrawCat;
            _cats.SelectedIndexChanged += (s, e) => Refresh2();
            Theme.DarkScrollbars(_cats);
            var hint = new WrapLabel
            {
                Text = "Prices read from the game's vendor table and vendor settings. Weapons are priced new, with their default parts. Ammo: price of one round.",
                Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Panel, Dock = DockStyle.Bottom,
            };
            left.Controls.Add(_cats);
            left.Controls.Add(gap);
            left.Controls.Add(_search);
            left.Controls.Add(head);
            left.Controls.Add(hint);

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(Theme.S(16), Theme.S(8), Theme.S(8), 0) };
            _title = new Label { Dock = DockStyle.Top, Height = Theme.S(36), Font = Theme.Title, ForeColor = Theme.Text, BackColor = Theme.Back, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            _sub = new Label { Dock = DockStyle.Top, Height = Theme.S(22), Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Back, UseMnemonic = false, AutoEllipsis = true };
            _table = new ItemTable(tip) { Dock = DockStyle.Fill };
            var header = new ItemTableHeader(_table, tip);
            header.SortChanged += (s, e) => Refresh2();
            right.Controls.Add(_table);
            right.Controls.Add(header);
            right.Controls.Add(_sub);
            right.Controls.Add(_title);
            Controls.Add(right);
            Controls.Add(left);
            _cats.SelectedIndex = 1;
        }

        public Control List { get { return _cats; } }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private void DrawCat(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Theme.S(4), Theme.S(3), e.Bounds.Height - Theme.S(8));
            var item = _cats.Items[e.Index];
            var c = item as ItemCategory;
            string name = c != null ? c.Name : (string)item;
            int n = c != null ? c.Count : ItemData.Items.Count;
            var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(18), e.Bounds.Height);
            TextRenderer.DrawText(g, name.ToUpperInvariant(), Theme.Bold, r, c == null ? Theme.Accent : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, n.ToString(CultureInfo.InvariantCulture), Theme.Small, r, Theme.TextFaint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        private void Refresh2()
        {
            string q = _search.Text.Trim();
            var cat = _cats.SelectedItem as ItemCategory;
            IEnumerable<GameItem> items = ItemData.Items;
            if (q.Length > 0)
                items = items.Where(i => i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || i.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            else if (cat != null)
                items = items.Where(i => i.Category == cat.Id);
            var list = items.ToList();
            _title.Text = q.Length > 0 ? "\"" + q + "\"" : (cat != null ? cat.Name : "All items").ToUpperInvariant();
            _sub.Text = list.Count + (list.Count == 1 ? " item" : " items") +
                        (cat != null && q.Length == 0 ? "  ·  sold at the market: " + Math.Round(ItemData.SellMultiplier(cat.Id) * 100) + " % of the price" : "") +
                        "  ·  MC = Marked Coins  ·  hover a row for the details";
            _table.SetItems(list, cat != null && q.Length == 0);
        }

#if DEBUG
        /// <summary>Test helper: a category (index in the list, 0 = all) and a search.</summary>
        public void TestShow(int category, string search, int sort)
        {
            _search.Text = search ?? "";
            _table.Sort = sort;
            _cats.SelectedIndex = category;
            Refresh2();
            Application.DoEvents();
        }
#endif
    }
}
