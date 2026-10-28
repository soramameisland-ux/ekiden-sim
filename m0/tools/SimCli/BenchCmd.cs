using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using Ekiden.Persistence;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;

namespace Ekiden.SimCli
{
    /// <summary>性能の予算(09 §14.2。T5)。予算は『下位のスマホ』の値なので、開発機では 1/<see cref="Common.PhoneSlowdown"/> 以内を合格とする(実機の確認は M1・L-38)。
    /// ホットパスの割り当て(GC)は、09 §14.2 の『0』を目標に、M0 の実測を閾値として記録する(<c>GcBaseline*</c>)。超えたら退行。</summary>
    internal static class BenchCmd
    {
        // 割り当ての基準値(M0 の実測。bytes)。退行を見つけるための上限で、0 に近づけるのは M1 のプロファイルのあと(IL2CPP・実機)
        private const long GcBaselineWeek = 640;          // 週の解決 1 回
        private const long GcBaselineRace = 57000;        // 第一戦 1 レース(20 校)
        private const long GcBaselineYear = 3100000;      // 世界の 1 年度(自校あり)

        public static int Run(string[] args)
        {
            bool quick = Common.Flag(args, "quick");
            int rep = quick ? 3 : 7;
            int bad = 0;
            Console.WriteLine("== 性能(09 §14.2。開発機の実測。判定 = 予算の 1/" + Common.PhoneSlowdown + " 以内)");

            // 1. 世界の生成
            double create = SaveCmd.Median(rep, () => WorldSim.Create(new KeyedRngFactory(Common.WorldSeed(1)), 15));
            bad += Line("世界の生成(初期の名簿 4 学年 + 予熱 15 年度)", create, 3000.0, "ms", "参照 Python 1100〜1300 ms");

            // 2. 1 年度の世界の更新(自校あり。大会・成長・入学を含む 1 回の呼び出し)
            WorldSim w = WorldSim.Create(new KeyedRngFactory(Common.WorldSeed(2)), 15);
            PlayerAgent agent = w.StartPlayer(Difficulty.Mid);
            var times = new List<double>();
            for (int y = 1; y <= 45; y++)
            {
                var sw = Stopwatch.StartNew();
                w.PlayYear(y, agent);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            double meanYear = times.Skip(2).Average(), maxYear = times.Skip(2).Max();
            bad += Line("1 年度の更新(3 大会 + 予選会 + 成長 + 入学。平均)", meanYear, SaveConstants.K_DOMAIN_CALL_MS, "ms", "参照 Python 約 70 ms(他校のみ)");
            bad += Line("1 年度の更新(最大)", maxYear, SaveConstants.K_DOMAIN_CALL_MS, "ms", "Domain の 1 呼び出し = K_DOMAIN_CALL_MS");

            // 3. レース 1 本(第一戦 20 校)
            WorldSim rw = WorldSim.Create(new KeyedRngFactory(Common.WorldSeed(3)), 15);
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
            List<TeamResult> sample = RaceSimulator.Simulate(kind, teams, envs, rngFor);
            int sections = RaceTables.Rule(kind).Run;
            bool sane = sample.Count == 20 && sample.All(t => t.Sections.Length == sections && t.Sections.All(x => x > 0)) && sample[0].TotalDs > 50000 && sample[0].TotalDs < 400000 && sample[0].TotalDs <= sample[19].TotalDs;
            Console.WriteLine("  " + Common.Mark(sane) + " 計測するレースの中身: " + sample.Count + " 校 × " + sections + " 区間、1 位 " + sample[0].TotalDs / 10.0 + " 秒 〜 最下位 " + sample[sample.Count - 1].TotalDs / 10.0 + " 秒、乱数の鍵 " + sample.Count * sections + " 個");
            if (!sane) bad++;
            double race = SaveCmd.Median(rep * 3, () => RaceSimulator.Simulate(kind, teams, envs, rngFor));
            bad += Line("1 レース(第一戦 20 校 × " + sections + " 区間)", race, 1.0 * sections, "ms", "参照 Python 3〜4 ms");
            bad += Line("レースの 1 区間", race / sections, 1.0, "ms", "予算 1 ms 以内");

            // 4. 週の解決
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
                    Menu menu = YearPlanner.WeekMenu(master, yw, plan, YearPlanner.Races, true, out bool camp);
                    resolver.Resolve(runner, menu, FacilityLevels.AllTwo, yw / 4, rngs, 1, yw, camp: camp, safety: true, bondAvg: 200.0);
                    runner.Fatigue = Math.Min(100, runner.Fatigue);
                }
            };
            double week = SaveCmd.Median(rep * 5, oneYear) / 48.0 * 1000.0;       // μs
            double train40 = week * 40 * 48 / 1000.0;
            bad += Line("週の解決 1 件(1 件あたり)", week, 25.0, "μs", "参照 Python 約 25 μs(これを上限とする)");
            Console.WriteLine("       自校 1 年度の育成(40 人 × 48 週)= " + train40.ToString("F1") + " ms(参照 Python 約 50 ms)");

