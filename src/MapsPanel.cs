using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>Zoomable / draggable minimap with resource markers and a hover tooltip.</summary>
    public sealed class MapCanvas : Control
    {
        private MapFloor _floor;
        private List<MapPoint> _points = new List<MapPoint>();
        private float _scale = 1f;           // screen px per image px
        private PointF _offset;              // screen position of the image's top-left corner
        private Point _dragStart;
        private PointF _dragOffset;
        private bool _dragging;
        private MapPoint _hover;

        public static readonly Color ExtractionColor = Color.FromArgb(0, 230, 118);

        public MapCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(16, 14, 13);
            Cursor = Cursors.Hand;
        }

        private Size ImageSize { get { var b = _floor == null ? null : _floor.Bitmap; return b == null ? new Size(1, 1) : b.Size; } }

        /// <summary>Shows a floor and its visible points; keeps the view unless the floor changed.</summary>
        public void SetContent(MapFloor floor, List<MapPoint> points)
        {
            bool changed = floor != _floor;
            _floor = floor;
            _points = points;
            _hover = null;
            if (changed) Fit();
            Invalidate();
        }

        public void Fit()
        {
            var s = ImageSize;
            if (Width <= 1 || Height <= 1) return;
            _scale = Math.Min(Width / (float)s.Width, Height / (float)s.Height);
            _offset = new PointF((Width - s.Width * _scale) / 2, (Height - s.Height * _scale) / 2);
            Invalidate();
        }

        public void ZoomAt(PointF screen, float factor)
        {
            var s = ImageSize;
            float min = Math.Min(Width / (float)s.Width, Height / (float)s.Height) * 0.9f;
            float max = 4f;
            float ns = Math.Max(min, Math.Min(max, _scale * factor));
            float f = ns / _scale;
            _offset = new PointF(screen.X - (screen.X - _offset.X) * f, screen.Y - (screen.Y - _offset.Y) * f);
            _scale = ns;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_floor != null && _scale <= Math.Min(Width / (float)ImageSize.Width, Height / (float)ImageSize.Height) + 0.001f) Fit();
        }

        private PointF Screen(MapPoint p)
        {
            var n = _floor.ToImage(p.X, p.Y);
            var s = ImageSize;
            return new PointF(_offset.X + n.X * s.Width * _scale, _offset.Y + n.Y * s.Height * _scale);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_floor == null || _floor.Bitmap == null)
            {
                TextRenderer.DrawText(g, "No map image.", Theme.Body, ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            var s = ImageSize;
            g.InterpolationMode = _scale < 1 ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_floor.Bitmap, new RectangleF(_offset.X, _offset.Y, s.Width * _scale, s.Height * _scale));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            int r = Theme.S(10);
            foreach (var p in _points.OrderBy(p => p == _hover))
            {
                var c = Screen(p);
                if (c.X < -r || c.Y < -r || c.X > Width + r || c.Y > Height + r) continue;
                bool hot = p == _hover;
                float rr = hot ? r * 1.35f : r;
                if (p.IsExtraction)
                {
                    var tri = new[] { new PointF(c.X, c.Y - rr * 1.15f), new PointF(c.X + rr, c.Y + rr * 0.75f), new PointF(c.X - rr, c.Y + rr * 0.75f) };
                    using (var b = new SolidBrush(ExtractionColor)) g.FillPolygon(b, tri);
                    using (var pen = new Pen(Color.Black, 1.6f)) g.DrawPolygon(pen, tri);
                    continue;
                }
                Color col;
                HideoutPanel.ResourceColors.TryGetValue(p.Type, out col);
                using (var b = new SolidBrush(col)) g.FillEllipse(b, c.X - rr, c.Y - rr, 2 * rr, 2 * rr);
                using (var pen = new Pen(Color.FromArgb(20, 18, 16), hot ? 2.4f : 1.6f)) g.DrawEllipse(pen, c.X - rr, c.Y - rr, 2 * rr, 2 * rr);
                var icon = Icons.Material(p.Type, Color.FromArgb(28, 26, 24));
                if (icon != null)
                {
                    float ir = rr * 0.72f;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(icon, c.X - ir, c.Y - ir, 2 * ir, 2 * ir);
                }
            }
            if (_hover != null) DrawTooltip(g, _hover, Screen(_hover));
        }

        private void DrawTooltip(Graphics g, MapPoint p, PointF at)
        {
            var lines = new List<KeyValuePair<string, Font>>();
            if (p.IsExtraction)
            {
                lines.Add(new KeyValuePair<string, Font>("Extraction point", Theme.Bold));
            }
            else
            {
                var sp = MapData.Spawn(p.Type);
                lines.Add(new KeyValuePair<string, Font>(p.Type, Theme.Bold));
                if (sp != null)
                {
                    lines.Add(new KeyValuePair<string, Font>(string.Format(CultureInfo.InvariantCulture, "{0:0} % chance to be there", sp.Chance * 100), Theme.Body));
                    lines.Add(new KeyValuePair<string, Font>(sp.Amount + " units when collected", Theme.Body));
                }
            }
            int pad = Theme.S(8), w = 0, h = pad;
            foreach (var l in lines)
            {
                var sz = TextRenderer.MeasureText(g, l.Key, l.Value);
                w = Math.Max(w, sz.Width);
                h += sz.Height;
            }
            w += 2 * pad;
            h += pad;
            float x = at.X + Theme.S(16), y = at.Y - h / 2f;
            if (x + w > Width - 4) x = at.X - Theme.S(16) - w;
            y = Math.Max(4, Math.Min(Height - h - 4, y));
            var rect = new Rectangle((int)x, (int)y, w, h);
            using (var b = new SolidBrush(Color.FromArgb(235, 33, 29, 27))) g.FillRectangle(b, rect);
            Color col = p.IsExtraction ? ExtractionColor : (HideoutPanel.ResourceColors.ContainsKey(p.Type) ? HideoutPanel.ResourceColors[p.Type] : Theme.Border);
            using (var pen = new Pen(col)) g.DrawRectangle(pen, rect);
            int ty = rect.Y + pad;
            for (int i = 0; i < lines.Count; i++)
            {
                var sz = TextRenderer.MeasureText(g, lines[i].Key, lines[i].Value);
                TextRenderer.DrawText(g, lines[i].Key, lines[i].Value, new Point(rect.X + pad, ty), i == 0 ? col : Theme.Text);
                ty += sz.Height;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomAt(e.Location, e.Delta > 0 ? 1.25f : 0.8f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragStart = e.Location;
            _dragOffset = _offset;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Fit();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                _offset = new PointF(_dragOffset.X + e.X - _dragStart.X, _dragOffset.Y + e.Y - _dragStart.Y);
                Invalidate();
                return;
            }
            if (_floor == null) return;
            MapPoint best = null;
            float bestD = Theme.S(13) * Theme.S(13);
            foreach (var p in _points)
            {
                var c = Screen(p);
                float d = (c.X - e.X) * (c.X - e.X) + (c.Y - e.Y) * (c.Y - e.Y);
                if (d <= bestD) { bestD = d; best = p; }
            }
            if (best != _hover) { _hover = best; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { _hover = null; Invalidate(); }
        }

#if DEBUG
        public void TestHover(int index)
        {
            _hover = index >= 0 && index < _points.Count ? _points[index] : null;
            if (_hover != null)
            {
                // centre the view on the hovered point at a closer zoom
                var s = ImageSize;
                var n = _floor.ToImage(_hover.X, _hover.Y);
                _scale = Math.Min(Width / (float)s.Width, Height / (float)s.Height) * 1.8f;
                _offset = new PointF(Width / 2f - n.X * s.Width * _scale, Height / 2f - n.Y * s.Height * _scale);
            }
            Invalidate();
        }
#endif
    }

    /// <summary>One toggle of the filter list: [box] icon name ...... count.</summary>
    public sealed class FilterRow : Control
    {
        public readonly string Type;
        private bool _checked = true, _hot;
        public string CountText = "";
        public event EventHandler Toggled;

        public FilterRow(string type)
        {
            Type = type;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = Theme.S(34);
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
        }

        public bool Checked { get { return _checked; } set { _checked = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_hot ? Theme.RowHover : BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = Theme.S(16), y = (Height - box) / 2;
            var br = new Rectangle(Theme.S(4), y, box, box);
            using (var p = new Pen(_checked ? Theme.Accent : Theme.Border, 1.5f)) g.DrawRectangle(p, br);
            if (_checked)
                using (var p = new Pen(Theme.Accent, 2f))
                    g.DrawLines(p, new[] { new Point(br.X + 3, br.Y + box / 2), new Point(br.X + box / 2 - 1, br.Bottom - 4), new Point(br.Right - 3, br.Y + 3) });
            int ix = Theme.S(30), isz = Theme.S(22);
            var ir = new Rectangle(ix, (Height - isz) / 2, isz, isz);
            if (Type == MapData.Extraction)
            {
                var c = new PointF(ir.X + isz / 2f, ir.Y + isz / 2f);
                float rr = isz / 2.4f;
                var tri = new[] { new PointF(c.X, c.Y - rr * 1.15f), new PointF(c.X + rr, c.Y + rr * 0.75f), new PointF(c.X - rr, c.Y + rr * 0.75f) };
                using (var b = new SolidBrush(MapCanvas.ExtractionColor)) g.FillPolygon(b, tri);
            }
            else
            {
                var icon = Icons.Material(Type, HideoutPanel.ResourceColors[Type]);
                if (icon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(icon, ir); }
            }
            var col = _checked ? Theme.Text : Theme.TextFaint;
            var tr = new Rectangle(ix + isz + Theme.S(10), 0, Width - ix - isz - Theme.S(10), Height);
            TextRenderer.DrawText(g, Type == MapData.Extraction ? "Extraction points" : Type, Theme.Bold, tr, col, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            TextRenderer.DrawText(g, CountText, Theme.Small, tr, Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Checked = !Checked;
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }
    }

    /// <summary>The MAPS view: resource spawn points and extraction points on the game minimaps.</summary>
    public sealed class MapsPanel : Panel
    {
        private readonly ListBox _maps, _floors;
        private readonly Panel _floorBox;
        private readonly MapCanvas _canvas;
        private readonly List<FilterRow> _filters = new List<FilterRow>();
        private readonly Label _summary, _title;

        public MapsPanel(ToolTip tip)
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            MapData.Load();

            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(260), BackColor = Theme.Panel, Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(12), Theme.S(12)) };
            var mapsHeader = new SectionHeader("Map") { BackColor = Theme.Panel, Height = Theme.S(36) };
            _maps = new ListBox
            {
                Dock = DockStyle.Top, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(40), IntegralHeight = false, Font = Theme.Bold,
            };
            foreach (var m in MapData.Maps) _maps.Items.Add(m);
            _maps.Height = _maps.ItemHeight * _maps.Items.Count + 2;
            _maps.DrawItem += DrawMap;
            _maps.SelectedIndexChanged += (s, e) => MapChanged();

            _floorBox = new Panel { Dock = DockStyle.Top, BackColor = Theme.Panel };
            var floorHeader = new SectionHeader("Floor") { BackColor = Theme.Panel, Height = Theme.S(36) };
            _floors = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(30), IntegralHeight = false, Font = Theme.Body,
            };
            _floors.DrawItem += DrawFloor;
            _floors.SelectedIndexChanged += (s, e) => Refresh2();
            _floorBox.Controls.Add(_floors);
            _floorBox.Controls.Add(floorHeader);

            var showHeader = new SectionHeader("Show") { BackColor = Theme.Panel, Height = Theme.S(40) };
            var filterBox = new Panel { Dock = DockStyle.Top, BackColor = Theme.Panel };
            var types = MapData.Resources.Select(r => r.Type).Concat(new[] { MapData.Extraction }).ToList();
            for (int i = types.Count - 1; i >= 0; i--)
            {
                var f = new FilterRow(types[i]);
                f.Toggled += (s, e) => Refresh2();
                _filters.Insert(0, f);
                filterBox.Controls.Add(f);
            }
            filterBox.Height = _filters.Sum(f => f.Height);
            tip.SetToolTip(filterBox, "Click to show or hide a resource.");

            _summary = new Label { Dock = DockStyle.Top, Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Back, AutoSize = false, Height = Theme.S(24), UseMnemonic = false, AutoEllipsis = true };
            tip.SetToolTip(_summary, "Each point has its own chance to be there when the raid starts.");
            var hint = Theme.Lbl("Wheel: zoom · drag: move · double-click: fit. Hover a point for its chance.", Theme.Small, Theme.TextDim);
            hint.AutoSize = false;
            hint.Dock = DockStyle.Bottom;
            hint.Height = Theme.S(44);

            left.Controls.Add(filterBox);
            left.Controls.Add(showHeader);
            left.Controls.Add(_floorBox);
            left.Controls.Add(_maps);
            left.Controls.Add(mapsHeader);
            left.Controls.Add(hint);

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(Theme.S(12)) };
            _title = new Label { Dock = DockStyle.Top, Height = Theme.S(36), Font = Theme.Title, ForeColor = Theme.Text, BackColor = Theme.Back, UseMnemonic = false };
            _canvas = new MapCanvas { Dock = DockStyle.Fill };
            right.Controls.Add(_canvas);
            right.Controls.Add(_summary);
            right.Controls.Add(_title);

            Controls.Add(right);
            Controls.Add(left);
            if (_maps.Items.Count > 0) _maps.SelectedIndex = 0;
        }

        public Control Canvas { get { return _canvas; } }

        private GameMap Map { get { return _maps.SelectedItem as GameMap; } }

        private int FloorIndex { get { return Math.Max(0, _floors.SelectedIndex); } }

        private void MapChanged()
        {
            var m = Map;
            if (m == null) return;
            _floors.BeginUpdate();
            _floors.Items.Clear();
            foreach (var f in m.Floors) _floors.Items.Add(f);
            _floors.EndUpdate();
            bool multi = m.Floors.Count > 1;
            _floorBox.Visible = multi;
            _floorBox.Height = multi ? Theme.S(36) + _floors.ItemHeight * m.Floors.Count + 2 : 0;
            if (m.Floors.Count > 0) _floors.SelectedIndex = 0;
            else Refresh2();
        }

        private void Refresh2()
        {
            var m = Map;
            if (m == null || m.Floors.Count == 0) return;
            int fi = FloorIndex;
            var floor = m.Floors[fi];
            bool multi = m.Floors.Count > 1;
            foreach (var f in _filters) f.CountText = m.Count(f.Type, multi ? fi : -1).ToString(CultureInfo.InvariantCulture);
            foreach (var f in _filters) f.Invalidate();
            var shown = new HashSet<string>(_filters.Where(f => f.Checked).Select(f => f.Type));
            var pts = m.Points.Where(p => shown.Contains(p.Type) && (!multi || p.Floor == fi)).ToList();
            _title.Text = m.Name.ToUpperInvariant() + (multi ? "  ·  " + floor.Name.ToUpperInvariant() : "");
            _canvas.SetContent(floor, pts);

            // average per raid for the whole map
            var parts = new List<string>();
            foreach (var r in MapData.Resources)
            {
                int n = m.Count(r.Type);
                if (n == 0) continue;
                double exp = n * r.Chance;
                string units = (exp * r.Amount).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} points, ≈ {2:0.#} present (≈ {3} units)", r.Type, n, exp, units));
            }
            _summary.Text = "On average per raid (whole map):   " + string.Join("   ·   ", parts);
            _maps.Invalidate();
            _floors.Invalidate();
        }

        private void DrawMap(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            var m = (GameMap)_maps.Items[e.Index];
            bool sel = e.Index == _maps.SelectedIndex;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Theme.S(6), Theme.S(3), e.Bounds.Height - Theme.S(12));
            var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(16), e.Bounds.Height);
            TextRenderer.DrawText(g, m.Name.ToUpperInvariant(), Theme.Bold, new Rectangle(r.X, r.Y + Theme.S(3), r.Width, Theme.S(20)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            int res = m.Points.Count(p => !p.IsExtraction);
            string sub = res + " resource points" + (m.Floors.Count > 1 ? " · " + m.Floors.Count + " floors" : "");
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(r.X, r.Y + Theme.S(21), r.Width, Theme.S(16)), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void DrawFloor(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            var m = Map;
            var f = (MapFloor)_floors.Items[e.Index];
            bool sel = e.Index == _floors.SelectedIndex;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Theme.S(5), Theme.S(3), e.Bounds.Height - Theme.S(10));
            var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(16), e.Bounds.Height);
            string label = f.Name + (e.Index == 0 ? "  (top)" : e.Index == _floors.Items.Count - 1 ? "  (bottom)" : "");
            TextRenderer.DrawText(g, label, Theme.Body, r, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            int n = m == null ? 0 : m.Points.Count(p => !p.IsExtraction && p.Floor == e.Index);
            TextRenderer.DrawText(g, n == 0 ? "-" : n + " pts", Theme.Small, r, n == 0 ? Theme.TextFaint : Theme.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

#if DEBUG
        /// <summary>Test helper: shows a map / floor, optionally with one point hovered.</summary>
        public void TestShow(string map, int floor, int hover)
        {
            _maps.SelectedItem = MapData.Maps.First(m => m.Name == map);
            if (floor >= 0 && floor < _floors.Items.Count) _floors.SelectedIndex = floor;
            Application.DoEvents();
            _canvas.Fit();
            if (hover >= 0) _canvas.TestHover(hover);
        }
#endif
    }
}
