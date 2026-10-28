using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Persistence.Sync;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>同期の判定の表(09 §9.2・TV-8)・マージの性質・乱数シミュレーション(TV-9)。</summary>
    public class SyncTests
    {
        private static SyncRecord R(int lineage, long counter, bool tomb = false, long lastSynced = 0)
        {
            return new SyncRecord { LineageId = lineage.ToString(), Counter = counter, IsTombstone = tomb, LastSynced = lastSynced };
        }

        [Test]
        public void TV8_DecisionTable_15Cases()
        {
            var rows = new (string, SyncRecord, SyncRecord, SyncAction)[]
            {
                ("端末なし・クラウドなし", null, null, SyncAction.Noop),
                ("端末なし・クラウドあり(初めての端末)", null, R(1, 5), SyncAction.Download),
                ("端末あり・クラウドなし(初めての同期)", R(1, 3, false, 0), null, SyncAction.Upload),
                ("同じ系譜・端末だけ進んだ(5 → 7)", R(1, 7, false, 5), R(1, 5), SyncAction.Upload),
                ("同じ系譜・クラウドだけ進んだ(5 → 8)", R(1, 5, false, 5), R(1, 8), SyncAction.Download),
                ("同じ系譜・どちらも変わらない", R(1, 5, false, 5), R(1, 5), SyncAction.Noop),
                ("同じ系譜・どちらも進んだ(番号が同じでも競合)", R(1, 6, false, 5), R(1, 6), SyncAction.Conflict),
                ("同じ系譜・端末が削除(墓石)、クラウドは変わらず", R(1, 6, true, 5), R(1, 5), SyncAction.Upload),
                ("同じ系譜・端末が削除、クラウドは進んだ", R(1, 6, true, 5), R(1, 7), SyncAction.Conflict),
                ("同じ系譜・クラウドが墓石、端末は変わらず", R(1, 5, false, 5), R(1, 6, true), SyncAction.Download),
                ("同じ系譜・クラウドが墓石、端末は進んだ", R(1, 7, false, 5), R(1, 6, true), SyncAction.Conflict),
                ("別の系譜・どちらも実体", R(1, 5, false, 5), R(2, 3), SyncAction.Conflict),
                ("別の系譜・クラウドが墓石(新しいゲームが勝つ)", R(2, 1, false, 0), R(1, 6, true), SyncAction.Upload),
                ("別の系譜・端末が墓石(クラウドの実体が勝つ)", R(1, 6, true, 5), R(2, 3), SyncAction.Download),
                ("端末なし・クラウドが墓石", null, R(1, 6, true), SyncAction.Noop),
            };
            foreach (var r in rows) Assert.AreEqual(r.Item4, SyncDecider.Decide(r.Item2, r.Item3), r.Item1);
        }

        [Test]
        public void ProfileMerge_Properties()
        {
            var rnd = new SimRng(11);
            Func<ProfileData> make = () =>
            {
                var p = new ProfileData { SchemaVersion = rnd.NextInt(2) + 1 };
                for (int i = 0; i < rnd.NextInt(8); i++) p.Achievements["ach." + rnd.NextInt(12)] = 1 + rnd.NextInt(100);
                for (int i = 0; i < rnd.NextInt(4); i++) p.Counters["c." + rnd.NextInt(6)] = rnd.NextInt(50);
                for (int i = 0; i < rnd.NextInt(3); i++)
                {
                    long date = 1 + rnd.NextInt(8);
                    string diff = new[] { null, "Mid", "Low" }[rnd.NextInt(3)];
                    string sc = "sc." + rnd.NextInt(4);
                    if (!p.Clears.Any(c => c.ScenarioId == sc && c.Difficulty == diff && c.Date == date))
                        p.Clears.Add(new ClearRecord { ScenarioId = sc, Difficulty = diff, Date = date, ClearYearIndex = date * 3, NoInvite = date % 2 == 0 });
                }
                return p;
            };
            for (int k = 0; k < 2000; k++)
            {
                ProfileData x = make(), y = make(), z = make();
                string ab = ProfileData.Merge(x, y).ToJson().ToJson(), ba = ProfileData.Merge(y, x).ToJson().ToJson();
                Assert.AreEqual(ab, ba, "可換");
                Assert.AreEqual(ProfileData.Merge(ProfileData.Merge(x, y), z).ToJson().ToJson(), ProfileData.Merge(x, ProfileData.Merge(y, z)).ToJson().ToJson(), "結合的");
                ProfileData m = ProfileData.Merge(x, y);
                Assert.AreEqual(m.ToJson().ToJson(), ProfileData.Merge(m, m).ToJson().ToJson(), "冪等");
                foreach (var kv in x.Achievements) Assert.IsTrue(m.Achievements.ContainsKey(kv.Key) && m.Achievements[kv.Key] <= kv.Value, "実績が消えない・解除は早いほう");
            }
        }

        [Test]
        public void TV9_Simulation_WithConditionalWrites_NeverLosesUpdates()
        {
            int lostRuns = 0, nonconv = 0, conflicts = 0, casFail = 0;
            for (int k = 0; k < 150; k++)
            {
                SyncWorld.Outcome r = SyncWorld.Simulate(2 + k % 2, 300, (ulong)(1000 + k), cas: true);
                if (r.LostDuring.HasValue || r.LostEnd > 0) lostRuns++;
                if (!r.Converged) nonconv++;
                conflicts += r.Stat.Conflict;
                casFail += r.Stat.CasFail;
            }
            Assert.AreEqual(0, lostRuns, "前提つき書き込みでは、更新が失われない");
            Assert.AreEqual(0, nonconv, "収束する");
            Assert.Greater(conflicts, 500, "競合は十分に起きている");
            Assert.Greater(casFail, 100, "前提の失敗も起きている");
        }

        [Test]
        public void TV9_Simulation_WithoutConditionalWrites_LosesUpdates()
        {
            int lostRuns = 0;
            for (int k = 0; k < 300; k++)
            {
                SyncWorld.Outcome r = SyncWorld.Simulate(2 + k % 2, 300, (ulong)(1000 + k), cas: false);
                if (r.LostDuring.HasValue || r.LostEnd > 0) lostRuns++;
            }
            Assert.Greater(lostRuns, 0, "前提なし(最後の書き込みが勝つ)では更新が失われる = 前提つき書き込みは必須");
        }

        [TestCase(0.15, 0.10)]
        [TestCase(0.05, 0.03)]
        [TestCase(0.02, 0.01)]
        public void TV9_NormalUse_LosesNothing(double pOff, double pFail)
        {
            int sessions, conflicts, lost;
            SyncWorld.Usage(60, 120, 5000, pOff, pFail, out sessions, out conflicts, out lost);
            Assert.AreEqual(0, lost, "失われた更新");
            Assert.Less(conflicts / (double)sessions, 0.15, "競合は同期できなかったときだけ。割合は小さい");
        }

        [Test]
        public void TV9_AlwaysSyncing_NeverConflicts()
        {
            int sessions, conflicts, lost;
            SyncWorld.Usage(60, 120, 5000, 0.0, 0.0, out sessions, out conflicts, out lost);
            Assert.AreEqual(0, conflicts, "いつも同期できれば、競合は 0 回");
            Assert.AreEqual(0, lost);
        }
    }
}
