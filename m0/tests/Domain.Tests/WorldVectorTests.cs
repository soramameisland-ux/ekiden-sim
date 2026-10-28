using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>05 §13 のテストベクタ(TV-1〜TV-10。乱数を使わない部分)と、02 §3.2 の県のマスタの検算。</summary>
    public class WorldVectorTests
    {
        [Test]
        public void Geo_Master_Counts()
        {
            Assert.AreEqual(47, Geo.Prefectures.Count);
            var tiers = Geo.Prefectures.GroupBy(p => p.Pop).ToDictionary(g => g.Key, g => g.Count());
            Assert.AreEqual(9, tiers[PopTier.A]); Assert.AreEqual(15, tiers[PopTier.B]); Assert.AreEqual(13, tiers[PopTier.C]); Assert.AreEqual(10, tiers[PopTier.D]);
            var regions = Geo.Prefectures.GroupBy(p => p.Region).ToDictionary(g => g.Key, g => g.Count());
            CollectionAssert.AreEqual(new[] { 1, 6, 7, 9, 7, 5, 4, 8 }, Enum.GetValues(typeof(Region)).Cast<Region>().Select(r => regions[r]).ToArray());
            Assert.AreEqual(112.3, Geo.TotalSupplyWeight, 1e-9);
            Assert.AreEqual(10, Geo.Prefectures.Count(p => p.Elite));
            Assert.AreEqual(216, Geo.Prefectures.Sum(p => p.HighSchoolCount), "高校数 216(02 §3.3)");
            // 地方別の供給比率(02 §3.2)
            double[] share = { 3.6, 10.7, 22.0, 18.7, 16.9, 8.9, 4.0, 15.3 };
            foreach (Region r in Enum.GetValues(typeof(Region)))
                Assert.AreEqual(share[(int)r], Geo.Prefectures.Where(p => p.Region == r).Sum(p => p.SupplyWeight) / Geo.TotalSupplyWeight * 100, 0.05, r.ToString());
            Assert.AreEqual(4.6, Geo.ById(40).SupplyWeight / Geo.TotalSupplyWeight * 100, 0.05);      // 福岡
            Assert.AreEqual(0.9, Geo.ById(5).SupplyWeight / Geo.TotalSupplyWeight * 100, 0.05);       // 秋田(人口区分 D)
        }

        [Test]
        public void Geo_DistanceBand()
        {
            Assert.AreEqual(0, Geo.Band(Region.Kanto, Region.Kanto));
            Assert.AreEqual(1, Geo.Band(Region.Kanto, Region.Chubu));
            Assert.AreEqual(2, Geo.Band(Region.Kanto, Region.Kyushu));
            Assert.AreEqual(1, Geo.Band(Region.Kinki, Region.Shikoku));
            // 隣接は対称
            foreach (Region a in Enum.GetValues(typeof(Region))) foreach (Region b in Enum.GetValues(typeof(Region))) Assert.AreEqual(Geo.Band(a, b), Geo.Band(b, a), a + "/" + b);
        }

        [Test]
        public void TV1_QAndV()
        {
            var r = new WorldRunner { A0 = 45, Gap = 30 }; r.SetValues();
            Assert.AreEqual(63.0, r.Q, 1e-12); Assert.AreEqual(54.0, r.V, 1e-12); Assert.IsTrue(r.V >= WorldK.K_V_TOP);
        }

        [Test]
        public void TV2_FamePoint()
        {
            double[] expect = { 12.5, 9.846, 7.778, 4.915, 1.765, 0.862, 0.604 };
            int[] ks = { 1, 2, 3, 5, 10, 15, 20 };
            for (int i = 0; i < ks.Length; i++) Assert.AreEqual(expect[i], WorldMath.FamePoint(ks[i]), 5e-4, "k=" + ks[i]);
        }

        [Test]
        public void TV3_PrestigeUpdate()
        {
            var s = new School { Hist = 10.0, Prestige = 450.0, FamePoints = 12.5 + 9.846 + 7.778 };
            WorldMath.UpdatePrestige(s);
            Assert.AreEqual(18.0496, s.Hist, 5e-5);
            Assert.AreEqual(587.262, s.Prestige, 5e-4);
            s.Prestige += WorldK.RivalRevert * (600.0 - s.Prestige);
            Assert.AreEqual(588.281, s.Prestige, 5e-4);
            Assert.AreEqual(724.525, 1000.0 * (1 - Math.Exp(-18.0496 / 14.0)), 5e-4);
        }

        [Test]
        public void TV4_OfferBase()
        {
            var s = new School { Region = Region.Kanto, Prestige = 500.0, CoachQ = 0.0, IsPlayer = true, Fac = FacilityLevels.AllTwo };
            s.Fac.Dorm = 3;
            var kanto = new WorldRunner { A0 = 45, Gap = 36, Region = Region.Kanto }; kanto.SetValues();
            Assert.AreEqual(66.6, kanto.Q, 1e-9);
            Assert.AreEqual(59.8, WorldMath.OfferBase(s, kanto, 60.0), 5e-4);
            Assert.AreEqual(41.8, WorldMath.OfferBase(s, new WorldRunner { A0 = 45, Gap = 36, Region = Region.Kyushu, Q = 66.6 }, 60.0), 5e-4);
            Assert.AreEqual(51.8, WorldMath.OfferBase(s, new WorldRunner { A0 = 45, Gap = 36, Region = Region.Chubu, Q = 66.6 }, 60.0), 5e-4);
            Assert.AreEqual(59.8 - 12.0 - 8.0, WorldMath.OfferBase(s, new WorldRunner { Overseas = true, Q = 66.6 }, 60.0), 5e-4, "海外は距離帯 3(−8)");
        }

        [Test]
        public void TV5_Funds()
        {
            double income = WorldMath.Income(600.0, 20);
            Assert.AreEqual(2332.0, income, 1e-9);
            double staff = WorldMath.StaffExpense(3, 2, 2, 3);
            Assert.AreEqual(260.0, staff);
            Assert.AreEqual(48.0, WorldMath.FacilityUpkeep(3, 2, 2, 2, 2));
            Assert.AreEqual(600.0, WorldMath.RunnerExpense(40));
            Assert.AreEqual(1424.0, income - staff - 48.0 - 600.0, 1e-9);
        }

        [Test]
        public void TV6_UpgradeCost()
        {
            Assert.AreEqual(550.0, WorldMath.UpgradeCost(1)); Assert.AreEqual(800.0, WorldMath.UpgradeCost(2));
            Assert.AreEqual(1050.0, WorldMath.UpgradeCost(3)); Assert.AreEqual(1300.0, WorldMath.UpgradeCost(4));
        }

        [Test]
        public void TV7_RankAndCap()
        {
            int[] pts = { 0, 5, 6, 14, 15, 29, 30, 49, 50, 300 };
            int[] rank = { 1, 1, 2, 2, 3, 3, 4, 4, 5, 10 };
            int[] cap = { 3, 3, 3, 3, 4, 4, 4, 4, 5, 5 };
            for (int i = 0; i < pts.Length; i++)
            {
                var s = new School { CumPoints = pts[i] };
                Assert.AreEqual(rank[i], s.Rank(), "累計" + pts[i]);
                Assert.AreEqual(cap[i], WorldMath.FacilityCap(s.Rank()), "累計" + pts[i]);
            }
        }

        [Test]
        public void TV8_Progress()
        {
            Assert.AreEqual(0.955, WorldMath.GapAdj(30), 1e-12);
            double[] gaps = { 10, 20, 30, 40, 45 };
            double[] adj = { 1.205, 1.08, 0.955, 0.83, 0.7675 };
            for (int i = 0; i < gaps.Length; i++) Assert.AreEqual(adj[i], WorldMath.GapAdj(gaps[i]), 1e-12);
            var rows = new[]
            {
                (GrowthKind.Normal, 1.0, new[] { 0.181, 0.353, 0.468, 0.554 }), (GrowthKind.Normal, 3.0, new[] { 0.220, 0.411, 0.535, 0.621 }),
                (GrowthKind.Normal, 5.0, new[] { 0.258, 0.468, 0.602, 0.688 }), (GrowthKind.Early, 3.0, new[] { 0.267, 0.439, 0.535, 0.598 }),
                (GrowthKind.Late, 3.0, new[] { 0.153, 0.334, 0.506, 0.626 }),
            };
            foreach (var (g, lv, exp) in rows)
                for (int y = 1; y <= 4; y++) Assert.AreEqual(exp[y - 1], WorldMath.ProgBase(g, y, lv) * WorldMath.GapAdj(30), 5e-4, g + " Lv" + lv + " 年" + y);
            Assert.AreEqual(63.62, 45 + 30 * WorldMath.ProgBase(GrowthKind.Normal, 4, 3) * WorldMath.GapAdj(30), 5e-3);
        }

        [Test]
        public void TV9_RivalFacilityAndSlots()
        {
            var rows = new[] { (50.0, 1.319, 1, 8), (280.0, 2.264, 3, 10), (500.0, 3.010, 4, 10), (780.0, 3.869, 6, 10), (950.0, 4.359, 6, 10) };
            foreach (var (pr, lv, slots, quota) in rows)
            {
                var s = new School { Prestige = pr };
                Assert.AreEqual(lv, s.Lv(), 5e-4, "名声" + pr);
                Assert.AreEqual(slots, s.ScholSlots(), "名声" + pr);
                Assert.AreEqual(quota, s.Quota(), "名声" + pr);
            }
            Assert.AreEqual(5.369, new School { Prestige = 780.0, CoachQ = 0.1 }.Lv(), 5e-4);
        }

        [Test]
        public void TV10_TicketRunnerMeans()
        {
            Assert.AreEqual(76.0, WorldK.Rookie[0] + 0.6 * WorldK.Rookie[2], 1e-9); Assert.AreEqual(68.5, WorldK.Rookie[0] + 0.3 * WorldK.Rookie[2], 1e-9);
            Assert.AreEqual(70.8, WorldK.Intl[0] + 0.6 * WorldK.Intl[2], 1e-9); Assert.AreEqual(68.4, WorldK.Intl[0] + 0.3 * WorldK.Intl[2], 1e-9);
        }

        [Test]
        public void Pool_Distribution()
        {
            var rngs = new KeyedRngFactory(777UL);
            List<WorldRunner> pool = PoolGenerator.Generate(rngs, 1);
            Assert.AreEqual(520, pool.Count);
            // 520 人 × 複数年度で平均・割合を見る(分布の検算)
            var all = new List<WorldRunner>();
            for (int y = 1; y <= 40; y++) all.AddRange(PoolGenerator.Generate(rngs, y));
            Assert.AreEqual(37.39, all.Average(r => r.A0), 0.12, "A0 の平均は参照モデルと同じ(37.4。[28, 72] に収める分の上振れを含む。強豪県の +3 は基準を下げて打ち消す)");
            Assert.AreEqual(24.0, all.Average(r => r.Gap), 0.4);
            Assert.AreEqual(0.03, all.Count(r => r.Overseas) / (double)all.Count, 0.005);
            Assert.AreEqual(0.19, all.Count(r => r.V >= WorldK.K_V_TOP) / (double)all.Count, 0.03, "上位級は約 19%(05 §2)");
            var japan = all.Where(r => !r.Overseas).ToList();
            foreach (Region reg in Enum.GetValues(typeof(Region)))
            {
                double expect = Geo.Prefectures.Where(p => p.Region == reg).Sum(p => p.SupplyWeight) / Geo.TotalSupplyWeight;
                Assert.AreEqual(expect, japan.Count(r => r.Region == reg) / (double)japan.Count, 0.01, reg.ToString());
            }
            Assert.AreEqual(0.25, all.Count(r => r.Growth == GrowthKind.Early) / (double)all.Count, 0.01);
            Assert.AreEqual(0.20, all.Count(r => r.Growth == GrowthKind.Late) / (double)all.Count, 0.01);
            Assert.AreEqual(0.15, all.Count(r => r.Profile == Profile.Mountain) / (double)all.Count, 0.01);
            // 同じ鍵なら同じ候補(再現性)
            var again = PoolGenerator.Candidate(rngs, 5, 123);
            var first = PoolGenerator.Candidate(rngs, 5, 123);
            Assert.AreEqual(first.A0, again.A0); Assert.AreEqual(first.LookSeed, again.LookSeed);
        }

        [Test]
        public void World_Composition()
        {
            var w = WorldSim.Create(new KeyedRngFactory(1UL), 3);
            Assert.AreEqual(36, w.Schools.Count);
            Assert.AreEqual(6, w.Schools.Count(s => s.Tier == Tier.Elite)); Assert.AreEqual(12, w.Schools.Count(s => s.Tier == Tier.Mid)); Assert.AreEqual(18, w.Schools.Count(s => s.Tier == Tier.Low));
            Assert.AreEqual(20, w.Schools.Count(s => s.Region == Region.Kanto)); Assert.AreEqual(6, w.Schools.Count(s => s.Region == Region.Kinki));
            Assert.AreEqual(4, w.Schools.Count(s => s.Region == Region.Chubu)); Assert.AreEqual(2, w.Schools.Count(s => s.Region == Region.Kyushu));
            Assert.AreEqual(2, w.Schools.Count(s => s.Region == Region.Tohoku)); Assert.AreEqual(1, w.Schools.Count(s => s.Region == Region.Hokkaido)); Assert.AreEqual(1, w.Schools.Count(s => s.Region == Region.Chugoku));
            foreach (School s in w.Schools)
            {
                Assert.LessOrEqual(s.Roster.Count, 40);
                Assert.IsTrue(s.Roster.All(r => r.Grade >= 1 && r.Grade <= 4 && r.Prog >= 0 && r.Prog <= 0.95));
            }
        }

        [Test]
        public void World_Reproducible()
        {
            var a = WorldSim.Create(new KeyedRngFactory(42UL), 2);
            var b = WorldSim.Create(new KeyedRngFactory(42UL), 2);
            for (int i = 0; i < 36; i++)
            {
                Assert.AreEqual(a.Schools[i].Prestige, b.Schools[i].Prestige); Assert.AreEqual(a.Schools[i].Roster.Count, b.Schools[i].Roster.Count);
                Assert.AreEqual(a.Schools[i].CoachQ, b.Schools[i].CoachQ);
            }
            YearResult ra = a.PlayYear(1), rb = b.PlayYear(1);
            CollectionAssert.AreEqual(ra.Order[RaceKind.NewYear], rb.Order[RaceKind.NewYear]);
        }

        [Test]
        public void World_LongRun_StaysStable()
        {
            // 自校なしの世界を 80 年度 × 8 シード。水準が発散・崩壊しない(05 §14.1)。帯は参照モデル(20 回)の値のまわりに余裕を取る
            double top = 0, mid = 0, low = 0, distinct = 0;
            const int seeds = 8;
            for (int seed = 1; seed <= seeds; seed++)
            {
                var w = WorldSim.Create(new KeyedRngFactory((ulong)seed * 7919UL + 3UL), 15);
                var wins = new HashSet<int>();
                for (int y = 1; y <= 80; y++)
                {
                    YearResult r = w.PlayYear(y);
                    foreach (var kv in r.Winner) wins.Add(kv.Value);
                }
                var order = w.Schools.OrderByDescending(s => s.Prestige).ToList();
                top += order.Take(6).Average(s => w.TeamPower(s)) / seeds;
                mid += order.Skip(6).Take(12).Average(s => w.TeamPower(s)) / seeds;
                low += order.Skip(18).Average(s => w.TeamPower(s)) / seeds;
                distinct += wins.Count / (double)seeds;
                foreach (School s in w.Schools) Assert.IsTrue(s.Prestige >= 0 && s.Prestige <= 1000 && s.Roster.Count <= 40);
            }
            Assert.That(top, Is.InRange(67.5, 70.5), "上位 6 校の戦力 P(80 年度)");
            Assert.That(mid, Is.InRange(59.5, 62.0), "中位 12 校");
            Assert.That(low, Is.InRange(54.0, 56.5), "下位 18 校");
            Assert.That(distinct, Is.InRange(11.0, 22.0), "80 年度で優勝した校の数");
        }
    }
}
