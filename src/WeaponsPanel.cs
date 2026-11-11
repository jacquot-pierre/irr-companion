using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>Drop-down of items with their game icon. Holds WeaponItem (or a string header, not selectable) and None.</summary>
    public sealed class ItemCombo : ComboBox
    {
        /// <summary>The "nothing in this slot" entry.</summary>
        public static readonly object None = new NoneItem();

        private sealed class NoneItem { public override string ToString() { return "(empty)"; } }

        /// <summary>Wheel turns while closed are given to this (scrolls the page instead of changing the part).</summary>
        public Action<int> Wheel;
        /// <summary>Silhouette drawn for the empty entry (type of the slot), or null.</summary>
        public Image EmptyIcon;
        private int _last = -1;

        /// <summary>Category of the silhouette shown for an empty slot (Barrel, Sight...), from the slot name.</summary>
        public static string SlotCategory(string slot)
        {
            string s = System.Text.RegularExpressions.Regex.Replace(slot ?? "", @"\s+\d+$", "");
            switch (s)
            {
                case "Gas Block": case "Gas Tube": return "GasTube";
                case "Front Sight": case "Canted Sight": return "Sight";
                case "Dust Cover": return "DustCover";
                case "Rail Mount": return "Rail";
                case "Grip": return "Foregrip";
                case "Pistol Grip": return "PistolGrip";
                default: return s;
            }
        }

        public ItemCombo()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Row;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ItemHeight = Theme.S(30);
            MaxDropDownItems = 14;
        }

        public WeaponItem SelectedWeaponItem { get { return SelectedItem as WeaponItem; } }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            // headers cannot be selected: go to the next (or previous) item
            if (SelectedItem is string)
            {
                int dir = SelectedIndex >= _last ? 1 : -1;
                int i = SelectedIndex + dir;
                while (i >= 0 && i < Items.Count && Items[i] is string) i += dir;
                if (i < 0 || i >= Items.Count) i = _last;
                SelectedIndex = i;
                return;
            }
            _last = SelectedIndex;
            base.OnSelectedIndexChanged(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!DroppedDown && Wheel != null)
            {
                var h = e as HandledMouseEventArgs;
                if (h != null) h.Handled = true;
                Wheel(e.Delta);
                return;
            }
            base.OnMouseWheel(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(sel ? Theme.AccentDark : Theme.Row)) g.FillRectangle(b, e.Bounds);
            if (e.Index < 0) return;
            var item = Items[e.Index];
            var r = e.Bounds;
            var header = item as string;
            if (header != null)
            {
                TextRenderer.DrawText(g, header.ToUpperInvariant(), Theme.Small, new Rectangle(r.X + Theme.S(8), r.Y, r.Width, r.Height), Theme.Accent,
                    TextFormatFlags.Left | TextFormatFlags.Bottom);
                return;
            }
            var it = item as WeaponItem;
            int s = r.Height - Theme.S(4);
            var slot = new Rectangle(r.X + Theme.S(4), r.Y + (r.Height - s) / 2, s, s);
            var icon = it != null ? Icons.ItemOrCategory(it.Id, it.Cat) : EmptyIcon;
            if (it != null || icon != null)
            {
                using (var b = new SolidBrush(it != null ? Color.FromArgb(62, 58, 53) : Color.FromArgb(44, 40, 37))) g.FillRectangle(b, slot);
                if (icon != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(icon, Rectangle.Inflate(slot, -1, -1));
                }
            }
            var text = it == null && icon == null ? new Rectangle(r.X + Theme.S(10), r.Y, r.Width - Theme.S(12), r.Height)
                : new Rectangle(slot.Right + Theme.S(8), r.Y, r.Right - slot.Right - Theme.S(10), r.Height);
            string extra = "";
            if (it != null)
            {
                int n = it.Slots.Count(x => !x.IsAmmo);
                if (n > 0) extra = "+" + n + " slot" + (n > 1 ? "s" : "");
            }
            if (extra.Length > 0)
            {
                int w = TextRenderer.MeasureText(extra, Theme.Small).Width;
                TextRenderer.DrawText(g, extra, Theme.Small, new Rectangle(text.Right - w, r.Y, w, r.Height), Theme.TextFaint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                text.Width -= w + Theme.S(6);
            }
            TextRenderer.DrawText(g, item.ToString(), Font, text, it == null ? Theme.TextFaint : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>The WEAPONS view: build a weapon from compatible parts and get the additem line that gives it.</summary>
    public sealed class WeaponsPanel : Panel
    {
        private readonly ToolTip _tip;
        private readonly Action<string> _status;
        private readonly List<WeaponBuild> _builds;
        private readonly ListBox _list;
        private readonly TextBox _name, _cmd;
        private readonly Label _info, _unsaved, _weaponInfo, _cmdInfo;
        private readonly ItemCombo _weapon;
        private readonly Panel _weaponIcon, _rows;
        private readonly StatsView _stats;
        private readonly ToolStripMenuItem _miRevert, _miDelete;
        private WeaponBuild _cur;
        private bool _loading;
        private int _listIndex = -1;

        public WeaponsPanel(ToolTip tip, Action<string> status)
        {
            _tip = tip;
            _status = status;
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            WeaponData.Load();
            try { _builds = WeaponBuilds.Load(); }
            catch (Exception ex)
            {
                _builds = new List<WeaponBuild>();
                MessageBox.Show("Could not read your weapon builds:\n" + ex.Message + "\n\nFile: " + WeaponBuilds.FilePath,
                    "IRR Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // ---- left: my builds
            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(260), BackColor = Theme.Panel, Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(12), Theme.S(12)) };
            var head = new SectionHeader("My builds") { BackColor = Theme.Panel, Height = Theme.S(36) };
            var newBtn = Theme.Btn("+ NEW BUILD", (s, e) => NewBuild(), true);
            newBtn.Dock = DockStyle.Top;
            _tip.SetToolTip(newBtn, "Start a new build from the weapon shown on the right.");
            var gap = new Panel { Dock = DockStyle.Top, Height = Theme.S(8), BackColor = Theme.Panel };
            _list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(48), IntegralHeight = false, Font = Theme.Bold,
            };
            _list.DrawItem += DrawBuild;
            _list.SelectedIndexChanged += (s, e) => ListChanged();
            Theme.DarkScrollbars(_list);
            var hint = Theme.Lbl("Only parts that fit the slot are offered, as in the gunsmith. COPY COMMAND copies the additem line that gives the whole build (cheat).", Theme.Small, Theme.TextDim);
            hint.AutoSize = false;
            hint.Dock = DockStyle.Bottom;
            hint.Height = Theme.S(76);
            left.Controls.Add(_list);
            left.Controls.Add(gap);
            left.Controls.Add(newBtn);
            left.Controls.Add(head);
            left.Controls.Add(hint);

            // ---- content
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(Theme.S(20), Theme.S(8), Theme.S(20), Theme.S(12)) };

            // header: name, state, SAVE / COPY COMMAND / ⋯
            var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(74), BackColor = Theme.Back };
            _name = new TextBox { Font = Theme.Header, BackColor = Theme.Back, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
            _name.SetBounds(0, Theme.S(10), Theme.S(380), Theme.S(30));
            _name.TextChanged += (s, e) => { if (_loading || _cur == null) return; _cur.Name = _name.Text; Changed(false); };
            _tip.SetToolTip(_name, "Name of the build (click to rename).");
            var line = new Panel { BackColor = Theme.Track, Height = 1 };
            line.SetBounds(0, Theme.S(40), Theme.S(380), 1);
            _info = Theme.Lbl("", Theme.Small, Theme.TextDim);
            _info.Location = new Point(0, Theme.S(48));
            _unsaved = Theme.Lbl("· unsaved changes", Theme.Small, Theme.Changed);
            var save = Theme.Btn("SAVE", (s, e) => SaveCurrent(), true);
            var copy = Theme.Btn("COPY COMMAND", (s, e) => CopyCommand());
            var more = Theme.Btn("⋯", null);
            more.Font = Theme.Header;
            save.Width = Theme.S(90);
            copy.Width = Theme.S(150);
            more.Width = Theme.S(44);
            _tip.SetToolTip(save, "Save this build (Ctrl+S).");
            _tip.SetToolTip(copy, "Copy the console line that gives the weapon, its parts, the magazines and the ammo (cheat).");
            _tip.SetToolTip(more, "More: revert, default parts, duplicate, delete.");
            var menu = DarkMenu.Create();
            _miRevert = new ToolStripMenuItem("Revert unsaved changes", null, (s, e) => Revert());
            var miDefaults = new ToolStripMenuItem("Back to the default parts", null, (s, e) =>
            {
                if (_cur == null) return;
                _cur.ResetParts();
                Changed(true);
                _status("Parts reset to the ones the weapon comes with.");
            });
            var miDup = new ToolStripMenuItem("Duplicate", null, (s, e) => Duplicate());
            _miDelete = new ToolStripMenuItem("Delete", null, (s, e) => DeleteCurrent());
            menu.Items.Add(_miRevert);
            menu.Items.Add(miDefaults);
            menu.Items.Add(miDup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_miDelete);
            more.Click += (s, e) => menu.Show(more, new Point(0, more.Height));
            top.Controls.Add(_name);
            top.Controls.Add(line);
            top.Controls.Add(_info);
            top.Controls.Add(_unsaved);
            top.Controls.Add(save);
            top.Controls.Add(copy);
            top.Controls.Add(more);
            top.Resize += (s, e) =>
            {
                int x = top.Width;
                foreach (var b in new[] { more, copy, save })
                {
                    x -= b.Width;
                    b.Location = new Point(x, Theme.S(8));
                    x -= Theme.S(8);
                }
                int w = Math.Max(Theme.S(200), Math.Min(Theme.S(460), x - Theme.S(16)));
                _name.Width = w;
                line.Width = w;
            };

            // base weapon
            var weaponBar = new Panel { Dock = DockStyle.Top, Height = Theme.S(112), BackColor = Theme.Row, Padding = new Padding(1) };
            weaponBar.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, weaponBar.Width - 1, weaponBar.Height - 1); };
            _weaponIcon = new Panel { BackColor = Color.FromArgb(62, 58, 53) };
            _weaponIcon.SetBounds(Theme.S(10), Theme.S(8), Theme.S(96), Theme.S(96));
            _weaponIcon.Paint += PaintWeaponIcon;
            var wl = Theme.Lbl("BASE WEAPON", Theme.Bold, Theme.TextDim);
            wl.BackColor = Theme.Row;
            wl.Location = new Point(Theme.S(122), Theme.S(12));
            _weapon = new ItemCombo { DropDownWidth = Theme.S(360), MaxDropDownItems = 18 };
            _weapon.SetBounds(Theme.S(122), Theme.S(36), Theme.S(360), Theme.S(30));
            string cls = null;
            foreach (var w in WeaponData.Weapons)
            {
                if (w.Cat != cls) { cls = w.Cat; _weapon.Items.Add(WeaponData.ClassName(cls)); }
                _weapon.Items.Add(w);
            }
            _weapon.SelectedIndexChanged += (s, e) => WeaponChanged();
            _tip.SetToolTip(_weapon, "The weapon to build. Parts that still fit are kept when you change it.");
            _weaponInfo = Theme.Lbl("", Theme.Small, Theme.TextDim);
            _weaponInfo.BackColor = Theme.Row;
            _weaponInfo.Location = new Point(Theme.S(122), Theme.S(78));
            weaponBar.Controls.Add(_weaponIcon);
            weaponBar.Controls.Add(wl);
            weaponBar.Controls.Add(_weapon);
            weaponBar.Controls.Add(_weaponInfo);

            // console line
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(132), BackColor = Theme.Back, Padding = new Padding(0, Theme.S(10), 0, 0) };
            var cmdHead = new Panel { Dock = DockStyle.Top, Height = Theme.S(24), BackColor = Theme.Back };
            var cl = Theme.Lbl("CONSOLE COMMAND", Theme.Bold, Theme.TextDim);
            cl.Location = new Point(0, Theme.S(2));
            _cmdInfo = Theme.Lbl("", Theme.Small, Theme.TextFaint);
            cmdHead.Controls.Add(cl);
            cmdHead.Controls.Add(_cmdInfo);
            cmdHead.Resize += (s, e) => _cmdInfo.Location = new Point(cl.Right + Theme.S(12), Theme.S(4));
            _cmd = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
                Font = Theme.Mono, BackColor = Theme.Row, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            };
            _tip.SetToolTip(_cmd, "Paste it in the game console (² / ~, ^ or F10) with Ctrl+V. The items go to your stash/inventory.");
            bottom.Controls.Add(_cmd);
            bottom.Controls.Add(cmdHead);

            // slots
            _rows = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(0, Theme.S(4), Theme.S(4), 0) };
            Theme.DarkScrollbars(_rows);
            _rows.Resize += (s, e) => { if (_cur != null && IsHandleCreated && ComboWidth() != _comboW) BuildRows(false); };

            _stats = new StatsView(_tip) { Dock = DockStyle.Right, Width = Theme.S(290) };
            content.Controls.Add(_rows);
            content.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.S(14), BackColor = Theme.Back });
            content.Controls.Add(_stats);
            content.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(6), BackColor = Theme.Back });
            content.Controls.Add(weaponBar);
            content.Controls.Add(top);
            content.Controls.Add(bottom);
            Controls.Add(content);
            Controls.Add(left);

            if (_builds.Count == 0) AddDraft();
            RefreshList();
            if (_builds.Count > 0) _list.SelectedIndex = 0;
        }

        public ListBox List { get { return _list; } }

        // a first build made when there is none, so the builder is not empty (no question on exit if left untouched)
        private WeaponBuild _draft;
        private string _draftJson;

        private void AddDraft()
        {
            if (WeaponData.Weapons.Count == 0) return;
            _draft = WeaponBuild.New(WeaponData.Weapons[0], "My first build");
            _draftJson = Json.Write(_draft.ToJson());
            _builds.Add(_draft);
        }

        private bool NeedsSaving(WeaponBuild b)
        {
            return b.Dirty && !(b == _draft && b.Saved == null && Json.Write(b.ToJson()) == _draftJson);
        }

        // ================================================================== builds list

        private void RefreshList()
        {
            _loading = true;
            var sel = _cur;
            _list.Items.Clear();
            foreach (var b in _builds) _list.Items.Add(b);
            if (sel != null && _builds.Contains(sel)) _list.SelectedItem = sel;
            _listIndex = _list.SelectedIndex;
            _loading = false;
            _list.Invalidate();
        }

        private void DrawBuild(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            var b = (WeaponBuild)_list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var br = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) g.FillRectangle(br, e.Bounds);
            if (sel) using (var br = new SolidBrush(Theme.Accent)) g.FillRectangle(br, e.Bounds.X, e.Bounds.Y + Theme.S(6), Theme.S(3), e.Bounds.Height - Theme.S(12));
            int s = e.Bounds.Height - Theme.S(8);
            var slot = new Rectangle(e.Bounds.X + Theme.S(10), e.Bounds.Y + Theme.S(4), s, s);
            using (var br = new SolidBrush(Color.FromArgb(62, 58, 53))) g.FillRectangle(br, slot);
            var w = b.Weapon;
            var icon = w == null ? null : Icons.ItemOrCategory(w.Id, w.Cat);
            if (icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, slot);
            }
            var r = new Rectangle(slot.Right + Theme.S(10), e.Bounds.Y, e.Bounds.Right - slot.Right - Theme.S(14), e.Bounds.Height);
            var top = new Rectangle(r.X, r.Y + Theme.S(5), r.Width, Theme.S(20));
            var bot = new Rectangle(r.X, r.Y + Theme.S(24), r.Width, Theme.S(18));
            TextRenderer.DrawText(g, b.Name + (b.Dirty ? " *" : ""), Theme.Bold, top, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int parts = b.Parts().Count(kv => !b.IsDefault(kv.Key, kv.Value));
            TextRenderer.DrawText(g, (w == null ? "?" : w.Name) + " · " + (parts == 0 ? "default parts" : parts + " part" + (parts == 1 ? "" : "s") + " changed"), Theme.Small, bot, Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void ListChanged()
        {
            if (_loading) return;
            var next = _list.SelectedItem as WeaponBuild;
            if (next == _cur) return;
            if (_cur != null && !ConfirmLeave(_cur))
            {
                _loading = true;
                _list.SelectedIndex = _listIndex;
                _loading = false;
                return;
            }
            Load(next);
        }

        /// <summary>Asks to save the unsaved changes of a build. False = cancel.</summary>
        private bool ConfirmLeave(WeaponBuild b)
        {
            if (!_builds.Contains(b) || !NeedsSaving(b)) return true;
            var r = MessageBox.Show(FindForm(), "Save the changes to \"" + b.Name + "\"?", "IRR Companion",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return Save(b);
            Discard(b);
            return true;
        }

        /// <summary>For the main window: unsaved builds when the app closes. False = cancel closing.</summary>
        public bool ConfirmClose()
        {
            var dirty = _builds.Where(NeedsSaving).ToList();
            if (dirty.Count == 0) return true;
            var r = MessageBox.Show(FindForm(), "Save the weapon builds " + string.Join(", ", dirty.Select(b => "\"" + b.Name + "\"")) + " ?",
                "IRR Companion", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) return true;
            foreach (var b in dirty) b.MarkSaved();
            return TryWrite();
        }

        private void Discard(WeaponBuild b)
        {
            int i = _builds.IndexOf(b);
            if (i < 0) return;
            if (b.Saved == null) _builds.RemoveAt(i);
            else
            {
                var fresh = WeaponBuild.FromJson((JObj)Json.Parse(b.Saved));
                fresh.MarkSaved();
                _builds[i] = fresh;
            }
        }

        private bool TryWrite()
        {
            try
            {
                WeaponBuilds.Save(_builds);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Could not save the weapon builds:\n" + ex.Message + "\n\nFile: " + WeaponBuilds.FilePath,
                    "IRR Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private bool Save(WeaponBuild b)
        {
            if (string.IsNullOrWhiteSpace(b.Name)) b.Name = b.Weapon != null ? b.Weapon.Name + " build" : "Build";
            string old = b.Saved;
            b.MarkSaved();
            if (TryWrite()) return true;
            b.Saved = old;
            return false;
        }

        public void SaveCurrent()
        {
            if (_cur == null) return;
            if (Save(_cur))
            {
                _status("Build \"" + _cur.Name + "\" saved to " + WeaponBuilds.FilePath);
                LoadName();
                RefreshList();
                UpdateState();
            }
        }

        private void NewBuild()
        {
            var w = _weapon.SelectedWeaponItem ?? WeaponData.Weapons.FirstOrDefault();
            if (w == null) return;
            var b = WeaponBuild.New(w, NewName(w.Name + " build"));
            _builds.Add(b);
            RefreshList();
            _list.SelectedItem = b;
        }

        private string NewName(string baseName)
        {
            string n = baseName;
            for (int i = 2; _builds.Any(b => b.Name == n); i++) n = baseName + " " + i;
            return n;
        }

        private void Duplicate()
        {
            if (_cur == null) return;
            var b = _cur.Copy(NewName(_cur.Name + " copy"));
            _builds.Insert(_builds.IndexOf(_cur) + 1, b);
            RefreshList();
            _list.SelectedItem = b;
        }

        private void Revert()
        {
            if (_cur == null || !_cur.Dirty || _cur.Saved == null) return;
            int i = _builds.IndexOf(_cur);
            Discard(_cur);
            _cur = null;
            RefreshList();
            _list.SelectedIndex = i;
            _status("Unsaved changes reverted.");
        }

        private void DeleteCurrent()
        {
            if (_cur == null) return;
            if (MessageBox.Show(FindForm(), "Delete the build \"" + _cur.Name + "\"?", "IRR Companion", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            int i = _builds.IndexOf(_cur);
            bool wasSaved = _cur.Saved != null;
            _builds.Remove(_cur);
            _cur = null;
            if (wasSaved) TryWrite();
            if (_builds.Count == 0) AddDraft();
            RefreshList();
            _list.SelectedIndex = Math.Min(i, _builds.Count - 1);
            _status("Build deleted.");
        }

        // ================================================================== editor

        private void Load(WeaponBuild b)
        {
            _cur = b;
            _listIndex = _list.SelectedIndex;
            _loading = true;
            LoadName();
            var w = b == null ? null : b.Weapon;
            _weapon.SelectedItem = w;
            _loading = false;
            _miDelete.Enabled = b != null;
            BuildRows(true);
            UpdateState();
        }

        private void LoadName()
        {
            bool l = _loading;
            _loading = true;
            _name.Text = _cur == null ? "" : _cur.Name;
            _loading = l;
        }

        private void WeaponChanged()
        {
            if (_loading || _cur == null) return;
            var w = _weapon.SelectedWeaponItem;
            if (w == null || (_cur.Root != null && _cur.Root.Item == w.Id)) return;
            _cur.ChangeWeapon(w);
            Changed(true);
        }

        /// <summary>After any change: refresh the rows (when parts changed), the console line and the save state.</summary>
        private void Changed(bool parts)
        {
            if (parts) BeginInvoke((Action)(() => BuildRows(false)));
            UpdateState();
            _list.Invalidate();
        }

        private void UpdateState()
        {
            var b = _cur;
            _miRevert.Enabled = b != null && b.Dirty && b.Saved != null;
            _unsaved.Visible = b != null && b.Dirty;
            _weaponIcon.Invalidate();
            _stats.SetBuild(b);
            if (b == null || b.Root == null)
            {
                _info.Text = "";
                _cmd.Text = "";
                _cmdInfo.Text = "";
                _weaponInfo.Text = "";
                return;
            }
            var w = b.Weapon;
            var lines = b.Lines();
            int parts = b.Parts().Count(kv => !b.IsDefault(kv.Key, kv.Value));
            _info.Text = (b.Saved == null ? "Not saved yet" : "Saved") + " · " + (parts == 0 ? "default parts" : parts + " part" + (parts == 1 ? "" : "s") + " changed");
            _unsaved.Location = new Point(_info.Right + Theme.S(4), _info.Top);
            int slots = w == null ? 0 : w.Slots.Count(s => !s.IsAmmo);
            _weaponInfo.Text = w == null ? "" : WeaponData.ClassName(w.Cat) + " · " + slots + " slot" + (slots == 1 ? "" : "s") + " on the weapon · additem " + w.Id;
            _cmd.Text = b.Command();
            _cmdInfo.Text = lines.Count + " additem command" + (lines.Count == 1 ? "" : "s") + " · the default parts come with the weapon" +
                            (b.Missing.Count > 0 ? " · left out (additem cannot give them): " + string.Join(", ", b.Missing) : "");
            _cmdInfo.ForeColor = b.Missing.Count > 0 ? Theme.Warn : Theme.TextFaint;
        }

        private void PaintWeaponIcon(object sender, PaintEventArgs e)
        {
            var w = _cur == null ? null : _cur.Weapon;
            var icon = w == null ? null : Icons.ItemOrCategory(w.Id, w.Cat);
            var r = _weaponIcon.ClientRectangle;
            if (icon == null)
            {
                TextRenderer.DrawText(e.Graphics, "?", Theme.Title, r, Theme.TextFaint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(icon, Rectangle.Inflate(r, -Theme.S(2), -Theme.S(2)));
        }

        private void CopyCommand()
        {
            if (_cur == null || _cur.Root == null) return;
            string c = _cur.Command();
            try
            {
                Clipboard.SetText(c);
                _status("Copied the \"" + _cur.Name + "\" build (" + _cur.Lines().Count + " additem)   ·   paste it in the game console (² / ~, ^ or F10) with Ctrl+V.");
            }
            catch (Exception ex) { _status("Could not access the clipboard: " + ex.Message); }
        }

        // ================================================================== slot rows

        /// <summary>Width of the part lists: follows the window, between 420 and 680 px (at 100 %).</summary>
        private int _comboW;

        private int ComboWidth() { return Math.Max(Theme.S(360), Math.Min(Theme.S(680), _rows.Width - Theme.S(134) - Theme.S(240))); }

        private void BuildRows(bool resetScroll)
        {
            _comboW = ComboWidth();
            var scroll = _rows.AutoScrollPosition;
            _rows.SuspendLayout();
            var old = _rows.Controls.Cast<Control>().ToList();
            _rows.Controls.Clear();
            foreach (var c in old) c.Dispose();
            var items = new List<Control>();
            if (_cur != null && _cur.Root != null)
            {
                var w = _cur.Weapon;
                items.Add(Caption("PARTS", "Pick a part for each slot. A part can have slots of its own (rails, sights...), shown under it."));
                AddRows(items, _cur.Root, 0, new List<string>());
                var ammo = w == null ? null : w.Slots.FirstOrDefault(s => s.IsAmmo);
                if (ammo != null)
                {
                    items.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(10), BackColor = Theme.Back });
                    items.Add(Caption("AMMO", "The weapon and the magazines come loaded; add loose rounds if you want more."));
                    items.Add(AmmoRow(ammo));
                }
            }
            for (int i = items.Count - 1; i >= 0; i--) _rows.Controls.Add(items[i]);
            _rows.ResumeLayout();
            _rows.AutoScrollPosition = resetScroll ? Point.Empty : new Point(-scroll.X, -scroll.Y);
        }

        private static Control Caption(string text, string hint)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = Theme.S(30), BackColor = Theme.Back };
            var t = Theme.Lbl(text, Theme.Bold, Theme.TextDim);
            t.Location = new Point(0, Theme.S(6));
            var h = Theme.Lbl(hint, Theme.Small, Theme.TextFaint);
            p.Controls.Add(t);
            p.Controls.Add(h);
            p.Resize += (s, e) => h.Location = new Point(t.Right + Theme.S(10), Theme.S(8));
            return p;
        }

        private void AddRows(List<Control> items, BuildNode node, int depth, List<string> path)
        {
            var it = WeaponData.Find(node.Item);
            if (it == null) return;
            foreach (var slot in it.Slots)
            {
                if (slot.IsAmmo || slot.Items.Count == 0) continue;
                BuildNode child;
                node.Parts.TryGetValue(slot.Name, out child);
                var p = new List<string>(path) { slot.Name };
                items.Add(SlotRow(node, slot, child, depth, p));
                if (child != null) AddRows(items, child, depth + 1, p);
            }
        }

        private void ScrollRows(int delta)
        {
            int y = -_rows.AutoScrollPosition.Y - delta / 2;
            _rows.AutoScrollPosition = new Point(0, Math.Max(0, y));
        }

        private Control SlotRow(BuildNode parent, WeaponSlot slot, BuildNode child, int depth, List<string> path)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = Theme.S(40), BackColor = Theme.Back };
            int x = depth * Theme.S(28);
            if (depth > 0)
                row.Paint += (s, e) =>
                {
                    using (var p = new Pen(Theme.Track))
                    {
                        int lx = x - Theme.S(16);
                        e.Graphics.DrawLine(p, lx, 0, lx, row.Height / 2);
                        e.Graphics.DrawLine(p, lx, row.Height / 2, x - Theme.S(4), row.Height / 2);
                    }
                };
            var name = Theme.Lbl(slot.Name, Theme.Body, child == null ? Theme.TextDim : Theme.Text);
            name.AutoSize = false;
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.SetBounds(x, 0, Theme.S(130), row.Height);
            _tip.SetToolTip(name, slot.Items.Count + " part" + (slot.Items.Count == 1 ? "" : "s") + " fit this slot.");
            var combo = new ItemCombo { Wheel = ScrollRows, DropDownWidth = Math.Max(Theme.S(420), _comboW - x), EmptyIcon = Icons.Category(ItemCombo.SlotCategory(slot.Name)) };
            combo.SetBounds(x + Theme.S(134), Theme.S(4), _comboW - x, Theme.S(30));
            combo.Items.Add(ItemCombo.None);
            foreach (var id in slot.Items)
            {
                var p = WeaponData.Find(id);
                if (p != null) combo.Items.Add(p);
            }
            combo.SelectedItem = child == null ? ItemCombo.None : (object)WeaponData.Find(child.Item);
            combo.SelectedIndexChanged += (s, e) =>
            {
                var picked = combo.SelectedWeaponItem;
                BuildNode cur;
                parent.Parts.TryGetValue(slot.Name, out cur);
                if (picked == null)
                {
                    if (cur == null) return;
                    parent.Parts.Remove(slot.Name);
                }
                else
                {
                    if (cur != null && cur.Item == picked.Id) return;
                    parent.Parts[slot.Name] = cur == null ? new BuildNode(picked.Id) : cur.Replace(picked.Id);
                }
                Changed(true);
            };
            row.Controls.Add(name);
            row.Controls.Add(combo);
            var item = child == null ? null : WeaponData.Find(child.Item);
            bool def = _cur.IsDefault(path, child);
            if (item != null && item.IsMag && _cur.Mag == child)
            {
                var n = Num(1, 20, _cur.Mags);
                n.SetBounds(combo.Right + Theme.S(12), Theme.S(6), Theme.S(56), Theme.S(28));
                n.ValueChanged += (s, e) => { if (_cur == null) return; _cur.Mags = (int)n.Value; Changed(true); };
                var load = _cur.LoadOf(item);
                var ammo = load == null ? null : WeaponData.Find(load.Ammo);
                string text = "magazines" + (def ? " (1 comes with the weapon)" : "") +
                              (load != null && load.Rounds > 0 && ammo != null ? " · loaded with " + load.Rounds + " × " + ammo.Name : " · empty");
                var l = Theme.Lbl(text, Theme.Small, Theme.TextDim);
                l.Location = new Point(n.Right + Theme.S(6), Theme.S(12));
                _tip.SetToolTip(n, "How many of this magazine in total, the one on the weapon included. additem gives them loaded" +
                                   (item.Loads.Count > 1 ? " (with the ammo picked below when the game has that variant)." : "."));
                row.Controls.Add(n);
                row.Controls.Add(l);
            }
            else if (item != null)
            {
                string text = def ? "comes with the weapon" : item.Give == null ? "additem cannot give this part" : "";
                var l = Theme.Lbl(text, Theme.Small, item.Give == null && !def ? Theme.Warn : Theme.TextFaint);
                l.Location = new Point(combo.Right + Theme.S(12), Theme.S(12));
                if (def) _tip.SetToolTip(l, "additem gives the weapon with this part already on it: it is not in the command.");
                row.Controls.Add(l);
            }
            return row;
        }

        private Control AmmoRow(WeaponSlot slot)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = Theme.S(40), BackColor = Theme.Back };
            var name = Theme.Lbl("Rounds", Theme.Body, Theme.Text);
            name.AutoSize = false;
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.SetBounds(0, 0, Theme.S(130), row.Height);
            var combo = new ItemCombo { Wheel = ScrollRows, DropDownWidth = Math.Max(Theme.S(420), _comboW) };
            combo.SetBounds(Theme.S(134), Theme.S(4), _comboW, Theme.S(30));
            combo.Items.Add(ItemCombo.None);
            foreach (var id in slot.Items)
            {
                var p = WeaponData.Find(id);
                if (p != null) combo.Items.Add(p);
            }
            var cur = WeaponData.Find(_cur.Ammo);
            combo.SelectedItem = cur == null ? ItemCombo.None : (object)cur;
            combo.SelectedIndexChanged += (s, e) =>
            {
                var picked = combo.SelectedWeaponItem;
                string id = picked == null ? null : picked.Id;
                if (id == _cur.Ammo) return;
                _cur.Ammo = id;
                Changed(true);  // the magazine row shows what it is loaded with
            };
            _tip.SetToolTip(combo, "Ammo type. The magazines are given loaded with it when the game has that variant (e.g. AR-15 magazines with x300).");
            var n = Num(0, 10000, _cur.AmmoCount);
            n.Increment = 30;
            n.SetBounds(combo.Right + Theme.S(12), Theme.S(6), Theme.S(80), Theme.S(28));
            n.ValueChanged += (s, e) => { _cur.AmmoCount = (int)n.Value; Changed(false); };
            var l = Theme.Lbl("loose rounds", Theme.Small, Theme.TextDim);
            l.Location = new Point(n.Right + Theme.S(6), Theme.S(12));
            row.Controls.Add(name);
            row.Controls.Add(combo);
            row.Controls.Add(n);
            row.Controls.Add(l);
            return row;
        }

        private static NumericUpDown Num(int min, int max, int value)
        {
            return new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), BackColor = Theme.Row, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle, Font = Theme.Bold, TextAlign = HorizontalAlignment.Right,
            };
        }

