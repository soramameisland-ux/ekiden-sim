using Ekiden.Persistence.Store;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    public class LocalStateTests
    {
        [Test]
        public void RoundTrip_Json()
        {
            var s = new LocalState { ClockHighWater = 1800000000, RunTotalSeconds = 36000, InstallId = "abc" };
            s.Entitlements["pack.golden_plan"] = new EntitlementCache { Owned = true, VerifiedAt = 1799990000, VerifiedRun = 3600 };
            s.Sync["slot1"] = new LocalState.SyncPoint { LineageId = "L1", Counter = 7, Deleted = false };
            s.Sync["slot2"] = new LocalState.SyncPoint { LineageId = "L2", Counter = 3, Deleted = true };
            s.PendingUnlocks.Add(new LocalState.PendingUnlock { AchievementId = "ach.clear", QueuedAt = 1799999999 });
            string json = s.ToJson().ToJson();
            LocalState t = LocalState.FromJsonOrDefault(json);
            Assert.AreEqual(json, t.ToJson().ToJson());
            Assert.AreEqual(7, t.LastSyncedCounter("slot1"));
            Assert.AreEqual(0, t.LastSyncedCounter("none"));
            Assert.IsTrue(t.Sync["slot2"].Deleted);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[1,2]")]
        [TestCase("{\"entitlements\":5,\"sync\":[1],\"pending_unlocks\":{}}")]
        public void BrokenFile_StartsFromDefaults(string text)
        {
            LocalState s = LocalState.FromJsonOrDefault(text);
            Assert.AreEqual(0, s.ClockHighWater);
            Assert.AreEqual(0, s.Entitlements.Count);
            Assert.AreEqual(0, s.Sync.Count);
        }
    }
}
