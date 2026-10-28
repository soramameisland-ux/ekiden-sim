using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>月末レポート(SC-11)の読み取りの API(08b・08 R-10)。月の初めからの変化と、ランクアップ・成長・調子の項目が、実際の変化と食い違わない。</summary>
    public class MonthViewTests
    {
        private sealed class Start
        {
            public Dictionary<int, int[]> Stats;
            public Dictionary<int, int> Form;
            public double Fatigue, FormAvg, Morale;
            public HashSet<int> Injured;
        }

        private static Start Snapshot(GameSession gs)
        {
            var rs = gs.State.Runners;
            return new Start
            {
                Stats = rs.ToDictionary(r => r.Id, r => (int[])r.Stats.Clone()), Form = rs.ToDictionary(r => r.Id, r => r.Form),
                Fatigue = rs.Average(r => (double)r.Fatigue), FormAvg = rs.Average(r => (double)r.Form), Morale = gs.State.Morale,
                Injured = new HashSet<int>(rs.Where(r => r.IsInjured).Select(r => r.Id)),
            };
        }

        [Test]
        public void Report_MatchesTheMonthsActualChanges()
        {
            const ulong seed = 20260403UL;
            GameSession a = GameSession.New(seed, Difficulty.Mid);
            GameSession b = GameSession.New(seed, Difficulty.Mid);                  // おまかせ(StepWeek)と同じ道。月の初めの状態を独立に取る
            var flow = new GameFlow(a, autoUpgrade: true, autoTickets: true);       // 強化と招聘の宣言もおまかせにすれば、流れ = StepWeek
            int rankUps = 0, growths = 0, formUps = 0, months = 0;
            for (int m = 0; m < 24; m++)
            {
                Start start = Snapshot(b);
                Stop s = flow.Advance();
                while (s.Kind != StopKind.MonthReport) s = flow.Advance();
                MonthReportView v = a.MonthReportView(s.Report);
                for (int w = 0; w < 4; w++) b.StepWeek();

                Assert.AreEqual(m % 12, v.MonthIndex);
                Assert.AreEqual((3 + m % 12) % 12 + 1, v.CalendarMonth);
                Assert.IsTrue(v.ChangeKnown);
                double fat = a.State.Runners.Average(r => (double)r.Fatigue), form = a.State.Runners.Average(r => (double)r.Form);
                Assert.AreEqual(fat, v.AvgFatigue, 1e-9);
                Assert.AreEqual(form, v.AvgForm, 1e-9);
                Assert.AreEqual(a.State.Morale, v.Morale, 1e-9);
                Assert.AreEqual(fat - start.Fatigue, v.AvgFatigueChange, 1e-9, "疲労の変化 = 月末 − 月の初め");
                Assert.AreEqual(form - start.FormAvg, v.AvgFormChange, 1e-9);
                Assert.AreEqual(a.State.Morale - start.Morale, v.MoraleChange, 1e-9);
                CollectionAssert.AreEqual(a.State.MonthlyPlan, v.ThemeIds);

                var expectedRank = new HashSet<string>();
                var expectedGrowth = new Dictionary<int, int>();
                var expectedForm = new Dictionary<int, int>();
                foreach (Runner r in a.State.Runners)
                {
                    int[] was;
                    if (!start.Stats.TryGetValue(r.Id, out was)) continue;
                    int total = 0;
                    for (int i = 0; i < was.Length; i++)
                    {
                        total += r.Stats[i] - was[i];
                        if (DisplayRules.RankValue(DisplayRules.RankLetter(r.Stats[i])) > DisplayRules.RankValue(DisplayRules.RankLetter(was[i]))) expectedRank.Add(r.Id + ":" + i);
                    }
                    if (total >= 3) expectedGrowth[r.Id] = total;
                    if (r.Form > start.Form[r.Id]) expectedForm[r.Id] = r.Form - start.Form[r.Id];
                }
                foreach (ReportItemView it in v.Items)
                {
                    if (it.Kind == "rank_up")
                    {
                        CollectionAssert.Contains(expectedRank, it.RunnerId + ":" + (int)it.Stat.Value);
                        Assert.AreEqual(DisplayRules.RankLetter(a.State.FindRunner(it.RunnerId).Stats[(int)it.Stat.Value]), it.Letter);
                        rankUps++;
                    }
                    else if (it.Kind == "big_growth")
                    {
                        Assert.AreEqual(expectedGrowth[it.RunnerId], it.Value);
                        growths++;
                    }
                    else if (it.Kind == "form_up")
                    {
                        Assert.AreEqual(expectedForm[it.RunnerId], it.Value);
                        formUps++;
                    }
                    if (it.RunnerId != 0) Assert.AreEqual(a.State.FindRunner(it.RunnerId).FamilyName + a.State.FindRunner(it.RunnerId).GivenName, it.RunnerName);
                }
                int injuries = 0, awaken = 0, notices = 0;
                foreach (WeekOutcome w in s.Report.Weeks)
                {
                    injuries += w.Reports.Count(x => x.NewInjury == InjurySeverity.Severe || x.NewInjury == InjurySeverity.Moderate);
                    awaken += w.Events.Count(f => f.Def.Category == EventCategory.Awakening);
                    notices += w.Events.Count(f => f.Def.Category != EventCategory.Awakening && !f.Def.Interactive);
                }
                int returns = start.Injured.Count(id => a.State.FindRunner(id) != null && !a.State.FindRunner(id).IsInjured);
                int expectedTotal = injuries + awaken + notices + returns + expectedRank.Count + expectedGrowth.Count + expectedForm.Count;
                Assert.AreEqual(expectedTotal, v.Items.Count + v.More, "材料の総数 = 出る件数 + ほか n 件(仕様 R-10 の種別ごとの数え方)");
                Assert.LessOrEqual(v.Items.Count, FlowRules.K_UI_REPORT_MAX);
                for (int i = 1; i < v.Items.Count; i++)
                    Assert.GreaterOrEqual(Priority(v.Items[i - 1].Kind), Priority(v.Items[i].Kind), "優先度の高い順");
                months++;
            }
            Assert.AreEqual(24, months);
            Assert.Greater(rankUps, 0, "ランクアップの項目が出る月がある");
            Assert.Greater(growths + formUps, 0);
        }

        private static int Priority(string kind)
        {
            switch (kind)
            {
                case "injury_severe": return 100;
                case "injury_moderate": return 90;
                case "awaken": return 80;
                case "gift_reveal": return 75;
                case "rank_up": return 60;
                case "big_growth": return 50;
                case "return": return 40;
                case "notice": return 30;
                case "form_up": return 20;
                default: return 0;
            }
        }

        [Test]
        public void Report_AfterRestoreMidMonth_HasNoChangeFigures()
        {
            GameSession gs = GameSession.New(7UL, Difficulty.Mid);
            var rep = new MonthReport { MonthIndex = 2, StartKnown = false, StartFatigue = 99, StartForm = 99, StartMorale = 99 };
            rep.Themes[0] = rep.Themes[1] = rep.Themes[2] = "th.base_build";
            MonthReportView v = gs.MonthReportView(rep);
            Assert.IsFalse(v.ChangeKnown);
            Assert.AreEqual(0.0, v.AvgFatigueChange);
            Assert.AreEqual(0.0, v.AvgFormChange);
            Assert.AreEqual(0.0, v.MoraleChange);
        }

        [Test]
        public void Report_MapsAwakeningNoticesRacesAndCautions()
        {
            GameSession gs = GameSession.New(9UL, Difficulty.Mid);
            SkillDef gold = gs.Catalog.Skills.First(x => x.Rank == SkillRank.Gold);
            RaceInfo? own;
            var outcome = FirstRaceOutcome(gs, out own);
            foreach (Runner r in gs.State.Runners) { r.Fatigue = 30; r.InjurySeverity = InjurySeverity.None; r.InjuryWeeksLeft = 0; }
            Runner tired = gs.State.Runners[0], hurt = gs.State.Runners[1], fine = gs.State.Runners[2];
            tired.Fatigue = 75;
            hurt.InjurySeverity = InjurySeverity.Moderate;
            hurt.InjuryWeeksLeft = 3;
            fine.Fatigue = 69;
            Runner edge = gs.State.Runners[3];
            edge.Fatigue = 70;                                                       // 警告の値ちょうどは含める
            var rep = new MonthReport { MonthIndex = 5, StartKnown = true, More = 4, StartFatigue = 10, StartForm = 0, StartMorale = 50 };
            rep.Themes[0] = "th.base_build"; rep.Themes[1] = "th.speed"; rep.Themes[2] = "th.recovery";
            rep.Items.Add(new FlowRules.ReportItem { Kind = "awaken", Key = "ev.x:" + fine.Id, Score = 3, RunnerId = fine.Id, EventId = "ev.awaken.test", SkillId = gold.Id });
            rep.Items.Add(new FlowRules.ReportItem { Kind = "notice", Key = "ev.n", EventId = "ev.n" });
            rep.Deferred.Add("ev.deferred");
            rep.Weeks.Add(new WeekOutcome { Race = outcome, OwnRace = own });
            MonthReportView v = gs.MonthReportView(rep);

            Assert.AreEqual(5, v.MonthIndex);
            Assert.AreEqual(4, v.More);
            CollectionAssert.AreEqual(new[] { "th.base_build", "th.speed", "th.recovery" }, v.ThemeIds);
            ReportItemView aw = v.Items[0];
            Assert.AreEqual("awaken", aw.Kind);
            Assert.AreEqual("skill." + gold.Id, aw.SkillId);
            Assert.AreEqual(SkillRank.Gold, aw.SkillRank);
            Assert.AreEqual("ev.awaken.test", aw.EventId);
            Assert.AreEqual(fine.FamilyName + fine.GivenName, aw.RunnerName);
            Assert.IsNull(v.Items[1].SkillRank);
            Assert.AreEqual("ev.n", v.Items[1].EventId);
            CollectionAssert.AreEqual(new[] { "ev.deferred" }, v.Notices);
            Assert.AreEqual(1, v.Races.Count);
            Assert.AreEqual(outcome.Kind, v.Races[0].Kind);
            Assert.AreEqual(outcome.Results[0].Id, v.Races[0].WinnerSchoolId);
            Assert.AreEqual("school." + outcome.Results[0].Id + ".name", v.Races[0].WinnerNameKey);
            int idx = outcome.Results.FindIndex(t => t.Id == gs.School.Id);
            Assert.AreEqual(idx + 1, v.Races[0].OwnRank);
            Assert.AreEqual(idx == 0, v.Races[0].OwnWon);
            CollectionAssert.AreEqual(new[] { tired.Id, edge.Id }, v.Caution.HighFatigue, "疲労 70 以上で、故障中でない選手だけ(ID の昇順)");
            CollectionAssert.AreEqual(new[] { hurt.Id }, v.Caution.Injured);
            Assert.IsNotNull(v.Next);
        }

        private static RaceOutcome FirstRaceOutcome(GameSession gs, out RaceInfo? own)
        {
            own = null;
            for (int w = 0; w < 48; w++)
            {
                if (Array.IndexOf(Ekiden.Domain.Team.Calendar.RaceWeeks, gs.YearWeek) >= 0)
                {
                    gs.ResolveTraining();
                    RaceOutcome r = gs.PlayWeekRaces(out own);
                    return r;
                }
                gs.StepWeek();
            }
            return null;
        }
    }
}
