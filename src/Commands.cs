using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRRCompanion
{
    public sealed class ArgDef
    {
        public string Name, Kind, Help, Unit, Default;
        public double? Min, Max, Step;
        public bool Optional, Editable;
        public string Off = "OFF", On = "ON";
        /// <summary>choice: (value, label) pairs.</summary>
        public List<KeyValuePair<string, string>> Options = new List<KeyValuePair<string, string>>();

        public bool IsBounded { get { return Min.HasValue && Max.HasValue; } }
    }

    public sealed class CommandDef
    {
        public string Cmd, Category, Title, Desc, Warn;
        public bool Host, CVar, Essential;
        public List<ArgDef> Args = new List<ArgDef>();

        public override string ToString() { return Cmd; }
    }

    public sealed class MapWeather
    {
        public string Map;
        public List<string> Weathers = new List<string>();
        public List<double> Weights = new List<double>();
    }

    public static class CommandData
    {
        public static readonly List<CommandDef> All = new List<CommandDef>();
        public static readonly List<string> Categories = new List<string>();
        public static readonly List<string> Essentials = new List<string>();
        public static readonly List<MapWeather> Maps = new List<MapWeather>();
        /// <summary>(internal name, category) of every item definition.</summary>
        public static readonly List<KeyValuePair<string, string>> Items = new List<KeyValuePair<string, string>>();
        public static int WeatherIndexBase = 1;

        public static void Load()
        {
            if (All.Count > 0) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("commands.json"))
            using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var root = (JObj)Json.Parse(text);
            WeatherIndexBase = (int)root.Num("weatherIndexBase", 1);
            foreach (var x in root.Arr("categories")) Categories.Add((string)x);
            foreach (var x in root.Arr("essentials")) Essentials.Add((string)x);
            foreach (JObj m in root.Arr("maps"))
            {
                var mw = new MapWeather { Map = m.Str("name") };
                foreach (JObj w in m.Arr("weathers"))
                {
                    mw.Weathers.Add(w.Str("name"));
                    mw.Weights.Add(w.Num("weight"));
                }
                Maps.Add(mw);
            }
            foreach (List<object> it in root.Arr("items"))
                Items.Add(new KeyValuePair<string, string>((string)it[0], (string)it[1]));
            foreach (JObj c in root.Arr("commands"))
            {
                var d = new CommandDef
                {
                    Cmd = c.Str("cmd"), Category = c.Str("cat"), Title = c.Str("title"), Desc = c.Str("desc"),
                    Warn = c.Str("warn"), Host = c["host"] is bool && (bool)c["host"], CVar = c["cvar"] is bool && (bool)c["cvar"],
                };
                d.Essential = Essentials.Contains(d.Cmd);
                foreach (JObj a in c.Arr("args"))
                {
                    var ad = new ArgDef
                    {
                        Name = a.Str("name"), Kind = a.Str("kind"), Help = a.Str("help"), Unit = a.Str("unit"),
                        Optional = a["optional"] is bool && (bool)a["optional"],
                        Editable = a["editable"] is bool && (bool)a["editable"],
                        Off = a.Str("off", "OFF"), On = a.Str("on", "ON"),
                    };
                    if (a["min"] is double) ad.Min = (double)a["min"];
                    if (a["max"] is double) ad.Max = (double)a["max"];
                    if (a["step"] is double) ad.Step = (double)a["step"];
                    var def = a["default"];
                    ad.Default = def is double ? Fmt((double)def) : def as string;
                    var opts = a.Arr("options");
                    if (opts != null)
                        foreach (List<object> o in opts) ad.Options.Add(new KeyValuePair<string, string>((string)o[0], (string)o[1]));
                    d.Args.Add(ad);
                }
                All.Add(d);
            }
        }

        public static CommandDef Find(string cmd)
        {
            return All.FirstOrDefault(x => string.Equals(x.Cmd, cmd, StringComparison.OrdinalIgnoreCase));
        }

        public static string Fmt(double d)
        {
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Quotes a console argument when it contains spaces.</summary>
        public static string Token(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            return s.IndexOf(' ') >= 0 ? "\"" + s.Replace("\"", "") + "\"" : s;
        }

        /// <summary>Splits a command line into tokens (double quotes group words).</summary>
        public static List<string> Split(string line)
        {
            var list = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false, any = false;
            foreach (char ch in line ?? "")
            {
                if (ch == '"') { quoted = !quoted; any = true; continue; }
                if (char.IsWhiteSpace(ch) && !quoted)
                {
                    if (cur.Length > 0 || any) list.Add(cur.ToString());
                    cur.Clear();
                    any = false;
                    continue;
                }
                cur.Append(ch);
            }
            if (cur.Length > 0 || any) list.Add(cur.ToString());
            return list;
        }
    }

    /// <summary>A saved command line with a user label.</summary>
    public sealed class Favorite
    {
        public string Label, Line;
        /// <summary>Extra builder state that the command line alone cannot give back (map of the weather).</summary>
        public string Map;

        public string Cmd
        {
            get
            {
                var t = CommandData.Split(Line);
                return t.Count > 0 ? t[0] : "";
            }
        }
    }

    public static class Favorites
    {
        public static string FilePath { get { return Path.Combine(Library.Folder, "favorites.json"); } }

        public static List<Favorite> Load()
        {
            var list = new List<Favorite>();
            if (!File.Exists(FilePath)) return list;
            var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JObj;
            if (root == null) return list;
            foreach (var x in root.Arr("favorites") ?? new List<object>())
            {
                var o = x as JObj;
                if (o == null || string.IsNullOrWhiteSpace(o.Str("line"))) continue;
                list.Add(new Favorite { Label = o.Str("label", ""), Line = o.Str("line"), Map = o.Str("map") });
            }
            return list;
        }

        public static void Save(IEnumerable<Favorite> favs)
        {
            Directory.CreateDirectory(Library.Folder);
            var root = new JObj();
            root["format"] = "IRRCompanion.Favorites";
            root["version"] = 1.0;
            var arr = new List<object>();
            foreach (var f in favs)
            {
                var o = new JObj();
                o["label"] = f.Label ?? "";
                o["line"] = f.Line;
                if (!string.IsNullOrEmpty(f.Map)) o["map"] = f.Map;
                arr.Add(o);
            }
            root["favorites"] = arr;
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
