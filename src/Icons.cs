using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;

namespace IRRCompanion
{
    /// <summary>
    /// Game icons embedded in the exe (src/Resources/icons, made by tools/build_icons.py).
    /// item_&lt;item definition name&gt;.png and res_&lt;material&gt;.png. The icons are artwork of the game's developers.
    /// </summary>
    public static class Icons
    {
        private static readonly Dictionary<string, Image> Cache = new Dictionary<string, Image>();

        private static Image Load(string name)
        {
            Image img;
            if (Cache.TryGetValue(name, out img)) return img;
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icons/" + name + ".png"))
                    img = s == null ? null : new Bitmap(Image.FromStream(s));
            }
            catch (Exception) { img = null; }
            Cache[name] = img;
            return img;
        }

        /// <summary>Icon of an item (item definition name, e.g. "GasCan"), or null.</summary>
        public static Image Item(string id) { return string.IsNullOrEmpty(id) ? null : Load("item_" + id); }

        /// <summary>Icon of a material (white in the game files), tinted with the given colour.</summary>
        public static Image Material(string name, Color tint)
        {
            string key = "res_" + name + "#" + tint.ToArgb();
            Image img;
            if (Cache.TryGetValue(key, out img)) return img;
            var src = Load("res_" + name);
            if (src != null)
            {
                var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                var m = new ColorMatrix(new[]
                {
                    new[] { tint.R / 255f, 0f, 0f, 0f, 0f },
                    new[] { 0f, tint.G / 255f, 0f, 0f, 0f },
                    new[] { 0f, 0f, tint.B / 255f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 1f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 1f },
                });
                using (var g = Graphics.FromImage(bmp))
                using (var a = new ImageAttributes())
                {
                    a.SetColorMatrix(m);
                    g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, a);
                }
                img = bmp;
            }
            Cache[key] = img;
            return img;
        }
    }
}
