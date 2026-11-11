using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace IRRCompanion
{
    /// <summary>An item a trader can have, with its chance (0..1) and how many.</summary>
    public sealed class TraderItem
    {
        public string Id, Name, Tier;
        public double Chance, Weight;
        /// <summary>Market price of one unit (cash), 0 if unknown.</summary>
        public double Price;
        public int Count = 1;
    }

    /// <summary>Random loot of a trader: one category (an item, or a family such as Sight) and its items.</summary>
    public sealed class TraderCategory
    {
        public string Label, Tag, Item;
        /// <summary>Share of the random draws that go to this category (0..1).</summary>
        public double Share;
        public readonly List<TraderItem> Items = new List<TraderItem>();
    }

    public sealed class TraderGroup
    {
        public int Min, Max;
        public double Chance;
        public readonly List<TraderItem> Items = new List<TraderItem>();
        public readonly List<TraderCategory> Categories = new List<TraderCategory>();
        /// <summary>Random loot: weight of each rarity tier ("1".."4").</summary>
        public readonly List<KeyValuePair<string, double>> Tiers = new List<KeyValuePair<string, double>>();
    }

    /// <summary>A trader met in a raid: name and map. Several can share a stock (Cpt Hale and Voss: UICS).</summary>
    public sealed class TraderPerson
    {
        public string Name, Map;
        /// <summary>The map is our guess (not read from the map files).</summary>
        public bool Guessed;
        public string MapText { get { return Map == null ? "map unknown" : Map + (Guessed ? " (probably)" : ""); } }
    }

    public sealed class Trader
    {
        public string Id, Name;
        public readonly List<TraderPerson> People = new List<TraderPerson>();
        /// <summary>"Lt Moroz" / "Cpt Hale & Voss".</summary>
        public string PeopleNames { get { return string.Join(" & ", People.ConvertAll(p => p.Name)); } }
        public readonly List<TraderItem> Fixed = new List<TraderItem>();
        public readonly List<TraderGroup> Choices = new List<TraderGroup>();
        public readonly List<TraderGroup> Random = new List<TraderGroup>();
        /// <summary>Chance of a weapon in stock (weapons are not listed), 0 if none.</summary>
        public double WeaponChance;
        public override string ToString() { return Name; }
    }

    /// <summary>Stock of the traders (src/Resources/traders.json, made by tools/build_traders.py).</summary>
    public static class TraderData
    {
        public static readonly List<Trader> Traders = new List<Trader>();

        public static void Load()
        {
            if (Traders.Count > 0) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("traders.json"))
            using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var root = (JObj)Json.Parse(text);
            var names = root.Obj("items");
            System.Func<JObj, TraderItem> item = o =>
            {
                string id = o.Str("id");
                var n = names.Obj(id);
                return new TraderItem
                {
                    Id = id, Name = n != null ? n.Str("name", id) : id, Chance = o.Num("chance", 1), Weight = o.Num("weight"),
                    Count = (int)o.Num("count", 1), Tier = o.Str("tier"), Price = n != null ? n.Num("price") : 0,
                };
            };
            foreach (JObj t in root.Arr("traders"))
            {
                var tr = new Trader { Id = t.Str("id"), Name = t.Str("name"), WeaponChance = t.Num("weapons") };
                var people = t.Arr("people");
                if (people != null)
                    foreach (JObj p in people)
                        tr.People.Add(new TraderPerson { Name = p.Str("name"), Map = p.Str("map"), Guessed = p["guessed"] is bool && (bool)p["guessed"] });
                foreach (JObj f in t.Arr("fixed")) tr.Fixed.Add(item(f));
                foreach (JObj g in t.Arr("choices"))
                {
                    var grp = new TraderGroup { Min = (int)g.Num("min"), Max = (int)g.Num("max"), Chance = g.Num("chance", 1) };
                    foreach (JObj i in g.Arr("items")) grp.Items.Add(item(i));
                    tr.Choices.Add(grp);
                }
                foreach (JObj g in t.Arr("random"))
                {
                    var grp = new TraderGroup { Min = (int)g.Num("min"), Max = (int)g.Num("max"), Chance = g.Num("chance", 1) };
                    foreach (var x in g.Arr("tiers"))
                    {
                        var pair = (List<object>)x;
                        grp.Tiers.Add(new KeyValuePair<string, double>((string)pair[0], (double)pair[1]));
                    }
                    foreach (JObj c in g.Arr("categories"))
                    {
                        var cat = new TraderCategory { Label = c.Str("label"), Tag = c.Str("tag"), Item = c.Str("item"), Share = c.Num("share") };
                        foreach (JObj i in c.Arr("items")) cat.Items.Add(item(i));
                        grp.Categories.Add(cat);
                    }
                    tr.Random.Add(grp);
                }
                Traders.Add(tr);
            }
        }
    }
}
