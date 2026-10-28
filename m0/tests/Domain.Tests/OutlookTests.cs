using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>次の大会の調子の見込み(08 R-05 ⑥。08b G-15)。実際の週の解決と突き合わせる。</summary>
    public class OutlookTests
    {
        private static GameSession AtWeek(ulong seed, Difficulty d, int yearWeek)
        {
            GameSession gs = GameSession.New(seed, d);
            while (gs.YearWeek != yearWeek) gs.StepWeek();
            return gs;
        }

        [Test]
        public void OutlookExists_OnlyWithinEightWeeks_ForARaceTheSchoolCanEnter()
        {
            int fall = Calendar.RaceWeeks[0];
            GameSession gs = AtWeek(11UL, Difficulty.Elite, fall - 9);
            Runner r = gs.State.Runners[0];
            Assert.IsNull(gs.FormOutlook(r.Id, RaceKind.Fall), "9 週先は出さない");
            Assert.IsNull(gs.Plan().OutlookRace);
            gs.StepWeek();
            FormOutlookView v = gs.FormOutlook(r.Id, RaceKind.Fall);
            Assert.IsNotNull(v, "8 週先から出す");
            Assert.AreEqual(8, v.WeeksAhead);
            Assert.AreEqual(RaceKind.Fall, gs.Plan().OutlookRace);
            Assert.AreEqual(8, gs.Plan().OutlookWeeks);
            Assert.AreEqual(1.0, v.Form.Sum(), 1e-9, "調子の確率の和は 1");
            Assert.IsTrue(v.Form.All(p => p >= 0.0 && p <= 1.0));
            Assert.AreEqual((1.0 - v.PInjury) * (v.Form[3] + v.Form[4]), v.PGood, 1e-12);
            Assert.IsNull(gs.FormOutlook(r.Id, RaceKind.National), "もっと先の大会は出さない");
            Assert.IsNull(gs.FormOutlook(999999, RaceKind.Fall), "いない選手は null");
        }

        [Test]
        public void SelectedThemeGoodChance_EqualsTheGroupAverageOfTheRunnerOutlooks_AndOthersDiffer()
        {
            GameSession gs = AtWeek(12UL, Difficulty.Mid, Calendar.RaceWeeks[0] - 6);
            string rec = PlanRules.AutoPlan(gs.State.Plan.Focus, Calendar.RaceWeeks[0] / 4 - 1);
            Assert.IsTrue(gs.SetMonthlyPlan(0, rec == "th.recovery" ? "th.speed" : "th.recovery").Ok, "推奨と違うテーマを選ぶ(今月の計画 ≠ 推奨プランでも一致すること)");
            Assert.IsTrue(gs.SetMonthlyPlan(1, rec == "th.mountain" ? "th.speed" : "th.mountain").Ok);
            PlanView pv = gs.Plan();
            Assert.IsTrue(pv.OutlookRace.HasValue);
            bool anyDifferent = false;
            foreach (GroupPlanView g in pv.Groups)
            {
                var members = gs.State.Runners.Where(r => r.Group == g.Group).ToList();
                if (members.Count == 0) { Assert.IsTrue(g.Options.All(o => !o.GoodChance.HasValue)); continue; }
                ThemeOptionView sel = g.Options.Single(o => o.Selected);
                double avg = members.Average(r => gs.FormOutlook(r.Id, pv.OutlookRace.Value).PGood);
                Assert.AreEqual(avg, sel.GoodChance.Value, 1e-12, "選んでいるテーマの値 = 選手ごとの見込みの平均");
                Assert.IsTrue(g.Options.All(o => o.GoodChance.HasValue && o.GoodChance.Value >= 0.0 && o.GoodChance.Value <= 1.0));
                if (g.Options.Select(o => Math.Round(o.GoodChance.Value, 6)).Distinct().Count() > 1) anyDifferent = true;
            }
            Assert.IsTrue(anyDifferent, "テーマによって『好調以上』の見込みは変わる");
        }

        [Test]
        public void EntryAndOrderRows_CarryTheOutlook_AssumingRegistered()
        {
            GameSession gs = AtWeek(13UL, Difficulty.Elite, Calendar.RaceWeeks[0] - 3);
            EntryView ev = gs.Entry(RaceKind.Fall);
            Assert.IsTrue(ev.Roster.All(row => row.Outlook != null));
            foreach (EntryRowView row in ev.Roster)
            {
                FormOutlookView o = row.Outlook;
                Assert.AreEqual(3, o.WeeksAhead);
                Assert.AreEqual(RaceKind.Fall, o.Kind);
                Assert.AreEqual(row.Row.Injury != InjurySeverity.None, o.Injured);
                if (!o.Injured) Assert.IsTrue(o.Tapered, "登録すると調整(ピーキング)の週が入る");
                if (o.Injured) Assert.AreEqual(0.0, o.PGood);
            }
            // 登録済みの選手は、FormOutlook(登録の見込みなしの版)と同じ
            foreach (int id in ev.Entered.Take(3))
            {
                FormOutlookView a = ev.Roster.Single(x => x.Row.Id == id).Outlook, b = gs.FormOutlook(id, RaceKind.Fall);
                Assert.AreEqual(a.PGood, b.PGood, 1e-12);
                Assert.AreEqual(a.FatigueAtRace, b.FatigueAtRace);
            }
            OrderView ov = gs.Order(RaceKind.Fall);
            foreach (OrderSectionView sec in ov.Sections.Where(x => x.RunnerId.HasValue))
                Assert.AreEqual(gs.FormOutlook(sec.RunnerId.Value, RaceKind.Fall).PGood, sec.Outlook.PGood, 1e-12);
            foreach (OrderCandidateView c in gs.OrderCandidates(RaceKind.Fall, 0))
                Assert.AreEqual(gs.FormOutlook(c.Row.Id, RaceKind.Fall).PGood, c.Outlook.PGood, 1e-12);
        }

        [Test]
        public void TaperRaisesTheChanceOfAGoodForm_WhenTheRunnerIsRegistered()
        {
            GameSession gs = AtWeek(14UL, Difficulty.Elite, Calendar.RaceWeeks[0] - 6);
            // 登録の前: 登録した場合(調整あり)のほうが、登録しない場合より『好調以上』の見込みが高い(03 §5.3 のピーキング)。
            // 登録しない場合 = おまかせ登録に入らない選手の FormOutlook(調整なし)
            Assert.AreEqual(0, gs.State.Entries[0].Count, "試験の前提: まだ登録していない(締切の前)");
            var auto = new HashSet<int>(TeamRacing.AutoRegister(gs.State, RaceKind.Fall));
            EntryView ev = gs.Entry(RaceKind.Fall);
            int better = 0, tested = 0;
            foreach (EntryRowView row in ev.Roster.Where(x => x.Row.Injury == InjurySeverity.None))
            {
                Assert.IsTrue(row.Outlook.Tapered, "登録した場合は調整が入る");
                FormOutlookView plain = gs.FormOutlook(row.Row.Id, RaceKind.Fall);
                if (auto.Contains(row.Row.Id)) { Assert.IsTrue(plain.Tapered, "おまかせ登録に入る選手は、登録の見込みで調整が入る"); Assert.AreEqual(row.Outlook.PGood, plain.PGood, 1e-12); continue; }
                Assert.IsFalse(plain.Tapered, "おまかせ登録に入らない選手は調整が入らない");
                tested++;
                if (row.Outlook.PGood > plain.PGood) better++;
            }
            Assert.Greater(tested, 0, "試験の前提: おまかせ登録に入らない選手がいる");
            Assert.AreEqual(tested, better, "調整が入る分、登録した場合は必ず高い");
        }

        // ------------------------------------------------------------ 実際の週の解決との突き合わせ
        [Test]
        public void Prediction_AgreesWithTheRealWeeks_FatigueNearlyExactly_FormCalibrated()
        {
            int total = 0, within3 = 0, exactFirstRace = 0, firstRaceTotal = 0, good = 0;
            double predictedGood = 0.0;
            for (ulong seed = 21; seed <= 30; seed++)
                for (int ki = 0; ki < 3; ki++)
                {
                    GameSession gs = GameSession.New(seed, seed % 2 == 0 ? Difficulty.Elite : Difficulty.Mid);
                    int rw = Calendar.RaceWeeks[ki];
                    while (gs.YearWeek != rw - 6) gs.StepWeek();
                    var pred = new Dictionary<int, FormOutlookView>();
                    foreach (Runner r in gs.State.Runners.Where(x => !x.IsInjured)) pred[r.Id] = gs.FormOutlook(r.Id, (RaceKind)ki);
                    var injuredDuring = new HashSet<int>();
                    while (gs.YearWeek != rw)
                    {
                        gs.StepWeek();
                        foreach (Runner r in gs.State.Runners.Where(x => x.IsInjured)) injuredDuring.Add(r.Id);
                    }
                    gs.ResolveTraining();
                    foreach (var kv in pred)
                    {
                        Runner r = gs.State.FindRunner(kv.Key);
                        if (r == null || r.IsInjured || injuredDuring.Contains(r.Id)) continue;          // 故障は確率で出す。起きなかった選手だけを見る
                        total++;
                        int diff = Math.Abs(r.Fatigue - kv.Value.FatigueAtRace);
                        if (diff <= 3) within3++;
                        if (ki == 0) { firstRaceTotal++; if (diff == 0) exactFirstRace++; }
                        predictedGood += kv.Value.Form[3] + kv.Value.Form[4];
                        if (r.Form >= 1) good++;
                        // 大会の週の練習が解決されたあとは、調子は確定している
                        FormOutlookView done = gs.FormOutlook(r.Id, (RaceKind)ki);
                        Assert.IsTrue(done.Determined);
                        Assert.AreEqual(0, done.WeeksAhead);
                        Assert.AreEqual(1.0, done.Form[r.Form + 2]);
                        Assert.AreEqual(r.Form >= 1 ? 1.0 : 0.0, done.PGood);
                        Assert.AreEqual(r.Fatigue, done.FatigueAtRace);
                    }
                }
            Assert.Greater(total, 800, "試験の前提: 十分な数の選手 × 大会");
            Assert.GreaterOrEqual(within3 / (double)total, 0.93, "疲労の見込みは、故障がなければほぼ実際の結果と一致する(±3 以内)");
            Assert.GreaterOrEqual(exactFirstRace / (double)firstRaceTotal, 0.93, "間に別の大会がない第一戦は、ほとんど完全に一致する");
            Assert.That(good / (double)total, Is.EqualTo(predictedGood / total).Within(0.04), "『好調以上』の見込みの平均 = 実際に好調以上だった割合");
        }

        [Test]
        public void AnIndividualMenu_ChangesThatRunnersOutlook_UntilTheEndOfTheMonth()
        {
            GameSession gs = AtWeek(16UL, Difficulty.Elite, Calendar.RaceWeeks[0] - 3);
            Runner r = gs.State.Runners.First(x => !x.IsInjured);
            FormOutlookView before = gs.FormOutlook(r.Id, RaceKind.Fall);
            Menu hard = gs.Master.Themes["th.speed"][1];
            Assert.IsTrue(gs.GuideMenu(r.Id, new MenuSpec(hard.Key1, hard.Key2, hard.Mileage), true).Ok);
            FormOutlookView after = gs.FormOutlook(r.Id, RaceKind.Fall);
            Assert.IsTrue(after.PGood != before.PGood || after.FatigueAtRace != before.FatigueAtRace, "個別指導のメニューは、調整(ピーキング)より優先される");
            Assert.IsTrue(gs.CancelGuide(r.Id).Ok);
            FormOutlookView back = gs.FormOutlook(r.Id, RaceKind.Fall);
            Assert.AreEqual(before.PGood, back.PGood, 1e-12);
            Assert.AreEqual(before.FatigueAtRace, back.FatigueAtRace);
        }

        [Test]
        public void AnIndividualMenu_EndsWithTheMonth_AsInTheRealWeeks()
        {
            int guided = 0, close = 0;
            for (ulong seed = 31; seed <= 38; seed++)
            {
                GameSession gs = AtWeek(seed, Difficulty.Elite, Calendar.RaceWeeks[0] - 3);          // 22 週: 5 月目(20〜23 週)の途中。大会は 25 週
                var picked = new List<Runner>();
                Menu hard = gs.Master.Themes["th.speed"][1];
                foreach (Runner r in gs.State.Runners.Where(x => !x.IsInjured && x.Fatigue < 60).Take(gs.State.GuidanceLeft))
                    if (gs.GuideMenu(r.Id, new MenuSpec(hard.Key1, hard.Key2, hard.Mileage), true).Ok) picked.Add(r);
                var pred = picked.ToDictionary(r => r.Id, r => gs.FormOutlook(r.Id, RaceKind.Fall));
                var injured = new HashSet<int>();
                while (gs.YearWeek != Calendar.RaceWeeks[0]) { gs.StepWeek(); foreach (Runner r in gs.State.Runners.Where(x => x.IsInjured)) injured.Add(r.Id); }
                gs.ResolveTraining();
                foreach (Runner p in picked)
                {
                    Runner r = gs.State.FindRunner(p.Id);
                    if (injured.Contains(r.Id) || r.IsInjured) continue;
                    guided++;
                    if (Math.Abs(r.Fatigue - pred[r.Id].FatigueAtRace) <= 3) close++;
                }
            }
            Assert.Greater(guided, 10, "試験の前提: 個別指導を受けた選手が十分にいる");
            Assert.GreaterOrEqual(close / (double)guided, 0.85, "個別指導は月末まで。そのあとの 24〜25 週は調整に戻る");
        }

        [Test]
        public void InjuredRunner_HasNoChanceOfAGoodForm()
        {
            GameSession gs = AtWeek(15UL, Difficulty.Elite, Calendar.RaceWeeks[0] - 4);
            Runner r = gs.State.Runners[2];
            r.InjurySeverity = InjurySeverity.Moderate; r.InjuryWeeksLeft = 3;
            FormOutlookView v = gs.FormOutlook(r.Id, RaceKind.Fall);
            Assert.IsTrue(v.Injured);
            Assert.AreEqual(0.0, v.PGood);
            Assert.IsTrue(gs.Entry(RaceKind.Fall).Roster.Single(x => x.Row.Id == r.Id).Outlook.Injured);
        }

        [Test]
        public void TheSafetyNetThreshold_FollowsTheSetting_InTheForecast()
        {
            var m = TrainingMaster.CreateDefault();
            var runner = new Runner { Grade = 2, GrowthType = GrowthType.Normal, Fatigue = 80, Motivation = 3 };
            for (int i = 0; i < StatInfo.Count; i++) { runner.Stats[i] = 58; runner.Potential[i] = 80; }
            var weeks = new List<Ekiden.Domain.Screens.PlannedWeek> { new Ekiden.Domain.Screens.PlannedWeek { Menu = m.Themes["th.base_build"][0], MonthIndex = 6 } };
            Ekiden.Domain.Screens.ForecastResult at85 = Ekiden.Domain.Screens.Forecaster.Run(m, runner, weeks, FacilityLevels.AllTwo, true, 0.0, null, 85);
            Ekiden.Domain.Screens.ForecastResult at75 = Ekiden.Domain.Screens.Forecaster.Run(m, runner, weeks, FacilityLevels.AllTwo, true, 0.0, null, 75);
            Ekiden.Domain.Screens.ForecastResult off = Ekiden.Domain.Screens.Forecaster.Run(m, runner, weeks, FacilityLevels.AllTwo, false, 0.0, null, 75);
            Assert.AreEqual(0, at85.SafetyWeeks, "疲労 80 は 85 の閾値では安全網が働かない");
            Assert.AreEqual(1, at75.SafetyWeeks, "75 の閾値なら働く");
            Assert.AreEqual(0, off.SafetyWeeks, "無効なら働かない");
            Assert.AreNotEqual(at85.F[0], at75.F[0], "安全網が働くとメニューが回復に替わる");
        }
    }
}