            // 5. 保存・読み込み
            WorldSim sw100 = WorldSim.Create(new KeyedRngFactory(Common.WorldSeed(4)), 15);
            PlayerAgent a100 = sw100.StartPlayer(Difficulty.Mid);
            for (int y = 1; y <= 100; y++) sw100.PlayYear(y, a100);
            double cap = SaveCmd.Median(rep, () => WorldSnapshot.Capture(sw100, a100, 101));
            bad += Line("状態の取り出し(世界 36 校 + 自校の抽象。100 年度)", cap, 30.0, "ms", "予算 30 ms(DTO への写し)");
            JsonObject synth = SyntheticSave.Build(100);
            JsonObject hdr = SaveCmd.Header(100);
            double enc = SaveCmd.Median(rep, () => Eksv.Encode(hdr, synth));
            byte[] bytes = Eksv.Encode(hdr, synth);
            double dec = SaveCmd.Median(rep, () => { JsonValue p; Eksv.Decode(bytes, out p); });
            Console.WriteLine("  ・ エンコード(100 年度の合成セーブ。直列化 + gzip + CRC): " + enc.ToString("F0") + " ms(自動セーブ。背景の書き込みなので時間の予算なし)");
            double encFast = SaveCmd.Median(rep, () => Eksv.Encode(hdr, synth, fast: true));
            bad += Line("エンコード(同 最速の圧縮 = 中断セーブ)", encFast, 200.0, "ms", "中断セーブは同期で 200 ms 以内");
            bad += Line("読み込み(伸長 + 解析)", dec, 500.0, "ms", "読み込み全体で 500 ms 以内");

            // 6. 割り当て(GC)
            GC.Collect();
            long gw = Alloc(20, oneYear) / (20 * 48);
            bad += Gc("週の解決", gw, GcBaselineWeek);
            long gr = Alloc(50, () => RaceSimulator.Simulate(kind, teams, envs, rngFor)) / 50;
            bad += Gc("1 レース(第一戦 20 校)", gr, GcBaselineRace);
            WorldSim gwld = WorldSim.Create(new KeyedRngFactory(Common.WorldSeed(5)), 15);
            PlayerAgent gag = gwld.StartPlayer(Difficulty.Mid);
            int gy = 0;
            long gyBytes = Alloc(10, () => gwld.PlayYear(++gy, gag)) / 10;
            bad += Gc("世界の 1 年度", gyBytes, GcBaselineYear);
            Console.WriteLine("  割り当て: 09 §14.2 の目標は『ホットパスで 0』。M0 は実測を基準値にして退行を止め、0 に近づける作業(プール・struct 化)は M1 の実機プロファイルのあと。");
            Console.WriteLine(bad == 0 ? "T5: 予算内" : "T5: 予算外 " + bad + " 件");
            return bad == 0 ? 0 : 1;
        }

        private static int Line(string what, double v, double budget, string unit, string note)
        {
            bool ok = v <= budget / Common.PhoneSlowdown;
            Console.WriteLine("  " + Common.Mark(ok) + " " + what + ": " + v.ToString("F2") + " " + unit + "(予算 " + budget.ToString("F0") + " " + unit + " → 余裕 ×" + (budget / Math.Max(v, 1e-9)).ToString("F0") + ")  " + note);
            return ok ? 0 : 1;
        }

        private static int Gc(string what, long bytes, long baseline)
        {
            bool ok = bytes <= baseline;
            Console.WriteLine("  " + Common.Mark(ok) + " 割り当て " + what + ": " + bytes.ToString("N0") + " bytes/回(基準値 " + baseline.ToString("N0") + ")");
            return ok ? 0 : 1;
        }

        /// <summary>a を n 回呼んだときの、このスレッドの割り当て量(bytes)。</summary>
        private static long Alloc(int n, Action a)
        {
            a();                                       // ならし(JIT・静的な表の初期化を除く)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < n; i++) a();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
