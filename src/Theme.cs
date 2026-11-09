using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>Dark palette inspired by the in-game settings menu.</summary>
    public static class Theme
    {
        public static readonly Color Back = Color.FromArgb(24, 21, 20);
        public static readonly Color Panel = Color.FromArgb(33, 29, 27);
        public static readonly Color PanelAlt = Color.FromArgb(42, 37, 34);
        public static readonly Color Row = Color.FromArgb(30, 27, 25);
        public static readonly Color RowHover = Color.FromArgb(40, 35, 32);
        public static readonly Color Border = Color.FromArgb(92, 86, 74);
        public static readonly Color Text = Color.FromArgb(226, 221, 200);
        public static readonly Color TextDim = Color.FromArgb(142, 136, 122);
        public static readonly Color TextFaint = Color.FromArgb(96, 91, 82);
        public static readonly Color Accent = Color.FromArgb(214, 208, 176);
        public static readonly Color AccentDark = Color.FromArgb(46, 58, 44);
        public static readonly Color Track = Color.FromArgb(70, 66, 60);
        public static readonly Color Higher = Color.FromArgb(139, 195, 74);
        public static readonly Color Lower = Color.FromArgb(239, 108, 87);
        public static readonly Color Changed = Color.FromArgb(240, 200, 90);
        public static readonly Color Warn = Color.FromArgb(255, 167, 38);
        public static readonly Color ColorA = Color.FromArgb(93, 169, 233);
        public static readonly Color ColorB = Color.FromArgb(240, 160, 64);
        public static readonly Color DiffRow = Color.FromArgb(44, 39, 33);
        public static readonly Color Danger = Color.FromArgb(150, 60, 50);

        public static Font Body, Bold, Small, Title, Header, Mono;
        public static float Scale = 1f;

        public static void Init(Control c)
        {
            float z = AppSettings.UiZoom;  // interface size chosen by the user, on top of the Windows scaling
            using (var g = c.CreateGraphics()) Scale = g.DpiX / 96f * z;
            Body = new Font("Segoe UI", 9.5f * z);
            Bold = new Font("Segoe UI Semibold", 9.5f * z);
            Small = new Font("Segoe UI", 8.5f * z);
            Title = new Font("Segoe UI Semibold", 15f * z);
            Header = new Font("Segoe UI Semibold", 11.5f * z);
            Mono = new Font("Consolas", 9.5f * z);
        }

        public static int S(int px) { return (int)Math.Round(px * Scale); }

        public static Button Btn(string text, EventHandler click, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? AccentDark : Panel,
                ForeColor = Text,
                Font = Bold,
                Height = S(32),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(62, 78, 58) : PanelAlt;
            b.FlatAppearance.MouseDownBackColor = AccentDark;
            if (click != null) b.Click += click;
            return b;
        }

        public static TextBox Box(bool multiline = false)
        {
            return new TextBox
            {
                BackColor = Row,
                ForeColor = Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Body,
                Multiline = multiline,
            };
        }

        public static Label Lbl(string text, Font f = null, Color? color = null)
        {
            return new Label
            {
                Text = text,
                Font = f ?? Body,
                ForeColor = color ?? Text,
                AutoSize = true,
                BackColor = Color.Transparent,
            };
        }

        // ----- native dark mode helpers (Windows 10 1809+; silently ignored elsewhere) -----
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkScrollbars(Control c)
        {
            try
            {
                if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                else c.HandleCreated += (s, e) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            }
            catch (Exception) { }
        }

        public static void DarkTitleBar(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch (Exception) { }
        }
    }
}
