using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>スカウトの行動(08 R-08・05 §7)。視察 6〜11 月、声かけ・練習会 12〜2 月、1 回 1 ポイント、好感度 +30 / +60、確定は年度末。</summary>
    public class ScoutSessionTests
    {
        private const ulong Seed = 20260402UL;

        private static GameSession New() { return GameSession.New(Seed, Difficulty.Mid); }

        private static void Advance(GameSession gs, int weeks) { for (int i = 0; i < weeks; i++) gs.StepWeek(); }

        [Test]
        public void Observe_OnlyInJuneToNovember_CostsOnePoint()
        {
            GameSession gs = New();
            OpResult early = gs.ScoutObserve(0);
            Assert.IsFalse(early.Ok); Assert.AreEqual(OpErr.ScoutObserveSeason.Code, early.Code);
            Advance(gs, 8);                                                                          // 6 月の最初の週: 4 ポイント付与
            Assert.AreEqual(4, gs.ScoutPointsAvailable);
            Assert.IsTrue(gs.ScoutObserve(0).Ok);
            Assert.AreEqual(3, gs.ScoutPointsAvailable);
            Assert.AreEqual(ScoutStage.Observed, gs.StageOf(0));
            OpResult again = gs.ScoutObserve(0);
            Assert.IsFalse(again.Ok); Assert.AreEqual(OpErr.ScoutObservedAlready.Code, again.Code);
            Assert.IsFalse(gs.ScoutObserve(-1).Ok);
            Assert.IsFalse(gs.ScoutObserve(100000).Ok);
            Assert.AreEqual(3, gs.ScoutPointsAvailable, "拒否した操作はポイントを使わない");
        }

        [Test]
        public void Observe_Fails_WithoutPoints()
        {
            GameSession gs = New();
            Advance(gs, 8);
            for (int i = 0; i < 4; i++) Assert.IsTrue(gs.ScoutObserve(i).Ok);
            OpResult o = gs.ScoutObserve(4);
            Assert.IsFalse(o.Ok); Assert.AreEqual(OpErr.ScoutPoints.Code, o.Code);
            Assert.AreEqual(ScoutStage.None, gs.StageOf(4));
        }

        [Test]
        public void TalkAndInvite_Order_AndWindow()
        {
            GameSession gs = New();
            Advance(gs, 8);
            gs.ScoutObserve(1);
            gs.ScoutObserve(2);
            Advance(gs, 4 * 3);                                                                      // 9 月
            OpResult early = gs.ScoutTalk(1);
            Assert.IsFalse(early.Ok); Assert.AreEqual(OpErr.ScoutTalkSeason.Code, early.Code);
            Advance(gs, 4 * 3);                                                                      // 12 月(month_index 8)
            OpResult noObs = gs.ScoutTalk(5);
            Assert.IsFalse(noObs.Ok); Assert.AreEqual(OpErr.ScoutNeedObserve.Code, noObs.Code);
            OpResult noTalk = gs.ScoutInvite(1);
            Assert.IsFalse(noTalk.Ok); Assert.AreEqual(OpErr.ScoutNeedTalk.Code, noTalk.Code);
            Assert.IsTrue(gs.ScoutTalk(1).Ok);
            Assert.AreEqual(ScoutStage.Talked, gs.StageOf(1));
            Assert.IsFalse(gs.ScoutTalk(1).Ok, "声かけは 1 回");
            Assert.IsTrue(gs.ScoutInvite(1).Ok);
            Assert.AreEqual(ScoutStage.Invited, gs.StageOf(1));
            Assert.IsFalse(gs.ScoutInvite(1).Ok);
            int before = gs.ScoutPointsAvailable;
            Assert.IsTrue(gs.ScoutTalk(2).Ok);
            Assert.AreEqual(before - 1, gs.ScoutPointsAvailable);
        }

        [Test]
        public void ViewCandidate_RevealsInformationByStage()
        {
            GameSession gs = New();
            Advance(gs, 8);
            ScoutView v0 = gs.ViewCandidate(3);
            Assert.IsNull(v0.QObs);
            Assert.IsFalse(v0.StyleVisible || v0.GrowthTypeVisible);
            Assert.AreEqual(gs.ScoutPool[3].V, v0.V);
            Assert.AreEqual(v0.V >= WorldK.K_V_TOP, v0.TopTier);
            gs.ScoutObserve(3);
            ScoutView v1 = gs.ViewCandidate(3);
            Assert.IsNotNull(v1.QObs);
            Assert.AreEqual(Scouting.InfoSd(gs.School.ScoutLv), v1.Sigma);
            Assert.IsTrue(v1.StyleVisible);
            Assert.IsFalse(v1.GrowthTypeVisible, "成長タイプは練習会のあと");
            Assert.AreEqual(Scouting.ObserveQ(gs.ScoutPool[3], gs.School.ScoutLv, gs.Rngs), v1.QObs.Value, "同じ候補は同じ誤差で見える");
            Assert.IsNull(gs.ViewCandidate(-1));
        }

        [Test]
        public void ManualInvitations_DecideTheIntake()
        {
            GameSession gs = New();
            gs.State.Settings.ScoutAuto = false;
            Advance(gs, 8);
            // 競合のない(V < 51.5)候補から 5 人を選び、視察 → 声かけ → 練習会まで進める
            List<WorldRunner> pick = gs.ScoutPool.Where(c => c.V < WorldK.K_V_TOP && c.V > 45).OrderByDescending(c => c.V).Take(5).ToList();
            Assert.AreEqual(5, pick.Count);
            foreach (WorldRunner c in pick.Take(4)) Assert.IsTrue(gs.ScoutObserve(c.Index).Ok);       // 6 月のポイントは 4
            Advance(gs, 4);
            Assert.IsTrue(gs.ScoutObserve(pick[4].Index).Ok);
            Advance(gs, 20);                                                                         // 12 月
            // ポイント: 6〜12 月で 4 × 7 = 28 − 5 = 23 → 声かけ + 練習会 5 人 = 10
            foreach (WorldRunner c in pick) { OpResult t = gs.ScoutTalk(c.Index); Assert.IsTrue(t.Ok, t.ToString()); OpResult iv = gs.ScoutInvite(c.Index); Assert.IsTrue(iv.Ok, iv.ToString()); }
            var seeds = new HashSet<uint>(pick.Select(c => c.LookSeed));
            Advance(gs, 48 - 32 - 1);
            int idBefore = gs.State.NextRunnerId;
            gs.StepWeek();                                                                           // 年度末 → 入学
            var joined = gs.State.Runners.Where(r => r.Id >= idBefore).ToList();
            int invited = joined.Count(r => seeds.Contains(r.LookSeed));
            Assert.GreaterOrEqual(invited, 4, "競合のない候補に練習会まで進めれば、ほぼ入る(入部 " + invited + " / 5)");
            foreach (Runner r in joined.Where(r => seeds.Contains(r.LookSeed))) Assert.AreEqual(4, r.Motivation, "練習会に招待した候補はやる気 4 で入る");
            foreach (Runner r in joined.Where(r => !seeds.Contains(r.LookSeed))) Assert.AreEqual(3, r.Motivation);
            Assert.AreEqual(0, gs.ScoutStages.Count, "年度末に行動の記録は閉じる");
            Assert.AreEqual(0, gs.State.ScoutPts, "ポイントは年度内の繰り越しだけ");
        }

        [Test]
        public void NoActions_AndManual_MeansNoInvitations()
        {
            GameSession a = New(), b = New();
            a.State.Settings.ScoutAuto = false;
            Advance(a, 48); Advance(b, 48);
            int na = a.State.Runners.Count(r => r.Grade == 1 && r.Motivation == 4);
            int nb = b.State.Runners.Count(r => r.Grade == 1 && r.Motivation == 4);
            Assert.AreEqual(0, na, "手動で何もしなければ、練習会に招いた候補はいない");
            Assert.Greater(nb, 0, "おまかせ(scout_auto)は練習会まで進めた候補がいる");
        }

        [Test]
        public void AutoScout_ObservesThenRecruits_WithinThePoints()
        {
            GameSession gs = New();
            gs.State.Settings.ScoutAuto = false;
            Advance(gs, 8);
            Assert.AreEqual(0, gs.AutoScoutObserve(), "4 ポイントでは、勧誘のぶん(16)を残すと視察できない");
            Advance(gs, 20);                                                                         // 11 月(month_index 7)
            int pts = gs.ScoutPointsAvailable;
            Assert.AreEqual(24, pts);
            int obs = gs.AutoScoutObserve();
            Assert.AreEqual(8, obs, "24 − 2 × 8 = 8 人を視察");
            Assert.AreEqual(pts - 8, gs.ScoutPointsAvailable);
            Assert.AreEqual(8, gs.ScoutStages.Count(kv => kv.Value == ScoutStage.Observed));
            Assert.AreEqual(0, gs.AutoScoutRecruit(), "勧誘は 12〜2 月");
            Advance(gs, 4);                                                                          // 12 月
            int chosen = gs.AutoScoutRecruit();
            Assert.That(chosen, Is.InRange(1, GameSession.ScoutTargetRecommended));
            Assert.AreEqual(chosen, gs.ScoutStages.Count(kv => kv.Value == ScoutStage.Invited));
            Assert.GreaterOrEqual(gs.ScoutPointsAvailable, 0);
            foreach (var kv in gs.ScoutStages.Where(x => x.Value == ScoutStage.Invited))
                if (gs.ScoutPool[kv.Key].V >= WorldK.K_V_TOP) Assert.LessOrEqual(gs.ScoutStages.Count(y => y.Value == ScoutStage.Invited && gs.ScoutPool[y.Key].V >= WorldK.K_V_TOP), gs.School.ScholSlots(), "上位級は奨学金枠まで");
        }

        [Test]
        public void ScoutPool_IsTheSameAsTheYearEndPool()
        {
            GameSession gs = New();
            var direct = PoolGenerator.Generate(gs.Rngs, 1, WorldK.K_POOL_N, gs.World.ElitePrefBonus);
            Assert.AreEqual(direct.Count, gs.ScoutPool.Count);
            for (int i = 0; i < direct.Count; i += 37)
            {
                Assert.AreEqual(direct[i].LookSeed, gs.ScoutPool[i].LookSeed);
                Assert.AreEqual(direct[i].V, gs.ScoutPool[i].V);
                Assert.AreEqual(i, gs.ScoutPool[i].Index);
            }
            Advance(gs, 48);
            Assert.AreNotEqual(direct[0].LookSeed, gs.ScoutPool[0].LookSeed, "次の年度は別の候補");
        }
    }
}
