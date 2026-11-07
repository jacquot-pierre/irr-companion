using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>Editor of one command argument: label + the control that fits its kind.</summary>
    public sealed class ArgEditor : Panel
    {
        public readonly ArgDef A;
        public event EventHandler Changed;

        private readonly Label _name, _help;
        private readonly CheckBox _include;
        private Segmented _seg;
        private DarkCombo _combo, _combo2;
        private ComboBox _edit;
        private Slider _slider;
        private TextBox _box;
        private List<string> _segValues;
        private bool _loading, _ready;
        private string _value;

        private static readonly List<string> AllItemNames = CommandData.Items.Select(x => x.Key).ToList();
        private const string AllItems = "All items";

        public ArgEditor(ArgDef a, ToolTip tip)
        {
            A = a;
            Dock = DockStyle.Top;
            BackColor = Theme.Back;
            Padding = new Padding(0, Theme.S(4), 0, Theme.S(4));

            string title = a.Name.ToUpperInvariant() + (string.IsNullOrEmpty(a.Unit) ? "" : "  (" + a.Unit + ")");
            if (a.Optional)
            {
                _include = new CheckBox
                {
                    Text = title, Font = Theme.Bold, ForeColor = Theme.Text, AutoSize = false, BackColor = Theme.Back,
                    Checked = a.Default != null && a.Default != "",
                };
                _include.CheckedChanged += (s, e) => { UpdateEnabled(); if (!_loading) Changed?.Invoke(this, EventArgs.Empty); };
                tip.SetToolTip(_include, "Optional argument: untick to leave it out of the command.");
                Controls.Add(_include);
            }
            else
            {
                _name = new Label { Text = title, Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Theme.Back, AutoEllipsis = true };
                Controls.Add(_name);
            }
            _help = new Label { Text = a.Help ?? "", Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Back, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight };
            Controls.Add(_help);

            switch (a.Kind)
            {
                case "toggle":
                    MakeSegmented(new[] { "0", "1" }, new[] { a.Off, a.On });
                    break;
                case "choice":
                    if (a.Editable)
                    {
                        _edit = MakeEditCombo(a.Options.Select(o => o.Key));
                    }
                    else if (a.Options.Count <= 4)
                    {
                        MakeSegmented(a.Options.Select(o => o.Key).ToArray(), a.Options.Select(o => o.Value).ToArray());
                    }
                    else
                    {
                        _combo = new DarkCombo();
                        foreach (var o in a.Options) _combo.Items.Add(o.Value);
                        _combo.SelectedIndexChanged += (s, e) =>
                        {
                            if (_loading || _combo.SelectedIndex < 0) return;
                            _value = a.Options[_combo.SelectedIndex].Key;
                            Raise();
                        };
                        Controls.Add(_combo);
                    }
                    break;
                case "int":
                case "float":
                    if (a.IsBounded)
                    {
                        double step = a.Step ?? (a.Kind == "int" ? 1 : 0.1);
                        _slider = new Slider
                        {
                            Min = a.Min.Value, Max = a.Max.Value, Increment = step,
                            Precision = a.Kind == "int" ? 0 : Decimals(step), BackColor = Theme.Row,
                        };
                        _slider.ValueChanged += (s, e) =>
                        {
                            if (_loading) return;
                            _value = CommandData.Fmt(_slider.Value);
                            _loading = true;
                            _box.Text = _value;
                            _loading = false;
                            Raise();
                        };
                        Controls.Add(_slider);
                    }
                    _box = Theme.Box();
                    _box.TextAlign = HorizontalAlignment.Right;
                    _box.Font = Theme.Bold;
                    _box.TextChanged += (s, e) =>
                    {
                        if (_loading) return;
                        double d;
                        string t = _box.Text.Trim().Replace(',', '.');
                        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { _box.ForeColor = Theme.Lower; return; }
                        if (a.Kind == "int") d = Math.Round(d);
                        _box.ForeColor = Theme.Text;
                        _value = CommandData.Fmt(d);
                        if (_slider != null) { _loading = true; _slider.Value = d; _loading = false; }
                        Raise();
                    };
                    tip.SetToolTip(_box, "Type any value" + (a.IsBounded ? " (the slider covers " + CommandData.Fmt(a.Min.Value) + " to " + CommandData.Fmt(a.Max.Value) + ")." : "."));
                    Controls.Add(_box);
                    break;
                case "item":
                    _combo2 = new DarkCombo();
                    _combo2.Items.Add(AllItems);
                    foreach (var c in CommandData.Items.Select(x => x.Value).Distinct()) _combo2.Items.Add(c);
                    _combo2.SelectedIndexChanged += (s, e) => { if (!_loading) FillItems(); };
                    tip.SetToolTip(_combo2, "Filter the item list by category.");
                    Controls.Add(_combo2);
                    _edit = MakeEditCombo(new string[0]);
                    var ac = new AutoCompleteStringCollection();
                    ac.AddRange(AllItemNames.ToArray());
                    _edit.AutoCompleteCustomSource = ac;
                    _edit.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    _edit.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    tip.SetToolTip(_edit, "Internal item name. Type to search; part of a name also works in the game.");
                    _loading = true;
                    _combo2.SelectedIndex = 0;
                    _loading = false;
                    FillItems();
                    break;
                case "weather":
                    _combo2 = new DarkCombo();
                    foreach (var m in CommandData.Maps) _combo2.Items.Add(m.Map);
                    _combo2.SelectedIndexChanged += (s, e) =>
                    {
                        if (_loading) return;
                        int keep = _combo == null ? 0 : _combo.SelectedIndex;
                        FillWeathers(keep);
                        Raise();
                    };
                    tip.SetToolTip(_combo2, "Each map has its own list of weathers.");
                    Controls.Add(_combo2);
                    _combo = new DarkCombo();
                    _combo.SelectedIndexChanged += (s, e) =>
                    {
                        if (_loading || _combo.SelectedIndex < 0) return;
                        _value = (_combo.SelectedIndex + CommandData.WeatherIndexBase).ToString(CultureInfo.InvariantCulture);
                        Raise();
                    };
                    tip.SetToolTip(_combo, "The percentage is the chance of this weather when a raid starts.");
                    Controls.Add(_combo);
                    break;
                default: // text
                    _box = Theme.Box();
                    _box.TextChanged += (s, e) => { if (!_loading) { _value = _box.Text; Raise(); } };
                    Controls.Add(_box);
                    break;
            }
            Height = Theme.S(_combo2 != null ? 106 : 70);
            Reset();
            _ready = true;
            PerformLayout();
        }

        private static int Decimals(double step)
        {
            string s = CommandData.Fmt(step);
            int i = s.IndexOf('.');
            return i < 0 ? 0 : s.Length - i - 1;
        }

        private void MakeSegmented(string[] values, string[] labels)
        {
            _segValues = values.ToList();
            _seg = new Segmented(labels) { BackColor = Theme.Row };
            _seg.OptionClicked += (s, e) =>
            {
                _seg.Selected = _seg.ClickedIndex;
                _value = _segValues[_seg.ClickedIndex];
                Raise();
            };
            Controls.Add(_seg);
        }

        private ComboBox MakeEditCombo(IEnumerable<string> items)
        {
            var cb = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat, BackColor = Theme.Row, ForeColor = Theme.Text,
                Font = Theme.Body, MaxDropDownItems = 20, IntegralHeight = false,
            };
            foreach (var i in items) cb.Items.Add(i);
            cb.TextChanged += (s, e) => { if (!_loading) { _value = cb.Text; Raise(); } };
            Controls.Add(cb);
            return cb;
        }

        private void FillItems()
        {
            string cat = _combo2.SelectedItem as string;
            string keep = _edit.Text;
            _loading = true;
            _edit.BeginUpdate();
            _edit.Items.Clear();
            foreach (var it in CommandData.Items)
                if (cat == null || cat == AllItems || it.Value == cat) _edit.Items.Add(it.Key);
            _edit.EndUpdate();
            _edit.Text = keep;
            _loading = false;
        }

        private void FillWeathers(int select)
        {
            var m = CommandData.Maps[Math.Max(0, _combo2.SelectedIndex)];
            double total = m.Weights.Sum();
            _loading = true;
            _combo.Items.Clear();
            for (int i = 0; i < m.Weathers.Count; i++)
                _combo.Items.Add(string.Format(CultureInfo.InvariantCulture, "{0}   ({1:0}%)", m.Weathers[i], total > 0 ? m.Weights[i] / total * 100 : 0));
            int sel = Math.Max(0, Math.Min(select, _combo.Items.Count - 1));
            _combo.SelectedIndex = sel;
            _value = (sel + CommandData.WeatherIndexBase).ToString(CultureInfo.InvariantCulture);
            _loading = false;
        }

        /// <summary>The user changed the value: an optional argument left out is put back in.</summary>
        private void Raise()
        {
            if (_include != null && !_include.Checked) { _include.Checked = true; return; }
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        private void UpdateEnabled()
        {
            if (_include != null) _include.ForeColor = _include.Checked ? Theme.Text : Theme.TextFaint;
        }

        /// <summary>Token for the command line, or null when the optional argument is left out.</summary>
        public string Token
        {
            get
            {
                if (_include != null && !_include.Checked) return null;
                if (string.IsNullOrWhiteSpace(_value)) return null;
                return CommandData.Token(_value);
            }
        }

        /// <summary>Map selected for a weather argument.</summary>
        public string Map { get { return A.Kind == "weather" ? _combo2.SelectedItem as string : null; } }

        public void Reset() { SetToken(A.Default, A.Optional && string.IsNullOrEmpty(A.Default) ? (bool?)false : null, null); }

        /// <summary>Loads a value (from a favorite). included=false unticks an optional argument.</summary>
        public void SetToken(string token, bool? included, string map)
        {
            _loading = true;
            if (_include != null) _include.Checked = included ?? (token != null);
            string v = token;
            if (v == null) v = A.Default ?? "";
            switch (A.Kind)
            {
                case "toggle":
                case "choice":
                    if (_seg != null)
                    {
                        int i = _segValues.IndexOf(v);
                        if (i < 0) i = Math.Max(0, _segValues.IndexOf(A.Default ?? ""));
                        _seg.Selected = i;
                        v = _segValues[i];
                    }
                    else if (_combo != null)
                    {
                        int i = A.Options.FindIndex(o => o.Key == v);
                        if (i < 0) i = Math.Max(0, A.Options.FindIndex(o => o.Key == A.Default));
                        _combo.SelectedIndex = i;
                        v = A.Options[i].Key;
                    }
                    else _edit.Text = v;
                    break;
                case "int":
                case "float":
                    _box.Text = v;
                    _box.ForeColor = Theme.Text;
                    double d;
                    if (_slider != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) _slider.Value = d;
                    break;
                case "item":
                    _edit.Text = v;
                    break;
                case "weather":
                {
                    string mapName = map;
                    int idx = 0, n;
                    string spec = token != null && token.Contains(":") ? token : A.Default;
                    if (spec != null && spec.Contains(":"))
                    {
                        var p = spec.Split(':');
                        if (mapName == null) mapName = p[0];
                        int.TryParse(p[1], out idx);
                    }
                    if (token != null && !token.Contains(":") && int.TryParse(token, out n)) idx = n - CommandData.WeatherIndexBase;
                    int mi = Math.Max(0, CommandData.Maps.FindIndex(m => m.Map == mapName));
                    _combo2.SelectedIndex = mi;
                    _loading = false;
                    FillWeathers(idx);
                    _loading = true;
                    v = _value;
                    break;
                }
                default:
                    _box.Text = v;
                    break;
            }
            _value = v;
            _loading = false;
            UpdateEnabled();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (!_ready) return;
            int w = Width, y = Padding.Top, lh = Theme.S(22), ch = Theme.S(32);
            var head = (Control)_include ?? _name;
            if (head == null) return;
            int headW = Math.Min(w, TextRenderer.MeasureText(head.Text, Theme.Bold).Width + Theme.S(_include != null ? 28 : 8));
            head.SetBounds(0, y, headW, lh);
            _help.SetBounds(headW + Theme.S(8), y, Math.Max(0, w - headW - Theme.S(8)), lh);
            y += lh + Theme.S(4);
            if (_combo2 != null)
            {
                _combo2.SetBounds(0, y, w, ch);
                y += ch + Theme.S(4);
            }
            if (_seg != null) _seg.SetBounds(0, y, w, ch);
            else if (_slider != null)
            {
                int bw = Theme.S(90);
                _slider.SetBounds(0, y, w - bw - Theme.S(10), ch);
                _box.SetBounds(w - bw, y + (ch - _box.PreferredHeight) / 2, bw, _box.PreferredHeight);
            }
            else if (_box != null) _box.SetBounds(0, y + (ch - _box.PreferredHeight) / 2, w, _box.PreferredHeight);
            else if (_edit != null) _edit.SetBounds(0, y, w, ch);
            else if (_combo != null) _combo.SetBounds(0, y, w, ch);
        }
    }

    /// <summary>The COMMANDS view: categories, command list and the command builder.</summary>
    public sealed class CommandsPanel : Panel
    {
        private const string CatFavorites = "★  FAVORITES", CatEssentials = "ESSENTIALS", CatAll = "ALL COMMANDS";

        private readonly ToolTip _tip;
        private readonly Action<string> _status;
        private readonly List<Favorite> _favs;
        private readonly ListBox _cats, _list;
        private readonly TextBox _search, _preview;
        private readonly Panel _builder, _argsHost;
        private readonly Label _title, _cmdLabel, _desc, _notes, _favLabel, _empty;
        private readonly Button _copy, _addFav, _updFav, _renFav, _delFav, _upFav, _downFav, _reset;
        private readonly List<ArgEditor> _editors = new List<ArgEditor>();
        private readonly Timer _copiedTimer = new Timer { Interval = 1400 };
        private CommandDef _cmd;
        private Favorite _fav;
        private bool _building;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public CommandsPanel(ToolTip tip, Action<string> status)
        {
            _tip = tip;
            _status = status;
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            CommandData.Load();
            try { _favs = Favorites.Load(); }
            catch (Exception ex)
            {
                _favs = new List<Favorite>();
                MessageBox.Show("Could not read your favorite commands:\n" + ex.Message + "\n\nFile: " + Favorites.FilePath,
                    "IRR Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // ---- categories (left)
            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(230), BackColor = Theme.Panel, Padding = new Padding(Theme.S(12)) };
            _search = Theme.Box();
            _search.Dock = DockStyle.Top;
            _search.HandleCreated += (s, e) => SendMessage(_search.Handle, 0x1501, (IntPtr)1, "Search commands…");
            _search.TextChanged += (s, e) => RefreshList();
            _tip.SetToolTip(_search, "Search in every command: name, title and description.");
            var gap = new Panel { Dock = DockStyle.Top, Height = Theme.S(8), BackColor = Theme.Panel };
            _cats = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(32), IntegralHeight = false, Font = Theme.Bold,
            };
            _cats.Items.Add(CatFavorites);
            _cats.Items.Add(CatEssentials);
            foreach (var c in CommandData.Categories) _cats.Items.Add(c);
            _cats.Items.Add(CatAll);
            _cats.DrawItem += DrawCat;
            _cats.SelectedIndexChanged += (s, e) =>
            {
                if (_search.Text.Length > 0) { _search.Text = ""; return; }
                RefreshList();
            };
            Theme.DarkScrollbars(_cats);
            var hint = Theme.Lbl("Open the console in game with ² / ~, ^ or F10, then paste (Ctrl+V).", Theme.Small, Theme.TextDim);
            hint.AutoSize = false;
            hint.Dock = DockStyle.Bottom;
            hint.Height = Theme.S(54);
            left.Controls.Add(_cats);
            left.Controls.Add(gap);
            left.Controls.Add(_search);
            left.Controls.Add(hint);

            // ---- builder (right)
            _builder = new Panel { Dock = DockStyle.Right, Width = Theme.S(480), BackColor = Theme.Back, Padding = new Padding(Theme.S(20), Theme.S(12), Theme.S(20), Theme.S(12)) };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(206), BackColor = Theme.Back };
            var cmdHeader = new SectionHeader("Command") { Dock = DockStyle.Top };
            _preview = Theme.Box(true);
            _preview.Dock = DockStyle.Top;
            _preview.Height = Theme.S(54);
            _preview.Font = new Font("Consolas", 11f);
            _preview.ForeColor = Theme.Accent;
            _preview.WordWrap = true;
            _tip.SetToolTip(_preview, "The command that COPY puts in the clipboard. You can also edit it here.");
            Func<int, TableLayoutPanel> row = cols =>
            {
                var t = new TableLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(40), ColumnCount = cols, RowCount = 1, BackColor = Theme.Back, Margin = new Padding(0) };
                t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                return t;
            };
            var copyRow = row(2);
            copyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
            copyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            _copy = Theme.Btn("COPY", (s, e) => CopyCommand(), true);
            _copy.Font = Theme.Header;
            _reset = Theme.Btn("DEFAULTS", (s, e) => { foreach (var ed in _editors) ed.Reset(); BuildLine(); });
            _addFav = Theme.Btn("★ ADD TO FAVORITES", (s, e) => AddFavorite());
            _updFav = Theme.Btn("SAVE", (s, e) => UpdateFavorite(), true);
            _renFav = Theme.Btn("RENAME", (s, e) => RenameFavorite());
            _delFav = Theme.Btn("REMOVE", (s, e) => RemoveFavorite());
            _upFav = Theme.Btn("▲", (s, e) => MoveFavorite(-1));
            _downFav = Theme.Btn("▼", (s, e) => MoveFavorite(1));
            _tip.SetToolTip(_copy, "Copy the command to the clipboard (also: double-click a command, or Enter in the list).");
            _tip.SetToolTip(_reset, "Put every argument back to its default value.");
            _tip.SetToolTip(_addFav, "Save this command with its current values in your favorites.");
            _tip.SetToolTip(_updFav, "Save the current values in this favorite.");
            _tip.SetToolTip(_upFav, "Move up in the favorites");
            _tip.SetToolTip(_downFav, "Move down in the favorites");
            foreach (var b in new[] { _copy, _reset, _addFav, _updFav, _renFav, _delFav, _upFav, _downFav }) { b.Dock = DockStyle.Fill; b.Margin = new Padding(Theme.S(2)); }
            copyRow.Controls.Add(_copy, 0, 0);
            copyRow.Controls.Add(_reset, 1, 0);
            _addRow = row(1);
            _addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _addRow.Controls.Add(_addFav, 0, 0);
            _favRow = row(5);
            foreach (var pct in new[] { 30, 25, 25, 10, 10 }) _favRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, pct));
            _favRow.Controls.Add(_updFav, 0, 0);
            _favRow.Controls.Add(_renFav, 1, 0);
            _favRow.Controls.Add(_delFav, 2, 0);
            _favRow.Controls.Add(_upFav, 3, 0);
            _favRow.Controls.Add(_downFav, 4, 0);
            _favRow.Visible = false;
            _favLabel = Theme.Lbl("", Theme.Small, Theme.Changed);
            _favLabel.Dock = DockStyle.Top;
            _favLabel.AutoSize = false;
            _favLabel.Height = Theme.S(22);
            bottom.Controls.Add(_favRow);
            bottom.Controls.Add(_addRow);
            bottom.Controls.Add(copyRow);
            bottom.Controls.Add(_favLabel);
            bottom.Controls.Add(_preview);
            bottom.Controls.Add(cmdHeader);

            _argsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Back };
            Theme.DarkScrollbars(_argsHost);
            _title = new Label { Dock = DockStyle.Top, Font = Theme.Title, ForeColor = Theme.Text, Height = Theme.S(34), AutoEllipsis = true };
            _cmdLabel = new Label { Dock = DockStyle.Top, Font = Theme.Mono, ForeColor = Theme.Accent, Height = Theme.S(22) };
            _desc = new Label { Dock = DockStyle.Top, Font = Theme.Body, ForeColor = Theme.TextDim, AutoSize = false };
            _notes = new Label { Dock = DockStyle.Top, Font = Theme.Small, ForeColor = Theme.Warn, AutoSize = false };
            _empty = Theme.Lbl("", Theme.Body, Theme.TextDim);
            _builder.Controls.Add(_argsHost);
            _builder.Controls.Add(_notes);
            _builder.Controls.Add(_desc);
            _builder.Controls.Add(_cmdLabel);
            _builder.Controls.Add(_title);
            _builder.Controls.Add(bottom);
            _builder.Resize += (s, e) => SizeTexts();

            // ---- list (middle)
            var mid = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(Theme.S(16), Theme.S(12), 0, Theme.S(12)) };
            _list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Back, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(54), IntegralHeight = false, Font = Theme.Body,
            };
            _list.DrawItem += DrawCommand;
            _list.SelectedIndexChanged += (s, e) => SelectItem(_list.SelectedItem);
            _list.DoubleClick += (s, e) => CopyCommand();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C)) { CopyCommand(); e.Handled = e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Delete && _fav != null) { RemoveFavorite(); e.Handled = true; }
            };
            Theme.DarkScrollbars(_list);
            _empty.Dock = DockStyle.Top;
            _empty.AutoSize = false;
            _empty.Height = Theme.S(60);
            _empty.Visible = false;
            mid.Controls.Add(_list);
            mid.Controls.Add(_empty);

            Controls.Add(mid);
            Controls.Add(_builder);
            Controls.Add(left);

            _copiedTimer.Tick += (s, e) => { _copiedTimer.Stop(); _copy.Text = "COPY"; };
            _cats.SelectedIndex = _favs.Count > 0 ? 0 : 1;
        }

        private readonly TableLayoutPanel _addRow, _favRow;

        public ListBox List { get { return _list; } }

        // ================================================================== list

        private string CurrentCategory { get { return _cats.SelectedItem as string ?? CatEssentials; } }

        private void RefreshList(object select = null)
        {
            string q = _search.Text.Trim();
            var items = new List<object>();
            if (q.Length > 0)
            {
                var words = q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var f in _favs)
                    if (words.All(w => (f.Label + " " + f.Line).ToLowerInvariant().Contains(w))) items.Add(f);
                foreach (var c in CommandData.All)
                    if (words.All(w => (c.Cmd + " " + c.Title + " " + c.Desc + " " + c.Category).ToLowerInvariant().Contains(w))) items.Add(c);
            }
            else
            {
                string cat = CurrentCategory;
                if (cat == CatFavorites) items.AddRange(_favs.Cast<object>());
                else if (cat == CatEssentials) items.AddRange(CommandData.Essentials.Select(CommandData.Find).Where(x => x != null).Cast<object>());
                else if (cat == CatAll) items.AddRange(CommandData.All.Cast<object>());
                else items.AddRange(CommandData.All.Where(x => x.Category == cat).Cast<object>());
            }
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var i in items) _list.Items.Add(i);
            _list.EndUpdate();
            _cats.Invalidate();

            _empty.Visible = items.Count == 0;
            if (items.Count == 0)
                _empty.Text = q.Length > 0 ? "No command matches \"" + q + "\"."
                    : "No favorites yet.\nSelect a command, set its values, then click ★ ADD TO FAVORITES.";

            if (select != null && _list.Items.Contains(select)) _list.SelectedItem = select;
            else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else SelectItem(null);
        }

        private bool IsFavoriteCmd(CommandDef c)
        {
            return _favs.Any(f => string.Equals(f.Cmd, c.Cmd, StringComparison.OrdinalIgnoreCase));
        }

        private void DrawCat(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var name = (string)_cats.Items[e.Index];
            bool sel = e.Index == _cats.SelectedIndex && _search.Text.Length == 0;
            var g = e.Graphics;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Theme.S(5), Theme.S(3), e.Bounds.Height - Theme.S(10));
            int count;
            if (name == CatFavorites) count = _favs.Count;
            else if (name == CatEssentials) count = CommandData.Essentials.Count;
            else if (name == CatAll) count = CommandData.All.Count;
            else count = CommandData.All.Count(x => x.Category == name);
            var col = name == CatFavorites ? Theme.Changed : (name == CatEssentials ? Theme.Accent : Theme.Text);
            var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(16), e.Bounds.Height);
            TextRenderer.DrawText(g, name == CatFavorites || name == CatEssentials || name == CatAll ? name : name.ToUpperInvariant(), Theme.Bold, r, col, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, count.ToString(CultureInfo.InvariantCulture), Theme.Small, r, Theme.TextFaint, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            if (e.Index == 1 || e.Index == _cats.Items.Count - 2)
                using (var p = new Pen(Theme.Track)) g.DrawLine(p, e.Bounds.X + Theme.S(8), e.Bounds.Bottom - 1, e.Bounds.Right - Theme.S(8), e.Bounds.Bottom - 1);
        }

        private void DrawCommand(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            var item = _list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var box = new Rectangle(e.Bounds.X, e.Bounds.Y + Theme.S(2), e.Bounds.Width - Theme.S(4), e.Bounds.Height - Theme.S(4));
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Row)) g.FillRectangle(b, box);
            using (var p = new Pen(sel ? Theme.Accent : Theme.Track)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
            int pad = Theme.S(12);
            var top = new Rectangle(box.X + pad, box.Y + Theme.S(5), box.Width - 2 * pad, Theme.S(22));
            var bot = new Rectangle(box.X + pad, box.Y + Theme.S(26), box.Width - 2 * pad, Theme.S(20));
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var fav = item as Favorite;
            if (fav != null)
            {
                TextRenderer.DrawText(g, "★  " + fav.Label, Theme.Bold, top, Theme.Changed, flags);
                TextRenderer.DrawText(g, fav.Line, Theme.Mono, bot, Theme.Accent, flags);
                return;
            }
            var c = (CommandDef)item;
            string tags = (c.Host ? "HOST  " : "") + (c.Warn != null ? "⚠  " : "") + (IsFavoriteCmd(c) ? "★" : "");
            int tagW = tags.Length == 0 ? 0 : TextRenderer.MeasureText(g, tags, Theme.Small).Width + Theme.S(6);
            TextRenderer.DrawText(g, c.Title, Theme.Bold, new Rectangle(top.X, top.Y, top.Width - tagW, top.Height), Theme.Text, flags);
            if (tags.Length > 0) TextRenderer.DrawText(g, tags, Theme.Small, top, c.Warn != null ? Theme.Warn : Theme.Changed, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            string args = string.Join(" ", c.Args.Select(a => a.Optional ? "[" + a.Name + "]" : "<" + a.Name + ">"));
            TextRenderer.DrawText(g, c.Cmd + (args.Length > 0 ? "  " + args : ""), Theme.Mono, bot, sel ? Theme.Accent : Theme.TextDim, flags);
        }

        // ================================================================== builder

        private void SelectItem(object item)
        {
            _fav = item as Favorite;
            var cmd = _fav != null ? CommandData.Find(_fav.Cmd) : item as CommandDef;
            if (cmd != _cmd) BuildEditors(cmd);
            _cmd = cmd;
            if (_fav != null)
            {
                var tokens = CommandData.Split(_fav.Line);
                for (int i = 0; i < _editors.Count; i++)
                    _editors[i].SetToken(i + 1 < tokens.Count ? tokens[i + 1] : null, i + 1 < tokens.Count, _fav.Map);
            }
            else foreach (var ed in _editors) ed.Reset();
            ShowFavoriteButtons();
            if (_fav != null && _cmd == null) _preview.Text = _fav.Line;
            else BuildLine();
        }

        private void BuildEditors(CommandDef cmd)
        {
            _building = true;
            _argsHost.SuspendLayout();
            foreach (var ed in _editors) { _argsHost.Controls.Remove(ed); ed.Dispose(); }
            _editors.Clear();
            var all = new List<Control>();
            if (cmd != null)
            {
                _title.Text = cmd.Title;
                _cmdLabel.Text = cmd.Cmd;
                _desc.Text = cmd.Desc;
                var notes = new List<string>();
                if (cmd.Host) notes.Add("Host only: works for the player who hosts the game (or in solo).");
                if (cmd.Warn != null) notes.Add("⚠ " + cmd.Warn);
                if (cmd.CVar) notes.Add("Setting: without a value, the game prints the current value.");
                if (cmd.Args.Any(a => a.Kind == "weather") && CommandData.WeatherIndexBase == 1)
                    notes.Add("Weather numbers are assumed to start at 1 (not yet confirmed in game). If the weather is off by one, please open an issue.");
                _notes.Text = string.Join("\n", notes);
                _notes.ForeColor = cmd.Warn != null ? Theme.Warn : Theme.Changed;
                foreach (var a in cmd.Args)
                {
                    var ed = new ArgEditor(a, _tip);
                    ed.Changed += (s, e) => { if (!_building) BuildLine(); };
                    _editors.Add(ed);
                }
                if (cmd.Args.Count == 0)
                {
                    var none = Theme.Lbl("No argument: copy and paste it as it is.", Theme.Body, Theme.TextDim);
                    none.AutoSize = false;
                    none.Dock = DockStyle.Top;
                    none.Height = Theme.S(40);
                    none.Tag = "none";
                    all.Add(none);
                }
            }
            else
            {
                _title.Text = _fav != null ? _fav.Label : "";
                _cmdLabel.Text = _fav != null ? "(unknown command)" : "";
                _desc.Text = "";
                _notes.Text = "";
            }
            // remove previous "no argument" label
            foreach (var c in _argsHost.Controls.Cast<Control>().Where(x => "none".Equals(x.Tag)).ToList()) { _argsHost.Controls.Remove(c); c.Dispose(); }
            if (cmd != null && cmd.Args.Count > 0) all.Add(new SectionHeader("Arguments") { Height = Theme.S(34) });
            all.AddRange(_editors);
            foreach (var c in _argsHost.Controls.OfType<SectionHeader>().ToList()) { _argsHost.Controls.Remove(c); c.Dispose(); }
            for (int i = all.Count - 1; i >= 0; i--) _argsHost.Controls.Add(all[i]);
            _argsHost.ResumeLayout();
            _argsHost.AutoScrollPosition = Point.Empty;
            _building = false;
            SizeTexts();
            bool any = cmd != null || _fav != null;
            _copy.Enabled = _reset.Enabled = _addFav.Enabled = any;
        }

        private void SizeTexts()
        {
            int w = _builder.ClientSize.Width - _builder.Padding.Horizontal;
            if (w <= 0) return;
            _desc.Height = string.IsNullOrEmpty(_desc.Text) ? 0 : TextRenderer.MeasureText(_desc.Text, _desc.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + Theme.S(8);
            _notes.Height = string.IsNullOrEmpty(_notes.Text) ? 0 : TextRenderer.MeasureText(_notes.Text, _notes.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + Theme.S(6);
        }

        private string CurrentLine()
        {
            if (_cmd == null) return _fav != null ? _fav.Line : "";
            var parts = new List<string> { _cmd.Cmd };
            foreach (var ed in _editors)
            {
                var t = ed.Token;
                if (t == null) break; // later arguments are positional: stop at the first one left out
                parts.Add(t);
            }
            return string.Join(" ", parts);
        }

        private void BuildLine()
        {
            _preview.Text = CurrentLine();
            if (_fav != null)
            {
                bool changed = _preview.Text != _fav.Line || (WeatherMap() ?? "") != (_fav.Map ?? "");
                _favLabel.Text = changed ? "Values changed: click SAVE to keep them in this favorite." : "Favorite: " + _fav.Label;
                _updFav.Text = changed ? "SAVE *" : "SAVE";
            }
            else _favLabel.Text = "";
        }

        private string WeatherMap()
        {
            var w = _editors.FirstOrDefault(x => x.A.Kind == "weather");
            return w == null ? null : w.Map;
        }

        private void ShowFavoriteButtons()
        {
            _addRow.Visible = _fav == null;
            _favRow.Visible = _fav != null;
            if (_fav != null)
            {
                _upFav.Enabled = _favs.IndexOf(_fav) > 0;
                _downFav.Enabled = _favs.IndexOf(_fav) < _favs.Count - 1;
            }
        }

        // ================================================================== actions

        private void CopyCommand()
        {
            string line = _preview.Text.Trim();
            if (line.Length == 0) return;
            try
            {
                Clipboard.SetText(line);
                _copy.Text = "COPIED ✓";
                _copiedTimer.Stop();
                _copiedTimer.Start();
                _status("Copied: " + line + "   ·   paste it in the game console (² / ~, ^ or F10) with Ctrl+V.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Could not access the clipboard: " + ex.Message, "IRR Companion");
            }
        }

        private string DefaultLabel()
        {
            if (_cmd == null) return "Command";
            var parts = new List<string>();
            foreach (var ed in _editors)
            {
                var t = ed.Token;
                if (t == null) break;
                if (ed.A.Kind == "weather") parts.Add(ed.Map + " " + WeatherName(ed));
                else if (ed.A.Kind == "toggle") parts.Add(t == "1" ? ed.A.On.ToLowerInvariant() : ed.A.Off.ToLowerInvariant());
                else parts.Add(t.Trim('"') + (string.IsNullOrEmpty(ed.A.Unit) ? "" : " " + ed.A.Unit));
            }
            return _cmd.Title + (parts.Count > 0 ? " – " + string.Join(", ", parts) : "");
        }

        private static string WeatherName(ArgEditor ed)
        {
            var m = CommandData.Maps.FirstOrDefault(x => x.Map == ed.Map);
            int n;
            if (m == null || !int.TryParse(ed.Token, out n)) return "";
            int i = n - CommandData.WeatherIndexBase;
            return i >= 0 && i < m.Weathers.Count ? m.Weathers[i].ToLowerInvariant() : "";
        }

        private bool SaveFavs()
        {
            try { Favorites.Save(_favs); return true; }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Could not save your favorites:\n" + ex.Message, "IRR Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void AddFavorite()
        {
            string line = _preview.Text.Trim();
            if (line.Length == 0) return;
            string label = TextPrompt.Ask(FindForm(), "Add to favorites", "Name of this favorite:", DefaultLabel());
            if (label == null) return;
            var f = new Favorite { Label = label.Trim().Length > 0 ? label.Trim() : DefaultLabel(), Line = line, Map = WeatherMap() };
            _favs.Add(f);
            if (!SaveFavs()) { _favs.Remove(f); return; }
            _status("Added to favorites: " + f.Label);
            _cats.Invalidate();
            _list.Invalidate();
        }

        private void UpdateFavorite()
        {
            if (_fav == null) return;
            _fav.Line = _preview.Text.Trim();
            _fav.Map = WeatherMap();
            if (SaveFavs()) _status("Favorite saved: " + _fav.Label);
            BuildLine();
            _list.Invalidate();
        }

        private void RenameFavorite()
        {
            if (_fav == null) return;
            string label = TextPrompt.Ask(FindForm(), "Rename favorite", "Name of this favorite:", _fav.Label);
            if (label == null || label.Trim().Length == 0) return;
            _fav.Label = label.Trim();
            SaveFavs();
            _title.Text = _cmd != null ? _cmd.Title : _fav.Label;
            BuildLine();
            _list.Invalidate();
        }

        private void RemoveFavorite()
        {
            if (_fav == null) return;
            if (MessageBox.Show(FindForm(), "Remove \"" + _fav.Label + "\" from your favorites?", "Remove favorite",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            int i = _favs.IndexOf(_fav);
            _favs.Remove(_fav);
            if (!SaveFavs()) { _favs.Insert(i, _fav); return; }
            _status("Favorite removed.");
            _fav = null;
            RefreshList(_favs.Count > 0 ? _favs[Math.Min(i, _favs.Count - 1)] : null);
        }

        private void MoveFavorite(int dir)
        {
            if (_fav == null) return;
            int i = _favs.IndexOf(_fav), j = i + dir;
            if (i < 0 || j < 0 || j >= _favs.Count) return;
            _favs.RemoveAt(i);
            _favs.Insert(j, _fav);
            SaveFavs();
            var f = _fav;
            if (CurrentCategory == CatFavorites && _search.Text.Length == 0) RefreshList(f);
        }

#if DEBUG
        /// <summary>Test helper: select a category and an entry.</summary>
        public void TestSelect(int category, string cmd, params string[] tokens)
        {
            _search.Text = "";
            _cats.SelectedIndex = category;
            var c = _list.Items.OfType<CommandDef>().FirstOrDefault(x => x.Cmd == cmd);
            if (c != null) _list.SelectedItem = c;
            for (int i = 0; i < tokens.Length && i < _editors.Count; i++) _editors[i].SetToken(tokens[i], true, null);
            BuildLine();
        }

        public void TestAddFavorite(string label, string line, string map)
        {
            _favs.Add(new Favorite { Label = label, Line = line, Map = map });
            _cats.SelectedIndex = 0;
            RefreshList();
        }

        public string TestLine { get { return _preview.Text; } }
#endif
    }

    /// <summary>Small dialog that asks for one line of text.</summary>
    public sealed class TextPrompt : Form
    {
        private readonly TextBox _box;

        private TextPrompt(string title, string label, string value)
        {
            Text = title;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            ClientSize = new Size(Theme.S(460), Theme.S(130));
            var l = Theme.Lbl(label, Theme.Body, Theme.TextDim);
            l.Location = new Point(Theme.S(14), Theme.S(14));
            _box = Theme.Box();
            _box.Text = value;
            _box.SetBounds(Theme.S(14), Theme.S(40), Theme.S(432), Theme.S(28));
            var ok = Theme.Btn("OK", (s, e) => { DialogResult = DialogResult.OK; Close(); }, true);
            var cancel = Theme.Btn("CANCEL", (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            ok.SetBounds(Theme.S(226), Theme.S(84), Theme.S(106), Theme.S(32));
            cancel.SetBounds(Theme.S(340), Theme.S(84), Theme.S(106), Theme.S(32));
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { l, _box, ok, cancel });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _box.Focus();
            _box.SelectAll();
        }

        /// <summary>Returns the text, or null if cancelled.</summary>
        public static string Ask(IWin32Window owner, string title, string label, string value)
        {
            using (var d = new TextPrompt(title, label, value))
                return d.ShowDialog(owner) == DialogResult.OK ? d._box.Text : null;
        }
    }
}
