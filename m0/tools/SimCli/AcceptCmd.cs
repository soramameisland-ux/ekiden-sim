using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.Domain.World;

namespace Ekiden.SimCli
{
    /// <summary>統計の受け入れ(09 §16.3。T4)。開始校ごとに 120 シード × 45 年度。
    /// 強豪県の +3(02 §6)は参照モデルにないので(L-44)、2 本立てにする:
    ///  T4a 等価性: +3 を外した世界(参照モデルと同じ構成)が 4 つの指標とも帯の内側(実装の取りこぼしの検出)
    ///  T4b 仕様の世界: +3 を入れた世界で、優勝率・三冠率・クリア年度は帯の内側、戦力 P は T4a との差(同じシードの対)が既知の範囲。</summary>
    internal static class AcceptCmd
    {
        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            string which = pos.Count > 0 ? pos[0] : "all";
            int n = Common.IntArg(pos, 1, 120);
            int years = Common.IntArg(pos, 2, 45);
            bool equiv = !Common.Flag(args, "no-equiv");
            bool real = Common.Flag(args, "real");
            var starts = which == "all" ? new[] { Difficulty.Elite, Difficulty.Mid, Difficulty.Low } : new[] { (Difficulty)Enum.Parse(typeof(Difficulty), which) };
            int bad = 0;
            if (real) return RunReal(starts, n, years, Common.Flag(args, "skilled"));
            foreach (Difficulty d in starts)
            {
                Acceptance.Band b = Acceptance.Bands[d];
                var sw = Stopwatch.StartNew();
                Summary spec = Many(d, n, years, null);
                double tSpec = sw.Elapsed.TotalSeconds;
                Summary noBonus = null;
                double tNo = 0;
                if (equiv)
                {
                    sw.Restart();
                    noBonus = Many(d, n, years, 0.0);
                    tNo = sw.Elapsed.TotalSeconds;
                }
                bool okSpec, okEq = true;
                if (equiv)
                {
                    okEq = Acceptance.EquivalentToReference(b, noBonus.Mean);
                    okSpec = Acceptance.SpecWorldAccepted(b, spec.Mean, noBonus.Mean);
                }
                else
                {
                    okSpec = Acceptance.WinInBand(b, spec.Mean) && Acceptance.TripleInBand(b, spec.Mean) && Acceptance.ClearInBand(b, spec.Mean)
                             && spec.Mean.P >= b.P - b.PTol && spec.Mean.P <= b.P + b.PTol + Acceptance.L44_P_SHIFT_MAX;
                }
                if (!okEq || !okSpec) bad++;
                Console.WriteLine("== 開始校 " + d + "(" + n + " シード × " + years + " 年度。所要 " + tSpec.ToString("F1") + (equiv ? " + " + tNo.ToString("F1") : "") + " 秒)");
                if (equiv) Print("T4a 等価性(強豪県の +3 を外す = 参照モデルと同じ構成)", b, noBonus, okEq, null);
                Print("T4b 仕様の世界(+3 あり)", b, spec, okSpec, equiv ? noBonus : null);
            }
            Console.WriteLine(bad == 0 ? "T4: すべて帯の内側" : "T4: 帯の外 " + bad + " 件");
            return bad == 0 ? 0 : 1;
        }

        /// <summary>実体の選手を持つ自校(GameSession)の指標を、抽象の自校の帯(参照)と並べて出す。+3 ありの世界で、抽象の自校(同じシード)も回して比べる。</summary>
        private static int RunReal(Difficulty[] starts, int n, int years, bool skilled = false)
        {
            if (skilled) return RunSkilled(starts, n, years);
            int bad = 0;
            foreach (Difficulty d in starts)
            {
                Acceptance.Band b = Acceptance.Bands[d];
                var sw = Stopwatch.StartNew();
                var real = new SeedResult[n];
                Parallel.For(0, n, i => real[i] = Acceptance.RunSeedReal(Common.WorldSeed(i + 1), years, d, Acceptance.FirstYearForRates, null));
                double tReal = sw.Elapsed.TotalSeconds;
                SeedResult r = Acceptance.Mean(real);
                sw.Restart();
                Summary abs = Many(d, n, years, null);
                var sorted = real.Select(x => x.Clear).OrderBy(x => x).ToList();
                Console.WriteLine("== 開始校 " + d + "(" + n + " シード × " + years + " 年度。実体 " + tReal.ToString("F1") + " 秒 / 抽象 " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒)");
                Console.WriteLine("              実体      抽象(同じ世界)   参照の帯");
                Console.WriteLine("  優勝率     " + r.WinRate.ToString("F3") + "     " + abs.Mean.WinRate.ToString("F3") + "            " + b.Win.ToString("F3") + " ± " + b.WinTol.ToString("F3") + "  " + Common.Mark(Acceptance.WinInBand(b, r)));
                Console.WriteLine("  三冠率     " + r.TripleRate.ToString("F3") + "     " + abs.Mean.TripleRate.ToString("F3") + "            " + b.Triple.ToString("F3") + " ± " + b.TripleTol.ToString("F3") + "  " + Common.Mark(Acceptance.TripleInBand(b, r)));
                Console.WriteLine("  クリア年度 " + r.Clear.ToString("F2") + "    " + abs.Mean.Clear.ToString("F2") + "           " + b.Clear.ToString("F2") + " ± " + b.ClearTol.ToString("F2") + "  " + Common.Mark(Acceptance.ClearInBand(b, r)) + "  (実体の中央値 " + sorted[sorted.Count / 2] + ")");
                Console.WriteLine("  戦力 P     " + r.P.ToString("F1") + "     " + abs.Mean.P.ToString("F1") + "            " + b.P.ToString("F1") + " ± " + b.PTol.ToString("F1") + "  " + Common.Mark(Acceptance.PInBand(b, r)) + "  (+3 を許す上限 " + (b.P + b.PTol + Acceptance.L44_P_SHIFT_MAX).ToString("F1") + ")");
                bool ok = Acceptance.RealAccepted(b, r);
                if (!ok) bad++;
                Console.WriteLine("  " + Common.Mark(ok) + " T4c 実体の自校: 優勝率・三冠率が帯の内側 / クリア年度が帯 ± " + Acceptance.REAL_CLEAR_SLACK.ToString("F1") + " 年度の内側 / 戦力 P が +3 を許した帯の内側");
            }
            Console.WriteLine(bad == 0 ? "T4c: すべて許容の内側" : "T4c: 許容の外 " + bad + " 件");
            return bad == 0 ? 0 : 1;
        }

