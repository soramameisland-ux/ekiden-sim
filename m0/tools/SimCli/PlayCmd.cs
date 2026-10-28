using System;
using System.Diagnostics;
using System.Linq;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using Ekiden.Persistence.Snapshot;

namespace Ekiden.SimCli
{
    /// <summary>実体の選手を持つ自校(<see cref="GameSession"/>。方針『おまかせ』)を 1 回プレイして、年度ごとの経過を出す。</summary>
    internal static class PlayCmd
    {
        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            int seed = Common.IntArg(pos, 0, 1);
            Difficulty d = pos.Count > 1 ? (Difficulty)Enum.Parse(typeof(Difficulty), pos[1]) : Difficulty.Mid;
            int years = Common.IntArg(pos, 2, 10);
            var sw = Stopwatch.StartNew();
            GameSession gs = GameSession.New(Common.WorldSeed(seed), d);
            double tNew = sw.Elapsed.TotalSeconds;
            Console.WriteLine("== 実体の自校(" + d + "・シード " + seed + "・学校 " + gs.School.Id + "・" + gs.State.Runners.Count + " 人。世界の生成 " + tNew.ToString("F1") + " 秒)");
            Console.WriteLine("年度  P   戦力順位  第一 第二 第三  名声  資金  施設Lv  部員 卒業 退部  ランク  OB  券");
            for (int y = 1; y <= years; y++)
            {
                gs.PlayYears(1);
                PlayerYearStats st = gs.Stats.Last();
                var t = gs.LastTurnover;
                Console.WriteLine(y.ToString().PadLeft(3) + "  " + st.P.ToString("F1") + "  " + st.PowerRank.ToString().PadLeft(3) + "       "
                                  + Place(st.Place[0]) + " " + Place(st.Place[1]) + " " + Place(st.Place[2]) + "  "
                                  + st.Prestige.ToString("F0").PadLeft(4) + "  " + gs.State.Funds.ToString().PadLeft(5) + "  " + gs.School.Lv().ToString("F1") + "   "
                                  + gs.State.Runners.Count.ToString().PadLeft(3) + "  " + t.Graduates.Count.ToString().PadLeft(3) + "  " + t.Quit.Count.ToString().PadLeft(3) + "  "
                                  + gs.State.CoachRank.ToString().PadLeft(4) + "   " + gs.State.Obs.Count.ToString().PadLeft(3) + "  " + gs.State.Tickets.Stock);
            }
            var ssw = Stopwatch.StartNew();
            int kb = GameSnapshot.Capture(gs).ToJson().Length / 1024;
            Console.WriteLine("保存の形(JSON)" + kb + " KB(取り出しと文字列化 " + ssw.Elapsed.TotalMilliseconds.ToString("F0") + " ms)。自校の選手 " + gs.State.Runners.Count + " 人・OB " + gs.State.Obs.Count + " 人");
            Console.WriteLine("所要 " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒(1 年度あたり " + ((sw.Elapsed.TotalSeconds - tNew) / years).ToString("F2") + " 秒)");
            return 0;
        }

        private static string Place(int p) { return (p == 99 ? " -" : p.ToString().PadLeft(2)) + " "; }
    }
}
