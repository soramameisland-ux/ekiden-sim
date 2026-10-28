using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>候補・途中の選手の自校の選手への展開(05 §6.2・§10.1)。</summary>
    public class RunnerFactoryTests
    {
        private static readonly KeyedRngFactory Rngs = new KeyedRngFactory(20260301UL);

        private static List<WorldRunner> Pool(int year) { return PoolGenerator.Generate(Rngs, year); }

        [Test]
        public void Candidates_ExpandWithinTheSpecRanges()
        {
            int id = 1, n = 0;
            for (int y = 1; y <= 4; y++)
                foreach (WorldRunner c in Pool(y))
                {
                    Runner r = RunnerFactory.FromCandidate(c, id++, y * 48, Rngs, 13);
                    for (int i = 0; i < StatInfo.Count; i++)
                    {
                        Assert.That(r.Stats[i], Is.InRange(RunnerFactory.StatMin, RunnerFactory.StatMax), "能力 " + i);
                        Assert.LessOrEqual(r.Stats[i], r.Potential[i], "I-01");
                        Assert.LessOrEqual(r.Potential[i], 100);
                    }
                    Assert.AreEqual(1, r.Grade);
                    Assert.AreEqual(20, r.Fatigue);
                    Assert.AreEqual(3, r.Motivation);
                    Assert.AreEqual(0, r.Form);
                    Assert.AreEqual(y * 48, r.EnrollWeek);
                    Assert.AreEqual(RecruitSource.Normal, r.RecruitSource);
                    Assert.IsNotNull(r.Style);
                    Assert.That(r.Traits.Count, Is.InRange(1, 2));
                    n++;
                }
            Assert.AreEqual(4 * WorldK.K_POOL_N, n);
        }

        [Test]
        public void PLong_AverageStaysWithinOneOfA0_PerProfile()
        {
            // 05 §6.2: 型の偏りを入れても、P_long の平均は A0 から ±1 以内
            var diff = new Dictionary<Profile, List<double>>();
            int id = 1;
            for (int y = 1; y <= 6; y++)
                foreach (WorldRunner c in Pool(y))
                {
                    if (c.A0 < 40 || c.A0 > 68) continue;                       // 能力の範囲の端(クランプの影響)を除く
                    Runner r = RunnerFactory.FromCandidate(c, id++, 0, Rngs, 13);
                    if (!diff.ContainsKey(c.Profile)) diff[c.Profile] = new List<double>();
                    diff[c.Profile].Add(TeamState.LongP(r) - c.A0);
                }
            foreach (var kv in diff)
            {
                Assert.Greater(kv.Value.Count, 100);
                Assert.AreEqual(0.0, kv.Value.Average(), 1.0, kv.Key.ToString());
            }
        }

        [Test]
        public void Potential_RoomIsAboutGap_AndGiftsRaiseTheCap()
        {
            int id = 1, withGift = 0;
            var rooms = new List<double>();
            var gaps = new List<double>();
            for (int y = 1; y <= 4; y++)
                foreach (WorldRunner c in Pool(y))
                {
                    if (c.A0 > 55) continue;                                     // 素質が 100 で頭打ちになる選手を除く
                    Runner r = RunnerFactory.FromCandidate(c, id++, 0, Rngs, 13);
                    rooms.Add(Enumerable.Range(0, 10).Average(i => r.Potential[i] - r.Stats[i]));
                    gaps.Add(c.Gap);
                    if (r.Gifts.Length > 0) withGift++;
                }
            double meanRoom = rooms.Average(), meanGap = gaps.Average();
            Assert.AreEqual(meanGap, meanRoom, 1.5, "素質の余白の平均は gap の平均に近い(max(2, …) と素養の加算の分だけ上)");
            Assert.That((double)withGift / rooms.Count, Is.InRange(0.15, 0.25), "素養を持つ割合(0 個 80%)");
        }

        [Test]
        public void SameCandidate_SameRunner_AndTheNoiseComesFromLabel8B12()
        {
            WorldRunner c = Pool(1)[17];
            Runner a = RunnerFactory.FromCandidate(c, 1, 0, Rngs, 13), b = RunnerFactory.FromCandidate(c, 99, 0, Rngs, 13);
            CollectionAssert.AreEqual(a.Stats, b.Stats);
            CollectionAssert.AreEqual(a.Potential, b.Potential);
            Assert.AreEqual(a.Style, b.Style);
            CollectionAssert.AreEqual(a.Traits, b.Traits);
            // 素質の雑音は label 8・a = look_seed・b = 12 の N(0,3) を能力の順に
            Pcg32 g = Rngs.Stream(RngRegistry.LabelLook, c.LookSeed, RunnerFactory.PotentialNoiseKey);
            int[] entry = Enumerable.Range(0, 10).Select(i => Math.Max(20, Math.Min(95, Rounding.HalfUp(AiRacing.ExpandStats(c.A0, c.Profile, c.N10(Rngs))[i])))).ToArray();
            Gift[] gifts = Individuality.GenerateGifts(Rngs, c.LookSeed);
            var pot = new int[10];
            for (int i = 0; i < 10; i++) pot[i] = Math.Min(100, entry[i] + Math.Max(2, Rounding.HalfUp(c.Gap + g.Normal(0, 3))));
            Individuality.AddGiftPotential(pot, gifts);
            CollectionAssert.AreEqual(pot, a.Potential);
            CollectionAssert.AreEqual(entry, a.Stats);
            // 別の候補は別の選手になる
            Runner other = RunnerFactory.FromCandidate(Pool(1)[18], 2, 0, Rngs, 13);
            CollectionAssert.AreNotEqual(a.Stats, other.Stats);
        }

        [Test]
        public void Origin_Overseas_And_Distance()
        {
            var pool = Pool(2);
            WorldRunner intl = pool.First(c => c.Overseas);
            Runner r = RunnerFactory.FromCandidate(intl, 1, 0, Rngs, 13);
            Assert.IsTrue(r.Overseas);
            Assert.AreEqual(intl.CountryIndex + 1, r.Country);
            Assert.AreEqual(0, r.Pref);
            Assert.AreEqual(0, r.HighSchool);
            Assert.IsTrue(r.Far);
            Assert.IsFalse(r.Near);
            foreach (WorldRunner c in pool.Where(x => !x.Overseas).Take(200))
            {
                Runner j = RunnerFactory.FromCandidate(c, 1, 0, Rngs, 13);         // 自校は関東の県(13)
                Region home = Geo.ById(13).Region;
                int band = Geo.Band(Geo.ById(j.Pref).Region, home);
                Assert.AreEqual(band >= 2, j.Far);
                Assert.AreEqual(band == 0, j.Near);
                Assert.That(j.HighSchool, Is.InRange(1, Geo.HighSchoolTotal));
            }
        }

        [Test]
        public void HighSchoolIds_CoverAll216_WithinEachPrefecture()
        {
            Assert.AreEqual(Geo.HighSchoolTotal, Geo.Prefectures.Sum(p => p.HighSchoolCount), "02 §3.3: 合計 216 校");
            var seen = new HashSet<int>();
            int first = 1;
            foreach (Prefecture p in Geo.Prefectures)
            {
                var ids = new HashSet<int>();
                for (int k = 0; k < 1000; k++) ids.Add(Geo.HighSchoolId(p.Id, k / 1000.0));
                Assert.AreEqual(p.HighSchoolCount, ids.Count, p.Name);
                Assert.AreEqual(first, ids.Min());
                Assert.AreEqual(first + p.HighSchoolCount - 1, ids.Max());
                first += p.HighSchoolCount;
                foreach (int i in ids) seen.Add(i);
            }
            Assert.AreEqual(216, seen.Count);
            Assert.AreEqual(Geo.HighSchoolTotal, Geo.HighSchoolId(47, 0.999999));
        }

        [Test]
        public void InFlightRunners_StartFromTheirCurrentPower_UnderTheirPotential()
        {
            WorldSim w = WorldSim.Create(new KeyedRngFactory(77UL), 15);
            School s = w.Schools[5];
            var byGrade = new Dictionary<int, List<double>>();
            int id = 1;
            foreach (WorldRunner wr in s.Roster)
            {
                Runner r = RunnerFactory.FromInFlight(wr, id++, 1, w.Rngs, 13);
                Assert.AreEqual(wr.Grade, r.Grade);
                Assert.AreEqual((1 - wr.Grade) * 48, r.EnrollWeek, "入学週は (年度 − 学年) × 48");
                for (int i = 0; i < 10; i++) Assert.LessOrEqual(r.Stats[i], r.Potential[i]);
                if (!byGrade.ContainsKey(wr.Grade)) byGrade[wr.Grade] = new List<double>();
                byGrade[wr.Grade].Add(TeamState.LongP(r));
            }
            Assert.AreEqual(4, byGrade.Count);
            for (int g = 2; g <= 4; g++) Assert.Greater(byGrade[g].Average(), byGrade[g - 1].Average() - 0.5, "学年が上がるほど力が高い: " + g);
            Assert.Greater(byGrade[4].Average(), byGrade[1].Average() + 3);
        }

        [Test]
        public void KeyRegistry_MentionsPotentialNoise()
        {
            Assert.That(RngRegistry.Ranges.Any(r => r.Label == RngRegistry.LabelLook && r.Purpose.Contains("b = 12")));
        }
    }
}
