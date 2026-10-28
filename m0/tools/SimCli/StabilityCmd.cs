using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;

namespace Ekiden.SimCli
{
    /// <summary>長期の安定(09 §16.3・§17.3): 方針『おまかせ』で 300 年度連続で回す。例外・不変条件の違反がなく、戦力 P と優勝率が頭打ちのままであること。
    /// 参照モデル(2 シード)は、年度 26〜100 / 101〜200 / 201〜300 で P 72.8 / 72.0 / 72.2 と 72.9 / 73.3 / 73.4、優勝率 0.56 / 0.53 / 0.54 と 0.66 / 0.71 / 0.71。</summary>
    internal static class StabilityCmd
    {
        private const double MaxPShift = 2.0;                                      // 1 シードの区間どうしの戦力 P の差(崩壊の検出。優勝率は 1 校の運の揺れが大きいので、シードをならして見る)
        private const double MaxPooledPShift = 0.5, MaxPooledWinShift = 0.06;     // シードをならした区間どうしの差
        private static bool Trace;

        private sealed class SeedRun
        {
            public int Seed;
            public double[] P, Win;                         // 3 つの区間: 26〜100 / 101〜200 / 201〜300(年度数が少なければ空)
            public List<string> Problems = new List<string>();
            public string Final;
            public double Seconds;
        }


        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            Trace = Common.Flag(args, "trace");
            int seeds = Common.IntArg(pos, 0, 2);
            int years = Common.IntArg(pos, 1, 300);
            Difficulty start = pos.Count > 2 ? (Difficulty)Enum.Parse(typeof(Difficulty), pos[2]) : Difficulty.Mid;
            var runs = new SeedRun[seeds];
            var sw = Stopwatch.StartNew();
            Parallel.For(0, seeds, i => runs[i] = One(i + 1, years, start));
            int bad = 0;
            Console.WriteLine("== 長期の安定(" + start + "・おまかせ・" + years + " 年度 × " + seeds + " シード。所要 " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒)");
            string[] names = { "26〜100", "101〜200", "201〜300" };
            foreach (SeedRun r in runs)
            {
                var cells = new List<string>();
                for (int k = 0; k < 3; k++) if (r.P[k] > 0) cells.Add(names[k] + " 年度: P " + r.P[k].ToString("F1") + " / 優勝率 " + r.Win[k].ToString("F2"));
                bool plateau = true;
                // 弱小は『なかなか上がらない尾』(クリアしない 5%。09 §16.3)があり、26〜100 年度の区間にまだ上昇が入る。頭打ちの比較は、弱小では 101 年度以降の区間どうしで行う
                for (int k = start == Difficulty.Low ? 2 : 1; k < 3; k++)
                    if (r.P[k] > 0 && (Math.Abs(r.P[k] - r.P[k - 1]) > MaxPShift || r.P[k] < 65.0 || r.P[k] > 80.0)) plateau = false;
                bool ok = plateau && r.Problems.Count == 0;
                if (!ok) bad++;
                Console.WriteLine("  " + Common.Mark(ok) + " シード " + r.Seed + "(" + r.Seconds.ToString("F1") + " 秒): " + string.Join(" | ", cells) + (plateau ? "" : "  ← 頭打ちでない(区間の差が P " + MaxPShift + " を超える、または P が 65〜80 の外)"));
                foreach (string p in r.Problems.Take(8)) Console.WriteLine("       違反: " + p);
            }
            // シードをならした区間の平均が、動かない(ずれ = 実装の漂い)。シード間の揺れに隠れないように、4 シード以上で見る
            if (seeds >= 4 && years >= 300)
            {
                double[] mp = new double[3], mw = new double[3];
                for (int k = 0; k < 3; k++) { mp[k] = runs.Average(x => x.P[k]); mw[k] = runs.Average(x => x.Win[k]); }
                bool pooled = Math.Abs(mp[2] - mp[1]) <= MaxPooledPShift && Math.Abs(mw[2] - mw[1]) <= MaxPooledWinShift && (start == Difficulty.Low || (Math.Abs(mp[1] - mp[0]) <= MaxPooledPShift && Math.Abs(mw[1] - mw[0]) <= MaxPooledWinShift));
                if (!pooled) bad++;
                Console.WriteLine("  " + Common.Mark(pooled) + " シードの平均: P " + mp[0].ToString("F2") + " / " + mp[1].ToString("F2") + " / " + mp[2].ToString("F2") + "、優勝率 " + mw[0].ToString("F3") + " / " + mw[1].ToString("F3") + " / " + mw[2].ToString("F3")
                                  + "(区間の差 P " + MaxPooledPShift + "・優勝率 " + MaxPooledWinShift + " 以内)");
            }
            Console.WriteLine(bad == 0 ? "安定: 例外なし・不変条件の違反なし・頭打ちを保つ" : "安定: 問題 " + bad + " 件");
            return bad == 0 ? 0 : 1;
        }

