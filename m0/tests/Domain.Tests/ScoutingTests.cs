using System;
using System.Linq;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>05 §7(スカウト活動)と 08 R-08 の規則。</summary>
    public class ScoutingTests
    {
        [Test]
        public void AnnualPoints_And_TargetCount()
        {
            // ランク 1〜2 で 36 → 12 人、3〜5 で 40 → 13 人、6〜8 で 44 → 14 人、9 以上で 48 → 16 人(05 §7.1)
            var rows = new[] { (1, 36, 12), (2, 36, 12), (3, 40, 13), (5, 40, 13), (6, 44, 14), (8, 44, 14), (9, 48, 16), (10, 48, 16) };
            foreach (var (rank, pts, targets) in rows)
            {
                Assert.AreEqual(pts, Scouting.AnnualPoints(rank), "ランク" + rank);
                Assert.AreEqual(targets, Scouting.AnnualPoints(rank) / Scouting.PointsPerTarget, "ランク" + rank);
            }
            Assert.AreEqual(38, Scouting.AnnualPoints(1, 2), "06 の補正を足す");
        }

        [Test]
        public void MonthlyGrant_SumsToAnnual()
        {
            foreach (int rank in new[] { 1, 3, 6, 9 })
            {
                int sum = Enumerable.Range(0, 12).Sum(m => Scouting.MonthlyGrant(m, rank));
                Assert.AreEqual(Scouting.AnnualPoints(rank), sum, "ランク" + rank);
            }
            Assert.AreEqual(0, Scouting.MonthlyGrant(0, 5)); Assert.AreEqual(0, Scouting.MonthlyGrant(11, 5));
            Assert.AreEqual(4 + 4, Scouting.MonthlyGrant(2, 3), "6 月にランクの追加分が一括で付く");
        }

        [Test]
        public void InfoSd_ByScoutLevel()
        {
            double[] expect = { 8.0, 6.8, 5.6, 4.4, 3.2 };
            for (int lv = 1; lv <= 5; lv++) Assert.AreEqual(expect[lv - 1], Scouting.InfoSd(lv), 1e-12, "Lv" + lv);
            Assert.AreEqual(1.0, Scouting.InfoSd(10));
        }

        [Test]
        public void Interest_And_Stages()
        {
            Assert.AreEqual(0.0, Scouting.Interest(ScoutStage.None)); Assert.AreEqual(0.0, Scouting.Interest(ScoutStage.Observed));
            Assert.AreEqual(30.0, Scouting.Interest(ScoutStage.Talked)); Assert.AreEqual(90.0, Scouting.Interest(ScoutStage.Invited));
            Assert.AreEqual(WorldK.InterestMax, Scouting.Interest(ScoutStage.Invited));
            Assert.IsTrue(Scouting.CanAct(ScoutStage.None, ScoutStage.Observed, 2)); Assert.IsFalse(Scouting.CanAct(ScoutStage.None, ScoutStage.Observed, 8), "視察は 6〜11 月");
            Assert.IsFalse(Scouting.CanAct(ScoutStage.None, ScoutStage.Talked, 8), "声かけは視察済みの候補に");
            Assert.IsTrue(Scouting.CanAct(ScoutStage.Observed, ScoutStage.Talked, 8)); Assert.IsFalse(Scouting.CanAct(ScoutStage.Observed, ScoutStage.Talked, 5), "声かけは 12〜2 月");
            Assert.IsTrue(Scouting.CanAct(ScoutStage.Talked, ScoutStage.Invited, 10)); Assert.IsFalse(Scouting.CanAct(ScoutStage.Talked, ScoutStage.Invited, 11));
            Assert.IsFalse(Scouting.CanAct(ScoutStage.Invited, ScoutStage.Invited, 9));
        }

        [Test]
        public void Points_CarryOverWithinYear_ThenReset()
        {
            var p = new ScoutPoints();
            p.Grant(Scouting.MonthlyGrant(2, 1)); p.Grant(Scouting.MonthlyGrant(3, 1));
            Assert.AreEqual(8, p.Available);
            Assert.IsTrue(p.TrySpend(5)); Assert.IsFalse(p.TrySpend(4)); Assert.AreEqual(3, p.Available);
            p.Grant(4); Assert.AreEqual(7, p.Available, "同じ年度の中では繰り越す");
            p.YearEnd(); Assert.AreEqual(0, p.Available, "3 月に残りは消える");
        }

        [Test]
        public void Visibility_Rules()
        {
            Assert.IsFalse(Scouting.StyleVisible(ScoutStage.None)); Assert.IsTrue(Scouting.StyleVisible(ScoutStage.Observed));
            Assert.IsFalse(Scouting.GiftPresenceVisible(ScoutStage.Talked)); Assert.IsTrue(Scouting.GiftPresenceVisible(ScoutStage.Invited));
            Assert.IsFalse(Scouting.GiftKindVisible(ScoutStage.Invited, 2), "種類はスカウト担当が Lv3 以上のとき"); Assert.IsTrue(Scouting.GiftKindVisible(ScoutStage.Invited, 3));
            Assert.IsFalse(Scouting.GiftKindVisible(ScoutStage.Observed, 5));
        }

        [Test]
        public void ObserveQ_IsDeterministic_AndHasRightSpread()
        {
            var rngs = new KeyedRngFactory(5UL);
            var pool = PoolGenerator.Generate(rngs, 1);
            foreach (WorldRunner c in pool.Take(20)) Assert.AreEqual(Scouting.ObserveQ(c, 2, rngs), Scouting.ObserveQ(c, 2, rngs));
            var errs1 = pool.Select(c => Scouting.ObserveQ(c, 1, rngs) - c.Q).ToList();
            var errs5 = pool.Select(c => Scouting.ObserveQ(c, 5, rngs) - c.Q).ToList();
            double Sd(System.Collections.Generic.List<double> v) { double m = v.Average(); return Math.Sqrt(v.Average(x => (x - m) * (x - m))); }
            Assert.AreEqual(8.0, Sd(errs1), 0.7); Assert.AreEqual(3.2, Sd(errs5), 0.3);
            Assert.AreEqual(0.0, errs1.Average(), 0.7);
        }
    }
}
