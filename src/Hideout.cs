using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRRCompanion
{
    public sealed class HideoutCondition
    {
        public string Type, Unit, Faction;   // Type: unit | faction | player
        public int Level;

        public string Text
        {
            get
            {
                if (Type == "unit") return Unit + " level " + Level;
                if (Type == "faction") return Faction + " loyalty level " + Level;
                if (Type == "player") return "Player level " + Level;
                return Type;
            }
        }
    }

    public sealed class HideoutItem
    {
        public string Name, Id, Tag;
        public int Count;

        /// <summary>Name to use in irr.Item.Give (the item definition name, else the gameplay tag).</summary>
        public string GiveName { get { return Id ?? Tag; } }
    }

    public sealed class HideoutLevel
    {
        public int Level, Cash;
        public string Description;
        public List<HideoutCondition> Conditions = new List<HideoutCondition>();
        public List<HideoutItem> Items = new List<HideoutItem>();
        /// <summary>Resource name (Concrete, Food, Metal, Oil) -> amount.</summary>
        public Dictionary<string, int> Resources = new Dictionary<string, int>();

        public bool IsFree { get { return Cash == 0 && Items.Count == 0 && Resources.Count == 0 && Conditions.Count == 0; } }
    }

    public sealed class HideoutUnit
    {
        public string Name, Tag;
        public int StartLevel;
        /// <summary>Free first levels of the game files (starting state), not shown: game level = shown level + FreeLevels.</summary>
        public int FreeLevels;
        public List<HideoutLevel> Levels = new List<HideoutLevel>();

        public int MaxLevel { get { return Levels.Count == 0 ? 0 : Levels.Max(l => l.Level); } }
        public HideoutLevel Get(int level) { return Levels.FirstOrDefault(l => l.Level == level); }
        public override string ToString() { return Name; }
    }

    public static class HideoutData
    {
        public static readonly List<HideoutUnit> Units = new List<HideoutUnit>();
        public static readonly List<string> Resources = new List<string>();
        /// <summary>Faction abbreviations as used in game (IGC, UICS, VLF, FANG).</summary>
        public static readonly List<string> Factions = new List<string>();
        public static readonly Dictionary<string, string> FactionNames = new Dictionary<string, string>();

        public static void Load()
        {
            if (Units.Count > 0) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("hideout.json"))
            using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var root = (JObj)Json.Parse(text);
            foreach (var x in root.Arr("resources")) Resources.Add((string)x);
            foreach (JObj f in root.Arr("factions"))
            {
                Factions.Add(f.Str("abbr"));
                FactionNames[f.Str("abbr")] = f.Str("name");
            }
            foreach (JObj u in root.Arr("units"))
            {
                var unit = new HideoutUnit { Name = u.Str("name"), Tag = u.Str("tag"), StartLevel = (int)u.Num("startLevel") };
                foreach (JObj l in u.Arr("levels"))
                {
                    var lv = new HideoutLevel { Level = (int)l.Num("level"), Cash = (int)l.Num("cash"), Description = l.Str("description", "") };
                    foreach (JObj c in l.Arr("conditions"))
                        lv.Conditions.Add(new HideoutCondition { Type = c.Str("type"), Unit = c.Str("unit"), Faction = c.Str("faction"), Level = (int)c.Num("level") });
                    foreach (JObj i in l.Arr("items"))
                        lv.Items.Add(new HideoutItem { Name = i.Str("name"), Id = i.Str("id"), Tag = i.Str("tag"), Count = (int)i.Num("count") });
                    var res = l.Obj("resources");
                    if (res != null) foreach (var k in res.Keys) lv.Resources[k] = (int)res.Num(k);
                    unit.Levels.Add(lv);
                }
                Units.Add(unit);
            }
            // A free level 1 (nothing to pay, no requirement) is the starting state: hide it and count the levels
            // from the first real upgrade (game level 2 is shown as level 1).
            foreach (var u in Units)
            {
                while (u.Levels.Any(l => l.Level == u.FreeLevels + 1 && l.IsFree)) u.FreeLevels++;
                if (u.FreeLevels == 0) continue;
                u.Levels.RemoveAll(l => l.Level <= u.FreeLevels);
                foreach (var l in u.Levels) l.Level -= u.FreeLevels;
                u.StartLevel = Math.Max(0, u.StartLevel - u.FreeLevels);
            }
            foreach (var u in Units)
                foreach (var l in u.Levels)
                {
                    foreach (var c in l.Conditions.Where(c => c.Type == "unit"))
                    {
                        var target = Find(c.Unit);
                        if (target != null) c.Level -= target.FreeLevels;
                    }
                    l.Conditions.RemoveAll(c => c.Type == "unit" && c.Level <= 0);  // met from the start
                }
        }

        public static HideoutUnit Find(string name)
        {
            return Units.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // ----- console commands for the GIVE buttons -----
        public static string GiveItem(HideoutItem item, int count)
        {
            return "irr.Item.Give " + CommandData.Token(item.GiveName) + " " + count.ToString(CultureInfo.InvariantCulture);
        }

        public static string GiveCash(int amount)
        {
            return "irr.Item.Give Cash " + amount.ToString(CultureInfo.InvariantCulture);
        }

        public static string GiveResource(string resource, int amount)
        {
            return "irr.Item.AddResource " + resource + " " + amount.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The player's own hideout progress (levels, player level, loyalty), stored in the app.</summary>
    public sealed class HideoutProgress
    {
        public int PlayerLevel = 1;
        public readonly Dictionary<string, int> Loyalty = new Dictionary<string, int>();
        public readonly Dictionary<string, int> UnitLevels = new Dictionary<string, int>();

        public static string FilePath { get { return Path.Combine(Library.Folder, "hideout.json"); } }

        public int LevelOf(string unit)
        {
            int v;
            if (UnitLevels.TryGetValue(unit, out v)) return v;
            var u = HideoutData.Find(unit);
            return u == null ? 0 : u.StartLevel;
        }

        public int LoyaltyOf(string faction)
        {
            int v;
            return Loyalty.TryGetValue(faction, out v) ? v : 0;
        }

        public bool IsMet(HideoutCondition c)
        {
            if (c.Type == "unit") return LevelOf(c.Unit) >= c.Level;
            if (c.Type == "faction") return LoyaltyOf(c.Faction) >= c.Level;
            if (c.Type == "player") return PlayerLevel >= c.Level;
            return false;
        }

        /// <summary>Next level to build, or null when the unit is at its maximum level.</summary>
        public HideoutLevel NextLevel(HideoutUnit u) { return u.Get(LevelOf(u.Name) + 1); }

        public static HideoutProgress Load()
        {
            var p = new HideoutProgress();
            if (!File.Exists(FilePath)) return p;
            var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JObj;
            if (root == null) return p;
            p.PlayerLevel = Math.Max(1, (int)root.Num("playerLevel", 1));
            var loy = root.Obj("loyalty");
            if (loy != null) foreach (var k in loy.Keys) p.Loyalty[k] = (int)loy.Num(k);
            var units = root.Obj("units");
            // version 1 stored the game levels, version 2 the shown levels (free first levels left out)
            bool gameLevels = root.Num("version", 1) < 2;
            if (units != null)
                foreach (var k in units.Keys)
                {
                    var u = HideoutData.Find(k);
                    int v = (int)units.Num(k);
                    p.UnitLevels[k] = gameLevels && u != null ? Math.Max(0, v - u.FreeLevels) : v;
                }
            return p;
        }

        public void Save()
        {
            Directory.CreateDirectory(Library.Folder);
            var root = new JObj();
            root["format"] = "IRRCompanion.Hideout";
            root["version"] = 2.0;
            root["playerLevel"] = (double)PlayerLevel;
            var loy = new JObj();
            foreach (var kv in Loyalty) loy[kv.Key] = (double)kv.Value;
            root["loyalty"] = loy;
            var units = new JObj();
            foreach (var kv in UnitLevels) units[kv.Key] = (double)kv.Value;
            root["units"] = units;
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Json.Write(root, true), new UTF8Encoding(false));
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, FilePath + ".bak", true);
                File.Delete(FilePath);
            }
            File.Move(tmp, FilePath);
        }
    }

    /// <summary>Totals of what is still needed (shopping list).</summary>
    public sealed class HideoutTotals
    {
        public sealed class Line
        {
            public HideoutItem Item;
            public int Count;
            public readonly List<string> For = new List<string>();
        }

        public readonly List<Line> Items = new List<Line>();
        public readonly Dictionary<string, int> Resources = new Dictionary<string, int>();
        public int Cash, Upgrades;

        /// <param name="nextOnly">only the next upgrade of each unit, else every remaining level</param>
        public static HideoutTotals Compute(HideoutProgress p, bool nextOnly)
        {
            var t = new HideoutTotals();
            foreach (var u in HideoutData.Units)
            {
                int cur = p.LevelOf(u.Name);
                foreach (var lv in u.Levels.Where(l => l.Level > cur).OrderBy(l => l.Level))
                {
                    if (nextOnly && lv.Level != cur + 1) break;
                    t.Upgrades++;
                    t.Cash += lv.Cash;
                    foreach (var kv in lv.Resources)
                    {
                        int v;
                        t.Resources.TryGetValue(kv.Key, out v);
                        t.Resources[kv.Key] = v + kv.Value;
                    }
                    foreach (var it in lv.Items)
                    {
                        var line = t.Items.FirstOrDefault(x => x.Item.Tag == it.Tag);
                        if (line == null) { line = new Line { Item = it }; t.Items.Add(line); }
                        line.Count += it.Count;
                        line.For.Add(u.Name + " " + lv.Level);
                    }
                }
            }
            t.Items.Sort((a, b) => string.Compare(a.Item.Name, b.Item.Name, StringComparison.OrdinalIgnoreCase));
            return t;
        }
    }
}
