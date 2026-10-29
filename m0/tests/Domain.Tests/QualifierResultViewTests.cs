using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>予選会の結果の View(SC-26 の予選会。04 §3.2): 順位表・通過ラインとの差・自校の 12 名。</summary>
    public class QualifierResultViewTests
    {
        private static GameSession PastTheQualifier(Difficulty d, bool wantSeed, out bool found, ulong from = 31, ulong to = 60)
        {
            found = false;
            GameSession last = null;
            for (ulong seed = from; seed < to; seed++)
            {
                GameSession gs = GameSession.New(seed, d);
                while (gs.YearWeek != GameSession.QualifierWeek || gs.Year != 2) gs.StepWeek();
                bool isSeed = gs.PrepareYear().Seeds.Contains(gs.School.Id);
                last = gs;
                if (isSeed != wantSeed) continue;
                gs.StepWeek();                                                         // 予選会の週を進める(予選会が行われる)
                found = true;
                return gs;
            }
            return last;
        }

        [Test]
        public void BeforeTheQualifier_ThereIsNoResult()
        {
            GameSession gs = GameSession.New(31UL, Difficulty.Low);
            QualifierResultView v = gs.QualifierResult();
            Assert.IsFalse(v.Held); Assert.IsFalse(v.OwnEntered);
            CollectionAssert.IsEmpty(v.Overall);
        }

        [Test]
        public void TheView_MatchesAnIndependentRecomputationFromTheTimes_WhetherTheSchoolPassesOrNot()
        {
            int passed = 0, failed = 0;
            for (ulong from = 31; from < 300 && (passed < 2 || failed < 2); from += 3)
            {
                bool found;
                GameSession gs = PastTheQualifier(Difficulty.Low, false, out found, from, from + 3);
                if (!found) continue;
                if (VerifyView(gs)) passed++; else failed++;
            }
            Assert.GreaterOrEqual(passed, 2, "試験の前提: 通過する年度がある"); Assert.GreaterOrEqual(failed, 2, "試験の前提: 落選する年度がある");
        }

        private static bool VerifyView(GameSession gs)
        {
            QualifierResultView v = gs.QualifierResult();
            List<QualifierResult> raw = gs.YearInProgress.QualifierResults;
            Assert.IsTrue(v.Held); Assert.IsTrue(v.OwnEntered);
            Assert.AreEqual(raw.Count, v.Teams); Assert.AreEqual(RaceConstants.QualPass, v.PassCount);
            // 独立に: 各校の合計 = 速い 10 人のタイムの和、順位 = 合計の小さい順(同じなら学校 ID の小さい方)
            var again = raw.Select(r => new { r.Id, Total = r.Times.OrderBy(x => x).Take(RaceConstants.QualCount).Sum() }).OrderBy(x => x.Total).ThenBy(x => x.Id).ToList();
            Assert.AreEqual(again.Count, v.Overall.Count);
            for (int i = 0; i < again.Count; i++)
            {
                Assert.AreEqual(i + 1, v.Overall[i].Rank); Assert.AreEqual(again[i].Id, v.Overall[i].SchoolId); Assert.AreEqual(again[i].Total, v.Overall[i].TotalDs);
                Assert.AreEqual(again[i].Total - again[0].Total, v.Overall[i].GapDs); Assert.AreEqual("school." + again[i].Id + ".name", v.Overall[i].SchoolNameKey);
                Assert.AreEqual(again[i].Id == gs.School.Id, v.Overall[i].IsOwn);
            }
            int ownIndex = again.FindIndex(x => x.Id == gs.School.Id);
            Assert.AreEqual(ownIndex + 1, v.OwnRank); Assert.AreEqual(again[ownIndex].Total, v.OwnTotalDs);
            Assert.AreEqual(ownIndex < RaceConstants.QualPass, v.Passed);
            Assert.AreEqual(again[RaceConstants.QualPass - 1].Total, v.LineDs, "通過ラインは 10 位の合計");
            if (v.Passed) Assert.AreEqual(again[RaceConstants.QualPass].Total - again[ownIndex].Total, v.MarginDs, "通過: 次点(11 位)との差");
            else Assert.AreEqual(again[ownIndex].Total - again[RaceConstants.QualPass - 1].Total, v.MarginDs, "落選: 通過ラインに足りなかった差");
            Assert.GreaterOrEqual(v.MarginDs, 0, "差はどちらの向きでも 0 以上(通過 = 余裕 / 落選 = 足りなかった)");
            if (!v.Passed) Assert.Greater(v.MarginDs, 0);
            // 通過校は実際に第三戦の出場権を得た学校
            CollectionAssert.AreEqual(again.Take(RaceConstants.QualPass).Select(x => x.Id).ToList(), gs.YearInProgress.Result.Qualified);
            // 自校の選手
            Assert.AreEqual(raw.Single(r => r.Id == gs.School.Id).Times.Length, v.Own.Count);
            Assert.AreEqual(RaceConstants.QualEntry, v.Own.Count);
            for (int i = 0; i < v.Own.Count; i++)
            {
                QualifierRunnerView r = v.Own[i];
                Assert.AreEqual(i + 1, r.Rank); Assert.AreEqual(i < RaceConstants.QualCount, r.Counted);
                if (i > 0) Assert.GreaterOrEqual(r.Ds, v.Own[i - 1].Ds, "タイムの小さい順");
                Runner real = gs.State.FindRunner(r.RunnerId);
                Assert.IsNotNull(real, "自校の選手 (rank " + (i + 1) + ")"); Assert.AreEqual(real.FamilyName, r.FamilyName);
            }
            Assert.AreEqual(v.OwnTotalDs, v.Own.Where(r => r.Counted).Sum(r => r.Ds), "合計 = 数えられる 10 名のタイムの和");
            Assert.AreEqual(12, v.Own.Select(r => r.RunnerId).Distinct().Count(), "同じ選手を二重に数えない");
            return v.Passed;
        }

        [Test]
        public void ASeedSchool_DoesNotRunTheQualifier()
        {
            bool found;
            GameSession gs = PastTheQualifier(Difficulty.Elite, true, out found);
            Assert.IsTrue(found, "試験の前提: シードの年度がある");
            QualifierResultView v = gs.QualifierResult();
            Assert.IsTrue(v.Held, "予選会そのものは行われる"); Assert.IsFalse(v.OwnEntered); Assert.AreEqual(0, v.OwnRank);
            CollectionAssert.IsEmpty(v.Own);
            Assert.IsFalse(v.Overall.Any(r => r.IsOwn));
        }

        /// <summary>タイムと選手 ID の対応: 1 人だけ極端に速くすると、その選手の ID が先頭に来る(乱数の引き方は選手の番号で決まるので、ほかの選手のタイムは変わらない)。</summary>
        [Test]
        public void RunnerIds_FollowTheirTimes()
        {
            for (int k = 0; k < 12; k++)
            {
                QualifierResult plain = Run(-1), fast = Run(k);
                Assert.AreEqual(k + 1, fast.RunnerIds[0], "速くした選手が先頭 (k=" + k + ")");
                Assert.Less(fast.Times[0], plain.Times[0]);
                CollectionAssert.AreEquivalent(Enumerable.Range(1, 12).ToArray(), fast.RunnerIds);
                Assert.IsTrue(plain.Times.SequenceEqual(plain.Times.OrderBy(x => x)));
                // 速くした 1 人を除いた 11 人のタイムは変わらない(乱数の引き方は選手の番号で決まる)
                var restPlain = plain.Times.ToList(); restPlain.Remove(plain.Times[Array.IndexOf(plain.RunnerIds, k + 1)]);
                var restFast = fast.Times.Skip(1).ToList();
                CollectionAssert.AreEquivalent(restPlain, restFast, "ほかの 11 人のタイム (k=" + k + ")");
            }
        }

        /// <summary>同じタイムの選手は走った順(番号の小さい方が先)に並ぶ。並べ替えの実装(実行環境)に依らず同じ結果にするため。</summary>
        [Test]
        public void TiedTimes_KeepTheRunningOrder()
        {
            var runners = new RaceRunner[40];                       // 17 人以上で並べ替えは挿入ソートだけでなくなる(同じ値の並びは実装依存になりうる)
            for (int i = 0; i < 40; i++)
            {
                runners[i] = new RaceRunner { RunnerId = 100 + i, Form = 0, Fatigue = 0 };
                for (int s = 0; s < runners[i].Stats.Length; s++) runners[i].Stats[s] = 60;
            }
            var rngs = new KeyedRngFactory(77UL);
            Env env = new Env { Temp = 15.0, Wind = 0.0, Rain = false };
            QualifierResult r = Qualifier.Run(new List<QualifierTeam> { new QualifierTeam { Id = 5, Runners = runners } }, (tid, ri) => rngs.Stream(4, 14, tid * 100), env)[0];   // 全員が同じ乱数の列 = 同じタイム
            Assert.AreEqual(1, r.Times.Distinct().Count(), "試験の前提: 全員が同じタイム");
            CollectionAssert.AreEqual(Enumerable.Range(100, 40).ToArray(), r.RunnerIds);
        }

        private static QualifierResult Run(int fastIndex)
        {
            var runners = new RaceRunner[12];
            for (int i = 0; i < 12; i++)
            {
                runners[i] = new RaceRunner { RunnerId = i + 1, Form = 0, Fatigue = 0 };
                for (int s = 0; s < runners[i].Stats.Length; s++) runners[i].Stats[s] = i == fastIndex ? 95 : 60;
            }
            var rngs = new KeyedRngFactory(77UL);
            Env env = new Env { Temp = 15.0, Wind = 0.0, Rain = false };
            return Qualifier.Run(new List<QualifierTeam> { new QualifierTeam { Id = 5, Runners = runners } }, (tid, ri) => rngs.Stream(4, 14, tid * 100 + ri), env)[0];
        }
    }
}
