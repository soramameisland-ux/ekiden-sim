using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;

namespace Ekiden.SimCli
{
    /// <summary>
    /// 区間記録の検算(調べもの用)。『実在の区間記録(秒)を各大会・各区間の基準に置いたら、ゲームの中で更新されるか。誰が更新するか』を測る。
    /// 世界の走者の最速タイム(<see cref="RaceHistory.Course"/>)が、実在の記録より速くなる最初の年度と、その記録を出したのが自校の走者(RunnerId &gt; 0)か他校の走者(0)かを数える。
    /// 使い方: records world|Elite|Mid|Low [シード数 40] [年度 45]。world = 自校なし(36 校すべて抽象)。
    /// <c>world</c> は予熱の記録のまま。開始校ありは <c>GameSession.New</c> が年度 1 の前に初期の区間記録(<see cref="RecordSeeds"/>。L-68)へ置き換えるので、『予熱後の最速』は初期値になる。
    /// <c>--own</c> = 記録がどれだけ持つか・誰が更新するか。<c>--reset</c> = world でも年度 1 の前に実在の水準へ置き換えた場合(開始校ありは既に置き換わっている)。
    /// 実在の記録の数値は、このファイルの外には持ち込まない(仕様書・データには入れない = CLAUDE.md の『実在の名前は使わない』のうち、数値だけの調べもの)。
    /// </summary>
    internal static class RecordsCmd
    {
        // 単位 = 0.1 秒(ds)。出典 = 2026 年時点の公表値(調べもの。資料により数秒の差がある)。
        private static readonly int[][] Real =
        {
            new[] { 13500, 9270, 13760, 10300, 10550, 16970 },                                               // 第一戦型(6 区間)
            new[] { 16180, 18610, 19660, 19830, 21010, 22070, 29710, 33320 },                                 // 第二戦型(8 区間)
            new[] { 36280, 39090, 35650, 36000, 40360, 34070, 36430, 38250, 40350, 40700 },                    // 第三戦型(10 区間)
        };

        private sealed class SeedRec
        {
            public int[][] First = { new int[6], new int[8], new int[10] };      // 実在の記録を初めて上回った年度(0 = 予熱の中。int.MaxValue = 期間内になし)
            public bool[][] ByPlayer = { new bool[6], new bool[8], new bool[10] };
            public int[][] Final = { new int[6], new int[8], new int[10] };
            public int[][] Burn = { new int[6], new int[8], new int[10] };
            // 予熱の記録を初期値にしたとき(--own): 年度ごとの更新の回数・初回の年度・自校の走者による更新の回数・期末の保持者が自校の走者か
            public int[][] Changes = { new int[6], new int[8], new int[10] };
            public int[][] FirstChange = { new int[6], new int[8], new int[10] };
            public int[][] PlayerChanges = { new int[6], new int[8], new int[10] };
            public bool[][] HeldByPlayer = { new bool[6], new bool[8], new bool[10] };
            public int[] TeamBurn = new int[3];                                   // 予熱後の大会記録(総合。ds)
            public bool[][] FirstByPlayer = { new bool[6], new bool[8], new bool[10] };
        }

        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            string mode = pos.Count > 0 ? pos[0] : "world";
            int n = Common.IntArg(pos, 1, 40);
            int years = Common.IntArg(pos, 2, 45);
            var res = new SeedRec[n];
            bool reset = Common.Flag(args, "reset");
            Parallel.For(0, n, i => res[i] = One(mode, Common.WorldSeed(i + 1), years, reset));
            if (Common.Flag(args, "own") || reset) return Own(res, mode, n, years, reset);
            Console.WriteLine("== 区間記録の検算  開始 = " + mode + "  シード " + n + "  " + years + " 年度(予熱 15 年度の後から)");
            Console.WriteLine("   実在の記録(秒)より、ゲーム内の最速が速くなった割合。『他校』= 記録を出したのが他校の走者(RunnerId 0)。単位は秒。");
            string[] names = { "第一戦型", "第二戦型", "第三戦型" };
            for (int k = 0; k < 3; k++)
            {
                IReadOnlyList<SectionDef> course = RaceTables.Course((RaceKind)k);
                Console.WriteLine("-- " + names[k]);
                Console.WriteLine("  区間  距離   実在   基準P  予熱後の最速(中央値)  期間末の最速(中央値)  期間内に更新  予熱で既に更新  更新者=他校  初回更新の年度(中央値。0=予熱)");
                for (int si = 0; si < course.Count; si++)
                {
                    double d = course[si].Distance;
                    double real = Real[k][si] / 10.0;
                    double impliedP = 50.0 + (d * Pace50.Of(d) * course[si].Terrain - real) / (d * RaceConstants.K_SLOPE);
                    var burn = res.Select(r => r.Burn[k][si] / 10.0).OrderBy(x => x).ToArray();
                    var fin = res.Select(r => r.Final[k][si] / 10.0).OrderBy(x => x).ToArray();
                    int broke = res.Count(r => r.First[k][si] != int.MaxValue);
                    int atStart = res.Count(r => r.First[k][si] == 0);
                    var firsts = res.Select(r => r.First[k][si]).Where(x => x != int.MaxValue).OrderBy(x => x).ToArray();
                    int byOther = res.Count(r => r.First[k][si] != int.MaxValue && !r.ByPlayer[k][si]);
                    Console.WriteLine("  " + (si + 1).ToString().PadLeft(2) + "  " + d.ToString("0.0").PadLeft(5) + " " + real.ToString("0.0").PadLeft(7) + "  " + impliedP.ToString("0.0").PadLeft(5)
                        + "  " + burn[n / 2].ToString("0.0").PadLeft(10) + " (" + Gap(burn[n / 2], real) + ")"
                        + "  " + fin[n / 2].ToString("0.0").PadLeft(10) + " (" + Gap(fin[n / 2], real) + ")"
                        + "  " + Pct(broke, n).PadLeft(6) + "  " + Pct(atStart, n).PadLeft(6) + "  " + (broke == 0 ? "-" : Pct(byOther, broke)).PadLeft(6) + "  " + (broke == 0 ? "-" : firsts[firsts.Length / 2].ToString()).PadLeft(4));
                }
            }
            return 0;
        }

