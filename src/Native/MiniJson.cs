using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mischpult.Native
{
    /// <summary>4.0: kleiner JSON-Leser/-Schreiber (fuer OBS): Objekte → Dictionary, Listen → List&lt;object&gt;, Zahlen → double.</summary>
    internal static class MiniJson
    {
        public static object Parse(string s)
        {
            if (s == null) return null;
            int i = 0;
            try
            {
                var v = Value(s, ref i);
                Ws(s, ref i);
                return i == s.Length ? v : null;
            }
            catch { return null; }
        }

        private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException();
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i++] != ':') throw new FormatException();
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException();
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException();
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException();
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException();
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        public static string Write(object o)
        {
            var sb = new StringBuilder();
            W(sb, o);
            return sb.ToString();
        }

        private static void W(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); return;
                case string s:
                    sb.Append('"');
                    foreach (char c in s)
                    {
                        if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                        else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                    }
                    sb.Append('"');
                    return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case IDictionary<string, object> d:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in d) { if (!first) sb.Append(','); first = false; W(sb, kv.Key); sb.Append(':'); W(sb, kv.Value); }
                    sb.Append('}');
                    return;
                }
                case IEnumerable e:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var x in e) { if (!first) sb.Append(','); first = false; W(sb, x); }
                    sb.Append(']');
                    return;
                }
                case IFormattable f: sb.Append(f.ToString(null, CultureInfo.InvariantCulture)); return;
                default: W(sb, o.ToString()); return;
            }
        }

        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static string S(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as string : null;
        public static bool B(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is bool b && b;
        public static int I(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is double x ? (int)x : -1;
    }
}
