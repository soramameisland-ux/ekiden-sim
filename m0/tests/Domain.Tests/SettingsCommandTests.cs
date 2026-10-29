using System.Linq;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>設定を変える命令(08 R-13)。画面が <c>State.Settings</c> を直接書かないための入口。</summary>
    public class SettingsCommandTests
    {
        private static GameSession New() { return GameSession.New(20261030UL, Difficulty.Mid); }

        [Test]
        public void SpectateMode_AcceptsTheThreeModes_AndRejectsOthersWithoutChangingAnything()
        {
            GameSession gs = New();
            foreach (string m in new[] { "Digest", "ResultOnly", "Full" })
            {
                Assert.IsTrue(gs.SetSpectateMode(m).Ok, m);
                Assert.AreEqual(m, gs.State.Settings.SpectateMode);
            }
            OpResult bad = gs.SetSpectateMode("full");
            Assert.IsFalse(bad.Ok); Assert.AreEqual(OpErr.SettingInvalid.Code, bad.Code);
            Assert.AreEqual("Full", gs.State.Settings.SpectateMode, "拒否した操作は何も変えない");
            Assert.IsFalse(gs.SetSpectateMode(null).Ok);
            CollectionAssert.AreEqual(new[] { "Full", "Digest", "ResultOnly" }, GameSession.SpectateModes);
        }

        [Test]
        public void SpectateMode_ChangesOnlyTheStops_NotTheResults()
        {
            // Full のときだけ当日(RaceDayPrep)で止まる。結果(順位・タイム)はどの観戦モードでも同じ(04 §14.5)。
            string Run(string mode)
            {
                GameSession gs = GameSession.New(20261030UL, Difficulty.Low);
                gs.SetSpectateMode(mode);
                var flow = new GameFlow(gs, autoUpgrade: true, autoTickets: true);
                gs.State.Settings.EventAuto = true;
                int done = 0;
                while (done < 1) { Stop s = flow.Advance(); if (s.Kind == StopKind.YearEndTurnover) done++; }
                return string.Join(",", gs.Stats.SelectMany(st => st.Place));
            }
            string full = Run("Full"), digest = Run("Digest"), only = Run("ResultOnly");
            Assert.AreEqual(full, digest); Assert.AreEqual(full, only);
        }

        [Test]
        public void PlanAuto_OnAppliesTheRecommendedPlanAtOnce_OffLeavesThePlan()
        {
            GameSession gs = New();
            Assert.IsTrue(gs.SetPlanAuto(false).Ok);
            Assert.IsFalse(gs.State.Settings.PlanAuto);
            Assert.IsTrue(gs.SetMonthlyPlan(0, "th.speed").Ok);
            string before = gs.State.MonthlyPlan[0];
            Assert.IsTrue(gs.SetPlanAuto(false).Ok);
            Assert.AreEqual(before, gs.State.MonthlyPlan[0], "オフにしても計画は変わらない");
            Assert.IsTrue(gs.SetPlanAuto(true).Ok);
            Assert.IsTrue(gs.State.Settings.PlanAuto);
            string[] rec = { gs.State.MonthlyPlan[0], gs.State.MonthlyPlan[1], gs.State.MonthlyPlan[2] };
            GameSession ref2 = New();
            ref2.ApplyRecommendedPlan();
            CollectionAssert.AreEqual(new[] { ref2.State.MonthlyPlan[0], ref2.State.MonthlyPlan[1], ref2.State.MonthlyPlan[2] }, rec, "オンにしたら今月の推奨プランに引き直す");
        }

        [Test]
        public void AutoFlags_AndSafety_AreSet_AndSafetyRejectsOtherValues()
        {
            GameSession gs = New();
            Assert.IsTrue(gs.SetEventAuto(false).Ok); Assert.IsFalse(gs.State.Settings.EventAuto);
            Assert.IsTrue(gs.SetScoutAuto(false).Ok); Assert.IsFalse(gs.State.Settings.ScoutAuto);
            foreach (int f in new[] { 75, 0, 85 }) { Assert.IsTrue(gs.SetAutoSafety(f).Ok); Assert.AreEqual(f, gs.State.Settings.AutoSafetyF); }
            foreach (int bad in new[] { 1, 80, -5, 100 })
            {
                OpResult r = gs.SetAutoSafety(bad);
                Assert.IsFalse(r.Ok, bad.ToString()); Assert.AreEqual(OpErr.SettingInvalid.Code, r.Code);
                Assert.AreEqual(85, gs.State.Settings.AutoSafetyF, "拒否した操作は何も変えない");
            }
        }
    }
}
