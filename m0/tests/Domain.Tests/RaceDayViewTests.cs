using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>大会の当日(SC-24)とレース進行(SC-25)の読み取り API(08b)。</summary>
    public class RaceDayViewTests
    {
        private static RaceDay Open(ulong seed, Difficulty d, RaceKind kind, out GameSession gs)
        {
            gs = GameSession.New(seed, d);
            while (gs.YearWeek != Calendar.RaceWeeks[(int)kind]) gs.StepWeek();
            Assert.IsTrue(gs.RaceThisWeek.HasValue, "この年度は " + kind + " に出る(試験の前提)");
            gs.ResolveTraining();
            return gs.OpenRaceDay(kind);
        }

        [Test]
        public void Day_MirrorsTheRaceDay_AndTheBenchDeltasAreExpectedTimeDifferences()
        {
            GameSession gs;
            RaceDay rd = Open(71UL, Difficulty.Elite, RaceKind.Fall, out gs);
            RaceDayView v = gs.RaceDayView(rd);
            Assert.AreEqual(RaceKind.Fall, v.Kind);
            Assert.AreEqual(rd.Sections, v.Sections);
            Assert.AreEqual(rd.Sections, v.Legs.Count);
            Assert.AreEqual(rd.Environment.Length, v.Env.Count);
            for (int i = 0; i < v.Env.Count; i++) { Assert.AreEqual(rd.Environment[i].Temp, v.Env[i].Temp); Assert.AreEqual(rd.Environment[i].Wind, v.Env[i].Wind); Assert.AreEqual(rd.Environment[i].Rain, v.Env[i].Rain); }
            Assert.AreEqual(rd.SwapLimit, v.SwapLimit);
            Assert.AreEqual(rd.SwapsLeft, v.SwapsLeft);
            Assert.AreEqual(rd.GekiLimit, v.GekiLimit);
            Assert.AreEqual(rd.GekiLeft, v.GekiLeft);
            Assert.IsFalse(v.Started);
            for (int i = 0; i < v.Legs.Count; i++)
            {
                LegView l = rd.View(i);
                DayLegView d = v.Legs[i];
                Assert.AreEqual(i, d.Section);
                Assert.AreEqual(l.Fit, d.Fit);
                Assert.AreEqual(l.FitMark, d.FitMark);
                Assert.AreEqual(l.PaceMark, d.PaceMark);
                Assert.AreEqual(l.ExpectedSec, d.ExpectedSec);
                Assert.AreEqual(l.Recommended, d.Recommended);
                Assert.AreEqual(l.Instr, d.Instr);
                Assert.AreEqual(l.Distance, d.Distance);
                Assert.AreEqual(l.Type, d.Type);
                Assert.AreEqual(l.Runner.Form, d.Form);
                Assert.AreEqual(l.Type == SectionType.Climb || l.Type == SectionType.Descent, d.Mountain);
                Assert.IsFalse(d.Passed);
                if (l.Runner.RunnerId != 0) Assert.AreEqual(l.Runner.RunnerId, d.Runner.Id);
            }
            Assert.AreEqual(rd.Bench.Count, v.Bench.Count);
            for (int b = 0; b < v.Bench.Count; b++)
            {
                BenchRunnerView bv = v.Bench[b];
                Assert.AreEqual(b, bv.BenchIndex);
                Assert.AreEqual(rd.Bench[b].Form, bv.Form);
                Assert.AreEqual(DisplayRules.RankLetter(rd.Bench[b].Stats[(int)Stat.STA]), bv.HeatRank);
                Assert.AreEqual(DisplayRules.RankLetter(rd.Bench[b].Stats[(int)Stat.PAC]), bv.WindRank);
                Assert.AreEqual(DisplayRules.RankLetter(rd.Bench[b].Stats[(int)Stat.CON]), bv.RainRank);
                for (int i = 0; i < rd.Sections; i++) Assert.AreEqual(rd.BenchExpectedSec(i, b) - rd.View(i).ExpectedSec, bv.DeltaSec[i], 1e-9);
            }
            List<TeamResult> res = rd.Preview();
            Assert.AreEqual(res.IndexOf(rd.OwnResult(res)) + 1, v.ExpectedRank);
            Assert.AreEqual(rd.OwnResult(res).TotalDs, v.ExpectedTotalDs);
        }

        [Test]
        public void Day_FollowsSwapsAndInstructions()
        {
            GameSession gs;
            RaceDay rd = Open(73UL, Difficulty.Elite, RaceKind.NewYear, out gs);
            Assert.IsTrue(rd.ResetSwaps().Ok);
            RaceDayView before = gs.RaceDayView(rd);
            Assert.Greater(before.Bench.Count, 0);
            int section = 5;
            string nameBefore = before.Legs[section].Name;
            Assert.IsTrue(rd.Swap(section, 0).Ok);
            RaceDayView after = gs.RaceDayView(rd);
            Assert.IsTrue(after.Legs[section].Swapped);
            Assert.AreEqual(before.SwapsLeft - 1, after.SwapsLeft);
            Assert.AreEqual(before.Bench[0].Name, after.Legs[section].Name, "控えが先発に入る");
            Assert.AreEqual(nameBefore, after.Bench.Single(b => b.Name == nameBefore).Name, "入れ替わった先発は控えに回る");
            Assert.IsTrue(after.Touched);
            Assert.IsTrue(rd.ApplyGauge().Ok);
            RaceDayView gauge = gs.RaceDayView(rd);
            foreach (DayLegView d in gauge.Legs) Assert.AreEqual(d.Recommended, d.Instr, "目安どおりの指示");
            Assert.IsTrue(rd.SetInstruction(2, Instruction.Attack).Ok);
            Assert.AreEqual(Instruction.Attack, gs.RaceDayView(rd).Legs[2].Instr);
            Assert.AreEqual(2, gs.RaceDayView(rd).Env.Count, "第三戦は往路と復路");
        }

        // ------------------------------------------------------------ レース進行
        [Test]
        public void Live_FollowsTheRace_WithCondensedStandings()
        {
            GameSession gs;
            RaceDay rd = Open(75UL, Difficulty.Elite, RaceKind.National, out gs);
            RaceLiveView v0 = gs.RaceLive(rd);
            Assert.IsFalse(v0.Started);
            Assert.AreEqual(0, v0.Resolved);
            Assert.IsEmpty(v0.Standings, "まだ中継所を通過していない");
            Assert.IsNull(v0.IncidentKind, "始まる前は異変がない");
            Assert.AreEqual(0, v0.Next.Section);
            Assert.IsTrue(rd.Start().Ok);
            for (int s = 0; s < rd.Sections; s++)
            {
                RaceLiveView before = gs.RaceLive(rd);
                Assert.IsTrue(before.Started);
                Assert.AreEqual(s, before.Resolved);
                Assert.IsFalse(before.Finished);
                Assert.AreEqual(s, before.Next.Section);
                Incident inc = rd.NextIncident();
                Assert.AreEqual(inc == null ? null : inc.Kind, before.IncidentKind);
                if (inc != null) { Assert.AreEqual(inc.Loss, before.IncidentLoss); Assert.AreEqual(inc.Pos, before.IncidentPos); }
                Assert.AreEqual(rd.GekiLeft, before.GekiLeft);
                List<StandingRow> rows = rd.Advance();
                RaceLiveView after = gs.RaceLive(rd);
                Assert.AreEqual(s + 1, after.Resolved);
                Assert.IsTrue(after.Next == null || after.Next.Section == s + 1);
                // 順位表: 先頭 3 校 + 自校の前後 1 校
                int own = rows.FindIndex(r => r.IsOwn);
                var expected = new SortedSet<int> { 0, 1, 2 };
                for (int i = Math.Max(0, own - 1); i <= Math.Min(rows.Count - 1, own + 1); i++) expected.Add(i);
                CollectionAssert.AreEqual(expected.Select(i => rows[i].TeamId).ToList(), after.Standings.Select(r => r.SchoolId).ToList());
                Assert.LessOrEqual(after.Standings.Count, 6);
                Assert.AreEqual(1, after.Standings.Count(r => r.IsOwn));
                foreach (StandingLineView l in after.Standings)
                {
                    StandingRow r = rows.Single(x => x.TeamId == l.SchoolId);
                    Assert.AreEqual(r.Rank, l.Rank);
                    Assert.AreEqual(r.CumDs, l.CumDs);
                    Assert.AreEqual(r.GapDs, l.GapDs);
                    Assert.AreEqual(r.SectionDs, l.SectionDs);
                    Assert.AreEqual("school." + l.SchoolId + ".name", l.NameKey);
                }
                Assert.AreEqual(1, after.Standings[0].Rank);
                Assert.AreEqual(0, after.Standings[0].GapDs);
            }
            RaceLiveView done = gs.RaceLive(rd);
            Assert.IsTrue(done.Finished);
            Assert.IsNull(done.Next);
            Assert.IsNull(done.IncidentKind);
        }

        [Test]
        public void Live_ShowsIncidentsAcrossManyRaces_AndGekiReducesTheCount()
        {
            int incidents = 0, gekiUsed = 0;
            for (ulong seed = 80; seed < 84; seed++)
            {
                GameSession gs;
                RaceDay rd = Open(seed, Difficulty.Mid, RaceKind.NewYear, out gs);
                rd.Start();
                while (rd.Resolved < rd.Sections)
                {
                    RaceLiveView v = gs.RaceLive(rd);
                    if (v.IncidentKind != null)
                    {
                        incidents++;
                        Assert.That(v.IncidentKind, Is.EqualTo("brake").Or.EqualTo("stall"));
                        Assert.Greater(v.IncidentLoss, 0);
                        Assert.That(v.IncidentPos, Is.EqualTo(1.0 / 3).Within(1e-9).Or.EqualTo(2.0 / 3).Within(1e-9));
                        if (v.GekiLeft > 0)
                        {
                            Assert.IsTrue(rd.UseGeki().Ok);
                            rd.Advance();
                            Assert.AreEqual(v.GekiLeft - 1, gs.RaceLive(rd).GekiLeft, "檄を使うと残りが 1 減る");
                            gekiUsed++;
                            continue;
                        }
                    }
                    rd.Advance();
                }
            }
            Assert.Greater(incidents, 0);
            Assert.Greater(gekiUsed, 0);
        }

        [Test]
        public void CondenseStandings_KeepsTheTopThreeAndTheNeighboursOfTheOwnSchool()
        {
            Func<int, string> key = id => "k" + id;
            foreach (int ownAt in new[] { 0, 1, 2, 3, 4, 5, 8, 11 })
            {
                var rows = Enumerable.Range(0, 12).Select(i => new StandingRow { TeamId = 100 + i, Rank = i + 1, CumDs = 1000 + 5 * i, GapDs = 5 * i, SectionDs = 300, IsOwn = i == ownAt }).ToList();
                List<StandingLineView> lines = GameSession.CondenseStandings(rows, key);
                var want = new SortedSet<int> { 0, 1, 2, ownAt - 1, ownAt, ownAt + 1 };
                want.RemoveWhere(i => i < 0 || i > 11);
                CollectionAssert.AreEqual(want.Select(i => 100 + i).ToList(), lines.Select(l => l.SchoolId).ToList(), "自校 " + (ownAt + 1) + " 位");
                int prev = -2;
                foreach (StandingLineView l in lines)
                {
                    int idx = l.SchoolId - 100;
                    Assert.AreEqual(prev >= 0 && idx != prev + 1, l.SeparatorBefore, "区切り: 自校 " + (ownAt + 1) + " 位の " + (idx + 1) + " 位の手前");
                    prev = idx;
                }
                Assert.AreEqual(1, lines.Count(l => l.IsOwn));
            }
            Assert.AreEqual(2, GameSession.CondenseStandings(new[] { new StandingRow { TeamId = 1, Rank = 1, IsOwn = true }, new StandingRow { TeamId = 2, Rank = 2 } }, key).Count, "校数が少なければある分だけ");
        }
    }
}