#if DEBUG
        /// <summary>Test helper: shows example builds (in memory only, nothing is saved).</summary>
        private readonly List<WeaponBuild> _testBuilds = new List<WeaponBuild>();

        public void TestShow(int index)
        {
            if (_testBuilds.Count == 0)
            {
                var b1 = WeaponBuild.New(WeaponData.Find("AKS-74U"), "AKS-74U suppressed");
                b1.Root.Parts["Handguard"] = new BuildNode("AK_Zenitco_B-11_Handguard");
                b1.Root.Parts["Handguard"].Parts["Grip"] = new BuildNode("Troy_Modular_Combat_Grip");
                b1.Root.Parts["Handguard"].Parts["Tactical 1"] = new BuildNode("PEQ-15_LA-5_Tactical_Device");
                b1.Root.Parts["Muzzle"] = new BuildNode("AK-74_Steel_PATRIOT_Silencer");
                b1.Root.Parts["Mag"] = new BuildNode("AK_545x39_Drum_95rnd");
                b1.Root.Parts["Rail Mount"] = new BuildNode("AK_Zenitco_B-13_Rail");
                b1.Root.Parts["Stock"] = new BuildNode("AK_SAG_Buffertube");
                b1.Root.Parts["Pistol Grip"] = new BuildNode("AK_Doublestar_AR-15_Pistol_Grip_Adapter");
                b1.Normalize();
                b1.AmmoCount = 120;
                b1.MarkSaved();
                var b2 = WeaponBuild.New(WeaponData.Find("AUG_A3"), "AUG long range");
                b2.Root.Parts["Barrel"] = new BuildNode("AUG_A3_Barrel_20_Inch_Barrel");
                b2.Root.Parts["Barrel"].Parts["Muzzle"] = new BuildNode("AUG_Ase_Utra_S_Series_SL7i_Suppressor");
                b2.Root.Parts["Mag"] = new BuildNode("AUG_556x45_42rnd");
                b2.Normalize();
                b2.MarkSaved();
                b2.Mags = 3;
                _testBuilds.Add(b1);
                _testBuilds.Add(b2);
                _builds.Insert(0, b2);
                _builds.Insert(0, b1);
                RefreshList();
            }
            _list.SelectedIndex = Math.Min(index, _builds.Count - 1);
            Load((WeaponBuild)_list.SelectedItem);
            Application.DoEvents();
        }

        /// <summary>Test helper: drops the example builds again.</summary>
        public void TestClear()
        {
            _builds.RemoveAll(_testBuilds.Contains);
            _testBuilds.Clear();
            _cur = null;
            RefreshList();
        }
