using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRRCompanion
{
    /// <summary>A kind of place where loot spawns (a container preset or loose loot) and the chance of each item.</summary>
    public sealed class LootSource
    {
        public string Id, Label, Kind;
        public readonly Dictionary<string, double> Chances = new Dictionary<string, double>();
        public double ChanceOf(string item) { double p; return Chances.TryGetValue(item, out p) ? p : 0; }
    }

    /// <summary>A loot container or loose loot point of a map.</summary>
    public struct LootPlace
    {
        public double X, Y;
        public int Floor, Source;
    }

    public sealed class LootItem
    {
        public string Id, Name;
        public override string ToString() { return Name; }
    }

    /// <summary>Where items spawn (src/Resources/loot.json, made by tools/build_loot.py).</summary>
    public static class LootData
    {
        public static readonly List<LootSource> Sources = new List<LootSource>();
        public static readonly List<LootItem> Items = new List<LootItem>();
        public static readonly Dictionary<string, List<LootPlace>> Places = new Dictionary<string, List<LootPlace>>();

        public static void Load()
        {
            if (Sources.Count > 0) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("loot.json"))
            {
                if (s == null) return;
                using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            }
            var root = (JObj)Json.Parse(text);
            foreach (JObj o in root.Arr("sources"))
            {
                var src = new LootSource { Id = o.Str("id"), Label = o.Str("label"), Kind = o.Str("kind") };
                var ch = o.Obj("chances");
                foreach (var k in ch.Keys) src.Chances[k] = ch.Num(k);
                Sources.Add(src);
            }
            var items = root.Obj("items");
            foreach (var k in items.Keys) Items.Add(new LootItem { Id = k, Name = items.Obj(k).Str("name", k) });
            Items.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
            var maps = root.Obj("maps");
            foreach (var m in maps.Keys)
            {
                var list = new List<LootPlace>();
                foreach (var x in maps.Arr(m))
                {
                    var a = (List<object>)x;
                    list.Add(new LootPlace { X = (double)a[0], Y = (double)a[1], Floor = (int)(double)a[2], Source = (int)(double)a[3] });
                }
                Places[m] = list;
            }
        }

        public static List<LootPlace> PlacesOf(string map)
        {
            List<LootPlace> l;
            return Places.TryGetValue(map, out l) ? l : new List<LootPlace>();
        }

        /// <summary>Expected number of this item in a raid on the map (sum of the chances of every place).</summary>
        public static double Expected(string map, string item, int floor = -1)
        {
            return PlacesOf(map).Where(p => floor < 0 || p.Floor == floor).Sum(p => Sources[p.Source].ChanceOf(item));
        }
    }
}
