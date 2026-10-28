using System;
using System.Collections.Generic;
using Ekiden.Domain.Rng;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>03 TV-1 / 09 TV-7 の適合ベクタ(Python の参照とビット一致)と、登録表の検査(§8.2)。</summary>
    public class RngTests
    {
        [Test]
        public void Derive_ConformanceVectors()
        {
            Assert.AreEqual(10206515190289864039UL, Mix.Derive(123456789UL, 1, 7, 100));
            Assert.AreEqual(0x8DA4D37D37DB1567UL, Mix.Derive(123456789UL, 1, 7, 100));
            Assert.AreEqual(12557608959185654001UL, Mix.Derive(123456789UL, 2, 7, 100));
        }

        [Test]
        public void Pcg32_U32_First3()
        {
            var r = new Pcg32(Mix.Derive(123456789UL, 1, 7, 100));
            Assert.AreEqual(2889221095u, r.U32());
            Assert.AreEqual(1724165226u, r.U32());
            Assert.AreEqual(319155939u, r.U32());
            Assert.AreEqual(3, r.Draws);
        }

        [Test]
        public void Uniform_And_Normal()
        {
            var r = new Pcg32(Mix.Derive(123456789UL, 1, 7, 100));
            Assert.AreEqual(0.672699207, r.Uniform(), 1e-9);
            Assert.AreEqual(0.401438499, r.Uniform(), 1e-9);
            var n = new Pcg32(Mix.Derive(123456789UL, 1, 7, 100));
            Assert.AreEqual(-0.725096155, n.Normal(0, 1), 1e-9);
            Assert.AreEqual(2, n.Draws, "Normal は常に 2 回消費する");
        }

        [Test]
        public void Factory_UsesDerive()
        {
            var f = new KeyedRngFactory(123456789UL);
            Assert.AreEqual(2889221095u, f.Stream(1, 7, 100).U32());
        }

        [Test]
        public void NegativeKeysAreTwosComplement()
        {
            // 予熱の年度(a ≤ 0)も同じ鍵の形(09 §8.1)。Python の `a & MASK64` と同じ
            Assert.AreEqual(Mix.Derive(1UL, 6, unchecked((long)0xFFFFFFFFFFFFFFF1UL), 0), Mix.Derive(1UL, 6, -15, 0));
        }

        [Test]
        public void Registry_Has26Ranges_NoOverlap()
        {
            Assert.AreEqual(26, RngRegistry.Ranges.Count);
            CollectionAssert.IsEmpty(RngRegistry.FindOverlaps());
        }

        [Test]
        public void KeyUniverse_NoCollisions()
        {
            // 09 §8.2 の TV-7 と同じ形の鍵の集合(予熱 15 年度 + 3 年度)。乱数で作る look_seed の分だけ、参照モデルと数が違う
            const ulong master = 20260101UL;
            var keys = new HashSet<(int, long, long)>();
            void Add(int l, long a, long b) { keys.Add((l, a, b)); }
            void Year(long y, bool player)
            {
                for (int b = 0; b <= 1; b++) Add(3, y, b);
                for (int b = 100; b < 136; b++) Add(3, y, b);
                for (int b = 1000; b < 1520; b++) Add(3, y, b);
                for (int idx = 0; idx < 520; idx++) for (int sc = 0; sc < 36; sc++) Add(3, y, 10000 + idx * 64 + sc);
                for (int kind = 1; kind <= 4; kind++)
                {
                    long rid = y * 10 + kind;
                    for (int tid = 0; tid < 36; tid++)
                    {
                        for (int si = 0; si < 10; si++) Add(4, rid, tid * 100 + si);
                        Add(4, rid, 8000 + tid); Add(4, rid, 9000 + tid);
                    }
                    Add(4, rid, 9998); Add(4, rid, 9999);
                }
                Add(6, y, 0);
                for (int s = 0; s < 36; s++) { Add(6, y, 100 + s); Add(6, y, 1000 + s); }
                if (player)
                {
                    for (int b = 9000; b < 9004; b++) Add(3, y, b);
                    for (long rid = 1; rid <= 200; rid++)
                        for (long wk = 48 * (y - 1); wk < 48 * y; wk++) { Add(1, rid, wk); Add(2, rid, wk); }
                    for (long wk = 48 * (y - 1); wk < 48 * y; wk++) { Add(5, wk, 0); for (int k = 0; k < 4; k++) Add(5, wk, 1000 + k); }
                }
            }
            for (long y = -14; y <= 0; y++) Year(y, false);
            for (long y = 1; y <= 3; y++) Year(y, true);
            for (int st = 0; st < 3; st++) for (int b = 0; b < 300; b++) Add(7, st, b);
            var rnd = new Random(1);
            for (int i = 0; i < 2000; i++)
            {
                long ls = rnd.Next();
                for (int b = 0; b < 9; b++) Add(8, ls, b);
                for (int b = 10; b < 42; b++) Add(8, ls, b);
            }
            var outs = new HashSet<ulong>();
            foreach (var k in keys) outs.Add(Mix.Derive(master, k.Item1, k.Item2, k.Item3));
            Assert.AreEqual(keys.Count, outs.Count, "導出値の衝突");
            Assert.Greater(keys.Count, 500000);
        }
    }
}
