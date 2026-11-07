using System;
using System.Windows.Forms;

namespace IRRPresetTool
{
    internal static class Program
    {
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
            Application.Run(new MainForm());
        }

        private static void ShowError(Exception ex)
        {
            MessageBox.Show("Unexpected error:\n\n" + (ex == null ? "unknown" : ex.ToString()), "IRR Preset Tool",
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
                case "--screenshot":
                    try
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        GameData.Load();
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
