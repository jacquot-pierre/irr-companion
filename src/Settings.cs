using System;
using System.IO;
using System.Text;

namespace IRRCompanion
{
    /// <summary>App preferences (settings.json in the data folder).</summary>
    public static class AppSettings
    {
        /// <summary>Interface size, applied at start: 1 = 100 %.</summary>
        public static float UiZoom = 1f;

        public static readonly float[] Zooms = { 1f, 1.15f, 1.25f, 1.5f, 1.75f, 2f };

        public static string FilePath { get { return Path.Combine(Library.Folder, "settings.json"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JObj;
                if (root == null) return;
                float z = (float)root.Num("uiZoom", 1);
                if (z >= 0.75f && z <= 3f) UiZoom = z;
            }
            catch (Exception) { /* a broken settings file only resets the preferences */ }
        }

        public static void Save()
        {
            Directory.CreateDirectory(Library.Folder);
            var root = new JObj();
            root["format"] = "IRRCompanion.Settings";
            root["uiZoom"] = Math.Round(UiZoom, 2);
            File.WriteAllText(FilePath, Json.Write(root, true), new UTF8Encoding(false));
        }
    }
}
