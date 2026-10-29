using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>ダイジェストの再生用のデータ(08 R-14。<c>RaceReplay</c>)。フルのレース進行(<c>RaceLive</c>)で見えたものと、独立に数え直した値と突き合わせる。</summary>
    public class RaceReplayTests
    {
        /// <summary>大会の週まで進めて、その週の大会の結果を返す(自校の有無は問わない)。</summary>
        private static RaceOutcome PlayRace(ulong seed, Difficulty d, RaceKind kind, out GameSession gs, out RaceInfo? own)
        {
            gs = GameSession.New(seed, d);
            while (true)
            {
                WeekOutcome wo = gs.StepWeek();
                if (wo.Race != null && wo.Race.Kind == kind) { own = wo.OwnRace; return wo.Race; }
            }
        }

        [Test]
        public void Steps_FollowTheSections_FlagsAndFinish()
        {
            foreach (RaceKind kind in new[] { RaceKind.Fall, RaceKind.National, RaceKind.NewYear })
            {
                GameSession gs;
                RaceInfo? own;
                RaceOutcome race = PlayRace(71UL, Difficulty.Elite, kind, out gs, out own);
                RaceReplayView v = gs.RaceReplay(race, own);
                int n = RaceTables.Course(kind).Count;
                Assert.AreEqual(n, v.Sections); Assert.AreEqual(n, v.Steps.Count); Assert.AreEqual(kind, v.Kind);
                Assert.AreEqual(kind == RaceKind.NewYear ? 4 : -1, v.HalfSection, "第三戦だけ 5 区が往路の終わり");
                for (int s = 0; s < n; s++)
                {
                    ReplayStepView st = v.Steps[s];
                    Assert.AreEqual(s, st.Section);
                    Assert.AreEqual(s == n - 1, st.IsFinal);
                    Assert.AreEqual(kind == RaceKind.NewYear && s == 4, st.IsHalfEnd);
                    Assert.LessOrEqual(st.Highlights.Count, 2, "区間ごとに最大 2 件(04 §9.3)");
                    Assert.AreEqual(s, st.Prize.Section);
                    Assert.IsTrue(st.Highlights.All(h => h.Section == s));
                    bool peak = st.Highlights.Any(h => h.Kind == HighlightKind.LeadChange || h.Kind == HighlightKind.CourseRecord || h.Kind == HighlightKind.TeamRecord);
                    Assert.AreEqual(peak, st.Peak, "山場 = 首位交代・区間記録・大会記録のどれかがある");
                }
                // フィニッシュ: 上位 3 校 + 自校(順位順)
                var expected = race.Results.Select((t, i) => new { t.Id, Rank = i + 1 }).Where(x => x.Rank <= 3 || x.Id == gs.School.Id).Select(x => x.Id).ToList();
                CollectionAssert.AreEqual(expected, v.Finish.Select(r => r.SchoolId).ToList());
                for (int i = 1; i < v.Finish.Count; i++) Assert.Less(v.Finish[i - 1].Rank, v.Finish[i].Rank);
                Assert.AreEqual(0, v.Finish[0].GapDs);
            }
        }

        [Test]
        public void Standings_AreTheRelayPointOrder_RecountedIndependently()
        {
            GameSession gs;
            RaceInfo? own;
            RaceOutcome race = PlayRace(72UL, Difficulty.Mid, RaceKind.National, out gs, out own);
            RaceReplayView v = gs.RaceReplay(race, own);
            for (int s = 0; s < v.Sections; s++)
            {
                // 独立に: 累積(同じなら校の ID の小さい方が上)で並べ直す
                var order = race.Results.OrderBy(t => t.Cum[s]).ThenBy(t => t.Id).ToList();
                int top = order[0].Cum[s];
                foreach (StandingLineView l in v.Steps[s].Standings)
                {
                    int idx = order.FindIndex(t => t.Id == l.SchoolId);
                    Assert.AreEqual(idx + 1, l.Rank, "中継所 " + s + " の順位");
                    Assert.AreEqual(order[idx].Cum[s], l.CumDs);
                    Assert.AreEqual(order[idx].Cum[s] - top, l.GapDs);
                    Assert.AreEqual(order[idx].Sections[s], l.SectionDs);
                    Assert.AreEqual(l.SchoolId == gs.School.Id, l.IsOwn);
                }
                var ranks = v.Steps[s].Standings.Select(l => l.Rank).ToList();
                CollectionAssert.AreEqual(ranks.OrderBy(x => x).ToList(), ranks, "順位順");
                Assert.AreEqual(1, v.Steps[s].Standings[0].Rank);
                // 先頭 3 校と、自校(出場していれば)の前後 1 校が入る
                int own0 = order.FindIndex(t => t.Id == gs.School.Id);
                var keep = new SortedSet<int> { 0, 1, 2 };
                if (own0 >= 0) for (int i = Math.Max(0, own0 - 1); i <= Math.Min(order.Count - 1, own0 + 1); i++) keep.Add(i);
                CollectionAssert.AreEqual(keep.Select(i => order[i].Id).ToList(), v.Steps[s].Standings.Select(l => l.SchoolId).ToList());
            }
        }

        [Test]
        public void ReplayAgreesWithTheLiveView_SectionBySection_WhenTheRaceIsPlayedByHand()
        {
            foreach (RaceKind kind in new[] { RaceKind.Fall, RaceKind.National })
            {
                GameSession gs = GameSession.New(75UL, Difficulty.Elite);
                while (gs.YearWeek != Calendar.RaceWeeks[(int)kind]) gs.StepWeek();
                Assert.IsTrue(gs.RaceThisWeek.HasValue, "この年度は " + kind + " に出る(試験の前提)");
                gs.ResolveTraining();
                RaceDay rd = gs.OpenRaceDay(kind);
                Assert.IsTrue(rd.Start().Ok);
                var live = new List<RaceLiveView>();
                for (int s = 0; s < rd.Sections; s++) { rd.Advance(); live.Add(gs.RaceLive(rd)); }
                RaceInfo? own;
                RaceOutcome race = gs.PlayWeekRaces(out own);
                RaceReplayView v = gs.RaceReplay(race, own);
                for (int s = 0; s < rd.Sections; s++)
                {
                    List<StandingLineView> a = live[s].Standings, b = v.Steps[s].Standings;
                    Assert.AreEqual(a.Count, b.Count, kind + " 中継所 " + s);
                    for (int i = 0; i < a.Count; i++)
                    {
                        Assert.AreEqual(a[i].SchoolId, b[i].SchoolId); Assert.AreEqual(a[i].Rank, b[i].Rank); Assert.AreEqual(a[i].CumDs, b[i].CumDs);
                        Assert.AreEqual(a[i].GapDs, b[i].GapDs); Assert.AreEqual(a[i].SectionDs, b[i].SectionDs); Assert.AreEqual(a[i].IsOwn, b[i].IsOwn); Assert.AreEqual(a[i].SeparatorBefore, b[i].SeparatorBefore);
                    }
                    // 見どころ: 進行中の見え方(区間 s までに見えたもの)のうち、区間 s のものが、再生の区間 s と同じ(未来が漏れない)
                    List<HighlightView> la = live[s].Highlights.Where(h => h.Section == s).ToList(), lb = v.Steps[s].Highlights;
                    Assert.AreEqual(la.Count, lb.Count, kind + " 見どころ 区間 " + s);
                    for (int i = 0; i < la.Count; i++)
                    {
                        Assert.AreEqual(la[i].Kind, lb[i].Kind); Assert.AreEqual(la[i].SchoolId, lb[i].SchoolId); Assert.AreEqual(la[i].Ds, lb[i].Ds);
                        Assert.AreEqual(la[i].RankDelta, lb[i].RankDelta); Assert.AreEqual(la[i].Percent, lb[i].Percent, 1e-12); Assert.AreEqual(la[i].IsOwn, lb[i].IsOwn);
                    }
                }
            }
        }

        [Test]
        public void OwnLegsAndPrizes_AreTheResultsView_AndANonEntrantGetsNoOwnRows()
        {
            GameSession gs;
            RaceInfo? own;
            RaceOutcome race = PlayRace(73UL, Difficulty.Elite, RaceKind.Fall, out gs, out own);
            RaceResultView rv = gs.RaceResult(race, own);
            RaceReplayView v = gs.RaceReplay(race, own);
            Assert.IsTrue(v.OwnEntered); Assert.AreEqual(rv.OwnRank, v.OwnRank);
            for (int s = 0; s < v.Sections; s++)
            {
                Assert.AreEqual(rv.OwnLegs[s].Ds, v.Steps[s].OwnLeg.Ds); Assert.AreEqual(rv.OwnLegs[s].SectionRank, v.Steps[s].OwnLeg.SectionRank);
                Assert.AreEqual(rv.OwnLegs[s].CumRank, v.Steps[s].OwnLeg.CumRank);
                Assert.AreEqual(rv.Prizes[s].SchoolId, v.Steps[s].Prize.SchoolId); Assert.AreEqual(rv.Prizes[s].Ds, v.Steps[s].Prize.Ds);
                // 自校の累積順位は、その中継所の順位表の自校と同じ
                StandingLineView me = v.Steps[s].Standings.Single(l => l.IsOwn);
                Assert.AreEqual(v.Steps[s].OwnLeg.CumRank, me.Rank);
                Assert.AreEqual(v.Steps[s].OwnLeg.CumDs, me.CumDs);
            }
            // 出場しなかった大会(同好会の最初の年度の第一戦)
            GameSession club = GameSession.New(74UL, Difficulty.Club);
            RaceOutcome r2 = null; RaceInfo? o2 = null;
            for (int w = 0; w < 48 && r2 == null; w++) { WeekOutcome wo = club.StepWeek(); if (wo.Race != null && wo.OwnRace == null) { r2 = wo.Race; o2 = wo.OwnRace; } }
            Assert.IsNotNull(r2, "試験の前提: 同好会が出ない大会がある");
            RaceReplayView nv = club.RaceReplay(r2, o2);
            Assert.IsFalse(nv.OwnEntered); Assert.AreEqual(0, nv.OwnRank);
            Assert.IsTrue(nv.Steps.All(st => st.OwnLeg == null && st.Standings.All(l => !l.IsOwn)));
            Assert.AreEqual(3, nv.Steps[0].Standings.Count, "自校がなければ先頭 3 校だけ");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, nv.Finish.Select(r => r.Rank).ToList());
        }

        [Test]
        public void ReplayIsPureReading_AndAverageHighlightsFillTheSlots()
        {
            GameSession gs;
            RaceInfo? own;
            RaceOutcome race = PlayRace(76UL, Difficulty.Mid, RaceKind.NewYear, out gs, out own);
            string before = string.Join(",", race.Results.Select(t => t.TotalDs));
            RaceReplayView a = gs.RaceReplay(race, own), b = gs.RaceReplay(race, own);
            Assert.AreEqual(before, string.Join(",", race.Results.Select(t => t.TotalDs)), "結果は変わらない");
            Assert.AreEqual(a.Steps.Sum(s => s.Highlights.Count), b.Steps.Sum(s => s.Highlights.Count), "何度呼んでも同じ");
            // 区間賞は毎区間あるので、見どころは 1 件以上(08 R-14.4 の実測 = ほぼ毎区間 2 件で埋まる)
            Assert.IsTrue(a.Steps.All(s => s.Highlights.Count >= 1), "毎区間 1 件以上");
            Assert.Throws<ArgumentNullException>(() => gs.RaceReplay(null));
        }
    }
}
