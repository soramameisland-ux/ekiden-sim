using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>新規ゲームの初期の区間記録(02 §4.8『初期の区間記録』。L-68): 年度 1 の前に区間記録を実在の水準へ置き換え、保持者は『歴代の記録』。</summary>
    public class LegacyRecordTests
    {
        [Test]
        public void SeedTable_MatchesDoc02_AndTheCourses()
        {
            string text = File.ReadAllText(DocPaths.Find("02_data_spec.md"), Encoding.UTF8);
            int start = text.IndexOf("**初期の区間記録(L-68", StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "02 §4.8 に『初期の区間記録』の表がある");
            int end = text.IndexOf("\n### ", start, StringComparison.Ordinal);
            string section = text.Substring(start, end - start);
            string[] names = { "第一戦", "第二戦", "第三戦" };
            int rows = 0;
            foreach (Match m in Regex.Matches(section, @"^\|\s*(第[一二三]戦)\s*\|\s*(\d+)\s*\|\s*([\d.]+)\s*\|\s*(\d+)\s*\|\s*([\d:]+)\s*\|", RegexOptions.Multiline))
            {
                int k = Array.IndexOf(names, m.Groups[1].Value);
                int si = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
                double km = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                int ds = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                Assert.AreEqual(RecordSeeds.CourseDs[k][si], ds, names[k] + " " + (si + 1) + " 区");
                Assert.AreEqual(RaceTables.Course((RaceKind)k)[si].Distance, km, 1e-9, "距離はゲームのコースと同じ");
                Assert.AreEqual(Hms(ds), m.Groups[5].Value, "表記は ds から決まる");
                rows++;
            }
            Assert.AreEqual(24, rows);
            for (int k = 0; k < 3; k++)
                Assert.AreEqual(RaceTables.Course((RaceKind)k).Count, RecordSeeds.CourseDs[k].Length);
        }

        private static string Hms(int ds)
        {
            int s = ds / 10;
            return s >= 3600 ? (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00") : (s / 60) + ":" + (s % 60).ToString("00");
        }

        [Test]
        public void NewGame_StartsWithTheLegacyRecords_AndTheRestOfTheWorldIsUntouched()
        {
            const ulong seed = 77UL;
            GameSession gs = GameSession.New(seed, Difficulty.Mid);
            WorldSim bare = WorldSim.Create(new KeyedRngFactory(seed), GameSession.BurnInYears);
            for (int k = 0; k < 3; k++)
            {
                Assert.AreEqual(RecordSeeds.CourseDs[k].Length, gs.World.History.Course[k].Length);
                for (int si = 0; si < RecordSeeds.CourseDs[k].Length; si++)
                {
                    CourseRecord c = gs.World.History.Course[k][si];
                    Assert.AreEqual(RecordSeeds.LegacySchoolId, c.SchoolId); Assert.AreEqual(0, c.RunnerId);
                    Assert.AreEqual(RecordSeeds.CourseDs[k][si], c.TimeDs); Assert.AreEqual(RecordSeeds.LegacyYear, c.Year);
                }
                // 順位表と大会記録は予熱のまま(= 何も足さない世界と同じ)
                Assert.AreEqual(bare.History.Team[k].SchoolId, gs.World.History.Team[k].SchoolId);
                Assert.AreEqual(bare.History.Team[k].TotalDs, gs.World.History.Team[k].TotalDs);
            }
            CollectionAssert.AreEqual(bare.History.Races.Select(e => e.WinnerId * 10 + (int)e.Kind).ToArray(), gs.World.History.Races.Select(e => e.WinnerId * 10 + (int)e.Kind).ToArray());
            Assert.AreEqual(bare.History.Races.Count, gs.World.History.Races.Count);
        }

        [Test]
        public void SeededRecords_UpdateOnlyWhenStrictlyFaster_AndRemember_TheLegacyHolderAsThePreviousOne()
        {
            var h = new RaceHistory();
            h.Record(-1, RaceKind.Fall, new List<TeamResult> { Res(9, 1000, 1000, 1000, 1000, 1000, 1000) }, null);      // 第一戦の枠を作る(予熱の記録)
            h.SeedLegacyCourseRecords();
            int[] seed = RecordSeeds.CourseDs[0];
            // 区間 0 は同タイム(更新でない)・区間 1 は 1 ds 速い(更新)・残りは遅い
            var times = new int[] { seed[0], seed[1] - 1, seed[2] + 1, seed[3] + 100, seed[4] + 100, seed[5] + 100 };
            List<RecordUpdate> ups = h.Record(1, RaceKind.Fall, new List<TeamResult> { Res(4, times) }, new[] { new RaceTeam { Id = 4, Lineup = Enumerable.Range(0, 6).Select(i => new RaceRunner { RunnerId = 40 + i }).ToArray() } });
            RecordUpdate u = ups.Single(x => x.Section >= 0);
            Assert.AreEqual(1, u.Section); Assert.AreEqual(4, u.SchoolId); Assert.AreEqual(41, u.RunnerId); Assert.AreEqual(seed[1] - 1, u.TimeDs); Assert.AreEqual(1, u.Year);
            Assert.AreEqual(RecordSeeds.LegacySchoolId, u.PrevSchoolId); Assert.AreEqual(seed[1], u.PrevTimeDs); Assert.AreEqual(RecordSeeds.LegacyYear, u.PrevYear);
            Assert.AreEqual(RecordSeeds.LegacySchoolId, h.Course[0][0].SchoolId, "同タイムでは保持者が変わらない");
            Assert.AreEqual(4, h.Course[0][1].SchoolId);
            Assert.AreEqual(RecordSeeds.LegacySchoolId, h.Course[0][2].SchoolId);
            // 他の大会も初期値が入っている(大会の履歴がなくても、最初の 1 回は基準でなく更新になる)
            Assert.AreEqual(8, h.Course[1].Length);
            Assert.IsTrue(h.Course[1].All(c => c.SchoolId == RecordSeeds.LegacySchoolId));
        }

        private static TeamResult Res(int id, params int[] sections)
        {
            var t = new TeamResult { Id = id, Sections = sections, Cum = new int[sections.Length] };
            int sum = 0;
            for (int i = 0; i < sections.Length; i++) { sum += sections[i]; t.Cum[i] = sum; }
            t.TotalDs = sum;
            return t;
        }

        [Test]
        public void HistoryView_ShowsTheLegacyHolderUntilBeaten_AndItMatchesAnIndependentRecount()
        {
            GameSession gs = GameSession.New(31UL, Difficulty.Elite);
            HistoryView v0 = gs.History();
            Assert.AreEqual(24, v0.CourseRecords.Count);
            foreach (CourseRecordView c in v0.CourseRecords)
            {
                Assert.IsTrue(c.IsLegacy); Assert.AreEqual("record.legacy_holder", c.SchoolNameKey); Assert.IsFalse(c.IsOwn);
                Assert.AreEqual(0, c.RunnerId); Assert.AreEqual("", c.RunnerName);
                Assert.AreEqual(RecordSeeds.CourseDs[(int)c.Kind][c.Section], c.TimeDs); Assert.AreEqual(RecordSeeds.LegacyYear, c.Year);
            }
            // 3 年度進めたあと: 記録は『その間の全大会の最速』と『初期値』の小さい方(独立の数え直し)。初期値を超えない区間は『歴代の記録』のまま
            var best = new int[3][] { new int[6], new int[8], new int[10] };
            for (int k = 0; k < 3; k++) for (int si = 0; si < best[k].Length; si++) best[k][si] = RecordSeeds.CourseDs[k][si];
            WorldSim w = gs.World;
            int races = w.History.Races.Count;
            // 進める前の大会数を覚え、進めたあとの順位表では区間がわからないので、更新の列(RaceOutcome.Records)を拾う代わりに記録の単調性と整合を見る
            gs.PlayYears(3);
            HistoryView v = gs.History();
            int beaten = 0;
            foreach (CourseRecordView c in v.CourseRecords)
            {
                int seed = RecordSeeds.CourseDs[(int)c.Kind][c.Section];
                Assert.LessOrEqual(c.TimeDs, seed, "記録は初期値より遅くならない");
                if (c.TimeDs == seed)
                {
                    Assert.IsTrue(c.IsLegacy); Assert.AreEqual("record.legacy_holder", c.SchoolNameKey); Assert.AreEqual(RecordSeeds.LegacyYear, c.Year);
                }
                else
                {
                    beaten++;
                    Assert.IsFalse(c.IsLegacy); Assert.AreEqual("school." + c.SchoolId + ".name", c.SchoolNameKey); Assert.GreaterOrEqual(c.SchoolId, 0);
                    Assert.GreaterOrEqual(c.Year, 1, "更新は年度 1 から");
                }
            }
            Assert.Greater(beaten, 0, "試験の前提: 3 年度で初期値を破られる区間がある");
            Assert.Less(beaten, 24, "全部は破られない(破られにくい区間が残る)");
            Assert.Greater(w.History.Races.Count, races);
        }

        [Test]
        public void RaceResult_FirstRaceOfYear1_ReportsTheLegacyHolderAsThePreviousOne()
        {
            GameSession gs = GameSession.New(31UL, Difficulty.Elite);
            RaceOutcome race = null; RaceInfo? info = null;
            for (int w = 0; w < 48 && race == null; w++)
            {
                if (Array.IndexOf(Ekiden.Domain.Team.Calendar.RaceWeeks, gs.YearWeek) >= 0)
                {
                    gs.ResolveTraining();
                    race = gs.PlayWeekRaces(out info);
                    if (race != null) break;
                    gs.FinishWeek(); gs.AdvanceWeek();
                }
                else gs.StepWeek();
            }
            Assert.IsNotNull(race, "試験の前提: 年度 1 に大会がある");
            Assert.AreEqual(RaceKind.Fall, race.Kind, "年度 1 で最初の大会は第一戦");
            // 独立の数え直し: 区間ごとの最速チームの最速タイムが初期値より速い区間だけが更新
            int n = race.Results[0].Sections.Length;
            var expect = new List<int>();
            for (int si = 0; si < n; si++) if (race.Results.Min(t => t.Sections[si]) < RecordSeeds.CourseDs[0][si]) expect.Add(si);
            CollectionAssert.AreEqual(expect, race.Records.Where(u => u.Section >= 0).Select(u => u.Section).ToList());
            Assert.Greater(expect.Count, 0, "試験の前提: 最初の大会で初期値を破る区間がある");
            RaceResultView v = gs.RaceResult(race, info);
            foreach (RecordUpdateView rv in v.Records.Where(r => r.Section >= 0))
            {
                Assert.AreEqual(RecordSeeds.LegacySchoolId, rv.PrevSchoolId); Assert.IsTrue(rv.PrevIsLegacy);
                Assert.AreEqual("record.legacy_holder", rv.PrevSchoolNameKey); Assert.AreEqual(RecordSeeds.LegacyYear, rv.PrevYear);
                Assert.AreEqual(RecordSeeds.CourseDs[0][rv.Section], rv.PrevTimeDs);
            }
        }
    }
}
