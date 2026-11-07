using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IRRCompanion
{
    /// <summary>JSON object that keeps key order (the game writes keys in a fixed order).</summary>
    public sealed class JObj
    {
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, object> _map = new Dictionary<string, object>(StringComparer.Ordinal);

        public IEnumerable<string> Keys { get { return _keys; } }
        public int Count { get { return _keys.Count; } }
        public bool Has(string k) { return _map.ContainsKey(k); }

        public object this[string k]
        {
            get { object v; return _map.TryGetValue(k, out v) ? v : null; }
            set { if (!_map.ContainsKey(k)) _keys.Add(k); _map[k] = value; }
        }

        public void Remove(string k) { if (_map.Remove(k)) _keys.Remove(k); }

        public JObj Obj(string k) { return this[k] as JObj; }
        public List<object> Arr(string k) { return this[k] as List<object>; }
        public string Str(string k, string def = null) { var v = this[k] as string; return v ?? def; }
        public double Num(string k, double def = 0) { var v = this[k]; return v is double ? (double)v : def; }
    }

    public static class Json
    {
        // ---------- parsing ----------
        public static object Parse(string s)
        {
            int i = 0;
            if (s.Length > 0 && s[0] == '﻿') i = 1;
            var v = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (i != s.Length) throw new FormatException("Unexpected data after JSON value at " + i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{') return ParseObj(s, ref i);
            if (c == '[') return ParseArr(s, ref i);
            if (c == '"') return ParseStr(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("Invalid JSON at " + st);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static JObj ParseObj(string s, ref int i)
        {
            var o = new JObj();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs(s, ref i);
                string k = ParseStr(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' at " + i);
                i++;
                o[k] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return o; }
                throw new FormatException("Expected ',' or '}' at " + i);
            }
        }

        private static List<object> ParseArr(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return a; }
                throw new FormatException("Expected ',' or ']' at " + i);
            }
        }

        private static string ParseStr(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: throw new FormatException("Bad escape at " + i);
                }
            }
            throw new FormatException("Unterminated string");
        }

        // ---------- writing ----------
        public static string Write(object v, bool indent = false)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v, indent, 0);
            return sb.ToString();
        }

        public static string Num(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (Math.Abs(d) < 1e15 && d == Math.Floor(d)) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void Nl(StringBuilder sb, bool indent, int depth)
        {
            if (!indent) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        private static void WriteValue(StringBuilder sb, object v, bool indent, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double) { sb.Append(Num((double)v)); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is string) { WriteStr(sb, (string)v); return; }
            var o = v as JObj;
            if (o != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var k in o.Keys)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Nl(sb, indent, depth + 1);
                    WriteStr(sb, k);
                    sb.Append(indent ? ": " : ":");
                    WriteValue(sb, o[k], indent, depth + 1);
                }
                if (!first) Nl(sb, indent, depth);
                sb.Append('}');
                return;
            }
            var a = v as List<object>;
            if (a != null)
            {
                sb.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Nl(sb, indent, depth + 1);
                    WriteValue(sb, a[i], indent, depth + 1);
                }
                if (a.Count > 0) Nl(sb, indent, depth);
                sb.Append(']');
                return;
            }
            throw new ArgumentException("Unsupported JSON value type " + v.GetType());
        }

        private static void WriteStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Deep copy of a parsed JSON value.</summary>
        public static object Clone(object v)
        {
            var o = v as JObj;
            if (o != null)
            {
                var r = new JObj();
                foreach (var k in o.Keys) r[k] = Clone(o[k]);
                return r;
            }
            var a = v as List<object>;
            if (a != null)
            {
                var r = new List<object>(a.Count);
                foreach (var x in a) r.Add(Clone(x));
                return r;
            }
            return v;
        }

        public static JObj CloneObj(JObj o) { return (JObj)Clone(o); }

        /// <summary>Reads a value by dotted path ("optimizationRanges.1").</summary>
        public static object GetPath(JObj root, string path)
        {
            object cur = root;
            foreach (var part in path.Split('.'))
            {
                var o = cur as JObj;
                if (o == null) return null;
                cur = o[part];
            }
            return cur;
        }

        public static void SetPath(JObj root, string path, object value)
        {
            var parts = path.Split('.');
            var cur = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var next = cur.Obj(parts[i]);
                if (next == null) { next = new JObj(); cur[parts[i]] = next; }
                cur = next;
            }
            cur[parts[parts.Length - 1]] = value;
        }
    }
}
