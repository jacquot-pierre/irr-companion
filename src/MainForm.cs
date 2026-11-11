using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>Row with a label and a segmented choice (AI SIMULATION / AI DIFFICULTY).</summary>
    public sealed class ChoiceRow : Panel
    {
        public readonly Label NameLabel;
        public readonly Segmented Choice;

        public ChoiceRow(string name, string tooltip, ToolTip tip, params string[] options)
        {
            Dock = DockStyle.Top;
            Height = Theme.S(52);
            BackColor = Theme.Back;
            Padding = new Padding(0, Theme.S(3), 0, Theme.S(3));
            NameLabel = new Label
            {
                Text = name.ToUpperInvariant(), Font = Theme.Bold, ForeColor = Theme.Text,
                BackColor = Theme.Row, TextAlign = ContentAlignment.MiddleLeft,
            };
            Choice = new Segmented(options);
            Controls.Add(NameLabel);
            Controls.Add(Choice);
            tip.SetToolTip(NameLabel, tooltip);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var r = new Rectangle(0, Padding.Top, Width - 1, Height - Padding.Vertical - 1);
            using (var b = new SolidBrush(Theme.Row)) e.Graphics.FillRectangle(b, r);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, r);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Choice == null || NameLabel == null) return;
            int top = Padding.Top + 1, h = Height - Padding.Vertical - 2, pad = Theme.S(14);
            int w = Math.Min(Theme.S(560), (int)(Width * 0.62));
            Choice.SetBounds(Width - pad - w, top + Theme.S(7), w, h - Theme.S(14));
            NameLabel.SetBounds(pad, top, Math.Max(10, Width - w - 3 * pad), h);
        }
    }

    public sealed class MainForm : Form
    {
        private readonly List<Preset> _presets = new List<Preset>();
        private Preset _current;
        private bool _loading;

        private readonly ToolTip _tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 400, ReshowDelay = 100 };
        private ListBox _list;
        private Panel _editPanel, _comparePanel, _rowsPanel, _side;
        private CommandsPanel _commandsPanel;
        private HideoutPanel _hideoutPanel;
        private MapsPanel _mapsPanel;
        private WeaponsPanel _weaponsPanel;
        private TradersPanel _tradersPanel;
        private Segmented _viewSwitch;
        private TextBox _name, _author, _desc;
        private Label _status, _info, _unsaved;
        private Panel _banner, _advBox, _perfBox;
        private Button _save, _copy, _compareBtn, _moreBtn;
        private ContextMenuStrip _moreMenu;
        private ToolStripMenuItem _miRevert, _miInfo, _miDelete;
        private CheckBox _onlyChanged;
        private ToggleHeader _perfHeader;
        /// <summary>Section headers of the editor with their rows (for "Only changed settings").</summary>
        private readonly List<KeyValuePair<SectionHeader, List<ParamRow>>> _sections = new List<KeyValuePair<SectionHeader, List<ParamRow>>>();
        private readonly Dictionary<Control, int> _natural = new Dictionary<Control, int>();
        private DarkCombo _refCombo, _cmpA, _cmpB;
        private ChoiceRow _simRow, _diffRow;
        private readonly List<ParamRow> _rows = new List<ParamRow>();
        private DataGridView _grid;
        private CheckBox _onlyDiff, _cmpAdvanced;
        private ToggleHeader _advHeader;
        private Label _cmpSummary;

        public MainForm()
        {
            Theme.Init(this);
            Text = "IRR Companion";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(Theme.S(1280), area.Width), Math.Min(Theme.S(860), area.Height));
            MinimumSize = new Size(Math.Min(Theme.S(1000), area.Width), Math.Min(Theme.S(620), area.Height));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            BuildLayout();

            foreach (var name in GameData.AiOrder) _presets.Add(Preset.FromGame(name, "Normal"));
            try
            {
                _presets.AddRange(Library.Load());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read your saved presets:\n" + ex.Message +
                                "\n\nFile: " + Library.FilePath, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshList();
            _refChoice = _presets.First(p => p.BuiltIn && p.Name == GameData.AiLabels["Normal"]);
            RefreshCombos();
            var firstMine = _presets.FirstOrDefault(p => !p.BuiltIn);
            SelectPreset(firstMine ?? _presets[1]);
            _cmpA.SelectedItem = _presets[1];
            _cmpB.SelectedItem = firstMine ?? _presets[2];
            SetStatus("Presets are stored in " + Library.FilePath);
            ActiveControl = _list;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = _list;
            _rowsPanel.AutoScrollPosition = new Point(0, 0);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        // ================================================================== layout

        private void BuildLayout()
        {
            // ---- header
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(64), BackColor = Theme.Panel };
            var title = Theme.Lbl("IRR COMPANION", Theme.Title, Theme.Accent);
            title.Location = new Point(Theme.S(18), Theme.S(8));
            var sub = Theme.Lbl("Companion app for Incursion Red River · game data " + GameData.GameBuild, Theme.Small, Theme.TextDim);
            sub.Location = new Point(Theme.S(20), Theme.S(40));
            // Views: 0 presets editor, 1 compare (opened from the editor, no tab of its own), 2 commands, 3 weapons,
            // 4 hideout, 5 maps, 6 traders. Tab i shows view TabViews[i].
            _viewSwitch = new Segmented("PRESETS", "COMMANDS", "WEAPONS", "TRADERS", "HIDEOUT", "MAPS") { Selected = 0, BackColor = Theme.Row };
            _viewSwitch.Size = new Size(Theme.S(660), Theme.S(36));
            _viewSwitch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _viewSwitch.OptionClicked += (s, e) => ShowView(TabViews[_viewSwitch.ClickedIndex]);
            header.Controls.Add(title);
            header.Controls.Add(sub);
            header.Controls.Add(_viewSwitch);
            header.Resize += (s, e) => _viewSwitch.Location = new Point(header.Width - _viewSwitch.Width - Theme.S(18), Theme.S(14));

            // ---- status bar
            var statusBar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(28), BackColor = Theme.Panel };
            _status = Theme.Lbl("", Theme.Small, Theme.TextDim);
            _status.Location = new Point(Theme.S(12), Theme.S(6));
            var folder = new LinkLabel
            {
                Text = "Open data folder", Font = Theme.Small, AutoSize = true, LinkColor = Theme.Accent,
                ActiveLinkColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.Transparent,
            };
            folder.LinkClicked += (s, e) =>
            {
                Directory.CreateDirectory(Library.Folder);
                Process.Start("explorer.exe", "\"" + Library.Folder + "\"");
            };
            // interface size (applied at the next start)
            var zoom = new DarkCombo { Width = Theme.S(118), Font = Theme.Small, ItemHeight = Theme.S(18) };
            foreach (var z in AppSettings.Zooms) zoom.Items.Add("UI size " + Math.Round(z * 100) + " %");
            int zi = Array.FindIndex(AppSettings.Zooms, z => Math.Abs(z - AppSettings.UiZoom) < 0.01f);
            if (zi < 0) { zoom.Items.Add("UI size " + Math.Round(AppSettings.UiZoom * 100) + " %"); zi = zoom.Items.Count - 1; }
            zoom.SelectedIndex = zi;
            _tip.SetToolTip(zoom, "Size of the text and of the whole interface (on top of the Windows display scaling). Applied after a restart.");
            zoom.SelectedIndexChanged += (s, e) =>
            {
                if (zoom.SelectedIndex >= AppSettings.Zooms.Length) return;
                float z = AppSettings.Zooms[zoom.SelectedIndex];
                if (Math.Abs(z - AppSettings.UiZoom) < 0.01f) return;
                AppSettings.UiZoom = z;
                try { AppSettings.Save(); }
                catch (Exception ex) { SetStatus("Could not save the settings: " + ex.Message); return; }
                if (MessageBox.Show(this, "The new size is applied when the app starts. Restart IRR Companion now?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    Application.Restart();
                else SetStatus("The new interface size is applied at the next start.");
            };
            statusBar.Controls.Add(_status);
            statusBar.Controls.Add(folder);
            statusBar.Controls.Add(zoom);
            statusBar.Resize += (s, e) =>
            {
                folder.Location = new Point(statusBar.Width - folder.Width - Theme.S(12), Theme.S(6));
                zoom.Location = new Point(folder.Left - zoom.Width - Theme.S(16), (statusBar.Height - zoom.Height) / 2);
            };

            // ---- sidebar
            var side = _side = new Panel { Dock = DockStyle.Left, Width = Theme.S(260), BackColor = Theme.Panel, Padding = new Padding(Theme.S(12)) };
            var sideTitle = new SectionHeader("Presets") { BackColor = Theme.Panel };
            _list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(30), IntegralHeight = false, Font = Theme.Body,
            };
            _list.DrawItem += DrawListItem;
            _list.SelectedIndexChanged += (s, e) =>
            {
                var p = _list.SelectedItem as Preset;
                if (p == null) { if (_current != null) _list.SelectedItem = _current; return; }
                if (p != _current) LoadPreset(p);
            };
            Theme.DarkScrollbars(_list);

            var sideButtons = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 1, RowCount = 2, Height = Theme.S(84), BackColor = Theme.Panel };
            sideButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var bNew = Theme.Btn("+ NEW", (s, e) => NewPreset());
            var bImp = Theme.Btn("IMPORT CODE", (s, e) => ImportCode());
            _tip.SetToolTip(bNew, "New preset based on the game's Regular difficulty.");
            _tip.SetToolTip(bImp, "Paste a share code (IRR1-...) copied from the game or from another player.");
            foreach (var b in new[] { bNew, bImp }) { b.Dock = DockStyle.Fill; b.Margin = new Padding(Theme.S(2)); }
            sideButtons.Controls.Add(bNew, 0, 0);
            sideButtons.Controls.Add(bImp, 0, 1);
            side.Controls.Add(_list);
            side.Controls.Add(sideTitle);
            side.Controls.Add(sideButtons);

            // ---- content
            _editPanel = BuildEditPanel();
            _comparePanel = BuildComparePanel();
            _comparePanel.Visible = false;
            _commandsPanel = new CommandsPanel(_tip, SetStatus) { Visible = false };
            _hideoutPanel = new HideoutPanel(_tip, SetStatus) { Visible = false };
            _mapsPanel = new MapsPanel(_tip) { Visible = false };
            _weaponsPanel = new WeaponsPanel(_tip, SetStatus) { Visible = false };
            _tradersPanel = new TradersPanel(_tip) { Visible = false };

            Controls.Add(_editPanel);
            Controls.Add(_comparePanel);
            Controls.Add(_commandsPanel);
            Controls.Add(_hideoutPanel);
            Controls.Add(_mapsPanel);
            Controls.Add(_weaponsPanel);
            Controls.Add(_tradersPanel);
            Controls.Add(side);
            Controls.Add(statusBar);
            Controls.Add(header);
        }

        private Panel BuildEditPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };

            // --- top: name, state line, actions, reference
            var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(118), BackColor = Theme.Back };
            _name = Theme.Box();
            _name.Font = Theme.Header;
            _name.TextChanged += (s, e) => MetaChanged();
            _name.Leave += (s, e) => RefreshCombos();
            _tip.SetToolTip(_name, "Preset name, shown in the game. Its ID (used by the game as folder name) is derived from it.");
            // author and description: edited from the ⋯ menu (Author and description…)
            _author = Theme.Box();
            _desc = Theme.Box();
            _author.TextChanged += (s, e) => MetaChanged();
            _desc.TextChanged += (s, e) => MetaChanged();

            _info =Theme.Lbl("", Theme.Small, Theme.TextDim);
            _unsaved = Theme.Lbl("· unsaved changes", Theme.Small, Theme.Changed);
            var infoRow = new FlowLayoutPanel { BackColor = Theme.Back, WrapContents = false, Margin = new Padding(0) };
            _info.Margin = _unsaved.Margin = new Padding(0, 0, Theme.S(4), 0);
            infoRow.Controls.Add(_info);
            infoRow.Controls.Add(_unsaved);

            _save = Theme.Btn("SAVE", (s, e) => SaveCurrent(), true);
            _copy = Theme.Btn("COPY CODE", (s, e) => CopyCode());
            _compareBtn = Theme.Btn("COMPARE", (s, e) => OpenCompare());
            _moreBtn = Theme.Btn("⋯", null);
            _moreBtn.Font = Theme.Header;
            _tip.SetToolTip(_save, "Save this preset in the tool (Ctrl+S). Nothing is written to the game.");
            _tip.SetToolTip(_copy, "Copy the share code to the clipboard, then paste it in the game's preset import.");
            _tip.SetToolTip(_compareBtn, "Compare this preset with the one chosen in \"Compare values to\", setting by setting.");
            _tip.SetToolTip(_moreBtn, "More: revert, view code, duplicate, author and description, delete.");
            _moreMenu = DarkMenu.Create();
            _miRevert = new ToolStripMenuItem("Revert unsaved changes", null, (s, e) => RevertCurrent());
            var miView = new ToolStripMenuItem("View share code", null, (s, e) => ViewCode());
            var miDup = new ToolStripMenuItem("Duplicate", null, (s, e) => DuplicatePreset());
            _miInfo = new ToolStripMenuItem("Author and description…", null, (s, e) => EditInfo());
            _miDelete = new ToolStripMenuItem("Delete", null, (s, e) => DeletePreset());
            _moreMenu.Items.AddRange(new ToolStripItem[] { _miRevert, miView, miDup, _miInfo, new ToolStripSeparator(), _miDelete });
            _moreBtn.Click += (s, e) => _moreMenu.Show(_moreBtn, new Point(_moreBtn.Width - _moreMenu.PreferredSize.Width, _moreBtn.Height));

            var refLbl = Theme.Lbl("Compare values to", Theme.Small, Theme.TextDim);
            _refCombo = new DarkCombo { Width = Theme.S(240) };
            _refCombo.SelectedIndexChanged += (s, e) =>
            {
                if (!_refFilling) _refChoice = _refCombo.SelectedItem as Preset;
                RefreshReferences();
                ApplyFilter();
            };
            _tip.SetToolTip(_refCombo, "Reference values shown next to each setting (▲ higher, ▼ lower).");
            _onlyChanged = new CheckBox { Text = "Only changed settings", ForeColor = Theme.Text, AutoSize = true };
            _onlyChanged.CheckedChanged += (s, e) => ApplyFilter();
            _tip.SetToolTip(_onlyChanged, "Show only the settings that differ from the preset chosen in \"Compare values to\".");

            top.Controls.AddRange(new Control[] { _name, infoRow, _save, _copy, _compareBtn, _moreBtn, refLbl, _refCombo, _onlyChanged });
            top.Resize += (s, e) =>
            {
                int pad = Theme.S(20), w = top.Width, bw = Theme.S(118), gap = Theme.S(6);
                int x = w - pad - Theme.S(40);
                _moreBtn.SetBounds(x, Theme.S(12), Theme.S(40), Theme.S(34));
                foreach (var b in new[] { _compareBtn, _copy, _save })
                {
                    x -= gap + bw;
                    b.SetBounds(x, Theme.S(12), bw, Theme.S(34));
                }
                _name.SetBounds(pad, Theme.S(13), Math.Max(Theme.S(120), x - pad - Theme.S(16)), _name.PreferredHeight);
                infoRow.SetBounds(pad, Theme.S(50), Math.Max(Theme.S(120), w - 2 * pad), Theme.S(20));
                refLbl.Location = new Point(pad, Theme.S(83));
                _refCombo.Location = new Point(refLbl.Right + Theme.S(8), Theme.S(79));
                _onlyChanged.Location = new Point(_refCombo.Right + Theme.S(20), Theme.S(81));
            };

            _banner = new Panel { Dock = DockStyle.Top, Height = Theme.S(40), BackColor = Theme.PanelAlt };
            var bannerText = Theme.Lbl("Game difficulty – read only. Make your own copy to change it.", Theme.Bold, Theme.Accent);
            var bannerBtn = Theme.Btn("MAKE MY OWN COPY", (s, e) => DuplicatePreset(), true);
            bannerBtn.Width = Theme.S(170);
            _banner.Controls.Add(bannerText);
            _banner.Controls.Add(bannerBtn);
            _banner.Resize += (s, e) =>
            {
                int tw = bannerText.PreferredWidth, total = tw + Theme.S(16) + bannerBtn.Width, x0 = Math.Max(Theme.S(10), (_banner.Width - total) / 2);
                bannerText.Location = new Point(x0, (_banner.Height - bannerText.Height) / 2);
                bannerBtn.SetBounds(x0 + tw + Theme.S(16), Theme.S(4), bannerBtn.Width, _banner.Height - Theme.S(8));
            };

            // --- rows: difficulty and the game menu settings, then ADVANCED and PERFORMANCE (closed)
            _rowsPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(Theme.S(20), 0, Theme.S(20), Theme.S(20)) };
            Theme.DarkScrollbars(_rowsPanel);
            var items = new List<Control>();

            items.Add(new SectionHeader("AI Difficulty"));
            _diffRow = new ChoiceRow("Start from",
                "Loads all the values of a game difficulty into this preset (like in the game).\nCUSTOM means the values differ from every game difficulty.",
                _tip, GameData.AiOrder.Select(x => GameData.AiLabels[x].ToUpperInvariant()).Concat(new[] { "CUSTOM" }).ToArray());
            _diffRow.Choice.OptionClicked += (s, e) => ApplyDifficulty(_diffRow.Choice.ClickedIndex);
            items.Add(_diffRow);
            AddRows(items, GameData.Params.Where(p => !p.IsSimulation && !p.Advanced));

            // ADVANCED: settings of the AI document that the game menu does not show.
            var advItems = new List<Control>();
            AddRows(advItems, GameData.Params.Where(p => p.Advanced));
            _advHeader = AddGroup(items, "Advanced", advItems, out _advBox);
            _advHeader.Toggled += (s, e) => _cmpAdvanced.Checked = _advHeader.Expanded;

            // PERFORMANCE: AI simulation level and optimization ranges (how far bots are simulated in detail).
            var perfItems = new List<Control>();
            _simRow = new ChoiceRow("AI Simulation",
                "Simulation level: how far bots are simulated in full detail. Higher costs more performance.\nEditing the values below switches to CUSTOM.",
                _tip, GameData.SimOrder.Select(x => x.ToUpperInvariant()).Concat(new[] { "CUSTOM" }).ToArray());
            _simRow.Choice.OptionClicked += (s, e) => ApplySimLevel(_simRow.Choice.ClickedIndex);
            perfItems.Add(Spacer(8));
            perfItems.Add(_simRow);
            AddRows(perfItems, GameData.Params.Where(p => p.IsSimulation));
            _perfHeader = AddGroup(items, "Performance", perfItems, out _perfBox);

            // Dock=Top stacks the last added control on top: add in reverse order.
            for (int i = items.Count - 1; i >= 0; i--) _rowsPanel.Controls.Add(items[i]);

            panel.Controls.Add(_rowsPanel);
            panel.Controls.Add(_banner);
            panel.Controls.Add(top);
            return panel;
        }

        private static Control Spacer(int h)
        {
            return new Panel { Dock = DockStyle.Top, Height = Theme.S(h), BackColor = Theme.Back };
        }

        /// <summary>Clickable header + one container holding the group (closed by default).</summary>
        private ToggleHeader AddGroup(List<Control> items, string title, List<Control> groupItems, out Panel box)
        {
            var header = new ToggleHeader(title, "");
            // One container for the whole group: showing / hiding many docked controls one by one breaks their order.
            var container = new Panel { Dock = DockStyle.Top, BackColor = Theme.Back, Visible = false, Height = groupItems.Sum(c => c.Height) };
            for (int i = groupItems.Count - 1; i >= 0; i--) container.Controls.Add(groupItems[i]);
            header.Toggled += (s, e) =>
            {
                container.Visible = header.Expanded;
                if (header.Expanded) _rowsPanel.ScrollControlIntoView(header);
            };
            items.Add(header);
            items.Add(container);
            box = container;
            return header;
        }

        private void AddRows(List<Control> items, IEnumerable<Param> ps)
        {
            string section = null;
            foreach (var p in ps)
            {
                if (p.Section != section)
                {
                    section = p.Section;
                    var h = new SectionHeader(section);
                    _sections.Add(new KeyValuePair<SectionHeader, List<ParamRow>>(h, new List<ParamRow>()));
                    items.Add(h);
                }
                var row = new ParamRow(p, _tip);
                row.UserChanged += RowChanged;
                _rows.Add(row);
                _sections[_sections.Count - 1].Value.Add(row);
                items.Add(row);
            }
        }

        /// <summary>Shows or hides a docked control by its height (keeps the dock order, unlike Visible).</summary>
        private void SetShown(Control c, bool shown)
        {
            int h;
            if (!_natural.TryGetValue(c, out h)) _natural[c] = h = c.Height;
            c.Height = shown ? h : 0;
            c.TabStop = shown;
        }

        /// <summary>"Only changed settings": hides the rows equal to the reference preset.</summary>
        private void ApplyFilter()
        {
            if (_current == null || _rowsPanel == null) return;
            var refP = _refCombo.SelectedItem as Preset;
            bool on = _onlyChanged.Checked && refP != null && refP != _current;
            _rowsPanel.SuspendLayout();
            foreach (var sec in _sections)
            {
                bool any = false;
                foreach (var r in sec.Value)
                {
                    bool show = !on || !GameData.ValuesEqual(_current.Get(r.P), refP.Get(r.P));
                    SetShown(r, show);
                    any |= show;
                }
                SetShown(sec.Key, any);
            }
            foreach (var box in new[] { _advBox, _perfBox })
                box.Height = box.Controls.Cast<Control>().Sum(c => c.Height);
            _rowsPanel.ResumeLayout(true);
            UpdateInfo();
        }

        /// <summary>State line under the name and the hints of the ADVANCED / PERFORMANCE bars.</summary>
        private void UpdateInfo()
        {
            if (_current == null || _info == null) return;
            var refP = _refCombo.SelectedItem as Preset;
            bool hasRef = refP != null && refP != _current;
            Func<Func<Param, bool>, int> differ = f => !hasRef ? 0 :
                GameData.Params.Count(p => f(p) && !GameData.ValuesEqual(_current.Get(p), refP.Get(p)));
            var parts = new List<string>();
            if (_current.BuiltIn) parts.Add("Game difficulty");
            else
            {
                string closest = null;
                int best = int.MaxValue;
                foreach (var name in GameData.AiOrder)
                {
                    var doc = GameData.Ai[name];
                    int n = GameData.Params.Count(p => !p.IsSimulation && !GameData.ValuesEqual(_current.Get(p), Json.GetPath(doc, p.DocKey)));
                    if (n < best) { best = n; closest = GameData.AiLabels[name]; }
                }
                parts.Add(best == 0 ? "Same values as " + closest : "Closest game difficulty: " + closest);
            }
            if (hasRef)
            {
                int n = differ(p => true);
                parts.Add(n == 0 ? "same values as " + refP.Name : n + (n == 1 ? " setting differs" : " settings differ") + " from " + refP.Name);
            }
            _info.Text = string.Join(" · ", parts);
            _unsaved.Visible = !_current.BuiltIn && _current.Dirty;

            int adv = GameData.Params.Count(p => p.Advanced), advDiff = differ(p => p.Advanced);
            _advHeader.Hint = adv + " settings the game menu does not show" + (advDiff > 0 ? " · " + advDiff + " differ" : "") + " · change them only if you know what they do";
            string sim = _current.SimLevel;
            int perfDiff = differ(p => p.IsSimulation);
            _perfHeader.Hint = "AI simulation: " + (sim == null ? "CUSTOM" : sim.ToUpperInvariant()) + " · optimization ranges" + (perfDiff > 0 ? " · " + perfDiff + " differ" : "");
            _advHeader.Invalidate();
            _perfHeader.Invalidate();
        }

        private void OpenCompare()
        {
            if (_current == null) return;
            var refP = _refCombo.SelectedItem as Preset;
            if (refP == null || refP == _current) refP = _presets.First(x => x.BuiltIn && x.Name == GameData.AiLabels["Normal"]);
            RefreshCombos();
            _cmpA.SelectedItem = refP;
            _cmpB.SelectedItem = _current;
            ShowView(1);
        }

        private void EditInfo()
        {
            if (_current == null || _current.BuiltIn) return;
            string author = TextPrompt.Ask(this, "Author", "Author of this preset (shown in the game)", _current.Author ?? "");
            if (author == null) return;
            string desc = TextPrompt.Ask(this, "Description", "Short description of this preset", _current.Description ?? "");
            if (desc == null) return;
            _author.Text = author;
            _desc.Text = desc;
        }

        private Panel BuildComparePanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(Theme.S(20), Theme.S(12), Theme.S(20), Theme.S(12)) };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(46), BackColor = Theme.Back, WrapContents = false };
            var la = Theme.Lbl("A", Theme.Header, Theme.ColorA);
            la.Margin = new Padding(0, Theme.S(8), Theme.S(6), 0);
            _cmpA = new DarkCombo { Width = Theme.S(220), Margin = new Padding(0, Theme.S(8), Theme.S(8), 0) };
            var swap = Theme.Btn("⇄", (s, e) => { var a = _cmpA.SelectedItem; _cmpA.SelectedItem = _cmpB.SelectedItem; _cmpB.SelectedItem = a; });
            swap.Width = Theme.S(44);
            swap.Margin = new Padding(0, Theme.S(4), Theme.S(8), 0);
            _tip.SetToolTip(swap, "Swap A and B");
            var lb = Theme.Lbl("B", Theme.Header, Theme.ColorB);
            lb.Margin = new Padding(0, Theme.S(8), Theme.S(6), 0);
            _cmpB = new DarkCombo { Width = Theme.S(220), Margin = new Padding(0, Theme.S(8), Theme.S(16), 0) };
            _onlyDiff = new CheckBox { Text = "Only differences", ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, Theme.S(11), Theme.S(16), 0) };
            _cmpAdvanced = new CheckBox { Text = "Advanced", ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, Theme.S(11), Theme.S(16), 0) };
            _tip.SetToolTip(_cmpAdvanced, "Also compare the advanced settings (not in the game menu).");
            _cmpSummary = Theme.Lbl("", Theme.Bold, Theme.Accent);
            _cmpSummary.Margin = new Padding(0, Theme.S(11), 0, 0);
            _cmpA.SelectedIndexChanged += (s, e) => RefreshCompare();
            _cmpB.SelectedIndexChanged += (s, e) => RefreshCompare();
            _onlyDiff.CheckedChanged += (s, e) => RefreshCompare();
            _cmpAdvanced.CheckedChanged += (s, e) => RefreshCompare();
            bar.Controls.AddRange(new Control[] { la, _cmpA, swap, lb, _cmpB, _onlyDiff, _cmpAdvanced, _cmpSummary });

            var legend = Theme.Lbl("Bar: recommended range of the setting, ● = A, ◆ = B.   Change column: B compared to A (▲ higher, ▼ lower).",
                Theme.Small, Theme.TextDim);
            legend.Dock = DockStyle.Bottom;
            legend.AutoSize = false;
            legend.Height = Theme.S(24);
            legend.TextAlign = ContentAlignment.MiddleLeft;

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Theme.Back,
                BorderStyle = BorderStyle.None,
                GridColor = Theme.Track,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = Theme.S(34),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            _grid.RowTemplate.Height = Theme.S(30);
            _grid.DefaultCellStyle.BackColor = Theme.Row;
            _grid.DefaultCellStyle.ForeColor = Theme.Text;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.RowHover;
            _grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            _grid.DefaultCellStyle.Font = Theme.Body;
            _grid.DefaultCellStyle.Padding = new Padding(Theme.S(6), 0, Theme.S(6), 0);
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextDim;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.Bold;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Panel;
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Theme.S(6), 0, Theme.S(6), 0);
            _grid.Columns.Add("param", "SETTING");
            _grid.Columns.Add("a", "A");
            _grid.Columns.Add("b", "B");
            _grid.Columns.Add("change", "CHANGE (B vs A)");
            _grid.Columns.Add("bar", "");
            _grid.Columns["param"].FillWeight = 34;
            _grid.Columns["a"].FillWeight = 11;
            _grid.Columns["b"].FillWeight = 11;
            _grid.Columns["change"].FillWeight = 16;
            _grid.Columns["bar"].FillWeight = 28;
            _grid.Columns["a"].HeaderCell.Style.ForeColor = Theme.ColorA;
            _grid.Columns["b"].HeaderCell.Style.ForeColor = Theme.ColorB;
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns["a"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns["b"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.CellPainting += GridCellPainting;
            _grid.RowPostPaint += GridRowPostPaint;
            Theme.DarkScrollbars(_grid);

            panel.Controls.Add(_grid);
            panel.Controls.Add(legend);
            panel.Controls.Add(bar);

            // opened from the COMPARE button of the presets editor: way back to the editor
            var strip = new Panel { Dock = DockStyle.Top, Height = Theme.S(44), BackColor = Theme.Back };
            var back = Theme.Btn("◀  BACK TO PRESET", (s, e) => ShowView(0));
            back.SetBounds(0, Theme.S(4), Theme.S(170), Theme.S(34));
            _tip.SetToolTip(back, "Back to the presets editor (Esc).");
            var title = Theme.Lbl("COMPARE PRESETS", Theme.Header, Theme.Text);
            title.Location = new Point(Theme.S(186), Theme.S(11));
            strip.Controls.Add(back);
            strip.Controls.Add(title);
            panel.Controls.Add(strip);
            return panel;
        }

        // ================================================================== list

        private void RefreshList()
        {
            _loading = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Items.Add("GAME DIFFICULTIES");
            foreach (var p in _presets.Where(x => x.BuiltIn)) _list.Items.Add(p);
            _list.Items.Add("MY PRESETS");
            var mine = _presets.Where(x => !x.BuiltIn).ToList();
            foreach (var p in mine) _list.Items.Add(p);
            if (mine.Count == 0) _list.Items.Add("  (none yet – duplicate a preset)");
            if (_current != null) _list.SelectedItem = _current;
            _list.EndUpdate();
            _loading = false;
        }

        private void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var item = _list.Items[e.Index];
            var g = e.Graphics;
            var p = item as Preset;
            bool sel = p != null && p == _current;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (p == null)
            {
                var s = (string)item;
                bool hint = s.StartsWith("  ");
                TextRenderer.DrawText(g, s.Trim(), hint ? Theme.Small : Theme.Bold, new Rectangle(e.Bounds.X + Theme.S(4), e.Bounds.Y, e.Bounds.Width, e.Bounds.Height),
                    hint ? Theme.TextFaint : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                return;
            }
            if (sel)
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Theme.S(4), Theme.S(3), e.Bounds.Height - Theme.S(8));
            var r = new Rectangle(e.Bounds.X + Theme.S(14), e.Bounds.Y, e.Bounds.Width - Theme.S(18), e.Bounds.Height);
            TextRenderer.DrawText(g, p.Name + (p.Dirty ? "  ●" : ""), Theme.Body, r, p.BuiltIn ? Theme.Accent : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        /// <summary>"None" entry of the "Compare values to" list.</summary>
        private sealed class NoReference { public override string ToString() { return "None"; } }

        private static readonly object NoRef = new NoReference();
        /// <summary>The reference the user picked (null = none); shown unless it is the preset being edited.</summary>
        private Preset _refChoice;
        private bool _refFilling;

        /// <summary>"Compare values to": None + every preset except the one being edited.</summary>
        private void FillReferenceCombo()
        {
            _refFilling = true;
            _refCombo.BeginUpdate();
            _refCombo.Items.Clear();
            _refCombo.Items.Add(NoRef);
            foreach (var p in _presets) if (p != _current) _refCombo.Items.Add(p);
            _refCombo.SelectedItem = _refChoice != null && _refChoice != _current && _presets.Contains(_refChoice) ? (object)_refChoice : NoRef;
            _refCombo.EndUpdate();
            _refFilling = false;
        }

        private void RefreshCombos()
        {
            FillReferenceCombo();
            foreach (var combo in new[] { _cmpA, _cmpB })
            {
                var sel = combo.SelectedItem;
                combo.BeginUpdate();
                combo.Items.Clear();
                foreach (var p in _presets) combo.Items.Add(p);
                if (sel != null && combo.Items.Contains(sel)) combo.SelectedItem = sel;
                else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                combo.EndUpdate();
            }
        }

        private void SelectPreset(Preset p)
        {
            LoadPreset(p);
            _list.SelectedItem = p;
        }

        // ================================================================== editor

        private void LoadPreset(Preset p)
        {
            _current = p;
            _loading = true;
            _name.Text = p.Name;
            _author.Text = p.Author;
            _desc.Text = p.Description;
            bool ro = p.BuiltIn;
            _name.ReadOnly = _author.ReadOnly = _desc.ReadOnly = ro;
            foreach (var r in _rows)
            {
                r.ReadOnly = ro;
                r.SetValue(p.Get(r.P));
            }
            _simRow.Choice.ReadOnly = ro;
            _diffRow.Choice.ReadOnly = ro;
            _banner.Visible = ro;
            _save.Enabled = !ro;
            _miRevert.Enabled = _miInfo.Enabled = _miDelete.Enabled = !ro;
            _loading = false;
            RefreshChoiceRows();
            FillReferenceCombo();
            RefreshReferences();
            ApplyFilter();
            UpdateDirtyUi();
            _list.Invalidate();
            if (_rowsPanel.ContainsFocus) ActiveControl = _list;
            _rowsPanel.AutoScrollPosition = new Point(0, 0);
        }

        private void RowChanged(object sender, EventArgs e)
        {
            if (_loading || _current == null || _current.BuiltIn) return;
            var row = (ParamRow)sender;
            _current.Set(row.P, row.Value);
            RefreshChoiceRows();
            RefreshReferences(row);
            UpdateDirtyUi();
        }

        private void MetaChanged()
        {
            if (_loading || _current == null || _current.BuiltIn) return;
            _current.Name = _name.Text;
            _current.Author = _author.Text;
            _current.Description = _desc.Text;
            _current.Dirty = true;
            UpdateDirtyUi();
        }

        private void UpdateDirtyUi()
        {
            if (_current == null) return;
            _save.Text = _current.Dirty ? "SAVE *" : "SAVE";
            _list.Invalidate();
            UpdateInfo();
            if (_comparePanel.Visible) RefreshCompare();
        }

        private void RefreshChoiceRows()
        {
            if (_current == null) return;
            string sim = _current.SimLevel;
            _simRow.Choice.Selected = sim == null ? GameData.SimOrder.Count : GameData.SimOrder.IndexOf(sim);
            int diff = GameData.AiOrder.Count;
            for (int i = 0; i < GameData.AiOrder.Count; i++)
            {
                var builtin = GameData.Ai[GameData.AiOrder[i]];
                bool same = GameData.Params.Where(x => !x.IsSimulation)
                    .All(x => GameData.ValuesEqual(_current.Get(x), Json.GetPath(builtin, x.DocKey)));
                if (same) { diff = i; break; }
            }
            _diffRow.Choice.Selected = diff;
        }

        private void ApplySimLevel(int index)
        {
            if (_current == null || _current.BuiltIn || index >= GameData.SimOrder.Count) return;
            _current.Sim = Json.CloneObj(GameData.Sim[GameData.SimOrder[index]]);
            _current.Dirty = true;
            ReloadValues();
        }

        private void ApplyDifficulty(int index)
        {
            if (_current == null || _current.BuiltIn || index >= GameData.AiOrder.Count) return;
            _current.Ai = Json.CloneObj(GameData.Ai[GameData.AiOrder[index]]);
            _current.Dirty = true;
            ReloadValues();
            SetStatus("Loaded the values of the game difficulty \"" + GameData.AiLabels[GameData.AiOrder[index]] + "\". Use REVERT to undo.");
        }

        private void ReloadValues()
        {
            _loading = true;
            foreach (var r in _rows) r.SetValue(_current.Get(r.P));
            _loading = false;
            RefreshChoiceRows();
            RefreshReferences();
            ApplyFilter();
            UpdateDirtyUi();
        }

        private void RefreshReferences(ParamRow only = null)
        {
            var refP = _refCombo.SelectedItem as Preset;
            foreach (var r in _rows)
            {
                if (only != null && r != only) continue;
                if (refP == null || refP == _current) r.SetReference("", null);
                else r.SetReference(refP.Name, refP.Get(r.P));
            }
        }

        // ================================================================== commands

        private string UniqueName(string baseName)
        {
            string name = baseName;
            int n = 2;
            while (_presets.Any(p => !p.BuiltIn && (p.Name == name || p.Id == Preset.MakeId(name))))
                name = baseName + " " + n++;
            return name;
        }

        private void AddAndSelect(Preset p)
        {
            _presets.Add(p);
            RefreshList();
            RefreshCombos();
            SelectPreset(p);
            ShowView(0);
            _name.Focus();
            _name.SelectAll();
        }

        private void NewPreset()
        {
            var baseP = _presets.First(x => x.BuiltIn && x.Name == GameData.AiLabels["Normal"]);
            var p = baseP.CloneAs(UniqueName("New preset"));
            p.Description = "";
            AddAndSelect(p);
            SetStatus("New preset created from the game's Regular difficulty. Click SAVE to keep it.");
        }

        private void DuplicatePreset()
        {
            if (_current == null) return;
            var p = _current.CloneAs(UniqueName(_current.BuiltIn ? "My " + _current.Name : _current.Name + " copy"));
            if (_current.BuiltIn) p.Description = "Based on the game difficulty " + _current.Name + ".";
            AddAndSelect(p);
            SetStatus("Duplicated. Change the values, then click SAVE.");
        }

        private void DeletePreset()
        {
            if (_current == null || _current.BuiltIn) return;
            if (MessageBox.Show(this, "Delete the preset \"" + _current.Name + "\" from the tool?\n\nThis does not affect the game.",
                    "Delete preset", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            int idx = _presets.IndexOf(_current);
            _presets.Remove(_current);
            if (!TrySaveLibrary()) return;
            var next = _presets.Where(p => !p.BuiltIn).LastOrDefault() ?? _presets[Math.Min(idx, _presets.Count - 1)];
            _current = null;
            RefreshList();
            RefreshCombos();
            SelectPreset(next);
            SetStatus("Preset deleted.");
        }

        private bool Validate(Preset p)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                MessageBox.Show(this, "Give the preset a name first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _name.Focus();
                return false;
            }
            var clash = _presets.FirstOrDefault(x => x != p && !x.BuiltIn && x.Id == p.Id);
            if (clash != null)
            {
                MessageBox.Show(this, "Another preset (\"" + clash.Name + "\") has the same ID \"" + p.Id +
                    "\".\nThe game would replace one with the other: choose a different name.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _name.Focus();
                return false;
            }
            return true;
        }

        private void SaveCurrent()
        {
            if (_current == null || _current.BuiltIn) return;
            if (!Validate(_current)) return;
            _current.SavedAt = Preset.Now();
            _current.MarkSaved();
            if (TrySaveLibrary())
            {
                RefreshCombos();
                UpdateDirtyUi();
                SetStatus("Saved \"" + _current.Name + "\" to " + Library.FilePath);
            }
        }

        private bool TrySaveLibrary()
        {
            try
            {
                Library.Save(_presets);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save your presets:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void RevertCurrent()
        {
            if (_current == null || _current.BuiltIn || !_current.Dirty) return;
            if (_current.SavedJson == null)
            {
                MessageBox.Show(this, "This preset has never been saved: there is nothing to revert to.", Text);
                return;
            }
            _current.Revert();
            LoadPreset(_current);
            RefreshCombos();
            SetStatus("Unsaved changes discarded.");
        }

        private void CopyCode()
        {
            if (_current == null || !Validate(_current)) return;
            string code = _current.ToShareCode();
            try
            {
                Clipboard.SetText(code);
                SetStatus("Share code of \"" + _current.Name + "\" copied (" + code.Length + " characters). Paste it in the game's preset import.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not access the clipboard: " + ex.Message, Text);
            }
        }

        private void ViewCode()
        {
            if (_current == null || !Validate(_current)) return;
            using (var d = new CodeDialog("Share code – " + _current.Name, _current.ToShareCode(), true))
                d.ShowDialog(this);
        }

        private void ImportCode()
        {
            string initial = "";
            try { if (Clipboard.ContainsText() && Clipboard.GetText().Trim().StartsWith("IRR")) initial = Clipboard.GetText().Trim(); }
            catch (Exception) { }
            using (var d = new CodeDialog("Import a share code", initial, false))
            {
                while (d.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var p = Preset.FromShareObject(ShareCode.Decode(d.Code), false);
                        string original = p.Name;
                        p.Name = UniqueName(string.IsNullOrWhiteSpace(p.Name) ? "Imported preset" : p.Name);
                        p.MarkSaved();
                        _presets.Add(p);
                        if (!TrySaveLibrary()) { _presets.Remove(p); return; }
                        RefreshList();
                        RefreshCombos();
                        SelectPreset(p);
                        ShowView(0);
                        SetStatus("Imported \"" + p.Name + "\"" + (p.Name != original ? " (renamed: a preset named \"" + original + "\" already exists)" : "") + ".");
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void SetStatus(string s) { _status.Text = s; }

        private static readonly int[] TabViews = { 0, 2, 3, 6, 4, 5 };

        private void ShowView(int index)
        {
            _viewSwitch.Selected = Math.Max(0, Array.IndexOf(TabViews, index <= 1 ? 0 : index));  // COMPARE belongs to the PRESETS tab
            _editPanel.Visible = index == 0;
            _comparePanel.Visible = index == 1;
            _commandsPanel.Visible = index == 2;
            _weaponsPanel.Visible = index == 3;
            _hideoutPanel.Visible = index == 4;
            _mapsPanel.Visible = index == 5;
            _tradersPanel.Visible = index == 6;
            _side.Visible = index < 2;
            if (index == 2) ActiveControl = _commandsPanel.List;
            if (index == 3) ActiveControl = _weaponsPanel.List;
            if (index == 4) ActiveControl = _hideoutPanel.List;
            if (index == 5) ActiveControl = _mapsPanel.Canvas;
            if (index == 6) ActiveControl = _tradersPanel.List;
            if (index == 1)
            {
                RefreshCombos();
                RefreshCompare();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S) && _editPanel.Visible) { SaveCurrent(); return true; }
            if (keyData == (Keys.Control | Keys.S) && _weaponsPanel.Visible) { _weaponsPanel.SaveCurrent(); return true; }
            if (keyData == Keys.Escape && _comparePanel.Visible) { ShowView(0); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!_weaponsPanel.ConfirmClose()) { e.Cancel = true; return; }
            var dirty = _presets.Where(p => !p.BuiltIn && p.Dirty).ToList();
            if (dirty.Count == 0) return;
            var r = MessageBox.Show(this, "Save changes to: " + string.Join(", ", dirty.Select(p => "\"" + p.Name + "\"")) + " ?",
                Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) { e.Cancel = true; return; }
            if (r == DialogResult.Yes)
            {
                foreach (var p in dirty)
                {
                    if (!Validate(p)) { e.Cancel = true; SelectPreset(p); return; }
                    p.SavedAt = Preset.Now();
                    p.MarkSaved();
                }
                if (!TrySaveLibrary()) e.Cancel = true;
            }
        }

#if DEBUG
        /// <summary>Test helper (--screenshot): saves PNGs of the edit and compare views.</summary>
        public void RenderTestShots(string prefix)
        {
            Action<string> shot = name =>
            {
                Application.DoEvents();
                using (var bmp = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bmp, new Rectangle(Point.Empty, Size));
                    bmp.Save(prefix + "_" + name + ".png", System.Drawing.Imaging.ImageFormat.Png);
                }
            };
            // a user preset to look at
            var p = _presets[2].CloneAs("Night ops");
            p.Author = "IRR Companion";
            p.Description = "Veteran bots, more aggressive, aiming for the head, wider simulation range.";
            p.Set(GameData.Params.First(x => x.Key == "Aggressiveness"), 0.75);
            p.Set(GameData.Params.First(x => x.Key == "bCanAimForHead"), true);
            p.Set(GameData.Params.First(x => x.Key == "OptimizationRanges.1"), 44787.234);
            p.Set(GameData.Params.First(x => x.Key == "GrenadeInaccuracy"), 700.0);
            p.MarkSaved();
            _presets.Add(p);
            RefreshList();
            RefreshCombos();
            SelectPreset(p);
            shot("edit");
            _rowsPanel.AutoScrollPosition = new Point(0, Theme.S(1300));
            shot("edit_scrolled");
            _rowsPanel.AutoScrollPosition = new Point(0, 99999);
            shot("edit_advanced_closed");
            _advHeader.Expanded = true;
            Application.DoEvents();
            _rowsPanel.AutoScrollPosition = new Point(0, -_rowsPanel.AutoScrollPosition.Y + _advHeader.Top - Theme.S(8));
            shot("edit_advanced");
            _advHeader.Expanded = false;
            _perfHeader.Expanded = true;
            Application.DoEvents();
            _rowsPanel.AutoScrollPosition = new Point(0, -_rowsPanel.AutoScrollPosition.Y + _perfHeader.Top - Theme.S(8));
            shot("edit_performance");
            _perfHeader.Expanded = false;
            _onlyChanged.Checked = true;
            _rowsPanel.AutoScrollPosition = new Point(0, 0);
            shot("edit_changed_only");
            _onlyChanged.Checked = false;
            SelectPreset(_presets[0]);
            shot("builtin");
            _cmpA.SelectedItem = _presets[1];
            _cmpB.SelectedItem = p;
            ShowView(1);
            shot("compare");
            _onlyDiff.Checked = true;
            shot("compare_diff");
            _cmpA.SelectedItem = _presets[2];
            _cmpB.SelectedItem = _presets[3];
            _cmpAdvanced.Checked = true;
            shot("compare_advanced");
            _cmpAdvanced.Checked = false;
            _cmpA.SelectedItem = _presets[0];
            _cmpB.SelectedItem = _presets[3];
            _onlyDiff.Checked = false;
            shot("compare_difficulties");
            ShowView(2);
            _commandsPanel.TestSelect(1, "irr.World.SetWeather", "5", "30");
            shot("commands_weather");
            _commandsPanel.TestSelect(1, "irr.Item.Spawn", "Glock_19", "3");
            shot("commands_item");
            _commandsPanel.TestSelect(3, "irr.Player.Invulnerable");
            shot("commands_player");
            _commandsPanel.TestSelect(6, "irr.AI.Headlamps", "2");
            shot("commands_cvar");
            _commandsPanel.TestAddFavorite("Thunderstorm on Delta", "irr.World.SetWeather 6 30", "Delta");
            _commandsPanel.TestAddFavorite("Noon", "irr.World.SetTimeOfDay 12", null);
            _commandsPanel.TestAddFavorite("God mode", "irr.Player.Invulnerable 1", null);
            _commandsPanel.TestAddFavorite("Extract now", "irr.Player.ForceExtraction", null);
            _commandsPanel.TestAddFavorite("Spawn 5 bandages", "irr.Item.Spawn Bandage 5", null);
            shot("commands_favorites");
            ShowView(3);
            _weaponsPanel.TestShow(0);
            shot("weapons");
            _weaponsPanel.TestShow(1);
            shot("weapons_aug");
            _weaponsPanel.TestClear();
            ShowView(6);
            _tradersPanel.TestShow(1, null);
            shot("traders");
            _tradersPanel.TestShow(2, null);
            Application.DoEvents();
            shot("traders_vlf");
            _tradersPanel.TestShow(0, "grenade");
            shot("traders_search");
            _tradersPanel.TestShow(0, null);
            ShowView(4);
            _hideoutPanel.TestShow("Generator", true);
            shot("hideout_unit");
            _hideoutPanel.TestShow("Stash", true);
            shot("hideout_stash");
            _hideoutPanel.TestShow(null, true);
            shot("hideout_shopping");
            _hideoutPanel.TestShow(null, false);
            shot("hideout_shopping_all");
            ShowView(5);
            _mapsPanel.TestShow("Quarry", 0, -1);
            shot("maps_quarry");
            _mapsPanel.TestShow("Delta", 0, 5);
            shot("maps_delta_hover");
            _mapsPanel.TestShow("Bunker", 2, -1);
            shot("maps_bunker");
            _mapsPanel.TestShowLock("Outskirts", "LargeSafe");
            shot("maps_safe_hover");
            ShowView(0);
            _presets.Remove(p);
            foreach (var x in _presets) x.Dirty = false;
        }
#endif

        // ================================================================== compare

        private sealed class CmpRow
        {
            public Param P;
            public string Section;
            public object A, B;
        }

        private void RefreshCompare()
        {
            var a = _cmpA.SelectedItem as Preset;
            var b = _cmpB.SelectedItem as Preset;
            _grid.Rows.Clear();
            if (a == null || b == null) return;

            int diffs = 0, total = 0;
            string section = null;
            var rows = new List<CmpRow>();
            // simulation level as first line
            rows.Add(new CmpRow { Section = "Simulation", A = a.SimLevel ?? "Custom", B = b.SimLevel ?? "Custom" });
            foreach (var p in GameData.Params.Where(x => !x.Advanced || _cmpAdvanced.Checked))
                rows.Add(new CmpRow { P = p, Section = p.IsSimulation ? "AI Optimization Ranges" : p.Section, A = a.Get(p), B = b.Get(p) });

            foreach (var r in rows)
            {
                bool differs = !GameData.ValuesEqual(r.A, r.B);
                total++;
                if (differs) diffs++;
                if (_onlyDiff.Checked && !differs) continue;
                if (r.Section != section)
                {
                    section = r.Section;
                    int hi = _grid.Rows.Add("", "", "", "", "");
                    _grid.Rows[hi].Tag = section;
                    _grid.Rows[hi].Height = Theme.S(34);
                    _grid.Rows[hi].DefaultCellStyle.BackColor = Theme.Back;
                    _grid.Rows[hi].DefaultCellStyle.SelectionBackColor = Theme.Back;
                }
                string name = r.P == null ? "AI SIMULATION" : r.P.Name.ToUpperInvariant();
                string fa = r.P == null ? (string)r.A : r.P.Format(r.A);
                string fb = r.P == null ? (string)r.B : r.P.Format(r.B);
                string change = "";
                Color changeColor = Theme.TextFaint;
                if (differs)
                {
                    if (r.A is double && r.B is double)
                    {
                        double da = (double)r.A, db = (double)r.B, d = db - da;
                        change = (d > 0 ? "▲ +" : "▼ −") + r.P.Format(Math.Abs(d));
                        if (Math.Abs(da) > 1e-12) change += string.Format(System.Globalization.CultureInfo.InvariantCulture, "  ({0:+0;-0}%)", d / Math.Abs(da) * 100);
                        changeColor = d > 0 ? Theme.Higher : Theme.Lower;
                    }
                    else
                    {
                        change = "● " + fa + " → " + fb;
                        changeColor = Theme.Changed;
                    }
                }
                else change = "=";
                int i = _grid.Rows.Add(name, fa, fb, change, "");
                var row = _grid.Rows[i];
                row.Tag = r;
                if (r.P != null) row.Cells[0].ToolTipText = r.P.Tooltip;
                row.Cells["change"].Style.ForeColor = changeColor;
                row.Cells["change"].Style.SelectionForeColor = changeColor;
                row.Cells["a"].Style.ForeColor = Theme.ColorA;
                row.Cells["b"].Style.ForeColor = Theme.ColorB;
                row.Cells["a"].Style.SelectionForeColor = Theme.ColorA;
                row.Cells["b"].Style.SelectionForeColor = Theme.ColorB;
                if (differs)
                {
                    row.DefaultCellStyle.BackColor = Theme.DiffRow;
                    row.Cells[0].Style.Font = Theme.Bold;
                }
                else
                {
                    row.Cells[0].Style.ForeColor = Theme.TextDim;
                    row.Cells[0].Style.SelectionForeColor = Theme.TextDim;
                }
            }
            _cmpSummary.Text = diffs == 0 ? "Identical" : diffs + " of " + total + " differ";
            // differences hidden in the advanced settings: shown on the checkbox
            int adv = _cmpAdvanced.Checked ? 0 : GameData.Params.Count(p => p.Advanced && !GameData.ValuesEqual(a.Get(p), b.Get(p)));
            _cmpAdvanced.Text = adv > 0 ? "Advanced (+" + adv + ")" : "Advanced";
            _grid.ClearSelection();
        }

        private void GridRowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            var section = _grid.Rows[e.RowIndex].Tag as string;
            if (section == null) return;
            var g = e.Graphics;
            var r = e.RowBounds;
            using (var b = new SolidBrush(Theme.Back)) g.FillRectangle(b, r);
            string text = section.ToUpperInvariant();
            var sz = TextRenderer.MeasureText(g, text, Theme.Header);
            int y = r.Bottom - Theme.S(4) - sz.Height;
            TextRenderer.DrawText(g, text, Theme.Header, new Point(r.X + Theme.S(4), y), Theme.Text);
            using (var p = new Pen(Theme.Accent))
                g.DrawLine(p, r.X + Theme.S(16) + sz.Width, y + sz.Height / 2, r.Right - Theme.S(8), y + sz.Height / 2);
        }

        private void GridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _grid.Columns["bar"].Index) return;
            var cr = _grid.Rows[e.RowIndex].Tag as CmpRow;
            e.PaintBackground(e.CellBounds, false);
            e.Handled = true;
            if (cr == null || cr.P == null || cr.P.IsBool || !(cr.A is double) || !(cr.B is double)) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var p = cr.P;
            int pad = Theme.S(14);
            var track = new Rectangle(e.CellBounds.X + pad, e.CellBounds.Y + e.CellBounds.Height / 2 - Theme.S(2),
                                      e.CellBounds.Width - 2 * pad, Theme.S(4));
            double min = p.ViewMin, max = p.ViewMax;
            Func<double, float> X = v =>
            {
                double t = max > min ? (v - min) / (max - min) : 0;
                return (float)(track.X + track.Width * Math.Max(0, Math.Min(1, t)));
            };
            using (var b = new SolidBrush(Theme.Track)) g.FillRectangle(b, track);
            float xa = X((double)cr.A), xb = X((double)cr.B);
            bool differs = !GameData.ValuesEqual(cr.A, cr.B);
            if (differs)
            {
                var col = (double)cr.B > (double)cr.A ? Theme.Higher : Theme.Lower;
                using (var b = new SolidBrush(Color.FromArgb(170, col)))
                    g.FillRectangle(b, Math.Min(xa, xb), track.Y, Math.Abs(xb - xa), track.Height);
            }
            float rr = Theme.S(6);
            float cy = track.Y + track.Height / 2f;
            using (var b = new SolidBrush(Theme.ColorA)) g.FillEllipse(b, xa - rr, cy - rr, 2 * rr, 2 * rr);
            var diamond = new[] { new PointF(xb, cy - rr - 1), new PointF(xb + rr + 1, cy), new PointF(xb, cy + rr + 1), new PointF(xb - rr - 1, cy) };
            using (var b = new SolidBrush(Theme.ColorB)) g.FillPolygon(b, diamond);
            using (var pen = new Pen(Theme.Row, 1.5f)) g.DrawPolygon(pen, diamond);
        }
    }

    /// <summary>Shows a share code (read-only) or lets the user paste one.</summary>
    public sealed class CodeDialog : Form
    {
        private readonly TextBox _box;
        public string Code { get { return _box.Text; } }

        public CodeDialog(string title, string code, bool readOnly)
        {
            Text = title;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Theme.S(720), Theme.S(420));
            MinimizeBox = MaximizeBox = false;
            ShowIcon = false;
            Padding = new Padding(Theme.S(14));

            var hint = Theme.Lbl(readOnly
                ? "Copy this code and paste it in the game (AI settings › presets › import), or share it with other players."
                : "Paste a share code (IRR1-...) exported from the game or from this tool.", Theme.Small, Theme.TextDim);
            hint.Dock = DockStyle.Top;
            hint.AutoSize = false;
            hint.Height = Theme.S(26);

            _box = Theme.Box(true);
            _box.Dock = DockStyle.Fill;
            _box.Font = Theme.Mono;
            _box.ScrollBars = ScrollBars.Vertical;
            _box.ReadOnly = readOnly;
            _box.Text = code;
            _box.WordWrap = true;
            Theme.DarkScrollbars(_box);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(48), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Back, Padding = new Padding(0, Theme.S(8), 0, 0) };
            if (readOnly)
            {
                var close = Theme.Btn("CLOSE", (s, e) => Close());
                var copy = Theme.Btn("COPY", (s, e) => { try { Clipboard.SetText(_box.Text); Text = "Copied!"; } catch (Exception) { } }, true);
                close.Width = copy.Width = Theme.S(110);
                buttons.Controls.Add(close);
                buttons.Controls.Add(copy);
                CancelButton = close;
            }
            else
            {
                var cancel = Theme.Btn("CANCEL", (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
                var ok = Theme.Btn("IMPORT", (s, e) => { DialogResult = DialogResult.OK; Close(); }, true);
                cancel.Width = ok.Width = Theme.S(110);
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                CancelButton = cancel;
            }
            Controls.Add(_box);
            Controls.Add(buttons);
            Controls.Add(hint);
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
            if (_box.ReadOnly) _box.SelectAll();
            else _box.SelectionStart = _box.TextLength;
        }
    }
}
