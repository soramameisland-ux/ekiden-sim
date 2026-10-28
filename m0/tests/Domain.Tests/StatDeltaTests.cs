using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>能力の増減(SC-19。08b G-14): 今月のここまでと前の月の増減。月の初めの能力をテストの側で記録して突き合わせる。</summary>
    public class StatDeltaTests
    {
        [Test]
        public void Deltas_MatchTheStatsAtTheStartOfEachMonth_AsRecordedFromOutside()
        {
            GameSession gs = GameSession.New(33UL, Difficulty.Elite);
            var startOfMonth = new Dictionary<int, List<int[]>>();                 // 選手 ID → 月の初めの能力の履歴
            int checkedDeltas = 0, nonZeroThis = 0, nonZeroLast = 0;
            for (int w = 0; w < 48 * 3; w++)
            {
                if (gs.YearWeek % 4 == 0)
                    foreach (Runner r in gs.State.Runners)
                    {
                        if (!startOfMonth.ContainsKey(r.Id)) startOfMonth[r.Id] = new List<int[]>();
                        startOfMonth[r.Id].Add((int[])r.Stats.Clone());
                    }
                foreach (Runner r in gs.State.Runners.Take(6))
                {
                    RunnerDetailView d = gs.RunnerDetail(r.Id);
                    List<int[]> h = startOfMonth[r.Id];
                    int[] now = r.Stats, start = h[h.Count - 1], prev = h.Count >= 2 ? h[h.Count - 2] : h[h.Count - 1];
                    for (int i = 0; i < StatInfo.Count; i++)
                    {
                        Assert.AreEqual(now[i] - start[i], d.Stats[i].DeltaThisMonth, "週 " + w + " 選手 " + r.Id + " 能力 " + i);
                        Assert.AreEqual(start[i] - prev[i], d.Stats[i].DeltaLastMonth);
                        checkedDeltas++;
                        if (d.Stats[i].DeltaThisMonth != 0) nonZeroThis++;
                        if (d.Stats[i].DeltaLastMonth != 0) nonZeroLast++;
                    }
                }
                gs.StepWeek();
            }
            Assert.Greater(checkedDeltas, 3000);
            Assert.Greater(nonZeroThis, 50, "試験の前提: 今月の増減が実際に出る");
            Assert.Greater(nonZeroLast, 50, "試験の前提: 前の月の増減が実際に出る");
        }

        [Test]
        public void NewRunners_StartWithZeroDeltas()
        {
            GameSession gs = GameSession.New(34UL, Difficulty.Mid);
            gs.PlayYears(1);                                                       // 年度末の卒業・入学のあと: 新入生が入っている
            foreach (Runner r in gs.State.Runners.Where(x => x.Grade == 1))
            {
                RunnerDetailView d = gs.RunnerDetail(r.Id);
                Assert.IsTrue(d.Stats.All(s => s.DeltaThisMonth == 0 && s.DeltaLastMonth == 0), "新入生の増減は 0");
            }
        }

        [Test]
        public void RollMonthStats_ShiftsTheBaselines_AndCloneCopiesThem()
        {
            var r = new Runner();
            for (int i = 0; i < StatInfo.Count; i++) r.Stats[i] = 50 + i;
            Assert.IsNull(r.MonthStartStats);
            r.RollMonthStats();
            CollectionAssert.AreEqual(r.Stats, r.MonthStartStats);
            CollectionAssert.AreEqual(r.Stats, r.PrevMonthStats, "最初に送るときの前の月は、いまの能力(増減 0)");
            for (int i = 0; i < StatInfo.Count; i++) r.Stats[i] += 2;
            r.RollMonthStats();
            Assert.AreEqual(50, r.PrevMonthStats[0]);
            Assert.AreEqual(52, r.MonthStartStats[0]);
            Runner c = r.Clone();
            CollectionAssert.AreEqual(r.MonthStartStats, c.MonthStartStats);
            CollectionAssert.AreEqual(r.PrevMonthStats, c.PrevMonthStats);
            c.MonthStartStats[0] = 99;
            Assert.AreEqual(52, r.MonthStartStats[0], "複製は元と独立");
        }
    }
}
