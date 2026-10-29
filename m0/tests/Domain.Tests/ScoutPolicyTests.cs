using System.Linq;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>検算用のスカウトの方針(L-58): 画面の『おまかせスカウト』のボタン(08 R-08)を月ごとに押す流れが、進行制御を通して動く。</summary>
    public class ScoutPolicyTests
    {
        private const ulong Seed = 20260601UL;

        private static string Who(GameSession gs)
        {
            return string.Join("|", gs.LastIntakeIds.Select(id => gs.State.FindRunner(id)).Select(r => r.FamilyName + r.GivenName + ":" + string.Join(",", r.Stats)));
        }

        [Test]
        public void TheFlowBaseline_IsTheSameAsStepWeek_AndTheButtonChangesTheIntake()
        {
            GameSession a = GameSession.New(Seed, Difficulty.Mid), b = GameSession.New(Seed, Difficulty.Mid), c = GameSession.New(Seed, Difficulty.Mid);
            a.PlayYears(3);
            Acceptance.PlayScoutPolicy(b, 3, button: false);
            Acceptance.PlayScoutPolicy(c, 3, button: true);
            Assert.AreEqual(a.State.Runners.Select(r => r.Id + ":" + string.Join(",", r.Stats)).ToArray(), b.State.Runners.Select(r => r.Id + ":" + string.Join(",", r.Stats)).ToArray(),
                "進行制御を通すだけなら、StepWeek の流れとバイト一致(= 基準として使える)");
            Assert.AreEqual(a.Stats.Select(s => s.P).ToArray(), b.Stats.Select(s => s.P).ToArray());
            Assert.Greater(c.LastIntakeTargeted.Count, 0, "ボタンで声かけ・練習会を行った候補が入学している(行動なしなら 0)");
            Assert.AreNotEqual(Who(b), Who(c), "年度末の提示が変わり、入学者が変わる");
            Assert.LessOrEqual(c.LastIntakeTargeted.Count, GameSession.ScoutTargetRecommended + 2, "目標はほぼ推奨の人数(8)");
        }
    }
}
