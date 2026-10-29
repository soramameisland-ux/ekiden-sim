using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;

namespace Ekiden.Verification
{
    /// <summary>
    /// 性能の測定(09 §14.2。T5)の、Unity の確認(IL2CPP のビルド・Editor)でも走る部分。SimCli の <c>bench</c>(.NET)のうち、Domain だけで動くもの: 世界の生成・1 年度の更新・レース 1 本・週の解決・割り当て(GC)。
    /// 保存・読み込み(合成のセーブが要るもの)は SimCli の <c>bench</c> だけ。予算は『下位のスマホ』の値なので、開発機では 1/<see cref="PhoneSlowdown"/> 以内を目安にする(実機の確認は M1・L-38)。
    /// 判定はせず、測った値を書く(結果の合否は呼び出し側の方針)。戻り値は目安を超えた項目の数。
    /// </summary>
    public static class PerfRunner
    {
        public const double PhoneSlowdown = 5.0;

        /// <summary>測定して <paramref name="w"/> に書く。<paramref name="quick"/> なら回数を減らす。戻り値 = 目安(予算 / <see cref="PhoneSlowdown"/>)を超えた項目の数。</summary>
        public static int Run(TextWriter w, bool quick = false)
        {
            int rep = quick ? 3 : 7;
            int over = 0;
            w.WriteLine("== 性能(09 §14.2。目安 = 予算の 1/" + PhoneSlowdown + " 以内)");

            double create = Median(rep, () => WorldSim.Create(new KeyedRngFactory(VerifyCommon.WorldSeed(1)), 15));
            over += Line(w, "世界の生成(初期の名簿 + 予熱 15 年度)", create, 3000.0, "ms");

            WorldSim world = WorldSim.Create(new KeyedRngFactory(VerifyCommon.WorldSeed(2)), 15);
            PlayerAgent agent = world.StartPlayer(Difficulty.Mid);
            var times = new List<double>();
            for (int y = 1; y <= 45; y++)
            {
                var sw = Stopwatch.StartNew();
                world.PlayYear(y, agent);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            over += Line(w, "1 年度の更新(平均。最初の 2 年度を除く)", times.Skip(2).Average(), 100.0, "ms");
            over += Line(w, "1 年度の更新(最大)", times.Skip(2).Max(), 100.0, "ms");

            WorldSim rw = WorldSim.Create(new KeyedRngFactory(VerifyCommon.WorldSeed(3)), 15);
            const RaceKind kind = RaceKind.Fall;
            int rid = 11;
            Env[] envs = RaceMath.GenEnv(rw.Rngs.Stream(RngRegistry.LabelRace, rid, 9999), kind);
            var teams = new List<RaceTeam>();
            foreach (School s in rw.Schools.OrderByDescending(x => x.Prestige).Take(20))
            {
                double lv = s.Lv();
                var roster = s.Roster.Select(r => AiRacing.ToRaceRunner(r, lv, rw.Rngs)).ToList();
                var (lineup, instr) = AiRacing.PrepareLineup(roster, kind, envs, rw.Rngs, rid, s.Id);
                teams.Add(new RaceTeam { Id = s.Id, Lineup = lineup, Instr = instr, Geki = 0 });
            }
            Func<int, int, Pcg32> rngFor = (tid, si) => rw.Rngs.Stream(RngRegistry.LabelRace, rid, tid * 100 + si);
            int sections = RaceTables.Rule(kind).Run;
            double race = Median(rep * 3, () => RaceSimulator.Simulate(kind, teams, envs, rngFor));
            over += Line(w, "1 レース(第一戦 20 校 × " + sections + " 区間)", race, 1.0 * sections, "ms");
            over += Line(w, "レースの 1 区間", race / sections, 1.0, "ms");

            var master = TrainingMaster.CreateDefault();
            var resolver = new WeekResolver(master);
            var runner = new Runner { Grade = 1 };
            Array.Copy(new[] { 62, 66, 64, 58, 54, 52, 60, 58, 56, 58 }, runner.Stats, 10);
            Array.Copy(new[] { 78, 80, 78, 74, 72, 70, 76, 74, 72, 76 }, runner.Potential, 10);
            var rngs = new KeyedRngFactory(77);
            string[] plan = YearPlanner.StdPlan;
            Action oneYear = () =>
            {
                for (int yw = 0; yw < 48; yw++)
                {
                    bool camp;
                    Menu menu = YearPlanner.WeekMenu(master, yw, plan, YearPlanner.Races, true, out camp);
                    resolver.Resolve(runner, menu, FacilityLevels.AllTwo, yw / 4, rngs, 1, yw, camp: camp, safety: true, bondAvg: 200.0);
                }
            };
            double week = Median(rep * 5, oneYear) / 48.0 * 1000.0;         // μs
            over += Line(w, "週の解決 1 件", week, 25.0, "μs");
            w.WriteLine("  ・ 自校 1 年度の育成(40 人 × 48 週)= " + (week * 40 * 48 / 1000.0).ToString("F1") + " ms");

            long gw = Alloc(20, oneYear);
            long gr = Alloc(50, () => RaceSimulator.Simulate(kind, teams, envs, rngFor));
            if (gw < 0 || gr < 0) w.WriteLine("  ・ 割り当て(GC): この環境では測れない(GC.GetAllocatedBytesForCurrentThread が使えない)");
            else
            {
                w.WriteLine("  ・ 割り当て 週の解決: " + (gw / (20 * 48)).ToString("N0") + " bytes/回(SimCli bench の基準値 640)");
                w.WriteLine("  ・ 割り当て 1 レース(第一戦 20 校): " + (gr / 50).ToString("N0") + " bytes/回(同 57,000)");
            }
            w.WriteLine(over == 0 ? "目安の内側" : "目安を超えた項目 " + over + " 件(実機の予算の確認は M1)");
            return over;
        }

        private static int Line(TextWriter w, string what, double v, double budget, string unit)
        {
            bool ok = v <= budget / PhoneSlowdown;
            w.WriteLine("  " + VerifyCommon.Mark(ok) + " " + what + ": " + v.ToString("F2") + " " + unit + "(予算 " + budget.ToString("F0") + " " + unit + ")");
            return ok ? 0 : 1;
        }

        /// <summary>a を n 回呼んだ時間(ms)の中央値。最初の 1 回(JIT・静的な表の初期化)は除く。</summary>
        private static double Median(int n, Action a)
        {
            a();
            var t = new List<double>();
            for (int i = 0; i < n; i++)
            {
                var sw = Stopwatch.StartNew();
                a();
                t.Add(sw.Elapsed.TotalMilliseconds);
            }
            t.Sort();
            return t[t.Count / 2];
        }

        /// <summary>a を n 回呼んだときの、このスレッドの割り当て量(bytes)。測れない環境では −1。</summary>
        private static long Alloc(int n, Action a)
        {
            try
            {
                a();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < n; i++) a();
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
