using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.App;
using Ekiden.Domain.Achievements;
using Ekiden.Domain.Game;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using Ekiden.Persistence.Sync;
using Ekiden.Platform;
using NUnit.Framework;

namespace Ekiden.App.Tests
{
    /// <summary>実績の解除の記録(02 §3.10・09 §11。08b G-08): <see cref="AchievementTracker"/> と、<see cref="GameHost.Advance"/> からの呼び出し。</summary>
    public class AchievementHostTests
    {
        private static HostEnvironment Env(MemoryFileStore fs, FakeClock clock, ProfileData profile = null)
        {
            return new HostEnvironment { Files = fs, Clock = clock, AppVersion = "1.0.0", MasterHash = "a1b2c3d4", NewLineageId = () => "3f2c9a6e-0000-4000-8000-000000000001", Profile = profile };
        }

        [Test]
        public void Tracker_RecordsNewUnlocksOnce_QueuesThemForTheStore_AndKeepsEarlierTimes()
        {
            GameHost h = GameHost.NewGame(Env(new MemoryFileStore(), new FakeClock()), 1, 51UL, Difficulty.Low);
            GameSession gs = h.Session;
            var profile = new ProfileData();
            profile.Achievements["ach.clear"] = 1000;                                    // 前のセーブで解除ずみ(早い日時)
            var tracker = new AchievementTracker(profile);
            var events = new List<string>();
            tracker.Unlocked += d => events.Add(d.Id);
            Assert.IsEmpty(tracker.Update(gs, null, 2000), "始めたばかりでは何も解除しない");
            gs.State.Cleared = new ClearInfo { Year = 3, NoInvite = true };
            gs.State.Obs.AddRange(Enumerable.Range(0, 10).Select(i => new OBRecord { RunnerId = 900 + i, Pref = 1 + i, HallOfFame = true }));
            List<AchievementDef> added = tracker.Update(gs, null, 3000);
            CollectionAssert.AreEqual(new[] { "ach.clear.low", "ach.clear.low.no_invite", "ach.dev.hall" }, added.Select(d => d.Id).ToArray(), "ach.clear は前のセーブで解除ずみ");
            CollectionAssert.AreEqual(added.Select(d => d.Id).ToArray(), events.ToArray());
            Assert.AreEqual(1000, profile.Achievements["ach.clear"], "解除の日時は早いほうのまま");
            Assert.AreEqual(3000, profile.Achievements["ach.dev.hall"]);
            CollectionAssert.AreEqual(added.Select(d => d.Id).ToArray(), tracker.Pending.Select(p => p.AchievementId).ToArray(), "ストアへの通知待ちは、新しく解除した分だけ");
            Assert.IsTrue(tracker.Pending.All(p => p.QueuedAt == 3000));
            Assert.IsEmpty(tracker.Update(gs, null, 4000), "もう一度見ても重ねない");
            Assert.AreEqual(3, tracker.Pending.Count);
            // クリアの記録は 1 件だけ(同じクリアは重ねない)
            ClearRecord c = profile.Clears.Single();
            Assert.AreEqual(gs.State.Scenario.Id, c.ScenarioId); Assert.AreEqual("Low", c.Difficulty); Assert.AreEqual(3, c.ClearYearIndex); Assert.IsTrue(c.NoInvite); Assert.AreEqual(3000, c.Date);
            Assert.IsTrue(tracker.IsUnlocked("ach.dev.hall")); Assert.IsFalse(tracker.IsUnlocked("ach.years.50"));
        }

        [Test]
        public void Host_RecordsWhatThePlayAchieves_AndEverythingSatisfiedEndsUpInTheProfile()
        {
            var clock = new FakeClock();
            var profile = new ProfileData();
            GameHost h = GameHost.NewGame(Env(new MemoryFileStore(), clock, profile), 1, 52UL, Difficulty.Elite);
            Assert.AreSame(profile, h.Achievements.Profile);
            var unlocked = new List<string>();
            h.Achievements.Unlocked += d => unlocked.Add(d.Id);
            int years = 0;
            for (int i = 0; i < 100000 && years < 8; i++)
            {
                clock.Advance(1);
                Stop s = h.Advance();
                if (s.Kind == StopKind.YearEndTurnover) years++;
            }
            Assert.AreEqual(8, years);
            Assert.Greater(profile.Achievements.Count, 0, "強豪で 8 年度遊べば、何かは解除する(初表彰台など)");
            // 年度末の停止で見た状態で満たしている実績は、すべてプロフィールにある
            foreach (string id in AchievementRules.Satisfied(AchievementFacts.From(h.Session))) Assert.IsTrue(profile.Achievements.ContainsKey(id), id);
            CollectionAssert.AreEquivalent(unlocked, profile.Achievements.Keys.ToList(), "解除の通知と記録が一致");
            Assert.AreEqual(profile.Achievements.Count, h.Achievements.Pending.Count);
            Assert.IsTrue(profile.Achievements.Values.All(t => t >= 1800000000L), "日時は時計の値");
            Assert.IsTrue(profile.Achievements.Values.Distinct().Count() > 1, "解除の日時は、実績が満たされた時点(遊んだ時間の進み)で違う");
        }
    }
}