        /// <summary>上級者の方針(当日変更と指示を最適化)と、おまかせを、同じ世界・同じシードで並べる。人が操作すれば改善する、という前提の確認。</summary>
        private static int RunSkilled(Difficulty[] starts, int n, int years)
        {
            foreach (Difficulty d in starts)
            {
                var sw = Stopwatch.StartNew();
                var auto = new SeedResult[n];
                var skilled = new SeedResult[n];
                Parallel.For(0, n, i =>
                {
                    auto[i] = Acceptance.RunSeedReal(Common.WorldSeed(i + 1), years, d);
                    skilled[i] = Acceptance.RunSeedReal(Common.WorldSeed(i + 1), years, d, Acceptance.FirstYearForRates, null, skilled: true);
                });
                SeedResult a = Acceptance.Mean(auto), s = Acceptance.Mean(skilled);
                Console.WriteLine("== 開始校 " + d + "(" + n + " シード × " + years + " 年度。所要 " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒)");
                Console.WriteLine("              おまかせ    上級(当日変更・指示)  差");
                Console.WriteLine("  優勝率     " + a.WinRate.ToString("F3") + "      " + s.WinRate.ToString("F3") + "              " + (s.WinRate - a.WinRate).ToString("+0.000;-0.000"));
                Console.WriteLine("  三冠率     " + a.TripleRate.ToString("F3") + "      " + s.TripleRate.ToString("F3") + "              " + (s.TripleRate - a.TripleRate).ToString("+0.000;-0.000"));
                Console.WriteLine("  クリア年度 " + a.Clear.ToString("F2") + "     " + s.Clear.ToString("F2") + "             " + (s.Clear - a.Clear).ToString("+0.00;-0.00"));
                Console.WriteLine("  戦力 P     " + a.P.ToString("F1") + "      " + s.P.ToString("F1") + "              " + (s.P - a.P).ToString("+0.0;-0.0"));
            }
            return 0;
        }

        private sealed class Summary
        {
            public SeedResult Mean;
            public double Median, NotCleared;
        }

        private static Summary Many(Difficulty d, int n, int years, double? bonus)
        {
            var res = new SeedResult[n];
            Parallel.For(0, n, i => res[i] = Acceptance.RunSeed(Common.WorldSeed(i + 1), years, d, Acceptance.FirstYearForRates, bonus));
            SeedResult m = Acceptance.Mean(res);
            var sorted = res.Select(r => r.Clear).OrderBy(x => x).ToList();
            return new Summary { Mean = m, Median = sorted[sorted.Count / 2], NotCleared = (double)sorted.Count(x => x > years) / n };
        }

        private static void Print(string title, Acceptance.Band b, Summary sum, bool ok, Summary pairSum)
        {
            SeedResult m = sum.Mean, pair = pairSum == null ? null : pairSum.Mean;
            Console.WriteLine("  " + Common.Mark(ok) + " " + title);
            Console.WriteLine("     優勝率   " + m.WinRate.ToString("F3") + "  (参照 " + b.Win.ToString("F3") + " ± " + b.WinTol.ToString("F3") + ")  " + Common.Mark(Acceptance.WinInBand(b, m)));
            Console.WriteLine("     三冠率   " + m.TripleRate.ToString("F3") + "  (参照 " + b.Triple.ToString("F3") + " ± " + b.TripleTol.ToString("F3") + ")  " + Common.Mark(Acceptance.TripleInBand(b, m)));
            Console.WriteLine("     クリア年度 " + m.Clear.ToString("F2") + "  (参照 " + b.Clear.ToString("F2") + " ± " + b.ClearTol.ToString("F2") + ")  " + Common.Mark(Acceptance.ClearInBand(b, m)) + "  中央値 " + sum.Median + "、未クリアの割合 " + sum.NotCleared.ToString("F3"));
            if (pair == null) Console.WriteLine("     戦力 P(25 年度〜) " + m.P.ToString("F1") + "  (参照 " + b.P.ToString("F1") + " ± " + b.PTol.ToString("F1") + ")  " + Common.Mark(Acceptance.PInBand(b, m)));
            else
            {
                double shift = m.P - pair.P;
                Console.WriteLine("     戦力 P(25 年度〜) " + m.P.ToString("F1") + "  (+3 を外した世界との差 " + shift.ToString("+0.00;-0.00") + "。許す範囲 " + Acceptance.L44_P_SHIFT_MIN.ToString("+0.0;-0.0") + "〜" + Acceptance.L44_P_SHIFT_MAX.ToString("+0.0;-0.0") + ")  " + Common.Mark(shift >= Acceptance.L44_P_SHIFT_MIN && shift <= Acceptance.L44_P_SHIFT_MAX));
            }
        }
    }
}
