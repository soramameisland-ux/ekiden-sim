using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>世界の履歴(02 §4.8・§4.9): 大会ごとの順位表と、区間記録・大会記録(04 §8.3。08b G-03・G-04)。</summary>
    public class RaceHistoryTests
    {
        private static TeamResult Res(int id, bool kuriage, params int[] sections)
        {
            var t = new TeamResult { Id = id, Sections = sections, Cum = new int[sections.Length], Kuriage = kuriage };
            int sum = 0;
            for (int i = 0; i < sections.Length; i++) { sum += sections[i]; t.Cum[i] = sum; }
            t.TotalDs = sum;
            return t;
        }

        private static RaceTeam Team(int id, params int[] runnerIds)
        {
            return new RaceTeam { Id = id, Lineup = runnerIds.Select(r => new RaceRunner { RunnerId = r }).ToArray() };
        }

        // 履歴は結果の長さだけを見る(Course[大会] を最初の結果の区間数で確保するので、同じ大会には同じ区間数で渡す)。
        [Test]
        public void FirstResult_SetsTheBaselineSilently_ThenOnlyStrictlyFasterTimesUpdate()
        {
            var h = new RaceHistory();
            // 1 回目: 基準(更新ではない)。保持者は区間ごとの最速チーム(同タイムは順位の先頭)
            var first = new List<TeamResult> { Res(3, false, 1000, 900), Res(1, false, 1000, 950) };
            List<RecordUpdate> u1 = h.Record(-5, RaceKind.Fall, first, new[] { Team(3, 30, 31), Team(1, 10, 11) });
            Assert.IsEmpty(u1);
            Assert.AreEqual(1, h.Races.Count);
            Assert.AreEqual(3, h.Course[0][0].SchoolId, "同タイムは順位の先頭(結果の並びの先頭)");
            Assert.AreEqual(30, h.Course[0][0].RunnerId);
            Assert.AreEqual(1900, h.Team[0].TotalDs);
            Assert.AreEqual(-5, h.Team[0].Year);
            // 2 回目: 区間 0 は同タイム(更新でない)、区間 1 は 1 ds 速い(更新)、大会記録は 1899 → 更新
            var second = new List<TeamResult> { Res(1, false, 1000, 899), Res(3, false, 1000, 950) };
            List<RecordUpdate> u2 = h.Record(-4, RaceKind.Fall, second, new[] { Team(1, 10, 11), Team(3, 30, 31) });
            Assert.AreEqual(2, u2.Count);
            RecordUpdate sec = u2[0];
            Assert.AreEqual(1, sec.Section); Assert.AreEqual(1, sec.SchoolId); Assert.AreEqual(11, sec.RunnerId); Assert.AreEqual(899, sec.TimeDs); Assert.AreEqual(-4, sec.Year);
            Assert.AreEqual(3, sec.PrevSchoolId); Assert.AreEqual(900, sec.PrevTimeDs); Assert.AreEqual(-5, sec.PrevYear);
            RecordUpdate team = u2[1];
            Assert.AreEqual(-1, team.Section); Assert.AreEqual(1, team.SchoolId); Assert.AreEqual(1899, team.TimeDs); Assert.AreEqual(1900, team.PrevTimeDs);
            Assert.AreEqual(3, h.Course[0][0].SchoolId, "同タイムでは保持者を替えない");
            Assert.AreEqual(1, h.Course[0][1].SchoolId);
            // 3 回目: 遅いだけ → 何も変わらない
            Assert.IsEmpty(h.Record(-3, RaceKind.Fall, new List<TeamResult> { Res(2, false, 1100, 1100) }, null));
            Assert.AreEqual(3, h.Races.Count);
            Assert.AreEqual(-4, h.Team[0].Year);
            // 4 回目: 大会記録と区間記録にちょうど並ぶだけ(同タイム)→ 更新でなく、保持者もそのまま
            Assert.IsEmpty(h.Record(-2, RaceKind.Fall, new List<TeamResult> { Res(5, false, 1000, 899) }, null));
            Assert.AreEqual(1, h.Team[0].SchoolId);
            Assert.AreEqual(-4, h.Team[0].Year);
            Assert.AreEqual(1, h.Course[0][1].SchoolId);
        }

        [Test]
        public void KindsAreSeparate_UnknownRunnersAreZero_AndKuriageWinnerDoesNotSetTheTeamRecord()
        {
            var h = new RaceHistory();
            h.Record(1, RaceKind.Fall, new List<TeamResult> { Res(1, false, 1000, 1000) }, null);
            Assert.IsNull(h.Course[1]);
            Assert.AreEqual(0, h.Course[0][0].RunnerId, "走者の ID を渡さなければ 0(他校の走者)");
            // 別の大会の記録とは混ざらない
            h.Record(1, RaceKind.National, new List<TeamResult> { Res(2, false, 5000, 5000, 5000) }, null);
            Assert.AreEqual(3, h.Course[1].Length);
            Assert.AreEqual(2, h.Team[1].SchoolId);
            Assert.AreEqual(1000, h.Course[0][0].TimeDs);
            // 先頭が繰り上げ(全チームが繰り上げ)の結果は、大会記録を作らない・更新しない。区間記録は走ったタイムなので更新される
            var h2 = new RaceHistory();
            List<RecordUpdate> u = h2.Record(1, RaceKind.NewYear, new List<TeamResult> { Res(1, true, 900, 900) }, null);
            Assert.IsNull(h2.Team[2]);
            Assert.IsEmpty(u);
            Assert.AreEqual(900, h2.Course[2][0].TimeDs);
            Assert.IsEmpty(new RaceHistory().Record(1, RaceKind.Fall, new List<TeamResult>(), null));
            Assert.IsEmpty(new RaceHistory().Record(1, RaceKind.Fall, null, null));
        }

        [Test]
        public void RaceEntry_KeepsTheStandingsInOrder_WithKuriage()
        {
            var h = new RaceHistory();
            h.Record(7, RaceKind.National, new List<TeamResult> { Res(4, false, 10, 10), Res(2, false, 11, 10), Res(9, true, 8, 30) }, null);
            RaceEntry e = h.Races.Single();
            Assert.AreEqual(7, e.Year);
            Assert.AreEqual(RaceKind.National, e.Kind);
            CollectionAssert.AreEqual(new[] { 4, 2, 9 }, e.Rows.Select(r => r.SchoolId).ToArray());
            CollectionAssert.AreEqual(new[] { 20, 21, 38 }, e.Rows.Select(r => r.TotalDs).ToArray());
            CollectionAssert.AreEqual(new[] { false, false, true }, e.Rows.Select(r => r.Kuriage).ToArray());
            Assert.AreEqual(4, e.WinnerId);
        }

        // ------------------------------------------------------------ 世界(独立の数え直し)
        [Test]
        public void World_HistoryMatchesAnIndependentRecount_FromTheRacesThemselves()
        {
            WorldSim w = WorldSim.Create(new KeyedRngFactory(2026UL), 3);
            Assert.AreEqual(9, w.History.Races.Count, "予熱 3 年度 × 3 大会");
            var outcomes = new List<(int year, RaceOutcome o)>();
            var updates = new List<RecordUpdate>();
            for (int year = 1; year <= 6; year++)
            {
                YearState st = w.BeginYear(year, null);
                w.PlayQualifier(st);
                foreach (RaceKind kind in WorldSim.AllRaces)
                {
                    RaceOutcome o = w.PlayRace(st, kind);
                    outcomes.Add((year, o));
                    updates.AddRange(o.Records);
                }
                YearResult res = w.EndYear(st);
                // 年度の結果の優勝校と、履歴の優勝校(先頭の行)が一致する
                foreach (RaceKind kind in WorldSim.AllRaces)
                    Assert.AreEqual(res.Winner[kind], w.History.Races.Last(e => e.Year == year && e.Kind == kind).WinnerId);
            }
            Assert.AreEqual(9 + 18, w.History.Races.Count);
            // 履歴の並び: 年度順・大会順
            var keys = w.History.Races.Select(e => e.Year * 10 + (int)e.Kind).ToList();
            CollectionAssert.AreEqual(keys.OrderBy(k => k).ToList(), keys);
            // 順位表は結果と同じ並び
            foreach (var (year, o) in outcomes)
            {
                RaceEntry e = w.History.Races.Single(x => x.Year == year && x.Kind == o.Kind);
                CollectionAssert.AreEqual(o.Results.Select(t => t.Id).ToArray(), e.Rows.Select(r => r.SchoolId).ToArray());
                CollectionAssert.AreEqual(o.Results.Select(t => t.TotalDs).ToArray(), e.Rows.Select(r => r.TotalDs).ToArray());
            }
            // 区間記録: 予熱のあとの 6 年度の最速が、基準(予熱の最速)より速ければ更新。数え直す(予熱の分は履歴の順位表からは区間がわからないので、基準を履歴から引く)
            // → ここでは『6 年度のうち、各区間の最速が現在の記録以上に速くなることはない』『更新の列が記録の変遷と一致する』を見る
            foreach (RaceKind kind in WorldSim.AllRaces)
            {
                int k = (int)kind;
                int n = RaceTables.Course(kind).Count;
                for (int si = 0; si < n; si++)
                {
                    int best = outcomes.Where(x => x.o.Kind == kind).Min(x => x.o.Results.Min(t => t.Sections[si]));
                    Assert.LessOrEqual(w.History.Course[k][si].TimeDs, best, "記録は、見えている最速以下");
                    var ups = updates.Where(u => u.Kind == kind && u.Section == si).ToList();
                    int prev = ups.Count > 0 ? ups[0].PrevTimeDs : w.History.Course[k][si].TimeDs;
                    foreach (RecordUpdate u in ups) { Assert.AreEqual(prev, u.PrevTimeDs, "更新は前の記録の続き"); Assert.Less(u.TimeDs, u.PrevTimeDs); prev = u.TimeDs; }
                    Assert.AreEqual(prev, w.History.Course[k][si].TimeDs);
                }
                // 大会記録 = 繰り上げのない優勝の最小(同タイムは先に出した方)
                var winners = w.History.Races.Where(e => e.Kind == kind && !e.Rows[0].Kuriage).ToList();
                int minTotal = winners.Min(e => e.Rows[0].TotalDs);
                RaceEntry first = winners.First(e => e.Rows[0].TotalDs == minTotal);
                Assert.AreEqual(minTotal, w.History.Team[k].TotalDs);
                Assert.AreEqual(first.Year, w.History.Team[k].Year);
                Assert.AreEqual(first.WinnerId, w.History.Team[k].SchoolId);
            }
        }

        [Test]
        public void World_FreshWorldAlreadyHasAHistory_AndPreviewsDoNotWriteIt()
        {
            WorldSim w = WorldSim.Create(new KeyedRngFactory(5UL), 4);
            Assert.AreEqual(12, w.History.Races.Count);
            foreach (RaceKind kind in WorldSim.AllRaces)
            {
                Assert.IsNotNull(w.History.Team[(int)kind]);
                Assert.IsTrue(w.History.Course[(int)kind].All(c => c != null));
                Assert.AreEqual(4, w.History.Races.Count(e => e.Kind == kind));
            }
            YearState st = w.BeginYear(1, null);
            w.PlayQualifier(st);
            int before = w.History.Races.Count;
            RaceOutcome o = w.PlayRace(st, RaceKind.Fall);
            Assert.AreEqual(before + 1, w.History.Races.Count);
            // 同じ世界を同じ手順で作り直すと同じ履歴
            WorldSim w2 = WorldSim.Create(new KeyedRngFactory(5UL), 4);
            WorldSim w3 = WorldSim.Create(new KeyedRngFactory(5UL), 4);
            CollectionAssert.AreEqual(w2.History.Races.Select(e => e.WinnerId).ToArray(), w3.History.Races.Select(e => e.WinnerId).ToArray());
            for (int k = 0; k < 3; k++)
            {
                CollectionAssert.AreEqual(w2.History.Course[k].Select(c => c.TimeDs).ToArray(), w3.History.Course[k].Select(c => c.TimeDs).ToArray());
                Assert.AreEqual(w2.History.Team[k].TotalDs, w3.History.Team[k].TotalDs);
            }
            Assert.AreEqual(12, w2.History.Races.Count, "作り直した世界の履歴の長さは予熱の分だけ");
        }
    }
}
