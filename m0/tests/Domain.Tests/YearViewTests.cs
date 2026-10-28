using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>年度末の読み取りの API(08b。SC-12 年度の結果・SC-13 卒業・SC-14 入学者)。精算・卒業・入学の実際の結果と食い違わない。</summary>
    public class YearViewTests
    {
        /// <summary>指定の年度の最後の週(第 48 週)の終わりまで進める。強化は画面の操作にする(autoUpgrade = false)。精算の前で止まる。</summary>
        private static GameSession ToYearEndBeforeSettle(ulong seed, Difficulty d, int years, bool autoUpgrade = false)
        {
            GameSession gs = GameSession.New(seed, d);
            gs.AutoUpgrade = autoUpgrade;
            for (int y = 0; y < years; y++)
            {
                for (int w = 0; w < Calendar.WeeksPerYear - 1; w++) gs.StepWeek();
                if (y < years - 1) gs.StepWeek();
            }
            gs.ResolveTraining();
            RaceInfo? o;
            RaceOutcome r = gs.PlayWeekRaces(out o);
            var sel = gs.SelectWeekEvents(o);
            gs.ResolveWeekEvents(sel, o, new DefaultChoicePolicy());
            gs.FinishWeek();
            return gs;
        }

        [Test]
        public void OverseasGraduates_KeepTheirCountry_AndShowItInTheGraduationAndOBViews()
        {
            Assert.AreEqual(5, OBRecord.FromGraduate(new Runner { Overseas = true, Country = 5 }, 3, 1, CareerPath.Employment).Country);
            Assert.AreEqual(0, OBRecord.FromGraduate(new Runner { Overseas = false, Country = 5, Pref = 12 }, 3, 1, CareerPath.Employment).Country, "留学生でなければ国は残さない");
            GameSession gs = ToYearEndBeforeSettle(35UL, Difficulty.Mid, 1);
            gs.SettleYear();
            Runner senior = gs.State.Runners.First(r => r.Grade >= 4);
            senior.Overseas = true; senior.Country = 3; senior.Pref = 0;
            int id = senior.Id;
            gs.TurnoverYear();
            GraduateRowView g = gs.Graduation().Graduates.Single(x => x.RunnerId == id);
            Assert.IsTrue(g.Overseas);
            Assert.AreEqual(Geo.Countries[2], g.CountryName);
            Assert.AreEqual("", g.PrefName);
            Assert.AreEqual(Geo.Countries[2], gs.Obs().Rows.Single(x => x.Grad.RunnerId == id).Grad.CountryName, "OB 名簿でも同じ行");
            foreach (GraduateRowView o in gs.Graduation().Graduates.Where(x => x.RunnerId != id && !x.Overseas)) Assert.AreEqual("", o.CountryName);
        }

        [Test]
        public void Summary_IsNullUntilTheYearIsSettled()
        {
            GameSession gs = ToYearEndBeforeSettle(31UL, Difficulty.Mid, 1);
            Assert.IsNull(gs.YearSummary(), "精算の前は結果がない");
            Assert.IsNull(gs.Graduation());
            Assert.IsNull(gs.Enrollment());
            gs.SettleYear();
            Assert.IsNotNull(gs.YearSummary());
            gs.TurnoverYear();
            gs.AdvanceWeek();
            Assert.IsNull(gs.YearSummary(), "次の年度に入ったら閉じる");
        }

        [Test]
        public void Summary_MatchesTheSettlement()
        {
            for (ulong seed = 1; seed <= 3; seed++)
            {
                GameSession gs = ToYearEndBeforeSettle(seed, Difficulty.Mid, 2);
                int fundsBefore = gs.State.Funds;
                double prestigeBefore = gs.School.Prestige;
                int rankBefore = gs.School.Rank();
                int[] lastRank = (int[])gs.State.LastRank.Clone();
                gs.SettleYear();
                YearSummaryView v = gs.YearSummary();
                PlayerYearStats st = gs.Stats[gs.Stats.Count - 1];

                Assert.AreEqual(gs.Year, v.Year);
                Assert.AreEqual(st.Points, v.EvalPoints);
                for (int k = 0; k < 3; k++)
                {
                    Assert.AreEqual((RaceKind)k, v.Races[k].Kind);
                    Assert.AreEqual(st.Place[k], v.Races[k].Place);
                    Assert.AreEqual(st.Wins[k], v.Races[k].Won);
                    int expected = st.Place[k] <= 10 ? WorldK.YearPoints[st.Place[k] - 1] : 0;
                    Assert.AreEqual(expected, v.Races[k].EvalPoints);
                }
                Assert.AreEqual(v.EvalPoints, v.Races.Sum(x => x.EvalPoints) + v.TripleCrownBonus, "年間の評価ポイント = 大会ごとの合計 + 三冠の加点");
                Assert.AreEqual(v.TripleCrown ? 30 : 0, v.TripleCrownBonus);
                Assert.AreEqual(prestigeBefore, v.PrestigeBefore, 0.0, "名声の前は精算の前の値");
                Assert.AreEqual(gs.School.Prestige, v.PrestigeAfter);
                Assert.AreEqual(rankBefore, v.CoachRankBefore);
                Assert.AreEqual(gs.School.Rank(), v.CoachRankAfter);
                Assert.AreEqual(v.CoachRankAfter > v.CoachRankBefore, v.RankUp);
                Assert.AreEqual(WorldMath.FacilityCap(v.CoachRankBefore), v.FacilityCapBefore);
                Assert.AreEqual(WorldMath.FacilityCap(v.CoachRankAfter), v.FacilityCapAfter);
                Assert.AreEqual(gs.School.CumPoints, v.CumPoints, 0.0);
                Assert.AreEqual(gs.School.FamePoints, v.FamePoints, 0.0);
                Assert.AreEqual(v.CoachRankAfter < WorldK.RankCum.Length ? (int?)WorldK.RankCum[v.CoachRankAfter] : null, v.NextRankPoints);
                Assert.AreEqual(Math.Max(1, lastRank[2] - 1), v.ExpectedTargetRank);
                Assert.AreEqual(st.Place[2] <= v.ExpectedTargetRank, v.TargetMet);
                Assert.AreEqual(v.TargetMet, v.TargetBonusFunds > 0);
                Assert.IsFalse(v.TurnoverDone);

                YearFinanceView f = v.Finance;
                Assert.IsNotNull(f);
                Assert.AreEqual(fundsBefore, f.FundsBefore);
                Assert.AreEqual(Math.Round(f.IncomeFormula, 6), Math.Round(WorldMath.Income(gs.School.Prestige, gs.School.YearPoints), 6), "収入の式は精算の時点の名声と評価ポイント");
                Assert.AreEqual(f.Income, f.IncomeFormula + f.IncomeMod, 1e-9);
                Assert.AreEqual(f.Expense, f.Staff + f.Upkeep + f.MembersExpense, 1e-9);
                Assert.AreEqual(15.0 * f.Members, f.MembersExpense, 1e-9);
                Assert.AreEqual((int)Math.Floor(fundsBefore + f.Income - f.Expense + 0.5), f.FundsAfterOps, "強化の前の資金 = 前の資金 + 収入 − 支出");
                Assert.AreEqual(gs.State.Funds, f.FundsNow);
                Assert.AreEqual(f.FundsAfterOps, f.FundsNow, "手動の強化のときは、精算では強化しない");
                CollectionAssert.AreEqual(f.FacBefore, f.FacAfter, "同上: 施設は変わらない");
            }
        }

        [Test]
        public void Summary_TripleCrownBonus_IsTheDifferenceFromTheRaces()
        {
            GameSession gs = ToYearEndBeforeSettle(3UL, Difficulty.Mid, 2);
            gs.SettleYear();
            PlayerYearStats st = gs.Stats[gs.Stats.Count - 1];
            st.Wins = new[] { true, true, true };                                // 三冠の年度にする(読み取りの対応を確かめる)
            st.Place = new[] { 1, 1, 1 };
            st.Points = 3 * WorldK.YearPoints[0] + 30;
            YearSummaryView v = gs.YearSummary();
            Assert.IsTrue(v.TripleCrown);
            Assert.AreEqual(30, v.TripleCrownBonus);
            Assert.AreEqual(3 * WorldK.YearPoints[0] + 30, v.EvalPoints);
            Assert.IsTrue(v.TargetMet, "第三戦 1 位は期待目標(最上位 1)を満たす");
            st.Wins = new[] { true, true, false };
            st.Place = new[] { 1, 1, 2 };
            st.Points = 2 * WorldK.YearPoints[0] + WorldK.YearPoints[1];
            v = gs.YearSummary();
            Assert.IsFalse(v.TripleCrown, "二冠までは三冠ではない");
            Assert.AreEqual(0, v.TripleCrownBonus);
        }

        [Test]
        public void Summary_FundsYearModifier_AppearsInTheIncome()
        {
            GameSession gs = ToYearEndBeforeSettle(4UL, Difficulty.Mid, 1);
            gs.State.Mods.Add(new TimedModifier { Kind = ModKind.FundsYear, To = TimedModifier.TeamTarget, Value = 50, Until = int.MaxValue, Src = "test" });
            int before = gs.State.Funds;
            gs.SettleYear();
            YearFinanceView f = gs.YearSummary().Finance;
            Assert.AreEqual(50.0, f.IncomeMod, 1e-9);
            Assert.AreEqual(f.IncomeFormula + 50.0, f.Income, 1e-9);
            Assert.AreEqual((int)Math.Floor(before + f.Income - f.Expense + 0.5), f.FundsAfterOps);
        }

        [Test]
        public void Summary_AutoUpgrade_ShowsSpendAndNewLevels()
        {
            GameSession gs = ToYearEndBeforeSettle(5UL, Difficulty.Elite, 3, autoUpgrade: true);
            gs.SettleYear();
            YearFinanceView f = gs.YearSummary().Finance;
            Assert.AreEqual(gs.State.Funds, f.FundsNow);
            Assert.LessOrEqual(f.FundsNow, f.FundsAfterOps);
            bool upgraded = f.FacAfter.Zip(f.FacBefore, (a, b) => a > b).Any(x => x);
            Assert.AreEqual(f.FundsNow < f.FundsAfterOps, upgraded, "資金が減っていれば、どこかの施設が上がっている");
            Assert.IsTrue(f.FacAfter.Zip(f.FacBefore, (a, b) => a >= b).All(x => x), "レベルは下がらない");
        }

        [Test]
        public void Summary_KeepsShowingAfterTurnover_AndTicketMilestonesAreForecast()
        {
            for (ulong seed = 1; seed <= 6; seed++)
            {
                GameSession gs = ToYearEndBeforeSettle(seed, Difficulty.Elite, 2);
                gs.SettleYear();
                YearSummaryView before = gs.YearSummary();
                var peek = before.TicketsAtYearEnd.Select(x => x.Id).ToList();
                var earnedBefore = before.TicketsEarned.Select(x => x.Id).ToList();
                gs.TurnoverYear();
                YearSummaryView after = gs.YearSummary();
                Assert.IsTrue(after.TurnoverDone);
                Assert.IsEmpty(after.TicketsAtYearEnd, "卒業のあとは『これから入る』はない");
                var earnedAfter = after.TicketsEarned.Select(x => x.Id).ToList();
                foreach (string m in peek) CollectionAssert.Contains(earnedAfter, m, "予告した節目は、卒業・入学のあとに実際に入る");
                CollectionAssert.IsSubsetOf(earnedBefore, earnedAfter);
                Assert.IsTrue(after.TicketsEarned.All(x => x.NameKey == (x.Years > 0 ? "tm.anniv.name" : x.Id + ".name")));
                Assert.AreEqual(before.EvalPoints, after.EvalPoints);
                Assert.AreEqual(before.PrestigeAfter, after.PrestigeAfter, 0.0);
            }
        }

        [Test]
        public void TicketMilestoneView_SplitsTheAnniversaryYears()
        {
            TicketMilestoneView a = TicketMilestoneView.Of("tm.anniv.30"), w = TicketMilestoneView.Of("tm.win.fall");
            Assert.AreEqual(30, a.Years);
            Assert.AreEqual("tm.anniv.name", a.NameKey);
            Assert.AreEqual("tm.anniv.30", a.Id);
            Assert.AreEqual(0, w.Years);
            Assert.AreEqual("tm.win.fall.name", w.NameKey);
        }

        [Test]
        public void PeekAtYearEnd_EqualsWhatEarnAtYearEndTakes()
        {
            foreach (int rank in new[] { 1, 3, 6, 9 })
                foreach (int years in new[] { 5, 10, 20, 21 })
                {
                    var t = new TicketState();
                    t.Earn(TicketMilestones.Rank3, 1);                       // すでに取った節目は予告に出ない
                    var peek = TicketMilestones.PeekAtYearEnd(t, rank, years);
                    var snapshot = new List<string>(t.Milestones);
                    var again = TicketMilestones.PeekAtYearEnd(t, rank, years);
                    CollectionAssert.AreEqual(snapshot, new List<string>(t.Milestones), "予告は状態を変えない");
                    CollectionAssert.AreEqual(peek, again);
                    int n = TicketMilestones.EarnAtYearEnd(t, years, rank, years);
                    Assert.AreEqual(peek.Count, n);
                    foreach (string m in peek) CollectionAssert.Contains(t.Milestones, m);
                }
        }

        [Test]
        public void Graduation_ListsGraduatesWithHallOfFameFirst_AndQuittersApart()
        {
            int sawGrads = 0, sawHof = 0, sawQuit = 0, sawHs = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                GameSession gs = ToYearEndBeforeSettle(seed, Difficulty.Elite, 4);
                gs.SettleYear();
                gs.TurnoverYear();
                GraduationView g = gs.Graduation();
                YearTurnover raw = gs.LastTurnover;
                Assert.AreEqual(gs.Year, g.Year);
                Assert.AreEqual(raw.Graduates.Count, g.Graduates.Count);
                Assert.AreEqual(raw.Quit.Count, g.Quit.Count);
                int firstNonHof = g.Graduates.FindIndex(x => !x.HallOfFame);
                if (firstNonHof >= 0) Assert.IsFalse(g.Graduates.Skip(firstNonHof).Any(x => x.HallOfFame), "殿堂入りが先頭にまとまっている");
                int prevHofId = -1, prevId = -1;
                foreach (GraduateRowView row in g.Graduates)
                {
                    OBRecord ob = raw.Graduates.Single(o => o.RunnerId == row.RunnerId);
                    Assert.AreEqual(ob.Name, row.Name);
                    Assert.AreEqual(ob.CareerPath, row.Career);
                    Assert.AreEqual(ob.HallOfFame, row.HallOfFame);
                    Assert.AreEqual(ob.SectionWins, row.SectionWins);
                    Assert.AreEqual(ob.RaceWins, row.RelayWins);
                    Assert.AreEqual(ob.T5000, row.Pb5000);
                    Assert.AreEqual(Ekiden.Domain.Screens.DisplayRules.RankLetter(ob.Power), row.Rank);
                    Assert.AreEqual(ob.FinalStats.Length, row.StatRanks.Length);
                    Assert.AreEqual(ob.Skills.Count, row.Skills.Count);
                    Assert.AreEqual(ob.Pref == 0, row.Overseas);
                    if (ob.HighSchoolName.Length > 0) { Assert.AreEqual(ob.HighSchoolName, row.HighSchoolNameKey); StringAssert.StartsWith("hs.", row.HighSchoolNameKey); sawHs++; }
                    else Assert.IsNull(row.HighSchoolNameKey);
                    int idHere = row.RunnerId;
                    if (row.HallOfFame) { Assert.Greater(idHere, prevHofId, "殿堂入りの中は ID の昇順"); prevHofId = idHere; }
                    else { Assert.Greater(idHere, prevId, "それ以外も ID の昇順"); prevId = idHere; }
                    sawGrads++;
                    if (row.HallOfFame) sawHof++;
                }
                sawQuit += g.Quit.Count;
                foreach (RunnerRowView q in g.Quit) Assert.IsNull(gs.State.FindRunner(q.Id), "退部した選手はもう名簿にいない");
            }
            Assert.Greater(sawGrads, 10);
            Assert.Greater(sawHs, 0, "高校の名前のキーが入る卒業生がいる");
        }

        [Test]
        public void Graduation_QuittersAreShownApart()
        {
            int quit = 0;
            for (ulong seed = 1; seed <= 4; seed++)
            {
                GameSession gs = ToYearEndBeforeSettle(seed, Difficulty.Elite, 2);
                gs.SettleYear();
                foreach (Runner r in gs.State.Runners.Where(x => x.Grade < 4)) r.Motivation = 1;      // やる気 1 は年度末に 40% で退部
                gs.TurnoverYear();
                GraduationView g = gs.Graduation();
                Assert.AreEqual(gs.LastTurnover.Quit.Count, g.Quit.Count);
                CollectionAssert.AreEqual(gs.LastTurnover.Quit.Select(x => x.Id), g.Quit.Select(x => x.Id));
                Assert.IsTrue(g.Quit.All(x => x.Motivation == 1));
                quit += g.Quit.Count;
            }
            Assert.Greater(quit, 0, "退部した選手が別枠に出る");
        }

        [Test]
        public void Enrollment_SplitsTargetedFromNatural_AndHidesPotential()
        {
            GameSession gs = ToYearEndBeforeSettle(11UL, Difficulty.Mid, 2);
            gs.SettleYear();
            gs.TurnoverYear();
            EnrollmentView e = gs.Enrollment();
            Assert.AreEqual(gs.Year + 1, e.Year);
            var all = e.Targeted.Concat(e.Natural).ToList();
            CollectionAssert.AreEquivalent(gs.LastIntakeIds, all.Select(x => x.Row.Id).ToList());
            Assert.IsTrue(all.All(x => x.Row.Grade == 1));
            foreach (EnrolleeRowView x in all)
            {
                Runner r = gs.State.FindRunner(x.Row.Id);
                Assert.AreEqual(gs.LastIntakeTargeted.Contains(r.Id), x.Targeted);
                CollectionAssert.AreEqual(r.Stats, x.Stats);
                Assert.AreEqual(Ekiden.Domain.Screens.DisplayRules.RankLetter(r.Stats[0]), x.StatRanks[0]);
                Assert.AreEqual(r.Far, x.Far);
                Assert.AreEqual(r.Near, x.Near);
                Assert.That(x.Stars, Is.InRange(1, 5));
            }
            Assert.IsTrue(e.Targeted.All(x => x.Targeted) && e.Natural.All(x => !x.Targeted));
        }

        [Test]
        public void Intake_StarsComeFromTheMarketValue_NotTheTrueValue()
        {
            GameSession gs = GameSession.New(8UL, Difficulty.Mid);
            School other = gs.World.Schools.First(s => !s.IsPlayer);
            var cands = other.Roster.Take(3).Select(w => w).ToList();
            double[] v = { 41.0, 53.0, 61.0 };
            for (int i = 0; i < cands.Count; i++) { cands[i].V = v[i]; cands[i].Q = 99.0; }        // 評価★は V から(本当の価値 Q は見せない)
            int before = gs.State.Runners.Count;
            ((IPlayerSide)gs).Intake(new List<WorldRunner>(cands), null);
            Assert.AreEqual(before + 3, gs.State.Runners.Count);
            var stars = gs.LastIntakeIds.Select(id => gs.LastIntakeStars[id]).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, stars);
        }

        [Test]
        public void Stars_FollowTheThresholds()
        {
            Func<double, int> d = Ekiden.Domain.Screens.DisplayRules.Stars;
            Assert.AreEqual(1, d(41.99)); Assert.AreEqual(2, d(42.0)); Assert.AreEqual(2, d(47.99)); Assert.AreEqual(3, d(48.0));
            Assert.AreEqual(3, d(53.99)); Assert.AreEqual(4, d(54.0)); Assert.AreEqual(4, d(59.99)); Assert.AreEqual(5, d(60.0));
        }
    }
}
