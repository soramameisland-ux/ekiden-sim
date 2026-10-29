using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>新設の施設(高地・メンタル・合宿所)と追加のスタッフ(主務)(05 §4.4・03 §4.6。L-70 ②・L-25): 費用・維持費・給与・ランクでの解放・指導ポイント・おまかせの対象外・画面の欄。</summary>
    public class FacilityExtraTests
    {
        private static GameSession New() { return GameSession.New(20261029UL, Difficulty.Mid); }

        private static void AllAt(GameSession gs, int lv)
        {
            gs.State.Fac = new FacilityLevels { Coach = lv, Trainer = lv, Nutri = lv, Dorm = lv, Track = lv, Hill = lv, Gym = lv, Medical = lv, Scout = lv };
            gs.State.Funds = 1000000;
        }

        [Test]
        public void ConstantsAndCosts_FollowSpec05()
        {
            string text = File.ReadAllText(DocPaths.Find("05_world_scout_spec.md"), Encoding.UTF8);
            Match m = Regex.Match(text, @"\|\s*新設・追加の費用[^|]*\|\s*(\d+)[(（万円。]");
            Assert.IsTrue(m.Success, "05 の定数表に『新設・追加の費用』の行がある");
            Assert.AreEqual(double.Parse(m.Groups[1].Value), WorldK.NewCost, 1e-9);
            Assert.AreEqual(2000.0, WorldMath.UpgradeCost(0), 1e-9, "Lv0 → 1 は新設費");
            Assert.AreEqual(550.0, WorldMath.UpgradeCost(1), 1e-9, "Lv1 → 2 は通常の表");
            Assert.AreEqual(7, WorldK.NewMinRank);
            Assert.IsTrue(Regex.IsMatch(text, @"\|\s*7\s*\|[^|\n]*新設の施設・追加のスタッフ"), "05 §3.3 のランク 7 の行に新設・追加が書いてある");
            // 3 つ(Lv8 まで)の合計 = 17,700 万円 × 3
            double one = Enumerable.Range(0, 8).Sum(lv => WorldMath.UpgradeCost(lv));
            Assert.AreEqual(17700.0, one, 1e-9);
        }

        [Test]
        public void UpkeepAndSalary_IncludeTheNewItems_AndAreUnchangedWhenAbsent()
        {
            var f = new FacilityLevels { Coach = 3, Trainer = 2, Nutri = 4, Dorm = 3, Track = 2, Hill = 2, Gym = 5, Medical = 1, Scout = 3 };
            Assert.AreEqual(WorldMath.StaffExpense(3, 2, 4, 3), WorldMath.StaffExpense(f), 0.0, "主務がいなければ従来どおり(ビット一致)");
            Assert.AreEqual(WorldMath.FacilityUpkeep(3, 2, 2, 5, 1), WorldMath.FacilityUpkeep(f), 0.0, "新設がなければ従来どおり(ビット一致)");
            f.Altitude = 3; f.Mental = 1; f.Camp = 8; f.Manager = 4;
            Assert.AreEqual(WorldMath.FacilityUpkeep(3, 2, 2, 5, 1) + 8.0 * (3 + 1 + 8), WorldMath.FacilityUpkeep(f), 1e-9, "新設の施設の維持費は 8 × Lv(Lv1 から払う)");
            Assert.AreEqual(WorldMath.StaffExpense(3, 2, 4, 3) + WorldK.ExpStaff[3], WorldMath.StaffExpense(f), 1e-9, "主務 Lv4 の給与は表の Lv4(160)");
            f.Manager = 1;
            Assert.AreEqual(WorldMath.StaffExpense(3, 2, 4, 3), WorldMath.StaffExpense(f), 1e-9, "主務 Lv1 の給与は 0(他のスタッフと同じ表)");
        }

        [Test]
        public void GuidancePoints_AreFour_FiveFromLv3_SixFromLv6()
        {
            int[] expect = { 4, 4, 4, 5, 5, 5, 6, 6, 6 };
            for (int lv = 0; lv <= 8; lv++) Assert.AreEqual(expect[lv], WorldMath.GuidancePoints(lv), "主務 Lv" + lv);
            Assert.AreEqual(WorldMath.GuidanceMax, WorldMath.GuidancePoints(8));
        }

        [Test]
        public void Guidance_ResetsMonthlyToTheManagersLevel_AndRisesAtOnceWhenTheManagerIsUpgradedMidMonth()
        {
            GameSession gs = New();
            gs.State.Fac.Manager = 6;
            for (int i = 0; i < 4; i++) gs.StepWeek();
            Assert.AreEqual(6, gs.State.GuidanceLeft, "月初に 6 へ戻る");
            Assert.IsTrue(gs.Talk(gs.State.Runners[0].Id).Ok);
            Assert.AreEqual(5, gs.State.GuidanceLeft);
            // 月の途中で主務を Lv2 → Lv3 にすると、増えた 1 点をすぐ使える
            GameSession g2 = New();
            AllAt(g2, 5);
            g2.School.CumPoints = 120;
            g2.State.Fac.Manager = 2;
            int before = g2.State.GuidanceLeft;
            Assert.IsTrue(g2.UpgradeFacility("manager").Ok);
            Assert.AreEqual(3, g2.State.Fac.Manager);
            Assert.AreEqual(before + 1, g2.State.GuidanceLeft, "Lv3 で 5 点");
            Assert.IsTrue(g2.UpgradeFacility("manager").Ok);
            Assert.AreEqual(before + 1, g2.State.GuidanceLeft, "Lv4 は点が増えない");
        }

        [Test]
        public void Build_NeedsRank7_ChargesTheNewCost_ThenFollowsTheNormalLadderAndCap()
        {
            GameSession gs = New();
            AllAt(gs, 5);
            gs.State.Funds = 100000;
            gs.School.CumPoints = 119;                                                     // ランク 6
            foreach (string k in FacilityPolicy.Extras)
            {
                OpResult no = gs.UpgradeFacility(k);
                Assert.IsFalse(no.Ok, k); Assert.AreEqual(OpErr.FacilityRank.Code, no.Code, k);
                Assert.AreEqual(0, FacilityPolicy.Level(gs.State.Fac, k));
            }
            Assert.AreEqual(100000, gs.State.Funds, "拒否した操作は資金を使わない");
            gs.School.CumPoints = 120;                                                     // ランク 7 = 上限 Lv6
            double funds = gs.State.Funds;
            double[] ladder = { 2000, 550, 800, 1050, 1300, 3000 };
            for (int lv = 0; lv < 6; lv++)
            {
                OpResult ok = gs.UpgradeFacility("altitude");
                Assert.IsTrue(ok.Ok, "Lv" + lv + " → " + (lv + 1) + ": " + ok.Reason);
                Assert.AreEqual(funds - ladder[lv], gs.State.Funds, 1e-9, "Lv" + lv + " → " + (lv + 1) + " の費用");
                funds = gs.State.Funds;
                Assert.AreEqual(lv + 1, gs.State.Fac.Altitude);
            }
            OpResult capped = gs.UpgradeFacility("altitude");
            Assert.IsFalse(capped.Ok); Assert.AreEqual(OpErr.FacilityCap.Code, capped.Code, "ランク 7 の上限 Lv6 で止まる");
            // 資金が足りないとき
            gs.State.Funds = 1999;
            OpResult poor = gs.UpgradeFacility("camp");
            Assert.IsFalse(poor.Ok); Assert.AreEqual(OpErr.FacilityFunds.Code, poor.Code);
            Assert.AreEqual(0, gs.State.Fac.Camp);
        }

        [Test]
        public void TheAutoPolicy_NeverBuysTheNewItems_AndTheRecommendedRunLeavesThemAlone()
        {
            foreach (string k in FacilityPolicy.Extras) Assert.IsFalse(FacilityPolicy.Order.Contains(k), k + " はおまかせの順に入らない");
            Assert.AreEqual(FacilityPolicy.Order.Concat(FacilityPolicy.Extras).ToArray(), FacilityPolicy.All);
            var f = new FacilityLevels { Coach = 1, Trainer = 1, Nutri = 1, Dorm = 1, Track = 1, Hill = 1, Gym = 1, Medical = 1, Scout = 1 };
            double funds = 1e9;
            FacilityPolicy.Upgrade(ref f, ref funds, 10);
            foreach (string k in FacilityPolicy.Order) Assert.AreEqual(8, FacilityPolicy.Level(f, k), k);
            foreach (string k in FacilityPolicy.Extras) Assert.AreEqual(0, FacilityPolicy.Level(f, k), k + " は買わない");
            GameSession gs = New();
            AllAt(gs, 8);
            gs.School.CumPoints = 300;
            Assert.AreEqual(0, gs.UpgradeRecommended(), "全部 Lv8 なら推奨は何もしない(新設は含めない)");
            Assert.AreEqual(0, gs.State.Fac.Altitude + gs.State.Fac.Mental + gs.State.Fac.Camp + gs.State.Fac.Manager);
        }

        [Test]
        public void YearSettlement_ChargesTheUpkeepAndTheManagersSalary()
        {
            GameSession a = New(), b = New();
            foreach (GameSession g in new[] { a, b }) { g.State.Funds = 20000; }
            b.State.Fac.Altitude = 3; b.State.Fac.Camp = 2; b.State.Fac.Manager = 5;
            Assert.AreEqual(a.Ops().Expense + 8.0 * (3 + 2) + WorldK.ExpStaff[4], b.Ops().Expense, 1e-9, "支出の見込み = 従来 + 新設の維持費 + 主務の給与");
            Assert.AreEqual(a.Ops().Staff + WorldK.ExpStaff[4], b.Ops().Staff, 1e-9);
            Assert.AreEqual(a.Ops().Upkeep + 40.0, b.Ops().Upkeep, 1e-9);
        }

        [Test]
        public void TheRealSettlement_ChargesTheNewItems_AndPaysFromFunds()
        {
            GameSession gs = New();
            FacilityLevels f = gs.State.Fac;
            f.Altitude = 3; f.Camp = 2; f.Manager = 5;
            gs.State.Fac = f;
            double fundsBefore = gs.State.Funds;
            Ekiden.Domain.Team.Settlement rec = Ekiden.Domain.Team.TeamYear.Settle(gs.State, 300.0, 10, 1, upgrade: false);
            Assert.AreEqual(WorldMath.StaffExpense(f.Coach, f.Trainer, f.Nutri, f.Scout) + WorldK.ExpStaff[4], rec.Staff, 1e-9, "給与に主務 Lv5(260)が入る");
            Assert.AreEqual(WorldMath.FacilityUpkeep(f.Dorm, f.Track, f.Hill, f.Gym, f.Medical) + 8.0 * (3 + 2), rec.Upkeep, 1e-9, "維持費に新設(8 × Lv)が入る");
            double expected = fundsBefore + WorldMath.Income(300.0, 10) - rec.Staff - rec.Upkeep - WorldMath.RunnerExpense(gs.State.Runners.Count);
            Assert.AreEqual(Math.Round(expected, MidpointRounding.AwayFromZero), gs.State.Funds, 1.0);
        }

        [Test]
        public void OpsView_ListsTheNewItemsSeparately_WithReasonsAndEffects()
        {
            GameSession gs = New();
            AllAt(gs, 5);
            gs.State.Funds = 100000;
            gs.School.CumPoints = 100;                                                     // ランク 5
            OpsView v = gs.Ops();
            Assert.AreEqual(FacilityPolicy.Order.Length, v.Rows.Count, "推奨の行は従来どおり 9 つ");
            Assert.IsTrue(v.Rows.All(r => !r.IsExtra));
            CollectionAssert.AreEqual(FacilityPolicy.Extras, v.ExtraRows.Select(r => r.Key).ToArray());
            Assert.IsTrue(v.ExtraRows.All(r => r.IsExtra && r.RecommendedOrder == 0 && r.Level == 0 && r.RecommendedLevel == 0));
            Assert.IsTrue(v.ExtraRows.Single(r => r.Key == "manager").IsStaff);
            Assert.IsFalse(v.ExtraRows.Single(r => r.Key == "camp").IsStaff);
            Assert.AreEqual(WorldK.NewMinRank, v.NewMinRank);
            Assert.AreEqual(4, v.GuidancePoints);
            Assert.IsTrue(v.ExtraRows.All(r => r.BlockedBy == "rank" && !r.CanUpgrade && r.NextCost == 2000.0), "ランク 5 は置けない");
            Assert.AreEqual(0, v.RecommendedCount, "推奨の件数に新設は入らない");
            gs.School.CumPoints = 120;
            v = gs.Ops();
            Assert.IsTrue(v.ExtraRows.All(r => r.BlockedBy == null && r.CanUpgrade));
            gs.State.Funds = 1000;
            Assert.IsTrue(gs.Ops().ExtraRows.All(r => r.BlockedBy == "funds"));
            gs.State.Funds = 100000;
            Assert.IsTrue(gs.UpgradeFacility("altitude").Ok); Assert.IsTrue(gs.UpgradeFacility("altitude").Ok);
            Assert.IsTrue(gs.UpgradeFacility("manager").Ok);
            v = gs.Ops();
            FacilityRowView alt = v.ExtraRows.Single(r => r.Key == "altitude");
            Assert.AreEqual(2, alt.Level);
            FacilityEffectView xp = alt.Effects.Single();
            Assert.AreEqual("xp_sta", xp.Code);
            Assert.AreEqual(0.03 * 2, xp.Value, 1e-12, "高地 Lv2: STA の XP +6%(Lv − 1 ではなく Lv に掛かる)");
            Assert.AreEqual(0.03 * 3, xp.NextValue, 1e-12);
            FacilityRowView mgr = v.ExtraRows.Single(r => r.Key == "manager");
            Assert.AreEqual("guidance", mgr.Effects.Single().Code);
            Assert.AreEqual(0.0, mgr.Effects.Single().Value, "主務 Lv1: 追加 0");
            Assert.AreEqual(0.0, mgr.Effects.Single().NextValue, "Lv2 も 0");
            FacilityRowView camp = v.ExtraRows.Single(r => r.Key == "camp");
            CollectionAssert.AreEqual(new[] { "camp_xp", "camp_injury" }, camp.Effects.Select(e => e.Code).ToArray());
            Assert.AreEqual(0.0, camp.Effects[0].Value); Assert.AreEqual(0.03, camp.Effects[0].NextValue, 1e-12);
            Assert.AreEqual(-0.04, camp.Effects[1].NextValue, 1e-12);
            // Lv6 以上は Lv5 を超えた分が 1/4
            gs.State.Fac.Altitude = 8;
            Assert.AreEqual(0.03 * 5.75, gs.Ops().ExtraRows.Single(r => r.Key == "altitude").Effects.Single().Value, 1e-12);
            gs.State.Fac.Manager = 3;
            Assert.AreEqual(1.0, gs.Ops().ExtraRows.Single(r => r.Key == "manager").Effects.Single().Value);
            Assert.AreEqual(5, gs.Ops().GuidancePoints);
        }

        // ------------------------------------------------------------ 効果(独立の数え直し)
        private static Runner Rookie(int fatigue = 40)
        {
            var r = new Runner { Grade = 2, Fatigue = fatigue };
            int[] st = { 58, 60, 56, 52, 45, 44, 55, 50, 52, 54 }, pot = { 80, 82, 78, 74, 70, 68, 75, 72, 70, 74 };
            Array.Copy(st, r.Stats, 10); Array.Copy(pot, r.Potential, 10);
            return r;
        }

        [Test]
        public void Effects_AreTheDocumentedMultipliers_AndAbsentMeansExactlyUnchanged()
        {
            TrainingMaster m = TrainingMaster.CreateDefault();
            var res = new WeekResolver(m);
            FacilityLevels baseFac = FacilityLevels.AllTwo;
            WeekReport none = res.Resolve(Rookie(), m.Themes["th.base_build"][0], baseFac, 0, new KeyedRngFactory(2026UL), 1, 0, bondAvg: 0);
            // Lv0 を明示しても、従来とビットまで一致
            FacilityLevels zero = baseFac; zero.Altitude = 0; zero.Mental = 0; zero.Camp = 0; zero.Manager = 0;
            WeekReport same = res.Resolve(Rookie(), m.Themes["th.base_build"][0], zero, 0, new KeyedRngFactory(2026UL), 1, 0, bondAvg: 0);
            CollectionAssert.AreEqual(none.XpAdd, same.XpAdd);
            Assert.AreEqual(none.InjuryP.Value, same.InjuryP.Value, 0.0);
            Assert.AreEqual(none.E, same.E, 0.0);
            // 高地だけ: STA の XP だけが増える(Lv8 は 5.75 で効く)
            FacilityLevels alt = baseFac; alt.Altitude = 8;
            WeekReport a = res.Resolve(Rookie(), m.Themes["th.base_build"][0], alt, 0, new KeyedRngFactory(2026UL), 1, 0, bondAvg: 0);
            for (int i = 0; i < 10; i++)
            {
                if (i == (int)Stat.STA) Assert.AreEqual(Math.Round(none.XpAdd[i] * (1 + 0.03 * 5.75), MidpointRounding.AwayFromZero), a.XpAdd[i], 1.0, "STA");
                else Assert.AreEqual(none.XpAdd[i], a.XpAdd[i], "STA 以外は変わらない " + (Stat)i);
            }
            Assert.AreEqual(none.InjuryP.Value, a.InjuryP.Value, 0.0, "高地は故障に効かない");
            // メンタルだけ: MEN と CON
            FacilityLevels men = baseFac; men.Mental = 5;
            WeekReport mn = res.Resolve(Rookie(), m.Themes["th.mountain"][0], men, 0, new KeyedRngFactory(2026UL), 1, 0, bondAvg: 0);
            WeekReport mn0 = res.Resolve(Rookie(), m.Themes["th.mountain"][0], baseFac, 0, new KeyedRngFactory(2026UL), 1, 0, bondAvg: 0);
            for (int i = 0; i < 10; i++)
                if (i != (int)Stat.MEN && i != (int)Stat.CON) Assert.AreEqual(mn0.XpAdd[i], mn.XpAdd[i], "MEN・CON 以外は変わらない " + (Stat)i);
            // 合宿所は合宿の週だけ: 非合宿では何も変わらない
            FacilityLevels camp = baseFac; camp.Camp = 8;
            WeekReport nc = res.Resolve(Rookie(), m.Themes["th.base_build"][0], camp, 0, new KeyedRngFactory(2026UL), 1, 0, bondAvg: 0);
            CollectionAssert.AreEqual(none.XpAdd, nc.XpAdd, "合宿所は非合宿の週に効かない");
            Assert.AreEqual(none.InjuryP.Value, nc.InjuryP.Value, 0.0);
            WeekReport c1 = res.Resolve(Rookie(), m.Themes["th.base_build"][0], camp, 0, new KeyedRngFactory(2026UL), 1, 0, camp: true, bondAvg: 0);
            WeekReport c0 = res.Resolve(Rookie(), m.Themes["th.base_build"][0], baseFac, 0, new KeyedRngFactory(2026UL), 1, 0, camp: true, bondAvg: 0);
            Assert.AreEqual(c0.E, c1.E, 0.0, "合宿所は E に入らない(E_MAX の外)");
            Assert.AreEqual(c0.InjuryP.Value * (1 - 0.04 * 5.75), c1.InjuryP.Value, 1e-12, "合宿の故障の確率 × (1 − 0.04 × Lv_eff)");
            for (int i = 0; i < 10; i++)
                if (c0.XpAdd[i] > 0) Assert.AreEqual(c0.XpAdd[i] * (1 + 0.03 * 5.75), c1.XpAdd[i], 1.0, "合宿の週の全 XP " + (Stat)i);
        }

        [Test]
        public void TheForecast_AgreesWithTheRealWeek_WithTheNewItems()
        {
            TrainingMaster m = TrainingMaster.CreateDefault();
            var res = new WeekResolver(m);
            FacilityLevels f = FacilityLevels.AllTwo;
            f.Altitude = 6; f.Mental = 3; f.Camp = 4;
            foreach (bool camp in new[] { false, true })
                foreach (string theme in new[] { "th.base_build", "th.speed", "th.mountain" })
                    for (int w = 0; w < 4; w++)
                    {
                        Runner real = Rookie(), pred = Rookie();
                        WeekReport rep = res.Resolve(real, m.Themes[theme][w], f, 3, new KeyedRngFactory(77UL), 5, w, camp: camp, bondAvg: 100);
                        ExpectedWeekReport ex = Forecaster.ExpectedWeek(m, pred, m.Themes[theme][w], f, 3, camp: camp, bondAvg: 100);
                        string tag = theme + " w" + w + " camp=" + camp;
                        CollectionAssert.AreEqual(real.Stats, pred.Stats, tag + " stats");
                        CollectionAssert.AreEqual(real.Xp, pred.Xp, tag + " xp");
                        Assert.AreEqual(rep.FEnd, ex.FEnd, tag + " F");
                        Assert.AreEqual(rep.InjuryP.Value, ex.InjuryP, 1e-12, tag + " p");
                    }
        }
    }
}
