using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ekiden.Domain.Text;
using Ekiden.Platform;

namespace Ekiden.Verification
{
    /// <summary>1 件の指摘。</summary>
    public sealed class LintIssue
    {
        public string Key, Message;
        public override string ToString() { return Key + ": " + Message; }
    }

    /// <summary>呼び出し側(画面・コード)が引くキーと、渡す差し込みの名前。<see cref="StringsLint.CheckCalls"/> で、本文の <c>{名前}</c> と一致するか検査する。</summary>
    public sealed class CallSite
    {
        public string Key;
        public string[] ArgNames = new string[0];
        public string Where = "";
    }

    /// <summary>
    /// 文字列テーブル(09 §12.3)の検査。①キーの書式 ②本文が空でない ③差し込み <c>{名前}</c> の書式(閉じている・名前が [a-z][a-z0-9_]*)④呼び出しの引数との一致(足りない・余る)
    /// ⑤使われている文字の集合(フォントの対応との突き合わせ。<see cref="MissingFrom"/>)。
    /// </summary>
    public static class StringsLint
    {
        /// <summary>キー: 小文字で始まる区切りが '.' の 2 語以上。語は英数字・'_'・'-'(ID の中身を含む。例 <c>scr.home.title</c> / <c>ev.team.captain.title</c>)。</summary>
        public static readonly Regex KeyFormat = new Regex(@"^[a-z][a-z0-9_]*(\.[A-Za-z0-9_\-]+)+$", RegexOptions.CultureInvariant);

        public static readonly Regex PlaceholderName = new Regex(@"^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

        /// <summary>整形(09 §13 の <c>Fmt</c>)が出す文字(数字・記号・単位・語)。表の本文に出ないが、フォントに要る。</summary>
        public const string FmtChars = "0123456789:.+−±,%万円億年度月週位未満超繰上第 km";

        public static List<LintIssue> CheckTable(IReadOnlyDictionary<string, string> table)
        {
            var issues = new List<LintIssue>();
            foreach (string key in table.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                string body = table[key];
                if (!KeyFormat.IsMatch(key)) issues.Add(new LintIssue { Key = key, Message = "キーの書式が違う(小文字で始まり、'.' で区切った 2 語以上)" });
                if (string.IsNullOrWhiteSpace(body)) { issues.Add(new LintIssue { Key = key, Message = "本文が空" }); continue; }
                foreach (string problem in CheckPlaceholders(body)) issues.Add(new LintIssue { Key = key, Message = problem });
            }
            return issues;
        }

        /// <summary>本文の差し込みの書式。</summary>
        public static List<string> CheckPlaceholders(string body)
        {
            var problems = new List<string>();
            int opens = 0;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '{')
                {
                    if (i + 1 < body.Length && body[i + 1] == '{') { i++; continue; }
                    int j = body.IndexOf('}', i + 1);
                    if (j < 0) { problems.Add("'{' が閉じていない(位置 " + i + ")"); break; }
                    string name = body.Substring(i + 1, j - i - 1);
                    if (!PlaceholderName.IsMatch(name)) problems.Add("差し込みの名前が不正: {" + name + "}");
                    opens++;
                    i = j;
                }
                else if (c == '}')
                {
                    if (i + 1 < body.Length && body[i + 1] == '}') { i++; continue; }
                    problems.Add("対応する '{' がない '}'(位置 " + i + ")");
                }
            }
            return problems;
        }

        /// <summary>呼び出しごとに: キーが表にある・本文の <c>{名前}</c> の集合と渡す名前の集合が一致する。</summary>
        public static List<LintIssue> CheckCalls(IReadOnlyDictionary<string, string> table, IEnumerable<CallSite> calls)
        {
            var issues = new List<LintIssue>();
            foreach (CallSite c in calls)
            {
                string body;
                if (!table.TryGetValue(c.Key, out body)) { issues.Add(new LintIssue { Key = c.Key, Message = "表にない(" + c.Where + ")" }); continue; }
                var need = new HashSet<string>(TableLocalization.Placeholders(body), StringComparer.Ordinal);
                var have = new HashSet<string>(c.ArgNames, StringComparer.Ordinal);
                foreach (string n in need.Where(n => !have.Contains(n)).OrderBy(n => n, StringComparer.Ordinal)) issues.Add(new LintIssue { Key = c.Key, Message = "本文の {" + n + "} に対して引数がない(" + c.Where + ")" });
                foreach (string n in have.Where(n => !need.Contains(n)).OrderBy(n => n, StringComparer.Ordinal)) issues.Add(new LintIssue { Key = c.Key, Message = "引数 " + n + " は本文で使われない(" + c.Where + ")" });
            }
            return issues;
        }

        /// <summary>使われている文字の集合(コードポイントの昇順・重複なし)。表の本文・名前の表(姓・名・かな・留学生の部品)・整形が出す文字。</summary>
        public static string Charset(IEnumerable<string> bodies, bool includeNames = true, bool includeFmt = true)
        {
            var set = new SortedSet<int>();
            Action<string> add = s =>
            {
                for (int i = 0; i < s.Length; i++)
                {
                    int cp = char.ConvertToUtf32(s, i);
                    if (char.IsHighSurrogate(s[i])) i++;
                    if (cp == '\n' || cp == '\r' || cp == '\t') continue;
                    set.Add(cp);
                }
            };
            foreach (string b in bodies) add(b);
            if (includeNames)
            {
                foreach (string s in NameTableData.FamilyText.Concat(NameTableData.FamilyKana).Concat(NameTableData.GivenText).Concat(NameTableData.GivenKana)) add(s);
                foreach (string[] col in NameTableData.IntlFamilyParts.Concat(NameTableData.IntlGivenParts)) foreach (string s in col) { add(s); add(NameGenerator.ToHiragana(s)); }
            }
            if (includeFmt) add(FmtChars);
            var sb = new StringBuilder();
            foreach (int cp in set) sb.Append(char.ConvertFromUtf32(cp));
            return sb.ToString();
        }

        /// <summary>フォントが持つ文字(<paramref name="fontChars"/>)にない文字。</summary>
        public static string MissingFrom(string charset, string fontChars)
        {
            var have = new HashSet<int>();
            for (int i = 0; i < fontChars.Length; i++)
            {
                int cp = char.ConvertToUtf32(fontChars, i);
                if (char.IsHighSurrogate(fontChars[i])) i++;
                have.Add(cp);
            }
            var sb = new StringBuilder();
            for (int i = 0; i < charset.Length; i++)
            {
                int cp = char.ConvertToUtf32(charset, i);
                if (char.IsHighSurrogate(charset[i])) i++;
                if (!have.Contains(cp) && cp != ' ') sb.Append(char.ConvertFromUtf32(cp));
            }
            return sb.ToString();
        }
    }
}
