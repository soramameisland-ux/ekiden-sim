using System;
using System.Collections.Generic;
using System.Text;

namespace Ekiden.Platform
{
    /// <summary>
    /// 平らな辞書(<c>Strings/ja.json</c>。09 §12.3)による <see cref="ILocalization"/>。
    /// 差し込みは <c>{名前}</c>、波括弧そのものは <c>{{</c> <c>}}</c>。欠落: 開発ビルド(<c>development</c>)は <c>⟦キー⟧</c> / <c>⟦名前⟧</c>、製品は <c>？</c>。キーの欠落は診断ログ(<paramref name="log"/>)に 1 回だけ残す。
    /// </summary>
    public sealed class TableLocalization : ILocalization
    {
        private readonly IReadOnlyDictionary<string, string> _table;
        private readonly bool _dev;
        private readonly Action<string> _log;
        private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);

        public TableLocalization(IReadOnlyDictionary<string, string> table, bool development, Action<string> log = null)
        {
            _table = table;
            _dev = development;
            _log = log;
        }

        public bool Has(string key) { return _table.ContainsKey(key); }

        public string Get(string key, params string[] args)
        {
            string t;
            if (!_table.TryGetValue(key, out t)) return Missing(key);
            return Format(t, args);
        }

        private string Missing(string key)
        {
            if (_log != null && _logged.Add(key)) _log("文字列のキーが表にない: " + key);
            return _dev ? "⟦" + key + "⟧" : "？";
        }

        private string Format(string t, string[] args)
        {
            if (t.IndexOf('{') < 0 && t.IndexOf('}') < 0) return t;
            var sb = new StringBuilder(t.Length + 16);
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '{')
                {
                    if (i + 1 < t.Length && t[i + 1] == '{') { sb.Append('{'); i++; continue; }
                    int j = t.IndexOf('}', i + 1);
                    if (j < 0) { sb.Append(c); continue; }                          // 壊れた本文はそのまま(StringsLint が見つける)
                    string name = t.Substring(i + 1, j - i - 1), value;
                    sb.Append(TryArg(args, name, out value) ? value : (_dev ? "⟦" + name + "⟧" : "？"));
                    i = j;
                }
                else if (c == '}' && i + 1 < t.Length && t[i + 1] == '}') { sb.Append('}'); i++; }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool TryArg(string[] args, string name, out string value)
        {
            value = null;
            if (args == null) return false;
            for (int k = 0; k + 1 < args.Length; k += 2)
                if (string.Equals(args[k], name, StringComparison.Ordinal)) { value = args[k + 1] ?? ""; return true; }
            return false;
        }

        /// <summary>本文の差し込みの名前(出てくる順。重複あり)。<c>{{</c> <c>}}</c> は数えない。閉じていない <c>{</c> は無視する。StringsLint が使う。</summary>
        public static List<string> Placeholders(string template)
        {
            var names = new List<string>();
            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] != '{') continue;
                if (i + 1 < template.Length && template[i + 1] == '{') { i++; continue; }
                int j = template.IndexOf('}', i + 1);
                if (j < 0) break;
                names.Add(template.Substring(i + 1, j - i - 1));
                i = j;
            }
            return names;
        }
    }
}
