using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.Domain.World;
using Ekiden.Persistence.Snapshot;

namespace Ekiden.SimCli
{
    /// <summary>T3 決定性(09 §16.1)。最終の状態(保存の正規形 JSON)の SHA-256 を比べる(バイト一致)。
    ///  ① 同じ master_seed と入力で 2 回 ② 任意の年度で『保存 → 読み込み → 続行』と『続行のみ』(JSON の文字列経由・EKSV コンテナ経由・毎年度)
    ///  ③ 並列に回しても世界ごとの結果は同じ。 中断 → 再開(§4.6)は Persistence.Tests の SuspendReplayTests。
    /// 端末・ビルドをまたいだビット一致は求めない(§8.4)。同じビルドの中の一致だけを見る。出した SHA-256 は記録用。</summary>
    internal static class DeterminismCmd
    {
        /// <summary>実体の自校(GameSession)の T3。年度の途中(予選会・大会の前後を含む週の境目)でも『保存 → 読み込み → 続行』が『続行のみ』とバイト一致する。</summary>
        private static int RunGame(int seeds, int years)
        {
            var sw = Stopwatch.StartNew();
            int weeks = years * 48;
            var starts = new[] { Difficulty.Mid, Difficulty.Low, Difficulty.Elite, Difficulty.Club };
            Console.WriteLine("== T3 決定性(実体の自校。" + seeds + " シード × " + years + " 年度 × 開始校 " + starts.Length + " 通り。年度の途中でも保存 → 読み込み)");
            var jobs = new List<(int seed, Difficulty d)>();
            for (int s = 1; s <= seeds; s++) foreach (Difficulty d in starts) jobs.Add((s, d));
            var par = new string[jobs.Count];
            Parallel.For(0, jobs.Count, i => par[i] = Determinism.RunGame(Common.WorldSeed(jobs[i].seed), jobs[i].d, weeks));
            int bad = 0;
            for (int i = 0; i < jobs.Count; i++)
            {
                ulong seed = Common.WorldSeed(jobs[i].seed);
                Difficulty d = jobs[i].d;
                string first = Determinism.RunGame(seed, d, weeks);
                var f = new List<string>();
                if (Determinism.RunGame(seed, d, weeks) != first) f.Add("① 2 回目");
                if (par[i] != first) f.Add("③ 並列");
                if (Determinism.RunGame(seed, d, weeks, new HashSet<int> { 1, 21, 26, 29, 36, 47, 48, weeks / 2 + 7, weeks - 1 }, Determinism.Via.JsonText) != first) f.Add("② JSON 経由");
                if (Determinism.RunGame(seed, d, weeks, new HashSet<int> { 20, 37, 96 }, Determinism.Via.Container) != first) f.Add("② コンテナ経由");
                if (jobs[i].seed == 1)
                {
                    int w = 62;
                    if (Determinism.RunGame(seed, d, w, new HashSet<int>(Enumerable.Range(1, w - 1))) != Determinism.RunGame(seed, d, w)) f.Add("② 毎週");
                }
                Console.WriteLine("  " + Common.Mark(f.Count == 0) + " シード " + jobs[i].seed + " 開始校 " + d.ToString().PadRight(5) + " 最終のセーブ " + (first.Length / 1024) + " KB  SHA-256 " + Determinism.Sha256Hex(first).Substring(0, 16) + "…" + (f.Count == 0 ? "" : "  ×: " + string.Join("・", f)));
                if (f.Count > 0) bad++;
            }
            Console.WriteLine((bad == 0 ? "T3(実体の自校): バイト一致" : "T3(実体の自校): 不一致 " + bad + " 件") + "  所要 " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒");
            return bad;
        }

        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            int seeds = Common.IntArg(pos, 0, 4);
            int years = Common.IntArg(pos, 1, 45);
            int bad = 0;
            var sw = Stopwatch.StartNew();
            var starts = new Difficulty?[] { null, Difficulty.Mid, Difficulty.Low, Difficulty.Elite };
            Console.WriteLine("== T3 決定性(" + seeds + " シード × " + years + " 年度 × 開始校 " + starts.Length + " 通り)");
            var jobs = new List<(int seed, Difficulty? start)>();
            for (int s = 1; s <= seeds; s++) foreach (Difficulty? d in starts) jobs.Add((s, d));
            var lines = new string[jobs.Count];
            var fails = new int[jobs.Count];
            // ③ 並列の結果(世界ごとのインスタンス)を先に取る
            var par = new string[jobs.Count];
            Parallel.For(0, jobs.Count, i => par[i] = Determinism.Run(Common.WorldSeed(jobs[i].seed), jobs[i].start, years, reloadAt: null));
            for (int i = 0; i < jobs.Count; i++)
            {
                ulong seed = Common.WorldSeed(jobs[i].seed);
                Difficulty? d = jobs[i].start;
                string first = Determinism.Run(seed, d, years, reloadAt: null);
                var f = new List<string>();
                if (Determinism.Run(seed, d, years, reloadAt: null) != first) f.Add("① 2 回目");
                if (par[i] != first) f.Add("③ 並列");
                int mid = Math.Max(1, years / 2);
                if (Determinism.Run(seed, d, years, reloadAt: new HashSet<int> { 1, mid, years - 1 }, via: Determinism.Via.JsonText) != first) f.Add("② JSON 経由");
                if (Determinism.Run(seed, d, years, reloadAt: new HashSet<int> { 3, mid }, via: Determinism.Via.Container) != first) f.Add("② コンテナ経由");
                if (jobs[i].seed == 1 && Determinism.Run(seed, d, years, reloadAt: new HashSet<int>(Enumerable.Range(1, years)), via: Determinism.Via.JsonText) != first) f.Add("② 毎年度");
                lines[i] = "  " + Common.Mark(f.Count == 0) + " シード " + jobs[i].seed + " 開始校 " + (d.HasValue ? d.Value.ToString().PadRight(5) : "なし ") + " 最終のセーブ " + (first.Length / 1024) + " KB  SHA-256 " + Determinism.Sha256Hex(first).Substring(0, 16) + "…" + (f.Count == 0 ? "" : "  ×: " + string.Join("・", f));
                fails[i] = f.Count;
            }
            foreach (string l in lines) Console.WriteLine(l);
            bad = fails.Count(x => x > 0);
            Console.WriteLine((bad == 0 ? "T3: バイト一致(①②③)" : "T3: 不一致 " + bad + " 件") + "  所要 " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒");
            bad += RunGame(Math.Min(seeds, 2), Math.Min(years, 6));
            return bad == 0 ? 0 : 1;
        }
    }
}
