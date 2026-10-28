using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>『惜しかった候補』(08a SC-14。08b G-05): 自校が狙ったのに入らなかった候補と、その理由(定員・奨学金枠・競り負け)。</summary>
    public class MissedCandidateTests
    {
        private static string Names(Dictionary<int, List<WorldRunner>> intake, int school)
        {
            return string.Join(",", intake[school].Select(r => r.Index));
        }

        [Test]
        public void Allocation_IsUnchangedByRecordingMisses_AndEveryTargetIsEitherEnrolledOrMissedOnce_WithTheRightReason()
        {
            var seen = new HashSet<MissReason>();
            for (ulong seed = 1; seed <= 12; seed++)
            foreach (int rosterCount in new[] { 25, 37 })          // 37 人 = 空き 3 枠(奨学金枠 3 と同時に尽きる場合 → 定員を先に数える)
            {
                var rngs = new KeyedRngFactory(seed);
                WorldSim w = WorldSim.Create(rngs, 3);
                School player = w.ClaimPlayerSchool(Difficulty.Mid);
                List<WorldRunner> pool = PoolGenerator.Generate(rngs, 1, WorldK.K_POOL_N, w.ElitePrefBonus);
                // 狙う候補: 評価の高い順に 120 人(上位級で奨学金枠が、そのあとの層で定員が尽きる)
                var targets = new HashSet<int>(pool.OrderByDescending(r => r.V).ThenBy(r => r.Index).Take(120).Select(r => r.Index));
                PlayerOffer offer = r => targets.Contains(r.Index) ? WorldK.InterestMax * (0.25 + 0.75 * (r.Index % 4) / 3.0) : 0.0;          // 熱意に差をつける(競り負けも出るように)
                Func<WorldRunner, bool> isTarget = r => targets.Contains(r.Index);
                // 抽象の名簿は満員に近いので、実体の自校のように在籍数(rosterCount)を渡す
                var plain = Allocator.Allocate(w.Schools, pool, rngs, 1, player, offer, null, rosterCount);
                var misses = new List<AllocationMiss>();
                var withMisses = Allocator.Allocate(w.Schools, pool, rngs, 1, player, offer, null, rosterCount, isTarget, misses);
                foreach (School s in w.Schools) Assert.AreEqual(Names(plain, s.Id), Names(withMisses, s.Id), "記録しても割り当ては変わらない");
                // 入った / 入らなかった の分かれ方
                var enrolled = new HashSet<int>(withMisses[player.Id].Select(r => r.Index));
                var missed = misses.Select(m => m.Candidate.Index).ToList();
                CollectionAssert.AreEquivalent(targets.Where(t => !enrolled.Contains(t)).ToList(), missed, "狙って入らなかった候補が、ちょうど 1 回ずつ");
                Assert.AreEqual(missed.Count, missed.Distinct().Count());
                // 理由を、最終の結果から数え直す: 候補を V の高い順に見て、そのときの自校の空きを数える
                var order = pool.OrderByDescending(r => r.V).ToList();             // 割り当ての処理順(V の降順。同じ V は元の順)
                int slots0 = Math.Max(0, Math.Min(player.Quota(), WorldK.RosterMax - rosterCount));
                int schol0 = player.ScholSlots();
                int takenSlots = 0, takenSchol = 0;
                foreach (WorldRunner r in order)
                {
                    bool top = r.V >= WorldK.K_V_TOP;
                    AllocationMiss m = misses.SingleOrDefault(x => x.Candidate.Index == r.Index);
                    if (m != null)
                    {
                        MissReason expected = slots0 - takenSlots <= 0 ? MissReason.Quota : (top && schol0 - takenSchol <= 0 ? MissReason.Scholarship : MissReason.Outbid);
                        Assert.AreEqual(expected, m.Reason, "候補 " + r.Index);
                        seen.Add(m.Reason);
                        if (m.Reason == MissReason.Outbid)
                        {
                            Assert.GreaterOrEqual(m.Margin, 0.0);
                            Assert.AreNotEqual(player.Id, m.WinnerSchoolId);
                            Assert.IsTrue(withMisses[m.WinnerSchoolId].Any(x => x.Index == r.Index), "競り勝った学校に入っている");
                        }
                        if (m.WinnerSchoolId >= 0) Assert.IsTrue(withMisses[m.WinnerSchoolId].Any(x => x.Index == r.Index));
                    }
                    if (enrolled.Contains(r.Index)) { takenSlots++; if (top) takenSchol++; }
                }
            }
            Assert.IsTrue(seen.Contains(MissReason.Outbid) && seen.Contains(MissReason.Quota) && seen.Contains(MissReason.Scholarship), "試験の前提: 3 つの理由がすべて現れる(" + string.Join(",", seen) + ")");
        }

        [Test]
        public void Plumbing_MissesReachTheEnrollmentView_InOrder()
        {
            GameSession gs = GameSession.New(5UL, Difficulty.Mid);
            gs.PlayYears(1);
            Assert.IsNotNull(gs.Enrollment());
            List<WorldRunner> c = gs.World.Schools.First(s => !s.IsPlayer).Roster.Take(5).ToList();
            ((IPlayerSide)gs).Missed(new List<AllocationMiss>
            {
                new AllocationMiss { Candidate = c[0], Reason = MissReason.Quota, WinnerSchoolId = -1 },
                new AllocationMiss { Candidate = c[1], Reason = MissReason.Outbid, WinnerSchoolId = 7, Margin = 4.5 },
                new AllocationMiss { Candidate = c[2], Reason = MissReason.Scholarship, WinnerSchoolId = 3 },
                new AllocationMiss { Candidate = c[3], Reason = MissReason.Outbid, WinnerSchoolId = 9, Margin = 0.5 },
            });
            EnrollmentView e = gs.Enrollment();
            CollectionAssert.AreEqual(new[] { c[3].Index, c[1].Index, c[2].Index, c[0].Index }, e.Missed.Select(m => m.Candidate.Index).ToArray(), "競り負け(差の小さい順)→ 奨学金枠 → 定員");
            CollectionAssert.AreEqual(new[] { "miss.outbid", "miss.outbid", "miss.scholarship", "miss.quota" }, e.Missed.Select(m => m.ReasonKey).ToArray());
            CollectionAssert.AreEqual(new[] { 0.5, 4.5, 0.0, 0.0 }, e.Missed.Select(m => m.Margin).ToArray());
            CollectionAssert.AreEqual(new[] { "school.9.name", "school.7.name", "school.3.name", "" }, e.Missed.Select(m => m.WinnerNameKey).ToArray());
            Assert.IsTrue(e.Missed.All(m => m.Candidate.Stage == ScoutStage.Invited));
            ((IPlayerSide)gs).Missed(new List<AllocationMiss>());
            Assert.IsEmpty(gs.Enrollment().Missed, "次の割り当てで置き換わる");
        }

        [Test]
        public void NoTargets_NoMisses()
        {
            var rngs = new KeyedRngFactory(3UL);
            WorldSim w = WorldSim.Create(rngs, 3);
            School player = w.ClaimPlayerSchool(Difficulty.Low);
            List<WorldRunner> pool = PoolGenerator.Generate(rngs, 1, WorldK.K_POOL_N, w.ElitePrefBonus);
            var misses = new List<AllocationMiss>();
            Allocator.Allocate(w.Schools, pool, rngs, 1, player, r => 0.0, null, null, r => false, misses);
            Assert.IsEmpty(misses);
            Allocator.Allocate(w.Schools, pool, rngs, 1, null, null, null, null, r => true, misses);
            Assert.IsEmpty(misses, "自校がなければ記録しない");
        }

        [Test]
        public void EnrollmentView_ListsTheMissedCandidates_WithReasonsAndSorted()
        {
            int seenMissed = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                GameSession gs = GameSession.New(seed, seed % 2 == 0 ? Difficulty.Elite : Difficulty.Mid);
                gs.PlayYears(2);
                EnrollmentView e = gs.Enrollment();
                Assert.IsNotNull(e);
                var enrolledNames = new HashSet<string>(e.Targeted.Concat(e.Natural).Select(x => x.Row.FamilyName + x.Row.GivenName));
                int rankOf(MissedCandidateView m) { return m.Reason == MissReason.Outbid ? 0 : (m.Reason == MissReason.Scholarship ? 1 : 2); }
                for (int i = 0; i < e.Missed.Count; i++)
                {
                    MissedCandidateView m = e.Missed[i];
                    Assert.AreEqual(ScoutStage.Invited, m.Candidate.Stage);
                    Assert.IsFalse(m.Candidate.CanObserve || m.Candidate.CanTalk || m.Candidate.CanInvite);
                    Assert.AreEqual("miss." + m.Reason.ToString().ToLowerInvariant(), m.ReasonKey);
                    Assert.That(m.Candidate.Stars, Is.InRange(1, 5));
                    Assert.IsNotEmpty(m.Candidate.FamilyName);
                    if (m.WinnerSchoolId >= 0) Assert.AreEqual("school." + m.WinnerSchoolId + ".name", m.WinnerNameKey); else Assert.AreEqual("", m.WinnerNameKey);
                    if (m.Reason != MissReason.Outbid) Assert.AreEqual(0.0, m.Margin);
                    if (i > 0)
                    {
                        MissedCandidateView p = e.Missed[i - 1];
                        Assert.LessOrEqual(rankOf(p), rankOf(m), "競り負け → 奨学金枠 → 定員");
                        if (rankOf(p) == rankOf(m) && m.Reason == MissReason.Outbid) Assert.LessOrEqual(p.Margin, m.Margin, "競り負けは差の小さい順");
                    }
                }
                seenMissed += e.Missed.Count;
            }
            Assert.Greater(seenMissed, 0, "試験の前提: 惜しかった候補が出る");
        }
    }
}