        /// <summary>予熱で自然にできた記録を初期値にする現行の方式で、記録がどれだけ長く持つか・誰が更新するか(--own)。</summary>
        private static int Own(SeedRec[] res, string mode, int n, int years, bool reset)
        {
            Console.WriteLine("== " + (reset ? "年度 1 の前に実在の記録へリセット" : "現行方式(予熱 15 年度の記録が初期値)") + "  開始 = " + mode + "  シード " + n + "  " + years + " 年度");
            string[] names = { "第一戦型", "第二戦型", "第三戦型" };
            int[] cnt = { 6, 8, 10 };
            for (int k = 0; k < 3; k++)
            {
                double ch = 0, pch = 0; int never = 0, held = 0, total = 0, byYear10 = 0, byYear20 = 0;
                var firsts = new List<int>();
                foreach (SeedRec r in res)
                    for (int si = 0; si < cnt[k]; si++)
                    {
                        total++; ch += r.Changes[k][si]; pch += r.PlayerChanges[k][si];
                        if (r.Changes[k][si] == 0) never++; else { firsts.Add(r.FirstChange[k][si]); if (r.FirstChange[k][si] <= 10) byYear10++; if (r.FirstChange[k][si] <= 20) byYear20++; }
                        if (r.HeldByPlayer[k][si]) held++;
                    }
                firsts.Sort();
                Console.WriteLine("-- " + names[k] + "  1 区間あたりの更新回数 " + (ch / total).ToString("0.00") + "(うち自校の走者 " + (pch / total).ToString("0.00") + ")"
                    + "  初回の更新の中央値 " + (firsts.Count == 0 ? "-" : firsts[firsts.Count / 2].ToString()) + " 年度"
                    + "  10 年度以内に抜かれた " + Pct(byYear10, total) + "  20 年度以内 " + Pct(byYear20, total) + "  期間内に一度も抜かれない " + Pct(never, total)
                    + "  期末の保持者が自校の走者 " + Pct(held, total));
            }
            Console.WriteLine("   大会記録(総合)の予熱後の中央値: " + string.Join(" / ", Enumerable.Range(0, 3).Select(k => Hms(res.Select(r => r.TeamBurn[k]).OrderBy(x => x).ToArray()[n / 2]))));
            if (!reset) return 0;
            string[] rn = { "第一戦型", "第二戦型", "第三戦型" };
            for (int k = 0; k < 3; k++)
            {
                Console.WriteLine("-- " + rn[k] + "  区間  年度 1 で抜かれる  3 年度以内  10 年度以内  45 年度間そのまま  初回の更新者=自校  期末の保持者=自校");
                for (int si = 0; si < cnt[k]; si++)
                {
                    int y1 = res.Count(r => r.FirstChange[k][si] == 1), y3 = res.Count(r => r.FirstChange[k][si] >= 1 && r.FirstChange[k][si] <= 3), y10 = res.Count(r => r.FirstChange[k][si] >= 1 && r.FirstChange[k][si] <= 10);
                    int stay = res.Count(r => r.Changes[k][si] == 0), changed = n - stay;
                    int firstPl = res.Count(r => r.Changes[k][si] > 0 && r.FirstByPlayer[k][si]);
                    Console.WriteLine("   " + (si + 1).ToString().PadLeft(2) + "  " + Pct(y1, n).PadLeft(6) + "  " + Pct(y3, n).PadLeft(6) + "  " + Pct(y10, n).PadLeft(6) + "  " + Pct(stay, n).PadLeft(6) + "  " + (changed == 0 ? "-" : Pct(firstPl, changed)).PadLeft(6)
                        + "  " + Pct(res.Count(r => r.HeldByPlayer[k][si]), n).PadLeft(6));
                }
            }
            return 0;
        }

