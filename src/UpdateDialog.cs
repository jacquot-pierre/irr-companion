using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>"A new version is available": release notes, then download (with progress) and restart.</summary>
    public sealed class UpdateDialog : Form
    {
        public enum Result { Later, Skip, Installed }

        private readonly UpdateInfo _info;
        private readonly Label _state;
        private readonly Panel _bar, _fill;
        private readonly Button _update, _skip, _later;
        private CancellationTokenSource _cancel;
        public Result Choice = Result.Later;

        public UpdateDialog(UpdateInfo info)
        {
            _info = info;
            Text = "Update IRR Companion";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Theme.S(720), Theme.S(480));
            MinimizeBox = MaximizeBox = false;
            ShowIcon = false;
            Padding = new Padding(Theme.S(14));

            var title = Theme.Lbl("IRR Companion " + Updater.Format(info.Version) + " is available", Theme.Header, Theme.Accent);
            title.Dock = DockStyle.Top;
            title.AutoSize = false;
            title.Height = Theme.S(32);
            var sub = Theme.Lbl("You have " + Updater.CurrentText + ". What's new:", Theme.Small, Theme.TextDim);
            sub.Dock = DockStyle.Top;
            sub.AutoSize = false;
            sub.Height = Theme.S(24);

            var notes = Theme.Box(true);
            notes.Dock = DockStyle.Fill;
            notes.ReadOnly = true;
            notes.ScrollBars = ScrollBars.Vertical;
            notes.WordWrap = true;
            notes.Text = string.IsNullOrWhiteSpace(info.Notes) ? "(no release notes)" : info.Notes;
            Theme.DarkScrollbars(notes);

            // progress bar + state line
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(96), BackColor = Theme.Back };
            _state = Theme.Lbl("Your presets, favorites, builds and progress are kept: they are not stored in the exe.", Theme.Small, Theme.TextDim);
            _state.AutoSize = false;
            _state.SetBounds(0, Theme.S(8), Theme.S(680), Theme.S(20));
            _state.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _bar = new Panel { BackColor = Theme.Row, Visible = false };
            _bar.SetBounds(0, Theme.S(32), Theme.S(680), Theme.S(8));
            _bar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _fill = new Panel { BackColor = Theme.Accent, Width = 0, Dock = DockStyle.Left };
            _bar.Controls.Add(_fill);

            bool canInstall = Updater.CanInstall();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(44), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Back };
            _update = canInstall
                ? Theme.Btn("UPDATE AND RESTART", (s, e) => Start(), true)
                : Theme.Btn("OPEN DOWNLOAD PAGE", (s, e) => { OpenPage(); Choice = Result.Later; Close(); }, true);
            _later = Theme.Btn("LATER", (s, e) => { Choice = Result.Later; Close(); });
            _skip = Theme.Btn("SKIP THIS VERSION", (s, e) => { Choice = Result.Skip; Close(); });
            _update.Width = Theme.S(210);
            _later.Width = Theme.S(100);
            _skip.Width = Theme.S(170);
            buttons.Controls.Add(_update);
            buttons.Controls.Add(_later);
            buttons.Controls.Add(_skip);
            CancelButton = _later;
            if (!canInstall)
                _state.Text = "The app's folder cannot be written (Program Files?): download the new exe from the release page.";
            bottom.Controls.Add(_state);
            bottom.Controls.Add(_bar);
            bottom.Controls.Add(buttons);

            Controls.Add(notes);
            Controls.Add(bottom);
            Controls.Add(sub);
            Controls.Add(title);
        }

        private void OpenPage()
        {
            try { Process.Start(_info.PageUrl ?? Updater.ReleasesPage); } catch (Exception) { }
        }

        private void Start()
        {
            _update.Enabled = _skip.Enabled = false;
            _later.Text = "CANCEL";
            _bar.Visible = true;
            _state.ForeColor = Theme.TextDim;
            _state.Text = "Downloading " + Updater.Format(_info.Version) + "...";
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            Task.Run(() => Updater.Download(_info, p => Ui(() =>
            {
                _fill.Width = (int)(_bar.Width * p);
                _state.Text = "Downloading " + Updater.Format(_info.Version) + "...  " + (int)(p * 100) + " %";
            }), token), token).ContinueWith(t => Ui(() =>
            {
                if (t.IsCanceled || token.IsCancellationRequested) { Close(); return; }
                if (t.IsFaulted)
                {
                    var ex = t.Exception.GetBaseException();
                    _state.Text = "Download failed: " + ex.Message;
                    _state.ForeColor = Theme.Lower;
                    _update.Enabled = _skip.Enabled = true;
                    _update.Text = "RETRY";
                    _later.Text = "LATER";
                    return;
                }
                Choice = Result.Installed;
                Close();
            }));
        }

        /// <summary>Runs on the UI thread, unless the window is already closed.</summary>
        private void Ui(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); }
            catch (InvalidOperationException) { /* closed meanwhile */ }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cancel != null && !_cancel.IsCancellationRequested && Choice != Result.Installed) _cancel.Cancel();
            base.OnFormClosing(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = _update;  // not the notes: a focused text box selects all its text
        }
    }
}
