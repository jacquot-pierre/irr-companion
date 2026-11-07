using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace IRRCompanion
{
    public sealed class Preset
    {
        public string Name = "New preset";
        public string Author = "";
        public string Description = "";
        public string SavedAt = "";
        public int Version = 1;
        public bool BuiltIn;

        /// <summary>Full AISettings document (all keys, game order).</summary>
        public JObj Ai;
        /// <summary>Simulation document (optimizationRanges + botMovementTicksPerSec).</summary>
        public JObj Sim;

        /// <summary>Selections for categories this tool does not edit (weapons, barks, global, menuStyle).</summary>
        public JObj OtherSelections = DefaultOtherSelections();
        /// <summary>Variants of other categories carried by an imported code, kept as-is.</summary>
        public List<object> OtherVars = new List<object>();

        public bool Dirty;
        private string _snapshot;

        public static JObj DefaultOtherSelections()
        {
            var o = new JObj();
            o["weapons"] = "Normal";
            o["barks"] = "Normal";
            o["global"] = "Normal";
            o["menuStyle"] = "Normal";
            return o;
        }

        public string Id { get { return MakeId(Name); } }

        public static string MakeId(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            var id = sb.ToString().Trim('_');
            return id.Length == 0 ? "preset" : id;
        }

        /// <summary>Built-in simulation level matching the values, or null when custom.</summary>
        public string SimLevel { get { return GameData.MatchSimLevel(Sim); } }

        public object Get(Param p) { return Json.GetPath(p.IsSimulation ? Sim : Ai, p.DocKey); }

        public void Set(Param p, object v)
        {
            Json.SetPath(p.IsSimulation ? Sim : Ai, p.DocKey, v);
            Dirty = true;
        }

        public static Preset FromGame(string aiName, string simName)
        {
            string label = GameData.AiLabels.ContainsKey(aiName) ? GameData.AiLabels[aiName] : aiName;
            return new Preset
            {
                Name = label,
                Author = "Game",
                Description = "In-game difficulty \"" + label + "\" (file " + aiName + ".json), with simulation \""
                              + simName + "\". Shipped with the game build of " + GameData.GameBuild + ".",
                BuiltIn = true,
                Ai = Json.CloneObj(GameData.Ai[aiName]),
                Sim = Json.CloneObj(GameData.Sim[simName]),
            };
        }

        public Preset CloneAs(string newName)
        {
            var p = FromShareObject(ToShareObject(), false);
            p.Name = newName;
            p.BuiltIn = false;
            if (Author == "Game") p.Author = "";
            p.Dirty = true;
            return p;
        }

        // ---------- share code document ----------

        public JObj ToShareObject()
        {
            string id = Id;
            string simLevel = SimLevel;

            var sel = new JObj();
            sel["aISettings"] = id;
            sel["weapons"] = OtherSelections.Str("weapons", "Normal");
            sel["barks"] = OtherSelections.Str("barks", "Normal");
            sel["simulation"] = simLevel ?? id;
            sel["global"] = OtherSelections.Str("global", "Normal");
            sel["menuStyle"] = OtherSelections.Str("menuStyle", "Normal");
            foreach (var k in OtherSelections.Keys)
                if (!sel.Has(k)) sel[k] = OtherSelections[k];

            var preset = new JObj();
            preset["presetId"] = id;
            preset["displayName"] = Name;
            preset["description"] = Description ?? "";
            preset["author"] = Author ?? "";
            preset["version"] = (double)Version;
            preset["savedAt"] = string.IsNullOrEmpty(SavedAt) ? Now() : SavedAt;
            preset["selections"] = sel;

            var vars = new List<object>();
            var aiDoc = Json.CloneObj(Ai);
            aiDoc["schemaSoftVersion"] = (double)GameData.AiSoftVersion;
            aiDoc["schemaHardVersion"] = (double)GameData.AiHardVersion;
            var ai = new JObj();
            ai["c"] = "AISettings";
            ai["n"] = id;
            ai["hv"] = (double)GameData.AiHardVersion;
            ai["doc"] = aiDoc;
            vars.Add(ai);

            if (simLevel == null)
            {
                var simDoc = Json.CloneObj(Sim);
                simDoc["schemaSoftVersion"] = (double)GameData.SimSoftVersion;
                simDoc["schemaHardVersion"] = (double)GameData.SimHardVersion;
                var sim = new JObj();
                sim["c"] = "Simulation";
                sim["n"] = id;
                sim["hv"] = (double)GameData.SimHardVersion;
                sim["doc"] = simDoc;
                vars.Add(sim);
            }
            foreach (var v in OtherVars) vars.Add(Json.Clone(v));

            var root = new JObj();
            root["t"] = "p";
            root["id"] = id;
            root["preset"] = preset;
            root["vars"] = vars;
            return root;
        }

        public string ToShareCode() { return ShareCode.Encode(ToShareObject()); }

        public static string Now()
        {
            return DateTime.UtcNow.ToString("yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture);
        }

        public static Preset FromShareObject(JObj root, bool markSnapshot = true)
        {
            if (root.Str("t") != "p") throw new FormatException("This code is not a preset code.");
            var meta = root.Obj("preset") ?? new JObj();
            var sel = meta.Obj("selections") ?? new JObj();
            var p = new Preset
            {
                Name = meta.Str("displayName") ?? meta.Str("presetId") ?? root.Str("id") ?? "Imported preset",
                Author = meta.Str("author", ""),
                Description = meta.Str("description", ""),
                SavedAt = meta.Str("savedAt", ""),
                Version = (int)meta.Num("version", 1),
            };

            JObj aiDoc = null, simDoc = null;
            var vars = root.Arr("vars") ?? new List<object>();
            foreach (var v in vars)
            {
                var o = v as JObj;
                if (o == null) continue;
                string c = o.Str("c");
                if (c == "AISettings" && aiDoc == null) aiDoc = o.Obj("doc");
                else if (c == "Simulation" && simDoc == null) simDoc = o.Obj("doc");
                else p.OtherVars.Add(Json.Clone(o));
            }

            // AISettings: start from the full Normal document so every key exists, then apply the code.
            string aiSel = sel.Str("aISettings", "Normal");
            JObj baseAi = GameData.Ai.ContainsKey(aiSel) ? GameData.Ai[aiSel] : GameData.Ai["Normal"];
            p.Ai = Json.CloneObj(baseAi);
            if (aiDoc != null) Overlay(p.Ai, aiDoc);
            p.Ai.Remove("schemaSoftVersion");
            p.Ai.Remove("schemaHardVersion");

            string simSel = sel.Str("simulation", "Normal");
            JObj baseSim = GameData.Sim.ContainsKey(simSel) ? GameData.Sim[simSel] : GameData.Sim["Normal"];
            p.Sim = Json.CloneObj(baseSim);
            if (simDoc != null) Overlay(p.Sim, simDoc);
            p.Sim.Remove("schemaSoftVersion");
            p.Sim.Remove("schemaHardVersion");

            p.OtherSelections = new JObj();
            foreach (var k in sel.Keys)
                if (k != "aISettings" && k != "simulation") p.OtherSelections[k] = sel[k];
            foreach (var k in DefaultOtherSelections().Keys)
                if (!p.OtherSelections.Has(k)) p.OtherSelections[k] = "Normal";

            if (markSnapshot) p.MarkSaved();
            return p;
        }

        private static void Overlay(JObj target, JObj src)
        {
            foreach (var k in src.Keys)
            {
                var so = src[k] as JObj;
                var to = target[k] as JObj;
                if (so != null && to != null) Overlay(to, so);
                else target[k] = Json.Clone(src[k]);
            }
        }

        /// <summary>Last saved state (null if never saved).</summary>
        public string SavedJson { get { return _snapshot; } }

        public void MarkSaved()
        {
            _snapshot = Json.Write(ToShareObject());
            Dirty = false;
        }

        public void Revert()
        {
            if (_snapshot == null) return;
            var p = FromShareObject((JObj)Json.Parse(_snapshot), false);
            Name = p.Name; Author = p.Author; Description = p.Description; SavedAt = p.SavedAt; Version = p.Version;
            Ai = p.Ai; Sim = p.Sim; OtherSelections = p.OtherSelections; OtherVars = p.OtherVars;
            Dirty = false;
        }

        public override string ToString() { return (Dirty ? "● " : "") + Name; }
    }

    /// <summary>The user's own presets, stored in %APPDATA%\IRR Companion\presets.json.</summary>
    public static class Library
    {
        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IRR Companion"); }
        }

        public static string FilePath { get { return Path.Combine(Folder, "presets.json"); } }

        /// <summary>Folder used before the tool was renamed (IRR Preset Tool).</summary>
        private static string OldFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IRR Preset Tool"); }
        }

        /// <summary>First start after the rename: copies presets and favorites from the old folder.</summary>
        public static void MigrateOldFolder()
        {
            try
            {
                if (Directory.Exists(Folder) || !Directory.Exists(OldFolder)) return;
                Directory.CreateDirectory(Folder);
                foreach (var f in Directory.GetFiles(OldFolder, "*.json"))
                    File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)), false);
            }
            catch (Exception) { /* the old files stay where they are */ }
        }

        public static List<Preset> Load()
        {
            var list = new List<Preset>();
            if (!File.Exists(FilePath)) return list;
            var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JObj;
            if (root == null) return list;
            foreach (var x in root.Arr("presets") ?? new List<object>())
            {
                var o = x as JObj;
                if (o == null) continue;
                try { list.Add(Preset.FromShareObject(o)); }
                catch (Exception) { /* skip unreadable entry */ }
            }
            return list;
        }

        public static void Save(IEnumerable<Preset> presets)
        {
            Directory.CreateDirectory(Folder);
            var root = new JObj();
            root["format"] = "IRRCompanion";
            root["version"] = 1.0;
            var arr = new List<object>();
            // Only the last saved state of each preset is written; unsaved edits stay in memory.
            foreach (var p in presets.Where(x => !x.BuiltIn && x.SavedJson != null)) arr.Add(Json.Parse(p.SavedJson));
            root["presets"] = arr;
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
