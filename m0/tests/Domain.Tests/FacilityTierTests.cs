using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ekiden.Domain.Game;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>施設・スタッフの Lv6〜8(05 §4.2・03 §4.6。L-70): 効果に使うレベル・費用・給与・ランクでの解放・強化の命令。</summary>
    public class FacilityTierTests
    {
        private static void SetAll(GameSession gs, int lv)
        {
            gs.State.Fac = new FacilityLevels { Coach = lv, Trainer = lv, Nutri = lv, Dorm = lv, Track = lv, Hill = lv, Gym = lv, Medical = lv, Scout = lv };
        }

        [Test]
        public void EffLv_IsTheLevelUpToFive_ThenHalfASlope()
        {
            double[] expect = { 1, 2, 3, 4, 5, 5.25, 5.5, 5.75 };
            for (int lv = 1; lv <= 8; lv++) Assert.AreEqual(expect[lv - 1], TrainingMaster.EffLv(lv), 1e-12, "Lv" + lv);
            Assert.AreEqual(8, TrainingMaster.FacMax);
        }

        [Test]
        public void CostsAndSalaries_FollowTheTablesOfSpec05()
        {
            string text = File.ReadAllText(DocPaths.Find("05_world_scout_spec.md"), Encoding.UTF8);
            Match cost = Regex.Match(text, @"\|\s*強化費用[(（]Lv5 以上[)）]\s*\|\s*([\d]+)\s*/\s*([\d]+)\s*/\s*([\d]+)\s*\|");
            Assert.IsTrue(cost.Success, "05 の定数表に『強化費用(Lv5 以上)』の行がある");
            for (int i = 0; i < 3; i++) Assert.AreEqual(double.Parse(cost.Groups[i + 1].Value), WorldK.UpCostHi[i], 1e-9);
            Match sal = Regex.Match(text, @"給与\s*=\s*\[Lv1:0,\s*Lv2:40,\s*Lv3:90,\s*Lv4:160,\s*Lv5:260,\s*Lv6:(\d+),\s*Lv7:(\d+),\s*Lv8:(\d+)\]");
            Assert.IsTrue(sal.Success, "05 §4.1 の給与の式が Lv8 まで");
            for (int i = 0; i < 3; i++) Assert.AreEqual(double.Parse(sal.Groups[i + 1].Value), WorldK.ExpStaff[5 + i], 1e-9);
            // 費用の表(Lv → 次のレベル)
            double[] expect = { 550, 800, 1050, 1300, 3000, 4000, 5000 };
            for (int lv = 1; lv <= 7; lv++) Assert.AreEqual(expect[lv - 1], WorldMath.UpgradeCost(lv), 1e-9, "Lv" + lv + " → " + (lv + 1));
            // 給与(コーチ・トレーナー・栄養士・スカウト担当の合計)
            Assert.AreEqual(4 * 380.0, WorldMath.StaffExpense(6, 6, 6, 6), 1e-9);
            Assert.AreEqual(380.0 + 520.0 + 680.0 + 260.0, WorldMath.StaffExpense(6, 7, 8, 5), 1e-9);
            // 施設の維持費は 8 × Σ(Lv − 1)
            Assert.AreEqual(8.0 * (7 + 6 + 5 + 4 + 3), WorldMath.FacilityUpkeep(8, 7, 6, 5, 4), 1e-9);
        }

        [Test]
        public void UpgradeCommand_GoesPastFive_OnlyWhenTheRankAllows_AndChargesTheTableCost()
        {
            GameSession gs = GameSession.New(20261029UL, Difficulty.Mid);
            SetAll(gs, 5);
            gs.State.Funds = 100000;
            // ランク 5 以下: 上限は Lv5(rank 6 まで)
            gs.School.CumPoints = 119;                                                      // ランク 6
            OpResult no = gs.UpgradeFacility("coach");
            Assert.IsFalse(no.Ok); Assert.AreEqual(OpErr.FacilityCap.Code, no.Code);
            // ランク 7(累計 120): Lv6 まで / ランク 9(230): Lv7 / ランク 10(300): Lv8
            int[] pts = { 120, 230, 300 };
            double funds = gs.State.Funds;
            for (int i = 0; i < 3; i++)
            {
                gs.School.CumPoints = pts[i];
                Assert.AreEqual(6 + i, WorldMath.FacilityCap(gs.School.Rank()));
                int before = FacilityPolicy.Level(gs.State.Fac, "coach");
                Assert.AreEqual(5 + i, before);
                OpResult ok = gs.UpgradeFacility("coach");
                Assert.IsTrue(ok.Ok, ok.Reason);
                Assert.AreEqual(before + 1, FacilityPolicy.Level(gs.State.Fac, "coach"));
                double[] table = { 3000, 4000, 5000 };
                Assert.AreEqual(funds - table[i], gs.State.Funds, 1e-9, "Lv" + before + " → " + (before + 1) + " の費用");
                funds = gs.State.Funds;
                OpResult again = gs.UpgradeFacility("coach");
                Assert.AreEqual(i < 2 ? OpErr.FacilityCap.Code : OpErr.FacilityCap.Code, again.Code, "そのランクの上限で止まる");
            }
            Assert.AreEqual(8, FacilityPolicy.Level(gs.State.Fac, "coach"));
        }

        [Test]
        public void Recommended_FillsLv5First_ThenRaisesTheLowestLevel_KeepingTheReserve_AndMatchesAnIndependentRecount()
        {
            // (1) Lv5 までは寮 → スカウト → コーチ … の順に上限まで(従来どおり)。Lv5 を超える強化は、全部が Lv5 になってから
            var f0 = new FacilityLevels { Coach = 1, Trainer = 1, Nutri = 1, Dorm = 1, Track = 1, Hill = 1, Gym = 1, Medical = 1, Scout = 1 };
            double fd0 = 1e7;
            FacilityPolicy.Upgrade(ref f0, ref fd0, 6);                                     // ランク 6 = 上限 Lv5
            foreach (string k in FacilityPolicy.Order) Assert.AreEqual(5, FacilityPolicy.Level(f0, k), k);
            // (2) Lv5 を超える分: レベルの低いものから 1 つずつ(同じなら順番)。独立の数え直し
            var f = new FacilityLevels { Coach = 5, Trainer = 5, Nutri = 5, Dorm = 5, Track = 5, Hill = 5, Gym = 5, Medical = 5, Scout = 5 };
            double funds = 33000;
            FacilityPolicy.Upgrade(ref f, ref funds, 10);
            int[] lv = Enumerable.Repeat(5, 9).ToArray();
            double fd = 33000;
            while (true)
            {
                int pick = -1;
                for (int k = 0; k < 9; k++) if (lv[k] < 8 && (pick < 0 || lv[k] < lv[pick])) pick = k;
                if (pick < 0) break;
                double c = lv[pick] == 5 ? 3000 : (lv[pick] == 6 ? 4000 : 5000);
                if (fd - c < WorldK.Reserve) break;
                fd -= c; lv[pick]++;
            }
            for (int k = 0; k < 9; k++) Assert.AreEqual(lv[k], FacilityPolicy.Level(f, FacilityPolicy.Order[k]), FacilityPolicy.Order[k]);
            Assert.AreEqual(fd, funds, 1e-6);
            Assert.AreEqual(7, lv[0], "寮(順番の先頭)が Lv7");
            for (int k = 1; k < 9; k++) Assert.AreEqual(6, lv[k], "ほかは Lv6(33000 = 3000 × 9 + 4000 + 予備費)");
            Assert.Less(lv.Max() - lv.Min(), 2, "レベルの低いものから上げるので、差は 1 以内");
        }

        [Test]
        public void ExperienceGrowsWithTheEffectiveLevel_NotTheRawLevel()
        {
            // コーチ Lv8 の全 XP 倍率は 1 + 0.06 × (5.75 − 1) = 1.285(Lv8 の素の値 1.42 ではない)
            GameSession gs = GameSession.New(20261029UL, Difficulty.Mid);
            SetAll(gs, 8);
            FacilityEffectView c = gs.Ops().Rows.Single(r => r.Key == "coach").Effects.Single(e => e.Code == "xp_all");
            Assert.AreEqual(0.06 * 4.75, c.Value, 1e-12);
            SetAll(gs, 5);
            Assert.AreEqual(0.06 * 4.0, gs.Ops().Rows.Single(r => r.Key == "coach").Effects.Single(e => e.Code == "xp_all").Value, 1e-12);
        }
    }
}