        private static string Hms(int ds) { int sec = (ds + 5) / 10; return (sec / 3600) + ":" + (sec / 60 % 60).ToString("00") + ":" + (sec % 60).ToString("00"); }

        private static string Gap(double game, double real) { return ((game - real) >= 0 ? "+" : "") + (game - real).ToString("0"); }
        private static string Pct(int a, int b) { return (100.0 * a / b).ToString("0") + "%"; }

        private static SeedRec One(string mode, ulong seed, int years, bool reset)
        {
            var r = new SeedRec();
            for (int k = 0; k < 3; k++) for (int si = 0; si < r.First[k].Length; si++) r.First[k][si] = int.MaxValue;
            if (mode == "world")
            {
                WorldSim w = WorldSim.Create(new KeyedRngFactory(seed), 15);
                Check(r, w.History, 0, true);
                if (reset) Reset(r, w.History);
                for (int y = 1; y <= years; y++) { w.PlayYear(y); Check(r, w.History, y, false); }
            }
            else
            {
                var gs = GameSession.New(seed, (Difficulty)Enum.Parse(typeof(Difficulty), mode), null);
                Check(r, gs.World.History, 0, true);
                if (reset) Reset(r, gs.World.History);
                for (int y = 1; y <= years; y++) { gs.PlayYears(1); Check(r, gs.World.History, y, false); }
            }
            return r;
        }

        /// <summary>予熱の記録を実在の記録に置き換える(保持者 = 学校 -1・走者 0)。以後の更新は通常の規則。</summary>
        private static void Reset(SeedRec r, RaceHistory h)
        {
            for (int k = 0; k < 3; k++)
                for (int si = 0; si < Real[k].Length; si++)
                {
                    h.Course[k][si] = new CourseRecord { SchoolId = -1, RunnerId = 0, TimeDs = Real[k][si], Year = 0 };
                    r.Final[k][si] = Real[k][si];
                    r.HeldByPlayer[k][si] = false;
                }
        }

        private static void Check(SeedRec r, RaceHistory h, int year, bool burn)
        {
            for (int k = 0; k < 3; k++)
            {
                CourseRecord[] c = h.Course[k];
                if (burn && h.Team[k] != null) r.TeamBurn[k] = h.Team[k].TotalDs;
                if (c == null) continue;
                for (int si = 0; si < c.Length; si++)
                {
                    if (c[si] == null) continue;
                    if (burn) r.Burn[k][si] = c[si].TimeDs;
                    else if (c[si].TimeDs < r.Final[k][si])
                    {
                        r.Changes[k][si]++;
                        if (r.FirstChange[k][si] == 0) { r.FirstChange[k][si] = year; r.FirstByPlayer[k][si] = c[si].RunnerId > 0; }
                        if (c[si].RunnerId > 0) r.PlayerChanges[k][si]++;
                    }
                    r.HeldByPlayer[k][si] = c[si].RunnerId > 0;
                    r.Final[k][si] = c[si].TimeDs;
                    if (r.First[k][si] == int.MaxValue && c[si].TimeDs < Real[k][si]) { r.First[k][si] = year; r.ByPlayer[k][si] = c[si].RunnerId > 0; }
                }
            }
        }
    }
}
