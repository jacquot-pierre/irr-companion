using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;

namespace IRRCompanion
{
    /// <summary>
    /// Game icons embedded in the exe (src/Resources/icons, made by tools/build_icons.py).
    /// item_&lt;item definition name&gt;.png, res_&lt;material&gt;.png and cat_&lt;category&gt;.png (generic silhouettes). The icons are artwork of the game's developers.
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
        public static Image Material(string name, Color tint) { return Tinted("res_" + name, tint); }

        /// <summary>Colour of the generic silhouettes (dim, so they do not pass for the real icon of an item).</summary>
        public static readonly Color SilhouetteTint = Color.FromArgb(150, 128, 122, 110);

        /// <summary>
        /// Generic silhouette of a weapon class (Rifle, SMG...) or a part category (Barrel, Mag, Sight...), from the
        /// game's inventory slots: shown for items without an icon of their own and for empty slots. Null if none.
        /// </summary>
        public static Image Category(string cat) { return string.IsNullOrEmpty(cat) ? null : Tinted("cat_" + cat, SilhouetteTint); }

        /// <summary>Icon of an item, or the silhouette of its category when the game has no icon for it.</summary>
        public static Image ItemOrCategory(string id, string cat) { return Item(id) ?? Category(cat); }

        private static Image Tinted(string resource, Color tint)
        {
            string key = resource + "#" + tint.ToArgb();
            Image img;
            if (Cache.TryGetValue(key, out img)) return img;
            var src = Load(resource);
            if (src != null)
            {
                var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                var m = new ColorMatrix(new[]
                {
                    new[] { tint.R / 255f, 0f, 0f, 0f, 0f },
                    new[] { 0f, tint.G / 255f, 0f, 0f, 0f },
                    new[] { 0f, 0f, tint.B / 255f, 0f, 0f },
                    new[] { 0f, 0f, 0f, tint.A / 255f, 0f },
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
