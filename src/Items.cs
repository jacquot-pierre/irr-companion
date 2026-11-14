using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRRCompanion
{
    /// <summary>A raid trader that can have the item in stock, with the chance.</summary>
    public sealed class ItemTrader
    {
        public string Trader, Who;
        public double Chance;
    }

    public sealed class GameItem
    {
        public string Id, Name, Category, Sub;
        public bool Icon;
        /// <summary>Market price of one unit (cash), 0 if none.</summary>
        public double Price;
        /// <summary>The market sells it (at Price).</summary>
        public bool MarketBuy;
        /// <summary>Black market price (Marked Coins), 0 if not sold there.</summary>
        public double Coins;
        /// <summary>What the market pays for it (cash), 0 if no price.</summary>
        public double Sell;
        /// <summary>The vendor table says it cannot be sold at the market: the sell price is uncertain.</summary>
        public bool NoSell;
        /// <summary>What a raid trader pays for it (quest crates), 0 if none.</summary>
        public int HandIn;
        public int Tier;
        /// <summary>Faction loyalty needed to buy it at the market ("UICS", 25), or null.</summary>
        public string LoyaltyFaction;
        public int LoyaltyLevel;
        public readonly List<ItemTrader> Traders = new List<ItemTrader>();
        /// <summary>Price at a raid trader: the market price (no trader multiplier in the game files), 0 if no trader has it.</summary>
        public double TraderPrice { get { return Traders.Count > 0 ? Price : 0; } }
        public override string ToString() { return Name; }
    }

    public sealed class ItemCategory
    {
        public string Id, Name;
        public int Count;
        public readonly List<string> Subs = new List<string>();
        public override string ToString() { return Name; }
    }

    /// <summary>Every item of the game with its prices (src/Resources/items.json, made by tools/build_items.py).</summary>
    public static class ItemData
    {
        public static readonly List<GameItem> Items = new List<GameItem>();
        public static readonly List<ItemCategory> Categories = new List<ItemCategory>();
        public static readonly Dictionary<string, double> SellMultipliers = new Dictionary<string, double>();
        public static double SellDefault;

        public static double SellMultiplier(string category)
        {
            double m;
            return SellMultipliers.TryGetValue(category, out m) ? m : SellDefault;
        }

        public static void Load()
        {
            if (Items.Count > 0) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("items.json"))
            using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var root = (JObj)Json.Parse(text);
            SellDefault = root.Num("sellDefault", 0.6);
            var mults = root.Obj("sellMultipliers");
            foreach (var k in mults.Keys) SellMultipliers[k] = mults.Num(k);
            foreach (JObj c in root.Arr("categories"))
            {
                var cat = new ItemCategory { Id = c.Str("id"), Name = c.Str("name"), Count = (int)c.Num("count") };
                foreach (var x in c.Arr("subs")) cat.Subs.Add((string)x);
                Categories.Add(cat);
            }
            foreach (JObj o in root.Arr("items"))
            {
                var it = new GameItem
                {
                    Id = o.Str("id"), Name = o.Str("name"), Category = o.Str("cat"), Sub = o.Str("sub"), Icon = o["icon"] is bool,
                    Price = o.Num("price"), MarketBuy = o["buy"] is bool, Coins = o.Num("coins"), Sell = o.Num("sell"),
                    NoSell = o["noSell"] is bool, HandIn = (int)o.Num("handIn"), Tier = (int)o.Num("tier"),
                };
                var loy = o.Obj("loyalty");
                if (loy != null) { it.LoyaltyFaction = loy.Str("faction"); it.LoyaltyLevel = (int)loy.Num("level"); }
                var tr = o.Arr("traders");
                if (tr != null)
                    foreach (JObj t in tr) it.Traders.Add(new ItemTrader { Trader = t.Str("trader"), Who = t.Str("who"), Chance = t.Num("chance") });
                Items.Add(it);
            }
        }

        /// <summary>"2 100", "0.8".</summary>
        public static string Money(double v)
        {
            if (v <= 0) return "";
            if (v >= 10 || v == System.Math.Floor(v))
                return System.Math.Round(v, System.MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");  // like the web (Math.round)
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
