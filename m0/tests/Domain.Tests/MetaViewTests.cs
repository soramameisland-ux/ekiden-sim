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
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>設定(SC-04)・年間予定(SC-09)・クリア(SC-15)・個別指導(SC-20)の読み取りの API(08b)。実際の状態と独立に数え直した値と突き合わせる。</summary>
    public class MetaViewTests
    {
        private static GameSession New(Difficulty d = Difficulty.Mid, ulong seed = 20261031UL) { return GameSession.New(seed, d); }

        private static void Advance(GameSession gs, int weeks) { for (int i = 0; i < weeks; i++) gs.StepWeek(); }

        // ------------------------------------------------------------ 設定
        [Test]
        public void Settings_ReflectTheCommands_AndOfferTheChoices()
        {
            GameSession gs = New();
            SettingsView v = gs.Settings();
            Assert.AreEqual("Full", v.SpectateMode);
            CollectionAssert.AreEqual(new[] { "Full", "Digest", "ResultOnly" }, v.SpectateModes);
            CollectionAssert.AreEqual(new[] { 75, 85, 0 }, v.AutoSafetyChoices);
            Assert.AreEqual(85, v.AutoSafetyF);
            Assert.IsTrue(v.PlanAuto && v.EventAuto && v.ScoutAuto, "新規ゲームはおまかせ");
            gs.SetSpectateMode("ResultOnly"); gs.SetAutoSafety(0); gs.SetPlanAuto(false); gs.SetEventAuto(false); gs.SetScoutAuto(false);
            v = gs.Settings();
            Assert.AreEqual("ResultOnly", v.SpectateMode); Assert.AreEqual(0, v.AutoSafetyF);
            Assert.IsFalse(v.PlanAuto); Assert.IsFalse(v.EventAuto); Assert.IsFalse(v.ScoutAuto);
            v.SpectateModes[0] = "x"; v.AutoSafetyChoices[0] = -1;                               // 返した配列を書き換えても、次の呼び出しには響かない
            Assert.AreEqual("Full", gs.Settings().SpectateModes[0]); Assert.AreEqual(75, gs.Settings().AutoSafetyChoices[0]);
        }

        // ------------------------------------------------------------ 年間予定
        [Test]
        public void Calendar_ListsTheYear_AsTheSpecTable_AndTheFlagsFollowTheMonths()
        {
            GameSession gs = New(Difficulty.Club);                                               // シード外(予選会を戦う。Low は最初の年度はシード)
            CalendarView v = gs.YearCalendar();
            Assert.AreEqual(12, v.Months.Count);
            CollectionAssert.AreEqual(new[] { 4, 5, 6, 7, 8, 9, 10, 11, 12, 1, 2, 3 }, v.Months.Select(m => m.CalendarMonth).ToArray());
            // 08a SC-09 の表(月 index, 月内の週 1〜4, 種別, 大会): 5・6 月の第 3 週に記録会 / 10 月 2 週 第一戦 / 10 月 3 週 予選会 / 11 月 1 週 第二戦 / 1 月 1 週 第三戦。締切は大会の 4 週前
            var expect = new List<Tuple<int, int, CalendarItemKind, RaceKind?>>
            {
                Tuple.Create(1, 3, CalendarItemKind.Meet, (RaceKind?)null), Tuple.Create(2, 3, CalendarItemKind.Meet, (RaceKind?)null),
                Tuple.Create(5, 2, CalendarItemKind.EntryDeadline, (RaceKind?)RaceKind.Fall), Tuple.Create(5, 3, CalendarItemKind.QualifierDeadline, (RaceKind?)null),
                Tuple.Create(6, 1, CalendarItemKind.EntryDeadline, (RaceKind?)RaceKind.National), Tuple.Create(6, 2, CalendarItemKind.Race, (RaceKind?)RaceKind.Fall),
                Tuple.Create(6, 3, CalendarItemKind.Qualifier, (RaceKind?)null), Tuple.Create(7, 1, CalendarItemKind.Race, (RaceKind?)RaceKind.National),
                Tuple.Create(8, 1, CalendarItemKind.EntryDeadline, (RaceKind?)RaceKind.NewYear), Tuple.Create(9, 1, CalendarItemKind.Race, (RaceKind?)RaceKind.NewYear),
            };
            var actual = v.Months.SelectMany(m => m.Items.Select(i => Tuple.Create(m.MonthIndex, i.WeekOfMonth, i.Kind, i.Race))).ToList();
            CollectionAssert.AreEqual(expect, actual, "行事の月・週・種別");
            foreach (CalendarMonthView m in v.Months)
            {
                int mi = m.MonthIndex;
                Assert.AreEqual(mi == 4, m.Camp, "夏合宿は 8 月(月 4)だけ: " + mi);
                Assert.AreEqual(mi >= 2 && mi <= 7, m.ScoutSurvey, "視察は 6〜11 月: " + mi);
                Assert.AreEqual(mi >= 8 && mi <= 10, m.ScoutPursue, "勧誘は 12〜2 月: " + mi);
                Assert.AreEqual(mi >= 2 && mi <= 10, m.TicketWindow, "招聘の宣言は 6〜2 月: " + mi);
                Assert.AreEqual(mi == 11, m.YearEnd, "年度末は 3 月: " + mi);
                foreach (CalendarItemView it in m.Items) Assert.AreEqual(it.YearWeek / 4, mi, "行事は自分の月に入る");
            }
            Assert.IsTrue(v.Months[0].IsCurrent); Assert.IsFalse(v.Months[0].IsPast);
        }

        [Test]
        public void Calendar_ThemesAreThePlanNow_TheRecommendationLater_AndNothingForThePast()
        {
            GameSession gs = New();
            gs.SetMonthlyPlan(0, "th.speed");
            Advance(gs, 9);                                                                      // 6 月 2 週(月 2)
            CalendarView v = gs.YearCalendar();
            Assert.AreEqual(2, v.CurrentMonthIndex);
            gs.SetMonthlyPlan(1, "th.mountain");
            v = gs.YearCalendar();
            Assert.IsNull(v.Months[0].Themes); Assert.IsNull(v.Months[1].Themes); Assert.IsTrue(v.Months[0].IsPast);
            CollectionAssert.AreEqual(gs.State.MonthlyPlan, v.Months[2].Themes, "今月は決めた計画");
            Assert.IsFalse(v.Months[2].ThemesAreRecommended);
            Assert.AreEqual("th.mountain", v.Months[2].Themes[1]);
            for (int m = 3; m < 12; m++)
            {
                string rec = PlanRules.AutoPlan(gs.State.Plan.Focus, m);                       // 独立に: 重点ごとの 12 か月のテンプレート
                CollectionAssert.AreEqual(new[] { rec, rec, rec }, v.Months[m].Themes, "月 " + m);
                Assert.IsTrue(v.Months[m].ThemesAreRecommended);
            }
            v.Months[2].Themes[0] = "x";
            Assert.AreNotEqual("x", gs.State.MonthlyPlan[0], "返した配列は写し");
        }

        [Test]
        public void Calendar_EntitlementAndPeaking_MatchTheRealYear()
        {
            foreach (Difficulty d in new[] { Difficulty.Elite, Difficulty.Low, Difficulty.Club })
            {
                GameSession gs = New(d);
                bool seed = gs.PrepareYear().Seeds.Contains(gs.School.Id);
                for (int pass = 0; pass < 2; pass++)
                {
                    CalendarView v = gs.YearCalendar();
                    int yw = gs.YearWeek;
                    foreach (CalendarItemView it in v.Months.SelectMany(m => m.Items))
                    {
                        Assert.AreEqual(it.YearWeek < yw, it.Past); Assert.AreEqual(it.YearWeek == yw, it.Current);
                        if (it.Kind == CalendarItemKind.Race || it.Kind == CalendarItemKind.EntryDeadline)
                        {
                            RaceKind k = it.Race.Value;
                            if (k == RaceKind.NewYear && !seed && yw <= GameSession.QualifierWeek)
                            {
                                Assert.IsNull(it.Participates, "予選会の前は分からない"); Assert.AreEqual("qualifier", it.NoEntryCode);
                            }
                            else
                            {
                                bool inRace = gs.PrepareYear().Entrants(k).Contains(gs.School.Id);
                                Assert.AreEqual(inRace, it.Participates, d + " " + k);
                                Assert.AreEqual(inRace ? null : (k == RaceKind.NewYear ? "qualifier" : "rank"), it.NoEntryCode);
                            }
                            if (it.Kind == CalendarItemKind.Race)
                            {
                                Assert.AreEqual(gs.State.Plan.Peaking[(int)k], it.Peaking);
                                Assert.AreEqual(Calendar.RaceWeeks[(int)k] - 3, it.PeakingFromWeek, "調整は大会の 3 週前から大会の週までの 4 週");
                            }
                        }
                        else if (it.Kind == CalendarItemKind.Qualifier)
                        {
                            Assert.AreEqual(!seed, it.Participates); Assert.AreEqual(seed ? "seed" : null, it.NoEntryCode);
                        }
                        else if (it.Kind == CalendarItemKind.Meet) Assert.IsNull(it.Participates);
                    }
                    Assert.AreEqual(seed ? 0 : 1, v.Months.SelectMany(m => m.Items).Count(i => i.Kind == CalendarItemKind.QualifierDeadline), "シードには予選会の締切がない");
                    if (pass == 0) Advance(gs, 30);                                              // 予選会の後(1 月の第三戦の前)
                }
            }
        }

        // ------------------------------------------------------------ クリア
        [Test]
        public void Clear_MatchesHistory_BeforeAndAfterTheClear()
        {
            GameSession gs = New(Difficulty.Elite, 20261101UL);
            ClearView c0 = gs.Clear();
            Assert.IsFalse(c0.Cleared); Assert.AreEqual(0, c0.ClearYear); Assert.IsFalse(c0.NoInvite);
            Assert.IsNotEmpty(c0.ScenarioId, "シナリオの ID が入る");
            Assert.AreEqual(gs.State.Scenario.Id, c0.ScenarioId); Assert.AreEqual(gs.State.Scenario.Difficulty, c0.Difficulty);
            for (int y = 0; y < 20 && gs.State.Cleared == null; y++)
            {
                gs.PlayYears(1);
                AssertSameAsHistory(gs);
            }
            Assert.IsNotNull(gs.State.Cleared, "強豪は 20 年度以内にクリアする");
            ClearView c = gs.Clear();
            Assert.IsTrue(c.Cleared); Assert.AreEqual(gs.State.Cleared.Year, c.ClearYear); Assert.AreEqual(gs.State.Cleared.NoInvite, c.NoInvite);
            for (int k = 0; k < 3; k++) Assert.Greater(c.FirstWinYear[k], 0, "クリア = 3 大会の初優勝がそろう: " + k);
            Assert.AreEqual(c.ClearYear, c.FirstWinYear.Max(), "クリアの年度 = 最後の初優勝の年度");
            // 独立に数え直す
            int titles = gs.Stats.Sum(st => st.Wins.Count(w => w)), triple = gs.Stats.Count(st => st.Wins.All(w => w));
            Assert.AreEqual(titles, c.Titles); Assert.AreEqual(triple, c.TripleCrowns);
            for (int k = 0; k < 3; k++) Assert.AreEqual(gs.Stats.First(st => st.Wins[k]).Year, c.FirstWinYear[k]);
        }

        private static void AssertSameAsHistory(GameSession gs)
        {
            ClearView c = gs.Clear();
            HistoryView h = gs.History();
            CollectionAssert.AreEqual(h.FirstWinYear, c.FirstWinYear); Assert.AreEqual(h.Titles, c.Titles); Assert.AreEqual(h.TripleCrowns, c.TripleCrowns);
            Assert.AreEqual(h.ClearYear ?? 0, c.ClearYear); Assert.AreEqual(h.NoInvite, c.NoInvite);
        }

        /// <summary>年度の優勝の集合(<c>_yearWins</c>)は、年度が終わったら捨てる。残すと、次の年度の最初の週まで History の優勝の通算が二重に数えられた(試験で見つけた)。</summary>
        [Test]
        public void History_TitlesAreNotDoubleCounted_BetweenTheYearEndAndTheNextWeek()
        {
            GameSession gs = New(Difficulty.Elite, 20261101UL);
            int checkedYears = 0;
            for (int y = 0; y < 12; y++)
            {
                gs.PlayYears(1);                                                                 // 年度末の入れ替えまで済んだ直後 = 次の年度の最初の週の前
                int expected = gs.Stats.Sum(st => st.Wins.Count(w => w));
                Assert.AreEqual(expected, gs.History().Titles, "年度 " + (y + 1) + " の直後");
                Assert.AreEqual(expected, gs.Clear().Titles);
                if (gs.Stats.Last().Wins.Any(w => w)) checkedYears++;
            }
            Assert.Greater(checkedYears, 0, "試験の前提: 優勝した年度がある");
        }

        /// <summary>年度末の精算の停止(戦績に入ったが、まだ年度が替わっていない)でも、今年度の優勝を二重に数えない。</summary>
        [Test]
        public void Titles_AreNotDoubleCounted_AtTheYearEndSettleStop()
        {
            GameSession gs = New(Difficulty.Elite, 20261101UL);
            var flow = new GameFlow(gs, autoUpgrade: true, autoTickets: true);
            gs.State.Settings.EventAuto = true;
            int settles = 0, withWins = 0;
            while (settles < 10)
            {
                Stop s = flow.Advance();
                if (s.Kind != StopKind.YearEndSettle) continue;
                settles++;
                int expected = gs.Stats.Sum(st => st.Wins.Count(w => w));
                Assert.AreEqual(expected, gs.Clear().Titles, "精算の停止 " + settles);
                Assert.AreEqual(expected, gs.History().Titles);
                if (gs.Stats.Last().Wins.Any(w => w)) withWins++;
            }
            Assert.Greater(withWins, 0, "試験の前提: 優勝した年度がある");
        }

        [Test]
        public void Clear_CountsAWinOfTheYearInProgress_LikeHistory()
        {
            GameSession gs = New(Difficulty.Elite, 20261101UL);
            for (int w = 0; w < 48 * 3; w++)
            {
                gs.StepWeek();
                AssertSameAsHistory(gs);                                                         // 年度の途中(大会の直後)でも History と同じ
            }
        }

        // ------------------------------------------------------------ 個別指導
        [Test]
        public void Guidance_ReportsPointsTalkAndMenu_AsTheCommandsDo()
        {
            GameSession gs = New();
            Runner r = gs.State.Runners[0];
            GuidanceView v = gs.Guidance(r.Id);
            Assert.AreEqual(r.Id, v.Row.Id); Assert.AreEqual(4, v.PointsLeft); Assert.AreEqual(4, v.PointsMax);
            Assert.IsTrue(v.CanTalk); Assert.AreEqual(OpErr.HintGuideTalk.Code, v.TalkWhy.Code); Assert.IsFalse(v.Talked);
            Assert.IsTrue(v.CanSetMenu); Assert.AreEqual(OpErr.HintGuideMenu.Code, v.MenuWhy.Code);
            Assert.IsNull(v.CurrentMenu); Assert.IsFalse(v.CanCancel); Assert.IsFalse(v.CancelRefunds);
            // 面談 → 同じ月は 2 回目が拒否される
            Assert.IsTrue(gs.Talk(r.Id).Ok);
            v = gs.Guidance(r.Id);
            Assert.AreEqual(3, v.PointsLeft); Assert.IsTrue(v.Talked); Assert.IsFalse(v.CanTalk); Assert.AreEqual(OpErr.GuideTalked.Code, v.TalkWhy.Code);
            Assert.AreEqual(OpErr.GuideTalked.Code, gs.Talk(r.Id).Code, "View の理由 = 命令の拒否の理由");
            // メニューの指定 → 取り消せて、今週の指定ならポイントが戻る
            var menu = new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low");
            Assert.IsTrue(gs.GuideMenu(r.Id, menu, true).Ok);
            v = gs.Guidance(r.Id);
            Assert.AreEqual(2, v.PointsLeft); Assert.AreEqual(menu.Key1, v.CurrentMenu.Key1); Assert.AreEqual(menu.Mileage, v.CurrentMenu.Mileage);
            Assert.IsTrue(v.CanCancel); Assert.IsTrue(v.CancelRefunds);
            Assert.AreEqual(gs.State.IndividualMenus[r.Id].UntilAbsWeek, v.CurrentUntilAbsWeek);
            Advance(gs, 1);
            v = gs.Guidance(r.Id);
            Assert.IsTrue(v.CanCancel, "月末までは取り消せる"); Assert.IsFalse(v.CancelRefunds, "解決された週の指定はポイントが戻らない");
            // ポイントが尽きたら、どちらも理由つきで不可
            gs.State.GuidanceLeft = 0;
            v = gs.Guidance(gs.State.Runners[1].Id);
            Assert.IsFalse(v.CanTalk); Assert.IsFalse(v.CanSetMenu);
            Assert.AreEqual(OpErr.GuidePoints.Code, v.TalkWhy.Code); Assert.AreEqual(OpErr.GuidePoints.Code, v.MenuWhy.Code);
        }

        [Test]
        public void Guidance_InjuredRunner_CannotBeGivenAMenu_AndTheManagerRaisesTheMaximum()
        {
            GameSession gs = New();
            Runner r = gs.State.Runners[2];
            r.InjurySeverity = InjurySeverity.Moderate; r.InjuryWeeksLeft = 5;
            GuidanceView v = gs.Guidance(r.Id);
            Assert.IsFalse(v.CanSetMenu);
            OpResult refused = gs.GuideMenu(r.Id, new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low"), true);
            Assert.IsFalse(refused.Ok);
            Assert.AreEqual(refused.Code, v.MenuWhy.Code, "View の理由 = 命令の拒否の理由(故障中)");
            gs.State.Fac.Manager = 6;
            Assert.AreEqual(6, gs.Guidance(r.Id).PointsMax);
            gs.State.Fac.Manager = 3;
            Assert.AreEqual(5, gs.Guidance(r.Id).PointsMax);
            Assert.Throws<ArgumentException>(() => gs.Guidance(-5));
        }

        [Test]
        public void Guidance_Presets_AreTheSpecMenus_AndPassTheValidation()
        {
            GameSession gs = New();
            GuidanceView v = gs.Guidance(gs.State.Runners[0].Id);
            CollectionAssert.AreEqual(new[] { "care", "adjust" }, v.Presets.Select(p => p.Id).ToArray());
            Assert.AreEqual(("ks.core", "ks.rest", "mil.low"), (v.Presets[0].Menu.Key1, v.Presets[0].Menu.Key2, v.Presets[0].Menu.Mileage), "ケア = 03 の SAFE");
            Assert.AreEqual(("ks.sharpen", "ks.pace_run", "mil.low"), (v.Presets[1].Menu.Key1, v.Presets[1].Menu.Key2, v.Presets[1].Menu.Mileage), "調整");
            foreach (GuidancePresetView p in v.Presets) Assert.IsEmpty(PlanRules.ValidateMenu(gs.Master, p.Menu.Key1, p.Menu.Key2, p.Menu.Mileage), p.Id);
        }

        [Test]
        public void Guidance_Warnings_FollowTheCommandsConfirmation()
        {
            GameSession gs = New();
            Runner r = gs.State.Runners[0];
            Assert.IsEmpty(gs.Guidance(r.Id).Warnings, "疲労が低く調整期間でもなければ注意なし");
            r.Fatigue = 85;
            GuidanceView v = gs.Guidance(r.Id);
            Assert.AreEqual(new[] { OpErr.WarnNoSafety.Code }, v.Warnings.Select(m => m.Code).ToArray());
            OpResult conf = gs.GuideMenu(r.Id, new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low"));
            Assert.IsTrue(conf.NeedsConfirm, "命令も同じ注意で確認を求める");
            CollectionAssert.AreEqual(v.Warnings.Select(m => m.Code).ToArray(), conf.Warnings.Select(m => m.Code).ToArray());
            gs.SetAutoSafety(0);
            Assert.IsEmpty(gs.Guidance(r.Id).Warnings, "安全網を無効にしていれば『働きません』は出ない");
            gs.SetAutoSafety(75);
            Assert.AreEqual(1, gs.Guidance(r.Id).Warnings.Count);
            r.Fatigue = 0;
            // 調整期間(ピーキング)の登録メンバー
            GameSession g2 = New();
            bool found = false;
            for (int w = 0; w < 48 && !found; w++)
            {
                foreach (Runner x in g2.State.Runners)
                    if (TeamWeek.TaperIndex(g2.State, x, g2.YearWeek) >= 0)
                    {
                        x.Fatigue = 0;
                        GuidanceView gv = g2.Guidance(x.Id);
                        Assert.IsTrue(gv.Warnings.Any(m => m.Code == OpErr.WarnOverrideTaper.Code), "調整期間は『調整メニューを上書きします』");
                        OpResult c2 = g2.GuideMenu(x.Id, new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low"));
                        Assert.IsTrue(c2.NeedsConfirm && c2.Warnings.Any(m => m.Code == OpErr.WarnOverrideTaper.Code));
                        found = true; break;
                    }
                if (!found) g2.StepWeek();
            }
            Assert.IsTrue(found, "試験の前提: 1 年度のうちに調整期間の登録メンバーがいる");
        }

        [Test]
        public void Guidance_SoloRunnerGetsTheBonusFlag()
        {
            GameSession gs = New();
            Runner r = gs.State.Runners[0];
            r.SetIndividuality(new Style(StyleForm.Balance, StyleRun.Solo, StyleFlow.Even), new Gift[0]);
            Assert.IsTrue(gs.Guidance(r.Id).SoloBonus);
            Assert.Greater(r.IndMult, 1.0);
            r.SetIndividuality(new Style(StyleForm.Balance, StyleRun.Pack, StyleFlow.Even), new Gift[0]);
            Assert.IsFalse(gs.Guidance(r.Id).SoloBonus);
        }

        // ------------------------------------------------------------ 個別指導の予測
        [Test]
        public void GuidancePreview_RejectsInvalidMenus_WithTheValidatorsReason()
        {
            GameSession gs = New();
            Runner r = gs.State.Runners[0];
            var bad = new[] { new MenuSpec("ks.sharpen", "ks.sharpen", "mil.low"), new MenuSpec("ks.cross_train", "ks.pace_run", "mil.low"), new MenuSpec("ks.sharpen", "ks.pace_run", null) };
            foreach (MenuSpec m in bad)
            {
                GuidancePreviewView p = gs.GuidancePreview(r.Id, m);
                Assert.IsFalse(p.Valid);
                Assert.AreEqual(PlanRules.ValidateMenu(gs.Master, m.Key1, m.Key2, m.Mileage, r.IsInjured)[0].Code, p.Why.Code);
                Assert.AreEqual(gs.GuideMenu(r.Id, m).Code, p.Why.Code, "命令の拒否と同じ理由");
            }
        }

        /// <summary>予測は『故障が起きなければ実際と一致』する(疲労・能力。R-05)。同じ世界で、指定したときとしなかったときを実際に進め、予測と突き合わせる。</summary>
        [Test]
        public void GuidancePreview_AgreesWithTheRealMonth_WhenNoInjuryHappens()
        {
            var menu = new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low");
            int checkedCases = 0;
            for (ulong seed = 1; seed <= 12 && checkedCases < 6; seed++)
            {
                for (int start = 0; start < 2; start++)
                {
                    GameSession probe = New(Difficulty.Mid, 20261200UL + seed);
                    Advance(probe, 9 + start);                                                   // 6 月 2 週〜3 週(月の途中から)
                    int rid = probe.State.Runners.OrderBy(x => x.Id).Skip((int)(seed % 7)).First().Id;
                    GuidancePreviewView p = probe.GuidancePreview(rid, menu);
                    Assert.IsTrue(p.Valid);
                    int left = 4 - probe.YearWeek % 4;
                    Assert.AreEqual(left, p.WeeksCovered, "今週から月末まで");
                    foreach (bool guided in new[] { true, false })
                    {
                        GameSession real = New(Difficulty.Mid, 20261200UL + seed);
                        Advance(real, 9 + start);
                        Runner r = real.State.FindRunner(rid);
                        int[] statsBefore = (int[])r.Stats.Clone();
                        if (guided) Assert.IsTrue(real.GuideMenu(rid, menu, true).Ok);
                        bool injuredBefore = r.IsInjured;
                        bool injuredDuring = false;
                        for (int i = 0; i < left; i++) { real.StepWeek(); injuredDuring |= r.IsInjured; }
                        if (injuredBefore || injuredDuring || !real.State.Runners.Contains(r)) continue;       // 故障が起きた月は一致しない(確率で出す)
                        int lv = 0;
                        for (int i = 0; i < 10; i++) lv += r.Stats[i] - statsBefore[i];
                        Assert.AreEqual(guided ? p.FatigueEnd : p.BaselineFatigueEnd, r.Fatigue, "月末の疲労 seed " + seed + (guided ? " 指定" : " 指定なし"));
                        Assert.AreEqual(guided ? p.Levelups : p.BaselineLevelups, lv, "能力が上がった回数の合計 seed " + seed);
                        checkedCases++;
                    }
                }
            }
            Assert.GreaterOrEqual(checkedCases, 6, "試験の前提: 故障のなかった月が十分ある");
        }

        [Test]
        public void GuidancePreview_ReadsWithoutChangingTheState()
        {
            GameSession gs = New();
            Advance(gs, 3);
            Runner r = gs.State.Runners[3];
            string before = string.Join(",", r.Stats) + "|" + r.Fatigue + "|" + string.Join(",", r.Xp) + "|" + gs.State.GuidanceLeft + "|" + gs.State.IndividualMenus.Count;
            GuidancePreviewView p = gs.GuidancePreview(r.Id, new MenuSpec("ks.core", "ks.rest", "mil.low"));
            Assert.IsTrue(p.Valid);
            Assert.AreEqual(before, string.Join(",", r.Stats) + "|" + r.Fatigue + "|" + string.Join(",", r.Xp) + "|" + gs.State.GuidanceLeft + "|" + gs.State.IndividualMenus.Count);
            Assert.Less(p.FatigueEnd, p.BaselineFatigueEnd + 1, "ケアは標準の計画より疲れが残らない(または同じ)");
            Assert.AreEqual(p.FitMark, Presentation.FitMark(0) == '△' ? p.FitMark : p.FitMark);      // 印は ◎〇△ のどれか
            Assert.IsTrue("◎〇△".IndexOf(p.FitMark) >= 0);
        }
    }
}
