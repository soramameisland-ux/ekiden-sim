using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using Ekiden.Persistence.Snapshot;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>読み取りの API は状態を変えない(08b §1)。すべての View を、年度のいろいろな時点で呼び、呼ぶ前後の保存の形(<see cref="GameSnapshot"/>)が同じであること。</summary>
    public class ViewPurityTests
    {
        private static void CallEverything(GameSession gs, MonthReport report, RaceDay day)
        {
            gs.Home();
            gs.RosterRows();
            foreach (Runner r in gs.State.Runners.Take(4)) { gs.RunnerRow(r.Id); gs.RunnerDetail(r.Id); gs.InjuryDialog(r.Id); }
            gs.Plan();
            gs.AnnualPlanView();
            gs.ConditionWarning();
            gs.Ops();
            gs.WorldOverview();
            gs.Obs();
            gs.History();
            gs.YearSummary();
            gs.Graduation();
            gs.Enrollment();
            gs.ScoutMap();
            gs.ScoutTargets();
            gs.ScoutCandidates();
            gs.ScoutCandidate(0);
            foreach (RaceKind k in new[] { RaceKind.Fall, RaceKind.National, RaceKind.NewYear })
            {
                gs.Entry(k);
                gs.Order(k);
                gs.OrderCandidates(k, 0);
                gs.Preview(k);
            }
            foreach (EventDef def in gs.Catalog.Events.Take(30))
                gs.EventDialog(new FiredEvent { Def = def, R = gs.State.Runners[0], P = gs.State.Runners.Count > 1 ? gs.State.Runners[1] : null });
            if (report != null) gs.MonthReportView(report);
            if (day != null) { gs.RaceDayView(day); gs.RaceLive(day); }
        }

        [Test]
        public void NoViewChangesTheState_AtAnyPointOfTheYear()
        {
            GameSession gs = GameSession.New(61UL, Difficulty.Elite);
            gs.AutoUpgrade = false;
            gs.State.Settings.SpectateMode = "Full";
            var flow = new GameFlow(gs);
            int days = 0;
            int calls = 0;
            string last = null;
            for (int i = 0; i < 400; i++)
            {
                Stop s = flow.Advance();
                MonthReport rep = s.Kind == StopKind.MonthReport ? s.Report : null;
                if (i % 7 != 0 && rep == null && s.RaceDay == null && s.Kind != StopKind.YearEndSettle && s.Kind != StopKind.YearEndTurnover) continue;
                gs.State.Runners[0].Fatigue = 75;                              // 警告や故障の知らせなど、状態に応じた View も通るようにしておく
                gs.State.Runners[1].Fatigue = 72;
                if (i % 2 == 1) { gs.State.Runners[2].InjurySeverity = InjurySeverity.Moderate; gs.State.Runners[2].InjuryWeeksLeft = 3; }
                string before = GameSnapshot.Capture(gs).ToJson() + (s.RaceDay != null ? SuspendSnapshot.CaptureRace(s.RaceDay.Capture()).ToJson() : "");
                CallEverything(gs, rep, s.RaceDay);
                if (s.RaceDay != null) days++;
                string after = GameSnapshot.Capture(gs).ToJson() + (s.RaceDay != null ? SuspendSnapshot.CaptureRace(s.RaceDay.Capture()).ToJson() : "");
                Assert.AreEqual(before, after, "停止 " + s.Key + "(" + i + ")のあと、View を呼んで状態が変わった");
                calls++;
                last = s.Key;
                if (s.Kind == StopKind.YearEndTurnover) break;
            }
            Assert.Greater(calls, 15);
            Assert.Greater(days, 0, "大会の当日の View も通った");
            Assert.AreEqual("YearEnd:Turnover", last);
        }
    }
}