        private static SeedRun One(int seed, int years, Difficulty start)
        {
            var run = new SeedRun { Seed = seed, P = new double[3], Win = new double[3] };
            var sw = Stopwatch.StartNew();
            try
            {
                WorldSim w = WorldSim.Create(new KeyedRngFactory(Common.WorldSeed(seed)), 15);
                PlayerAgent agent = w.StartPlayer(start);
                var cnt = new int[3];
                for (int y = 1; y <= years; y++)
                {
                    w.PlayYear(y, agent);
                    foreach (string e in WorldInvariants.Check(w, agent)) run.Problems.Add("年度 " + y + " " + e);
                    if (y % 25 == 0)
                    {
                        foreach (string e in WorldSnapshot.NotQ6Closed(w, agent)) run.Problems.Add("年度 " + y + " Q6 で閉じていない " + e);
                        string once = WorldSnapshot.Capture(w, agent, y + 1).ToJson();
                        WorldSnapshot.Restored r = WorldSnapshot.Restore((JsonObject)JsonValue.Parse(once));
                        if (WorldSnapshot.Capture(r.World, r.Agent, r.NextYear).ToJson() != once) run.Problems.Add("年度 " + y + " 取り出し → 復元 → 取り出しが一致しない");
                    }
                    if (run.Problems.Count > 50) break;
                    if (Trace && (y <= 10 || y % 10 == 0) && y <= 120)
                    {
                        PlayerYearStats ts = agent.Stats[agent.Stats.Count - 1];
                        Console.WriteLine("    [seed " + seed + "] 年度 " + y.ToString().PadLeft(3) + ": P " + ts.P.ToString("F1") + " 戦力順位 " + ts.PowerRank + " 最強の他校 " + ts.Top1.ToString("F1") + " 名声 " + ts.Prestige.ToString("F0") + " 資金 " + ts.Funds.ToString("F0") + " 施設Lv " + ts.Lv.ToString("F1") + " 監督ランク " + ts.CoachRank + " 順位 " + string.Join("/", ts.Place));
                    }
                    int k = y >= 201 ? 2 : (y >= 101 ? 1 : (y >= 26 ? 0 : -1));
                    if (k >= 0)
                    {
                        PlayerYearStats st = agent.Stats[agent.Stats.Count - 1];
                        run.P[k] += st.P;
                        run.Win[k] += st.Wins.Count(x => x) / 3.0;
                        cnt[k]++;
                    }
                }
                for (int k = 0; k < 3; k++) if (cnt[k] > 0) { run.P[k] /= cnt[k]; run.Win[k] /= cnt[k]; }
                run.Final = WorldSnapshot.Capture(w, agent, years + 1).ToJson();
            }
            catch (Exception ex)
            {
                run.Problems.Add("例外 " + ex.GetType().Name + ": " + ex.Message);
            }
            run.Seconds = sw.Elapsed.TotalSeconds;
            return run;
        }
    }
}