#endif
    }

    /// <summary>STATS of a build: the weapon's values with its parts, compared with the weapon as additem gives it,
    /// then the stats of the ammo.</summary>
    public sealed class StatsView : Panel
    {
        private readonly ToolTip _tip;
        private Dictionary<string, double> _now, _ref;
        private WeaponItem _ammo;
        private readonly List<KeyValuePair<Rectangle, string>> _tips = new List<KeyValuePair<Rectangle, string>>();
        private string _shownTip;

        public StatsView(ToolTip tip)
        {
            _tip = tip;
            BackColor = Theme.Row;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        public void SetBuild(WeaponBuild b)
        {
            _now = b == null ? null : WeaponStats.Compute(b.Root);
            var w = b == null ? null : b.Weapon;
            _ref = w == null || w.Defaults == null ? null : WeaponStats.Compute(w.Defaults);
            string ammo = b == null ? null : b.Ammo;
            if (ammo == null && b != null && b.Mag != null)
            {
                var load = b.LoadOf(WeaponData.Find(b.Mag.Item));
                if (load != null) ammo = load.Ammo;
            }
            _ammo = WeaponData.Find(ammo);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            _tips.Clear();
            int pad = Theme.S(12), y = Theme.S(10), w = Width - 2 * pad;
            TextRenderer.DrawText(g, "STATS", Theme.Bold, new Point(pad, y), Theme.TextDim);
            _tips.Add(new KeyValuePair<Rectangle, string>(new Rectangle(pad, y, w, Theme.S(20)),
                "Stats of the weapon with its parts, from the game files. The totals are worked out like the game's stat\n" +
                "modifiers (additions summed, percentages summed then applied); the game may round them differently.\n" +
                "Green / red: better / worse than the weapon as additem gives it (its default parts)."));
            y += Theme.S(22);
            if (_now == null)
            {
                TextRenderer.DrawText(g, "No stats for this weapon.", Theme.Small, new Point(pad, y), Theme.TextFaint);
                return;
            }
            TextRenderer.DrawText(g, "vs the weapon as additem gives it", Theme.Small, new Point(pad, y), Theme.TextFaint);
            y += Theme.S(20);
            foreach (var stat in WeaponStats.Order)
            {
                double v;
                if (!_now.TryGetValue(stat, out v)) continue;
                double r = v;
                bool hasRef = _ref != null && _ref.TryGetValue(stat, out r);
                var row = new Rectangle(pad, y, w, Theme.S(stat == "Ergonomics" || stat == "Recoil" ? 36 : 21));
                TextRenderer.DrawText(g, WeaponStats.Label(stat).ToUpperInvariant(), Theme.Small, new Point(row.X, row.Y + Theme.S(4)), Theme.TextDim);
                string val = WeaponStats.Format(stat, v);
                var vs = TextRenderer.MeasureText(g, val, Theme.Bold);
                int deltaW = Theme.S(64);
                TextRenderer.DrawText(g, val, Theme.Bold, new Point(row.Right - deltaW - vs.Width, row.Y + Theme.S(2)), Theme.Text);
                double d = hasRef ? v - r : 0;
                if (Math.Abs(d) > 0.0049)
                {
                    bool better = WeaponStats.LowerIsBetter(stat) ? d < 0 : d > 0;
                    string ds = (d > 0 ? "+" : "\u2212") + WeaponStats.Number(stat, Math.Abs(d));
                    TextRenderer.DrawText(g, ds, Theme.Small, new Rectangle(row.Right - deltaW, row.Y + Theme.S(4), deltaW, Theme.S(18)),
                        better ? Theme.Higher : Theme.Lower, TextFormatFlags.Right);
                }
                if (stat == "Ergonomics" || stat == "Recoil")
                {
                    var bar = new Rectangle(row.X, row.Y + Theme.S(24), w, Theme.S(6));
                    using (var b = new SolidBrush(Theme.Track)) g.FillRectangle(b, bar);
                    float f = (float)Math.Max(0, Math.Min(1, v / 100));
                    using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, bar.X, bar.Y, bar.Width * f, bar.Height);
                    if (hasRef)
                    {
                        float rx = bar.X + bar.Width * (float)Math.Max(0, Math.Min(1, r / 100));
                        using (var p = new Pen(Theme.TextDim, 2)) g.DrawLine(p, rx, bar.Y - Theme.S(2), rx, bar.Bottom + Theme.S(2));
                    }
                }
                _tips.Add(new KeyValuePair<Rectangle, string>(row, Tip(stat, v, hasRef ? r : (double?)null)));
                y = row.Bottom + Theme.S(3);
            }
            if (_ammo != null && _ammo.Stats.Any(m => WeaponStats.AmmoOrder.Contains(m.Stat)))
            {
                y += Theme.S(4);
                using (var p = new Pen(Theme.Track)) g.DrawLine(p, pad, y, pad + w, y);
                y += Theme.S(6);
                var title = new Rectangle(pad, y, w, Theme.S(20));
                TextRenderer.DrawText(g, "AMMO  " + _ammo.Name, Theme.Small, title, Theme.TextDim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += Theme.S(20);
                // the ammo stats side by side: label above, value below
                int cw = w / WeaponStats.AmmoOrder.Length, x = pad;
                var lines = new List<string>();
                foreach (var stat in WeaponStats.AmmoOrder)
                {
                    var m = _ammo.Stat(stat);
                    if (m == null) continue;
                    string lab = stat == "PiercingLevel" ? "PIERCING" : WeaponStats.Label(stat).ToUpperInvariant();
                    TextRenderer.DrawText(g, lab, Theme.Small, new Point(x, y), Theme.TextDim);
                    TextRenderer.DrawText(g, WeaponStats.Format(stat, m.Value), Theme.Bold, new Point(x, y + Theme.S(16)), Theme.Text);
                    lines.Add(WeaponStats.Label(stat) + ": " + WeaponStats.Format(stat, m.Value));
                    x += cw;
                }
                _tips.Add(new KeyValuePair<Rectangle, string>(new Rectangle(pad, title.Y, w, Theme.S(56)),
                    _ammo.Name + "\n" + string.Join("\n", lines) + "\nThe ammo of the build, else the one the magazine comes loaded with."));
            }
        }

        private static string Tip(string stat, double v, double? r)
        {
            string s = WeaponStats.Label(stat) + ": " + WeaponStats.Format(stat, v);
            if (r != null) s += "\nWith the default parts: " + WeaponStats.Format(stat, r.Value);
            switch (stat)
            {
                case "Ergonomics": s += "\nHigher is better (handling, aiming)."; break;
                case "Recoil": s += "\nLower is better. Parts change it by a percentage."; break;
                case "Weight": s += "\nWeapon and parts (magazine included, not the rounds)."; break;
                case "Accuracy": s += "\nLower is better (minutes of angle)."; break;
                case "MuzzleVelocity": s += "\nBarrels and muzzle devices change it by a percentage."; break;
                case "SightingRange": s += "\nThe sight's range when a sight is fitted."; break;
            }
            return s;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string t = _tips.Where(x => x.Key.Contains(e.Location)).Select(x => x.Value).FirstOrDefault();
            if (t == _shownTip) return;
            _shownTip = t;
            _tip.SetToolTip(this, t ?? "");
        }
    }
}
