using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ekiden.Persistence.Strings
{
    /// <summary>
    /// 文字列テーブル(<c>Strings/ja.json</c> = <c>{ "キー": "本文" }</c> の平らな辞書。09 §12.3)の読み書き。
    /// 読むときは厳格に: 根はオブジェクト・値はすべて文字列・キーの重複と空のキーは不正(一般の <see cref="Json.JsonValue"/> は重複を後勝ちにするので使わない)。不正は <see cref="FormatException"/>(位置つき)。
    /// </summary>
    public static class StringTable
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 0;
            Skip(json, ref i);
            if (i >= json.Length || json[i] != '{') throw new FormatException("文字列テーブルの根は { } のオブジェクト");
            i++;
            Skip(json, ref i);
            if (i < json.Length && json[i] == '}') { i++; End(json, ref i); return d; }
            while (true)
            {
                Skip(json, ref i);
                string k = Str(json, ref i, "キー");
                if (k.Length == 0) throw new FormatException("空のキー(位置 " + i + ")");
                Skip(json, ref i);
                if (i >= json.Length || json[i] != ':') throw new FormatException("':' が要る(位置 " + i + ")");
                i++;
                Skip(json, ref i);
                string v = Str(json, ref i, "本文(値は文字列だけ。キー " + k + ")");
                if (d.ContainsKey(k)) throw new FormatException("キーが重複している: " + k);
                d[k] = v;
                Skip(json, ref i);
                if (i >= json.Length) throw new FormatException("途中で終わっている");
                if (json[i] == ',') { i++; continue; }
                if (json[i] == '}') { i++; End(json, ref i); return d; }
                throw new FormatException("',' か '}' が要る(位置 " + i + ")");
            }
        }

        /// <summary>キーの昇順(序数)で書く。1 行 1 項目。再現できる(同じ辞書は同じ文字列)ので、差分が読める。</summary>
        public static string Write(IReadOnlyDictionary<string, string> table)
        {
            var keys = new List<string>(table.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.Append("{\n");
            for (int n = 0; n < keys.Count; n++)
            {
                sb.Append("  ");
                Quote(sb, keys[n]);
                sb.Append(": ");
                Quote(sb, table[keys[n]]);
                sb.Append(n + 1 < keys.Count ? ",\n" : "\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void Quote(StringBuilder sb, string s)
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r' || s[i] == '﻿')) i++;
        }

        private static void End(string s, ref int i)
        {
            Skip(s, ref i);
            if (i < s.Length) throw new FormatException("末尾に余計な文字(位置 " + i + ")");
        }

        private static string Str(string s, ref int i, string what)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException(what + "は \" で始まる文字列(位置 " + i + ")");
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("文字列が閉じていない");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw new FormatException("文字列に制御文字(位置 " + (i - 1) + ")");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("文字列が閉じていない");
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
                        if (i + 4 > s.Length) throw new FormatException("\\u が途中で終わっている");
                        int cp;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out cp)) throw new FormatException("\\u の 16 進が不正(位置 " + i + ")");
                        sb.Append((char)cp);
                        i += 4;
                        break;
                    default: throw new FormatException("不正なエスケープ \\" + e + "(位置 " + (i - 1) + ")");
                }
            }
        }
    }
}
