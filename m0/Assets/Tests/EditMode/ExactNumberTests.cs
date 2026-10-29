using System;
using System.Collections.Generic;
using Ekiden.Domain.Numerics;
using Ekiden.Persistence.Json;
using NUnit.Framework;

namespace Ekiden.Unity.Tests
{
    /// <summary>
    /// L-71: Unity(Mono・IL2CPP)の <c>double.Parse</c> は、まれに最近接に丸めず 1 ulp ずれる(<c>Q6</c> の値で約 0.05%)。
    /// 保存の JSON の数の読み取りは <see cref="ExactDouble"/> を通すので、書いて読み戻すとビット一致する。Unity の中で走らせる(.NET の試験は <c>tests/Persistence.Tests/JsonTests.cs</c>)。
    /// 直す前は 20 万個のうち約 100 個がずれた。
    /// </summary>
    public class ExactNumberTests
    {
        private static ulong Next(ref ulong s) { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }

        [Test]
        public void Q6Numbers_AreReadBackBitExactly_ThroughTheJsonReader()
        {
            ulong s = 88172645463325252UL;
            var vals = new List<double>();
            for (int i = 0; i < 200000; i++) vals.Add(Q6.Round(((Next(ref s) >> 11) / 9007199254740992.0 - 0.5) * 2000.0));
            vals.Add(18.232545);                                    // 夜間の作業で Unity だけ …d92c になった値
            var arr = new JsonArray();
            foreach (double d in vals) arr.Add(new JsonNumber(d));
            var back = (JsonArray)JsonValue.Parse(arr.ToJson());
            Assert.AreEqual(vals.Count, back.Count);
            int bad = 0;
            string first = null;
            for (int i = 0; i < vals.Count; i++)
            {
                double want = vals[i] == 0 ? 0.0 : vals[i];
                double got = ((JsonNumber)back[i]).Value;
                if (BitConverter.DoubleToInt64Bits(want) != BitConverter.DoubleToInt64Bits(got))
                {
                    bad++;
                    if (first == null) first = want.ToString("R") + " → " + got.ToString("R");
                }
            }
            Assert.AreEqual(0, bad, "読み戻して値が変わった " + bad + " 件(例: " + first + ")");
        }

        [Test]
        public void TheValueThatBrokeUnity_IsParsedExactly()
        {
            Assert.AreEqual(0x40323b8811b1d92bL, BitConverter.DoubleToInt64Bits(ExactDouble.Parse("18.232545")));
        }

        [Test]
        public void ConditionConstants_AreParsedExactly()
        {
            // 条件式の数(06 §3)も同じ読み取りを通る。期待値は『整数 ÷ 10 のべき』の 1 回の除算(IEEE で最近接。double.Parse に頼らない)
            var cases = new[] { ("0.15", 15.0, 100.0), ("0.3", 3.0, 10.0), ("2.675", 2675.0, 1000.0), ("18.232545", 18232545.0, 1000000.0), ("60", 60.0, 1.0), ("0.05", 5.0, 100.0) };
            foreach (var c in cases)
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(c.Item2 / c.Item3), BitConverter.DoubleToInt64Bits(ExactDouble.Parse(c.Item1)), c.Item1);
        }
    }
}
