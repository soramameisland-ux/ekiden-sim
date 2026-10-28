using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;

namespace Ekiden.SimCli
{
    /// <summary>M0 の SimCli(09 §15.2)。段階ごとにコマンドを足す。</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "help";
            switch (cmd)
            {
                case "world": return World(args.Skip(1).ToArray());
                case "play": return PlayCmd.Run(args.Skip(1).ToArray());
                case "flow": return FlowCmd.Run(args.Skip(1).ToArray());
                case "accept": return AcceptCmd.Run(args.Skip(1).ToArray());
                case "stability": return StabilityCmd.Run(args.Skip(1).ToArray());
                case "determinism": return DeterminismCmd.Run(args.Skip(1).ToArray());
                case "bench": return BenchCmd.Run(args.Skip(1).ToArray());
                case "save": return SaveCmd.Run(args.Skip(1).ToArray());
                case "fuzz-save": return FuzzSaveCmd.Run(args.Skip(1).ToArray());
                case "sync-sim": return SyncSimCmd.Run(args.Skip(1).ToArray());
                case "vectors": return VectorsCmd.Run(args.Skip(1).ToArray());
                case "strings": return StringsCmd.Run(args.Skip(1).ToArray());
                case "help":
                default:
                    Console.WriteLine("SimCli コマンド:");
                    Console.WriteLine("  world [シード数 20] [年度数 80]   自校なしの世界の指標(05 §14.1)を出す");
                    Console.WriteLine("  play [シード 1] [Elite|Mid|Low|Club] [年度数 10]   実体の選手を持つ自校(『おまかせ』)を 1 回プレイして年度ごとの経過を出す");
                    Console.WriteLine("  flow [シード 1] [Elite|Mid|Low|Club] [年度数 2] [--events]   進行制御(08 §6)の停止の並びを出す(何も操作しない流れ = おまかせの確認つき)");
                    Console.WriteLine("  accept [Elite|Mid|Low|all] [シード数 120] [年度数 45] [--no-equiv]   統計の受け入れ(09 §16.3。T4。強豪県の +3 あり/なしの 2 本立て。L-44)");
                    Console.WriteLine("  stability [シード数 2] [年度数 300] [Elite|Mid|Low]   長期の安定(09 §17.3。例外・不変条件の違反なし、戦力の頭打ち)");
                    Console.WriteLine("  determinism [シード数 4] [年度数 45]   T3 決定性(同じ入力で 2 回・保存 → 読み込み → 続行・並列)のバイト一致");
                    Console.WriteLine("  bench [--quick]   性能の予算(09 §14.2。T5)");
                    Console.WriteLine("  save [年度数 100]   セーブの大きさ・保存と読み込みの時間(09 §5.4)");
                    Console.WriteLine("  fuzz-save [回数 20000]   コンテナのファズ・電源断・検証と修復(09 §4・§7)");
                    Console.WriteLine("  sync-sim [回数 400] [手数 300]   クラウド同期の乱数シミュレーション(09 §9.7)");
                    Console.WriteLine("  strings lint|chars|seed [Assets/Strings/ja.json] [フォントの文字のファイル]   文字列テーブル(09 §12.3)の書式の検査・使われている文字の集合とフォントにない文字・足りないキーを足す");
                    Console.WriteLine("  vectors   T1 の主なベクタの値を『名前 値』の行で出す(tools/compare_vectors.py が参照モデルと突き合わせる)");
                    return 0;
            }
        }

        /// <summary>自校なしの世界(05 §14.1)。名声順位ごとの戦力 P・優勝校の数・最多優勝校のシェア・上位 6 校の入れ替わり・第三戦の連続優勝。</summary>
        private static int World(string[] a)
        {
            int n = a.Length > 0 ? int.Parse(a[0]) : 20;
            int years = a.Length > 1 ? int.Parse(a[1]) : 80;
            var sw = Stopwatch.StartNew();
            var snap = new Dictionary<int, double[]>();
            foreach (int y in new[] { 1, 40, years }) snap[y] = new double[3];
            double distinct = 0, share = 0, turnover = 0, streakLen = 0;
            for (int seed = 1; seed <= n; seed++)
            {
                var rngs = new KeyedRngFactory((ulong)seed * 1000003UL + 17UL);
                WorldSim w = WorldSim.Create(rngs, 15);
                var wins = new Dictionary<int, int>();
                int prev = -1, repeats = 0;
                HashSet<int> top6First = null;
                for (int y = 1; y <= years; y++)
                {
                    YearResult r = w.PlayYear(y);
                    foreach (RaceKind k in new[] { RaceKind.Fall, RaceKind.National, RaceKind.NewYear })
                    {
                        wins.TryGetValue(r.Winner[k], out int c);
                        wins[r.Winner[k]] = c + 1;
                    }
                    if (r.Winner[RaceKind.NewYear] == prev) repeats++;
                    prev = r.Winner[RaceKind.NewYear];
                    if (snap.ContainsKey(y))
                    {
                        var order = w.Schools.OrderByDescending(s => s.Prestige).ToList();
                        double Mean(IEnumerable<School> ss) { return ss.Average(s => w.TeamPower(s)); }
                        snap[y][0] += Mean(order.Take(6)) / n; snap[y][1] += Mean(order.Skip(6).Take(12)) / n; snap[y][2] += Mean(order.Skip(18)) / n;
                        if (y == 1) top6First = new HashSet<int>(order.Take(6).Select(s => s.Id));
                        if (y == years) turnover += order.Take(6).Count(s => !top6First.Contains(s.Id)) / (double)n;
                    }
                }
                int total = wins.Values.Sum();
                distinct += wins.Count / (double)n;
                share += wins.Values.Max() / (double)total / n;
                streakLen += (double)years / (years - repeats) / n;
            }
            Console.WriteLine("== 自校なしの世界(" + years + " 年度 × " + n + " 回。C#)");
            foreach (int y in snap.Keys.OrderBy(k => k))
                Console.WriteLine("  " + y.ToString().PadLeft(3) + " 年度: 戦力P 上位6校 " + snap[y][0].ToString("F1") + " / 中位12校 " + snap[y][1].ToString("F1") + " / 下位18校 " + snap[y][2].ToString("F1"));
            Console.WriteLine("  優勝校の数 " + distinct.ToString("F1") + " 校 / 最多優勝校のシェア " + (share * 100).ToString("F0") + "% / 上位6校の入れ替わり " + turnover.ToString("F1") + " 校 / 第三戦の連続優勝(同じ校が続く年数の平均) " + streakLen.ToString("F2") + " 年");
            Console.WriteLine("  所要時間 " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒(1 回あたり " + (sw.Elapsed.TotalSeconds / n).ToString("F2") + " 秒)");
            return 0;
        }

    }
}
