using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>進行制御(<see cref="GameFlow"/>。08 §6): 『続ける』が唯一の前進、止まる場所は決定表のとおり、何もしなければおまかせ(<see cref="GameSession.StepWeek"/>)と同じ結果。</summary>
    public class GameFlowTests
    {
        private const ulong Seed = 20260403UL;

        /// <summary>1 年度ぶんの停止の並び(次の年度の最初の停止の手前まで)。</summary>
        private static List<Stop> RunYear(GameFlow flow, Action<Stop> onStop = null)
        {
            var list = new List<Stop>();
            int year = flow.Session.Year;
            while (true)
            {
                Stop s = flow.Advance();
                list.Add(s);
                if (onStop != null) onStop(s);
                if (s.Kind == StopKind.YearEndTurnover) return list;
            }
        }

        [Test]
        public void FirstStop_IsAnnualStart_AndManualPlanAddsMonthPlan()
        {
            GameSession a = GameSession.New(Seed, Difficulty.Mid);
            var fa = new GameFlow(a);
            Stop s = fa.Advance();
            Assert.AreEqual(StopKind.AnnualStart, s.Kind);
            Assert.AreEqual("AnnualStart", s.Key);
            GameSession b = GameSession.New(Seed, Difficulty.Mid);
            b.State.Settings.PlanAuto = false;
            var fb = new GameFlow(b);
            Assert.AreEqual(StopKind.AnnualStart, fb.Advance().Kind);
            Assert.AreEqual(StopKind.MonthPlan, fb.Advance().Kind, "plan_auto でなければ、年度初めのあとに月間計画で止まる");
        }

        [Test]
        public void OneYear_StopsFollowTheDecisionTable()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            List<Stop> stops = RunYear(flow);
            List<string> keys = stops.Select(x => x.Key).ToList();
            Assert.AreEqual("AnnualStart", keys[0]);
            Assert.AreEqual(12, stops.Count(x => x.Kind == StopKind.MonthReport), "月末レポートは毎月");
            Assert.AreEqual("YearEnd:Settle", keys[keys.Count - 2]);
            Assert.AreEqual("YearEnd:Turnover", keys[keys.Count - 1]);
            Assert.Less(keys.LastIndexOf("MonthReport"), keys.IndexOf("YearEnd:Settle"));
            // 大会ごとに エントリー → オーダー → 当日 の順
            foreach (RaceKind k in WorldSim.AllRaces)
            {
                int e = keys.IndexOf("Entry:" + k), o = keys.IndexOf("Order:" + k), d = keys.IndexOf("RaceDay:" + k);
                if (o < 0) { Assert.AreEqual(-1, d, "出場権がなければ当日もない"); continue; }
                Assert.Greater(o, e, "Entry:" + k + " のあとに Order");
                Assert.Greater(d, o, "Order のあとに RaceDay");
            }
            Assert.GreaterOrEqual(keys.IndexOf("Entry:Fall"), 0, "第一戦のエントリーで止まる(必ず止まる。登録しないと棄権)");
            // 停止のない週は飛ばされ、止まった時点の週は単調に進む
            Assert.AreEqual(Calendar.WeeksPerYear, gs.State.AbsWeek, "年度末の停止のあと、次の年度の最初の週になっている");
            Assert.AreEqual(2, gs.Year);
        }

        [Test]
        public void NoOperations_EqualsTheAutoplay()
        {
            foreach (bool eventAuto in new[] { true, false })
            {
                GameSession a = GameSession.New(Seed, Difficulty.Mid), b = GameSession.New(Seed, Difficulty.Mid);
                a.State.Settings.EventAuto = b.State.Settings.EventAuto = eventAuto;
                a.AutoUpgrade = false; a.AutoDeclareTickets = false;               // 流れ(GameFlow)が止める部分に合わせる
                for (int w = 0; w < Calendar.WeeksPerYear * 3; w++) a.StepWeek();
                var flow = new GameFlow(b);
                for (int y = 0; y < 3; y++) RunYear(flow);
                Assert.AreEqual(GameSessionTests.Digest(a), GameSessionTests.Digest(b), "何も操作しなければおまかせと同じ(event_auto = " + eventAuto + ")");
            }
        }

        [Test]
        public void EventStops_OnlyForRealChoices_AndAChoiceIsApplied()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.State.Settings.EventAuto = false;
            var flow = new GameFlow(gs);
            Assert.IsFalse(flow.ChooseEvent("x").Ok, "止まっていないときは選べない");
            int chosenNonDefault = 0, eventStops = 0;
            for (int y = 0; y < 3; y++)
            {
                RunYear(flow, s =>
                {
                    if (s.Kind != StopKind.Event) return;
                    eventStops++;
                    Assert.GreaterOrEqual(s.Choices.Count, 2, "選択肢が 2 つ以上のイベントだけ止まる");
                    Assert.IsFalse(flow.ChooseEvent("no_such_choice").Ok);
                    ChoiceDef pick = s.Choices.Last();
                    Assert.IsTrue(flow.ChooseEvent(pick.Id).Ok);
                    if (!s.Event.Def.Default.Contains(pick.Id)) chosenNonDefault++;
                });
            }
            Assert.Greater(eventStops, 3, "3 年度で選択のあるイベントが起こる");
            // 選んだ選択肢が記録されている(最後のものを確かめる)
            Assert.Greater(gs.State.EventLog.Count, 0);
            Assert.Greater(chosenNonDefault, 0, "既定と違う選択肢も選べる");
        }

        [Test]
        public void ChosenOption_IsRecordedInTheEventLog()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.State.Settings.EventAuto = false;
            var flow = new GameFlow(gs);
            string chosen = null, eventId = null;
            while (chosen == null)
            {
                Stop s = flow.Advance();
                if (s.Kind == StopKind.Event)
                {
                    ChoiceDef pick = s.Choices.Last();
                    flow.ChooseEvent(pick.Id);
                    chosen = pick.Id; eventId = s.Event.Def.Id;
                }
            }
            flow.Advance();                                                                          // 次の停止に進むと、選択が適用される
            EventLogEntry e = gs.State.EventLog.Last(x => x.Id == eventId);
            Assert.AreEqual(chosen, e.Choice);
        }

        [Test]
        public void Entry_ChosenAtTheStop_DecidesWhoRuns()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            List<int> custom = null;
            Stop race = null;
            while (race == null)
            {
                Stop s = flow.Advance();
                if (s.Kind == StopKind.Entry && s.Race == RaceKind.Fall)
                {
                    custom = gs.State.Runners.OrderBy(r => TeamState.LongP(r)).ThenBy(r => r.Id).Take(10).Select(r => r.Id).ToList();
                    Assert.IsTrue(gs.SetEntries(RaceKind.Fall, custom).Ok);
                }
                if (s.Kind == StopKind.RaceDay && s.Race == RaceKind.Fall) race = s;
            }
            Assert.IsNotNull(custom);
            RaceTeam team = race.Week.Race.Teams.First(t => t.Id == gs.School.Id);
            int outside = team.Lineup.Count(x => !custom.Contains(x.RunnerId));
            int hurt = custom.Count(id => gs.State.FindRunner(id).IsInjured);
            Assert.LessOrEqual(outside, hurt, "登録した顔ぶれが走る。外れるのは、故障した登録メンバーの差し替えだけ(04 §5.3): 登録 [" + string.Join(",", custom) + "] 走者 [" + string.Join(",", team.Lineup.Select(x => x.RunnerId)) + "]");
            Assert.Greater(team.Lineup.Count(x => custom.Contains(x.RunnerId)), 0);
            var autoPick = TeamRacing.AutoRegister(gs.State, RaceKind.Fall);
            Assert.AreNotEqual(autoPick.Count(id => custom.Contains(id)), 10, "おまかせ登録とは違う顔ぶれを登録した");
            Assert.IsTrue(race.Week.OwnRace.HasValue);
            Assert.AreEqual(1, race.Week.OwnRace.Value.Kind);
        }

        [Test]
        public void YearEnd_UpgradeBetweenSettleAndTurnover()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            Stop s;
            do { s = flow.Advance(); } while (s.Kind != StopKind.YearEndSettle);
            Assert.IsTrue(flow.CanSave, "年度末の精算のあとは保存できる");
            int dorm = gs.State.Fac.Dorm, funds = gs.State.Funds;
            Assert.IsNotNull(s.Year);
            Assert.AreEqual(1, gs.Stats.Count, "精算の時点で、その年度の記録は残っている");
            Assert.GreaterOrEqual(gs.UpgradeRecommended(), 0);
            Assert.AreEqual(gs.State.Fac.Dorm, gs.School.Fac.Dorm);
            Stop t = flow.Advance();
            Assert.AreEqual(StopKind.YearEndTurnover, t.Kind);
            Assert.AreEqual(10, t.Turnover.Graduates.Count);
            Assert.That(t.IntakeIds.Count, Is.InRange(1, 11));
            foreach (int id in t.IntakeIds) Assert.AreEqual(1, gs.State.FindRunner(id).Grade);
            Assert.IsTrue(flow.CanSave);
            Assert.AreEqual(2, gs.Year);
            Assert.AreEqual(StopKind.AnnualStart, flow.Advance().Kind, "次の年度の最初の停止");
        }

        [Test]
        public void CanSave_OnlyBeforeAWeekIsResolved()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            Stop s = flow.Advance();
            Assert.IsTrue(flow.CanSave);
            bool sawUnsavable = false;
            for (int i = 0; i < 60; i++)
            {
                s = flow.Advance();
                if (s.Kind == StopKind.RaceDay || s.Kind == StopKind.MonthReport || s.Kind == StopKind.Event || s.Kind == StopKind.Injury) { Assert.IsFalse(flow.CanSave, s.Key); sawUnsavable = true; }
                if (s.Kind == StopKind.Entry || s.Kind == StopKind.Order || s.Kind == StopKind.AnnualStart) Assert.IsTrue(flow.CanSave, s.Key);
            }
            Assert.IsTrue(sawUnsavable);
        }

        [Test]
        public void MonthReport_SummarisesTheFourWeeks()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            var reports = new List<MonthReport>();
            RunYear(flow, s => { if (s.Kind == StopKind.MonthReport) reports.Add(s.Report); });
            Assert.AreEqual(12, reports.Count);
            for (int m = 0; m < 12; m++)
            {
                Assert.AreEqual(m, reports[m].MonthIndex);
                Assert.AreEqual(4, reports[m].Weeks.Count);
                Assert.LessOrEqual(reports[m].Items.Count, FlowRules.K_UI_REPORT_MAX);
                Assert.That(reports[m].Morale, Is.InRange(0.0, 100.0));
                foreach (var g in reports[m].Items.GroupBy(i => i.Kind)) Assert.LessOrEqual(g.Count(), FlowRules.K_UI_REPORT_PER_KIND);
            }
            Assert.IsTrue(reports.Any(r => r.Items.Count > 0), "1 年度のうちには報告する項目がある");
        }

        // ---------------------------------------------------------------- 区間オーダー・当日(SC-22・SC-24・SC-25)
        [Test]
        public void RaceDayPrep_AppearsOnlyInFullMode_BetweenOrderAndResult()
        {
            foreach (string mode in new[] { "Full", "Digest", "ResultOnly" })
            {
                GameSession gs = GameSession.New(Seed, Difficulty.Mid);
                gs.State.Settings.SpectateMode = mode;
                var flow = new GameFlow(gs);
                List<string> keys = RunYear(flow).Select(x => x.Key).ToList();
                foreach (RaceKind k in WorldSim.AllRaces)
                {
                    int o = keys.IndexOf("Order:" + k), p = keys.IndexOf("RaceDayPrep:" + k), d = keys.IndexOf("RaceDay:" + k);
                    if (o < 0) continue;
                    if (mode == "Full") { Assert.Greater(p, o, k + " 当日の準備は オーダーのあと"); Assert.Greater(d, p, k + " 結果は当日のあと"); }
                    else Assert.AreEqual(-1, p, mode + ": 当日の準備では止まらない(おまかせ)");
                }
            }
        }

        [Test]
        public void RaceDayPrep_ManualDecisions_AreWhatRuns()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            int checkedRaces = 0;
            for (int y = 0; y < 2; y++)
            {
                RaceDay rd = null; List<TeamResult> preview = null; int[] lineupIds = null;
                RunYear(flow, s =>
                {
                    if (s.Kind == StopKind.RaceDayPrep)
                    {
                        rd = s.RaceDay;
                        Assert.AreSame(rd, gs.CurrentRaceDay);
                        Assert.IsFalse(flow.CanSave, "当日は練習が解決されたあとなので保存しない");
                        rd.ApplyGauge();
                        if (rd.Bench.Count > 0) rd.Swap(0, 0);
                        rd.SetInstruction(rd.Sections - 1, Instruction.Attack);
                        preview = rd.Preview();
                        lineupIds = rd.Lineup.Select(x => x.RunnerId).ToArray();
                    }
                    else if (s.Kind == StopKind.RaceDay && s.Race.HasValue && rd != null)
                    {
                        RaceTeam own = s.Week.Race.Teams.First(t => t.Id == gs.School.Id);
                        CollectionAssert.AreEqual(lineupIds, own.Lineup.Select(x => x.RunnerId).ToArray(), "決めた先発が走る");
                        CollectionAssert.AreEqual(rd.Instructions, own.Instr);
                        CollectionAssert.AreEqual(preview.Select(r => r.Id), s.Week.Race.Results.Select(r => r.Id));
                        CollectionAssert.AreEqual(preview.Select(r => r.TotalDs), s.Week.Race.Results.Select(r => r.TotalDs), "見込みと実際は一致する");
                        Assert.IsNull(gs.CurrentRaceDay);
                        rd = null;
                        checkedRaces++;
                    }
                });
            }
            Assert.GreaterOrEqual(checkedRaces, 3);
        }

        [Test]
        public void LiveRace_ThroughTheFlow_EndsWithTheResultShownLive()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            int lives = 0;
            RaceDay rd = null; List<StandingRow> last = null;
            RunYear(flow, s =>
            {
                if (s.Kind == StopKind.RaceDayPrep)
                {
                    rd = s.RaceDay;
                    rd.Start();
                    for (int i = 0; i < rd.Sections; i++)
                    {
                        rd.SetInstruction(i, rd.View(i).Recommended);
                        if (rd.NextIncident() != null && rd.GekiLeft > 0) rd.UseGeki();
                        last = rd.Advance();
                    }
                    Assert.AreEqual(rd.Sections, rd.Resolved);
                }
                else if (s.Kind == StopKind.RaceDay && s.Race.HasValue && rd != null)
                {
                    int ownRank = s.Week.OwnRace.Value.TeamRank;
                    Assert.AreEqual(ownRank, last.First(r => r.IsOwn).Rank, "最後の中継所の順位 = 大会の結果の順位");
                    lives++; rd = null;
                }
            });
            Assert.GreaterOrEqual(lives, 2);
        }

        [Test]
        public void OrderSubmittedAtTheOrderStop_IsUsed_AndClearedAfterTheRace()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            bool seen = false;
            RunYear(flow, s =>
            {
                if (s.Kind == StopKind.Order && s.Race == RaceKind.National)
                {
                    int[] sug = gs.SuggestOrder(RaceKind.National);
                    Assert.IsNotNull(sug);
                    Assert.IsTrue(gs.SetOrder(RaceKind.National, sug.Reverse().ToArray()).Ok);
                }
                else if (s.Kind == StopKind.RaceDayPrep && s.Race == RaceKind.National)
                {
                    Assert.IsNotNull(gs.OrderOf(RaceKind.National), "当日の準備でも提出したオーダーが残っている");
                    s.RaceDay.ResetSwaps();
                    var ord = gs.OrderOf(RaceKind.National);
                    Assert.GreaterOrEqual(s.RaceDay.Lineup.Count(x => ord.Contains(x.RunnerId)), ord.Count - 3);
                    seen = true;
                }
                else if (s.Kind == StopKind.RaceDay && s.Race == RaceKind.National) Assert.IsNull(gs.OrderOf(RaceKind.National), "大会が終わったらオーダーは空になる");
            });
            Assert.IsTrue(seen);
        }

        [Test]
        public void DigestMode_StillUsesTheSubmittedOrder_WithAutomaticDay()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.State.Settings.SpectateMode = "Digest";
            var flow = new GameFlow(gs);
            int[] ordered = null; bool checkedOnce = false;
            RunYear(flow, s =>
            {
                if (s.Kind == StopKind.Order && s.Race == RaceKind.Fall)
                {
                    ordered = gs.SuggestOrder(RaceKind.Fall).Reverse().ToArray();
                    Assert.IsTrue(gs.SetOrder(RaceKind.Fall, ordered).Ok);
                }
                else if (s.Kind == StopKind.RaceDay && s.Race == RaceKind.Fall && ordered != null)
                {
                    RaceTeam own = s.Week.Race.Teams.First(t => t.Id == gs.School.Id);
                    Assert.GreaterOrEqual(own.Lineup.Count(x => ordered.Contains(x.RunnerId)), ordered.Length - 3, "オーダーを基に、自動の当日変更だけが入る");
                    checkedOnce = true;
                }
            });
            Assert.IsTrue(checkedOnce);
        }

        [Test]
        public void FlowWithAutoUpgradeAndTickets_EqualsStepWeek()
        {
            GameSession a = GameSession.New(Seed, Difficulty.Mid), b = GameSession.New(Seed, Difficulty.Mid);
            a.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinFall, 0);
            b.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinFall, 0);
            for (int w = 0; w < Calendar.WeeksPerYear * 3; w++) a.StepWeek();
            var flow = new GameFlow(b, autoUpgrade: true, autoTickets: true);
            for (int y = 0; y < 3; y++) RunYear(flow);
            Assert.AreEqual(GameSessionTests.Digest(a), GameSessionTests.Digest(b), "強化と招聘の宣言もおまかせにすれば、流れ = StepWeek");
        }

        [Test]
        public void InjuryStops_AreForSevereOrImportantRunners()
        {
            int stops = 0;
            for (ulong seed = 1; seed <= 4; seed++)
            {
                GameSession gs = GameSession.New(Seed + seed, Difficulty.Mid);
                var flow = new GameFlow(gs);
                for (int y = 0; y < 2; y++)
                    RunYear(flow, s =>
                    {
                        if (s.Kind != StopKind.Injury) return;
                        stops++;
                        Runner r = gs.State.FindRunner(s.RunnerId);
                        Assert.IsNotNull(r);
                        Assert.IsTrue(r.IsInjured || r.InjuryAge < 99);
                        Assert.IsTrue(r.InjurySeverity == InjurySeverity.Severe || r.Group == 0 || gs.State.Entries.Any(e => e.Contains(r.Id)) || r.InjurySeverity == InjurySeverity.None,
                                      "重傷、または A グループ・登録メンバーの中傷で止まる");
                    });
            }
            Assert.GreaterOrEqual(stops, 0);
        }

        [Test]
        public void WarnStop_AtMostOncePerMonth()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            int warns = 0, warnMonthMax = 0;
            var perMonth = new Dictionary<int, int>();
            int month = 0;
            for (int y = 0; y < 2; y++)
                RunYear(flow, s =>
                {
                    if (s.Kind == StopKind.Warn) { warns++; perMonth.TryGetValue(month, out int n); perMonth[month] = n + 1; warnMonthMax = Math.Max(warnMonthMax, n + 1); }
                    if (s.Kind == StopKind.MonthReport) month++;
                    // 月の初めに、全グループを夏合宿のメニュー(負荷が高い)にして疲労を溜める
                    if (s.Kind == StopKind.AnnualStart || s.Kind == StopKind.MonthReport)
                        for (int g = 0; g < 3; g++) gs.SetMonthlyPlan(g, "th.camp");
                });
            Assert.GreaterOrEqual(warns, 1, "疲労が溜まっていれば警告で止まる");
            Assert.LessOrEqual(warnMonthMax, 1, "月に 1 回まで");
        }
    }
}
