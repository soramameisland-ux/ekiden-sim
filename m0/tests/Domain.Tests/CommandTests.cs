using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>プレイヤーの操作(<see cref="GameSession"/> の操作の層)。無効な操作は理由つきで拒否し、状態を変えない(08 F4)。</summary>
    public class CommandTests
    {
        private const ulong Seed = 20260401UL;

        private static GameSession New(Difficulty d = Difficulty.Mid) { return GameSession.New(Seed, d); }

        private static void Advance(GameSession gs, int weeks) { for (int i = 0; i < weeks; i++) gs.StepWeek(); }

        private static MenuSpec Care { get { return new MenuSpec("ks.core", "ks.rest", "mil.low"); } }

        // ---------------------------------------------------------------- グループ(R-04)
        [Test]
        public void Groups_AreRecommendedAtStart_AndStayUntilApril()
        {
            GameSession gs = New();
            TeamState s = gs.State;
            var order = s.Runners.OrderByDescending(r => TeamState.LongP(r)).ThenBy(r => r.Id).ToList();
            for (int i = 0; i < order.Count; i++) Assert.AreEqual(i < 10 ? 0 : (i < 20 ? 1 : 2), order[i].Group, "開始時は推奨の編成");
            // 手動で動かしたグループは、毎週の更新で戻らない
            Runner weakest = order.Last();
            Assert.IsTrue(gs.MoveToGroup(weakest.Id, 0).Ok);
            Advance(gs, 10);
            Assert.AreEqual(0, weakest.Group, "グループは保存される編成(毎週は決め直さない)");
            Assert.AreEqual(11, s.Runners.Count(r => r.Group == 0), "A は 11 人(手動で 1 人足した)");
        }

        [Test]
        public void ApplyRecommendedGroups_OnlyInApril_AndNewRunnersJoinGroupC()
        {
            GameSession gs = New();
            Runner r = gs.State.Runners[0];
            gs.MoveToGroup(r.Id, 2);
            Assert.IsTrue(gs.ApplyRecommendedGroups().Ok, "4 月は適用できる");
            Advance(gs, 4);
            OpResult no = gs.ApplyRecommendedGroups();
            Assert.IsFalse(no.Ok);
            Assert.AreEqual(OpErr.RecommendApril.Code, no.Code);
            Advance(gs, 44);                                                                         // 次の年度の最初の週(おまかせ: 推奨の編成が自動で適用される)
            var order = gs.State.Runners.OrderByDescending(x => TeamState.LongP(x)).ThenBy(x => x.Id).ToList();
            for (int i = 0; i < order.Count; i++) Assert.AreEqual(i < 10 ? 0 : (i < 20 ? 1 : 2), order[i].Group, "おまかせは年度初めに推奨の編成を適用");
        }

        [Test]
        public void NewRunners_StartInGroupC_WhenPlanIsManual()
        {
            GameSession gs = New();
            gs.State.Settings.PlanAuto = false;
            Advance(gs, 47);
            int before = gs.State.NextRunnerId;
            gs.StepWeek();                                                                           // 年度末 → 入学
            foreach (Runner r in gs.State.Runners.Where(x => x.Id >= before)) Assert.AreEqual(2, r.Group, "新入部員は C");
        }

        // ---------------------------------------------------------------- 月間計画
        [Test]
        public void MonthlyPlan_SetAndValidate()
        {
            GameSession gs = New();
            Assert.IsTrue(gs.SetMonthlyPlan(1, "th.speed").Ok);
            Assert.AreEqual("th.speed", gs.State.MonthlyPlan[1]);
            Assert.IsFalse(gs.SetMonthlyPlan(3, "th.speed").Ok);
            OpResult bad = gs.SetMonthlyPlan(0, "th.nothing");
            Assert.IsFalse(bad.Ok);
            Assert.AreEqual("th.base_build", gs.State.MonthlyPlan[0], "拒否した操作は状態を変えない");
            Assert.IsFalse(gs.SetMonthlyPlan(0, "th.custom").Ok, "週ごとのメニューを決める前は自由編集にできない");
        }

        [Test]
        public void CustomWeeks_AreValidated_AndUsedByTheWeek()
        {
            GameSession gs = New();
            var weeks = new[] { new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low"), new MenuSpec("ks.hill", "ks.long_run", "mil.std"), Care, Care };
            Assert.IsTrue(gs.SetCustomWeeks(0, weeks).Ok);
            Assert.AreEqual("th.custom", gs.State.MonthlyPlan[0]);
            OpResult bad = gs.SetCustomWeeks(1, new[] { new MenuSpec("ks.hill", "ks.hill", "mil.low"), Care, Care, Care });
            Assert.IsFalse(bad.Ok);
            Assert.AreEqual(OpErr.CustomWeek.Code, bad.Code); Assert.AreEqual("1", bad.Error.Args[1], "n = 1 週目"); Assert.AreEqual(OpErr.MenuDupKs.Code, bad.Error.Why.Code);
            Assert.IsNull(gs.State.CustomWeeks[1], "拒否した操作は状態を変えない");
            Assert.IsFalse(gs.SetCustomWeeks(0, weeks.Take(3).ToArray()).Ok);
            bool camp, ind;
            var wk = new TeamWeek(gs.Master);
            Runner a = gs.State.Runners.First(r => r.Group == 0 && !r.IsInjured);
            Menu m = wk.MenuFor(gs.State, a, out camp, out ind);
            Assert.AreEqual("ks.sharpen", m.Key1, "今週(月の 1 週目)のメニューは自由編集の 1 週目");
        }

        [Test]
        public void AnnualPlan_ReappliesTheRecommendedPlan_WhenAuto()
        {
            GameSession gs = New();
            Advance(gs, 8);                                                                          // 6 月
            Assert.AreEqual("th.base_build", gs.State.MonthlyPlan[0]);
            var peaking = new[] { PeakingMode.Short, PeakingMode.Short, PeakingMode.Standard, PeakingMode.Short, PeakingMode.None };
            Assert.IsTrue(gs.SetAnnualPlan(PlanFocus.Speed, 1, peaking).Ok);
            Assert.AreEqual("th.speed", gs.State.MonthlyPlan[0], "スピード寄りの 6 月は『スピード』(R-04 の表)");
            Assert.AreEqual(PeakingMode.Short, gs.State.Plan.Peaking[0]);
            Assert.IsFalse(gs.SetAnnualPlan(PlanFocus.Long, 5, peaking).Ok);
            Assert.IsFalse(gs.SetAnnualPlan(PlanFocus.Long, 0, peaking.Take(3).ToArray()).Ok);
            Assert.AreEqual(PlanFocus.Speed, gs.State.Plan.Focus, "拒否した操作は状態を変えない");
        }

        // ---------------------------------------------------------------- 個別指導(R-06 / 08 TV-11)
        [Test]
        public void Guide_Menu_CostsOnePointAndLastsUntilMonthEnd()
        {
            GameSession gs = New();
            TeamState s = gs.State;
            Runner r = s.Runners.First(x => !x.IsInjured);
            Assert.AreEqual(4, s.GuidanceLeft);
            Assert.IsTrue(gs.GuideMenu(r.Id, Care).Ok);
            Assert.AreEqual(3, s.GuidanceLeft);
            Assert.AreEqual(4, s.IndividualMenus[r.Id].UntilAbsWeek, "4 月の 1 週目に指定 → 5 月の最初の週まで");
            // 同じ週に指定し直すと前の指定の分が戻る
            Assert.IsTrue(gs.GuideMenu(r.Id, new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low")).Ok);
            Assert.AreEqual(3, s.GuidanceLeft);
            Assert.AreEqual("ks.sharpen", s.IndividualMenus[r.Id].Menu.Key1);
            // 取り消し(今週の指定 → ポイントが戻る)
            Assert.IsTrue(gs.CancelGuide(r.Id).Ok);
            Assert.AreEqual(4, s.GuidanceLeft);
            Assert.IsFalse(gs.CancelGuide(r.Id).Ok);
            // 次の週に入ると取り消してもポイントは戻らない
            gs.GuideMenu(r.Id, Care);
            gs.StepWeek();
            Assert.IsTrue(gs.CancelGuide(r.Id).Ok);
            Assert.AreEqual(3, s.GuidanceLeft);
        }

        [Test]
        public void Guide_Menu_RejectsWithReasons_AndKeepsState()
        {
            GameSession gs = New();
            TeamState s = gs.State;
            Runner r = s.Runners[0], hurt = s.Runners[1];
            hurt.InjurySeverity = InjurySeverity.Moderate;
            OpResult o;
            o = gs.GuideMenu(hurt.Id, Care);
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.MenuInjured.Code, o.Code);
            o = gs.GuideMenu(r.Id, new MenuSpec("ks.hill", "ks.hill", "mil.low"));
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.MenuDupKs.Code, o.Code);
            o = gs.GuideMenu(r.Id, new MenuSpec("ks.cross_train", "ks.rest", "mil.low"));
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.MenuCrossTrain.Code, o.Code);
            o = gs.GuideMenu(r.Id, new MenuSpec("ks.core", "ks.rest", null));
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.MenuMileage.Code, o.Code);
            Assert.IsFalse(gs.GuideMenu(999, Care).Ok);
            Assert.AreEqual(0, s.IndividualMenus.Count);
            Assert.AreEqual(4, s.GuidanceLeft, "拒否した操作はポイントを使わない");
            for (int i = 0; i < 4; i++) Assert.IsTrue(gs.GuideMenu(s.Runners[2 + i].Id, Care).Ok);
            o = gs.GuideMenu(s.Runners[7].Id, Care);
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.GuidePoints.Code, o.Code);
        }

        [Test]
        public void Guide_Menu_AsksConfirmation_ForTaperAndSafetyNet()
        {
            GameSession gs = New();
            TeamState s = gs.State;
            Runner r = s.Runners.First(x => !x.IsInjured);
            r.Fatigue = 90;
            OpResult o = gs.GuideMenu(r.Id, Care);
            Assert.IsFalse(o.Ok);
            Assert.IsTrue(o.NeedsConfirm);
            CollectionAssert.Contains(o.Warnings.Select(w => w.Code).ToList(), OpErr.WarnNoSafety.Code);
            Assert.AreEqual(4, s.GuidanceLeft, "確認の前はポイントを使わない");
            Assert.IsTrue(gs.GuideMenu(r.Id, Care, confirmed: true).Ok);
            Assert.AreEqual(3, s.GuidanceLeft);
            // 調整期間にある登録メンバー
            GameSession g2 = New();
            Advance(g2, 22);
            Runner e = g2.State.Runners.First(x => g2.State.Entries[0].Contains(x.Id) && !x.IsInjured);
            e.Fatigue = 10;
            OpResult t = g2.GuideMenu(e.Id, Care);
            Assert.IsTrue(t.NeedsConfirm);
            CollectionAssert.Contains(t.Warnings.Select(w => w.Code).ToList(), OpErr.WarnOverrideTaper.Code);
        }

        [Test]
        public void Talk_RaisesMotivationOnce_PerMonth()
        {
            GameSession gs = New();
            TeamState s = gs.State;
            Runner r = s.Runners[0];
            r.Motivation = 2;
            Assert.IsTrue(gs.Talk(r.Id).Ok);
            Assert.AreEqual(3, r.Motivation);
            Assert.AreEqual(3, s.GuidanceLeft);
            OpResult again = gs.Talk(r.Id);
            Assert.IsFalse(again.Ok); Assert.AreEqual(OpErr.GuideTalked.Code, again.Code);
            Assert.AreEqual(3, r.Motivation);
            r = s.Runners[1]; r.Motivation = 5;
            Assert.IsTrue(gs.Talk(r.Id).Ok);
            Assert.AreEqual(5, r.Motivation, "やる気は 5 まで");
            Advance(gs, 4);
            Assert.AreEqual(4, s.GuidanceLeft, "月初に 4 へ戻る(繰り越さない)");
            Assert.IsTrue(gs.Talk(s.Runners[0].Id).Ok, "翌月はまた面談できる");
        }

        // ---------------------------------------------------------------- 登録(04 §4)
        [Test]
        public void SetEntries_OnlyBetweenDeadlineAndRaceWeek()
        {
            GameSession gs = New();
            var ids = gs.State.Runners.Take(10).Select(r => r.Id).ToList();
            OpResult early = gs.SetEntries(RaceKind.Fall, ids);
            Assert.IsFalse(early.Ok); Assert.AreEqual(OpErr.EntryWindow.Code, early.Code);
            Advance(gs, 21);
            Assert.AreEqual(10, gs.State.Entries[0].Count, "締切の週に、おまかせ登録が初期値として入る");
            Assert.IsTrue(gs.SetEntries(RaceKind.Fall, ids).Ok);
            CollectionAssert.AreEqual(ids, gs.State.Entries[0], "並びを保つ");
            Advance(gs, 5);                                                                          // 週 26(第一戦の翌週)
            Assert.IsFalse(gs.SetEntries(RaceKind.Fall, ids).Ok);
        }

        [Test]
        public void SetEntries_ValidatesMembers()
        {
            GameSession gs = New();
            Advance(gs, 21);
            TeamState s = gs.State;
            List<int> all = s.Runners.Select(r => r.Id).ToList();
            OpResult o;
            o = gs.SetEntries(RaceKind.Fall, all.Take(11).ToList());
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.EntryMax.Code, o.Code); Assert.AreEqual("10", o.Error.Args[1]);
            o = gs.SetEntries(RaceKind.Fall, new List<int> { all[0], all[0] });
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.EntryDup.Code, o.Code);
            o = gs.SetEntries(RaceKind.Fall, new List<int> { all[0], 999 });
            Assert.IsFalse(o.Ok);
            s.FindRunner(all[1]).InjurySeverity = InjurySeverity.Severe;
            o = gs.SetEntries(RaceKind.Fall, new List<int> { all[0], all[1] });
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.EntrySevere.Code, o.Code);
            for (int i = 2; i < 5; i++) s.FindRunner(all[i]).Overseas = true;
            o = gs.SetEntries(RaceKind.Fall, new List<int> { all[2], all[3], all[4] });
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.EntryIntl.Code, o.Code);
            List<int> before = s.Entries[0].ToList();
            o = gs.SetEntries(RaceKind.Fall, new List<int> { all[6], all[7] });
            Assert.IsTrue(o.NeedsConfirm, "出走人数に足りないと棄権になる");
            CollectionAssert.AreEqual(before, s.Entries[0], "確認の前は変えない");
            Assert.IsTrue(gs.SetEntries(RaceKind.Fall, new List<int> { all[6], all[7] }, confirmed: true).Ok);
            Assert.IsTrue(gs.AutoEntries(RaceKind.Fall).Ok);
            Assert.AreEqual(RaceTables.Rule(RaceKind.Fall).Entry, s.Entries[0].Count);
        }

        [Test]
        public void ManualEntries_ChangeWhoRuns()
        {
            GameSession gs = New();
            Advance(gs, 21);
            TeamState s = gs.State;
            // 力の低い順に 10 人を登録すると、おまかせ登録とは違う顔ぶれが走る
            var weakest = s.Runners.OrderBy(r => TeamState.LongP(r)).ThenBy(r => r.Id).Take(10).Select(r => r.Id).ToList();
            Assert.IsTrue(gs.SetEntries(RaceKind.Fall, weakest).Ok);
            Advance(gs, 4);
            WeekOutcome o = gs.StepWeek();                                                          // 第一戦(週 25)
            Assert.IsNotNull(o.Race);
            RaceTeam team = o.Race.Teams.FirstOrDefault(t => t.Id == gs.School.Id);
            if (team != null) Assert.IsTrue(team.Lineup.All(x => weakest.Contains(x.RunnerId)), "登録していない選手は出走しない");
        }

        // ---------------------------------------------------------------- 特別招聘(07 §3.3)
        [Test]
        public void DeclareTicket_ReasonsAndSuccess()
        {
            GameSession gs = New();
            OpResult o = gs.DeclareTicket(TicketKind.Rookie);
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.TicketNone.Code, o.Code);
            gs.State.Tickets.Earn(TicketMilestones.WinFall, 0);
            gs.State.Tickets.Earn(TicketMilestones.WinNational, 0);
            o = gs.DeclareTicket(TicketKind.Rookie);
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.TicketMonth.Code, o.Code);
            Advance(gs, 8);                                                                          // 6 月
            Assert.IsFalse(gs.DeclareTicket(TicketKind.Intl, 13).Ok, "留学生に出身県は指定できない");
            Assert.IsFalse(gs.DeclareTicket(TicketKind.Rookie, 48).Ok);
            Assert.IsTrue(gs.DeclareTicket(TicketKind.Rookie, 13).Ok);
            Assert.AreEqual(13, gs.State.Tickets.Pending.Pref);
            Assert.AreEqual(1, gs.State.Tickets.Stock);
            o = gs.DeclareTicket(TicketKind.Intl);
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.TicketPending.Code, o.Code);
            Advance(gs, 40);
            Runner t = gs.State.Runners.Single(r => r.RecruitSource == RecruitSource.Ticket);
            Assert.AreEqual(13, t.Pref, "指定した県の選手が入学する");
            Advance(gs, 8);
            o = gs.DeclareTicket(TicketKind.Intl);
            Assert.IsTrue(o.Ok, o.Reason);
            Assert.IsNotNull(gs.State.Tickets.Pending);
        }

        // ---------------------------------------------------------------- 施設(R-11)
        [Test]
        public void UpgradeFacility_CostCapAndFunds()
        {
            GameSession gs = New();
            TeamState s = gs.State;
            Assert.AreEqual(1500, s.Funds);
            Assert.IsTrue(gs.UpgradeFacility("dorm").Ok);
            Assert.AreEqual(3, s.Fac.Dorm);
            Assert.AreEqual(1500 - (300 + 250 * 2), s.Funds, "費用 = 300 + 250 × 現在 Lv");
            Assert.AreEqual(3, gs.School.Fac.Dorm, "世界の側の自校にも反映される");
            OpResult cap = gs.UpgradeFacility("dorm");
            Assert.IsFalse(cap.Ok); Assert.AreEqual(OpErr.FacilityCap.Code, cap.Code);
            Assert.IsFalse(gs.UpgradeFacility("pool").Ok);
            s.Funds = 100;
            OpResult poor = gs.UpgradeFacility("scout");
            Assert.IsFalse(poor.Ok); Assert.AreEqual(OpErr.FacilityFunds.Code, poor.Code);
            Assert.AreEqual(2, s.Fac.Scout);
            Assert.AreEqual(100, s.Funds);
        }

        [Test]
        public void UpgradeRecommended_FollowsThePolicy_AndKeepsTheReserve()
        {
            GameSession gs = New(Difficulty.Low);
            TeamState s = gs.State;
            s.Funds = 4000;
            int n = gs.UpgradeRecommended();
            Assert.Greater(n, 0);
            Assert.GreaterOrEqual(s.Funds, WorldK.Reserve);
            Assert.AreEqual(FacilityPolicy.Order[0], "dorm");
            Assert.GreaterOrEqual(s.Fac.Dorm, s.Fac.Medical, "寮から先に上がる");
            Assert.LessOrEqual(s.Fac.Dorm, WorldMath.FacilityCap(gs.School.Rank()));
        }

        // ---------------------------------------------------------------- 年度末の段階(手動の強化)
        [Test]
        public void YearEnd_Manual_SettleOnly_WhenAutoUpgradeIsOff()
        {
            GameSession a = New(), b = New();
            a.AutoUpgrade = false;
            Advance(a, 48); Advance(b, 48);
            Assert.GreaterOrEqual(b.State.Fac.Dorm, a.State.Fac.Dorm);
            Assert.AreEqual(6, a.State.Fac.Dorm + a.State.Fac.Scout + a.State.Fac.Coach, "強化なし(中堅は Lv2)");
            Assert.Greater(a.State.Funds, b.State.Funds, "強化しなければ資金が残る");
        }
    }
}
