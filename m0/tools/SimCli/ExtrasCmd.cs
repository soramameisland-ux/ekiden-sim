using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.Domain.Game;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;

namespace Ekiden.SimCli
{
    /// <summary>L-70 ②: 新設の施設・主務を人が買ったときの、後半の強さ(05 §4.4)。同じ世界・同じシードで『買わない(おまかせ)』『推奨の強化のあとに買う』『先に買う』を並べる。
    /// 画面の操作と同じ公開の命令(<see cref="GameSession.UpgradeFacility"/>)だけを使う。</summary>
    internal static class ExtrasCmd
    {
        private enum Policy { None, Last, First }

        /// <summary>使い方: extras [Elite|Mid|Low|Club|all] [シード数 60] [年度数 45]</summary>
        public static int Run(string[] args)
        {
            string which = args.Length > 0 ? args[0] : "all";
            int n = args.Length > 1 ? int.Parse(args[1]) : 60;
            int years = args.Length > 2 ? int.Parse(args[2]) : 45;
            Difficulty[] starts = which == "all" ? new[] { Difficulty.Elite, Difficulty.Mid, Difficulty.Low, Difficulty.Club } : new[] { (Difficulty)Enum.Parse(typeof(Difficulty), which) };
            foreach (Difficulty d in starts)
            {
                var sw = Stopwatch.StartNew();
                Row[][] rows = new Row[3][];
                foreach (Policy p in new[] { Policy.None, Policy.Last, Policy.First })
                {
                    var r = new Row[n];
                    Policy pp = p;
                    Parallel.For(0, n, i => r[i] = Play(Common.WorldSeed(i + 1), years, d, pp));
                    rows[(int)p] = r;
                }
                Console.WriteLine("== 開始校 " + d + "(" + n + " シード × " + years + " 年度。所要 " + sw.Elapsed.TotalSeconds.ToString("F0") + " 秒)");
                Console.WriteLine("                         買わない    推奨のあと    先に買う");
                Line("優勝率(25 年度〜)", rows, x => x.Win, "F3");
                Line("三冠率(25 年度〜)", rows, x => x.Triple, "F3");
                Line("クリア年度(平均)", rows, x => x.Clear, "F2");
                Line("戦力 P(25 年度〜)", rows, x => x.P, "F2");
                Line("45 年度末の資金", rows, x => x.Funds, "F0");
                Line("新設に使った累計(万円)", rows, x => x.Spent, "F0");
                Line("買った新設の Lv 合計", rows, x => x.ExtraLv, "F1");
                Line("ランク 7 に届いた年度", rows, x => x.Rank7Year, "F1");
            }
            return 0;
        }

        private static void Line(string label, Row[][] rows, Func<Row, double> f, string fmt)
        {
            var m = new double[3];
            for (int k = 0; k < 3; k++) m[k] = rows[k].Average(f);
            Console.WriteLine("  " + label.PadRight(22) + m[0].ToString(fmt).PadLeft(8) + m[1].ToString(fmt).PadLeft(12) + m[2].ToString(fmt).PadLeft(12) + "     差(先に − 買わない) " + (m[2] - m[0]).ToString("+0.000;-0.000;0.000"));
        }

        private sealed class Row
        {
            public double Win, Triple, Clear, P, Funds, Spent, ExtraLv, Rank7Year;
        }

        private static Row Play(ulong seed, int years, Difficulty d, Policy policy)
        {
            GameSession gs = GameSession.New(seed, d);
            var flow = new GameFlow(gs, autoUpgrade: false, autoTickets: true);
            gs.State.Settings.EventAuto = true;
            int done = 0;
            double spent = 0;
            int rank7 = years + 1;
            while (done < years)
            {
                Stop s = flow.Advance();
                if (s.Kind == StopKind.YearEndSettle)
                {
                    if (rank7 > years && gs.School.Rank() >= WorldK.NewMinRank) rank7 = gs.Year;
                    if (policy == Policy.First) spent += BuyExtras(gs);
                    gs.UpgradeRecommended();
                    if (policy == Policy.Last) spent += BuyExtras(gs);
                }
                else if (s.Kind == StopKind.YearEndTurnover) done++;
            }
            SeedResult r = Acceptance.Summarize(gs.Stats, years, Acceptance.FirstYearForRates);
            FacilityLevels f = gs.State.Fac;
            return new Row { Win = r.WinRate, Triple = r.TripleRate, Clear = r.Clear, P = r.P, Funds = gs.State.Funds, Spent = spent, ExtraLv = f.Altitude + f.Mental + f.Camp + f.Manager, Rank7Year = rank7 };
        }

        /// <summary>予備費(<see cref="WorldK.Reserve"/>)を残して、レベルの低い新設から 1 つずつ買う(同じなら高地 → メンタル → 合宿所 → 主務)。買えなくなったら終わり。</summary>
        private static double BuyExtras(GameSession gs)
        {
            double spent = 0;
            while (true)
            {
                string pick = null;
                foreach (string k in FacilityPolicy.Extras)
                    if (pick == null || FacilityPolicy.Level(gs.State.Fac, k) < FacilityPolicy.Level(gs.State.Fac, pick)) pick = k;
                int lv = FacilityPolicy.Level(gs.State.Fac, pick);
                double cost = WorldMath.UpgradeCost(lv);
                if (gs.State.Funds - cost < WorldK.Reserve) break;
                if (!gs.UpgradeFacility(pick).Ok) break;
                spent += cost;
            }
            return spent;
        }
    }
}
