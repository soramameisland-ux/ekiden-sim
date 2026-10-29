using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>走者ごとの檄の効き f_k(04 §9.2。L-60 ②): やる気と絆から決まり、損失の軽減 0.2 × f_k に効く。</summary>
    public class GekiFactorTests
    {
        private static RaceRunner Plain(double f)
        {
            var r = new RaceRunner { Form = 0, Fatigue = 0, GekiF = f };
            for (int i = 0; i < r.Stats.Length; i++) r.Stats[i] = 60;
            return r;
        }

        private static RaceTeam TeamOf(int id, double f, int n)
        {
            return new RaceTeam { Id = id, Geki = 4, Lineup = Enumerable.Range(0, n).Select(_ => Plain(f)).ToArray() };
        }

        [Test]
        public void RaceRunnerFrom_TakesMotivationAndBond()
        {
            var r = new Runner { Id = 5, Motivation = 3, BondAvg = 500.0 };
            for (int i = 0; i < r.Stats.Length; i++) r.Stats[i] = 55;
            Assert.AreEqual(0.5 + 0.1 * 3 + 0.3 * 500.0 / 1000.0, RaceRunner.From(r).GekiF, 1e-12, "標準: やる気 3・絆 500 → 0.95");
            r.Motivation = 5; r.BondAvg = 800.0;
            Assert.AreEqual(1.2, RaceRunner.From(r).GekiF, 0.0, "上限 1.2");
            r.Motivation = 1; r.BondAvg = 0.0;
            Assert.AreEqual(0.6, RaceRunner.From(r).GekiF, 1e-12);
            Assert.AreEqual(0.6, RaceRunner.From(r).Clone().GekiF, 1e-12, "Clone が残す");
            Assert.AreEqual(1.0, new RaceRunner().GekiF, "他校の抽象の選手は 1.0");
        }

        /// <summary>檄の使われ方(どの区間か)は f_k に依らず、軽減される損失の大きさだけが (1 − 0.2 f_k) に比例する。1 チームだけで走らせる(他のチームとの集団・追う展開の補正が合計に混ざらないように)。</summary>
        [Test]
        public void TheLossReduction_ScalesWithEachRunnersF_ButNotWhichSectionsUseIt()
        {
            int checkedSections = 0, faster = 0;
            for (int rid = 11; rid < 71; rid++)
            {
                var master = (ulong)(20261029 + rid);
                var rngs = new KeyedRngFactory(master);
                Env[] envs = RaceMath.GenEnv(rngs.Stream(4, rid, 9999), RaceKind.National);
                List<TeamResult> low = Simulate(rid, master, envs, 0.6), high = Simulate(rid, master, envs, 1.2);
                TeamResult a = low.Single(t => t.Id == 1), b = high.Single(t => t.Id == 1);
                CollectionAssert.AreEqual(a.Events.Where(e => e.Kind == "geki_brake").Select(e => e.Section).ToArray(), b.Events.Where(e => e.Kind == "geki_brake").Select(e => e.Section).ToArray(), "檄を使う区間は同じ (rid " + rid + ")");
                foreach (RaceEvent g in a.Events.Where(e => e.Kind == "geki_brake"))
                {
                    var ea = a.Events.Where(e => e.Section == g.Section && (e.Kind == "brake" || e.Kind == "stall")).ToList();
                    var eb = b.Events.Where(e => e.Section == g.Section && (e.Kind == "brake" || e.Kind == "stall")).ToList();
                    if (ea.Count != 1 || eb.Count != 1) continue;                                   // ブレーキと失速が両方起きた区間は、大きい方だけ軽減されるので比べない
                    Assert.AreEqual(ea[0].Kind, eb[0].Kind);
                    Assert.AreEqual((1.0 - 0.2 * 1.2) / (1.0 - 0.2 * 0.6), eb[0].Value / ea[0].Value, 1e-3, "損失の比 = (1 − 0.2×1.2) / (1 − 0.2×0.6) (rid " + rid + " 区間 " + g.Section + ")");
                    checkedSections++;
                }
                Assert.LessOrEqual(b.TotalDs, a.TotalDs, "効きが高いほど遅くならない (rid " + rid + ")");
                if (b.TotalDs < a.TotalDs) faster++;
            }
            Assert.Greater(checkedSections, 30, "試験の前提: 檄を使った区間が十分にある");
            Assert.Greater(faster, 20);
        }

        private static List<TeamResult> Simulate(int rid, ulong master, Env[] envs, double f)
        {
            var rngs = new KeyedRngFactory(master);
            return RaceSimulator.Simulate(RaceKind.National, new List<RaceTeam> { TeamOf(1, f, 8) }, envs, (tid, si) => rngs.Stream(4, rid, tid * 100 + si));
        }

        /// <summary>自校の当日の先発は、その選手のやる気と絆から f_k が決まる。</summary>
        [Test]
        public void TheOwnSchoolsLineup_CarriesEachRunnersF()
        {
            GameSession gs = GameSession.New(20261029UL, Difficulty.Elite);
            while (!(gs.Year == 2 && gs.YearWeek == Calendar.RaceWeeks[0])) gs.StepWeek();
            gs.ResolveTraining();
            RaceDay rd = gs.OpenRaceDay(RaceKind.Fall);
            Assert.IsNotNull(rd);
            int seen = 0;
            foreach (RaceRunner x in rd.Lineup.Concat(rd.Bench))
            {
                if (x.RunnerId == 0) { Assert.AreEqual(1.0, x.GekiF, "補充要員は 1.0"); continue; }
                Runner r = gs.State.FindRunner(x.RunnerId);
                Assert.AreEqual(RaceMath.GekiFactor(r.Motivation, r.BondAvg), x.GekiF, 0.0);
                seen++;
            }
            Assert.GreaterOrEqual(seen, 6);
        }
    }
}
