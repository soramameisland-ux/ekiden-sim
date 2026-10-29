using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
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
    /// <summary>月間計画(SC-16)・エントリー(SC-21)・区間オーダー(SC-22)の読み取りの API(08b)。</summary>
    public class PlanEntryOrderViewTests
    {
        private static GameSession Game(Difficulty d = Difficulty.Elite, ulong seed = 20261101UL) { return GameSession.New(seed, d); }

        private static void StepTo(GameSession gs, int yearWeek) { while (gs.YearWeek < yearWeek) gs.StepWeek(); }

        // ------------------------------------------------------------ 月間計画
        [Test]
        public void Plan_GroupsThemesWeeksAndFits()
        {
            GameSession gs = Game();
            PlanView p = gs.Plan();
            Assert.AreEqual(0, p.MonthIndex);
            Assert.AreEqual(0, p.WeekOfMonth);
            Assert.AreEqual(PlanRules.AutoPlan(gs.State.Plan.Focus, 0), p.RecommendedTheme);
            Assert.AreEqual(3, p.Groups.Count);
            Assert.AreEqual(gs.State.Runners.Count, p.Groups.Sum(g => g.Count));
            Assert.AreEqual(gs.State.GuidanceLeft, p.GuidanceLeft);
            foreach (GroupPlanView g in p.Groups)
            {
                Assert.AreEqual(gs.State.Runners.Count(r => r.Group == g.Group && r.IsInjured), g.Injured);
                Assert.AreEqual(gs.State.MonthlyPlan[g.Group], g.SelectedTheme);
                Assert.AreEqual(5, g.Options.Count, "基礎・スピード・山・夏合宿・回復(自由編集はメニューを決めてから)");
                Assert.AreEqual(1, g.Options.Count(o => o.Recommended), "推奨の印は 1 つ");
                Assert.AreEqual(1, g.Options.Count(o => o.Selected));
                foreach (ThemeOptionView o in g.Options)
                {
                    Assert.AreEqual(g.Count, o.FitGood + o.FitOk + o.FitLow, "相性の人数の合計 = グループの人数");
                    Menu[] menus = gs.Master.Themes[o.ThemeId];
                    Assert.AreEqual(4, o.Weeks.Count);
                    for (int i = 0; i < 4; i++) { Assert.AreEqual(menus[i].Key1, o.Weeks[i].Key1); Assert.AreEqual(menus[i].Key2, o.Weeks[i].Key2); Assert.AreEqual(menus[i].Mileage, o.Weeks[i].Mileage); }
                    if (g.Count > 0) { Assert.GreaterOrEqual(o.FatigueEndMax, o.FatigueEndAvg - 1e-9); Assert.GreaterOrEqual(o.ExpectedInjuries, 0.0); }
                }
            }
            Assert.IsTrue(p.Groups.Sum(g => g.Count) > 0 && p.Groups.Any(g => g.Count > 0));
        }

        [Test]
        public void Plan_ThemesGrowWhatTheyAreMeantTo_AndSelectionFollowsTheCommand()
        {
            GameSession gs = Game();
            GroupPlanView a = gs.Plan().Groups.First(g => g.Count >= 5);
            ThemeOptionView speed = a.Options.First(o => o.ThemeId == "th.speed"), baseB = a.Options.First(o => o.ThemeId == "th.base_build"), mount = a.Options.First(o => o.ThemeId == "th.mountain");
            Assert.Greater(speed.GainP[(int)SectionType.Fast], baseB.GainP[(int)SectionType.Fast], "スピード強化は短い区間の適性が伸びる");
            Assert.Greater(mount.GainP[(int)SectionType.Climb], baseB.GainP[(int)SectionType.Climb], "山対策は登坂の適性が伸びる");
            Assert.Greater(mount.GainP[(int)SectionType.Climb], speed.GainP[(int)SectionType.Climb]);
            Assert.IsTrue(gs.SetMonthlyPlan(a.Group, "th.speed").Ok);
            GroupPlanView after = gs.Plan().Groups[a.Group];
            Assert.AreEqual("th.speed", after.SelectedTheme);
            Assert.IsTrue(after.Options.First(o => o.ThemeId == "th.speed").Selected);
            Assert.IsFalse(after.Options.First(o => o.ThemeId == "th.base_build").Selected);
            // 自由編集のメニューを決めると、選択肢に th.custom が出る
            Assert.IsTrue(gs.SetCustomWeeks(a.Group, Enumerable.Range(0, 4).Select(i => new MenuSpec("ks.pace_run", "ks.build_up", "mil.std")).ToList()).Ok);
            GroupPlanView custom = gs.Plan().Groups[a.Group];
            Assert.AreEqual(6, custom.Options.Count);
            Assert.AreEqual("th.custom", custom.SelectedTheme);
            Assert.AreEqual("ks.pace_run", custom.Options.First(o => o.ThemeId == "th.custom").Weeks[0].Key1);
        }

        [Test]
        public void Plan_ForecastMatchesTheRealMonth_WhenNobodyGetsInjured()
        {
            // 月末の疲労の予測(R-05)は、故障が起きなければ実際の結果と一致する(03・08 §11.1)。月の頭に出した予測を、4 週あとの実際と比べる。
            GameSession gs = Game(Difficulty.Mid, 31UL);
            gs.State.Settings.PlanAuto = false;
            int compared = 0;
            for (int month = 0; month < 3; month++)
            {
                PlanView p = gs.Plan();
                var expect = new Dictionary<int, double>();
                foreach (GroupPlanView g in p.Groups)
                {
                    ThemeOptionView sel = g.Options.First(o => o.Selected);
                    // グループ全体の平均で比べる
                    expect[g.Group] = sel.FatigueEndAvg;
                }
                var before = gs.State.Runners.Where(r => !r.IsInjured).Select(r => r.Id).ToList();
                for (int w = 0; w < 4; w++) gs.StepWeek();
                foreach (GroupPlanView g in p.Groups)
                {
                    var members = gs.State.Runners.Where(r => r.Group == g.Group && before.Contains(r.Id) && !r.IsInjured).ToList();
                    if (members.Count < 3 || g.Injured > 0 || gs.State.Runners.Any(r => r.Group == g.Group && r.IsInjured)) continue;      // 故障が起きた月は比べない
                    double actual = members.Average(r => (double)r.Fatigue);
                    if (Math.Abs(actual - expect[g.Group]) > 6.0) Assert.Inconclusive("イベント・調整の影響で外れた月は比べない");   // 保険
                    Assert.AreEqual(expect[g.Group], actual, 6.0, "月 " + month + " グループ " + g.Group + " の月末の疲労の予測と実際");
                    compared++;
                }
            }
            Assert.Greater(compared, 0, "比べられた月がある");
        }

        [Test]
        public void Plan_NumbersEqualTheForecasterAndTheFitRules_ForEveryGroupAndTheme()
        {
            GameSession gs = Game(Difficulty.Mid, 3UL);
            // 疲労の高い選手を作って、『要注意』が数えられるようにする(4 月の頭。大会の調整期間にはまだ入らない)
            foreach (Runner r in gs.State.Runners.Where(x => !x.IsInjured).Take(4)) r.Fatigue = 88;                       // 安全網(85 以上)が働く
            PlanView p = gs.Plan();
            int checkedOptions = 0, attentionTotal = 0;
            foreach (GroupPlanView g in p.Groups)
            {
                List<Runner> members = gs.State.Runners.Where(r => r.Group == g.Group).ToList();
                foreach (ThemeOptionView o in g.Options)
                {
                    int good = 0, ok = 0, low = 0, attention = 0, safety = 0, fmax = 0;
                    double inj = 0, fend = 0;
                    foreach (Runner r in members)
                    {
                        double fit = r.Style.HasValue ? Presentation.FitView(gs.Master, r.Style.Value, r.Gifts, r.GiftRevealed, o.ThemeId) : Presentation.FitValue(gs.Master, null, o.ThemeId);
                        char m = Presentation.FitMark(fit);
                        if (m == '◎') good++; else if (m == '〇') ok++; else low++;
                        var weeks = gs.Master.Themes[o.ThemeId].Select(menu => new PlannedWeek { Menu = r.IsInjured ? gs.Master.Injured : menu, MonthIndex = 0, Camp = o.ThemeId == "th.camp" }).ToList();
                        ForecastResult f = Forecaster.Run(gs.Master, r, weeks, gs.State.Fac, true, r.BondAvg, gs.State.ModsFor(r));
                        if (f.FMax >= 70) attention++;
                        safety += f.SafetyWeeks;
                        inj += f.PInjury;
                        fend += f.End.Fatigue;
                        fmax = Math.Max(fmax, f.End.Fatigue);
                    }
                    Assert.AreEqual(good, o.FitGood, g.Group + " " + o.ThemeId + " ◎");
                    Assert.AreEqual(ok, o.FitOk, g.Group + " " + o.ThemeId + " 〇");
                    Assert.AreEqual(low, o.FitLow, g.Group + " " + o.ThemeId + " △");
                    Assert.AreEqual(attention, o.Attention, g.Group + " " + o.ThemeId + " 要注意");
                    Assert.AreEqual(safety, o.SafetyWeeks, g.Group + " " + o.ThemeId + " 安全網");
                    Assert.AreEqual(inj, o.ExpectedInjuries, 1e-9);
                    Assert.AreEqual(members.Count > 0 ? fend / members.Count : 0.0, o.FatigueEndAvg, 1e-9);
                    Assert.AreEqual(fmax, o.FatigueEndMax);
                    checkedOptions++;
                    attentionTotal += attention;
                }
            }
            Assert.AreEqual(15, checkedOptions);
            Assert.Greater(attentionTotal, 0, "疲労の高い選手がいれば要注意が数えられる(検査が空振りしない)");
            Assert.Greater(p.Groups.SelectMany(g => g.Options).Sum(o => o.SafetyWeeks), 0, "疲労 85 以上の選手がいれば、安全網が働く週が数えられる(検査が空振りしない)");
        }

        [Test]
        public void ThemeFit_WorksForCustomWeeks_AndEqualsTheThemeWhenTheMenusAreTheSame()
        {
            GameSession gs = Game();
            Runner r = gs.State.Runners.OrderBy(x => x.Id).First(x => x.Style.HasValue);
            foreach (string th in new[] { "th.speed", "th.mountain", "th.base_build" })
                Assert.AreEqual(Presentation.FitValue(gs.Master, r.Gm, th), Presentation.FitValue(gs.Master, r.Gm, gs.Master.Themes[th]), 1e-12, th + ": メニューの並びでも同じ値");
            // 自由編集に th.speed と同じ 4 週を入れると、相性の印も同じ。詳細画面・計画画面が落ちない(以前は th.custom で KeyNotFound)
            var weeks = gs.Master.Themes["th.speed"].Select(m => new MenuSpec(m.Key1, m.Key2, m.Mileage)).ToList();
            Assert.IsTrue(gs.SetCustomWeeks(r.Group, weeks).Ok);
            RunnerDetailView d = gs.RunnerDetail(r.Id);
            Assert.AreEqual("th.custom", d.ThemeId);
            gs.SetMonthlyPlan(r.Group, "th.speed");
            Assert.AreEqual(gs.RunnerDetail(r.Id).ThemeFitMark, d.ThemeFitMark, "同じメニューなら同じ相性");
            GroupPlanView g = gs.Plan().Groups[r.Group];
            ThemeOptionView custom = g.Options.First(o => o.ThemeId == "th.custom"), speed = g.Options.First(o => o.ThemeId == "th.speed");
            Assert.AreEqual(speed.FitGood, custom.FitGood);
            Assert.AreEqual(speed.FitLow, custom.FitLow);
            Assert.AreEqual(speed.FatigueEndAvg, custom.FatigueEndAvg, 1e-9);
        }

        // ------------------------------------------------------------ エントリー
        [Test]
        public void Entry_SummaryRosterAndProblems()
        {
            GameSession gs = Game();
            EntryView e = gs.Entry(RaceKind.Fall);
            Assert.AreEqual(10, e.Capacity);
            Assert.AreEqual(6, e.Required);
            Assert.AreEqual(2, e.IntlRegisterMax);
            Assert.AreEqual(1, e.IntlRunMax);
            Assert.IsFalse(e.Open, "締切の前");
            Assert.AreEqual(TeamWeek.EntryDeadlineWeeks[0], e.DeadlineWeeksLeft);
            StepTo(gs, TeamWeek.EntryDeadlineWeeks[0]);
            e = gs.Entry(RaceKind.Fall);
            Assert.AreEqual(e.Qualified, e.Open, "締切の週から登録できる(出場権があれば)");
            Assert.AreEqual(0, e.DeadlineWeeksLeft);
            Assert.AreEqual(gs.State.Entries[0].Count, e.Registered, "締切の週におまかせ登録が入る");
            Assert.AreEqual(gs.State.Runners.Count, e.Roster.Count);
            CollectionAssert.AreEquivalent(gs.State.Entries[0], e.Roster.Where(r => r.Entered).Select(r => r.Row.Id).ToList());
            CollectionAssert.AreEqual(e.Entered.OrderByDescending(id => TeamState.LongP(gs.State.FindRunner(id))).ThenBy(id => id).ToList(), e.Entered, "登録メンバーは力の高い順");
            foreach (EntryRowView r in e.Roster)
            {
                var dv = gs.State.FindRunner(r.Row.Id).Stats.Select(x => (double)x).ToArray();
                double best = Enum.GetValues(typeof(SectionType)).Cast<SectionType>().Max(t => RaceTables.Rating(dv, t));
                Assert.AreEqual(best, r.BestFit, 1e-12, "区間適性の最高のタイプ");
                Assert.AreEqual(RaceTables.Rating(dv, r.BestType), r.BestFit, 1e-12);
                Assert.AreEqual(gs.State.FindRunner(r.Row.Id).InjurySeverity != InjurySeverity.Severe, r.CanEnter, "登録できないのは重傷だけ(04 E3)");
            }
            Assert.IsEmpty(e.Problems.Where(p => p.Code != "entry.short"), "おまかせ登録は検証を満たす");
            // 出走人数に足りない登録 → 指摘(確認を経て登録できる)
            Assert.IsTrue(gs.SetEntries(RaceKind.Fall, gs.State.Entries[0].Take(4).ToList(), true).Ok);
            Assert.IsTrue(gs.Entry(RaceKind.Fall).Problems.Any(p => p.Code == "entry.short" && p.Value == 6));
            // 登録した選手が故障 → 差し替えの指摘
            gs.AutoEntries(RaceKind.Fall);
            Runner victim = gs.State.FindRunner(gs.State.Entries[0][0]);
            victim.InjurySeverity = InjurySeverity.Moderate;
            victim.InjuryWeeksLeft = 4;
            EntryView injured = gs.Entry(RaceKind.Fall);
            Assert.IsTrue(injured.Problems.Any(p => p.Code == "entry.injured" && p.RunnerId == victim.Id));
            Assert.IsTrue(injured.Roster.First(r => r.Row.Id == victim.Id).CanEnter, "中傷は登録できる(04 E3)。大会までに治らなければ出られない");
            victim.InjurySeverity = InjurySeverity.Minor;
            Assert.IsFalse(gs.Entry(RaceKind.Fall).Problems.Any(p => p.Code == "entry.injured"), "軽傷は出られるので指摘しない");
            victim.InjurySeverity = InjurySeverity.Severe;
            Assert.IsFalse(gs.Entry(RaceKind.Fall).Roster.First(r => r.Row.Id == victim.Id).CanEnter);
            Assert.AreEqual("injured", gs.Entry(RaceKind.Fall).Roster.First(r => r.Row.Id == victim.Id).BlockedReason);
        }

        [Test]
        public void Entry_OpenFollowsTheWindow_AndNonQualifiersAreMarked()
        {
            GameSession gs = Game(Difficulty.Low, 9UL);
            for (int w = 0; w < 30; w++)
            {
                for (int k = 0; k < 3; k++)
                {
                    EntryView e = gs.Entry((RaceKind)k);
                    bool inWindow = gs.YearWeek >= TeamWeek.EntryDeadlineWeeks[k] && gs.YearWeek <= Calendar.RaceWeeks[k];
                    Assert.AreEqual(e.Qualified && inWindow, e.Open, "週 " + gs.YearWeek + " 第" + (k + 1) + "戦");
                }
                gs.StepWeek();
            }
        }

        // ------------------------------------------------------------ 区間オーダー
        [Test]
        public void Order_SuggestedThenSubmitted_SectionsBenchAndTotals()
        {
            GameSession gs = Game();
            StepTo(gs, TeamWeek.EntryDeadlineWeeks[0] + 1);
            RaceKind kind = RaceKind.Fall;
            if (!gs.Entry(kind).Qualified) Assert.Inconclusive("この世界では第一戦に出られない");
            OrderView o = gs.Order(kind);
            Assert.IsTrue(o.Open);
            Assert.IsFalse(o.Submitted, "出していなければ、おまかせ(最適な配置)を見せる");
            int[] sug = gs.SuggestOrder(kind);
            CollectionAssert.AreEqual(sug, o.Sections.Select(s => s.RunnerId.Value).ToArray());
            Assert.AreEqual(RaceTables.Course(kind).Count, o.Sections.Count);
            double sum = 0;
            for (int i = 0; i < o.Sections.Count; i++)
            {
                OrderSectionView c = o.Sections[i];
                SectionDef def = RaceTables.Course(kind)[i];
                Assert.AreEqual(def.Distance, c.Distance);
                Assert.AreEqual(def.Type, c.Type);
                KeyValuePair<double, char> fit = gs.SectionFit(kind, c.RunnerId.Value, i);
                Assert.AreEqual(fit.Key, c.Fit);
                Assert.AreEqual(fit.Value, c.Mark);
                Assert.AreEqual(RaceMath.BaseTime(def.Distance, def.Terrain, fit.Key), c.ExpectedSec, 1e-9);
                Assert.AreEqual(c.RunnerId.Value, c.Runner.Id);
                sum += c.ExpectedSec;
            }
            Assert.AreEqual(sum, o.TotalExpectedSec, 1e-9);
            CollectionAssert.AreEquivalent(gs.State.Entries[0].Except(sug).ToList(), o.Bench.Select(b => b.Id).ToList(), "補欠 = 登録のうち出走しない");
            Assert.AreEqual(gs.Forecast(kind).Length, o.Forecast.Count, "環境の予報(SC-23 と同じ)");
            Assert.IsNotEmpty(o.Forecast);
            Assert.IsEmpty(o.Problems);
            // 提出する
            int[] reversed = sug.Reverse().ToArray();
            Assert.IsTrue(gs.SetOrder(kind, reversed).Ok);
            OrderView s2 = gs.Order(kind);
            Assert.IsTrue(s2.Submitted);
            CollectionAssert.AreEqual(reversed, s2.Sections.Select(s => s.RunnerId.Value).ToArray());
            Assert.IsEmpty(s2.Problems);
        }

        [Test]
        public void Order_ClosesWhenTheRaceWeekIsResolved()
        {
            GameSession gs = Game();
            StepTo(gs, Calendar.RaceWeeks[0]);
            if (!gs.Entry(RaceKind.Fall).Qualified) Assert.Inconclusive("この世界では第一戦に出られない");
            Assert.IsTrue(gs.Order(RaceKind.Fall).Open, "大会の週の初めまでは出せる");
            Assert.IsTrue(gs.Home().Next.OrderOpen);
            gs.ResolveTraining();
            Assert.IsFalse(gs.Order(RaceKind.Fall).Open, "週の練習が解決されたら、オーダーは出せない");
            Assert.IsFalse(gs.Home().Next.OrderOpen);
            Assert.IsTrue(gs.Home().Next.EntryOpen, "登録は大会の週の間は直せる(SetEntries の期間)");
        }

        [Test]
        public void OrderCandidates_AreSortedByFit_AndShowWhereEachIsAssigned()
        {
            GameSession gs = Game();
            StepTo(gs, TeamWeek.EntryDeadlineWeeks[2] + 1);
            RaceKind kind = RaceKind.NewYear;
            if (!gs.Entry(kind).Qualified) Assert.Inconclusive("この世界では第三戦に出られない");
            int[] order = gs.SuggestOrder(kind);
            for (int section = 0; section < order.Length; section += 3)
            {
                List<OrderCandidateView> c = gs.OrderCandidates(kind, section);
                Assert.AreEqual(gs.State.Entries[2].Count, c.Count);
                for (int i = 1; i < c.Count; i++) Assert.GreaterOrEqual(c[i - 1].Fit, c[i].Fit, "適性値の高い順");
                foreach (OrderCandidateView x in c)
                {
                    Assert.AreEqual(gs.SectionFit(kind, x.Row.Id, section).Key, x.Fit);
                    int at = Array.IndexOf(order, x.Row.Id);
                    Assert.AreEqual(at >= 0 ? (int?)at : null, x.AssignedSection);
                }
            }
            Assert.Throws<ArgumentException>(() => gs.OrderCandidates(kind, 99));
        }

        [Test]
        public void Order_ReportsProblems_ForInjuredAndUnregisteredRunners()
        {
            GameSession gs = Game();
            StepTo(gs, TeamWeek.EntryDeadlineWeeks[0] + 1);
            RaceKind kind = RaceKind.Fall;
            if (!gs.Entry(kind).Qualified) Assert.Inconclusive("この世界では第一戦に出られない");
            int[] sug = gs.SuggestOrder(kind);
            Assert.IsTrue(gs.SetOrder(kind, sug).Ok);
            Runner r = gs.State.FindRunner(sug[2]);
            r.InjurySeverity = InjurySeverity.Moderate;
            r.InjuryWeeksLeft = 3;
            OrderView o = gs.Order(kind);
            Assert.IsTrue(o.Problems.Any(p => p.Code == "order.injured" && p.RunnerId == r.Id));
            Assert.IsFalse(o.Problems.Any(p => p.Code == "order.dup"));
        }

        [Test]
        public void ProblemCodes_AllHaveTexts()
        {
            // 検証の指摘のコードは、すべて文字列テーブルに本文がある(StringsCoverageTests が ja.json を確かめる)
            Assert.AreEqual(8, ViewProblem.Codes.Length);
            Assert.AreEqual(ViewProblem.Codes.Length, ViewProblem.Codes.Distinct().Count());
        }
    }
}
