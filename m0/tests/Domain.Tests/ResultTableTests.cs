using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>結果の画面(SC-26)の区間成績の表と順位推移のグラフのデータ(<c>RaceResultView.Teams</c>)。全校の結果から独立に数え直した値と突き合わせる。</summary>
    public class ResultTableTests
    {
        private static RaceOutcome PlayRace(ulong seed, Difficulty d, RaceKind kind, out GameSession gs, out RaceInfo? own, int fromYear = 1)
        {
            gs = GameSession.New(seed, d);
            while (true)
            {
                WeekOutcome wo = gs.StepWeek();
                if (wo.Race != null && wo.Race.Kind == kind) { own = wo.OwnRace; return wo.Race; }
            }
        }

        [Test]
        public void Table_IsTheTopTenPlusOwn_InOverallOrder_AndMatchesTheOverallRows()
        {
            foreach (RaceKind kind in new[] { RaceKind.Fall, RaceKind.National, RaceKind.NewYear })
            {
                GameSession gs;
                RaceInfo? own;
                RaceOutcome race = PlayRace(71UL, Difficulty.Elite, kind, out gs, out own);
                RaceResultView v = gs.RaceResult(race, own);
                Assert.AreEqual(race.Results.Count, v.FieldSize);
                int top = GameSession.ResultTableTop;
                Assert.AreEqual(10, top);
                int expected = Math.Min(top, v.Overall.Count) + (v.OwnEntered && v.OwnRank > top ? 1 : 0);
                Assert.AreEqual(expected, v.Teams.Count, kind.ToString());
                for (int i = 0; i < v.Teams.Count; i++)
                {
                    ResultTeamView t = v.Teams[i];
                    ResultRowView o = v.Overall.Single(r => r.SchoolId == t.SchoolId);
                    Assert.AreEqual(o.Rank, t.Rank); Assert.AreEqual(o.TotalDs, t.TotalDs); Assert.AreEqual(o.GapDs, t.GapDs); Assert.AreEqual(o.Kuriage, t.Kuriage); Assert.AreEqual(o.IsOwn, t.IsOwn);
                    Assert.AreEqual(v.Sections, t.Sections.Count);
                    Assert.AreEqual(i >= top, t.SeparatorBefore);
                    if (i > 0) Assert.Less(v.Teams[i - 1].Rank, t.Rank, "総合順位の順");
                }
                Assert.AreEqual(1, v.Teams[0].Rank);
            }
        }

        [Test]
        public void Cells_AreRecountedIndependently_FromTheTeamResults()
        {
            GameSession gs;
            RaceInfo? own;
            RaceOutcome race = PlayRace(72UL, Difficulty.Mid, RaceKind.National, out gs, out own);
            RaceResultView v = gs.RaceResult(race, own);
            List<TeamResult> res = race.Results;
            foreach (ResultTeamView t in v.Teams)
            {
                TeamResult r = res.Single(x => x.Id == t.SchoolId);
                for (int s = 0; s < v.Sections; s++)
                {
                    ResultSectionCellView c = t.Sections[s];
                    int secRank = 1, cumRank = 1, lead = int.MaxValue;
                    foreach (TeamResult o in res)
                    {
                        if (o.Sections[s] < r.Sections[s]) secRank++;
                        if (o.Cum[s] < r.Cum[s]) cumRank++;
                        lead = Math.Min(lead, o.Cum[s]);
                    }
                    Assert.AreEqual(r.Sections[s], c.Ds); Assert.AreEqual(secRank, c.SectionRank, "区間順位 " + s); Assert.AreEqual(r.Cum[s], c.CumDs);
                    Assert.AreEqual(cumRank, c.CumRank, "累積順位 " + s); Assert.AreEqual(r.Cum[s] - lead, c.GapDs);
                    Assert.AreEqual(secRank == 1, c.Prize);
                    Assert.AreEqual(r.Events.Any(e => e.Section == s && e.Kind == "brake"), c.Brake);
                    Assert.AreEqual(r.Events.Any(e => e.Section == s && e.Kind == "stall"), c.Stall);
                    Assert.GreaterOrEqual(c.CumRank, 1); Assert.LessOrEqual(c.CumRank, v.FieldSize);
                }
            }
            // 優勝校の最後の中継所の累積順位は 1。総合順位とグラフの終点がつながる
            foreach (ResultTeamView t in v.Teams.Where(x => x.Rank <= 3)) Assert.AreEqual(t.Rank, t.Sections[v.Sections - 1].CumRank, "終点 = 総合順位(同タイムなし)");
            // 区間賞は Prizes と同じ
            for (int s = 0; s < v.Sections; s++)
                foreach (ResultTeamView t in v.Teams.Where(x => x.Sections[s].Prize)) Assert.AreEqual(v.Prizes[s].Ds, t.Sections[s].Ds);
        }

        [Test]
        public void OwnRow_IsTheSameAsTheOwnLegs()
        {
            GameSession gs;
            RaceInfo? own;
            RaceOutcome race = PlayRace(73UL, Difficulty.Elite, RaceKind.Fall, out gs, out own);
            RaceResultView v = gs.RaceResult(race, own);
            Assert.IsTrue(v.OwnEntered);
            ResultTeamView me = v.Teams.Single(t => t.IsOwn);
            for (int s = 0; s < v.Sections; s++)
            {
                Assert.AreEqual(v.OwnLegs[s].Ds, me.Sections[s].Ds); Assert.AreEqual(v.OwnLegs[s].SectionRank, me.Sections[s].SectionRank);
                Assert.AreEqual(v.OwnLegs[s].CumDs, me.Sections[s].CumDs); Assert.AreEqual(v.OwnLegs[s].CumRank, me.Sections[s].CumRank);
                Assert.AreEqual(v.OwnLegs[s].Brake, me.Sections[s].Brake); Assert.AreEqual(v.OwnLegs[s].Stall, me.Sections[s].Stall); Assert.AreEqual(v.OwnLegs[s].Prize, me.Sections[s].Prize);
            }
        }

        /// <summary>境目: 自校が総合 10 位なら上位に含まれる(区切りなし・11 行にならない)/ 11 位なら最後に区切りつきで足される。</summary>
        [Test]
        public void TheBoundary_TenthIsInsideTheTable_EleventhIsAddedWithASeparator()
        {
            int seenTenth = 0, seenEleventh = 0;
            foreach (Difficulty d in new[] { Difficulty.Mid, Difficulty.Low, Difficulty.Club })
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    GameSession gs = GameSession.New(seed * 104729UL, d);
                    for (int w = 0; w < 48 * 8; w++)
                    {
                        WeekOutcome wo = gs.StepWeek();
                        if (wo.Race == null || wo.OwnRace == null) continue;
                        RaceResultView v = gs.RaceResult(wo.Race, wo.OwnRace);
                        if (v.OwnRank == 10)
                        {
                            seenTenth++;
                            Assert.AreEqual(10, v.Teams.Count); Assert.IsTrue(v.Teams[9].IsOwn); Assert.IsFalse(v.Teams[9].SeparatorBefore);
                        }
                        else if (v.OwnRank == 11)
                        {
                            seenEleventh++;
                            Assert.AreEqual(11, v.Teams.Count); Assert.IsTrue(v.Teams[10].IsOwn); Assert.IsTrue(v.Teams[10].SeparatorBefore);
                        }
                    }
                }
            Assert.Greater(seenTenth, 0, "試験の前提: 10 位の大会がある");
            Assert.Greater(seenEleventh, 0, "試験の前提: 11 位の大会がある");
        }

        [Test]
        public void OwnOutsideTheTopTen_IsAddedLastWithASeparator_AndANonEntrantAddsNothing()
        {
            // 自校が 10 位の外の大会を探す(同好会・弱小の第一戦 / 第二戦)
            bool found = false;
            foreach (Difficulty d in new[] { Difficulty.Low, Difficulty.Club })
                for (ulong seed = 1; seed <= 6 && !found; seed++)
                {
                    GameSession gs = GameSession.New(seed * 7919UL, d);
                    for (int w = 0; w < 48 * 6 && !found; w++)
                    {
                        WeekOutcome wo = gs.StepWeek();
                        if (wo.Race == null || wo.OwnRace == null) continue;
                        RaceResultView v = gs.RaceResult(wo.Race, wo.OwnRace);
                        if (v.OwnRank <= GameSession.ResultTableTop) continue;
                        found = true;
                        Assert.AreEqual(GameSession.ResultTableTop + 1, v.Teams.Count);
                        ResultTeamView last = v.Teams[v.Teams.Count - 1];
                        Assert.IsTrue(last.IsOwn); Assert.IsTrue(last.SeparatorBefore); Assert.AreEqual(v.OwnRank, last.Rank);
                        Assert.IsTrue(v.Teams.Take(GameSession.ResultTableTop).All(t => !t.SeparatorBefore && !t.IsOwn));
                    }
                }
            Assert.IsTrue(found, "試験の前提: 自校が 10 位の外の大会がある");
            // 出場しなかった大会: 上位 10 校だけ
            GameSession club = GameSession.New(74UL, Difficulty.Club);
            for (int w = 0; w < 48; w++)
            {
                WeekOutcome wo = club.StepWeek();
                if (wo.Race != null && wo.OwnRace == null)
                {
                    RaceResultView v = club.RaceResult(wo.Race, wo.OwnRace);
                    Assert.IsFalse(v.OwnEntered);
                    Assert.AreEqual(GameSession.ResultTableTop, v.Teams.Count); Assert.IsTrue(v.Teams.All(t => !t.IsOwn && !t.SeparatorBefore));
                    return;
                }
            }
            Assert.Fail("試験の前提: 同好会が出ない大会がある");
        }
    }
}
