using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Text;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>04 §13 のテストベクタ(TV-1〜TV-11)と、10 の TV-5。値は ref_model_04.py の `vectors` の出力。</summary>
    public class RaceVectorTests
    {
        private static RaceRunner Fixed(int[] stats, int form = 0, params string[] skills)
        {
            var r = new RaceRunner { Form = form };
            Array.Copy(stats, r.Stats, 10);
            foreach (string s in skills) r.Skills.Add("skill." + s);
            return r;
        }

        private static RaceRunner Flat(int p, int form = 0, string name = null)
        {
            var r = Fixed(Enumerable.Repeat(p, 10).ToArray(), form);
            r.Name = name;
            return r;
        }

        /// <summary>04 の表の総合タイム表記(秒を四捨五入)。画面の <see cref="Fmt.Total"/> は切り捨てで、1 秒ずれることがある(L-36。画面が正)。</summary>
        private static string FmtT(double sec)
        {
            long s = (long)Math.Round(sec, MidpointRounding.ToEven);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", s / 3600, s % 3600 / 60, s % 60);
        }

        private static readonly int[] StatsTv1 = { 62, 70, 66, 58, 52, 55, 60, 64, 55, 58 };

        [Test]
        public void TV1_SectionRating()
        {
            RaceRunner r = Fixed(StatsTv1);
            Assert.AreEqual(63.8, RaceTables.Rating(r.Stats, SectionType.Fast), 1e-9);
            Assert.AreEqual(65.2, RaceTables.Rating(r.Stats, SectionType.Mid), 1e-9);
            Assert.AreEqual(65.6, RaceTables.Rating(r.Stats, SectionType.Long), 1e-9);
            Assert.AreEqual(60.3, RaceTables.Rating(r.Stats, SectionType.Climb), 1e-9);
            Assert.AreEqual(60.65, RaceTables.Rating(r.Stats, SectionType.Descent), 1e-9);
            Assert.AreEqual(68.3, RaceMath.SectionRating(Fixed(StatsTv1, 0, "mountain_god"), SectionType.Climb), 1e-9);
            Assert.AreEqual(68.6, RaceMath.SectionRating(Fixed(StatsTv1, 0, "ace_section"), SectionType.Long, longest: true), 1e-9);
        }

        [Test]
        public void TV2_SectionTime()
        {
            Assert.AreEqual(185.1824, Pace50.Of(23.1), 5e-5);
            double bt = RaceMath.BaseTime(23.1, 1.0, 65.6);
            Assert.AreEqual(4133.569, bt, 5e-4);
            Assert.AreEqual("1:08:54", FmtT(bt));
            double mult = 1.0 + RaceConstants.FormAdj[1 + 2] + 0.0 + 0.004 + 0.0 + (-0.003) + 0.0;
            Assert.AreEqual(0.9940, mult, 5e-5);
            Assert.AreEqual(4108.767, bt * mult, 5e-4);
            Assert.AreEqual(41088, Ekiden.Domain.Numerics.Rounding.HalfUp(bt * mult * 10));
            Assert.AreEqual(4315.446, bt * mult + bt * 0.05, 5e-4);
            Assert.AreEqual(4274.110, bt * mult + bt * 0.05 * RaceConstants.GekiBrakeMult, 5e-4);
            var byP = new Dictionary<int, string> { { 50, "1:11:18" }, { 65, "1:08:59" }, { 75, "1:07:27" }, { 85, "1:05:54" }, { 95, "1:04:22" } };
            foreach (var kv in byP) Assert.AreEqual(kv.Value, FmtT(RaceMath.BaseTime(23.1, 1.0, kv.Key)), "P=" + kv.Key);
        }

        [Test]
        public void TV3_LegRandom()
        {
            var rn = new KeyedRngFactory(20260101UL).Stream(4, 11, 101);
            LegDraw d = RaceMath.DrawLeg(rn, Fixed(StatsTv1), RaceTables.Rule(RaceKind.Fall).Weight, Instruction.Keep, 1.0, 8.0, 18.0, SectionType.Fast);
            Assert.AreEqual(0.003477, d.Noise, 5e-7); Assert.AreEqual(0.0, d.Brake); Assert.AreEqual(1.0 / 3.0, d.BrakePos, 5e-4); Assert.AreEqual(0.0, d.Boost); Assert.AreEqual(0.0, d.Stall);
            Assert.AreEqual(0.00720, RaceConstants.NoiseSd * (1.5 - 0.60), 5e-6);
            double pr = Math.Max(0.5, Math.Min(1.5, 1.5 - 0.64)) * RaceTables.Rule(RaceKind.NewYear).Weight;
            Assert.AreEqual(1.858, RaceConstants.BrakeP * (1.5 - 0.60) * pr * 100, 5e-4);
        }

        [Test]
        public void TV3b_Stall()
        {
            RaceRunner r = Fixed(StatsTv1, 1);
            Assert.AreEqual(0.2122, RaceMath.PaceQ(r, 23.1, SectionType.Long, 4.0), 5e-5);
            double[] stall = { 0.02081, 0.0, 0.0 };
            double[] pstall = { 0.5306, 0.2122, 0.0637 };
            var ins = new[] { Instruction.Attack, Instruction.Keep, Instruction.Hold };
            double q0 = RaceMath.PaceQ(r, 23.1, SectionType.Long, 4.0);
            for (int i = 0; i < 3; i++)
            {
                var rn = new KeyedRngFactory(20260101UL).Stream(4, 13, 105);
                LegDraw d = RaceMath.DrawLeg(rn, r, RaceTables.Rule(RaceKind.NewYear).Weight, ins[i], 1.0, 23.1, 4.0, SectionType.Long);
                Assert.AreEqual(pstall[i], Math.Min(0.95, q0 * RaceConstants.InstrStall[(int)ins[i]]), 5e-5);
                Assert.AreEqual(stall[i], d.Stall, 5e-6, ins[i].ToString());
            }
        }

        [Test]
        public void TV4_Environment()
        {
            var f = new KeyedRngFactory(20260101UL);
            Env[] fall = RaceMath.GenEnv(f.Stream(4, 11, 9999), RaceKind.Fall);
            Assert.AreEqual(16.9, fall[0].Temp, 5e-3); Assert.AreEqual(-1.29, fall[0].Wind, 5e-3); Assert.IsFalse(fall[0].Rain);
            Env[] nat = RaceMath.GenEnv(f.Stream(4, 11, 9999), RaceKind.National);
            Assert.AreEqual(9.9, nat[0].Temp, 5e-2); Assert.AreEqual(-2.05, nat[0].Wind, 5e-3);
            Env[] ny = RaceMath.GenEnv(f.Stream(4, 11, 9999), RaceKind.NewYear);
            Assert.AreEqual(2, ny.Length);
            Assert.AreEqual(3.08, ny[0].Temp, 5e-3); Assert.AreEqual(-2.31, ny[0].Wind, 5e-3); Assert.AreEqual(5.66, ny[1].Temp, 5e-3); Assert.AreEqual(0.92, ny[1].Wind, 5e-3);
            Assert.IsFalse(ny[0].Rain); Assert.IsFalse(ny[1].Rain);
            Assert.AreEqual(0.015603, RaceMath.EnvAdj(new Env { Temp = 25.0, Wind = 3.0, Rain = true }, Fixed(StatsTv1)), 5e-7);
        }

        [Test]
        public void TV5_Hungarian()
        {
            double[][] c = { new double[] { 10, 9, 8, 7 }, new double[] { 4, 3, 5, 9 }, new double[] { 8, 7, 6, 2 } };
            int[] a = Hungarian.Solve(c);
            CollectionAssert.AreEqual(new[] { 2, 1, 3 }, a);
            Assert.AreEqual(13, c[0][a[0]] + c[1][a[1]] + c[2][a[2]]);
        }

        [Test]
        public void TV6_MiniRace()
        {
            var teams = new List<RaceTeam>();
            int tid = 0;
            foreach (var (pw, fm) in new[] { (70, 1), (66, 0), (62, -1) })
            {
                teams.Add(new RaceTeam { Id = tid++, Lineup = Enumerable.Range(0, 6).Select(_ => Flat(pw, fm)).ToArray(), Geki = 0 });
            }
            var f = new KeyedRngFactory(20260101UL);
            Env[] envs = RaceMath.GenEnv(f.Stream(4, 11, 9999), RaceKind.Fall);
            List<TeamResult> res = RaceSimulator.Simulate(RaceKind.Fall, teams, envs, (t, si) => f.Stream(4, 11, t * 100 + si));
            CollectionAssert.AreEqual(new[] { 13484, 9707, 14444, 10365, 10729, 17351 }, res[0].Sections);
            CollectionAssert.AreEqual(new[] { 13484, 23191, 37635, 48000, 58729, 76080 }, res[0].Cum);
            Assert.AreEqual("2:06:48", FmtT(res[0].TotalDs / 10.0)); Assert.AreEqual(0, res[0].Id); Assert.IsFalse(res[0].Kuriage); Assert.AreEqual(0, res[0].Events.Count);
            CollectionAssert.AreEqual(new[] { 14217, 9902, 14735, 10928, 10975, 18450 }, res[1].Sections);
            CollectionAssert.AreEqual(new[] { 14217, 24119, 38854, 49782, 60757, 79207 }, res[1].Cum);
            Assert.AreEqual("2:12:01", FmtT(res[1].TotalDs / 10.0)); Assert.AreEqual(1, res[1].Id);
            CollectionAssert.AreEqual(new[] { "stall", "stall", "stall" }, res[1].Events.Select(e => e.Kind).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 3, 5 }, res[1].Events.Select(e => e.Section).ToArray());
            CollectionAssert.AreEqual(new[] { 14141, 10370, 14888, 10807, 11149, 18088 }, res[2].Sections);
            CollectionAssert.AreEqual(new[] { 14141, 24511, 39399, 50206, 61129, 79217 }, res[2].Cum);
            Assert.AreEqual("2:12:02", FmtT(res[2].TotalDs / 10.0)); Assert.AreEqual(2, res[2].Id); Assert.IsTrue(res[2].Kuriage);
            CollectionAssert.AreEqual(new[] { "brake", "kuriage" }, res[2].Events.Select(e => e.Kind).ToArray());
            Assert.AreEqual(1, res[2].Events[0].Section); Assert.AreEqual(0.0476, res[2].Events[0].Value, 5e-5); Assert.AreEqual(4, res[2].Events[1].Section);
            Assert.AreEqual(3, res.Count);
        }

        [Test]
        public void TV7_RankingRule()
        {
            var keys = new List<TeamResult>
            {
                new TeamResult { Id = 2, Kuriage = false, TotalDs = 78000, Sections = new[] { 1, 2, 3 } },
                new TeamResult { Id = 1, Kuriage = false, TotalDs = 78000, Sections = new[] { 1, 2, 2 } },
                new TeamResult { Id = 0, Kuriage = true, TotalDs = 70000, Sections = new[] { 1, 2, 3 } },
            };
            RaceSimulator.StableSort(keys, RaceSimulator.CompareResults);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, keys.Select(k => k.Id).ToArray());
        }

        [Test]
        public void TV8_Qualifier()
        {
            var teams = new List<QualifierTeam>();
            int tid = 0;
            foreach (int pw in new[] { 68, 66, 64 })
                teams.Add(new QualifierTeam { Id = tid++, Runners = Enumerable.Range(0, 12).Select(i => Flat(pw - (i % 4) * 2)).ToArray() });
            var f = new KeyedRngFactory(20260101UL);
            var res = Qualifier.Run(teams, (t, i) => f.Stream(4, 14, t * 100 + i), new Env { Temp = 18.0, Wind = 0.0, Rain = false });
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, res.Select(r => r.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 360986, 362190, 362425 }, res.Select(r => r.Total).ToArray());
            Assert.AreEqual("10:01:39", FmtT(res[0].Total / 10.0));
            CollectionAssert.AreEqual(new[] { 35585, 35719, 35740, 35815, 36069, 36191, 36207, 36227, 36562, 36871, 36936, 37381 }, res[0].Times);
            CollectionAssert.AreEqual(new[] { 35414, 35640, 35925, 36034, 36211, 36334, 36510, 36687, 36687, 36748, 37637, 39055 }, res[1].Times);
            CollectionAssert.AreEqual(new[] { 35579, 35779, 35859, 36116, 36117, 36408, 36506, 36529, 36643, 36889, 36930, 37587 }, res[2].Times);
        }

        [Test]
        public void TV10_PaceQAndGauge()
        {
            var cases = new[]
            {
                ("バランス型", new[] { 70, 70, 70, 70, 70, 70, 70, 70, 70, 70 }, 0, 0.310, "△", 0.210, "〇"),
                ("バランス型", new[] { 70, 70, 70, 70, 70, 70, 70, 70, 70, 70 }, 2, 0.220, "〇", 0.120, "◎"),
                ("持久型", new[] { 62, 76, 70, 64, 68, 68, 70, 70, 70, 70 }, 0, 0.218, "〇", 0.076, "◎"),
                ("持久型", new[] { 62, 76, 70, 64, 68, 68, 70, 70, 70, 70 }, 2, 0.128, "◎", 0.030, "◎"),
                ("スピード型", new[] { 78, 64, 66, 76, 68, 68, 70, 70, 70, 70 }, 0, 0.386, "△", 0.334, "△"),
                ("スピード型", new[] { 78, 64, 66, 76, 68, 68, 70, 70, 70, 70 }, 2, 0.296, "△", 0.244, "〇"),
            };
            foreach (var c in cases)
            {
                RaceRunner r = Fixed(c.Item2, c.Item3);
                Assert.AreEqual(c.Item4, RaceMath.PaceQ(r, 21.3, SectionType.Long, 10.0), 5e-4, c.Item1 + " 長");
                Assert.AreEqual(c.Item5, DayPlanner.Gauge(r, 21.3, SectionType.Long, 10.0).mark);
                Assert.AreEqual(c.Item6, RaceMath.PaceQ(r, 8.0, SectionType.Fast, 10.0), 5e-4, c.Item1 + " 短");
                Assert.AreEqual(c.Item7, DayPlanner.Gauge(r, 8.0, SectionType.Fast, 10.0).mark);
            }
            // 期待時間が等しくなる q(指示の切り替え点)
            double? qa = null, qh = null;
            var env = new Env { Temp = 10.0, Wind = 0.0, Rain = false };
            for (int i = 0; i < 4000; i++)
            {
                double sta = 40 + i * 0.02;
                var r = Fixed(new[] { 70, 0, 70, 70, 70, 70, 70, 70, 70, 70 });
                r.Stats[1] = (int)Math.Round(sta);          // 参照モデルは小数の能力値を使う。ここでは整数の能力で切り替え点の近さだけを見る
                double q = RaceMath.PaceQ(r, 21.3, SectionType.Long, 10.0);
                if (!(0.05 < q && q < 0.55)) continue;
                double eAtk = DayPlanner.LegEv(r, 0, RaceKind.NewYear, env, Instruction.Attack), eKeep = DayPlanner.LegEv(r, 0, RaceKind.NewYear, env, Instruction.Keep), eHold = DayPlanner.LegEv(r, 0, RaceKind.NewYear, env, Instruction.Hold);
                if (qh == null && eHold >= eKeep) qh = q;
                if (qa == null && eAtk < eKeep) qa = q;
            }
            Assert.IsNotNull(qa); Assert.IsNotNull(qh);
            Assert.AreEqual(0.167, qa.Value, 0.03); Assert.AreEqual(0.262, qh.Value, 0.03);
        }

        [Test]
        public void TV11_DaySwap()
        {
            string names = "ABCDEFGHIJ";
            var entry = new List<RaceRunner>();
            for (int i = 0; i < 10; i++) entry.Add(Flat(0, 0, names[i].ToString()));
            for (int i = 0; i < 10; i++) { double pw = 76.0 + 0.1 * i; for (int s = 0; s < 10; s++) entry[i].Stats[s] = 0; entry[i].Stats = Enumerable.Repeat((double)(int)pw, 10).ToArray(); }
            // 参照モデルは小数の能力(76.0 + 0.1 i)を使う。整数の能力では並びが変わるので、ここでは能力を整数にして期待を作り直す
            entry[2] = Flat(80, -1, "C");
            entry[9] = Flat(75, 2, "J");
            var envs = new[] { new Env { Temp = 18.0, Wind = 0.0, Rain = false } };
            var (lu, ins, sw) = DayPlanner.PlanDay(entry, RaceKind.Fall, envs, PlanMode.Auto);
            Assert.AreEqual(1, sw);
            Assert.AreEqual("J", lu.Last().Name, "不調の先発(C)を絶好調の控え(J)に置き換える");
            var (lu2, ins2, sw2) = DayPlanner.PlanDay(entry, RaceKind.Fall, envs, PlanMode.Skilled);
            Assert.AreEqual(1, sw2);
            Assert.AreEqual("J", lu2.Last().Name);
        }

        [Test]
        public void Individuality_TV5_RaceAdjustments()
        {
            // 走法: 集団の追走側 / 単独走(10 §6)
            double[] pack = { -0.45, -0.30, -0.15 }, solo = { 0.30, 0.20, 0.10 };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(pack[i], RaceConstants.PackAdj * RaceConstants.K_RUN_SCALE[i] * 100, 1e-9);
                Assert.AreEqual(solo[i], RaceConstants.SoloAdj * RaceConstants.K_RUN_SCALE[i] * 100, 1e-9);
            }
            // 展開: 追う展開(追い上げ型 −0.2% / 普通 0 / 逃げ型 +0.2%)
            Assert.AreEqual(-0.2, RaceConstants.K_STYLE_CHASE_ADJ[(int)StyleFlow.Chase] * 100, 1e-9);
            Assert.AreEqual(0.0, RaceConstants.K_STYLE_CHASE_ADJ[(int)StyleFlow.Even], 1e-12);
            Assert.AreEqual(0.2, RaceConstants.K_STYLE_CHASE_ADJ[(int)StyleFlow.Lead] * 100, 1e-9);
            // 乱数の消費は変わらない(8 回)
            var r = Fixed(StatsTv1); r.Style = new Style(StyleForm.Long, StyleRun.Solo, StyleFlow.Lead); r.Leading = true;
            var rn = new KeyedRngFactory(1UL).Stream(4, 1, 1);
            RaceMath.DrawLeg(rn, r, 1.0, Instruction.Keep, 1.0, 21.3, 10.0, SectionType.Long);
            Assert.AreEqual(8, rn.Draws);
        }
    }
}
