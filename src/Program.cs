using System;
using System.Windows.Forms;

namespace IRRCompanion
{
    internal static class Program
    {
        /// <summary>Version this exe was updated from (started by the updater), else null.</summary>
        public static string UpdatedFrom;

        [STAThread]
        private static void Main(string[] args)
        {
#if DEBUG
            if (TestHelpers.Run(args)) return;
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            try
            {
                GameData.Load();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return;
            }
            Library.MigrateOldFolder();
            AppSettings.Load();
            UpdatedFrom = Updater.UpdatedFrom(args);
            Updater.CleanUp();
            Application.Run(new MainForm());
            Updater.InstallIfReady();  // a downloaded update replaces the exe once the window is closed
        }

        private static void ShowError(Exception ex)
        {
            MessageBox.Show("Unexpected error:\n\n" + (ex == null ? "unknown" : ex.ToString()), "IRR Companion",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

#if DEBUG
    /// <summary>Debug-build command-line helpers used for automated checks.</summary>
    internal static class TestHelpers
    {
        public static bool Run(string[] args)
        {
            if (args.Length < 2) return false;
            switch (args[0])
            {
                case "--decode":
                case "--roundtrip":
                {
                    GameData.Load();
                    var obj = ShareCode.Decode(System.IO.File.ReadAllText(args[1]).Trim());
                    string output = args[0] == "--decode"
                        ? Json.Write(obj, true)
                        : Preset.FromShareObject(obj).ToShareCode();
                    if (args.Length >= 3) System.IO.File.WriteAllText(args[2], output);
                    return true;
                }
                case "--self-update":
                {
                    // test of the updater: shot of the update window, then download, check, swap and restart
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var info = Updater.Check();
                    if (info == null) { System.IO.File.WriteAllText(args[1] + ".txt", "no newer release"); return true; }
                    using (var d = new UpdateDialog(info))
                    {
                        d.StartPosition = FormStartPosition.Manual;
                        d.Location = new System.Drawing.Point(-30000, -30000);
                        d.Show();
                        Application.DoEvents();
                        using (var bmp = new System.Drawing.Bitmap(d.Width, d.Height))
                        {
                            d.DrawToBitmap(bmp, new System.Drawing.Rectangle(System.Drawing.Point.Empty, d.Size));
                            bmp.Save(args[1] + "_dialog.png");
                        }
                    }
                    Updater.Download(info, p => { }, System.Threading.CancellationToken.None);
                    System.IO.File.WriteAllText(args[1] + ".txt", "downloaded " + info.Tag + ", installing");
                    Updater.InstallIfReady();
                    return true;
                }
                case "--screenshot":
                    try
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        GameData.Load();
                        if (args.Length >= 3) AppSettings.UiZoom = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                        using (var f = new MainForm())
                        {
                            f.StartPosition = FormStartPosition.Manual;
                            f.Location = new System.Drawing.Point(-30000, -30000);
                            f.ShowInTaskbar = false;
                            f.Show();
                            Application.DoEvents();
                            f.RenderTestShots(args[1]);
                            f.Hide();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.WriteAllText(args[1] + ".error.txt", ex.ToString());
                    }
                    return true;
            }
            return false;
        }
    }
#endif
}
