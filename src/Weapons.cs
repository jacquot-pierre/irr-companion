using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRRCompanion
{
    /// <summary>An attachment slot of a weapon or of a part (Handguard, Muzzle, Rail 1...).</summary>
    public sealed class WeaponSlot
    {
        public string Name;
        /// <summary>"part" or "ammo" (chamber of a weapon, rounds of a magazine).</summary>
        public string Kind;
        /// <summary>Item names (as used by additem) the slot accepts, from the game files.</summary>
        public readonly List<string> Items = new List<string>();
        public bool IsAmmo { get { return Kind == "ammo"; } }
    }

    /// <summary>A loaded magazine additem can give: the row name, the ammo in it and how many rounds.</summary>
    public sealed class MagLoad
    {
        public string Give, Ammo;
        public int Rounds;
    }

    /// <summary>A weapon, an attachment or an ammo type.</summary>
    public sealed class WeaponItem
    {
        /// <summary>Item definition name (ID_ without the prefix), as typed after additem.</summary>
        public string Id;
        public string Name;
        /// <summary>Weapon class (Rifle, SMG...) for weapons, part category (Barrel, Mag...) for attachments, Ammo.</summary>
        public string Cat;
        public int Capacity;
        /// <summary>Name additem takes (row of the game's vendor table, often not the item name), null if none.</summary>
        public string Give;
        /// <summary>Weapons: the parts it comes with (the default preset additem gives). Null if none.</summary>
        public BuildNode Defaults;
        /// <summary>Magazines: the loaded magazines additem can give (one per ammo type).</summary>
        public readonly List<MagLoad> Loads = new List<MagLoad>();
        public readonly List<WeaponSlot> Slots = new List<WeaponSlot>();
        public bool IsMag { get { return Cat == "Mag"; } }
        public WeaponSlot Slot(string name) { return Slots.FirstOrDefault(s => s.Name == name); }
        public override string ToString() { return Name; }
    }

    /// <summary>Weapons and attachments of the game (src/Resources/weapons.json, made by tools/build_weapons.py).</summary>
    public static class WeaponData
    {
        public static readonly Dictionary<string, WeaponItem> Items = new Dictionary<string, WeaponItem>(StringComparer.Ordinal);
        public static readonly List<WeaponItem> Weapons = new List<WeaponItem>();
        /// <summary>Weapon class id -> display name, in display order.</summary>
        public static readonly List<KeyValuePair<string, string>> Classes = new List<KeyValuePair<string, string>>();

        public static void Load()
        {
            if (Items.Count > 0) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("weapons.json"))
            using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var root = (JObj)Json.Parse(text);
            foreach (JObj c in root.Arr("classes")) Classes.Add(new KeyValuePair<string, string>(c.Str("id"), c.Str("name")));
            var items = root.Obj("items");
            foreach (var id in items.Keys)
            {
                var o = items.Obj(id);
                var it = new WeaponItem { Id = id, Name = o.Str("name", id), Cat = o.Str("cat", ""), Capacity = (int)o.Num("capacity"), Give = o.Str("give") };
                var defaults = o.Obj("defaults");
                if (defaults != null)
                {
                    var d = new JObj();
                    d["item"] = id;
                    d["parts"] = defaults;
                    it.Defaults = BuildNode.FromJson(d);
                }
                var loads = o.Arr("loads");
                if (loads != null)
                    foreach (JObj l in loads)
                        it.Loads.Add(new MagLoad { Give = l.Str("give"), Ammo = l.Str("ammo"), Rounds = (int)l.Num("rounds") });
                var slots = o.Arr("slots");
                if (slots != null)
                    foreach (JObj s in slots)
                    {
                        var slot = new WeaponSlot { Name = s.Str("name"), Kind = s.Str("kind", "part") };
                        foreach (var x in s.Arr("items")) slot.Items.Add((string)x);
                        it.Slots.Add(slot);
                    }
                Items[id] = it;
            }
            foreach (var w in root.Arr("weapons"))
            {
                WeaponItem it;
                if (Items.TryGetValue((string)w, out it)) Weapons.Add(it);
            }
        }

        public static WeaponItem Find(string id)
        {
            WeaponItem it;
            return id != null && Items.TryGetValue(id, out it) ? it : null;
        }

        public static string ClassName(string id)
        {
            foreach (var kv in Classes) if (kv.Key == id) return kv.Value;
            return id;
        }
    }

    /// <summary>An item put in a slot, with what is put in its own slots.</summary>
    public sealed class BuildNode
    {
        public string Item;
        /// <summary>Slot name -> part in it.</summary>
        public readonly Dictionary<string, BuildNode> Parts = new Dictionary<string, BuildNode>(StringComparer.Ordinal);

        public BuildNode(string item) { Item = item; }

        public JObj ToJson()
        {
            var o = new JObj();
            o["item"] = Item;
            if (Parts.Count > 0)
            {
                var p = new JObj();
                foreach (var kv in Parts) p[kv.Key] = kv.Value.ToJson();
                o["parts"] = p;
            }
            return o;
        }

        public static BuildNode FromJson(JObj o)
        {
            var n = new BuildNode(o.Str("item"));
            var p = o.Obj("parts");
            if (p != null)
                foreach (var k in p.Keys)
                {
                    var c = p.Obj(k);
                    if (c != null && c.Str("item") != null) n.Parts[k] = FromJson(c);
                }
            return n;
        }

        /// <summary>
        /// Drops what does not fit any more (unknown item, slot gone, part not accepted by the slot).
        /// Run after loading (the game data may have changed) and after a part change.
        /// </summary>
        public void Normalize()
        {
            var it = WeaponData.Find(Item);
            foreach (var k in Parts.Keys.ToList())
            {
                var slot = it == null ? null : it.Slot(k);
                if (slot == null || slot.IsAmmo || !slot.Items.Contains(Parts[k].Item) || WeaponData.Find(Parts[k].Item) == null)
                    Parts.Remove(k);
                else
                    Parts[k].Normalize();
            }
        }

        public BuildNode Clone() { return FromJson(ToJson()); }

        /// <summary>The part in the slot at this path (slot names from this node), or null.</summary>
        public BuildNode At(IList<string> path, int from = 0)
        {
            if (from >= path.Count) return this;
            BuildNode c;
            return Parts.TryGetValue(path[from], out c) ? c.At(path, from + 1) : null;
        }

        /// <summary>
        /// Puts in this node (the defaults of a new weapon) the parts of old (the previous build) that were not
        /// defaults of the previous weapon and still fit; same slot names, same parent parts.
        /// </summary>
        public void Overlay(BuildNode old, BuildNode oldDefaults)
        {
            var it = WeaponData.Find(Item);
            if (it == null || old == null) return;
            foreach (var kv in old.Parts)
            {
                var slot = it.Slot(kv.Key);
                if (slot == null || slot.IsAmmo || !slot.Items.Contains(kv.Value.Item)) continue;
                BuildNode def = null;
                if (oldDefaults != null) oldDefaults.Parts.TryGetValue(kv.Key, out def);
                BuildNode mine;
                Parts.TryGetValue(kv.Key, out mine);
                bool wasDefault = def != null && def.Item == kv.Value.Item;
                if (!wasDefault) Parts[kv.Key] = kv.Value.Clone();
                else if (mine != null && mine.Item == kv.Value.Item) mine.Overlay(kv.Value, def);
            }
            Normalize();
        }

        /// <summary>A node for another item that keeps the parts of this one that still fit (same slot name).</summary>
        public BuildNode Replace(string item)
        {
            var n = new BuildNode(item);
            foreach (var kv in Parts) n.Parts[kv.Key] = kv.Value;
            n.Normalize();
            return n;
        }
    }

    /// <summary>A saved weapon build (WEAPONS tab).</summary>
    public sealed class WeaponBuild
    {
        public string Name = "New build";
        public BuildNode Root;
        /// <summary>How many magazines to give (the one on the weapon included).</summary>
        public int Mags = 2;
        public string Ammo;
        public int AmmoCount = 0;
        /// <summary>JSON as last saved, null if never saved.</summary>
        public string Saved;

        public bool Dirty { get { return Saved != Json.Write(ToJson()); } }

        public WeaponItem Weapon { get { return WeaponData.Find(Root == null ? null : Root.Item); } }

        public JObj ToJson()
        {
            var o = new JObj();
            o["name"] = Name;
            o["weapon"] = Root == null ? null : Root.ToJson();
            o["mags"] = (double)Mags;
            o["ammo"] = Ammo;
            o["ammoCount"] = (double)AmmoCount;
            return o;
        }

        public static WeaponBuild FromJson(JObj o)
        {
            var b = new WeaponBuild
            {
                Name = o.Str("name", "Build"),
                Mags = Math.Max(1, (int)o.Num("mags", 2)),
                Ammo = o.Str("ammo"),
                AmmoCount = Math.Max(0, (int)o.Num("ammoCount", 0)),
            };
            var w = o.Obj("weapon");
            if (w != null && w.Str("item") != null) b.Root = BuildNode.FromJson(w);
            b.Normalize();
            return b;
        }

        /// <summary>A build of the weapon as additem gives it (its default parts).</summary>
        public static WeaponBuild New(WeaponItem weapon, string name)
        {
            var b = new WeaponBuild { Name = name, Root = weapon.Defaults != null ? weapon.Defaults.Clone() : new BuildNode(weapon.Id) };
            b.Ammo = null;
            b.Normalize();
            return b;
        }

        /// <summary>Changes the base weapon: its default parts, plus the parts picked before that still fit.</summary>
        public void ChangeWeapon(WeaponItem weapon)
        {
            var old = Root;
            var oldWeapon = Weapon;
            Root = weapon.Defaults != null ? weapon.Defaults.Clone() : new BuildNode(weapon.Id);
            if (old != null) Root.Overlay(old, oldWeapon == null ? null : oldWeapon.Defaults);
            Normalize();
        }

        /// <summary>Back to the parts the weapon comes with.</summary>
        public void ResetParts()
        {
            var w = Weapon;
            if (w == null) return;
            Root = w.Defaults != null ? w.Defaults.Clone() : new BuildNode(w.Id);
            Normalize();
        }

        /// <summary>True if this part (at this slot path) comes with the weapon: it is in the weapon's default preset.</summary>
        public bool IsDefault(IList<string> path, BuildNode node)
        {
            var w = Weapon;
            if (w == null || w.Defaults == null || node == null) return false;
            var d = w.Defaults.At(path);
            if (d == null || d.Item != node.Item) return false;
            // the parent parts must be the default ones too
            for (int i = 1; i < path.Count; i++)
            {
                var sub = path.Take(i).ToList();
                var a = w.Defaults.At(sub);
                var b2 = Root.At(sub);
                if (a == null || b2 == null || a.Item != b2.Item) return false;
            }
            return true;
        }

        public WeaponBuild Copy(string name)
        {
            var b = FromJson(ToJson());
            b.Name = name;
            b.Saved = null;
            return b;
        }

        public void Normalize()
        {
            if (Root == null) return;
            Root.Normalize();
            var w = Weapon;
            var ammo = w == null ? null : w.Slots.FirstOrDefault(s => s.IsAmmo);
            if (ammo == null || !ammo.Items.Contains(Ammo))
            {
                // the ammo of the magazine on the weapon, else the first one the weapon takes
                var mag = Mag;
                var magItem = mag == null ? null : WeaponData.Find(mag.Item);
                string magAmmo = magItem == null ? null : magItem.Loads.Select(l => l.Ammo).FirstOrDefault(a => a != null);
                Ammo = ammo == null ? null : ammo.Items.Contains(magAmmo) ? magAmmo : ammo.Items.Count > 0 ? ammo.Items[0] : null;
            }
        }

        public void MarkSaved() { Saved = Json.Write(ToJson()); }

        /// <summary>The magazine put on the weapon (null if none).</summary>
        public BuildNode Mag
        {
            get
            {
                if (Root == null) return null;
                foreach (var kv in Walk(Root, new List<string>()))
                {
                    var it = WeaponData.Find(kv.Value.Item);
                    if (it != null && it.IsMag) return kv.Value;
                }
                return null;
            }
        }

        /// <summary>Every part with its slot path, in slot order.</summary>
        public IEnumerable<KeyValuePair<List<string>, BuildNode>> Parts()
        {
            return Root == null ? Enumerable.Empty<KeyValuePair<List<string>, BuildNode>>() : Walk(Root, new List<string>());
        }

        private static IEnumerable<KeyValuePair<List<string>, BuildNode>> Walk(BuildNode n, List<string> path)
        {
            var it = WeaponData.Find(n.Item);
            if (it == null) yield break;
            foreach (var s in it.Slots)
            {
                BuildNode c;
                if (!n.Parts.TryGetValue(s.Name, out c)) continue;
                var p = new List<string>(path) { s.Name };
                yield return new KeyValuePair<List<string>, BuildNode>(p, c);
                foreach (var x in Walk(c, p)) yield return x;
            }
        }

        /// <summary>The loaded magazine additem gives for this magazine: the one with the build's ammo if any.</summary>
        public MagLoad LoadOf(WeaponItem mag)
        {
            if (mag == null) return null;
            return mag.Loads.FirstOrDefault(l => l.Ammo == Ammo) ?? mag.Loads.FirstOrDefault(l => l.Give == mag.Give) ?? mag.Loads.FirstOrDefault();
        }

        /// <summary>
        /// additem name -> count, in the order of the build: the weapon (it comes with its default parts and a
        /// loaded magazine), the parts that are not defaults, the extra magazines, the loose rounds.
        /// Items additem cannot give are left out and listed in Missing.
        /// </summary>
        public List<KeyValuePair<string, int>> Lines()
        {
            Missing.Clear();
            var order = new List<string>();
            var count = new Dictionary<string, int>();
            Action<WeaponItem, string, int> add = (it, give, n) =>
            {
                if (n <= 0 || it == null) return;
                if (give == null) { if (!Missing.Contains(it.Name)) Missing.Add(it.Name); return; }
                if (!count.ContainsKey(give)) { order.Add(give); count[give] = 0; }
                count[give] += n;
            };
            var w = Weapon;
            if (w == null) return new List<KeyValuePair<string, int>>();
            add(w, w.Give, 1);
            var mag = Mag;
            foreach (var kv in Parts())
            {
                var it = WeaponData.Find(kv.Value.Item);
                bool def = IsDefault(kv.Key, kv.Value);
                if (kv.Value == mag)
                {
                    var load = LoadOf(it);
                    add(it, load != null ? load.Give : it.Give, Math.Max(0, Mags - (def ? 1 : 0)));
                }
                else if (!def) add(it, it.Give, 1);
            }
            var ammo = WeaponData.Find(Ammo);
            add(ammo, ammo == null ? null : ammo.Give, AmmoCount);
            return order.Select(id => new KeyValuePair<string, int>(id, count[id])).ToList();
        }

        /// <summary>Names of the items of the last Lines() call that additem cannot give.</summary>
        public readonly List<string> Missing = new List<string>();

        /// <summary>The console line that gives the whole build: additem X 1 | additem Y 1 | ...</summary>
        public string Command()
        {
            return string.Join(" | ", Lines().Select(l => "additem " + l.Key + " " + l.Value));
        }
    }

    /// <summary>The user's weapon builds (weapons.json in the data folder).</summary>
    public static class WeaponBuilds
    {
        public static string FilePath { get { return Path.Combine(Library.Folder, "weapons.json"); } }

        public static List<WeaponBuild> Load()
        {
            var list = new List<WeaponBuild>();
            if (!File.Exists(FilePath)) return list;
            var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JObj;
            var arr = root == null ? null : root.Arr("builds");
            if (arr == null) throw new InvalidDataException("not a weapon builds file");
            foreach (var o in arr.OfType<JObj>())
            {
                var b = WeaponBuild.FromJson(o);
                if (b.Root == null) continue;
                b.MarkSaved();
                list.Add(b);
            }
            return list;
        }

        /// <summary>Writes the saved state of every build (unsaved changes of other builds are not written).</summary>
        public static void Save(IEnumerable<WeaponBuild> builds)
        {
            Directory.CreateDirectory(Library.Folder);
            var root = new JObj();
            root["format"] = "IRRCompanion.WeaponBuilds";
            root["version"] = 1.0;
            root["builds"] = builds.Where(b => b.Saved != null).Select(b => Json.Parse(b.Saved)).ToList();
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
}
