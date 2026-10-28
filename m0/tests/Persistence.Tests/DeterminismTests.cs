using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>T3 決定性(09 §16.1): ① 同じ入力で 2 回 = バイト一致 ② 任意の年度で『保存 → 読み込み → 続行』= 『続行のみ』とバイト一致 ③ 並列に回しても世界ごとの結果は変わらない。</summary>
    public class DeterminismTests
    {
        private const ulong Seed = 20260101UL;
        private const int Years = 14;

        [Test]
        public void SameInput_TwiceIsByteIdentical()
        {
            foreach (Difficulty? d in new Difficulty?[] { null, Difficulty.Mid, Difficulty.Low })
            {
                string a = Determinism.Run(Seed, d, Years, reloadAt: null);
                string b = Determinism.Run(Seed, d, Years, reloadAt: null);
                Assert.AreEqual(a, b, "開始校 " + (d.HasValue ? d.Value.ToString() : "なし"));
            }
        }

        [Test]
        public void DifferentSeed_Differs()
        {
            Assert.AreNotEqual(Determinism.Run(Seed, Difficulty.Mid, 6), Determinism.Run(Seed + 1, Difficulty.Mid, 6));
        }

        [Test]
        public void SaveLoadContinue_EqualsContinueOnly_ForEveryYear_ViaJsonText()
        {
            string reference = Determinism.Run(Seed, Difficulty.Mid, Years, reloadAt: null);
            for (int y = 1; y < Years; y++)
            {
                string s = Determinism.Run(Seed, Difficulty.Mid, Years, reloadAt: new HashSet<int> { y }, via: Determinism.Via.JsonText);
                Assert.AreEqual(reference, s, "年度 " + y + " の終わりで保存 → 読み込み");
            }
        }

        [Test]
        public void SaveLoadContinue_ViaContainer_AndRepeatedReloads()
        {
            string reference = Determinism.Run(Seed, Difficulty.Elite, Years, reloadAt: null);
            Assert.AreEqual(reference, Determinism.Run(Seed, Difficulty.Elite, Years, reloadAt: new HashSet<int> { 3, 8 }, via: Determinism.Via.Container));
            Assert.AreEqual(reference, Determinism.Run(Seed, Difficulty.Elite, Years, reloadAt: new HashSet<int>(Enumerable.Range(1, Years)), via: Determinism.Via.JsonText), "毎年度 読み込み直す");
            string noPlayer = Determinism.Run(Seed, null, Years, reloadAt: null);
            Assert.AreEqual(noPlayer, Determinism.Run(Seed, null, Years, reloadAt: new HashSet<int> { 2, 9 }, via: Determinism.Via.Container), "自校のない世界");
        }

        [Test]
        public void CaptureRestoreCapture_IsIdempotent_AndQ6Closed()
        {
            var w = WorldSim.Create(new KeyedRngFactory(Seed), 15);
            PlayerAgent agent = w.StartPlayer(Difficulty.Low);
            for (int y = 1; y <= 10; y++)
            {
                w.PlayYear(y, agent);
                Assert.IsEmpty(WorldSnapshot.NotQ6Closed(w, agent), "年度 " + y + " の状態の double は Q6 で閉じている(09 D-10)");
            }
            JsonObject snap = WorldSnapshot.Capture(w, agent, 11);
            WorldSnapshot.Restored r = WorldSnapshot.Restore((JsonObject)JsonValue.Parse(snap.ToJson()));
            Assert.AreEqual(snap.ToJson(), WorldSnapshot.Capture(r.World, r.Agent, r.NextYear).ToJson());
            Assert.AreEqual(Seed, r.MasterSeed);
            Assert.AreEqual(11, r.NextYear);
            Assert.IsEmpty(WorldSnapshot.NotQ6Closed(r.World, r.Agent));
        }

        [Test]
        public void Parallel_GivesTheSamePerWorldResult_AsSequential()
        {
            ulong[] seeds = Enumerable.Range(1, 6).Select(i => (ulong)i * 1000003UL + 17UL).ToArray();
            string[] seq = seeds.Select(s => Determinism.Run(s, Difficulty.Mid, 8, reloadAt: null)).ToArray();
            var par = new string[seeds.Length];
            Parallel.For(0, seeds.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => par[i] = Determinism.Run(seeds[i], Difficulty.Mid, 8, reloadAt: null));
            CollectionAssert.AreEqual(seq, par);
        }

        [Test]
        public void Restore_RejectsForeignRngAlgorithm()
        {
            var w = WorldSim.Create(new KeyedRngFactory(Seed), 2);
            JsonObject snap = WorldSnapshot.Capture(w, null, 1);
            ((JsonObject)snap["rng"])["algorithm"] = new JsonString("mt19937");
            Assert.Throws<System.FormatException>(() => WorldSnapshot.Restore(snap));
        }
    }
}
