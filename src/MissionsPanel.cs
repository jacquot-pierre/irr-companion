using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>The story drawn as a tree: missions from top to bottom, branches side by side, a bar where the
    /// next mission waits for all the missions given so far, and the side missions a mission gives.</summary>
    public sealed class StoryTree : Panel
    {
        private sealed class Node
        {
            public Rectangle Rect;
            public Mission Mission;
            public bool Chip;
        }

        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Point[]> _lines = new List<Point[]>();
        private readonly List<Point[]> _chipLines = new List<Point[]>();
        private readonly List<KeyValuePair<Rectangle, string>> _bars = new List<KeyValuePair<Rectangle, string>>();
        private Rectangle _end;
        private Node _hot;
        private Mission _selected;
        private readonly ToolTip _tip;
        private string _tipText;
        public event EventHandler SelectedChanged;

        public StoryTree(ToolTip tip)
        {
            _tip = tip;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Theme.Back;
            Theme.DarkScrollbars(this);
        }

        private static int BoxW { get { return Theme.S(212); } }
        private static int BoxH { get { return Theme.S(52); } }
        private static int Gap { get { return Theme.S(30); } }
        private static int Edge { get { return Theme.S(14); } }

        /// <summary>Width the tree needs (two branches side by side, room for the side missions).</summary>
        public static int NeededWidth { get { return Edge * 2 + BoxW * 2 + Theme.S(48) + SystemInformation.VerticalScrollBarWidth; } }

        public Mission Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                var n = _nodes.FirstOrDefault(x => x.Mission == value);
                if (n != null) ScrollIntoView(n.Rect);
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        private void ScrollIntoView(Rectangle r)
        {
            int top = -AutoScrollPosition.Y;
            if (r.Top - Theme.S(20) < top) AutoScrollPosition = new Point(0, Math.Max(0, r.Top - Theme.S(20)));
            else if (r.Bottom + Theme.S(20) > top + ClientSize.Height) AutoScrollPosition = new Point(0, r.Bottom + Theme.S(20) - ClientSize.Height);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Build();
        }

        private void Build()
        {
            _nodes.Clear();
            _lines.Clear();
            _chipLines.Clear();
            _bars.Clear();
            int width = Math.Max(BoxW * 2 + Theme.S(24), ClientSize.Width - Edge * 2);
            var anchors = new List<Point>();
            int y = Seq(MissionData.Story, Edge, width, Edge + Theme.S(4), ref anchors);
            // end of the story
            var sz = TextRenderer.MeasureText("END · achievement " + MissionData.Achievement, Theme.Small);
            _end = new Rectangle(Edge + (width - sz.Width - Theme.S(24)) / 2, y, sz.Width + Theme.S(24), Theme.S(26));
            foreach (var a in anchors) _lines.Add(Elbow(a, new Point(_end.X + _end.Width / 2, _end.Y)));
            AutoScrollMinSize = new Size(0, _end.Bottom + Edge);
            Invalidate();
        }

        private static Point[] Elbow(Point a, Point b)
        {
            if (a.X == b.X) return new[] { a, b };
            int mid = b.Y - Theme.S(12);
            return new[] { a, new Point(a.X, mid), new Point(b.X, mid), b };
        }

        private int Seq(List<StoryItem> items, int x0, int width, int y, ref List<Point> anchors)
        {
            foreach (var it in items)
            {
                if (it.Branches != null)
                {
                    int k = it.Branches.Count, colW = width / k, maxY = y;
                    var ends = new List<Point>();
                    for (int i = 0; i < k; i++)
                    {
                        var a = new List<Point>(anchors);
                        int yy = Seq(it.Branches[i], x0 + i * colW, colW, y, ref a);
                        ends.AddRange(a);
                        maxY = Math.Max(maxY, yy);
                    }
                    anchors = ends;
                    y = maxY;
                    continue;
                }
                var m = it.Mission;
                int cx = x0 + width / 2;
                if (m.WaitAll)
                {
                    // synchronisation bar: every line above ends on it
                    var bar = new Rectangle(x0 + Theme.S(16), y + Theme.S(4), width - Theme.S(32), Theme.S(4));
                    foreach (var a in anchors) _lines.Add(new[] { a, new Point(a.X, bar.Y) });
                    var blockers = MissionData.Blockers(m);
                    _bars.Add(new KeyValuePair<Rectangle, string>(bar, "waits until all the story missions above are done" +
                        (blockers.Count > 0 ? ", and " + string.Join(", ", blockers.Select(MissionData.ShortTitle)) : "")));
                    anchors = new List<Point> { new Point(cx, bar.Bottom) };
                    y = bar.Bottom + Theme.S(26);
                }
                var r = new Rectangle(cx - BoxW / 2, y, BoxW, BoxH);
                foreach (var a in anchors) _lines.Add(Elbow(a, new Point(cx, r.Top)));
                _nodes.Add(new Node { Rect = r, Mission = m });
                int bottom = r.Bottom;
                // side missions it gives: small chips on the right of the box
                int chipY = r.Top;
                foreach (var id in m.Unlocks)
                {
                    Mission side;
                    if (!MissionData.Missions.TryGetValue(id, out side)) continue;
                    var ts = TextRenderer.MeasureText(ChipText(side), side.BlocksStory ? Theme.Bold : Theme.Small);
                    var cr = new Rectangle(r.Right + Theme.S(18), chipY, ts.Width + Theme.S(16), Theme.S(23));
                    _nodes.Add(new Node { Rect = cr, Mission = side, Chip = true });
                    _chipLines.Add(new[] { new Point(r.Right, cr.Y + cr.Height / 2), new Point(cr.X, cr.Y + cr.Height / 2) });
                    chipY = cr.Bottom + Theme.S(5);
                    bottom = Math.Max(bottom, cr.Bottom);
                }
                anchors = new List<Point> { new Point(cx, r.Bottom) };
                y = bottom + Gap;
            }
            return y;
        }

        /// <summary>Text at a position of the tree: TextRenderer (GDI) ignores the scroll transform of the Graphics.</summary>
        private void Txt(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags flags)
        {
            r.Offset(AutoScrollPosition);
            TextRenderer.DrawText(g, text, font, r, color, flags);
        }

        private static string ChipText(Mission m) { return (m.BlocksStory ? "! " : "+ ") + MissionData.ShortTitle(m); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.Border, Theme.S(2)))
                foreach (var l in _lines) g.DrawLines(p, l);
            using (var p = new Pen(Theme.TextFaint, 1) { DashStyle = DashStyle.Dash })
                foreach (var l in _chipLines) g.DrawLines(p, l);
            g.SmoothingMode = SmoothingMode.None;
            foreach (var kv in _bars)
            {
                var b = kv.Key;
                using (var br = new SolidBrush(Theme.Changed)) g.FillRectangle(br, b);
                Txt(g, kv.Value, Theme.Small, new Rectangle(b.X - Theme.S(12), b.Bottom + Theme.S(2), b.Width + Theme.S(24), Theme.S(18)), Theme.Changed,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            foreach (var n in _nodes) DrawNode(g, n);
            using (var br = new SolidBrush(Theme.Panel)) g.FillRectangle(br, _end);
            using (var p = new Pen(Theme.Accent)) g.DrawRectangle(p, _end.X, _end.Y, _end.Width - 1, _end.Height - 1);
            Txt(g, "END · achievement " + MissionData.Achievement, Theme.Small, _end, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawNode(Graphics g, Node n)
        {
            var m = n.Mission;
            bool sel = m == _selected, hot = n == _hot;
            var r = n.Rect;
            var mc = MissionData.MapColor(m);
            using (var b = new SolidBrush(sel ? Theme.AccentDark : hot ? Theme.RowHover : n.Chip ? Theme.Panel : Theme.Row)) g.FillRectangle(b, r);
            // a side mission that holds the story: yellow like the waits it holds
            bool blocks = n.Chip && m.BlocksStory;
            using (var p = new Pen(sel ? Theme.Accent : blocks ? Theme.Changed : hot ? Theme.TextDim : Theme.Border)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            const TextFormatFlags f = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (n.Chip)
            {
                Txt(g, ChipText(m), blocks ? Theme.Bold : Theme.Small, new Rectangle(r.X + Theme.S(8), r.Y, r.Width - Theme.S(10), r.Height),
                    blocks ? Theme.Changed : sel ? Theme.Text : Theme.TextDim, f);
                return;
            }
            using (var b = new SolidBrush(mc)) g.FillRectangle(b, r.X + 1, r.Y + 1, Theme.S(4), r.Height - 2);
            string num = m.Number.ToString(CultureInfo.InvariantCulture);
            int nw = Theme.S(30);
            Txt(g, num, Theme.Bold, new Rectangle(r.X + Theme.S(8), r.Y, nw, r.Height), Theme.TextFaint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var tr = new Rectangle(r.X + Theme.S(8) + nw, r.Y + Theme.S(5), r.Width - nw - Theme.S(14), Theme.S(22));
            Txt(g, m.Title, Theme.Bold, tr, Theme.Text, f);
            var sr = new Rectangle(tr.X, r.Y + Theme.S(26), tr.Width, Theme.S(20));
            Txt(g, m.MapText + (m.Place != null ? " · " + m.Place : ""), Theme.Small, sr, mc, f);
        }

        private Node HitTest(Point p)
        {
            p.Offset(-AutoScrollPosition.X, -AutoScrollPosition.Y);
            return _nodes.FirstOrDefault(n => n.Rect.Contains(p));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var n = HitTest(e.Location);
            if (n != _hot)
            {
                _hot = n;
                Cursor = n != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
                string t = n == null ? null : n.Chip
                    ? n.Mission.Title + ": side mission, given with " + MissionData.Missions[n.Mission.GivenBy].Title + "." +
                      (n.Mission.BlocksStory ? " The game counts it as a story mission: the yellow bars wait until it is done." : "")
                    : "Story mission " + n.Mission.Number + ": " + n.Mission.Title + " (" + n.Mission.MapText + ")";
                if (t != _tipText) { _tipText = t; _tip.SetToolTip(this, t); }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != null) { _hot = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var n = HitTest(e.Location);
            if (n != null) Selected = n.Mission;
        }

        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

        /// <summary>Up / down: previous / next mission of the story.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Down || keyData == Keys.Up)
            {
                var list = _nodes.Where(n => !n.Chip).Select(n => n.Mission).OrderBy(x => x.Number).ToList();
                int i = _selected == null ? -1 : list.IndexOf(_selected.IsSide ? MissionData.Missions[_selected.GivenBy] : _selected);
                i = Math.Max(0, Math.Min(list.Count - 1, i + (keyData == Keys.Down ? 1 : -1)));
                if (list.Count > 0) Selected = list[i];
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>The MISSIONS view: the story missions in their order (tree on the left), what to do in the selected
    /// one, what it gives, and its briefing.</summary>
    public sealed class MissionsPanel : Panel
    {
        private readonly ToolTip _tip;
        private readonly StoryTree _tree;
        private readonly Panel _content;
        private bool _briefingOpen;

        public MissionsPanel(ToolTip tip)
        {
            _tip = tip;
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            MissionData.Load();

            var left = new Panel { Dock = DockStyle.Left, Width = StoryTree.NeededWidth, BackColor = Theme.Back };
            var head = new SectionHeader("Story") { Height = Theme.S(36), Padding = new Padding(Theme.S(10), 0, 0, 0) };
            var hint = new WrapLabel
            {
                Text = "The FANG story from the game files, top to bottom. Side by side: branches given at the same time. Click a mission for what to do in it.",
                Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Back, Padding = new Padding(Theme.S(14), 0, Theme.S(10), Theme.S(4)),
            };
            _tree = new StoryTree(tip) { Dock = DockStyle.Fill };
            _tree.SelectedChanged += (s, e) => Show(_tree.Selected);
            left.Controls.Add(_tree);
            left.Controls.Add(hint);
            left.Controls.Add(head);
            var line = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border };

            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(Theme.S(20), Theme.S(8), Theme.S(20), Theme.S(20)) };
            Theme.DarkScrollbars(_content);
            Controls.Add(_content);
            Controls.Add(line);
            Controls.Add(left);
            var first = MissionData.Missions.Values.FirstOrDefault(m => m.Number == 1);
            if (first != null) _tree.Selected = first;
        }

        public Control Tree { get { return _tree; } }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETREDRAW = 0x000B;

        private void Show(Mission m)
        {
            bool frozen = _content.IsHandleCreated;
            if (frozen) SendMessage(_content.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                var scroll = _content.AutoScrollPosition;
                _content.SuspendLayout();
                var old = _content.Controls.Cast<Control>().ToList();
                _content.Controls.Clear();
                foreach (var c in old) c.Dispose();
                var items = new List<Control>();
                if (m != null) Build(m, items);
                for (int i = items.Count - 1; i >= 0; i--) _content.Controls.Add(items[i]);
                _content.ResumeLayout();
                _content.AutoScrollPosition = Point.Empty;
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

        private static Control CardHead(string title, string state)
        {
            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(40), BackColor = Theme.Row };
            var l = new Label { Text = title, Font = Theme.Header, ForeColor = Theme.Text, BackColor = Theme.Row, AutoSize = true, Location = new Point(Theme.S(12), Theme.S(9)), UseMnemonic = false };
            var s = new Label { Text = state ?? "", Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Row, AutoSize = true, UseMnemonic = false };
            head.Controls.Add(l);
            head.Controls.Add(s);
            head.Resize += (o, e) => s.Location = new Point(head.Width - s.Width - Theme.S(14), Theme.S(13));
            return head;
        }

        private static string Names(IEnumerable<string> ids)
        {
            return string.Join(", ", ids.Select(id => { Mission x; return MissionData.Missions.TryGetValue(id, out x) ? x.Title : id; }));
        }

        private static string Money(int v) { return v.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " "); }

        private void Build(Mission m, List<Control> items)
        {
            items.Add(new Label
            {
                Text = (m.IsSide ? "" : m.Number + ".  ") + m.Title.ToUpperInvariant(), Dock = DockStyle.Top, Height = Theme.S(40), Font = Theme.Title,
                ForeColor = Theme.Text, BackColor = Theme.Back, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true,
            });
            // where and when
            string where = (m.Maps.Count >= 4 ? "Any map" : m.MapText) + (m.Place != null ? " · " + m.Place : "");
            var when = new List<string>();
            if (m.IsSide)
                when.Add(m.Kind + " side mission, given with " + (m.GivenBy != null ? MissionData.Missions[m.GivenBy].Title : "?") + "." +
                         (m.BlocksStory ? "" : " Not needed for the story."));
            else
            {
                when.Add("Story mission " + m.Number + " of " + MissionData.Missions.Values.Count(x => !x.IsSide) + ".");
                when.Add(m.After.Count == 0 ? "The first mission." : "Comes after " + string.Join(" / ", m.After.Select(id => MissionData.Missions[id].Title)) + ".");
                if (m.WaitAll)
                {
                    var blockers = MissionData.Blockers(m);
                    when.Add("Waits until all the story missions given before it are done (both branches)" +
                             (blockers.Count > 0 ? ", and " + string.Join(", ", blockers.Select(x => x.Title)) + " if it is still in progress" : "") + ".");
                }
                if (m.Unlocks.Count > 0) when.Add("Also gives the side missions " + Names(m.Unlocks) + ".");
            }
            if (m.BlocksStory)
                items.Add(new WrapLabel
                {
                    Text = "Needed for the story: the game counts it as a story mission, so the story stops at the next yellow bar until it is done.",
                    Font = Theme.Bold, ForeColor = Theme.Changed, BackColor = Theme.Back, Padding = new Padding(0, 0, 0, Theme.S(4)),
                });
            var mapLine = new Label { Text = where, Dock = DockStyle.Top, Height = Theme.S(24), Font = Theme.Bold, ForeColor = MissionData.MapColor(m), BackColor = Theme.Back, UseMnemonic = false };
            items.Add(mapLine);
            items.Add(new WrapLabel { Text = string.Join(" ", when), Font = Theme.Body, ForeColor = Theme.TextDim, BackColor = Theme.Back, Padding = new Padding(0, 0, 0, Theme.S(10)) });

            // what to do
            var card = new Card();
            card.Add(CardHead("WHAT TO DO", m.Steps.Count == 1 ? "1 step" : m.Steps.Count + " steps, in this order"));
            int n = 0;
            foreach (var st in m.Steps)
            {
                n++;
                // a step without objectives of its own: its title is what to do
                card.Add(Caption("STEP " + n + (st.Objectives.Count > 0 ? "  ·  " + st.Title.ToUpperInvariant() : "")));
                var lines = st.Objectives.Count > 0 ? st.Objectives : new List<string> { st.Title == "Extract" ? "Extract from the map." : st.Title };
                foreach (var o in lines)
                {
                    var row = new InfoRow(o, null, null, Theme.Text, Theme.Accent, null, _tip, o);
                    card.Add(row);
                }
            }
            if (m.QuestItems.Count > 0)
            {
                card.Add(Caption("QUEST ITEMS"));
                foreach (var q in m.QuestItems) card.Add(new InfoRow(q, null, null, Theme.TextDim, null, null, _tip, null));
            }
            card.Done();
            items.Add(card);
            items.Add(Gap(12));
            foreach (var t in m.Tips)
            {
                items.Add(new WrapLabel { Text = "Tip: " + t, Font = Theme.Bold, ForeColor = Theme.Changed, BackColor = Theme.Back, Padding = new Padding(0, 0, 0, Theme.S(6)) });
                items.Add(Gap(6));
            }

            // rewards
            if (m.Rewards.Count > 0)
            {
                var rc = new Card();
                rc.Add(CardHead("REWARDS", m.Rewards.Count + (m.Rewards.Count == 1 ? " item" : " items")));
                foreach (var r in m.Rewards)
                {
                    var row = new InfoRow(r.Name, null, r.Id == "Cash" ? Money(r.Count) : "× " + r.Count, Theme.Text, null, null, _tip, r.Name + (r.Id == "Cash" ? "" : " × " + r.Count));
                    row.SetIcon(r.Icon ? Icons.Item(r.Id) : null);
                    rc.Add(row);
                }
                rc.Done();
                items.Add(rc);
                items.Add(Gap(12));
            }

            // briefing (closed by default: it tells the story)
            if (m.Briefing.Count > 0)
            {
                var th = new ToggleHeader("Briefing", "the story told by Observer (spoilers)") { Expanded = _briefingOpen };
                th.Toggled += (s, e) => { _briefingOpen = th.Expanded; Show(m); };
                items.Add(th);
                if (_briefingOpen)
                    foreach (var p in m.Briefing)
                        items.Add(new WrapLabel { Text = p, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Theme.Back, Padding = new Padding(Theme.S(4), Theme.S(4), 0, Theme.S(6)) });
            }
            items.Add(new WrapLabel
            {
                Text = "Read from the game's mission files. The kill counts are shown in game only.",
                Font = Theme.Small, ForeColor = Theme.TextFaint, BackColor = Theme.Back, Padding = new Padding(0, Theme.S(14), 0, 0),
            });
        }

#if DEBUG
        /// <summary>Test helper: selects a mission by id, briefing open or closed.</summary>
        public void TestShow(string id, bool briefing)
        {
            _briefingOpen = briefing;
            _tree.Selected = null;
            _tree.Selected = MissionData.Missions[id];
            Application.DoEvents();
        }
#endif
    }
}
