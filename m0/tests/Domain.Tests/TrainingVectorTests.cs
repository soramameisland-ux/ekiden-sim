using System;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Training;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>03 §12 のテストベクタ(TV-2〜TV-7)と、10 の TV-2・TV-3。値は ref_model_03.py / ref_model_10.py の `vectors` の出力。</summary>
    public class TrainingVectorTests
    {
        private static readonly TrainingMaster M = TrainingMaster.CreateDefault();
        private static readonly FacilityLevels Fac2 = FacilityLevels.AllTwo;

        private static Runner NewRunner(int[] stats, int[] pot, int grade = 2, int fatigue = 0)
        {
            var r = new Runner { Grade = grade, Fatigue = fatigue };
            Array.Copy(stats, r.Stats, 10);
            Array.Copy(pot, r.Potential, 10);
            return r;
        }

        private static readonly int[] StatsA = { 58, 60, 56, 52, 45, 44, 55, 50, 52, 54 };
        private static readonly int[] PotA = { 80, 82, 78, 74, 70, 68, 75, 72, 70, 74 };

        [Test]
        public void TV2_Tables()
        {
            int[] fs = { 0, 39, 40, 55, 69, 70, 84, 85, 100 };
            double[] e = { 1.0, 1.0, 1.0, 0.9224, 0.85, 0.7, 0.7, 0.3, 0.3 };
            double[] mm = { 1.0, 1.0, 1.0, 1.2586, 1.5, 2.0, 2.0, 4.0, 4.0 };
            for (int i = 0; i < fs.Length; i++)
            {
                Assert.AreEqual(e[i], WeekResolver.EFatigue(fs[i]), 5e-5, "E_fatigue " + fs[i]);
                Assert.AreEqual(mm[i], WeekResolver.MFatigue(fs[i]), 5e-5, "m_fatigue " + fs[i]);
            }
            // T(stat) xu
            foreach (var kv in new[] { (30, 380), (50, 500), (55, 530), (80, 680), (99, 794) })
                Assert.AreEqual(kv.Item2, Ekiden.Domain.Numerics.Rounding.HalfUp(10 * (TrainingMaster.K_T_BASE + TrainingMaster.K_T_SLOPE * kv.Item1)));
            // peaking
            int[] withSharpen = { 0, 3, 6, 8, -8, -8 };
            int[] noSharpen = { 0, 2, 3, 4, -8, -8 };
            for (int w = 0; w <= 5; w++)
            {
                Assert.AreEqual(withSharpen[w], WeekResolver.PeakingBonus(w, true, false), "peaking " + w + " T");
                Assert.AreEqual(noSharpen[w], WeekResolver.PeakingBonus(w, false, false), "peaking " + w + " F");
            }
            int[] master = { 0, 7, 10, 12, -8 };
            for (int w = 0; w <= 4; w++) Assert.AreEqual(master[w], WeekResolver.PeakingBonus(w, true, true), "peaking_master " + w);
            // form_level
            double[] sc = { 30, 20, 19.9, 8, 7.9, 0, -7.9, -8, -19.9, -20, -30 };
            int[] lv = { 2, 2, 1, 1, 0, 0, 0, -1, -1, -2, -2 };
            for (int i = 0; i < sc.Length; i++) Assert.AreEqual(lv[i], WeekResolver.FormLevel(sc[i]), "form_level " + sc[i]);
        }

        [Test]
        public void TV3_OneWeek()
        {
            var r = NewRunner(StatsA, PotA, 2, 40);
            var rep = new WeekResolver(M).Resolve(r, M.Themes["th.base_build"][0], Fac2, 0, new KeyedRngFactory(2026UL), 1, 0, bondAvg: 0);
            Assert.AreEqual("ks.pace_run", rep.Menu.Key1);
            Assert.AreEqual(1.166, rep.E, 5e-7); Assert.AreEqual(0.564, rep.R, 5e-7); Assert.AreEqual(29.0, rep.L); Assert.AreEqual(46, rep.FEnd);
            CollectionAssert.AreEqual(new[] { 0, 160, 137, 0, 0, 0, 49, 0, 0, 0 }, rep.XpAdd);
            Assert.AreEqual(0, rep.Levelups);
            CollectionAssert.AreEqual(StatsA, r.Stats);
            CollectionAssert.AreEqual(new[] { 0, 160, 137, 0, 0, 0, 49, 0, 0, 0 }, r.Xp);
            Assert.AreEqual(0.006416, rep.InjuryP.Value, 5e-7); Assert.IsNull(rep.NewInjury);
            Assert.AreEqual(0, rep.Peaking); Assert.AreEqual(5.672137, rep.Noise, 5e-7); Assert.AreEqual(7.672137, rep.FormScore, 5e-7); Assert.AreEqual(0, rep.Form);
        }

        [Test]
        public void TV4_FatigueOverFourWeeks()
        {
            var r = NewRunner(StatsA, Enumerable.Repeat(99, 10).ToArray(), 2, 40);
            var res = new WeekResolver(M);
            double[] loads = { 29, 34, 35, 19 };
            int[] fend = { 46, 54, 59, 45 };
            for (int w = 0; w < 4; w++)
            {
                var rep = res.Resolve(r, M.Themes["th.base_build"][w], Fac2, 0, new KeyedRngFactory(2026UL), 1, w);
                Assert.AreEqual(loads[w], rep.L); Assert.AreEqual(0.564, rep.R, 5e-5); Assert.AreEqual(fend[w], rep.FEnd);
            }
        }

        [Test]
        public void TV5_Peaking()
        {
            var r = NewRunner(new[] { 58, 60, 56, 52, 45, 44, 50, 50, 52, 54 }, Enumerable.Repeat(99, 10).ToArray(), 3, 60);
            var res = new WeekResolver(M);
            double[] loads = { 20, 15, 10, 6 };
            int[] fend = { 46, 35, 25, 14 };
            int[] taper = { 0, 1, 2, 3 };
            int[] peaking = { 0, 3, 6, 8 };
            double[] rr = { 0.564, 0.564, 0.564, 0.664 };
            for (int k = 0; k < 4; k++)
            {
                var rep = res.Resolve(r, M.Taper[k], Fac2, 6, new KeyedRngFactory(2026UL), 2, 100 + k);
                Assert.AreEqual(loads[k], rep.L, "L T" + (k + 1)); Assert.AreEqual(rr[k], rep.R, 5e-5); Assert.AreEqual(fend[k], rep.FEnd);
                Assert.AreEqual(taper[k], r.TaperWeeks); Assert.AreEqual(peaking[k], rep.Peaking);
            }
        }

        [Test]
        public void TV6_InjuryProbability()
        {
            var menus = new[]
            {
                (new Menu("ks.threshold", "ks.pace_run", "mil.std"), new[] { 0.54, 0.64, 0.79, 2.17 }),
                (new Menu("ks.interval", "ks.long_run", "mil.high"), new[] { 0.79, 0.93, 1.14, 3.16 }),
                (new Menu("ks.repetition", "ks.downhill", "mil.max"), new[] { 1.23, 1.45, 1.79, 4.94 }),
            };
            int[] fs = { 30, 50, 66, 86 };
            foreach (var (menu, expected) in menus)
            {
                for (int i = 0; i < 4; i++)
                {
                    var rr = NewRunner(new[] { 50, 50, 50, 50, 50, 50, 50, 50, 50, 54 }, Enumerable.Repeat(99, 10).ToArray(), 1, fs[i]);
                    var rep = new WeekResolver(M).Resolve(rr, menu, Fac2, 0, new KeyedRngFactory(1UL), 1, 1, safety: false);
                    Assert.AreEqual(expected[i], Math.Round(rep.InjuryP.Value * 100, 2), 1e-9, menu.Key1 + " F=" + fs[i]);
                }
            }
        }

        [Test]
        public void TV7_SafetyNet()
        {
            var rr = NewRunner(Enumerable.Repeat(50, 10).ToArray(), Enumerable.Repeat(99, 10).ToArray(), 1, 85);
            var rep = new WeekResolver(M).Resolve(rr, M.Themes["th.camp"][2], Fac2, 4, new KeyedRngFactory(1UL), 1, 1, camp: true);
            Assert.AreEqual("ks.core", rep.Menu.Key1); Assert.AreEqual("ks.rest", rep.Menu.Key2); Assert.AreEqual("mil.low", rep.Menu.Mileage); Assert.AreEqual(35, rep.FEnd);
            rr = NewRunner(Enumerable.Repeat(50, 10).ToArray(), Enumerable.Repeat(99, 10).ToArray(), 1, 84);
            rep = new WeekResolver(M).Resolve(rr, M.Themes["th.camp"][2], Fac2, 4, new KeyedRngFactory(1UL), 1, 1, camp: true);
            Assert.AreEqual("ks.long_run", rep.Menu.Key1); Assert.AreEqual("ks.interval", rep.Menu.Key2); Assert.AreEqual("mil.max", rep.Menu.Mileage); Assert.AreEqual(81, rep.FEnd);
        }

        // ---- 10 の TV-2(乗数)と TV-3(1 週の解決。山対策テーマの第1週)
        [Test]
        public void Individuality_TV2_Multipliers()
        {
            double[] a = Individuality.GrowthMult(new Style(StyleForm.Speed, StyleRun.Pack, StyleFlow.Chase), new Gift[0]);
            CollectionAssert.AreEqual(new[] { 1.2, 0.8, 0.8, 1.2, 1, 1, 1, 1, 1, 1 }, a.Select(x => Math.Round(x, 4)).ToArray());
            double[] b = Individuality.GrowthMult(new Style(StyleForm.Long, StyleRun.Solo, StyleFlow.Lead), new[] { Gift.Climber });
            CollectionAssert.AreEqual(new[] { 0.8, 1.2, 1.2, 0.8, 1.15, 1, 1, 1, 1, 1 }, b.Select(x => Math.Round(x, 4)).ToArray());
            double[] c = Individuality.GrowthMult(new Style(StyleForm.Balance, StyleRun.Free, StyleFlow.Even), new[] { Gift.Ace });
            CollectionAssert.AreEqual(new[] { 1.05, 1.05, 1.05, 1.05, 1.05, 1.05, 1.15, 1.15, 1.05, 1.05 }, c.Select(x => Math.Round(x, 4)).ToArray());
            Assert.AreEqual(0.30, Individuality.BondK(new Style(StyleForm.Speed, StyleRun.Pack, StyleFlow.Chase)));
            Assert.AreEqual(0.05, Individuality.BondK(new Style(StyleForm.Long, StyleRun.Solo, StyleFlow.Lead)));
            Assert.AreEqual(1.25, Individuality.IndMult(new Style(StyleForm.Long, StyleRun.Solo, StyleFlow.Lead)));
            Assert.AreEqual(0.15, Individuality.BondK(Style.Neutral));
        }

        [Test]
        public void Individuality_TV3_OneWeek()
        {
            int[] bs = { 50, 55, 52, 48, 45, 44, 50, 48, 46, 50 };
            int[] pt = { 75, 80, 77, 73, 70, 69, 75, 73, 71, 75 };
            var cases = new[]
            {
                (style: (Style?)null, gifts: new Gift[0], indiv: false, e: 1.2535, xu: new[] { 0, 72, 21, 0, 210, 212, 0, 0, 0, 0 }),
                (style: (Style?)new Style(StyleForm.Speed, StyleRun.Pack, StyleFlow.Chase), gifts: new Gift[0], indiv: false, e: 1.3409, xu: new[] { 0, 61, 18, 0, 225, 227, 0, 0, 0, 0 }),
                (style: (Style?)new Style(StyleForm.Long, StyleRun.Solo, StyleFlow.Lead), gifts: new Gift[0], indiv: true, e: 1.4939, xu: new[] { 0, 102, 29, 0, 251, 253, 0, 0, 0, 0 }),
                (style: (Style?)new Style(StyleForm.Balance, StyleRun.Free, StyleFlow.Even), gifts: new[] { Gift.Climber }, indiv: false, e: 1.2535, xu: new[] { 0, 72, 21, 0, 265, 212, 0, 0, 0, 0 }),
            };
            foreach (var c in cases)
            {
                int[] pot = (int[])pt.Clone();
                Individuality.AddGiftPotential(pot, c.gifts);              // 素養は入学時に素質の上限を加える(10 §4)。ベクタの 265 はこれを含む
                var r = NewRunner(bs, pot, 2, 0);
                r.SetIndividuality(c.style, c.gifts);
                var rep = new WeekResolver(M).Resolve(r, M.Themes["th.mountain"][0], FacilityLevels.AllTwo, 0, new KeyedRngFactory(2026UL), 1, 0, individual: c.indiv, bondAvg: 500.0);
                Assert.AreEqual(c.e, rep.E, 5e-5, "E");
                CollectionAssert.AreEqual(c.xu, rep.XpAdd, "xu");
            }
        }

        [Test]
        public void Style_Codes()
        {
            Assert.AreEqual("BFE", Style.Neutral.Code);
            Assert.AreEqual("SPC", new Style(StyleForm.Speed, StyleRun.Pack, StyleFlow.Chase).Code);
            Assert.IsTrue(Style.TryParse("LSL", out Style s)); Assert.AreEqual(new Style(StyleForm.Long, StyleRun.Solo, StyleFlow.Lead), s);
            Assert.IsFalse(Style.TryParse("XYZ", out _)); Assert.IsFalse(Style.TryParse("SP", out _)); Assert.IsFalse(Style.TryParse(null, out _)); Assert.IsFalse(Style.TryParse("sPc", out _));
        }
    }
}
