using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Persistence.Strings;
using Ekiden.Platform;
using Ekiden.Verification;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>文字列テーブル(09 §12.3): 差し込み・欠落の表示・厳格な読み込み・StringsLint。</summary>
    public class StringsTests
    {
        private static Dictionary<string, string> Table(params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        // ------------------------------------------------------------ TableLocalization
        [Test]
        public void Get_PlainAndNamedPlaceholders()
        {
            var loc = new TableLocalization(Table("scr.home.title", "ホーム", "scr.home.welcome", "{school}の監督、{name}さん。{school}は{rank}位", "ui.brace", "{{x}} と {n}"), true);
            Assert.AreEqual("ホーム", loc.Get("scr.home.title"));
            Assert.AreEqual("青嵐大学の監督、山田さん。青嵐大学は3位", loc.Get("scr.home.welcome", "school", "青嵐大学", "name", "山田", "rank", "3"), "同じ名前を何度でも・順不同");
            Assert.AreEqual("{x} と 7", loc.Get("ui.brace", "n", "7"), "{{ }} は波括弧そのもの");
            Assert.AreEqual("青嵐大学の監督、山田さん。青嵐大学は1位", loc.Get("scr.home.welcome", "rank", "1", "name", "山田", "school", "青嵐大学", "extra", "余り"), "余った引数は無視");
            Assert.IsTrue(loc.Has("scr.home.title"));
            Assert.IsFalse(loc.Has("scr.none"));
        }

        [Test]
        public void MissingKeyAndMissingArgument_DevShowsMarkers_ProductShowsQuestionMark_AndLogsOnce()
        {
            var log = new List<string>();
            var table = Table("a.b", "こんにちは{who}");
            var dev = new TableLocalization(table, true, log.Add);
            Assert.AreEqual("⟦x.y⟧", dev.Get("x.y"));
            Assert.AreEqual("⟦x.y⟧", dev.Get("x.y"));
            Assert.AreEqual(1, log.Count, "同じキーの欠落は診断ログに 1 回だけ");
            StringAssert.Contains("x.y", log[0]);
            Assert.AreEqual("こんにちは⟦who⟧", dev.Get("a.b"), "引数がなければ ⟦名前⟧");
            Assert.AreEqual("こんにちは⟦who⟧", dev.Get("a.b", "other", "z"));
            Assert.AreEqual("こんにちは⟦who⟧", dev.Get("a.b", "who"), "奇数個の引数の最後は無視");
            var prod = new TableLocalization(table, false);
            Assert.AreEqual("？", prod.Get("x.y"));
            Assert.AreEqual("こんにちは？", prod.Get("a.b"));
            Assert.AreEqual("こんにちは太郎", prod.Get("a.b", "who", "太郎"));
        }

        [Test]
        public void BrokenBodies_AreShownAsIs_NotCrashed()
        {
            var loc = new TableLocalization(Table("a.open", "ひらく{未完", "a.close", "とじる}だけ", "a.empty", "{}"), true);
            Assert.AreEqual("ひらく{未完", loc.Get("a.open"));
            Assert.AreEqual("とじる}だけ", loc.Get("a.close"));
            Assert.AreEqual("⟦⟧", loc.Get("a.empty"));
        }

        [Test]
        public void Placeholders_ListsNamesInOrder_IgnoringEscapes()
        {
            CollectionAssert.AreEqual(new[] { "a", "b", "a" }, TableLocalization.Placeholders("{a} と {b} と {a} {{c}}"));
            CollectionAssert.IsEmpty(TableLocalization.Placeholders("なし {{ }}"));
        }

        // ------------------------------------------------------------ StringTable(読み込み)
        [Test]
        public void StringTable_ParsesAFlatDictionary_AndWritesItBackIdentically()
        {
            string json = "{\n  \"scr.a.b\": \"本文\\n二行目 \\\"引用\\\" \\\\ \\u0041\",\n  \"ui.x\": \"{n}件\"\n}\n";
            Dictionary<string, string> d = StringTable.Parse(json);
            Assert.AreEqual("本文\n二行目 \"引用\" \\ A", d["scr.a.b"]);
            Assert.AreEqual("{n}件", d["ui.x"]);
            string written = StringTable.Write(d);
            Assert.AreEqual(d.Count, StringTable.Parse(written).Count);
            CollectionAssert.AreEquivalent(d, StringTable.Parse(written));
            Assert.AreEqual(written, StringTable.Write(StringTable.Parse(written)), "書いたものを読んで書き直しても同じ");
            Assert.AreEqual("{\n}\n", StringTable.Write(new Dictionary<string, string>()));
            Assert.AreEqual(0, StringTable.Parse("{ }").Count);
            Assert.AreEqual(1, StringTable.Parse("\uFEFF{\"a.b\":\"c\"}").Count, "BOM つきでも読める");
        }

        [Test]
        public void StringTable_RejectsMalformedAndAmbiguousTables()
        {
            foreach (string bad in new[]
            {
                "[]", "\"x\"", "{\"a.b\": 1}", "{\"a.b\": null}", "{\"a.b\": {\"c\": \"d\"}}",
                "{\"a.b\": \"x\", \"a.b\": \"y\"}",                // キーの重複
                "{\"\": \"x\"}",                                  // 空のキー
                "{\"a.b\": \"x\"} 余計", "{\"a.b\": \"x\"", "{\"a.b\" \"x\"}", "{\"a.b\": \"x\",}", "{\"a.b\": \"\\q\"}", "{\"a.b\": \"\\u12\"}", "{\"a.b\": \"改\n行\"}",
            })
                Assert.Throws<FormatException>(() => StringTable.Parse(bad), bad);
        }

        // ------------------------------------------------------------ StringsLint
        [Test]
        public void Lint_FindsBadKeysEmptyBodiesAndBrokenPlaceholders()
        {
            var table = Table("scr.home.title", "ホーム", "BadKey", "x", "scr.empty", "  ", "scr.open", "{school", "scr.close", "x}", "scr.name", "{Bad Name}", "scr.ok", "{a}と{b}{{c}}", "ev.team.captain.title", "主将の指名", "skill.climb.name", "登坂");
            List<LintIssue> issues = StringsLint.CheckTable(table);
            Func<string, bool> has = k => issues.Any(i => i.Key == k);
            Assert.IsTrue(has("BadKey"), "キーの書式");
            Assert.IsTrue(has("scr.empty"), "空の本文");
            Assert.IsTrue(has("scr.open"), "閉じていない {");
            Assert.IsTrue(has("scr.close"), "対応のない }");
            Assert.IsTrue(has("scr.name"), "差し込みの名前");
            Assert.IsFalse(has("scr.ok") || has("scr.home.title") || has("ev.team.captain.title") || has("skill.climb.name"), "正しいものは指摘しない(ID を含むキーも)");
        }

        [Test]
        public void Lint_CallsMustMatchThePlaceholders()
        {
            var table = Table("scr.home.welcome", "{school}の{name}", "scr.home.title", "ホーム");
            var calls = new[]
            {
                new CallSite { Key = "scr.home.welcome", ArgNames = new[] { "school", "name" }, Where = "HomeScreen" },
                new CallSite { Key = "scr.home.welcome", ArgNames = new[] { "school" }, Where = "足りない" },
                new CallSite { Key = "scr.home.title", ArgNames = new[] { "extra" }, Where = "余る" },
                new CallSite { Key = "scr.none", Where = "表にない" },
            };
            List<LintIssue> issues = StringsLint.CheckCalls(table, calls);
            Assert.AreEqual(3, issues.Count, string.Join(" / ", issues));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("{name}") && i.Message.Contains("足りない")));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("extra") && i.Message.Contains("余る")));
            Assert.IsTrue(issues.Any(i => i.Key == "scr.none"));
        }

        [Test]
        public void Charset_CoversBodiesNamesAndFormatting_AndMissingFromFindsGaps()
        {
            string cs = StringsLint.Charset(new[] { "ホーム 🏃", "{n}件" });
            StringAssert.Contains("ホ", cs);
            StringAssert.Contains("🏃", cs, "サロゲートペアも 1 文字として扱う");
            StringAssert.Contains("佐", cs, "名前の表の文字");
            StringAssert.Contains("さ", cs, "かな");
            StringAssert.Contains("−", cs, "整形が出す文字(U+2212)");
            StringAssert.Contains("万", cs);
            var cps = new List<int>();
            for (int i = 0; i < cs.Length; i++) { cps.Add(char.ConvertToUtf32(cs, i)); if (char.IsHighSurrogate(cs[i])) i++; }
            CollectionAssert.AreEqual(cps.Distinct().OrderBy(x => x).ToList(), cps, "コードポイントの昇順・重複なし");
            Assert.AreEqual("🏃", StringsLint.MissingFrom("ホ🏃ー ", "ホー"), "フォントにない文字だけ(空白は数えない)");
            Assert.AreEqual("", StringsLint.MissingFrom("ホー", "ホーム"));
        }
    }
}
