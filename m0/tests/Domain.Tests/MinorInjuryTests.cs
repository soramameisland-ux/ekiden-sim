using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>故障と出走(04 E3・O3。L-59): 登録できないのは重傷だけ / 配置できないのは中傷以上 / 軽傷は調子 −2 で出られる(出られる選手が足りずに棄権にならない)。</summary>
    public class MinorInjuryTests
    {
        private const ulong Seed = 20260601UL;

        private static GameSession AtWeek(ulong seed, Difficulty d, int yearWeek, int year = 1)
        {
            GameSession gs = GameSession.New(seed, d);
            while (!(gs.Year == year && gs.YearWeek == yearWeek)) gs.StepWeek();
            return gs;
        }

        private static void Hurt(Runner r, InjurySeverity s, int weeks = 40) { r.InjurySeverity = s; r.InjuryWeeksLeft = s == InjurySeverity.None ? 0 : weeks; }

        [Test]
        public void Eligibility_FollowsTheSeverity_AndTheBench()
        {
            var s = new TeamState();
            var r = new Runner { Id = 7 };
            var table = new[]
            {
                // 故障, 登録できる(E3), 配置できる(O3), 出られる(Bench なし)
                (InjurySeverity.None, true, true), (InjurySeverity.Minor, true, true), (InjurySeverity.Moderate, true, false), (InjurySeverity.Severe, false, false),
            };
            foreach (var t in table)
            {
                r.InjurySeverity = t.Item1;
                Assert.AreEqual(t.Item2, TeamRacing.CanRegister(r), t.Item1 + " 登録");
                Assert.AreEqual(t.Item3, TeamRacing.CanPlace(r), t.Item1 + " 配置");
                Assert.AreEqual(t.Item3, TeamRacing.CanRun(s, r), t.Item1 + " 出走");
            }
            r.InjurySeverity = InjurySeverity.None;
            s.Bench.Add(r.Id);
            Assert.IsFalse(TeamRacing.CanRun(s, r), "イベントで欠場にされた選手は出られない");
            r.InjurySeverity = InjurySeverity.Minor;
            Assert.IsFalse(TeamRacing.CanRun(s, r));
        }

        [Test]
        public void Entry_AcceptsMinorAndModerate_RejectsSevere_AndTheOrderAcceptsOnlyMinor()
        {
            GameSession gs = AtWeek(Seed, Difficulty.Elite, TeamWeek.EntryDeadlineWeeks[0]);
            TeamState s = gs.State;
            List<int> entered = s.Entries[0].ToList();
            Assert.GreaterOrEqual(entered.Count, 6, "試験の前提: おまかせ登録が入っている");
            Runner a = s.FindRunner(entered[0]), b = s.FindRunner(entered[1]), c = s.FindRunner(entered[2]);
            Hurt(a, InjurySeverity.Minor, 2); Hurt(b, InjurySeverity.Moderate, 6);
            Assert.IsTrue(gs.SetEntries(RaceKind.Fall, entered, true).Ok, "軽傷・中傷の登録は通る(E3)");
            Hurt(c, InjurySeverity.Severe, 20);
            OpResult o = gs.SetEntries(RaceKind.Fall, entered, true);
            Assert.IsFalse(o.Ok); StringAssert.Contains("重傷", o.Reason);
            EntryView ev = gs.Entry(RaceKind.Fall);
            Assert.IsTrue(ev.Roster.Single(x => x.Row.Id == a.Id).CanEnter); Assert.IsTrue(ev.Roster.Single(x => x.Row.Id == b.Id).CanEnter);
            EntryRowView sev = ev.Roster.Single(x => x.Row.Id == c.Id);
            Assert.IsFalse(sev.CanEnter); Assert.AreEqual("injured", sev.BlockedReason);
            Assert.IsFalse(ev.Problems.Any(p => p.Code == "entry.injured" && p.RunnerId == a.Id), "軽傷は出られるので指摘しない");
            Assert.IsTrue(ev.Problems.Any(p => p.Code == "entry.injured" && p.RunnerId == b.Id), "中傷は大会までに治らなければ出られない");

            // オーダー(O3): 軽傷までは配置できる
            Hurt(c, InjurySeverity.None);
            Assert.IsTrue(gs.SetEntries(RaceKind.Fall, entered, true).Ok);
            int sections = RaceTables.Course(RaceKind.Fall).Count;
            List<int> order = entered.Where(id => id != b.Id).Take(sections).ToList();               // 軽傷の選手(entered[0])を先発に含むオーダー
            Assert.AreEqual(sections, order.Count); Assert.IsTrue(order.Contains(a.Id));
            Assert.IsTrue(gs.SetOrder(RaceKind.Fall, order).Ok, "軽傷の選手を含むオーダーは通る");
            Assert.IsFalse(gs.Order(RaceKind.Fall).Problems.Any(p => p.Code == "order.injured"));
            var withModerate = order.ToList(); withModerate[1] = b.Id;
            OpResult r2 = gs.SetOrder(RaceKind.Fall, withModerate);
            Assert.IsFalse(r2.Ok); StringAssert.Contains("中傷", r2.Reason);
        }

        [Test]
        public void OrderView_FlagsOnlyModerateAndSevere()
        {
            GameSession gs = AtWeek(Seed, Difficulty.Elite, TeamWeek.EntryDeadlineWeeks[0] + 1);
            int[] sug = gs.SuggestOrder(RaceKind.Fall);
            Assert.IsTrue(gs.SetOrder(RaceKind.Fall, sug).Ok);
            Runner r = gs.State.FindRunner(sug[2]);
            Hurt(r, InjurySeverity.Minor, 2);
            Assert.IsFalse(gs.Order(RaceKind.Fall).Problems.Any(p => p.Code == "order.injured"), "軽傷は配置できる");
            Hurt(r, InjurySeverity.Moderate, 6);
            Assert.IsTrue(gs.Order(RaceKind.Fall).Problems.Any(p => p.Code == "order.injured" && p.RunnerId == r.Id));
        }

        [Test]
        public void QualifierRegistration_RejectsOnlySevere()
        {
            GameSession gs = null;
            for (ulong seed = Seed; seed < Seed + 8 && gs == null; seed++)
            {
                GameSession t = AtWeek(seed, Difficulty.Low, GameSession.QualifierDeadlineWeek);
                if (!t.PrepareYear().Seeds.Contains(t.School.Id)) gs = t;
            }
            Assert.IsNotNull(gs, "試験の前提: シード外の年度がある");
            foreach (Runner r in gs.State.Runners) Hurt(r, InjurySeverity.None);                              // 自然に起きた故障は消して、試験が決めた故障だけにする
            List<int> ids = gs.State.Runners.Select(r => r.Id).Take(RaceConstants.QualEntry).ToList();
            Assert.AreEqual(RaceConstants.QualEntry, ids.Count, "試験の前提: 12 名いる");
            Hurt(gs.State.FindRunner(ids[0]), InjurySeverity.Minor, 2); Hurt(gs.State.FindRunner(ids[1]), InjurySeverity.Moderate, 6);
            OpResult q = gs.SetQualifierEntries(ids);
            Assert.IsTrue(q.Ok, q.Reason);
            Hurt(gs.State.FindRunner(ids[2]), InjurySeverity.Severe, 20);
            OpResult o = gs.SetQualifierEntries(ids);
            Assert.IsFalse(o.Ok); StringAssert.Contains("重傷", o.Reason);
        }

        [Test]
        public void AutoRegister_KeepsAMinorInjuredRunner_AndLeavesOutAModerateOne()
        {
            GameSession gs = AtWeek(Seed, Difficulty.Elite, TeamWeek.EntryDeadlineWeeks[0]);
            TeamState s = gs.State;
            List<int> baseline = TeamRacing.AutoRegister(s, RaceKind.Fall);
            Assert.Greater(baseline.Count, 6);
            Runner top = s.FindRunner(baseline[0]);
            Hurt(top, InjurySeverity.Minor, 2);
            CollectionAssert.AreEquivalent(baseline, TeamRacing.AutoRegister(s, RaceKind.Fall), "軽傷でも出られるので、登録は変わらない");
            Hurt(top, InjurySeverity.Moderate, 6);
            CollectionAssert.DoesNotContain(TeamRacing.AutoRegister(s, RaceKind.Fall), top.Id, "中傷は出られないので、おまかせ登録に入れない");
        }

        /// <summary>出られる選手が出走人数ちょうどで、そのうち 1 人が軽傷でも、棄権にならずに走る(調子 −2 で)。</summary>
        [Test]
        public void ARosterWithJustEnoughRunners_DoesNotWithdraw_BecauseOfAMinorInjury()
        {
            int ranWithMinor = 0, tried = 0;
            for (ulong seed = Seed; seed < Seed + 12; seed++)
            {
                GameSession gs = AtWeek(seed, Difficulty.Elite, Calendar.RaceWeeks[0], 2);
                TeamState s = gs.State;
                if (!gs.PrepareYear().Entrants(RaceKind.Fall).Contains(gs.School.Id)) continue;
                int run = RaceTables.Rule(RaceKind.Fall).Run;
                s.Bench.Clear();                                                                       // イベントの欠場も消す(試験が決めた状態だけにする)
                List<Runner> able = s.Runners.OrderBy(r => r.Id).Take(run).ToList();
                foreach (Runner r in s.Runners) if (!able.Contains(r)) Hurt(r, InjurySeverity.Severe, 40);
                foreach (Runner r in able) { Hurt(r, InjurySeverity.None); r.Fatigue = 10; }
                Runner minor = able[3];
                Hurt(minor, InjurySeverity.Minor, 40);
                Assert.IsTrue(gs.SetEntries(RaceKind.Fall, able.Select(r => r.Id).ToList(), true).Ok);
                gs.ResolveTraining();
                if (able.Any(r => r != minor && r.IsInjured)) continue;                          // 練習の週に新しく故障した(確率は小さい): この世界は使わない
                tried++;
                Assert.AreEqual(run, s.Runners.Count(r => TeamRacing.CanRun(s, r)), "出られるのは出走人数ちょうど(軽傷を含む) bench=" + string.Join(",", s.Bench) + " able=" + string.Join(",", able.Select(r => r.Id + ":" + r.InjurySeverity)));
                Assert.IsTrue(minor.IsInjured); Assert.AreEqual(-2, minor.Form, "軽傷でも調子は −2");
                RaceDay rd = gs.OpenRaceDay(RaceKind.Fall);
                Assert.IsNotNull(rd, "軽傷の選手を含めて出走人数がそろう: 棄権にならない (seed " + seed + ")");
                RaceRunner inLineup = rd.Lineup.FirstOrDefault(x => x.RunnerId == minor.Id);
                if (inLineup != null) { Assert.AreEqual(-2, inLineup.Form, "当日の調子も −2"); ranWithMinor++; }
                RaceInfo? own;
                gs.PlayWeekRaces(out own);
                Assert.IsTrue(own.HasValue, "大会に出た (seed " + seed + ")");
            }
            Assert.GreaterOrEqual(tried, 5, "試験の前提: 使える世界が十分にある");
            Assert.GreaterOrEqual(ranWithMinor, 3, "軽傷の選手が実際に走った世界がある(欠場の抽選で外れる世界もある)");
        }
    }
}
