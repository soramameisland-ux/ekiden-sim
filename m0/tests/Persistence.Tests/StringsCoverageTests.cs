using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Views;
using Ekiden.Persistence.Strings;
using Ekiden.Platform;
using Ekiden.Verification;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>文字列テーブルの実物(Assets/Strings/ja.json。09 §12.3)。ID から決まるキーがすべて表にあり、書式の指摘がなく、使われる文字が把握できる。</summary>
    public class StringsCoverageTests
    {
        private static string Find(string relative)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string p = Path.Combine(dir, relative);
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException(relative);
        }

        private static Dictionary<string, string> Real() { return StringTable.Parse(File.ReadAllText(Find("Assets/Strings/ja.json"), new UTF8Encoding(false))); }

        [Test]
        public void EveryKeyDerivedFromAnIdIsInTheTable()
        {
            Dictionary<string, string> table = Real();
            Dictionary<string, string> defaults = StringsSeed.Defaults();
            string[] missing = defaults.Keys.Where(k => !table.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.IsEmpty(missing, "表にない(m0 で `SimCli strings seed` を実行して足す): " + string.Join(", ", missing.Take(10)));
            Assert.GreaterOrEqual(defaults.Count, 200, "イベント・特殊能力・練習・施設・能力・大会などのキー");
        }

        [Test]
        public void EveryTicketMilestoneHasAName_AndTheAnniversaryTakesTheYears()
        {
            Dictionary<string, string> table = Real();
            var ids = new[]
            {
                TicketMilestones.WinFall, TicketMilestones.WinNational, TicketMilestones.WinNewYear, TicketMilestones.Podium, TicketMilestones.Seed, TicketMilestones.Sweep,
                TicketMilestones.Clear, TicketMilestones.Rank3, TicketMilestones.Rank6, TicketMilestones.Rank9, TicketMilestones.Anniversary(10), TicketMilestones.Anniversary(70),
            };
            foreach (string id in ids) Assert.IsTrue(table.ContainsKey(TicketMilestoneView.Of(id).NameKey), id);
            var loc = new TableLocalization(table, true, null);
            Assert.AreEqual("継続の記念(30年度)", loc.Get("tm.anniv.name", "years", "30"));
        }

        [Test]
        public void EveryHighlightKeyIsInTheTable_WithTheValuesItsViewCarries()
        {
            Dictionary<string, string> table = Real();
            var need = new Dictionary<string, string[]>
            {
                { "hl.lead_change", new[] { "{school}", "{other}" } }, { "hl.gap_lose", new[] { "{school}", "{sec}" } }, { "hl.gap_gain", new[] { "{school}", "{sec}" } },
                { "hl.brake", new[] { "{school}", "{pct}" } }, { "hl.stall", new[] { "{school}", "{pct}" } }, { "hl.boost", new[] { "{school}", "{pct}" } },
                { "hl.course_record", new[] { "{school}", "{section}", "{sec}" } }, { "hl.team_record", new[] { "{school}", "{sec}" } },
                { "hl.section_prize", new[] { "{school}", "{section}" } }, { "hl.rank_up", new[] { "{school}", "{n}" } }, { "hl.rank_down", new[] { "{school}", "{n}" } },
            };
            CollectionAssert.AreEquivalent(need.Keys, HighlightView.AllKeys, "試験の表と View の鍵が同じ");
            foreach (var kv in need)
            {
                Assert.IsTrue(table.ContainsKey(kv.Key), kv.Key);
                foreach (string ph in kv.Value) StringAssert.Contains(ph, table[kv.Key], kv.Key);
            }
            var loc = new TableLocalization(table, true, null);
            Assert.AreEqual("青嵐大が北洋大を抜いて首位に立った", loc.Get("hl.lead_change", "school", "青嵐大", "other", "北洋大"));
        }

        [Test]
        public void TheRealTable_HasNoLintIssues_AndAsksNothingOfTheFontBeyondWhatItUses()
        {
            Dictionary<string, string> table = Real();
            List<LintIssue> issues = StringsLint.CheckTable(table);
            Assert.IsEmpty(issues, string.Join("\n", issues.Take(10)));
            string cs = StringsLint.Charset(table.Values);
            Assert.Greater(cs.Length, 300, "使われている文字の集合が取れる(名前の表・整形を含む)");
            Assert.IsFalse(cs.Any(c => char.IsSurrogate(c)), "いまの表にはサロゲートペア(BMP の外)の文字は使わない(フォントの対応が重くなる)");
            Assert.IsFalse(cs.Any(c => char.IsControl(c)), "制御文字なし");
        }

        [Test]
        public void TheRealTable_WorksThroughTableLocalization()
        {
            var log = new List<string>();
            var loc = new TableLocalization(Real(), true, log.Add);
            Assert.AreEqual("秋雲駅伝", loc.Get("race.fall.name"));
            Assert.AreEqual("スタミナ", loc.Get("stat.STA.name"));
            Assert.AreEqual("主将の指名", loc.Get("ev.team.captain.title"));
            Assert.AreEqual("⟦scr.none.x⟧", loc.Get("scr.none.x"));
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void Merge_AddsOnlyMissingKeys_AndKeepsEditedTexts()
        {
            var existing = new Dictionary<string, string> { { "race.fall.name", "秋の大学駅伝(編集済み)" }, { "scr.custom.title", "独自" } };
            List<string> added;
            Dictionary<string, string> merged = StringsSeed.Merge(existing, StringsSeed.Defaults(), out added);
            Assert.AreEqual("秋の大学駅伝(編集済み)", merged["race.fall.name"], "あるキーの本文は変えない");
            Assert.AreEqual("独自", merged["scr.custom.title"], "既存の独自のキーは残す");
            Assert.IsFalse(added.Contains("race.fall.name"));
            Assert.IsTrue(added.Contains("race.national.name"));
            CollectionAssert.AreEqual(added.OrderBy(k => k, StringComparer.Ordinal).ToList(), added, "足したキーは昇順");
            List<string> again;
            StringsSeed.Merge(merged, StringsSeed.Defaults(), out again);
            Assert.IsEmpty(again, "もう一度やっても何も足さない(冪等)");
        }
    }
}
