using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace IRRPresetTool
{
    public sealed class Param
    {
        public string Key, DocKey, Category, Section, Name, Description, Warning;
        public bool IsBool;
        public int Precision;
        public double? Min, Max, UIMin, UIMax, Step;

        public bool IsSimulation { get { return Category == "Simulation"; } }

        /// <summary>Range shown on sliders / comparison bars.</summary>
        public double ViewMin { get { return UIMin ?? Min ?? 0; } }
        public double ViewMax { get { return UIMax ?? Max ?? 1; } }

        /// <summary>Hard limits accepted by the editor.</summary>
        public double HardMin { get { return Min ?? Math.Min(0, ViewMin); } }
        public double HardMax { get { return Max ?? Math.Max(ViewMax * 4, 1); } }

        public double Increment
        {
            get { return Step ?? Math.Pow(10, -Precision); }
        }

        public bool OutsideRecommended(double v)
        {
            return v < ViewMin - 1e-9 || v > ViewMax + 1e-9;
        }

        public string Format(object v)
        {
            if (v == null) return "—";
            if (v is bool) return (bool)v ? "On" : "Off";
            if (v is double)
            {
                // Same rounding as the in-game menu (the exact value is kept in the preset).
                return ((double)v).ToString("F" + Math.Max(Precision, 0), System.Globalization.CultureInfo.InvariantCulture);
            }
            return v.ToString();
        }

        public string Tooltip
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(Name);
                if (!string.IsNullOrEmpty(Description)) sb.Append("\n\n").Append(Description);
                if (!string.IsNullOrEmpty(Warning)) sb.Append("\n\n⚠ ").Append(Warning);
                if (!IsBool)
                {
                    sb.Append("\n\nRecommended range: ").Append(Format(ViewMin)).Append(" – ").Append(Format(ViewMax));
                    if (Min.HasValue || Max.HasValue)
                        sb.Append("\nAllowed: ").Append(Min.HasValue ? Format(Min.Value) : "…")
                          .Append(" – ").Append(Max.HasValue ? Format(Max.Value) : "…");
                }
                sb.Append("\n\nGame key: ").Append(Key);
                return sb.ToString();
            }
        }
    }

    /// <summary>Default presets and parameter descriptions extracted from the game (embedded resource).</summary>
    public static class GameData
    {
        public static string GameBuild;
        public static int AiHardVersion, AiSoftVersion, SimHardVersion, SimSoftVersion;
        public static List<string> AiOrder = new List<string>();
        public static List<string> SimOrder = new List<string>();
        /// <summary>In-game names of the difficulty variants (Easy -> Recruit, ...).</summary>
        public static Dictionary<string, string> AiLabels = new Dictionary<string, string>();
        public static Dictionary<string, JObj> Ai = new Dictionary<string, JObj>();
        public static Dictionary<string, JObj> Sim = new Dictionary<string, JObj>();
        public static List<Param> Params = new List<Param>();

        public static void Load()
        {
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("defaults.json"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                text = r.ReadToEnd();
            var root = (JObj)Json.Parse(text);
            GameBuild = root.Str("gameBuild");
            AiHardVersion = (int)root.Num("aiHardVersion");
            AiSoftVersion = (int)root.Num("aiSoftVersion");
            SimHardVersion = (int)root.Num("simHardVersion");
            SimSoftVersion = (int)root.Num("simSoftVersion");
            foreach (var x in root.Arr("aiOrder")) AiOrder.Add((string)x);
            var labels = root.Obj("aiLabels");
            foreach (var k in AiOrder) AiLabels[k] = labels != null ? labels.Str(k, k) : k;
            foreach (var x in root.Arr("simOrder")) SimOrder.Add((string)x);
            foreach (var k in root.Obj("ai").Keys) Ai[k] = root.Obj("ai").Obj(k);
            foreach (var k in root.Obj("sim").Keys) Sim[k] = root.Obj("sim").Obj(k);
            foreach (var x in root.Arr("params"))
            {
                var o = (JObj)x;
                Params.Add(new Param
                {
                    Key = o.Str("key"),
                    DocKey = o.Str("docKey"),
                    Category = o.Str("cat"),
                    Section = o.Str("section"),
                    Name = o.Str("name"),
                    Description = o.Str("desc", ""),
                    Warning = o.Str("warn", ""),
                    IsBool = o.Str("type") == "bool",
                    Precision = (int)o.Num("precision", 2),
                    Min = o.Has("min") ? (double?)o.Num("min") : null,
                    Max = o.Has("max") ? (double?)o.Num("max") : null,
                    UIMin = o.Has("uiMin") ? (double?)o.Num("uiMin") : null,
                    UIMax = o.Has("uiMax") ? (double?)o.Num("uiMax") : null,
                    Step = o.Has("step") ? (double?)o.Num("step") : null,
                });
            }
        }

        /// <summary>Name of the built-in simulation level whose values equal this doc, or null.</summary>
        public static string MatchSimLevel(JObj simDoc)
        {
            foreach (var name in SimOrder)
            {
                bool same = true;
                foreach (var p in Params)
                {
                    if (!p.IsSimulation) continue;
                    if (!ValuesEqual(Json.GetPath(simDoc, p.DocKey), Json.GetPath(Sim[name], p.DocKey))) { same = false; break; }
                }
                if (same) return name;
            }
            return null;
        }

        public static bool ValuesEqual(object a, object b)
        {
            if (a is double && b is double) return Math.Abs((double)a - (double)b) < 1e-9;
            return Equals(a, b);
        }
    }
}
