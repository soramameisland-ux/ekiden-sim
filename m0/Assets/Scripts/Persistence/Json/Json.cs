using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ekiden.Domain.Numerics;

namespace Ekiden.Persistence.Json
{
    /// <summary>セーブの JSON を読む・書く最小の実装(09 §5.3)。順序を保つ(オブジェクトのキーは出てきた順)。未知の項目は読み飛ばさず、そのまま保って書き戻す。外部のライブラリに依存しない(Unity とサーバーなしの両方で同じ結果)。</summary>
    public abstract class JsonValue
    {
        public virtual bool IsNull { get { return false; } }

        public static JsonValue Parse(string text) { return JsonReader.Parse(text); }

        public string ToJson() { var sb = new StringBuilder(); JsonWriter.Write(this, sb); return sb.ToString(); }

        public byte[] ToUtf8() { return new UTF8Encoding(false).GetBytes(ToJson()); }

        public abstract JsonValue DeepClone();

        /// <summary>構造の等しさ(数は値で比べる。オブジェクトはキーの順も含めて比べる)。</summary>
        public abstract bool StructEquals(JsonValue other);
    }

    public sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();
        private JsonNull() { }
        public override bool IsNull { get { return true; } }
        public override JsonValue DeepClone() { return this; }
        public override bool StructEquals(JsonValue other) { return other is JsonNull; }
    }

    public sealed class JsonBool : JsonValue
    {
        public static readonly JsonBool True = new JsonBool(true), False = new JsonBool(false);
        public bool Value { get; }
        private JsonBool(bool v) { Value = v; }
        public static JsonBool Of(bool v) { return v ? True : False; }
        public override JsonValue DeepClone() { return this; }
        public override bool StructEquals(JsonValue other) { var o = other as JsonBool; return o != null && o.Value == Value; }
    }

    /// <summary>数。整数は long、そのほかは double。読んだ数は元の文字列(<see cref="Raw"/>)を保ち、そのまま書き戻す(値を変えたものは新しく作る)。</summary>
    public sealed class JsonNumber : JsonValue
    {
        public double Value { get; }
        public bool IsInteger { get; }
        public long Int { get; }
        public string Raw { get; }

        public JsonNumber(long v) { Value = v; IsInteger = true; Int = v; }

        public JsonNumber(double v)
        {
            Value = v;
            if (v == Math.Floor(v) && Math.Abs(v) < 9e15 && !(v == 0 && 1 / v < 0)) { IsInteger = true; Int = (long)v; }
        }

        internal JsonNumber(double v, bool isInt, long i, string raw) { Value = v; IsInteger = isInt; Int = i; Raw = raw; }

        public override JsonValue DeepClone() { return this; }

        public override bool StructEquals(JsonValue other)
        {
            var o = other as JsonNumber;
            return o != null && (Value == o.Value || (double.IsNaN(Value) && double.IsNaN(o.Value)));
        }
    }

    public sealed class JsonString : JsonValue
    {
        public string Value { get; }
        public JsonString(string v) { Value = v; }
        public override JsonValue DeepClone() { return this; }
        public override bool StructEquals(JsonValue other) { var o = other as JsonString; return o != null && o.Value == Value; }
    }

    public sealed class JsonArray : JsonValue, IEnumerable<JsonValue>
    {
        public IEnumerator<JsonValue> GetEnumerator() { return Items.GetEnumerator(); }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return Items.GetEnumerator(); }

        public readonly List<JsonValue> Items = new List<JsonValue>();
        public JsonArray() { }
        public JsonArray(IEnumerable<JsonValue> items) { Items.AddRange(items); }
        public int Count { get { return Items.Count; } }
        public JsonValue this[int i] { get { return Items[i]; } set { Items[i] = value; } }
        public void Add(JsonValue v) { Items.Add(v); }

        public override JsonValue DeepClone()
        {
            var a = new JsonArray();
            foreach (JsonValue v in Items) a.Items.Add(v.DeepClone());
            return a;
        }

        public override bool StructEquals(JsonValue other)
        {
            var o = other as JsonArray;
            if (o == null || o.Count != Count) return false;
            for (int i = 0; i < Count; i++) if (!Items[i].StructEquals(o.Items[i])) return false;
            return true;
        }
    }

    /// <summary>キーの順を保つオブジェクト。</summary>
    public sealed class JsonObject : JsonValue
    {
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, JsonValue> _map = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

        public int Count { get { return _keys.Count; } }
        public IReadOnlyList<string> Keys { get { return _keys; } }
        public bool ContainsKey(string k) { return _map.ContainsKey(k); }

        public JsonValue this[string k]
        {
            get { return _map[k]; }
            set
            {
                if (!_map.ContainsKey(k)) _keys.Add(k);
                _map[k] = value;
            }
        }

        public bool TryGet(string k, out JsonValue v) { return _map.TryGetValue(k, out v); }

        public JsonValue Get(string k)
        {
            JsonValue v;
            return _map.TryGetValue(k, out v) ? v : null;
        }

        public bool Remove(string k)
        {
            if (!_map.Remove(k)) return false;
            _keys.Remove(k);
            return true;
        }

        public override JsonValue DeepClone()
        {
            var o = new JsonObject();
            foreach (string k in _keys) o[k] = _map[k].DeepClone();
            return o;
        }

        public override bool StructEquals(JsonValue other)
        {
            var o = other as JsonObject;
            if (o == null || o.Count != Count) return false;
            for (int i = 0; i < _keys.Count; i++)
            {
                if (_keys[i] != o._keys[i]) return false;
                if (!_map[_keys[i]].StructEquals(o._map[_keys[i]])) return false;
            }
            return true;
        }

        /// <summary>キーの集合と各値だけで比べる(順序は無視)。</summary>
        public bool SameContent(JsonObject o)
        {
            if (o == null || o.Count != Count) return false;
            foreach (string k in _keys)
            {
                JsonValue v;
                if (!o._map.TryGetValue(k, out v) || !SameValue(_map[k], v)) return false;
            }
            return true;
        }

        private static bool SameValue(JsonValue a, JsonValue b)
        {
            var oa = a as JsonObject;
            if (oa != null) return oa.SameContent(b as JsonObject);
            var aa = a as JsonArray;
            var ab = b as JsonArray;
            if (aa != null)
            {
                if (ab == null || aa.Count != ab.Count) return false;
                for (int i = 0; i < aa.Count; i++) if (!SameValue(aa[i], ab[i])) return false;
                return true;
            }
            return a.StructEquals(b);
        }
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    /// <summary>厳格な JSON の読み取り(RFC 8259)。深さ・長さの上限あり。</summary>
    public static class JsonReader
    {
        public const int MaxDepth = 64;

        public static JsonValue Parse(string s)
        {
            int i = 0;
            Skip(s, ref i);
            JsonValue v = Value(s, ref i, 0);
            Skip(s, ref i);
            if (i != s.Length) throw new JsonException("末尾に余分な文字がある(位置 " + i + ")");
            return v;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static JsonValue Value(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new JsonException("入れ子が深すぎる");
            if (i >= s.Length) throw new JsonException("途中で終わっている");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var o = new JsonObject();
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new JsonException("キーが要る(位置 " + i + ")");
                    string k = Str(s, ref i);
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new JsonException("':' が要る(位置 " + i + ")");
                    i++;
                    Skip(s, ref i);
                    o[k] = Value(s, ref i, depth + 1);
                    Skip(s, ref i);
                    if (i >= s.Length) throw new JsonException("途中で終わっている");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw new JsonException("',' か '}' が要る(位置 " + i + ")");
                }
            }
            if (c == '[')
            {
                i++;
                var a = new JsonArray();
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    Skip(s, ref i);
                    a.Add(Value(s, ref i, depth + 1));
                    Skip(s, ref i);
                    if (i >= s.Length) throw new JsonException("途中で終わっている");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw new JsonException("',' か ']' が要る(位置 " + i + ")");
                }
            }
            if (c == '"') return new JsonString(Str(s, ref i));
            if (c == 't') return Lit(s, ref i, "true", JsonBool.True);
            if (c == 'f') return Lit(s, ref i, "false", JsonBool.False);
            if (c == 'n') return Lit(s, ref i, "null", JsonNull.Instance);
            if (c == '-' || (c >= '0' && c <= '9')) return Num(s, ref i);
            throw new JsonException("不正な文字 '" + c + "'(位置 " + i + ")");
        }

        private static JsonValue Lit(string s, ref int i, string word, JsonValue v)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new JsonException("不正な値(位置 " + i + ")");
            i += word.Length;
            return v;
        }

        private static JsonValue Num(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            if (i >= s.Length) throw new JsonException("数が途中で終わっている");
            if (s[i] == '0') i++;
            else if (s[i] >= '1' && s[i] <= '9') { while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; }
            else throw new JsonException("不正な数(位置 " + i + ")");
            bool isInt = true;
            if (i < s.Length && s[i] == '.')
            {
                isInt = false;
                i++;
                int d = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == d) throw new JsonException("小数点のあとに数字がない");
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                isInt = false;
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                int d = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == d) throw new JsonException("指数に数字がない");
            }
            string raw = s.Substring(start, i - start);
            double v = Ekiden.Domain.Numerics.ExactDouble.Parse(raw);                     // double.Parse は Unity で 1 ulp ずれることがある(L-71)ので、最近接を保証する読み取りを使う
            long l = 0;
            if (isInt && !long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) isInt = false;
            if (isInt && raw == "-0") { isInt = false; }
            return new JsonNumber(v, isInt, l, raw);
        }

        private static string Str(string s, ref int i)
        {
            i++;                                                   // 開きの引用符
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new JsonException("文字列が閉じていない");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw new JsonException("文字列に制御文字がある");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new JsonException("エスケープが途中で終わっている");
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
                    case 'u':
                        {
                            if (i + 4 > s.Length) throw new JsonException("\\u が途中で終わっている");
                            int cp = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int h = Hex(s[i + k]);
                                if (h < 0) throw new JsonException("\\u の 16 進が不正");
                                cp = cp * 16 + h;
                            }
                            i += 4;
                            sb.Append((char)cp);
                            break;
                        }
                    default: throw new JsonException("不正なエスケープ \\" + e);
                }
            }
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }

    /// <summary>JSON の書き出し(09 §5.3): 空白なし・キーは持っている順・文字列は UTF-8 のまま(必要なエスケープだけ)。数は、読んだものは元の文字列で、新しく作った整数は 10 進で、小数は Q6 の書式で書く。</summary>
    public static class JsonWriter
    {
        public static void Write(JsonValue v, StringBuilder sb)
        {
            var o = v as JsonObject;
            if (o != null)
            {
                sb.Append('{');
                for (int i = 0; i < o.Keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Str(o.Keys[i], sb);
                    sb.Append(':');
                    Write(o[o.Keys[i]], sb);
                }
                sb.Append('}');
                return;
            }
            var a = v as JsonArray;
            if (a != null)
            {
                sb.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(a[i], sb);
                }
                sb.Append(']');
                return;
            }
            var n = v as JsonNumber;
            if (n != null)
            {
                if (n.Raw != null) sb.Append(n.Raw);
                else if (n.IsInteger) sb.Append(n.Int.ToString(CultureInfo.InvariantCulture));
                else sb.Append(Q6.Format(n.Value));
                return;
            }
            var s = v as JsonString;
            if (s != null) { Str(s.Value, sb); return; }
            var b = v as JsonBool;
            if (b != null) { sb.Append(b.Value ? "true" : "false"); return; }
            sb.Append("null");
        }

        private static void Str(string s, StringBuilder sb)
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
